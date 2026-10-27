using System.Reflection;
using Xunit;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02, who may bulk import, without a database: BulkImportData is held by the administrator alone by default, is an ordinary grantable permission (not one of the permanently Admin-only security
/// permissions), is registered as active, is separate from STORY-018's one-patient ImportPatientData (which the office manager keeps), and the two new audit event names are distinct from every other event name.
/// </summary>
public class BulkImportPermissionTests
{
    [Fact]
    public void Bulk_import_is_held_by_the_administrator_alone_by_default()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
            Assert.Equal(role == Role.Admin, PermissionMatrix.RoleHas(role, Permission.BulkImportData));
        Assert.DoesNotContain(Permission.BulkImportData, PermissionMatrix.PermissionsFor(Role.Unassigned));
    }

    [Fact]
    public void It_can_be_granted_to_another_role_because_it_is_not_a_protected_permission()
    {
        Assert.True(PermissionProtection.IsGrantable(Permission.BulkImportData));
    }

    [Fact]
    public void It_is_registered_as_active_and_is_not_the_one_patient_import_permission()
    {
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.BulkImportData)));
        Assert.NotEqual(Permission.ImportPatientData, Permission.BulkImportData);
        Assert.True(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.ImportPatientData));          // STORY-018's import stays as it was
        Assert.False(PermissionMatrix.RoleHas(Role.OfficeManager, Permission.BulkImportData));
    }

    [Fact]
    public void The_new_audit_event_names_are_distinct_and_none_repeats_an_existing_event()
    {
        string[] added = [AuditEventTypes.ImportBatchCreated, AuditEventTypes.ImportFileRejected];
        var all = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(added.Length, added.Distinct().Count());
        Assert.All(added, name => Assert.Single(all, name));
    }
}
