using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017, the pure part of the engine: how stored rows become an effective policy. With no rows the result is exactly the code defaults for every (role, permission) cell, generated from the enums so
/// nothing is hard-coded; a grant adds and a revoke removes; the administrator and an unassigned account never change; an invalid row never restores access (the role is denied, or the store degraded, or a
/// delegation disabled); a retired permission is ignored.
/// </summary>
public class PolicySnapshotBuilderTests
{
    private static RolePermissionOverride Row(string role, string permission, string effect = "Revoke") => PolicyTestSupport.Override(role, permission, effect);

    private static PolicySnapshot Build(params RolePermissionOverride[] rows) =>
        PolicySnapshotBuilder.Build(1, rows, [], null);

    private static PermissionDelegation Delegation(string delegateRole, string permission, string target) =>
        new() { Id = Guid.NewGuid(), DelegateRole = delegateRole, Permission = permission, TargetRole = target, ChangedAtUtc = DateTimeOffset.UtcNow };

    [Fact]
    public void With_no_stored_rows_every_cell_equals_the_code_default_generated_from_the_enums()
    {
        var snapshot = PolicySnapshotBuilder.Build(0, [], [], null);
        var cells = 0;
        foreach (var role in Enum.GetValues<Role>())
            foreach (var permission in Enum.GetValues<Permission>())
            {
                cells++;
                Assert.Equal(PermissionMatrix.RoleHas(role, permission), snapshot.EffectiveFor(role).Contains(permission));
            }
        Assert.Equal(Enum.GetValues<Role>().Length * Enum.GetValues<Permission>().Length, cells);                    // the proof covers every cell the enums define, whatever their number
        Assert.True(snapshot.IsHealthy);
        Assert.Equal(0, snapshot.OverrideCount);
    }

    [Fact]
    public void A_revoke_removes_a_default_a_grant_adds_one_and_a_redundant_row_changes_nothing()
    {
        var snapshot = Build(Row("Dentist", nameof(Permission.ViewBilling)), Row("Dentist", nameof(Permission.ImportDocuments), "Grant"), Row("Dentist", nameof(Permission.ViewPatientRecords), "Grant"));
        Assert.DoesNotContain(Permission.ViewBilling, snapshot.EffectiveFor(Role.Dentist));
        Assert.Contains(Permission.ImportDocuments, snapshot.EffectiveFor(Role.Dentist));
        Assert.Contains(Permission.ViewPatientRecords, snapshot.EffectiveFor(Role.Dentist));                         // already held: harmless
        Assert.Equal(3, snapshot.OverrideCount);
        Assert.True(snapshot.IsHealthy);
        Assert.Contains(Permission.ViewBilling, snapshot.EffectiveFor(Role.Billing));  // other roles are untouched
    }

    [Fact]
    public void Reverting_means_the_row_is_gone_and_the_default_returns()
    {
        Assert.DoesNotContain(Permission.ViewBilling, Build(Row("Dentist", nameof(Permission.ViewBilling))).EffectiveFor(Role.Dentist));
        Assert.Contains(Permission.ViewBilling, Build().EffectiveFor(Role.Dentist));
    }

    [Fact]
    public void The_administrator_and_an_unassigned_account_never_change_whatever_a_row_says()
    {
        var snapshot = Build(Row("Admin", nameof(Permission.ViewBilling)), Row("Unassigned", nameof(Permission.ViewBilling), "Grant"));
        Assert.Equal(Enum.GetValues<Permission>().Length, snapshot.EffectiveFor(Role.Admin).Count);
        Assert.Empty(snapshot.EffectiveFor(Role.Unassigned));
        Assert.Equal(2, snapshot.InvalidRows.Count);
        Assert.All(snapshot.InvalidRows, r => Assert.Equal(InvalidRowReason.FixedRoleOverride, r.Reason));
        Assert.False(snapshot.Degraded);
        Assert.Equal(RoleHealth.Healthy, snapshot.HealthOf(Role.Admin));                                              // Admin keeps access by invariant, so it is never put in repair
    }

    [Theory]
    [InlineData("Dentist", "NoSuchPermission", "Revoke", InvalidRowReason.UnknownPermission)]
    [InlineData("Dentist", "viewbilling", "Revoke", InvalidRowReason.UnknownPermission)]
    [InlineData("Dentist", "17", "Revoke", InvalidRowReason.UnknownPermission)]
    [InlineData("Dentist", "ViewBilling", "Allow", InvalidRowReason.UnknownEffect)]
    [InlineData("Dentist", "ViewBilling", "grant", InvalidRowReason.UnknownEffect)]
    [InlineData("Dentist", "ManageUsers", "Grant", InvalidRowReason.ProtectedPermissionOverride)]
    [InlineData("OfficeManager", "ManageRoles", "Grant", InvalidRowReason.ProtectedPermissionOverride)]
    [InlineData("OfficeManager", "IssuePasswordResets", "Grant", InvalidRowReason.ProtectedPermissionOverride)]
    [InlineData("OfficeManager", "ManageMfaPolicy", "Grant", InvalidRowReason.ProtectedPermissionOverride)]
    [InlineData("OfficeManager", "ManagePermissionGrants", "Grant", InvalidRowReason.ProtectedPermissionOverride)]
    public void An_invalid_override_for_a_known_role_puts_that_role_in_repair_and_never_restores_access(string role, string permission, string effect, InvalidRowReason reason)
    {
        var snapshot = Build(Row(role, permission, effect));
        var parsed = Enum.Parse<Role>(role);
        Assert.Equal(RoleHealth.RepairRequired, snapshot.HealthOf(parsed));
        Assert.Equal(reason, Assert.Single(snapshot.InvalidRows).Reason);
        Assert.False(snapshot.Degraded);
        Assert.All(Enum.GetValues<Role>().Where(r => r != parsed), r => Assert.Equal(RoleHealth.Healthy, snapshot.HealthOf(r)));   // other roles are unaffected
        Assert.All(Enum.GetValues<Permission>(), p => Assert.Equal(PermissionMatrix.RoleHas(parsed, p), snapshot.EffectiveFor(parsed).Contains(p)));   // the protected grant was not applied
        Assert.False(snapshot.IsHealthy);
    }

