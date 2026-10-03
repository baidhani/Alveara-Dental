using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-005: who may read and who may write a patient's clinical documentation. Reading is its own permission so that the permission front desk and billing hold to
/// find a patient (ViewPatientRecords) never opens a medical history; writing stays with ManageClinicalNotes.
/// </summary>
public class ClinicalPermissionTests
{
    [Theory]
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Dentist, true)]
    [InlineData(Role.Hygienist, true)]
    [InlineData(Role.Assistant, true)]
    [InlineData(Role.FrontDesk, false)]
    [InlineData(Role.Billing, false)]
    [InlineData(Role.OfficeManager, false)]
    [InlineData(Role.Unassigned, false)]
    public void Clinical_documentation_can_be_read_only_by_the_clinical_team_and_the_administrator(Role role, bool expected) =>
        Assert.Equal(expected, PermissionMatrix.RoleHas(role, Permission.ViewClinicalDocumentation));

    [Theory]
    [InlineData(Role.Admin, true)]
    [InlineData(Role.Dentist, true)]
    [InlineData(Role.Hygienist, true)]
    [InlineData(Role.Assistant, false)]
    [InlineData(Role.FrontDesk, false)]
    [InlineData(Role.Billing, false)]
    [InlineData(Role.OfficeManager, false)]
    [InlineData(Role.Unassigned, false)]
    public void Clinical_documentation_can_be_written_only_by_the_roles_that_hold_ManageClinicalNotes(Role role, bool expected) =>
        Assert.Equal(expected, PermissionMatrix.RoleHas(role, Permission.ManageClinicalNotes));

    [Fact]
    public void Every_role_that_may_write_clinical_documentation_may_also_read_it()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => PermissionMatrix.RoleHas(r, Permission.ManageClinicalNotes)))
            Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewClinicalDocumentation), $"{role} can write clinical notes but not read them");
    }

    [Fact]
    public void The_permission_that_front_desk_and_billing_use_to_find_patients_does_not_open_the_medical_history()
    {
        foreach (var role in new[] { Role.FrontDesk, Role.Billing })
        {
            Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewPatientRecords));
            Assert.False(PermissionMatrix.RoleHas(role, Permission.ViewClinicalDocumentation));
        }
    }

    [Fact]
    public void The_new_permission_was_appended_so_no_existing_permission_changed_its_ordinal()
    {
        // Appended means: it sits directly after the previous last permission and nothing before it moved (a later story may append after it).
        Assert.Equal((int)Permission.UpdateChairsideFlow + 1, (int)Permission.ViewClinicalDocumentation);
    }
}
