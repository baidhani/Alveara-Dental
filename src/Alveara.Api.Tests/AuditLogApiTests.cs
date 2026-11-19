using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Permissions;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 slice 1 (PKG-10 group A, U-011, BR-02) against real SQL Server and the real HTTP pipeline:
/// - the actor-name rule: a staff profile's display name, else the account's username, never a bare id (only a non-account id reads as the fallback);
/// - the audit API names the people, filters by event type and date range, and a patient edit's row carries the field names and an opaque History reference -
///   never an old or new value, the patient's name or contact data;
/// - BR-02: an account holding ViewAuditLog without ViewPatientRecords sees the same PHI-free row, and the History API refuses it (403, no content) while an
///   authorised account reads it.
/// </summary>
public class AuditLogApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _patient;
    private Session _admin = null!;
    private Session _billing = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = await _s.PatientAsync("Ann", "Rivera");
        _admin = await _api.SessionAsync("Admin");
        _billing = await _api.SessionAsync("Billing");
    });

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<Guid> EditPhoneAsync(string phone)
    {
        await using var db = _fixture.CreateContext();
        var p = await db.Patients.AsNoTracking().SingleAsync(x => x.Id == _patient);
        var fields = new PatientFields(p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth.ToString("yyyy-MM-dd"), p.Sex, phone, p.Email, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode);
        await new PatientEditService(db, SchedulingTestSupport.Clock).UpdateAsync(_patient, fields, Convert.ToBase64String(p.RowVersion), _admin.UserId, default);
        await using var check = _fixture.CreateContext();
        return await check.PatientHistory.AsNoTracking().Where(h => h.PatientId == _patient && h.FieldName == "phone").OrderByDescending(h => h.ChangedAtUtc).Select(h => h.Id).FirstAsync();
    }

    private static async Task<JsonElement[]> AuditAsync(Session who, string query = "")
    {
        var response = await who.Client.GetAsync($"/api/auth/audit-log?take=500{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray().ToArray();
    }

    private async Task PolicyAsync(string role, Permission permission, string effect)
    {
        await using var db = _fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
        db.RolePermissionOverrides.Add(new RolePermissionOverride
        {
            Id = Guid.NewGuid(), Role = role, Permission = permission.ToString(), Effect = effect, Reason = "controlled setup", ChangedByUserId = _admin.UserId, ChangedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    [Fact]
    public async Task A_person_is_named_by_staff_display_name_else_username_and_never_by_a_bare_id()
    {
        await using var db = _fixture.CreateContext();
        var noProfile = new UserAccount { Id = Guid.NewGuid(), Username = $"noprofile-{Guid.NewGuid():N}"[..20], CreatedAtUtc = DateTimeOffset.UtcNow };
        db.UserAccounts.Add(noProfile);
        await db.SaveChangesAsync();
        var withProfile = await db.StaffProfiles.AsNoTracking().FirstAsync(s => s.UserAccountId != null);
        var unknown = Guid.NewGuid();
        var names = await ClinicalNames.ResolveAsync(db, [noProfile.Id, withProfile.UserAccountId, unknown], default);
        Assert.Equal(noProfile.Username, ClinicalNames.Name(names, noProfile.Id));                              // U-011, F-029: the username when there is no staff profile
        Assert.Equal(withProfile.DisplayName, ClinicalNames.Name(names, withProfile.UserAccountId));
        Assert.Equal(ClinicalNames.Fallback, ClinicalNames.Name(names, unknown));                                // only an id that is no account at all
    }

    [Fact]
    public async Task A_patient_edit_row_names_its_fields_and_carries_only_an_opaque_history_reference_and_the_people_are_named()
    {
        var history = await EditPhoneAsync("555-010-9999");
        var rows = await AuditAsync(_admin, "&eventType=PatientUpdated");
        var row = Assert.Single(rows);
        Assert.Equal("Patient updated: phone.", row.GetProperty("details").GetString());
        Assert.Equal(history, row.GetProperty("historyEntryId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("performedByName").GetString()));
        Assert.NotEqual(_admin.UserId.ToString(), row.GetProperty("performedByName").GetString());
        var text = row.GetRawText();
        foreach (var leak in new[] { "555-010-9999", "Rivera", "Ann" }) Assert.DoesNotContain(leak, text);      // no value, no patient name

        Assert.All(await AuditAsync(_admin, "&eventType=PatientRegistered"), r => Assert.Equal("PatientRegistered", r.GetProperty("eventType").GetString()));
        var future = DateTimeOffset.UtcNow.AddDays(1).ToString("o");
        Assert.Empty(await AuditAsync(_admin, $"&fromUtc={Uri.EscapeDataString(future)}"));
        var types = JsonDocument.Parse(await _admin.Client.GetStringAsync("/api/auth/audit-log/event-types")).RootElement.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("PatientUpdated", types);
    }

    [Fact]
    public async Task An_audit_reader_without_patient_access_sees_the_same_phi_free_row_and_is_refused_the_history_itself()
    {
        var history = await EditPhoneAsync("555-010-7777");
        await PolicyAsync("Billing", Permission.ViewAuditLog, OverrideEffects.Grant);
        await PolicyAsync("Billing", Permission.ViewPatientRecords, OverrideEffects.Revoke);

        var row = Assert.Single(await AuditAsync(_billing, "&eventType=PatientUpdated"));                        // the audit reader sees the row ...
        Assert.Equal(history, row.GetProperty("historyEntryId").GetGuid());
        Assert.DoesNotContain("555-010-7777", row.GetRawText());

        var denied = await _billing.Client.GetAsync($"/api/patients/{_patient}/history");                        // ... but not the History it points at (direct API)
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("555-010-7777", await denied.Content.ReadAsStringAsync());

        var allowed = await _admin.Client.GetAsync($"/api/patients/{_patient}/history");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains("555-010-7777", await allowed.Content.ReadAsStringAsync());
    }
}
