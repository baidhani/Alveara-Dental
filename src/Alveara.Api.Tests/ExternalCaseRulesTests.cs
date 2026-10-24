using Xunit;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C02, the pure rules of referral and lab cases without a database: the status sets, the moves between them (closed sets, final statuses, the lab remake loop), which moves need a note, when a case is overdue
/// or wants its follow-up task (the due date itself is not yet overdue but already wants the task), the date limits, and the 09:00 practice-time task moment across a daylight-saving change.
/// </summary>
public class ExternalCaseRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private const string Referral = ExternalCaseKinds.Referral;
    private const string Lab = ExternalCaseKinds.Lab;

    [Fact]
    public void A_referral_has_five_statuses_and_a_lab_case_six_with_InProgress_only_for_the_lab()
    {
        Assert.Equal(new[] { "Draft", "Sent", "Received", "Completed", "Cancelled" }, ExternalCaseRules.StatusesOf(Referral));
        Assert.Equal(new[] { "Draft", "Sent", "InProgress", "Received", "Completed", "Cancelled" }, ExternalCaseRules.StatusesOf(Lab));
        Assert.False(ExternalCaseRules.IsKnown(Referral, "InProgress"));
        Assert.True(ExternalCaseRules.IsKnown(Lab, "InProgress"));
        Assert.False(ExternalCaseRules.IsKnown(Lab, "Done"));
    }

    [Theory]
    [InlineData(Referral, "Draft", "Sent,Cancelled")]
    [InlineData(Referral, "Sent", "Received,Cancelled")]
    [InlineData(Referral, "Received", "Completed,Cancelled")]
    [InlineData(Referral, "Completed", "")]
    [InlineData(Referral, "Cancelled", "")]
    [InlineData(Lab, "Draft", "Sent,Cancelled")]
    [InlineData(Lab, "Sent", "InProgress,Received,Cancelled")]
    [InlineData(Lab, "InProgress", "Received,Cancelled")]
    [InlineData(Lab, "Received", "Completed,Sent,Cancelled")]
    [InlineData(Lab, "Completed", "")]
    [InlineData(Lab, "Cancelled", "")]
    public void The_moves_from_each_status_are_exactly_these(string kind, string from, string expected)
    {
        Assert.Equal(expected, string.Join(",", ExternalCaseRules.NextStatuses(kind, from)));
    }

    [Theory]
    [InlineData(Referral)]
    [InlineData(Lab)]
    public void Every_move_stays_inside_the_known_statuses_a_final_status_has_no_move_and_a_case_can_always_reach_completion(string kind)
    {
        var all = ExternalCaseRules.StatusesOf(kind);
        foreach (var from in all)
        {
            Assert.All(ExternalCaseRules.NextStatuses(kind, from), to => Assert.Contains(to, all));
            if (ExternalCaseRules.IsFinal(from)) Assert.Empty(ExternalCaseRules.NextStatuses(kind, from));
        }
        // from every open status Completed is reachable (and Cancelled is reachable from every open status)
        foreach (var start in all.Where(ExternalCaseRules.IsOpen))
        {
            var seen = new HashSet<string> { start };
            var queue = new Queue<string>(new[] { start });
            while (queue.Count > 0) foreach (var next in ExternalCaseRules.NextStatuses(kind, queue.Dequeue())) if (seen.Add(next)) queue.Enqueue(next);
            Assert.Contains("Completed", seen);
            Assert.Contains("Cancelled", seen);
        }
    }

    [Fact]
    public void An_unknown_status_or_a_move_that_skips_a_step_is_not_allowed()
    {
        Assert.False(ExternalCaseRules.CanMove(Referral, "Draft", "Completed"));
        Assert.False(ExternalCaseRules.CanMove(Lab, "Draft", "InProgress"));
        Assert.False(ExternalCaseRules.CanMove(Referral, "Sent", "InProgress"));
        Assert.False(ExternalCaseRules.CanMove(Lab, "Completed", "Sent"));
        Assert.False(ExternalCaseRules.CanMove(Lab, "Mystery", "Sent"));
        Assert.True(ExternalCaseRules.CanMove(Lab, "Received", "Sent"));          // returned for a remake
        Assert.False(ExternalCaseRules.CanMove(Referral, "Received", "Sent"));    // only a lab case can go back
    }

    [Theory]
    [InlineData(Referral, "Draft", "Cancelled", true)]
    [InlineData(Referral, "Sent", "Cancelled", true)]
    [InlineData(Lab, "InProgress", "Cancelled", true)]
    [InlineData(Lab, "Received", "Sent", true)]
    [InlineData(Referral, "Sent", "Received", false)]
    [InlineData(Lab, "Sent", "InProgress", false)]
    [InlineData(Lab, "Received", "Completed", false)]
    [InlineData(Lab, "Draft", "Sent", false)]
    public void A_cancellation_and_a_remake_need_a_note_and_the_other_moves_do_not(string kind, string from, string to, bool required)
    {
        Assert.Equal(required, ExternalCaseRules.NoteRequired(kind, from, to));
    }

    [Theory]
    [InlineData("Draft", false)]
    [InlineData("Sent", true)]
    [InlineData("InProgress", true)]
    [InlineData("Received", true)]
    [InlineData("Completed", false)]
    [InlineData("Cancelled", false)]
    public void Only_a_case_that_has_gone_out_and_is_not_finished_is_tracked_against_its_due_date(string status, bool tracked)
    {
        Assert.Equal(tracked, ExternalCaseRules.Tracks(status));
        Assert.Equal(!ExternalCaseRules.IsFinal(status), ExternalCaseRules.IsOpen(status));
    }

    [Fact]
    public void A_case_is_overdue_the_day_after_its_due_date_but_wants_its_task_on_the_due_date_itself()
    {
        var yesterday = Today.AddDays(-1);
        var tomorrow = Today.AddDays(1);
        Assert.True(ExternalCaseRules.IsOverdue("Sent", yesterday, Today));
        Assert.False(ExternalCaseRules.IsOverdue("Sent", Today, Today));        // due today is not yet late
        Assert.False(ExternalCaseRules.IsOverdue("Sent", tomorrow, Today));
        Assert.True(ExternalCaseRules.WantsTask("Sent", Today, Today));         // but the task for it is due today
        Assert.True(ExternalCaseRules.WantsTask("Received", yesterday, Today));
        Assert.False(ExternalCaseRules.WantsTask("Sent", tomorrow, Today));
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public void A_draft_and_a_finished_case_are_never_overdue_and_never_want_a_task_however_old_the_due_date(string status)
    {
        var longAgo = Today.AddDays(-300);
        Assert.False(ExternalCaseRules.IsOverdue(status, longAgo, Today));
        Assert.False(ExternalCaseRules.WantsTask(status, longAgo, Today));
    }

    [Fact]
    public void A_case_without_a_due_date_is_never_overdue_and_never_wants_a_task()
    {
        Assert.False(ExternalCaseRules.IsOverdue("Sent", null, Today));
        Assert.False(ExternalCaseRules.WantsTask("Sent", null, Today));
    }

    [Fact]
    public void A_due_date_may_be_up_to_a_year_back_and_two_years_ahead_and_no_further()
    {
        Assert.True(ExternalCaseRules.DueDateInRange(Today.AddDays(-365), Today));
        Assert.False(ExternalCaseRules.DueDateInRange(Today.AddDays(-366), Today));
        Assert.True(ExternalCaseRules.DueDateInRange(Today.AddYears(2), Today));
        Assert.False(ExternalCaseRules.DueDateInRange(Today.AddYears(2).AddDays(1), Today));
        Assert.True(ExternalCaseRules.DueDateInRange(Today, Today));
    }

    [Fact]
    public void A_date_a_case_went_out_or_came_back_is_not_in_the_future_and_not_before_2000()
    {
        Assert.True(ExternalCaseRules.EventDateValid(Today, Today));
        Assert.False(ExternalCaseRules.EventDateValid(Today.AddDays(1), Today));
        Assert.True(ExternalCaseRules.EventDateValid(new DateOnly(2000, 1, 1), Today));
        Assert.False(ExternalCaseRules.EventDateValid(new DateOnly(1999, 12, 31), Today));
    }

    [Fact]
    public void The_follow_up_task_is_due_at_nine_in_the_morning_practice_time_across_a_daylight_saving_change()
    {
        var clock = PatientTestSupport.Clock;       // Chicago in these tests
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 14, 0, 0, TimeSpan.Zero), ExternalCaseRules.TaskDueAt(new DateOnly(2026, 10, 30), clock));   // CDT, UTC-5
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero), ExternalCaseRules.TaskDueAt(new DateOnly(2026, 11, 3), clock));      // CST after the change, UTC-6
    }

    [Fact]
    public void A_referral_goes_to_a_referral_provider_and_a_lab_case_to_a_lab_vendor()
    {
        Assert.Equal(ExternalPartyKinds.ReferralProvider, ExternalCaseKinds.PartyKindFor(Referral));
        Assert.Equal(ExternalPartyKinds.LabVendor, ExternalCaseKinds.PartyKindFor(Lab));
        Assert.Equal(new[] { "Referral", "Lab" }, ExternalCaseKinds.All);
        Assert.Equal(new[] { "ReferralProvider", "LabVendor" }, ExternalPartyKinds.All);
    }

    [Fact]
    public void The_text_limits_are_positive_and_ordered_sensibly()
    {
        Assert.All(new[] { ExternalCaseRules.NoteMaxLength, ExternalCaseRules.ReasonMaxLength, ExternalCaseRules.PartyNameMaxLength, ExternalCaseRules.ContactMaxLength, ExternalCaseRules.MaterialMaxLength,
                           ExternalCaseRules.ShadeMaxLength, ExternalCaseRules.TitleMaxLength }, n => Assert.InRange(n, 1, 4000));
    }
}

