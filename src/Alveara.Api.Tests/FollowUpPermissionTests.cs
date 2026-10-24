using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-009: who holds the two follow-up permissions by default, stated once and plainly (the owner approved this table), and that neither is a protected permission, so the policy editor can change them.</summary>
public class FollowUpPermissionTests
{
    [Theory]
    [InlineData(Role.Admin, true, true)]
    [InlineData(Role.Dentist, true, true)]
    [InlineData(Role.Hygienist, true, true)]
    [InlineData(Role.Assistant, true, true)]
    [InlineData(Role.FrontDesk, true, true)]
    [InlineData(Role.OfficeManager, true, true)]
    [InlineData(Role.Billing, false, false)]
    [InlineData(Role.Unassigned, false, false)]
    public void The_clinical_and_front_office_roles_view_and_manage_follow_ups_and_billing_and_unassigned_do_neither(Role role, bool view, bool manage)
    {
        Assert.Equal(view, PermissionMatrix.RoleHas(role, Permission.ViewFollowUps));
        Assert.Equal(manage, PermissionMatrix.RoleHas(role, Permission.ManageFollowUps));
    }

    [Fact]
    public void Managing_follow_ups_implies_viewing_them_in_the_defaults()
    {
        foreach (var role in Enum.GetValues<Role>())
            if (PermissionMatrix.RoleHas(role, Permission.ManageFollowUps))
                Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewFollowUps), $"{role} manages follow-ups but cannot view them");
    }

    [Theory]
    [InlineData(Permission.ViewFollowUps)]
    [InlineData(Permission.ManageFollowUps)]
    public void Neither_follow_up_permission_is_protected_so_an_administrator_can_grant_revoke_and_delegate_it(Permission permission)
    {
        Assert.True(PermissionProtection.IsGrantable(permission));
        Assert.True(PermissionProtection.IsDelegable(permission));
        Assert.DoesNotContain(permission, PermissionProtection.AdminAssignableNeverDelegable);
    }
}
