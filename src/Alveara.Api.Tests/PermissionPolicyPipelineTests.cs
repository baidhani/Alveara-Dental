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
/// ALV-N017 through the real pipeline (real API, real SQL Server, cookie sessions, the real permission attributes): with no stored rows access is exactly today's; a stored revoke is refused on the very next
/// request with the same 403 body callers already know; a stored grant opens a route; deleting the row (a revert) restores the default; a protected permission can never be granted by a stored row; an invalid
/// row denies its role with its own code while the administrator keeps working. The policy rows are written by controlled setup (the write API arrives in ALV-N018).
/// </summary>
public class PermissionPolicyPipelineTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    /// <summary>One representative GET route per permission, one per module.</summary>
    public static IEnumerable<object[]> Samples() => new (Permission, string)[]
    {
        (Permission.CompleteForms, "/api/forms/templates/available"),
        (Permission.ManageDocumentSettings, "/api/document-settings"),
        (Permission.ManageFormTemplates, "/api/forms/templates"),
        (Permission.ManagePracticeConfiguration, "/api/config/appointment-types"),
        (Permission.SendMessages, "/api/messages/recipients"),
        (Permission.ViewAuditLog, "/api/completion/reconciliation"),
        (Permission.ViewBackupStatus, "/api/backup/history"),
        (Permission.ViewBilling, "/api/billing/payment-methods"),
        (Permission.ViewClinicalDocumentation, "/api/clinical/templates"),
        (Permission.ViewDocuments, "/api/document-categories"),
        (Permission.ViewPatientRecords, "/api/patients"),
        (Permission.ViewPermissionMatrix, "/api/auth/permission-matrix"),
        (Permission.ViewPrescriptions, "/api/prescription-workflow/options"),
        (Permission.ViewSchedule, "/api/appointments"),
    }.Select(s => new object[] { s.Item1, s.Item2 });

    private async Task ApplyAsync(Action<AlveraDbContext> change)
    {
        await using var db = _fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
        change(db);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private Task SetOverrideAsync(Role role, Permission permission, string effect, Guid? by = null) => ApplyAsync(db => db.RolePermissionOverrides.Add(new RolePermissionOverride
    {
        Id = Guid.NewGuid(), Role = role.ToString(), Permission = permission.ToString(), Effect = effect, Reason = "controlled setup", ChangedByUserId = by ?? db.UserAccounts.Select(u => u.Id).First(), ChangedAtUtc = DateTimeOffset.UtcNow,
    }));

    private Task RevertAsync(Role role, Permission permission) => ApplyAsync(db => db.RolePermissionOverrides.RemoveRange(db.RolePermissionOverrides.Where(o => o.Role == role.ToString() && o.Permission == permission.ToString())));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(Session s, string url) => await s.SendAsync(HttpMethod.Get, url);

    private async Task EnsureAdminAsync() => await _api.SessionAsync("Admin");

    private static Role? HolderOf(Permission p) => Enum.GetValues<Role>().Cast<Role?>().FirstOrDefault(r => r is not (Role.Admin or Role.Unassigned) && PermissionMatrix.RoleHas(r!.Value, p));

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task A_stored_revoke_is_refused_on_the_very_next_request_with_the_known_403_body_and_a_revert_restores_it(Permission permission, string url)
    {
        await EnsureAdminAsync();
        if (HolderOf(permission) is not { } role)
        {
            // only the administrator holds it by default: prove the same cycle the other way round (a stored grant opens it, the revert closes it)
            var dentist = await _api.SessionAsync("Dentist");
            var closed = await GetAsync(dentist, url);
            Assert.Equal(HttpStatusCode.Forbidden, closed.Status);
            Assert.Equal(permission.ToString(), closed.Body.GetProperty("required").GetString());
            await SetOverrideAsync(Role.Dentist, permission, OverrideEffects.Grant);
            Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(dentist, url)).Status);
            await RevertAsync(Role.Dentist, permission);
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(dentist, url)).Status);
            return;
        }
        var session = await _api.SessionAsync(role.ToString());

        var before = await GetAsync(session, url);
        Assert.NotEqual(HttpStatusCode.Forbidden, before.Status);                                                      // today's behaviour, no rows
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, before.Status);

        await SetOverrideAsync(role, permission, OverrideEffects.Revoke);
        var revoked = await GetAsync(session, url);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.Status);
        Assert.Equal("permission_denied", revoked.Body.GetProperty("error").GetString());
        Assert.Equal(permission.ToString(), revoked.Body.GetProperty("required").GetString());

        await RevertAsync(role, permission);
        var reverted = await GetAsync(session, url);
        Assert.Equal(before.Status, reverted.Status);
    }

    [Fact]
    public async Task A_stored_grant_opens_a_route_for_a_role_that_lacks_it_and_a_revert_closes_it_again()
    {
        await EnsureAdminAsync();
        var session = await _api.SessionAsync("Billing");
        Assert.False(PermissionMatrix.RoleHas(Role.Billing, Permission.ViewBackupStatus));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(session, "/api/backup/history")).Status);
        await SetOverrideAsync(Role.Billing, Permission.ViewBackupStatus, OverrideEffects.Grant);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(session, "/api/backup/history")).Status);
        await RevertAsync(Role.Billing, Permission.ViewBackupStatus);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(session, "/api/backup/history")).Status);
    }

    [Fact]
    public async Task A_change_to_one_role_changes_nothing_for_another_role_or_for_the_administrator()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        await SetOverrideAsync(Role.Dentist, Permission.ViewPatientRecords, OverrideEffects.Revoke);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(dentist, "/api/patients")).Status);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(hygienist, "/api/patients")).Status);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(admin, "/api/patients")).Status);
    }

    [Fact]
    public async Task The_permission_lists_the_client_reads_follow_the_stored_policy_and_an_administrators_list_is_always_complete()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var mine = async () => (await GetAsync(manager, "/api/auth/permissions")).Body.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        Assert.Contains(nameof(Permission.ImportDocuments), await mine());
        await SetOverrideAsync(Role.OfficeManager, Permission.ImportDocuments, OverrideEffects.Revoke);
        Assert.DoesNotContain(nameof(Permission.ImportDocuments), await mine());
        var matrix = (await GetAsync(admin, "/api/auth/permission-matrix")).Body.EnumerateArray().First(r => r.GetProperty("role").GetString() == "OfficeManager");
        Assert.DoesNotContain(nameof(Permission.ImportDocuments), matrix.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(Enum.GetValues<Permission>().Length, (await GetAsync(admin, "/api/auth/permissions")).Body.GetProperty("permissions").GetArrayLength());
    }

    [Theory]
    [InlineData(nameof(Permission.ManageUsers))]
    [InlineData(nameof(Permission.ManageRoles))]
    [InlineData(nameof(Permission.ManageMfaPolicy))]
    [InlineData(nameof(Permission.IssuePasswordResets))]
    [InlineData(nameof(Permission.ManagePermissionGrants))]
    public async Task A_stored_row_can_never_grant_a_protected_permission_and_its_role_is_denied_until_repaired(string protectedName)
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        await SetOverrideAsync(Role.OfficeManager, Enum.Parse<Permission>(protectedName), OverrideEffects.Grant);

        var users = await GetAsync(manager, "/api/auth/users");
        Assert.Equal(HttpStatusCode.Forbidden, users.Status);
        var anything = await GetAsync(manager, "/api/patients");                                                       // even a permission the role holds by default: the row is invalid, so the role is denied
        Assert.Equal(HttpStatusCode.Forbidden, anything.Status);
        Assert.Equal("permission_policy_repair_required", anything.Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/auth/users")).Status);                            // the administrator keeps working
        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(admin, "/api/patients")).Status);
    }

    [Fact]
    public async Task A_row_whose_role_cannot_be_attributed_denies_every_working_role_with_503_and_the_administrator_still_works()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        await ApplyAsync(db => db.RolePermissionOverrides.Add(new RolePermissionOverride
        {
            Id = Guid.NewGuid(), Role = "Wizard", Permission = "ViewBilling", Effect = OverrideEffects.Revoke, Reason = "corrupt", ChangedByUserId = db.UserAccounts.Select(u => u.Id).First(), ChangedAtUtc = DateTimeOffset.UtcNow,
        }));
        var response = await GetAsync(dentist, "/api/patients");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.Equal("permission_policy_degraded", response.Body.GetProperty("error").GetString());
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, (await GetAsync(admin, "/api/patients")).Status);
    }

    [Fact]
    public async Task The_shipped_delegation_rows_exist_and_change_no_ordinary_access()
    {
        var dentist = await _api.SessionAsync("Dentist");
        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(dentist, "/api/patients")).Status);
        await using var db = _fixture.CreateContext();
        Assert.Equal(5, await db.PermissionDelegations.CountAsync());                                                  // the shipped delegation rows exist and are valid
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(dentist, "/api/auth/permissions")).Status);
    }

    // ---------- account administration through the API (ALV-N017 target rules, last administrator, unchanged user list) ----------

    [Fact]
    public async Task The_user_list_has_the_same_status_and_body_for_every_role_before_and_after_the_two_assignable_permissions_are_granted()
    {
        var admin = await _api.SessionAsync("Admin");
        var roles = Enum.GetValues<Role>().Where(r => r is not (Role.Admin or Role.Unassigned)).ToList();
        var sessions = new Dictionary<Role, Session>();
        foreach (var role in roles) sessions[role] = await _api.SessionAsync(role.ToString());
        async Task<Dictionary<string, (HttpStatusCode, string?)>> Snapshot()
        {
            var result = new Dictionary<string, (HttpStatusCode, string?)> { ["Admin"] = ((await GetAsync(admin, "/api/auth/users")).Status, null) };
            foreach (var role in roles)
            {
                var r = await GetAsync(sessions[role], "/api/auth/users");
                result[role.ToString()] = (r.Status, r.Body.ValueKind == JsonValueKind.Undefined ? null : r.Body.GetRawText());
            }
            return result;
        }
        var before = await Snapshot();
        Assert.Equal(HttpStatusCode.OK, before["Admin"].Item1);
        Assert.All(roles, r => Assert.Equal(HttpStatusCode.Forbidden, before[r.ToString()].Item1));
        await SetOverrideAsync(Role.OfficeManager, Permission.ManageAccountStatus, OverrideEffects.Grant);
        await SetOverrideAsync(Role.OfficeManager, Permission.RevokeSessions, OverrideEffects.Grant);
        var after = await Snapshot();
        Assert.All(roles, r => Assert.Equal(before[r.ToString()], after[r.ToString()]));                              // the broader, target-filtered listing belongs to ALV-N018
        Assert.Equal("permission_denied", JsonDocument.Parse(after["OfficeManager"].Item2!).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_granted_office_manager_is_stopped_by_the_target_rules_through_the_api_and_every_refusal_is_audited()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var frontDesk = await _api.SessionAsync("FrontDesk");
        var dentist = await _api.SessionAsync("Dentist");
        await SetOverrideAsync(Role.OfficeManager, Permission.ManageAccountStatus, OverrideEffects.Grant);
        await SetOverrideAsync(Role.OfficeManager, Permission.RevokeSessions, OverrideEffects.Grant);

        var onAdmin = await manager.SendAsync(HttpMethod.Put, $"/api/auth/{admin.UserId}/enabled", new { enabled = false });
        Assert.Equal((HttpStatusCode.Forbidden, "account_target_not_allowed", "target_is_admin"), (onAdmin.Status, onAdmin.Body.GetProperty("error").GetString(), onAdmin.Body.GetProperty("rule").GetString()));
        var onDentist = await manager.SendAsync(HttpMethod.Put, $"/api/auth/{dentist.UserId}/enabled", new { enabled = false });
        Assert.Equal("target_exceeds_actor", onDentist.Body.GetProperty("rule").GetString());
        var self = await manager.SendAsync(HttpMethod.Put, $"/api/auth/{manager.UserId}/enabled", new { enabled = false });
        Assert.Equal("self_disable", self.Body.GetProperty("rule").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(HttpMethod.Post, $"/api/auth/{dentist.UserId}/revoke-sessions")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(HttpMethod.Put, $"/api/auth/{dentist.UserId}/session-timeout", new { sessionTimeoutMinutes = 60 })).Status);

        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(HttpMethod.Put, $"/api/auth/{frontDesk.UserId}/enabled", new { enabled = false })).Status);   // a subset of the office manager permissions
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(HttpMethod.Post, $"/api/auth/{frontDesk.UserId}/revoke-sessions")).Status);

        await using var db = _fixture.CreateContext();
        Assert.Equal(5, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.AccountActionRefused));
        Assert.True((await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == frontDesk.UserId)).IsDisabled);
        Assert.False((await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == dentist.UserId)).IsDisabled);
        Assert.False((await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == admin.UserId)).IsDisabled);
    }

    [Fact]
    public async Task The_only_administrator_cannot_demote_or_disable_themselves_through_the_api_and_the_refusal_is_a_stable_409()
    {
        var admin = await _api.SessionAsync("Admin");
        var demote = await admin.SendAsync(HttpMethod.Put, $"/api/auth/{admin.UserId}/role", new { role = "Dentist" });
        Assert.Equal((HttpStatusCode.Conflict, "last_admin"), (demote.Status, demote.Body.GetProperty("error").GetString()));
        var disable = await admin.SendAsync(HttpMethod.Put, $"/api/auth/{admin.UserId}/enabled", new { enabled = false });
        Assert.Equal((HttpStatusCode.Conflict, "last_admin"), (disable.Status, disable.Body.GetProperty("error").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/auth/users")).Status);                             // still signed in and still an administrator
    }

    [Fact]
    public async Task No_combination_of_stored_grants_lets_a_non_administrator_reach_a_protected_endpoint()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var target = await _api.SessionAsync("FrontDesk");
        foreach (var p in Enum.GetValues<Permission>().Where(PermissionProtection.IsGrantable).Where(p => !PermissionMatrix.RoleHas(Role.OfficeManager, p)))
            await SetOverrideAsync(Role.OfficeManager, p, OverrideEffects.Grant);                                          // every grantable permission an office manager lacks

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(HttpMethod.Put, $"/api/auth/{target.UserId}/role", new { role = "Admin" })).Status);          // ManageRoles
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(HttpMethod.Post, $"/api/auth/{target.UserId}/reset-password")).Status);                       // IssuePasswordResets
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(manager, "/api/auth/users")).Status);                                                                    // ManageUsers
        var protectedNames = new[] { Permission.ManageUsers, Permission.ManageRoles, Permission.ManageMfaPolicy, Permission.IssuePasswordResets, Permission.ManagePermissionGrants };
        var mine = (await GetAsync(manager, "/api/auth/permissions")).Body.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.All(protectedNames, p => Assert.DoesNotContain(p.ToString(), mine));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/auth/users")).Status);
        await using var db = _fixture.CreateContext();
        Assert.Equal(Role.FrontDesk, (await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == target.UserId)).Role);
    }

    [Fact]
    public async Task Without_a_session_the_answer_is_still_401_and_the_policy_is_not_consulted_for_a_missing_role()
    {
        var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/patients")).StatusCode);
    }
}
