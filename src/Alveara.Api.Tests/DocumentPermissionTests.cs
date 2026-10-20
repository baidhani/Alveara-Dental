using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-010: who holds the three document permissions, stated once and plainly (docs/DOCUMENTS.md, "Permissions"). Importing and editing are the administrator's and the office manager's; reading follows the roles that work with a patient's file; settings are the administrator's.</summary>
public class DocumentPermissionTests
{
    [Theory]
    [InlineData(Role.Admin, true, true, true)]
    [InlineData(Role.OfficeManager, true, true, false)]
    [InlineData(Role.FrontDesk, false, true, false)]
    [InlineData(Role.Dentist, false, true, false)]
    [InlineData(Role.Hygienist, false, true, false)]
    [InlineData(Role.Assistant, false, true, false)]
    [InlineData(Role.Billing, false, true, false)]
    [InlineData(Role.Unassigned, false, false, false)]
    public void Documents_are_imported_by_the_administrator_and_office_manager_read_by_the_staff_who_work_with_a_file_and_configured_by_the_administrator(Role role, bool import, bool view, bool settings)
    {
        Assert.Equal(import, PermissionMatrix.RoleHas(role, Permission.ImportDocuments));
        Assert.Equal(view, PermissionMatrix.RoleHas(role, Permission.ViewDocuments));
        Assert.Equal(settings, PermissionMatrix.RoleHas(role, Permission.ManageDocumentSettings));
    }

    [Fact]
    public void Nobody_can_import_a_document_without_being_able_to_view_it()
    {
        foreach (var role in Enum.GetValues<Role>())
            if (PermissionMatrix.RoleHas(role, Permission.ImportDocuments)) Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewDocuments));
    }
}