/// <summary>ALV-009-C02: who holds the referral and lab permissions by default, and that none of them is protected so the policy editor can grant, revoke and delegate them.</summary>
public class ExternalCasePermissionTests
{
    [Theory]
    [InlineData(Role.Admin, true, true, true)]
    [InlineData(Role.OfficeManager, true, true, true)]
    [InlineData(Role.Dentist, true, true, false)]
    [InlineData(Role.FrontDesk, true, true, false)]
    [InlineData(Role.Hygienist, true, false, false)]
    [InlineData(Role.Assistant, true, false, false)]
    [InlineData(Role.Billing, false, false, false)]
    [InlineData(Role.Unassigned, false, false, false)]
    public void The_default_holders_of_the_view_the_manage_and_the_party_permissions(Role role, bool view, bool manage, bool parties)
    {
        Assert.Equal(view, PermissionMatrix.RoleHas(role, Permission.ViewExternalCases));
        Assert.Equal(manage, PermissionMatrix.RoleHas(role, Permission.ManageExternalCases));
        Assert.Equal(parties, PermissionMatrix.RoleHas(role, Permission.ManageExternalParties));
    }

    [Fact]
    public void Nobody_can_manage_cases_without_being_able_to_view_them()
    {
        foreach (var role in Enum.GetValues<Role>())
            if (PermissionMatrix.RoleHas(role, Permission.ManageExternalCases)) Assert.True(PermissionMatrix.RoleHas(role, Permission.ViewExternalCases), role.ToString());
    }

    [Fact]
    public void None_of_the_three_is_protected_so_each_can_be_granted_revoked_and_delegated()
    {
        foreach (var p in new[] { Permission.ViewExternalCases, Permission.ManageExternalCases, Permission.ManageExternalParties })
        {
            Assert.True(PermissionProtection.IsGrantable(p), p.ToString());
            Assert.True(PermissionProtection.IsDelegable(p), p.ToString());
            Assert.DoesNotContain(p, PermissionProtection.AdminAssignableNeverDelegable);
        }
    }
}
