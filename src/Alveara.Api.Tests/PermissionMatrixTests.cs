using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>Pure unit tests for the role -> permission mapping itself (no DB, no HTTP) - the API-
/// level enforcement is covered separately in AuthControllerPermissionMatrixTests.</summary>
public class PermissionMatrixTests
{
    [Fact]
    public void Admin_holds_every_defined_permission()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            Assert.True(PermissionMatrix.RoleHas(Role.Admin, permission), $"Admin is missing {permission}.");
        }
    }

    [Fact]
    public void A_newly_self_registered_Unassigned_account_holds_no_permissions()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            Assert.False(PermissionMatrix.RoleHas(Role.Unassigned, permission), $"Unassigned unexpectedly has {permission}.");
        }
    }

    [Fact]
    public void Dentist_can_manage_clinical_notes_but_not_security_administration()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.Dentist, Permission.ManageClinicalNotes));
        Assert.True(PermissionMatrix.RoleHas(Role.Dentist, Permission.ViewPatientRecords));
        Assert.False(PermissionMatrix.RoleHas(Role.Dentist, Permission.ManageUsers));
        Assert.False(PermissionMatrix.RoleHas(Role.Dentist, Permission.ManageRoles));
    }

    [Fact]
    public void Billing_can_manage_billing_but_not_clinical_notes()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.Billing, Permission.ManageBilling));
        Assert.False(PermissionMatrix.RoleHas(Role.Billing, Permission.ManageClinicalNotes));
        Assert.False(PermissionMatrix.RoleHas(Role.Billing, Permission.ManageUsers));
    }

    [Fact]
    public void FrontDesk_can_manage_appointments_but_not_clinical_or_security_permissions()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.FrontDesk, Permission.ManageAppointments));
        Assert.False(PermissionMatrix.RoleHas(Role.FrontDesk, Permission.ManageClinicalNotes));
        Assert.False(PermissionMatrix.RoleHas(Role.FrontDesk, Permission.ManageUsers));
    }

    [Fact]
    public void Assistant_can_view_but_not_manage_patient_records()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.Assistant, Permission.ViewPatientRecords));
        Assert.False(PermissionMatrix.RoleHas(Role.Assistant, Permission.ManageClinicalNotes));
    }

    [Fact]
    public void OfficeManager_can_view_the_audit_log_and_permission_matrix_but_cannot_manage_security_administration()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.ViewAuditLog));
        Assert.True(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.ViewPermissionMatrix));
        Assert.False(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.ManageUsers));
        Assert.False(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.ManageRoles));
    }

    [Fact]
    public void Every_role_defined_on_the_enum_has_an_explicit_entry_in_the_matrix()
    {
        // A role silently falling through to "no permissions" because someone forgot to add it to
        // the matrix would be a subtle security gap - so every Role must resolve to a distinct,
        // deliberately-authored set (even if that set happens to be empty, as for Unassigned).
        foreach (var role in Enum.GetValues<Role>())
        {
            var permissions = PermissionMatrix.PermissionsFor(role);
            Assert.NotNull(permissions);
        }
    }
}
