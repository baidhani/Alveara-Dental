using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-010, ALV-N026: who holds the document permissions, stated once and plainly (docs/DOCUMENTS.md, "Permissions"). Importing is the administrator's, the office manager's and (ALV-N026 U-008) the dentist's;
/// editing, inactivating and reactivating need ManageDocuments (ALV-N026 PD-24), held by the administrator and the office manager only; reading follows the roles that work with a patient's file (a Clinical
/// document also needs ViewClinicalDocumentation); settings are the administrator's.
/// </summary>
public class DocumentPermissionTests
{
    [Theory]
    [InlineData(Role.Admin, true, true, true, true)]
    [InlineData(Role.OfficeManager, true, true, false, true)]
    [InlineData(Role.FrontDesk, false, true, false, false)]
    [InlineData(Role.Dentist, true, true, false, false)]
    [InlineData(Role.Hygienist, false, true, false, false)]
    [InlineData(Role.Assistant, false, true, false, false)]
    [InlineData(Role.Billing, false, true, false, false)]
    [InlineData(Role.Unassigned, false, false, false, false)]
    public void Documents_are_imported_by_the_administrator_office_manager_and_dentist_managed_by_the_administrator_and_office_manager_and_configured_by_the_administrator(
        Role role, bool import, bool view, bool settings, bool manage)
    {
        Assert.Equal(import, PermissionMatrix.RoleHas(role, Permission.ImportDocuments));
        Assert.Equal(view, PermissionMatrix.RoleHas(role, Permission.ViewDocuments));
        Assert.Equal(settings, PermissionMatrix.RoleHas(role, Permission.ManageDocumentSettings));
        Assert.Equal(manage, PermissionMatrix.RoleHas(role, Permission.ManageDocuments));
    }

    [Theory]
    [InlineData(Role.Admin, true)]
    [InlineData(Role.OfficeManager, true)]
    [InlineData(Role.FrontDesk, false)]
    [InlineData(Role.Dentist, false)]
    [InlineData(Role.Hygienist, false)]
    [InlineData(Role.Assistant, false)]
    [InlineData(Role.Billing, false)]
    [InlineData(Role.Unassigned, false)]
    public void The_storage_check_and_the_review_of_unreferenced_files_are_held_by_the_administrator_and_office_manager(Role role, bool held)
    {
        Assert.Equal(held, PermissionMatrix.RoleHas(role, Permission.ManageDocumentStorage));
    }

    [Fact]
    public void Nobody_can_import_or_manage_a_document_without_being_able_to_view_documents()
    {
        foreach (var role in Enum.GetValues<Role>())
        {
            if (PermissionMatrix.RoleHas(role, Permission.ImportDocuments)) Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewDocuments));
            if (PermissionMatrix.RoleHas(role, Permission.ManageDocuments)) Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewDocuments));
        }
    }

    /// <summary>ALV-N026 (PD-24): importing does not imply managing for any role by default.</summary>
    [Fact]
    public void ImportDocuments_does_not_imply_ManageDocuments()
    {
        Assert.True(PermissionMatrix.RoleHas(Role.Dentist, Permission.ImportDocuments));
        Assert.False(PermissionMatrix.RoleHas(Role.Dentist, Permission.ManageDocuments));
    }
}
