using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N018 through the real pipeline (real API, real SQL Server, cookie sessions): the administrator's override write (grant, revoke, revert) with its reason, confirmation (typed for the high-impact list),
/// idempotency key and stale-write check; the protected permissions and fixed roles refused; one transaction for version, row, history and audit; the office manager's delegated grant limited by the closed
/// delegation table and by what the delegate itself holds; the administrator's delegation table; and the account list that holders of the two assignable permissions see.
/// </summary>
public class PermissionPolicyWriteTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private int _keys;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private string Key() => $"k-{Interlocked.Increment(ref _keys)}-{Guid.NewGuid():N}";

    private static async Task<long> VersionAsync(Session s) => (await s.SendAsync(HttpMethod.Get, "/api/permissions/effective")).Body.GetProperty("version").GetInt64();

    private static Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(Session s, string role, string permission, string effect, long version, string? key, string? reason = "Test reason", bool confirmed = true, string? typed = null) =>
        s.SendAsync(HttpMethod.Put, "/api/permissions/overrides", new { role, permission, effect, reason, expectedVersion = version, confirmed, typedConfirmation = typed }, key);

    private static Task<(HttpStatusCode Status, JsonElement Body)> DelegateAsync(Session s, string target, string permission, bool allowed, long version, string? key, string reason = "Delegated change") =>
        s.SendAsync(HttpMethod.Post, "/api/permissions/delegated-grants", new { targetRole = target, permission, allowed, reason, expectedVersion = version, confirmed = true }, key);

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(Session s, string url) => await s.SendAsync(HttpMethod.Get, url);

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;

    private async Task<(int Overrides, int History, long Version)> StateAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await db.RolePermissionOverrides.CountAsync(), await db.RolePermissionHistories.CountAsync(), (await db.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version);
    }

    // ---------- the administrator's override write ----------

    [Fact]
    public async Task An_administrator_grants_and_reverts_a_cell_with_a_reason_and_it_applies_to_the_very_next_request()
    {
        var admin = await _api.SessionAsync("Admin");
        var billing = await _api.SessionAsync("Billing");
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(billing, "/api/backup/history")).Status);

        var v = await VersionAsync(admin);
        var granted = await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key(), "Billing reviews the backup status");
        Assert.Equal(HttpStatusCode.OK, granted.Status);
        Assert.Equal(("Granted", "granted", true, v + 1), (Str(granted.Body, "change"), Str(granted.Body, "source"), granted.Body.GetProperty("effective").GetBoolean(), granted.Body.GetProperty("policyVersion").GetInt64()));
        Assert.True(granted.Body.GetProperty("enabledUsers").GetInt32() >= 1);                                          // how many enabled accounts the change affects
        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(billing, "/api/backup/history")).Status);             // the very next request

        var reverted = await PutAsync(admin, "Billing", "ViewBackupStatus", "Revert", v + 1, Key(), "Back to the default");
        Assert.Equal(("Reverted", "default", false), (Str(reverted.Body, "change"), Str(reverted.Body, "source"), reverted.Body.GetProperty("effective").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(billing, "/api/backup/history")).Status);

        await using var db = _fixture.CreateContext();
        var history = await db.RolePermissionHistories.AsNoTracking().OrderBy(h => h.PolicyVersion).ToListAsync();
        Assert.Equal(new[] { PolicyChangeKinds.Granted, PolicyChangeKinds.Reverted }, history.Select(h => h.Kind).ToArray());
        Assert.Equal(("Billing reviews the backup status", false, true, false, admin.UserId), (history[0].Reason, history[0].OldEffective, history[0].NewEffective, history[0].ViaDelegation, history[0].ChangedByUserId));
        Assert.Equal(0, await db.RolePermissionOverrides.CountAsync());
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.PermissionGranted || a.EventType == AuditEventTypes.PermissionReverted).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Contains(audit, a => a.EventType == AuditEventTypes.PermissionGranted && a.Reason == "Billing reviews the backup status" && a.PerformedByUserAccountId == admin.UserId);
    }

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    [Fact]
    public async Task A_revoke_of_a_default_keeps_the_known_403_body_and_a_revert_restores_it()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var v = await VersionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Dentist", "ViewBilling", "Revoke", v, Key())).Status);
        var denied = await GetAsync(dentist, "/api/billing/payment-methods");
        Assert.Equal((HttpStatusCode.Forbidden, "permission_denied", "ViewBilling"), (denied.Status, Error(denied.Body), Str(denied.Body, "required")));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Dentist", "ViewBilling", "Revert", v + 1, Key())).Status);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(dentist, "/api/billing/payment-methods")).Status);
    }

    [Theory]
    [InlineData("ViewPatientRecords")]
    [InlineData("ManageBilling")]
    [InlineData("ManageBackups")]
    [InlineData("ManagePrescriptions")]
    [InlineData("ViewAuditLog")]
    public async Task A_high_impact_change_needs_the_permission_name_typed_exactly(string permission)
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        var role = permission is "ViewPatientRecords" or "ManagePrescriptions" ? "Hygienist" : "Dentist";
        var effect = PermissionMatrix.RoleHas(Enum.Parse<Role>(role), Enum.Parse<Permission>(permission)) ? "Revoke" : "Grant";
        Assert.Equal("typed_confirmation_required", Error((await PutAsync(admin, role, permission, effect, v, Key())).Body));
        Assert.Equal("typed_confirmation_required", Error((await PutAsync(admin, role, permission, effect, v, Key(), typed: permission.ToLowerInvariant())).Body));
        Assert.Equal("typed_confirmation_required", Error((await PutAsync(admin, role, permission, effect, v, Key(), typed: "something else")).Body));
        Assert.Equal((0, 0, v), await StateAsync());
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, role, permission, effect, v, Key(), typed: permission)).Status);
    }

    [Theory]
    [InlineData("ManageAccountStatus")]
    [InlineData("RevokeSessions")]
    public async Task The_two_assignable_permissions_need_typed_confirmation_and_are_never_delegable(string permission)
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        Assert.Equal("typed_confirmation_required", Error((await PutAsync(admin, "OfficeManager", permission, "Grant", v, Key())).Body));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "OfficeManager", permission, "Grant", v, Key(), typed: permission)).Status);
        var table = await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows = new[] { new { delegateRole = "OfficeManager", permission, targetRole = "Billing" } }, reason = "Try it", expectedVersion = v + 1, confirmed = true }, Key());
        Assert.Equal((HttpStatusCode.BadRequest, "delegation_not_allowed"), (table.Status, Error(table.Body)));
    }

    [Fact]
    public async Task An_ordinary_change_needs_an_explicit_confirmation_a_bounded_reason_a_key_and_the_version_seen()
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        Assert.Equal("confirmation_required", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key(), confirmed: false)).Body));
        Assert.Equal("reason_required", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key(), reason: null)).Body));
        Assert.Equal("reason_required", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key(), reason: "   ")).Body));
        Assert.Equal("reason_required", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key(), reason: new string('r', 501))).Body));
        Assert.Equal("reason_required", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key(), reason: "line\nbreak")).Body));
        Assert.Equal("idempotency_key_required", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, null)).Body));
        Assert.Equal("idempotency_key_required", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, new string('k', 65))).Body));
        var noVersion = await admin.SendAsync(HttpMethod.Put, "/api/permissions/overrides", new { role = "Billing", permission = "ViewBackupStatus", effect = "Grant", reason = "x", confirmed = true }, Key());
        Assert.Equal("expected_version_required", Error(noVersion.Body));
        Assert.Equal((0, 0, v), await StateAsync());
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key(), reason: new string('r', 500))).Status);   // the limit itself is allowed
    }

    [Theory]
    [InlineData("OfficeManager", "ManageUsers")]
    [InlineData("OfficeManager", "ManageRoles")]
    [InlineData("Dentist", "ManageMfaPolicy")]
    [InlineData("Dentist", "IssuePasswordResets")]
    [InlineData("OfficeManager", "ManagePermissionGrants")]
    public async Task A_protected_permission_can_never_be_written_for_any_role(string role, string permission)
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        var r = await PutAsync(admin, role, permission, "Grant", v, Key(), typed: permission);
        Assert.Equal((HttpStatusCode.BadRequest, "permission_protected"), (r.Status, Error(r.Body)));
        Assert.Equal((0, 0, v), await StateAsync());
    }

    [Theory]
    [InlineData("Admin", "ViewBilling", "Revoke", "role_not_editable")]
    [InlineData("Unassigned", "ViewBilling", "Grant", "role_not_editable")]
    [InlineData("Wizard", "ViewBilling", "Grant", "invalid_role")]
    [InlineData("dentist", "ViewBilling", "Grant", "invalid_role")]
    [InlineData("3", "ViewBilling", "Grant", "invalid_role")]
    [InlineData("Dentist", "NoSuchPermission", "Grant", "invalid_permission")]
    [InlineData("Dentist", "viewbilling", "Revoke", "invalid_permission")]
    [InlineData("Dentist", "17", "Revoke", "invalid_permission")]
    [InlineData("Dentist", "ViewBilling", "Allow", "invalid_effect")]
    [InlineData("Dentist", "ViewBilling", "grant", "invalid_effect")]
    public async Task Names_come_from_closed_sets_and_the_fixed_roles_cannot_be_edited(string role, string permission, string effect, string code)
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        var r = await PutAsync(admin, role, permission, effect, v, Key(), typed: permission);
        Assert.Equal((HttpStatusCode.BadRequest, code), (r.Status, Error(r.Body)));
        Assert.Equal((0, 0, v), await StateAsync());
    }

    [Fact]
    public async Task A_change_that_leaves_the_cell_as_it_is_is_refused_and_consumes_no_version()
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        Assert.Equal("matches_default", Error((await PutAsync(admin, "Dentist", "ViewPatientRecords", "Grant", v, Key(), typed: "ViewPatientRecords")).Body));   // already held by default
        Assert.Equal("matches_default", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Revoke", v, Key())).Body));                              // not held by default
        Assert.Equal("no_change", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Revert", v, Key())).Body));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key())).Status);
        Assert.Equal("no_change", Error((await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v + 1, Key())).Body));
        Assert.Equal((1, 1, v + 1), await StateAsync());
    }

    [Fact]
    public async Task A_stale_write_is_a_409_and_stores_nothing()
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key())).Status);
        var stale = await PutAsync(admin, "Hygienist", "ViewBackupStatus", "Grant", v, Key());                         // the caller still holds the old version
        Assert.Equal((HttpStatusCode.Conflict, "policy_changed"), (stale.Status, Error(stale.Body)));
        Assert.Equal((1, 1, v + 1), await StateAsync());
    }

    [Fact]
    public async Task The_same_key_and_request_replays_quietly_and_the_same_key_with_another_request_is_refused()
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        var key = Key();
        var first = await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, key);
        var again = await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, key);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal((HttpStatusCode.OK, true, "Replayed"), (again.Status, again.Body.GetProperty("replayed").GetBoolean(), Str(again.Body, "change")));
        var other = await PutAsync(admin, "Hygienist", "ViewBackupStatus", "Grant", v + 1, key);
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (other.Status, Error(other.Body)));
        Assert.Equal((1, 1, v + 1), await StateAsync());
    }

    [Fact]
    public async Task Only_an_administrator_can_write_overrides_and_the_delegation_table_and_a_write_needs_a_csrf_token()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var dentist = await _api.SessionAsync("Dentist");
        var v = await VersionAsync(admin);
        foreach (var s in new[] { manager, dentist })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(s, "Billing", "ViewBackupStatus", "Grant", v, Key())).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(s, "/api/permissions/delegations")).Status);
            var put = await s.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows = Array.Empty<object>(), reason = "x", expectedVersion = v, confirmed = true }, Key());
            Assert.Equal(HttpStatusCode.Forbidden, put.Status);
        }
        var noCsrf = await admin.SendAsync(HttpMethod.Put, "/api/permissions/overrides", new { role = "Billing", permission = "ViewBackupStatus", effect = "Grant", reason = "x", expectedVersion = v, confirmed = true }, Key(), csrf: false);
        Assert.NotEqual(HttpStatusCode.OK, noCsrf.Status);
        Assert.Equal((0, 0, v), await StateAsync());
    }

    [Fact]
    public async Task A_role_whose_stored_policy_needs_repair_cannot_be_edited_until_it_is_repaired()
    {
        var admin = await _api.SessionAsync("Admin");
        await using (var db = _fixture.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1 WHERE [Id] = 1");
            db.RolePermissionOverrides.Add(new RolePermissionOverride { Id = Guid.NewGuid(), Role = "Billing", Permission = "NoSuchPermission", Effect = "Revoke", Reason = "corrupt", ChangedByUserId = admin.UserId, ChangedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var v = await VersionAsync(admin);
        var r = await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, Key());
        Assert.Equal((HttpStatusCode.Conflict, "policy_needs_repair"), (r.Status, Error(r.Body)));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Hygienist", "ViewBackupStatus", "Grant", v, Key())).Status);     // another role is unaffected
    }

    [Fact]
    public async Task When_the_audit_entry_cannot_be_written_the_whole_change_is_rolled_back()
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseGrantAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.PermissionGranted}') THROW 59997, 'audit down', 1;");
        var key = Key();
        var r = await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, key);
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "policy_write_failed"), (r.Status, Error(r.Body)));
        Assert.Equal((0, 0, v), await StateAsync());                                                                       // no row, no history, no version
        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.IdempotencyReceipts.CountAsync(x => x.IdempotencyKey == key));                         // and no receipt, so the same key can be tried again
        await check.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestRefuseGrantAudit");
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Billing", "ViewBackupStatus", "Grant", v, key)).Status);
    }

    [Fact]
    public async Task Concurrent_writes_from_the_same_version_produce_exactly_one_change()
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        var roles = new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager" };
        var results = await Task.WhenAll(roles.Select(role => PutAsync(admin, role, "ViewBackupStatus", PermissionMatrix.RoleHas(Enum.Parse<Role>(role), Permission.ViewBackupStatus) ? "Revoke" : "Grant", v, Key())));
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Equal("policy_changed", Error(r.Body)));
        Assert.Equal((1, 1, v + 1), await StateAsync());
    }

    // ---------- the effective view for the editing screen ----------

    [Fact]
    public async Task The_effective_view_carries_the_affected_user_count_and_the_high_impact_flag()
    {
        var admin = await _api.SessionAsync("Admin");
        await _api.SessionAsync("Dentist");
        await _api.SessionAsync("Dentist");
        var view = (await GetAsync(admin, "/api/permissions/effective")).Body;
        var dentist = view.GetProperty("roles").EnumerateArray().First(r => Str(r, "role") == "Dentist");
        Assert.Equal(2, dentist.GetProperty("enabledUsers").GetInt32());
        Assert.True(dentist.GetProperty("permissions").EnumerateArray().First(c => Str(c, "permission") == "ViewPatientRecords").GetProperty("requiresTypedConfirmation").GetBoolean());
        Assert.False(dentist.GetProperty("permissions").EnumerateArray().First(c => Str(c, "permission") == "ViewSchedule").GetProperty("requiresTypedConfirmation").GetBoolean());
    }

    // ---------- the office manager's delegation ----------

    [Fact]
    public async Task The_office_manager_grants_and_removes_document_import_for_a_working_role_and_the_history_says_it_was_delegated()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var dentist = await _api.SessionAsync("Dentist");
        var perms = async () => (await GetAsync(dentist, "/api/auth/permissions")).Body.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.DoesNotContain("ImportDocuments", await perms());

        var card = (await GetAsync(manager, "/api/permissions/my-delegations")).Body;
        var group = Assert.Single(card.GetProperty("delegations").EnumerateArray());
        Assert.Equal("ImportDocuments", Str(group, "permission"));
        Assert.Equal(new[] { "Assistant", "Billing", "Dentist", "FrontDesk", "Hygienist" }, group.GetProperty("targets").EnumerateArray().Select(t => Str(t, "role")).OrderBy(x => x).ToArray());

        var v = card.GetProperty("policyVersion").GetInt64();
        var granted = await DelegateAsync(manager, "Dentist", "ImportDocuments", true, v, Key());
        Assert.Equal((HttpStatusCode.OK, "Granted", true), (granted.Status, Str(granted.Body, "change"), granted.Body.GetProperty("effective").GetBoolean()));
        Assert.Contains("ImportDocuments", await perms());
        var removed = await DelegateAsync(manager, "Dentist", "ImportDocuments", false, v + 1, Key());
        Assert.Equal(("Reverted", false), (Str(removed.Body, "change"), removed.Body.GetProperty("effective").GetBoolean()));
        Assert.DoesNotContain("ImportDocuments", await perms());
        Assert.Equal("no_change", Error((await DelegateAsync(manager, "Dentist", "ImportDocuments", false, v + 2, Key())).Body));

        await using var db = _fixture.CreateContext();
        var history = await db.RolePermissionHistories.AsNoTracking().OrderBy(h => h.PolicyVersion).ToListAsync();
        Assert.All(history, h => Assert.True(h.ViaDelegation));
        Assert.All(history, h => Assert.Equal(manager.UserId, h.ChangedByUserId));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/permissions/history")).Status);
    }

    [Theory]
    [InlineData("Admin", "ImportDocuments")]
    [InlineData("Unassigned", "ImportDocuments")]
    [InlineData("OfficeManager", "ImportDocuments")]
    [InlineData("Dentist", "ViewBilling")]
    [InlineData("Dentist", "ManageAccountStatus")]
    [InlineData("Dentist", "ManageUsers")]
    [InlineData("Dentist", "ManagePermissionGrants")]
    public async Task The_office_manager_cannot_go_beyond_the_closed_delegation_table(string target, string permission)
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var v = await VersionAsync(admin);
        var r = await DelegateAsync(manager, target, permission, true, v, Key());
        Assert.Equal((HttpStatusCode.Forbidden, "delegation_not_allowed"), (r.Status, Error(r.Body)));
        Assert.Equal((0, 0, v), await StateAsync());
    }

    [Fact]
    public async Task A_role_without_a_delegation_cannot_delegate_and_unknown_names_are_refused()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var manager = await _api.SessionAsync("OfficeManager");
        var v = await VersionAsync(admin);
        Assert.Equal("delegation_not_allowed", Error((await DelegateAsync(dentist, "Hygienist", "ImportDocuments", true, v, Key())).Body));
        Assert.Equal("invalid_role", Error((await DelegateAsync(manager, "Wizard", "ImportDocuments", true, v, Key())).Body));
        Assert.Equal("invalid_permission", Error((await DelegateAsync(manager, "Dentist", "Nope", true, v, Key())).Body));
        var empty = (await GetAsync(dentist, "/api/permissions/my-delegations")).Body;
        Assert.Equal(0, empty.GetProperty("delegations").GetArrayLength());
        Assert.Equal((0, 0, v), await StateAsync());
    }

    [Fact]
    public async Task A_delegate_cannot_grant_what_it_does_not_hold_and_loses_the_ability_when_the_delegation_is_removed()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var v = await VersionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "OfficeManager", "ImportDocuments", "Revoke", v, Key())).Status);          // the office manager no longer holds document import
        var noHold = await DelegateAsync(manager, "Dentist", "ImportDocuments", true, v + 1, Key());
        Assert.Equal((HttpStatusCode.Forbidden, "delegation_not_allowed"), (noHold.Status, Error(noHold.Body)));
        Assert.Equal(0, (await GetAsync(manager, "/api/permissions/my-delegations")).Body.GetProperty("delegations").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "OfficeManager", "ImportDocuments", "Revert", v + 1, Key())).Status);
        var table = await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows = Array.Empty<object>(), reason = "Delegation withdrawn", expectedVersion = v + 2, confirmed = true }, Key());
        Assert.Equal(HttpStatusCode.OK, table.Status);
        var gone = await DelegateAsync(manager, "Dentist", "ImportDocuments", true, v + 3, Key());
        Assert.Equal((HttpStatusCode.Forbidden, "delegation_not_allowed"), (gone.Status, Error(gone.Body)));
    }

    [Fact]
    public async Task A_delegated_write_is_stale_checked_idempotent_and_needs_a_confirmation_and_a_reason()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var v = await VersionAsync(admin);
        var key = Key();
        Assert.Equal(HttpStatusCode.OK, (await DelegateAsync(manager, "Billing", "ImportDocuments", true, v, key)).Status);
        Assert.True((await DelegateAsync(manager, "Billing", "ImportDocuments", true, v, key)).Body.GetProperty("replayed").GetBoolean());
        Assert.Equal("policy_changed", Error((await DelegateAsync(manager, "Dentist", "ImportDocuments", true, v, Key())).Body));
        Assert.Equal("reason_required", Error((await DelegateAsync(manager, "Dentist", "ImportDocuments", true, v + 1, Key(), reason: " ")).Body));
        var noConfirm = await manager.SendAsync(HttpMethod.Post, "/api/permissions/delegated-grants", new { targetRole = "Dentist", permission = "ImportDocuments", allowed = true, reason = "x", expectedVersion = v + 1 }, Key());
        Assert.Equal("confirmation_required", Error(noConfirm.Body));
        Assert.Equal("idempotency_key_required", Error((await DelegateAsync(manager, "Dentist", "ImportDocuments", true, v + 1, null)).Body));
        Assert.Equal((1, 1, v + 1), await StateAsync());
    }

    // ---------- the administrator's delegation table ----------

    private static object Row(string delegateRole, string permission, string target) => new { delegateRole, permission, targetRole = target };

    [Fact]
    public async Task An_administrator_edits_the_delegation_table_within_the_closed_sets_with_history_and_audit()
    {
        var admin = await _api.SessionAsync("Admin");
        var table = (await GetAsync(admin, "/api/permissions/delegations")).Body;
        Assert.Equal(5, table.GetProperty("rows").GetArrayLength());
        Assert.DoesNotContain("ManageAccountStatus", table.GetProperty("delegablePermissions").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("ManagePermissionGrants", table.GetProperty("delegablePermissions").EnumerateArray().Select(e => e.GetString()));
        var v = table.GetProperty("policyVersion").GetInt64();

        var rows = table.GetProperty("rows").EnumerateArray().Select(r => Row(Str(r, "delegateRole"), Str(r, "permission"), Str(r, "targetRole"))).Append(Row("OfficeManager", "ViewDocuments", "Dentist")).ToArray();
        var added = await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows, reason = "Office manager may also manage who views documents", expectedVersion = v, confirmed = true }, Key());
        Assert.Equal((HttpStatusCode.OK, "DelegationsChanged"), (added.Status, Str(added.Body, "change")));
        Assert.Equal(6, (await GetAsync(admin, "/api/permissions/delegations")).Body.GetProperty("rows").GetArrayLength());
        var noChange = await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows, reason = "Again", expectedVersion = v + 1, confirmed = true }, Key());
        Assert.Equal("no_change", Error(noChange.Body));

        await using var db = _fixture.CreateContext();
        var history = await db.RolePermissionHistories.AsNoTracking().SingleAsync();
        Assert.Equal((PolicyChangeKinds.DelegationAdded, "OfficeManager", "ViewDocuments", "Dentist"), (history.Kind, history.Role, history.Permission, history.TargetRole));
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.DelegationChanged));
    }

    [Theory]
    [InlineData("OfficeManager", "ManageUsers", "Dentist")]
    [InlineData("OfficeManager", "ManagePermissionGrants", "Dentist")]
    [InlineData("OfficeManager", "IssuePasswordResets", "Dentist")]
    [InlineData("OfficeManager", "ManageAccountStatus", "Dentist")]
    [InlineData("OfficeManager", "RevokeSessions", "Dentist")]
    [InlineData("Dentist", "ImportDocuments", "Hygienist")]
    [InlineData("Admin", "ImportDocuments", "Dentist")]
    [InlineData("OfficeManager", "ImportDocuments", "Admin")]
    [InlineData("OfficeManager", "ImportDocuments", "Unassigned")]
    [InlineData("OfficeManager", "ImportDocuments", "OfficeManager")]
    public async Task The_delegation_table_can_never_name_a_protected_permission_a_non_office_delegate_or_a_forbidden_target(string delegateRole, string permission, string target)
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        var r = await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows = new[] { Row(delegateRole, permission, target) }, reason = "Try it", expectedVersion = v, confirmed = true }, Key());
        Assert.Equal((HttpStatusCode.BadRequest, "delegation_not_allowed"), (r.Status, Error(r.Body)));
        Assert.Equal((0, 0, v), await StateAsync());
    }

    [Fact]
    public async Task The_delegation_table_write_is_stale_checked_idempotent_and_needs_a_reason_and_a_confirmation()
    {
        var admin = await _api.SessionAsync("Admin");
        var v = await VersionAsync(admin);
        var rows = new[] { Row("OfficeManager", "ImportDocuments", "Dentist") };
        var key = Key();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows, reason = "Narrow it", expectedVersion = v, confirmed = true }, key)).Status);
        Assert.True((await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows, reason = "Narrow it", expectedVersion = v, confirmed = true }, key)).Body.GetProperty("replayed").GetBoolean());
        Assert.Equal("policy_changed", Error((await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows = Array.Empty<object>(), reason = "Empty it", expectedVersion = v, confirmed = true }, Key())).Body));
        Assert.Equal("reason_required", Error((await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows = Array.Empty<object>(), reason = "", expectedVersion = v + 1, confirmed = true }, Key())).Body));
        Assert.Equal("confirmation_required", Error((await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { rows = Array.Empty<object>(), reason = "x", expectedVersion = v + 1, confirmed = false }, Key())).Body));
        Assert.Equal("rows_required", Error((await admin.SendAsync(HttpMethod.Put, "/api/permissions/delegations", new { reason = "x", expectedVersion = v + 1, confirmed = true }, Key())).Body));
        Assert.Equal(4, (await StateAsync()).History);                                                                     // four rows were removed, once
    }

    // ---------- the account list ----------

    private static HashSet<Role> RolesWhoseSetIsASubsetOf(Role actor, params Permission[] extra)
    {
        var held = PermissionMatrix.PermissionsFor(actor).Concat(extra).ToHashSet();
        return Enum.GetValues<Role>().Where(r => r != Role.Admin && PermissionMatrix.PermissionsFor(r).IsSubsetOf(held)).ToHashSet();
    }

    [Fact]
    public async Task A_holder_of_an_assignable_permission_sees_only_the_accounts_the_target_rule_allows_and_an_administrator_sees_all()
    {
        var admin = await _api.SessionAsync("Admin");
        var sessions = new Dictionary<Role, Session>();
        foreach (var role in new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.FrontDesk, Role.Billing, Role.OfficeManager }) sessions[role] = await _api.SessionAsync(role.ToString());
        var all = (await GetAsync(admin, "/api/auth/users")).Body.GetArrayLength();
        Assert.Equal(7, all);

        var v = await VersionAsync(admin);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "OfficeManager", "ManageAccountStatus", "Grant", v, Key(), typed: "ManageAccountStatus")).Status);
        var listed = (await GetAsync(sessions[Role.OfficeManager], "/api/auth/users")).Body.EnumerateArray().Select(u => Enum.Parse<Role>(Str(u, "role"))).ToList();
        var expected = RolesWhoseSetIsASubsetOf(Role.OfficeManager, Permission.ManageAccountStatus).Intersect(sessions.Keys).ToHashSet();   // only roles that have an account in this database can be listed
        Assert.Equal(expected.OrderBy(r => r), listed.Distinct().OrderBy(r => r));
        Assert.DoesNotContain(Role.Admin, listed);
        Assert.DoesNotContain(Role.Dentist, listed);                                                                       // holds permissions the office manager lacks
        Assert.Contains(Role.OfficeManager, listed);                                                                       // the holder's own role is a subset of itself

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "Dentist", "RevokeSessions", "Grant", v + 1, Key(), typed: "RevokeSessions")).Status);
        var dentistList = (await GetAsync(sessions[Role.Dentist], "/api/auth/users")).Body.EnumerateArray().Select(u => Enum.Parse<Role>(Str(u, "role"))).ToHashSet();
        Assert.Equal(RolesWhoseSetIsASubsetOf(Role.Dentist, Permission.RevokeSessions).Intersect(sessions.Keys).OrderBy(r => r), dentistList.OrderBy(r => r));
        Assert.DoesNotContain(Role.Admin, dentistList);

        Assert.Equal(all, (await GetAsync(admin, "/api/auth/users")).Body.GetArrayLength());
    }

    [Fact]
    public async Task Everyone_else_keeps_the_existing_denial_of_the_user_list_with_the_same_body()
    {
        await _api.SessionAsync("Admin");
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager" })
        {
            var r = await GetAsync(await _api.SessionAsync(role), "/api/auth/users");
            Assert.Equal(HttpStatusCode.Forbidden, r.Status);
            Assert.Equal("permission_denied", Error(r.Body));
            Assert.Equal("ManageUsers", Str(r.Body, "required"));                                                          // the body callers already handle
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Factory.CreateClient().GetAsync("/api/auth/users")).StatusCode);
    }
}