    [Theory]
    [InlineData("Wizard")]
    [InlineData("dentist")]
    [InlineData("3")]
    [InlineData("")]
    public void An_override_whose_role_cannot_be_attributed_degrades_the_whole_store(string role)
    {
        var snapshot = Build(Row(role, nameof(Permission.ViewBilling)));
        Assert.True(snapshot.Degraded);
        Assert.Equal(InvalidRowReason.UnknownRole, Assert.Single(snapshot.InvalidRows).Reason);
        Assert.All(Enum.GetValues<Role>(), r => Assert.Equal(PermissionMatrix.RoleHas(r, Permission.ViewBilling), snapshot.EffectiveFor(r).Contains(Permission.ViewBilling)));
    }

    private sealed class RetiredNames(params string[] retired) : IPermissionNames
    {
        public bool IsActive(string name) => PermissionNames.Committed.IsActive(name);
        public bool IsRetired(string name) => retired.Contains(name);
    }

    [Fact]
    public void A_row_naming_a_retired_permission_is_ignored_and_counted_and_is_not_invalid()
    {
        var snapshot = PolicySnapshotBuilder.Build(1, [Row("Dentist", "OldPermission"), Row("Dentist", nameof(Permission.ViewBilling))], [], new RetiredNames("OldPermission"));
        Assert.Equal(1, snapshot.RetiredRowCount);
        Assert.Empty(snapshot.InvalidRows);
        Assert.True(snapshot.IsHealthy);
        Assert.DoesNotContain(Permission.ViewBilling, snapshot.EffectiveFor(Role.Dentist));
    }

    [Fact]
    public void The_default_delegation_is_valid_and_active()
    {
        var rows = new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing" }.Select(t => Delegation("OfficeManager", "ImportDocuments", t)).ToArray();
        var snapshot = PolicySnapshotBuilder.Build(1, [], rows, null);
        Assert.Equal(5, snapshot.Delegations.Count);
        Assert.Empty(snapshot.InvalidRows);
        Assert.All(snapshot.Delegations, d => Assert.Equal((Role.OfficeManager, Permission.ImportDocuments), (d.DelegateRole, d.Permission)));
    }

    [Theory]
    [InlineData("OfficeManager", "ManageUsers", "Dentist", InvalidRowReason.DelegationProtectedPermission)]
    [InlineData("OfficeManager", "ManagePermissionGrants", "Dentist", InvalidRowReason.DelegationProtectedPermission)]
    [InlineData("OfficeManager", "ManageAccountStatus", "Dentist", InvalidRowReason.DelegationNotDelegable)]
    [InlineData("OfficeManager", "RevokeSessions", "Dentist", InvalidRowReason.DelegationNotDelegable)]
    [InlineData("OfficeManager", "NoSuchPermission", "Dentist", InvalidRowReason.DelegationUnknownPermission)]
    [InlineData("Admin", "ImportDocuments", "Dentist", InvalidRowReason.DelegationBadDelegate)]
    [InlineData("Dentist", "ImportDocuments", "Hygienist", InvalidRowReason.DelegationBadDelegate)]
    [InlineData("OfficeManager", "ImportDocuments", "Admin", InvalidRowReason.DelegationBadTarget)]
    [InlineData("OfficeManager", "ImportDocuments", "Unassigned", InvalidRowReason.DelegationBadTarget)]
    [InlineData("OfficeManager", "ImportDocuments", "OfficeManager", InvalidRowReason.DelegationBadTarget)]
    [InlineData("OfficeManager", "ImportDocuments", "Wizard", InvalidRowReason.DelegationBadTarget)]
    public void An_invalid_delegation_is_disabled_and_changes_no_ordinary_decision(string delegateRole, string permission, string target, InvalidRowReason reason)
    {
        var snapshot = PolicySnapshotBuilder.Build(1, [], [Delegation(delegateRole, permission, target)], null);
        Assert.Empty(snapshot.Delegations);
        Assert.Equal(reason, Assert.Single(snapshot.InvalidRows).Reason);
        Assert.False(snapshot.Degraded);
        Assert.All(Enum.GetValues<Role>(), r => Assert.Equal(RoleHealth.Healthy, snapshot.HealthOf(r)));
        Assert.All(Enum.GetValues<Role>(), r => Assert.Equal(PermissionMatrix.PermissionsFor(r).Count, snapshot.EffectiveFor(r).Count));
    }

    [Fact]
    public void The_same_rows_always_give_the_same_snapshot()
    {
        var rows = new[] { Row("Dentist", nameof(Permission.ViewBilling)), Row("Billing", nameof(Permission.ImportDocuments), "Grant") };
        var a = PolicySnapshotBuilder.Build(7, rows, [], null);
        var b = PolicySnapshotBuilder.Build(7, rows.Reverse().ToArray(), [], null);
        Assert.All(Enum.GetValues<Role>(), r => Assert.True(a.EffectiveFor(r).SetEquals(b.EffectiveFor(r))));
    }
}
