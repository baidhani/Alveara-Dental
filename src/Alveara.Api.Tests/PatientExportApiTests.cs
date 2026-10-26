using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients.Portability;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-018, the export through the real pipeline (real API, real SQL Server, cookie sessions): the administrator alone downloads a patient's file (every working role is 403 and an anonymous call 401), the
/// download is JSON that no cache may keep and that follows the documented layout, an unknown patient is 404 and a patient merged away is 409, every export leaves one audit entry for the signed-in user,
/// the permissions are held by the roles the owner chose, and an administrator can grant the export to another role through the policy rows without any code change.
/// </summary>
public class PatientExportApiTests : IAsyncLifetime
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

    private static string ExportUrl(Guid id) => $"/api/patients/{id}/export";

    private async Task<List<AuditLogEntry>> ExportLogAsync() { await using var db = _fixture.CreateContext(); return await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.PatientExported).ToListAsync(); }

    [Fact]
    public async Task The_administrator_downloads_a_json_file_in_the_documented_layout_that_no_cache_may_keep()
    {
        var response = await _admin.Client.GetAsync(ExportUrl(_patient));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(new Regex(@"^alveara-patient-[0-9a-f]{32}\.json$"), response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());                         // personal details: never cached
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(PatientExportFormat.FormatName, root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(_patient, root.GetProperty("source").GetProperty("sourcePatientId").GetGuid());
        Assert.Equal("Ann", root.GetProperty("patient").GetProperty("firstName").GetString());
        Assert.Equal("Rivera", root.GetProperty("patient").GetProperty("lastName").GetString());
    }

    [Fact]
    public async Task Only_the_administrator_may_export_and_an_anonymous_caller_sees_nothing_and_no_refused_call_is_logged()
    {
        foreach (var (role, session) in _others)
            Assert.True(HttpStatusCode.Forbidden == (await session.Client.GetAsync(ExportUrl(_patient))).StatusCode, $"{role} must not export");
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ExportUrl(_patient))).StatusCode);
        Assert.Empty(await ExportLogAsync());                                                           // nothing left the system, so nothing was logged as an export
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.GetAsync(ExportUrl(_patient))).StatusCode);
    }

    [Fact]
    public async Task An_unknown_patient_is_404_and_a_patient_merged_into_another_is_409_and_neither_is_logged()
    {
        var unknown = await _admin.SendAsync(HttpMethod.Get, ExportUrl(Guid.NewGuid()));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));

        var survivor = await _s.PatientAsync("Cy", "Kim");
        var preview = (await _admin.SendAsync(HttpMethod.Get, $"/api/patient-merges/preview?sourceId={_patient}&survivorId={survivor}")).Body;
        var merge = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", new
        {
            sourcePatientId = _patient, survivorPatientId = survivor, reason = "Registered twice",
            sourceRowVersion = preview.GetProperty("sourceRowVersion").GetString(), survivorRowVersion = preview.GetProperty("survivorRowVersion").GetString(),
            fieldChoices = preview.GetProperty("conflicts").EnumerateArray().ToDictionary(c => c.GetProperty("field").GetString()!, _ => "Source"), confirmed = true, confirmIdentityDifference = true,
        }, "merge-for-export");
        Assert.Equal(HttpStatusCode.OK, merge.Status);

        var merged = await _admin.SendAsync(HttpMethod.Get, ExportUrl(_patient));
        Assert.Equal((HttpStatusCode.Conflict, "patient_merged"), (merged.Status, merged.Body.GetProperty("error").GetString()));
        Assert.Empty(await ExportLogAsync());
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.GetAsync(ExportUrl(survivor))).StatusCode);
    }

    [Fact]
    public async Task Every_export_is_logged_with_the_signed_in_user_and_the_time()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.GetAsync(ExportUrl(_patient))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.GetAsync(ExportUrl(_patient))).StatusCode);

        var log = await ExportLogAsync();
        Assert.Equal(2, log.Count);
        Assert.All(log, e => Assert.Equal(_admin.UserId, e.PerformedByUserAccountId));
        Assert.All(log, e => Assert.InRange(e.TimestampUtc, before, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.All(log, e => Assert.Equal(_patient, e.TargetUserAccountId));
    }

    [Fact]
    public void Export_is_held_by_the_administrator_alone_and_import_by_the_administrator_and_the_office_manager_by_default()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
        {
            Assert.Equal(role == Role.Admin, PermissionMatrix.RoleHas(role, Permission.ExportPatientData));
            Assert.Equal(role is Role.Admin or Role.OfficeManager, PermissionMatrix.RoleHas(role, Permission.ImportPatientData));
        }

        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ExportPatientData)));
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ImportPatientData)));
    }

    [Fact]
    public async Task An_administrator_can_grant_the_export_to_another_role_and_take_it_back_without_any_code_change()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _others["Dentist"].Client.GetAsync(ExportUrl(_patient))).StatusCode);

        var rowId = Guid.NewGuid();
        await SetOverrideAsync(rowId, "Grant");
        Assert.Equal(HttpStatusCode.OK, (await _others["Dentist"].Client.GetAsync(ExportUrl(_patient))).StatusCode);   // the stored grant opens it
        Assert.Equal(_others["Dentist"].UserId, (await ExportLogAsync()).Single().PerformedByUserAccountId);              // and the log names the dentist

        await using (var db = _fixture.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
            await db.Database.ExecuteSqlAsync($"DELETE FROM [RolePermissionOverrides] WHERE [Id] = {rowId}");
            await tx.CommitAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await _others["Dentist"].Client.GetAsync(ExportUrl(_patient))).StatusCode);
    }

    private async Task SetOverrideAsync(Guid rowId, string effect)
    {
        await using var db = _fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
        db.RolePermissionOverrides.Add(new RolePermissionOverride
        {
            Id = rowId, Role = nameof(Role.Dentist), Permission = nameof(Permission.ExportPatientData), Effect = effect, Reason = "controlled setup", ChangedByUserId = _admin.UserId, ChangedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
