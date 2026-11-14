using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (briefing 5.3, owner addendum 4), the pure part: the three new permissions, their default holders, the removed office-manager chairside default, and the role limit on
/// <see cref="Permission.CompleteOwnStartedVisit"/> (Dentist by default, grantable only to the Hygienist, never delegable). A stored grant to any other role is an invalid row that puts that
/// role in repair, so it can never take effect. The HTTP refusal (400 permission_role_limited) is in PermissionPolicyWriteTests.
/// </summary>
public class VisitWorkflowPermissionTests
{
    [Fact]
    public void The_new_permissions_are_appended_after_every_existing_one()
    {
        Assert.Equal((int)Permission.ManageMeasurementBaselines + 1, (int)Permission.CompleteOwnStartedVisit);
        Assert.Equal((int)Permission.CompleteOwnStartedVisit + 1, (int)Permission.ReassignAppointmentProvider);
        Assert.Equal((int)Permission.ReassignAppointmentProvider + 1, (int)Permission.CollectPayments);
    }

    [Theory]
    [InlineData(Role.Dentist, true, false, false)]
    [InlineData(Role.Hygienist, false, false, false)]
    [InlineData(Role.Assistant, false, false, false)]
    [InlineData(Role.FrontDesk, false, false, true)]
    [InlineData(Role.Billing, false, false, false)]
    [InlineData(Role.OfficeManager, false, true, false)]
    [InlineData(Role.Admin, true, true, true)]
    [InlineData(Role.Unassigned, false, false, false)]
    public void Default_holders_of_the_new_permissions(Role role, bool completeOwn, bool reassign, bool collect)
    {
        Assert.Equal(completeOwn, PermissionMatrix.RoleHas(role, Permission.CompleteOwnStartedVisit));
        Assert.Equal(reassign, PermissionMatrix.RoleHas(role, Permission.ReassignAppointmentProvider));
        Assert.Equal(collect, PermissionMatrix.RoleHas(role, Permission.CollectPayments));
    }

    [Fact]
    public void The_office_manager_keeps_front_office_moves_but_loses_the_chairside_default()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.UpdateVisitFlow));
        Assert.False(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.UpdateChairsideFlow));
        Assert.True(PermissionProtection.IsGrantableTo(Permission.UpdateChairsideFlow, Role.OfficeManager));   // an administrator may grant it back
    }

    [Theory]
    [InlineData(Role.Dentist, true)]
    [InlineData(Role.Hygienist, true)]
    [InlineData(Role.Assistant, false)]
    [InlineData(Role.FrontDesk, false)]
    [InlineData(Role.Billing, false)]
    [InlineData(Role.OfficeManager, false)]
    public void Completing_a_started_visit_may_be_given_only_to_the_dentist_and_the_hygienist(Role role, bool grantable)
    {
        Assert.Equal(grantable, PermissionProtection.IsGrantableTo(Permission.CompleteOwnStartedVisit, role));
        Assert.False(PermissionProtection.IsDelegable(Permission.CompleteOwnStartedVisit));
        Assert.True(PermissionProtection.IsGrantableTo(Permission.ReassignAppointmentProvider, role));            // the other two are ordinary grantable permissions
        Assert.True(PermissionProtection.IsGrantableTo(Permission.CollectPayments, role));
    }

    [Fact]
    public void A_stored_grant_to_the_hygienist_takes_effect()
    {
        var snapshot = PolicySnapshotBuilder.Build(1, [PolicyTestSupport.Override("Hygienist", nameof(Permission.CompleteOwnStartedVisit), "Grant")], [], null);
        Assert.Contains(Permission.CompleteOwnStartedVisit, snapshot.EffectiveFor(Role.Hygienist));
        Assert.True(snapshot.IsHealthy);
    }

    [Theory]
    [InlineData("Assistant")]
    [InlineData("FrontDesk")]
    [InlineData("Billing")]
    [InlineData("OfficeManager")]
    public void A_stored_grant_to_any_other_role_is_invalid_never_applied_and_puts_the_role_in_repair(string role)
    {
        var snapshot = PolicySnapshotBuilder.Build(1, [PolicyTestSupport.Override(role, nameof(Permission.CompleteOwnStartedVisit), "Grant")], [], null);
        var parsed = Enum.Parse<Role>(role);
        Assert.Equal(InvalidRowReason.RoleLimitedPermissionOverride, Assert.Single(snapshot.InvalidRows).Reason);
        Assert.Equal(RoleHealth.RepairRequired, snapshot.HealthOf(parsed));                                         // policy health reports it; the role is denied until repaired
        Assert.Equal(0, snapshot.OverrideCount);
    }

    [Fact]
    public void A_delegation_of_completing_a_started_visit_is_invalid()
    {
        var delegation = new PermissionDelegation
        {
            Id = Guid.NewGuid(), DelegateRole = "OfficeManager", Permission = nameof(Permission.CompleteOwnStartedVisit), TargetRole = "Hygienist", ChangedAtUtc = DateTimeOffset.UtcNow,
        };
        var snapshot = PolicySnapshotBuilder.Build(1, [], [delegation], null);
        Assert.Equal(InvalidRowReason.DelegationNotDelegable, Assert.Single(snapshot.InvalidRows).Reason);
        Assert.Empty(snapshot.Delegations);
    }
}
