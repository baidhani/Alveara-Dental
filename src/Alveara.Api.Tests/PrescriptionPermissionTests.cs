using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-016: who holds the two prescribing permissions, stated once and plainly. Prescribing is the dentist's and the administrator's; the rest of the clinical team reads; nobody else holds either.</summary>
public class PrescriptionPermissionTests
{
    [Theory]
    [InlineData(Role.Admin, true, true)]
    [InlineData(Role.Dentist, true, true)]
    [InlineData(Role.Hygienist, false, true)]
    [InlineData(Role.Assistant, false, true)]
    [InlineData(Role.FrontDesk, false, false)]
    [InlineData(Role.Billing, false, false)]
    [InlineData(Role.OfficeManager, false, false)]
    [InlineData(Role.Unassigned, false, false)]
    public void Prescribing_is_held_by_the_dentist_and_administrator_and_reading_by_the_clinical_team(Role role, bool manage, bool view)
    {
        Assert.Equal(manage, PermissionMatrix.RoleHas(role, Permission.ManagePrescriptions));
        Assert.Equal(view, PermissionMatrix.RoleHas(role, Permission.ViewPrescriptions));
    }

    [Fact]
    public void Everyone_who_reads_clinical_documentation_reads_prescriptions_and_no_one_else_does()
    {
        foreach (var role in Enum.GetValues<Role>())
            Assert.Equal(PermissionMatrix.RoleHas(role, Permission.ViewClinicalDocumentation), PermissionMatrix.RoleHas(role, Permission.ViewPrescriptions));
    }
}
