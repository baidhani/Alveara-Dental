using Xunit;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017: the committed permission registry and the protected-permission rules. A permission cannot be removed or renamed silently (the enum and the registry must agree); a stored policy row may only name
/// a registered permission; the five protected permissions are the administrator's alone and can neither be granted nor delegated; the two administrator-assignable ones can be granted but never delegated.
/// </summary>
public class PermissionRegistryTests
{
    private static readonly string[] EnumNames = Enum.GetNames<Permission>();

    [Fact]
    public void Every_permission_in_code_is_registered_as_active()
    {
        var missing = EnumNames.Where(n => !PermissionRegistry.IsActive(n)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_active_registry_entry_still_exists_in_code_and_no_retired_name_has_come_back()
    {
        var removedWithoutRetiring = PermissionRegistry.Entries.Where(e => e.Value == PermissionStatus.Active && !EnumNames.Contains(e.Key)).Select(e => e.Key).ToList();
        Assert.Empty(removedWithoutRetiring);                                                                  // a permission removed from code must be marked Retired in the same change
        var reused = PermissionRegistry.Entries.Where(e => e.Value == PermissionStatus.Retired && EnumNames.Contains(e.Key)).Select(e => e.Key).ToList();
        Assert.Empty(reused);                                                                                  // a retired name is never reused (a rename is a retirement plus a new permission)
    }

    [Fact]
    public void The_registry_names_each_permission_once_and_unknown_names_are_not_known()
    {
        Assert.Equal(PermissionRegistry.Entries.Count, PermissionRegistry.Entries.Keys.Distinct().Count());
        Assert.False(PermissionRegistry.IsKnown("NotAPermission"));
        Assert.False(PermissionRegistry.IsActive("manageusers"));                                              // names are exact, never case-folded
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ManagePermissionGrants)));
    }

    [Fact]
    public void The_enum_value_of_a_permission_never_enters_the_registry_so_a_stored_row_cannot_depend_on_an_ordinal()
    {
        Assert.All(PermissionRegistry.Entries.Keys, k => Assert.False(int.TryParse(k, out _)));
    }

    [Fact]
    public void The_five_protected_permissions_are_the_administrators_alone_and_neither_grantable_nor_delegable()
    {
        var protectedSet = new[] { Permission.ManageUsers, Permission.ManageRoles, Permission.ManageMfaPolicy, Permission.IssuePasswordResets, Permission.ManagePermissionGrants };
        Assert.Equal(protectedSet.OrderBy(p => p), PermissionProtection.AdminOnly.OrderBy(p => p));
        foreach (var p in protectedSet)
        {
            Assert.False(PermissionProtection.IsGrantable(p), p.ToString());
            Assert.False(PermissionProtection.IsDelegable(p), p.ToString());
            foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Admin))
                Assert.False(PermissionMatrix.RoleHas(role, p), $"{role} must not hold {p} by default");
            Assert.True(PermissionMatrix.RoleHas(Role.Admin, p));
        }
    }

    [Fact]
    public void Account_status_and_session_revocation_may_be_granted_by_an_administrator_but_never_delegated()
    {
        foreach (var p in new[] { Permission.ManageAccountStatus, Permission.RevokeSessions })
        {
            Assert.True(PermissionProtection.IsGrantable(p));
            Assert.False(PermissionProtection.IsDelegable(p));
        }
        Assert.True(PermissionProtection.IsDelegable(Permission.ImportDocuments));
    }

    [Fact]
    public void Only_the_office_manager_may_delegate_and_only_to_the_five_other_working_roles()
    {
        Assert.Equal([Role.OfficeManager], Enum.GetValues<Role>().Where(PermissionProtection.MayDelegate).ToArray());
        Assert.Equal(new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.FrontDesk, Role.Billing }.OrderBy(r => r), Enum.GetValues<Role>().Where(PermissionProtection.IsDelegationTarget).OrderBy(r => r));
        Assert.True(PermissionProtection.IsFixedRole(Role.Admin));
        Assert.True(PermissionProtection.IsFixedRole(Role.Unassigned));
        Assert.False(PermissionProtection.IsFixedRole(Role.OfficeManager));
    }

    [Fact]
    public void Admin_holds_every_permission_and_an_unassigned_account_none_including_the_new_one()
    {
        Assert.All(Enum.GetValues<Permission>(), p => Assert.True(PermissionMatrix.RoleHas(Role.Admin, p), p.ToString()));
        Assert.All(Enum.GetValues<Permission>(), p => Assert.False(PermissionMatrix.RoleHas(Role.Unassigned, p), p.ToString()));
    }
}
