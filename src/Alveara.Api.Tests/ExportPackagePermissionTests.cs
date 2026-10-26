using System.Reflection;
using Xunit;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, who may take what out of the system, without a database: the bulk export and the stand-alone readable record are held by the administrator alone by default, are ordinary grantable permissions (not one of the
/// permanently Admin-only security permissions), are registered as active, and the new audit event names are distinct, so an export can be traced to the right kind of event.
/// </summary>
public class ExportPackagePermissionTests
{
    private static readonly Permission[] NewPermissions = [Permission.ExportAllPatientData, Permission.ExportPatientReadableRecord];

    [Fact]
    public void The_bulk_export_and_the_readable_record_are_held_by_the_administrator_alone_by_default()
    {
        foreach (var permission in NewPermissions)
            foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
                Assert.Equal(role == Role.Admin, PermissionMatrix.RoleHas(role, permission));
        Assert.Empty(PermissionMatrix.PermissionsFor(Role.Unassigned).Intersect(NewPermissions));
    }

    [Fact]
    public void Both_can_be_granted_to_another_role_because_neither_is_a_protected_permission()
    {
        foreach (var permission in NewPermissions) Assert.True(PermissionProtection.IsGrantable(permission), permission.ToString());
    }

    [Fact]
    public void Both_are_registered_as_active_and_a_one_patient_package_needs_neither_of_them()
    {
        foreach (var permission in NewPermissions) Assert.True(PermissionRegistry.IsActive(permission.ToString()), permission.ToString());
        Assert.NotEqual(Permission.ExportPatientData, Permission.ExportAllPatientData);                        // three separate permissions: one patient's package, every patient's, one patient's readable record
        Assert.NotEqual(Permission.ExportPatientData, Permission.ExportPatientReadableRecord);
    }

    [Fact]
    public void The_new_audit_event_names_are_distinct_and_none_repeats_an_existing_event()
    {
        string[] added = [AuditEventTypes.PatientDataPackageExported, AuditEventTypes.AllPatientDataPackageExported, AuditEventTypes.PatientReadableRecordExported, AuditEventTypes.DataExportFailed];
        var all = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(added.Length, added.Distinct().Count());
        Assert.All(added, name => Assert.Single(all, name));                                                  // each appears once among every event name
    }
}
