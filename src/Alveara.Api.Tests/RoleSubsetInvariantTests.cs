using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 (U-038, owner decision 2026-11-24): the Office Manager is the broader operational role and Front Desk its narrower subset, so
/// <c>default Front Desk permissions ⊆ default Office Manager permissions</c>. Adding a default to Front Desk without adding it to the Office Manager
/// (as happened with CollectPayments in ALV-N021 A2) silently stops an Office Manager from managing standard Front Desk accounts (the ALV-N017
/// target rules compare effective permission sets). This guard fails on the next such change and names the missing permissions.
/// </summary>
public class RoleSubsetInvariantTests
{
    [Fact]
    public void Every_default_front_desk_permission_is_also_a_default_office_manager_permission()
    {
        var missing = PermissionMatrix.PermissionsFor(Role.FrontDesk).Except(PermissionMatrix.PermissionsFor(Role.OfficeManager)).OrderBy(p => p.ToString()).ToList();
        Assert.True(missing.Count == 0, "Front Desk has default permissions the Office Manager lacks: " + string.Join(", ", missing));
    }

    [Fact]
    public void Collect_payments_is_a_default_of_both_roles()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.FrontDesk, Permission.CollectPayments));
        Assert.True(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.CollectPayments));
    }
}
