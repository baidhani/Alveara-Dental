using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017, the decision service over a controlled store: the administrator and an unassigned account are answered from the code rules without reading the store; a role in repair or a degraded store is denied
/// with its own outcome; an unreadable store past the allowance is Unavailable; the version is read once per request however many checks the request makes; a screen is never shown what a request would refuse.
/// </summary>
public class EffectivePermissionServiceTests
{
    private static (EffectivePermissionService Service, ControlledPolicySource Source, ManualTime Time) Make()
    {
        var source = new ControlledPolicySource();
        var time = new ManualTime();
        return (new EffectivePermissionService(PolicyTestSupport.Store(source, time)), source, time);
    }

    [Fact]
    public async Task The_administrator_and_an_unassigned_account_are_answered_without_reading_the_store()
    {
        var (service, source, _) = Make();
        source.Failing = true;
        foreach (var p in Enum.GetValues<Permission>())
        {
            Assert.Equal(PermissionOutcome.Allowed, await service.CheckAsync(Role.Admin, p));
            Assert.Equal(PermissionOutcome.Denied, await service.CheckAsync(Role.Unassigned, p));
        }
        Assert.Equal(0, source.VersionReads);
        Assert.Equal(Enum.GetValues<Permission>().Length, (await service.PermissionsForAsync(Role.Admin)).Count);
        Assert.Empty(await service.PermissionsForAsync(Role.Unassigned));
    }

    [Fact]
    public async Task Every_cell_decision_equals_the_code_default_when_there_are_no_rows()
    {
        var (service, _, _) = Make();
        foreach (var role in Enum.GetValues<Role>())
            foreach (var permission in Enum.GetValues<Permission>())
                Assert.Equal(PermissionMatrix.RoleHas(role, permission) ? PermissionOutcome.Allowed : PermissionOutcome.Denied, await service.CheckAsync(role, permission));
    }

    [Fact]
    public async Task The_version_is_read_once_per_request_however_many_checks_it_makes()
    {
        var (service, source, _) = Make();
        for (var i = 0; i < 25; i++) await service.CheckAsync(Role.Dentist, Permission.ViewBilling);
        await service.CheckAnyAsync(Role.Dentist, [Permission.ManageBilling, Permission.ViewBilling]);
        await service.PermissionsForAsync(Role.Hygienist);
        Assert.Equal(2, source.VersionReads);                                                                           // the first check: the version it reads opens the bracket, then rows, then the closing version; the rest reuse it
        Assert.Equal(1, source.RowReads);
    }

    [Fact]
    public async Task A_role_with_an_invalid_row_is_denied_everything_with_its_own_outcome_and_other_roles_are_unaffected()
    {
        var (service, source, _) = Make();
        source.Overrides.Add(PolicyTestSupport.Override("Dentist", "ManageUsers", "Grant"));
        source.Version = 1;
        Assert.Equal(PermissionOutcome.RepairRequired, await service.CheckAsync(Role.Dentist, Permission.ViewPatientRecords));   // even a default the role holds
        Assert.Empty(await service.PermissionsForAsync(Role.Dentist));
        Assert.Equal(PermissionOutcome.Allowed, await service.CheckAsync(Role.Admin, Permission.ManageUsers));
        var other = new EffectivePermissionService(PolicyTestSupport.Store(source));
        Assert.Equal(PermissionOutcome.Allowed, await other.CheckAsync(Role.Hygienist, Permission.ViewPatientRecords));
        Assert.Equal(PermissionOutcome.RepairRequired, await other.CheckAsync(Role.Dentist, Permission.ManageUsers));        // the protected grant is not applied, and the role is denied outright
    }

    [Fact]
    public async Task A_degraded_store_denies_every_non_administrator_and_leaves_the_administrator_able_to_repair()
    {
        var (service, source, _) = Make();
        source.Overrides.Add(PolicyTestSupport.Override("Wizard", "ViewBilling"));
        source.Version = 1;
        foreach (var role in Enum.GetValues<Role>().Where(r => r is not (Role.Admin or Role.Unassigned)))
            Assert.Equal(PermissionOutcome.Degraded, await service.CheckAsync(role, Permission.ViewPatientRecords));
        Assert.Equal(PermissionOutcome.Allowed, await service.CheckAsync(Role.Admin, Permission.ManagePermissionGrants));
    }

    [Fact]
    public async Task An_unreadable_store_past_the_allowance_is_unavailable_and_never_allows_anything_to_a_working_role()
    {
        var (service, source, time) = Make();
        await service.CheckAsync(Role.Dentist, Permission.ViewPatientRecords);
        source.Failing = true;
        time.Advance(TimeSpan.FromSeconds(61));
        var fresh = new EffectivePermissionService(PolicyTestSupport.Store(source, time));                            // a new request on a host that never had a snapshot
        foreach (var p in Enum.GetValues<Permission>())
            Assert.Equal(PermissionOutcome.Unavailable, await fresh.CheckAsync(Role.Dentist, p));
        await Assert.ThrowsAsync<PolicyUnavailableException>(() => fresh.PermissionsForAsync(Role.Dentist));
    }

    [Fact]
    public async Task A_grant_and_a_revoke_change_the_next_decision_and_check_any_needs_just_one()
    {
        var (service, source, _) = Make();
        source.Overrides.Add(PolicyTestSupport.Override("Dentist", nameof(Permission.ImportDocuments), "Grant"));
        source.Overrides.Add(PolicyTestSupport.Override("Dentist", nameof(Permission.ViewBilling)));
        source.Version = 1;
        Assert.Equal(PermissionOutcome.Allowed, await service.CheckAsync(Role.Dentist, Permission.ImportDocuments));
        Assert.Equal(PermissionOutcome.Denied, await service.CheckAsync(Role.Dentist, Permission.ViewBilling));
        Assert.Equal(PermissionOutcome.Allowed, await service.CheckAnyAsync(Role.Dentist, [Permission.ViewBilling, Permission.ImportDocuments]));
        Assert.Equal(PermissionOutcome.Denied, await service.CheckAnyAsync(Role.Dentist, [Permission.ViewBilling, Permission.ManageBilling]));
    }

    [Fact]
    public async Task The_matrix_lists_every_role_with_its_effective_set()
    {
        var (service, _, _) = Make();
        var matrix = await service.MatrixAsync();
        Assert.Equal(Enum.GetValues<Role>().Length, matrix.Count);
        Assert.All(Enum.GetValues<Role>(), r => Assert.True(matrix[r].SetEquals(PermissionMatrix.PermissionsFor(r))));
    }

    [Theory]
    [InlineData(PermissionOutcome.Denied, 403, "permission_denied")]
    [InlineData(PermissionOutcome.RepairRequired, 403, "permission_policy_repair_required")]
    [InlineData(PermissionOutcome.Degraded, 503, "permission_policy_degraded")]
    [InlineData(PermissionOutcome.Unavailable, 503, "permission_policy_unavailable")]
    public void Refusals_have_stable_statuses_and_codes_and_the_ordinary_denial_keeps_its_body(PermissionOutcome outcome, int status, string code)
    {
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(PermissionResults.Refusal(outcome, "ViewBilling"));
        Assert.Equal(status, result.StatusCode);
        Assert.Contains($"error = {code}", result.Value!.ToString());
        if (outcome == PermissionOutcome.Denied) Assert.Contains("required = ViewBilling", result.Value!.ToString());
        Assert.Null(PermissionResults.Refusal(PermissionOutcome.Allowed, "ViewBilling"));
    }
}
