using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the package for one patient against a real SQL Server, built from a real chart that crosses the domains. It holds the readme, one data file per domain and a manifest whose format, version, scope and
/// counts are right; every file matches the hash the manifest gives; the rows of each table are exactly the patient's (the counts agree with the database, and another patient's rows are not in it); relationships across
/// domains still resolve inside the package; nothing secret is in it; the history row and the audit entry are written with no patient detail; a second export of the same data gives the same data files; an unknown or
/// merged-away patient is refused with no history row; a domain that cannot be read makes the package Partial and says so; and when the audit entry cannot be saved no package is returned, the history row is Failed
/// and no temporary file is left behind.
/// </summary>
public class PatientDataPackageServiceTests : MergeChartTestBase
{
    private string _temp = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _temp = Path.Combine(Path.GetTempPath(), $"alveara-export-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temp);
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private Task<DataPackageFile> ExportAsync(Guid patient)
    {
        var services = new ServiceCollection().AddScoped(_ => Fixture.CreateContext()).BuildServiceProvider();
        var db = Fixture.CreateContext();
        var options = Options.Create(new DataExportOptions { TempDirectory = _temp });
        var builder = new PatientDataPackageBuilder(db, Clock, options, new ReadableRecordLoader(db, Clock), NullLogger<PatientDataPackageBuilder>.Instance);
        return new PatientDataPackageService(db, builder, Clock, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PatientDataPackageService>.Instance).ExportPatientAsync(patient, S.Actor, default);
    }

    private static Dictionary<string, byte[]> Entries(Stream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }

    private static JsonElement Json(byte[] bytes) => JsonDocument.Parse(bytes).RootElement;

    private static IEnumerable<JsonElement> Rows(Dictionary<string, byte[]> entries, string domain, string table) =>
        Json(entries[$"data/{domain}.json"]).GetProperty("tables").GetProperty(table).EnumerateArray();

    private async Task<(Dictionary<string, byte[]> Entries, JsonElement Manifest)> ExportedAsync(Guid patient)
    {
        await using var file = await ExportAsync(patient);
        var entries = Entries(file.Content);
        return (entries, Json(entries["manifest.json"]));
    }

    [Fact]
    public async Task A_real_chart_exports_as_a_package_with_a_readme_a_data_file_for_every_domain_and_a_manifest_that_says_what_it_holds()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();

        var (entries, manifest) = await ExportedAsync(Ann);

        var expected = new[] { "README.txt", "manifest.json", "readable-record.html" }.Concat(ExportDomainCatalog.Domains.Select(d => $"data/{d.Key}.json")).Order().ToArray();
        Assert.Equal(expected, entries.Keys.Order().ToArray());
        Assert.Equal(("alveara.patient-data-package", 1), (manifest.GetProperty("package").GetProperty("name").GetString(), manifest.GetProperty("package").GetProperty("version").GetInt32()));
        Assert.Equal("Complete", manifest.GetProperty("result").GetString());
        Assert.Equal(("OnePatient", 1, Ann), (manifest.GetProperty("scope").GetProperty("kind").GetString(), manifest.GetProperty("scope").GetProperty("patientCount").GetInt32(), manifest.GetProperty("scope").GetProperty("subjectRecordId").GetGuid()));
        Assert.Equal(ExportDomainCatalog.Domains.Count, manifest.GetProperty("domains").GetArrayLength());
        Assert.All(manifest.GetProperty("domains").EnumerateArray(), d => Assert.Equal("Included", d.GetProperty("status").GetString()));
        Assert.Equal(ExportDomainCatalog.Excluded.Select(g => g.Key).Order(), manifest.GetProperty("excluded").EnumerateArray().Select(g => g.GetProperty("key").GetString()!).Order());
        Assert.All(manifest.GetProperty("excluded").EnumerateArray(), g => Assert.False(string.IsNullOrWhiteSpace(g.GetProperty("reason").GetString())));
        Assert.Equal(0, manifest.GetProperty("problems").GetArrayLength());
        Assert.Equal("readable-record.html", manifest.GetProperty("readableRecord").GetString());
        Assert.Equal(JsonValueKind.Null, manifest.GetProperty("fhir").ValueKind);                       // reserved, never written today
        Assert.False(string.IsNullOrWhiteSpace(manifest.GetProperty("schemaVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(manifest.GetProperty("appVersion").GetString()));
    }

    [Fact]
    public async Task The_package_carries_the_readable_record_of_the_patient_as_a_page_that_says_it_is_inside_the_package()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var ann = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann));
        var bo = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Bo));

        var (entries, manifest) = await ExportedAsync(Ann);

        var page = System.Text.Encoding.UTF8.GetString(entries["readable-record.html"]);
        Assert.Contains($"<h1>{ann.FirstName} {ann.LastName}</h1>", page);
        Assert.Contains("Treatment plans", page);
        Assert.Contains("package's <em>documents</em> folder", page);
        Assert.Contains(manifest.GetProperty("exportId").GetGuid().ToString(), page);                            // the page and the manifest name the same export
        Assert.DoesNotContain($"{bo.FirstName} {bo.LastName}", page);                                            // not the other patient
    }

    [Fact]
    public async Task Every_file_matches_the_hash_and_size_the_manifest_gives()
    {
        await GiveRetiringChartAsync();

        var (entries, manifest) = await ExportedAsync(Ann);

        var listed = manifest.GetProperty("files").EnumerateArray().ToDictionary(f => f.GetProperty("path").GetString()!);
        Assert.Equal(entries.Keys.Where(k => k != "manifest.json").Order(), listed.Keys.Order());       // the manifest hashes every other file, and only those
        foreach (var (path, file) in listed)
        {
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(entries[path])), file.GetProperty("sha256").GetString());
            Assert.Equal(entries[path].Length, file.GetProperty("bytes").GetInt64());
        }
    }

    [Fact]
    public async Task The_rows_of_each_table_are_exactly_the_patients_and_the_counts_agree_with_the_database_and_the_manifest()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var counts = await CountsAsync(Ann);

        var (entries, manifest) = await ExportedAsync(Ann);

        var inFile = ExportDomainCatalog.Domains.SelectMany(d => d.Tables.Select(t => (Domain: d.Key, Table: t))).ToDictionary(x => x.Table, x => Rows(entries, x.Domain, x.Table).Count());
        foreach (var column in PatientOwnedData.Owned) Assert.True(counts[$"{column.Table}.{column.Column}"] == inFile[column.Table], $"{column.Table}: database {counts[$"{column.Table}.{column.Column}"]}, package {inFile[column.Table]}");
        Assert.Equal(1, inFile["Patients"]);
        Assert.Equal(Ann, Rows(entries, "registration", "Patients").Single().GetProperty("Id").GetGuid());                  // not Bo
        var fromManifest = manifest.GetProperty("domains").EnumerateArray().SelectMany(d => d.GetProperty("tables").EnumerateArray()).ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("rows").GetInt32());
        Assert.Equal(inFile, fromManifest);
        Assert.True(inFile["Appointments"] > 0 && inFile["TreatmentPlans"] > 0 && inFile["BillingEntries"] > 0 && inFile["PerioSessions"] > 0, "the arrangement covers several domains");
    }

    [Fact]
    public async Task Relationships_across_domains_still_resolve_inside_the_package()
    {
        await GiveRetiringChartAsync();

        var (entries, _) = await ExportedAsync(Ann);

        HashSet<Guid> Ids(string domain, string table) => Rows(entries, domain, table).Select(r => r.GetProperty("Id").GetGuid()).ToHashSet();
        var plans = Ids("treatment_plans", "TreatmentPlans"); var diagnoses = Ids("diagnoses", "Diagnoses"); var findings = Ids("dental_chart", "ToothFindings"); var appointments = Ids("appointments", "Appointments");
        var procedures = Json(entries["data/treatment_plans.json"]).GetProperty("lookupTables").GetProperty("ProcedureDefinitions").EnumerateArray().Select(r => r.GetProperty("Id").GetGuid()).ToHashSet();
        var items = Rows(entries, "treatment_plans", "TreatmentPlanItems").ToList();

        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Contains(i.GetProperty("PlanId").GetGuid(), plans));                       // inside a domain
        Assert.All(items, i => Assert.Contains(i.GetProperty("DiagnosisId").GetGuid(), diagnoses));              // across to the diagnoses domain
        Assert.All(items, i => Assert.Contains(i.GetProperty("ProcedureId").GetGuid(), procedures));            // across to a lookup table
        Assert.All(Rows(entries, "diagnoses", "DiagnosisLinks").Where(l => l.GetProperty("LinkType").GetString() == "Finding"), l => Assert.Contains(l.GetProperty("TargetId").GetGuid(), findings));
        Assert.All(Rows(entries, "appointments", "AppointmentEvents"), e => Assert.Contains(e.GetProperty("AppointmentId").GetGuid(), appointments));    // a child table with no patient id
        Assert.NotEmpty(Rows(entries, "appointments", "AppointmentEvents"));
    }

    [Fact]
    public async Task Nothing_secret_and_no_account_is_in_the_package()
    {
        await GiveRetiringChartAsync();

        var (entries, manifest) = await ExportedAsync(Ann);

        var text = string.Concat(entries.Where(e => e.Key.StartsWith("data/")).Select(e => System.Text.Encoding.UTF8.GetString(e.Value)));
        foreach (var secret in new[] { "PasswordHash", "MfaSecret", "RecoveryCode", "TokenHash", "\"UserAccounts\"" }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        var listedExcluded = manifest.GetProperty("excluded").EnumerateArray().SelectMany(g => g.GetProperty("tables").EnumerateArray().Select(t => t.GetString())).ToList();
        Assert.Contains("UserAccounts", listedExcluded);                                                         // left out, and said so
        Assert.Contains("AuditLogEntries", listedExcluded);
    }

    [Fact]
    public async Task The_export_is_recorded_before_it_leaves_with_its_totals_and_hash_and_logged_with_no_patient_detail()
    {
        await GiveRetiringChartAsync();
        var firstName = await WithDb(db => db.Patients.AsNoTracking().Where(p => p.Id == Ann).Select(p => p.FirstName).SingleAsync());
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);

        await using var file = await ExportAsync(Ann);

        var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync(h => h.ExportId == file.ExportId));
        Assert.Equal((DataExportKind.Package, DataExportScope.OnePatient, Ann, S.Actor, DataExportResult.Complete), (row.Kind, row.Scope, row.SubjectRecordId, row.RequestedByUserId, row.Result));
        Assert.Equal((1, file.SizeBytes, file.Sha256, 0), (row.PatientCount, row.SizeBytes, row.Sha256, row.ProblemCount));
        Assert.NotNull(row.FinishedAtUtc);
        file.Content.Position = 0;
        Assert.Equal(row.Sha256, Convert.ToHexStringLower(await SHA256.HashDataAsync(file.Content)));              // the hash on the row is the hash of the file
        var entry = await WithDb(db => db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.PatientDataPackageExported));
        Assert.Equal((S.Actor, Ann, file.ExportId), (entry.PerformedByUserAccountId, entry.TargetUserAccountId, entry.CorrelationId));
        Assert.InRange(entry.TimestampUtc, before, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.DoesNotContain(firstName, entry.Details);
        Assert.Contains(file.ExportId.ToString(), entry.Details);
    }

    [Fact]
    public async Task Exporting_the_same_data_twice_gives_two_exports_with_the_same_data_files_and_each_has_its_own_row()
    {
        await GiveRetiringChartAsync();

        var (first, _) = await ExportedAsync(Ann);
        var (second, _) = await ExportedAsync(Ann);

        foreach (var domain in ExportDomainCatalog.Domains) Assert.Equal(first[$"data/{domain.Key}.json"], second[$"data/{domain.Key}.json"]);     // same input, same data
        Assert.Equal(2, await WithDb(db => db.DataExportHistory.CountAsync(h => h.SubjectRecordId == Ann)));
        Assert.Equal(2, await WithDb(db => db.DataExportHistory.Select(h => h.ExportId).Distinct().CountAsync()));
    }

    [Fact]
    public async Task An_unknown_patient_and_a_merged_away_patient_are_refused_with_no_history_row_and_no_file()
    {
        var unknown = await Assert.ThrowsAsync<PatientException>(() => ExportAsync(Guid.NewGuid()));
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Bo, Ann, default));                        // Bo is merged into Ann through the real merge, so the record is retired the way it is in use
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Bo, Ann, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion, preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));
        var merged = await Assert.ThrowsAsync<PatientException>(() => ExportAsync(Bo));

        Assert.Equal(("patient_not_found", 404, "patient_merged", 409), (unknown.Code, unknown.StatusCode, merged.Code, merged.StatusCode));
        Assert.Equal(0, await WithDb(db => db.DataExportHistory.CountAsync()));
        Assert.Empty(Directory.GetFiles(_temp));
    }

    [Fact]
    public async Task A_domain_that_cannot_be_read_makes_the_package_partial_and_the_manifest_says_so()
    {
        await GiveRetiringChartAsync();
        await WithDb(db => db.Database.ExecuteSqlRawAsync("EXEC sp_rename 'PerioReadings', 'PerioReadingsGone'"));          // the periodontal domain can no longer be read

        await using var file = await ExportAsync(Ann);
        var entries = Entries(file.Content);
        var manifest = Json(entries["manifest.json"]);

        Assert.Equal("Partial", file.Result);
        Assert.Equal("Partial", manifest.GetProperty("result").GetString());
        var perio = manifest.GetProperty("domains").EnumerateArray().Single(d => d.GetProperty("key").GetString() == "periodontal");
        Assert.Equal("Unavailable", perio.GetProperty("status").GetString());
        Assert.False(entries.ContainsKey("data/periodontal.json"));                                              // no half-written file
        var problem = Assert.Single(manifest.GetProperty("problems").EnumerateArray());
        Assert.Equal(("domain_unavailable", "periodontal"), (problem.GetProperty("code").GetString(), problem.GetProperty("where").GetString()));
        Assert.Contains("Included", manifest.GetProperty("domains").EnumerateArray().Where(d => d.GetProperty("key").GetString() != "periodontal").Select(d => d.GetProperty("status").GetString()));
        var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync(h => h.ExportId == file.ExportId));
        Assert.Equal((DataExportResult.Partial, 1), (row.Result, row.ProblemCount));
    }

    [Fact]
    public async Task When_the_log_entry_cannot_be_saved_no_package_is_returned_the_row_is_failed_and_no_temporary_file_is_left()
    {
        await GiveRetiringChartAsync();
        await WithDb(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestFault] ON [AuditLogEntries] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = 'PatientDataPackageExported') THROW 59032, 'the audit store is down', 1; END"));
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => ExportAsync(Ann));

            var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync());
            Assert.Equal(DataExportResult.Failed, row.Result);                                                   // not left Started and not Complete
            Assert.NotNull(row.FinishedAtUtc);
            Assert.Empty(Directory.GetFiles(_temp));                                                             // the half-built package was deleted
            Assert.Equal(0, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PatientDataPackageExported)));
            Assert.Equal(1, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.DataExportFailed)));      // the failure itself is logged
        }
        finally
        {
            await WithDb(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestFault]"));
        }
    }

    [Fact]
    public async Task Closing_the_package_deletes_its_temporary_file()
    {
        await GiveRetiringChartAsync();

        var file = await ExportAsync(Ann);
        Assert.NotEmpty(Directory.GetFiles(_temp));                                                              // while the caller holds it
        await file.DisposeAsync();

        Assert.Empty(Directory.GetFiles(_temp));
    }
}
