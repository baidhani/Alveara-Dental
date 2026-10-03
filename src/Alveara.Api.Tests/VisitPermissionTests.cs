using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-011-C01: who may do which visit move, as a pure table (no DB, no HTTP). The HTTP enforcement is in VisitsApiTests.</summary>
public class VisitPermissionTests
{
    [Theory]
    [InlineData(Role.FrontDesk, true, false)]
    [InlineData(Role.OfficeManager, true, true)]
    [InlineData(Role.Admin, true, true)]
    [InlineData(Role.Dentist, false, true)]
    [InlineData(Role.Hygienist, false, true)]
    [InlineData(Role.Assistant, false, true)]
    [InlineData(Role.Billing, false, false)]
    [InlineData(Role.Unassigned, false, false)]
    public void Front_office_moves_and_chairside_moves_belong_to_different_teams(Role role, bool frontOffice, bool chairside)
    {
        Assert.Equal(frontOffice, PermissionMatrix.RoleHas(role, Permission.UpdateVisitFlow));
        Assert.Equal(chairside, PermissionMatrix.RoleHas(role, Permission.UpdateChairsideFlow));
    }

    [Fact]
    public void Every_move_is_front_office_or_chairside_work_so_no_move_is_left_without_an_owner()
    {
        foreach (var state in VisitStates.InOrder.Skip(1))
        {
            var needed = VisitStateMachine.DutyFor(state) == TransitionDuty.FrontOffice ? Permission.UpdateVisitFlow : Permission.UpdateChairsideFlow;
            Assert.True(PermissionMatrix.RoleHas(Role.Admin, needed), $"Admin cannot move a visit to {state}");
            Assert.True(PermissionMatrix.RoleHas(Role.OfficeManager, needed), $"The office manager cannot move a visit to {state}");
        }
    }

    [Fact]
    public void STORY_011s_own_permission_is_unchanged_front_desk_and_office_manager_manage_appointments_and_clinicians_do_not()
    {
        foreach (var role in new[] { Role.FrontDesk, Role.OfficeManager, Role.Admin }) Assert.True(PermissionMatrix.RoleHas(role, Permission.ManageAppointments));
        foreach (var role in new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.Billing, Role.Unassigned }) Assert.False(PermissionMatrix.RoleHas(role, Permission.ManageAppointments));
    }

    [Fact]
    public void The_new_permissions_were_appended_so_no_existing_permission_changed_its_value()
    {
        // Appended means: the two sit directly after the previous last permission, in this order, and nothing before them moved. (Later stories append after them,
        // so this does not insist they are the LAST members: STORY-005 added ViewClinicalDocumentation after them.)
        Assert.Equal((int)Permission.VoidForms + 1, (int)Permission.UpdateVisitFlow);
        Assert.Equal((int)Permission.UpdateVisitFlow + 1, (int)Permission.UpdateChairsideFlow);
    }

    [Fact]
    public void Nobody_who_cannot_see_the_schedule_can_move_a_visit()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Admin))
            if (PermissionMatrix.RoleHas(role, Permission.UpdateVisitFlow) || PermissionMatrix.RoleHas(role, Permission.UpdateChairsideFlow))
                Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewSchedule), $"{role} can move a visit but cannot see the board");
    }
}
