using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N015: the four permissions of the notification center in the fixed role table. Reading, marking read and hiding one's own notifications and messages needs no permission, so none is listed for it.
/// Priority is not permission-limited (design decision 3), so there is no <c>SendUrgentMessages</c> permission.
/// </summary>
public class NotificationPermissionTests
{
    private static readonly Role[] Working = [Role.Dentist, Role.Hygienist, Role.Assistant, Role.FrontDesk, Role.Billing, Role.OfficeManager, Role.Admin];

    [Fact]
    public void Every_working_role_may_send_messages_and_a_self_registered_account_may_not()
    {
        foreach (var role in Working) Assert.True(PermissionMatrix.RoleHas(role, Permission.SendMessages), role.ToString());
        Assert.False(PermissionMatrix.RoleHas(Role.Unassigned, Permission.SendMessages));
    }

    [Fact]
    public void Only_the_administrator_and_the_office_manager_are_told_about_a_duplicate_document()
    {
        var holders = Enum.GetValues<Role>().Where(r => PermissionMatrix.RoleHas(r, Permission.ReceiveDocumentNotifications)).ToHashSet();
        Assert.Equal(new HashSet<Role> { Role.Admin, Role.OfficeManager }, holders);
    }

    [Theory]
    [InlineData(Permission.ViewNotificationAuditHistory)]
    [InlineData(Permission.ViewMessageAuditContent)]
    public void The_notification_history_and_the_break_glass_message_view_are_the_administrators_alone(Permission permission)
    {
        var holders = Enum.GetValues<Role>().Where(r => PermissionMatrix.RoleHas(r, permission)).ToList();
        Assert.Equal([Role.Admin], holders);
    }

    [Fact]
    public void No_permission_limits_who_may_choose_Urgent()
    {
        Assert.DoesNotContain(Enum.GetNames<Permission>(), n => n.Contains("Urgent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_new_permissions_are_appended_after_the_existing_ones_so_no_stored_ordinal_moves()
    {
        var names = Enum.GetNames<Permission>().ToList();
        Assert.True(names.IndexOf(nameof(Permission.SendMessages)) > names.IndexOf(nameof(Permission.ManageDocumentStorage)));
        Assert.Equal([nameof(Permission.SendMessages), nameof(Permission.ReceiveDocumentNotifications), nameof(Permission.ViewNotificationAuditHistory), nameof(Permission.ViewMessageAuditContent)],
            names.Skip(names.IndexOf(nameof(Permission.ManageDocumentStorage)) + 1).Take(4));
    }
}
