using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the export package through the real pipeline (real API, real SQL Server, cookie sessions and CSRF): each of the three downloads is its own permission, a POST that needs the CSRF token, and is
/// refused to an anonymous caller (401) and to every role that does not hold the permission (403); the downloads are sent with no-store and nosniff, carry the export id and result, and the bytes match the hash in the
/// header and in the history; a bad folder-naming value is 400; an unknown patient is 404 and a merged-away patient 409; the readable record is an attachment with a content-security policy; an audit store that is
/// down means nothing is sent (500 export_failed) and the row is Failed; permissions are separate (one patient's package does not open the bulk export) and an administrator can grant one without a code change; and the
/// history shows a person only their own exports unless they hold the bulk permission, while the overview explains the package before it is started.
/// </summary>
public class PatientExportsApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _patient;
    private Session _admin = null!;
    private readonly Dictionary<string, Session> _others = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = await _s.PatientAsync("Ann", "Rivera");
        _admin = await _api.SessionAsync("Admin");
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager" }) _others[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private string PackageUrl(Guid? id = null) => $"/api/patient-exports/patients/{id ?? _patient}/package";
    private const string AllUrl = "/api/patient-exports/all-patients/package";
    private string RecordUrl(Guid? id = null) => $"/api/patient-exports/patients/{id ?? _patient}/readable-record";

    private static async Task<HttpResponseMessage> PostAsync(Session session, string url, object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (csrf) request.Headers.Add("X-CSRF-Token", session.Csrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await session.Client.SendAsync(request);
    }

    private static Dictionary<string, byte[]> Unzip(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }

    private async Task<List<DataExportHistory>> HistoryRowsAsync() { await using var db = _fixture.CreateContext(); return await db.DataExportHistory.AsNoTracking().OrderBy(h => h.RequestedAtUtc).ToListAsync(); }

    private async Task<int> AuditCountAsync(string eventType) { await using var db = _fixture.CreateContext(); return await db.AuditLogEntries.CountAsync(a => a.EventType == eventType); }

    [Fact]
    public async Task The_administrator_downloads_a_package_that_no_cache_may_keep_and_the_bytes_match_the_hash_and_the_history()
    {
        var response = await PostAsync(_admin, PackageUrl());
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(@"^alveara-patient-package-[0-9a-f]{32}\.zip$", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var exportId = Guid.Parse(response.Headers.GetValues("X-Export-Id").Single());
        Assert.Equal("Complete", response.Headers.GetValues("X-Export-Result").Single());
        Assert.Equal("0", response.Headers.GetValues("X-Export-Problems").Single());
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(sha, response.Headers.GetValues("X-Package-Sha256").Single());
        var row = Assert.Single(await HistoryRowsAsync());
        Assert.Equal((exportId, sha, bytes.Length, DataExportResult.Complete, _admin.UserId), (row.ExportId, row.Sha256, (int)row.SizeBytes, row.Result, row.RequestedByUserId));
        var manifest = JsonDocument.Parse(Unzip(bytes)["manifest.json"]).RootElement;
        Assert.Equal(("OnePatient", exportId), (manifest.GetProperty("scope").GetProperty("kind").GetString(), manifest.GetProperty("exportId").GetGuid()));
        Assert.Equal(1, await AuditCountAsync(AuditEventTypes.PatientDataPackageExported));
    }

    [Fact]
    public async Task The_request_may_leave_out_the_document_files_and_choose_the_folder_naming_and_a_bad_choice_is_400()
    {
        var response = await PostAsync(_admin, PackageUrl(), new { includeDocumentFiles = false, folderNaming = "NameAndId" });
        var manifest = JsonDocument.Parse(Unzip(await response.Content.ReadAsByteArrayAsync())["manifest.json"]).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(manifest.GetProperty("documentFilesIncluded").GetBoolean());
        Assert.Equal("NameAndId", manifest.GetProperty("folderNaming").GetString());
        Assert.False((await HistoryRowsAsync()).Single().IncludesDocumentFiles);

        var bad = await _admin.SendAsync(HttpMethod.Post, PackageUrl(), new { folderNaming = "names" });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_folder_naming"), (bad.Status, bad.Body.GetProperty("error").GetString()));
        var wrongType = await _admin.SendAsync(HttpMethod.Post, PackageUrl(), new { includeDocumentFiles = "yes" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.Status);
        Assert.Single(await HistoryRowsAsync());                                                                    // neither refusal left a row
    }

    [Fact]
    public async Task The_administrator_downloads_every_patients_package()
    {
        var response = await PostAsync(_admin, AllUrl, new { includeDocumentFiles = false });
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("alveara-all-patients-package-", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var entries = Unzip(bytes);
        var manifest = JsonDocument.Parse(entries["manifest.json"]).RootElement;
        Assert.Equal("AllPatients", manifest.GetProperty("scope").GetProperty("kind").GetString());
        Assert.True(entries.ContainsKey($"readable-records/{_patient:D}.html"));
        var row = Assert.Single(await HistoryRowsAsync());
        Assert.Equal((DataExportScope.AllPatients, (Guid?)null), (row.Scope, row.SubjectRecordId));
        Assert.Equal(1, await AuditCountAsync(AuditEventTypes.AllPatientDataPackageExported));
    }

    [Fact]
    public async Task The_readable_record_is_an_attachment_the_page_cannot_script_and_it_is_recorded_as_a_readable_record()
    {
        var response = await PostAsync(_admin, RecordUrl());
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(@"^alveara-readable-record-[0-9a-f]{32}\.html$", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("<h1>Ann Rivera</h1>", page);
        var row = Assert.Single(await HistoryRowsAsync());
        Assert.Equal((DataExportKind.ReadableRecord, _patient, DataExportResult.Complete), (row.Kind, row.SubjectRecordId, row.Result));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(page))), row.Sha256);
        Assert.Equal(1, await AuditCountAsync(AuditEventTypes.PatientReadableRecordExported));
    }

    [Fact]
    public async Task Every_download_and_both_reads_are_refused_to_an_anonymous_caller_and_to_every_role_without_the_permission()
    {
        using var anonymous = _api.Factory.CreateClient();
        foreach (var url in new[] { PackageUrl(), AllUrl, RecordUrl() })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(url, null)).StatusCode);
        foreach (var url in new[] { "/api/patient-exports/history", "/api/patient-exports/overview" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        foreach (var (role, session) in _others)
        {
            foreach (var url in new[] { PackageUrl(), AllUrl, RecordUrl() })
                Assert.True(HttpStatusCode.Forbidden == (await PostAsync(session, url)).StatusCode, $"{role} must not POST {url}");
            foreach (var url in new[] { "/api/patient-exports/history", "/api/patient-exports/overview" })
                Assert.True(HttpStatusCode.Forbidden == (await session.Client.GetAsync(url)).StatusCode, $"{role} must not read {url}");
        }

        Assert.Empty(await HistoryRowsAsync());                                                                    // nothing was exported, so nothing was recorded
    }

    [Fact]
    public async Task A_download_without_the_csrf_token_is_refused_and_nothing_is_recorded()
    {
        foreach (var url in new[] { PackageUrl(), AllUrl, RecordUrl() })
        {
            var refused = await _admin.SendAsync(HttpMethod.Post, url, csrf: false);
            Assert.Equal((HttpStatusCode.BadRequest, "csrf_token_invalid"), (refused.Status, refused.Body.GetProperty("error").GetString()));
        }

        Assert.Empty(await HistoryRowsAsync());
        var link = await _admin.Client.GetAsync(PackageUrl());                                                        // a plain link cannot start an export (the host answers an unknown GET with the front page)
        Assert.NotEqual("application/zip", link.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await HistoryRowsAsync());
    }

    [Fact]
    public async Task An_unknown_patient_is_404_and_a_patient_merged_away_is_409_and_neither_is_recorded()
    {
        var unknown = await _admin.SendAsync(HttpMethod.Post, PackageUrl(Guid.NewGuid()));
        var unknownRecord = await _admin.SendAsync(HttpMethod.Post, RecordUrl(Guid.NewGuid()));
        Assert.Equal(("patient_not_found", "patient_not_found"), (unknown.Body.GetProperty("error").GetString(), unknownRecord.Body.GetProperty("error").GetString()));
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (unknown.Status, unknownRecord.Status));

        var survivor = await _s.PatientAsync("Cy", "Kim");
        var preview = (await _admin.SendAsync(HttpMethod.Get, $"/api/patient-merges/preview?sourceId={_patient}&survivorId={survivor}")).Body;
        var merge = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", new
        {
            sourcePatientId = _patient, survivorPatientId = survivor, reason = "Registered twice",
            sourceRowVersion = preview.GetProperty("sourceRowVersion").GetString(), survivorRowVersion = preview.GetProperty("survivorRowVersion").GetString(),
            fieldChoices = preview.GetProperty("conflicts").EnumerateArray().ToDictionary(c => c.GetProperty("field").GetString()!, _ => "Source"), confirmed = true, confirmIdentityDifference = true,
        }, "merge-for-exports");
        Assert.Equal(HttpStatusCode.OK, merge.Status);

        var merged = await _admin.SendAsync(HttpMethod.Post, PackageUrl());
        var mergedRecord = await _admin.SendAsync(HttpMethod.Post, RecordUrl());
        Assert.Equal((HttpStatusCode.Conflict, "patient_merged"), (merged.Status, merged.Body.GetProperty("error").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "patient_merged"), (mergedRecord.Status, mergedRecord.Body.GetProperty("error").GetString()));
        Assert.Empty(await HistoryRowsAsync());
    }

    [Fact]
    public async Task When_the_audit_store_is_down_nothing_is_sent_the_answer_is_a_stable_code_and_the_row_is_failed()
    {
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestFault] ON [AuditLogEntries] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] IN ('PatientReadableRecordExported', 'PatientDataPackageExported')) THROW 59034, 'the audit store is down', 1; END");
        try
        {
            foreach (var url in new[] { RecordUrl(), PackageUrl() })
            {
                var response = await PostAsync(_admin, url);
                var text = await response.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal("export_failed", JsonDocument.Parse(text).RootElement.GetProperty("error").GetString());
                Assert.DoesNotContain("Rivera", text);                                                              // no page and no package left
                Assert.False(response.Headers.Contains("X-Export-Id"));
            }

            Assert.All(await HistoryRowsAsync(), r => Assert.Equal(DataExportResult.Failed, r.Result));
            Assert.Equal(2, await AuditCountAsync(AuditEventTypes.DataExportFailed));
        }
        finally
        {
            await using var db = _fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestFault]");
        }
    }

    [Fact]
    public async Task The_three_permissions_are_separate_and_an_administrator_can_grant_one_without_a_code_change()
    {
        await GrantAsync("Dentist", Permission.ExportPatientData);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_others["Dentist"], PackageUrl())).StatusCode);              // the granted one
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_others["Dentist"], AllUrl)).StatusCode);              // does not open the bulk export
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(_others["Dentist"], RecordUrl())).StatusCode);         // nor the stand-alone readable record
        Assert.Equal(_others["Dentist"].UserId, (await HistoryRowsAsync()).Single().RequestedByUserId);
    }

    [Fact]
    public async Task The_history_shows_a_person_only_their_own_exports_unless_they_hold_the_bulk_permission_and_has_no_patient_detail()
    {
        await GrantAsync("Dentist", Permission.ExportPatientData);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_admin, PackageUrl())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_others["Dentist"], PackageUrl())).StatusCode);

        var asDentist = await _others["Dentist"].SendAsync(HttpMethod.Get, "/api/patient-exports/history");
        var asAdmin = await _admin.SendAsync(HttpMethod.Get, "/api/patient-exports/history");

        Assert.Equal(HttpStatusCode.OK, asDentist.Status);
        Assert.Equal(1, asDentist.Body.GetArrayLength());
        Assert.Equal(2, asAdmin.Body.GetArrayLength());
        Assert.Equal("Complete", asAdmin.Body[0].GetProperty("result").GetString());
        Assert.False(asAdmin.Body[0].GetProperty("interrupted").GetBoolean());
        Assert.Equal(_patient, asAdmin.Body[0].GetProperty("patientId").GetGuid());
        Assert.DoesNotContain("Rivera", asAdmin.Body.GetRawText());
        var invalid = await _admin.SendAsync(HttpMethod.Get, "/api/patient-exports/history?limit=0");
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_limit"), (invalid.Status, invalid.Body.GetProperty("error").GetString()));
        Assert.Equal(1, (await _admin.SendAsync(HttpMethod.Get, "/api/patient-exports/history?limit=1")).Body.GetArrayLength());
    }

    [Fact]
    public async Task The_overview_explains_the_package_before_it_is_started()
    {
        var overview = await _admin.SendAsync(HttpMethod.Get, "/api/patient-exports/overview");

        Assert.Equal(HttpStatusCode.OK, overview.Status);
        Assert.Equal(ExportDomainCatalog.Domains.Count, overview.Body.GetProperty("domains").GetArrayLength());
        Assert.Equal(ExportDomainCatalog.Excluded.Count, overview.Body.GetProperty("excluded").GetArrayLength());
        Assert.Equal(5000, overview.Body.GetProperty("maxPatients").GetInt32());
        Assert.Equal("IdOnly", overview.Body.GetProperty("defaultFolderNaming").GetString());
        Assert.Equal("TableByTable", overview.Body.GetProperty("allPatientsReadMode").GetString());
        Assert.True(overview.Body.GetProperty("patientCount").GetInt32() >= 1);
        Assert.True(overview.Body.GetProperty("allPatientsWithinLimits").GetBoolean());
    }

    private async Task GrantAsync(string role, Permission permission)
    {
        await using var db = _fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
        db.RolePermissionOverrides.Add(new RolePermissionOverride
        {
            Id = Guid.NewGuid(), Role = role, Permission = permission.ToString(), Effect = "Grant", Reason = "controlled setup", ChangedByUserId = _admin.UserId, ChangedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
