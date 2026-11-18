using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Treatment;
using static Alveara.Api.Architecture.Treatment.TreatmentPlanStateRules;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N023 (owner decisions PD-14, PD-15, DR-5, DR-8; review BRIEFING-R01 CR-3/CR-4; briefing revision 2 sections 2.1 and 4.6): the one derived plan-state definition (every matrix row and the
/// listed mixtures); a Completed or Closed plan refuses every change - including Withdraw - except a deliberate "Revise anyway"; a target date needs a date, a past one needs confirmation and is
/// shown overdue; a rename needs a reason; "Adopt earlier completions" is offered only with an old-style completion; the performed-after-encounter note counts practice days.
/// </summary>
public class TreatmentPlanStateTests : TreatmentPlanTestBase
{
    private static ItemFacts F(string acceptance = ItemAcceptance.Pending, string completion = ItemCompletion.NotStarted, bool withdrawn = false) => new(withdrawn, acceptance, completion);
    private static readonly ItemFacts Unanswered = F(), Accepted = F(ItemAcceptance.Accepted), Declined = F(ItemAcceptance.Declined), Done = F(ItemAcceptance.Accepted, ItemCompletion.Completed),
        Gone = F(ItemAcceptance.Accepted, withdrawn: true);

    public static TheoryData<string, string, ItemFacts[]> Matrix => new()
    {
        { "row 1: withdrawn plan", PlanStates.Withdrawn, [Accepted] },
        { "row 2: empty plan (all withdrawn)", PlanStates.Closed, [Gone, Gone] },
        { "row 3: every remaining completed", PlanStates.Completed, [Done, Done] },
        { "row 3: completed + withdrawn only", PlanStates.Completed, [Done, Gone] },
        { "row 4: completed + declined", PlanStates.Closed, [Done, Declined] },
        { "row 4: all declined", PlanStates.Closed, [Declined, Declined] },
        { "row 4: declined + withdrawn", PlanStates.Closed, [Declined, Gone] },
        { "row 5: accepted, no link or target", PlanStates.InProgress, [Accepted] },
        { "row 5: completed while another is unanswered", PlanStates.InProgress, [Done, Unanswered] },
        { "row 5: completed while another is accepted", PlanStates.InProgress, [Done, Accepted] },
        { "row 5: accepted + declined", PlanStates.InProgress, [Accepted, Declined] },
        { "row 6: all unanswered", PlanStates.Proposed, [Unanswered, Unanswered] },
        { "row 6: unanswered + declined", PlanStates.Proposed, [Unanswered, Declined] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void The_derived_state_follows_the_matrix(string row, string expected, ItemFacts[] items)
    {
        var status = row.StartsWith("row 1") ? TreatmentPlanStatuses.Withdrawn : TreatmentPlanStatuses.Proposed;
        Assert.Equal(expected, Derive(status, items));
        // owner addendum 01: read-only only when finished AND it contains completed care
        Assert.Equal(expected is PlanStates.Completed or PlanStates.Closed && items.Any(i => !i.Withdrawn && i.Completion == ItemCompletion.Completed), IsLocked(expected, items));
    }

    private async Task<PlanView> OneItemPlanAsync(string code = "LOCAL-STATE1")
    {
        var proc = await ProcedureAsync(code: $"{code}{Guid.NewGuid():N}"[..14]);
        var plan = await CreateAsync([Item(await DiagnosisAsync(), proc.Summary.Id)]);
        return await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
    }

    private static async Task<string> Code(Func<Task> act) => (await Assert.ThrowsAsync<TreatmentPlanException>(act)).Code;

    [Fact]
    public void A_closed_plan_says_why()
    {
        Assert.Equal(ClosedReasons.NoProcedures, ClosedReason(PlanStates.Closed, [Gone]));
        Assert.Equal(ClosedReasons.AllDeclined, ClosedReason(PlanStates.Closed, [Declined, Gone]));
        Assert.Equal(ClosedReasons.CompletedOrDeclined, ClosedReason(PlanStates.Closed, [Done, Declined]));
        Assert.Null(ClosedReason(PlanStates.InProgress, [Accepted]));
    }

    [Fact]
    public async Task An_empty_plan_is_closed_but_stays_correctable_add_rename_and_withdraw()
    {
        var plan = await OneItemPlanAsync();
        var empty = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Wrong procedure", plan.RowVersion, S.Actor, default));
        Assert.Equal((PlanStates.Closed, ClosedReasons.NoProcedures, false), (empty.PlanState, empty.ClosedReason, empty.Locked));
        var proc = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var corrected = await Svc(s => s.AddItemAsync(empty.Id, Item(plan.Items[0].DiagnosisId, proc.Summary.Id, key: "add-right-1"), empty.RowVersion, S.Actor, default));   // the right one added
        Assert.Equal(PlanStates.Proposed, corrected.PlanState);
        var again = await Svc(s => s.WithdrawItemAsync(plan.Id, corrected.Items[1].Id, "Not needed after all", corrected.RowVersion, S.Actor, default));
        var renamed = await Svc(s => s.RenameAsync(again.Id, "Empty plan", "Kept for the record", again.RowVersion, S.Actor, default));
        var withdrawn = await Svc(s => s.WithdrawAsync(renamed.Id, "Nothing to do", renamed.RowVersion, S.Actor, default));
        Assert.Equal(PlanStates.Withdrawn, withdrawn.PlanState);
    }

    [Fact]
    public async Task An_all_declined_plan_is_closed_but_the_patient_can_change_their_mind_and_it_can_be_withdrawn()
    {
        var proc = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var plan = await CreateAsync([Item(await DiagnosisAsync(), proc.Summary.Id)]);
        var declined = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Declined", "Too costly", null, plan.RowVersion, S.Actor, default));
        Assert.Equal((PlanStates.Closed, ClosedReasons.AllDeclined, false), (declined.PlanState, declined.ClosedReason, declined.Locked));
        var reaccepted = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, declined.RowVersion, S.Actor, default));
        Assert.Equal(PlanStates.InProgress, reaccepted.PlanState);                                                        // back to In progress
        var again = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Declined", "Decided against it", null, reaccepted.RowVersion, S.Actor, default));
        Assert.Equal(PlanStates.Withdrawn, (await Svc(s => s.WithdrawAsync(plan.Id, "Patient declined everything", again.RowVersion, S.Actor, default))).PlanState);
    }

    [Fact]
    public async Task A_completed_plan_refuses_every_change_including_withdraw_and_takes_only_a_deliberate_revise_anyway()
    {
        var plan = await OneItemPlanAsync();
        Assert.Equal(PlanStates.InProgress, plan.PlanState);
        var done = await LegacyComplete(plan.Id, plan.Items[0].Id);
        Assert.Equal((PlanStates.Completed, true, true), (done.PlanState, done.HasAdoptable, done.Locked));                // completed the old way: adoptable
        var item = done.Items[0].Id;
        var proc = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);

        var refusals = new (string What, Func<Task> Act)[]
        {
            ("withdraw plan (CR-3)", () => Svc(s => s.WithdrawAsync(done.Id, "Wrong plan", done.RowVersion, S.Actor, default))),
            ("rename", () => Svc(s => s.RenameAsync(done.Id, "New title", "Clearer", done.RowVersion, S.Actor, default))),
            ("add procedure", async () => await Svc(s => s.AddItemAsync(done.Id, Item(done.Items[0].DiagnosisId, proc.Summary.Id), done.RowVersion, S.Actor, default))),
            ("reference code", () => Svc(s => s.SetReferenceCodeAsync(done.Id, "plan-x", done.RowVersion, S.Actor, default))),
            ("answer", () => Svc(s => s.DecideItemAsync(done.Id, item, "Declined", "x", null, done.RowVersion, S.Actor, default))),
            ("bulk answer", () => Svc(s => s.DecidePlanAsync(done.Id, "Accepted", null, null, done.RowVersion, S.Actor, default))),
            ("target date", () => Svc(s => s.ScheduleItemAsync(done.Id, item, true, new DateOnly(2030, 5, 20), S.Actor, default))),
            ("withdraw item", () => Svc(s => s.WithdrawItemAsync(done.Id, item, "x", done.RowVersion, S.Actor, default))),
            ("link", async () => await Svc(s => s.LinkAppointmentAsync(done.Id, item, Guid.NewGuid(), S.Actor, default))),
            ("revise without the flag", () => Svc(s => s.ReviseAsync(done.Id, new ReviseInput("Typo", "Corrected wording", null, null, null), done.RowVersion, S.Actor, default))),
        };
        foreach (var (what, act) in refusals) Assert.True(await Code(act) == "plan_finished", what);
        Assert.Equal(TreatmentPlanStatuses.Proposed, (await Svc(s => s.GetAsync(done.Id, default))).Status);              // nothing changed

        var revised = await Svc(s => s.ReviseAsync(done.Id, new ReviseInput("Typo", "Corrected wording", null, null, null), done.RowVersion, S.Actor, default, reviseAnyway: true));
        Assert.Equal((done.CurrentRevision + 1, PlanStates.Completed), (revised.CurrentRevision, revised.PlanState));     // the completed care is untouched
    }

    [Fact]
    public async Task A_closed_plan_completed_and_declined_also_refuses_withdraw()
    {
        var proc1 = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var proc2 = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var dx = await DiagnosisAsync();
        var plan = await CreateAsync([Item(dx, proc1.Summary.Id), Item(dx, proc2.Summary.Id)]);
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[1].Id, "Declined", "Too costly", null, plan.RowVersion, S.Actor, default));
        var closed = await LegacyComplete(plan.Id, plan.Items[0].Id);
        Assert.Equal((PlanStates.Closed, ClosedReasons.CompletedOrDeclined, true), (closed.PlanState, closed.ClosedReason, closed.Locked));
        Assert.Equal("plan_finished", await Code(() => Svc(s => s.WithdrawAsync(closed.Id, "x", closed.RowVersion, S.Actor, default))));
    }

    [Fact]
    public async Task A_target_date_is_required_a_past_one_needs_confirmation_and_is_overdue_and_it_is_never_a_booking()
    {
        var plan = await OneItemPlanAsync();
        var item = plan.Items[0].Id;
        Assert.Equal("plannedDate", Assert.Single((await Assert.ThrowsAsync<TreatmentPlanException>(() => Svc(s => s.ScheduleItemAsync(plan.Id, item, true, null, S.Actor, default)))).FieldErrors).Key);
        var past = DateOnly.FromDateTime(SchedulingTestSupport.Clock.ToPracticeLocal(SchedulingTestSupport.Clock.UtcNow).DateTime).AddDays(-7);
        var refused = await Assert.ThrowsAsync<TreatmentPlanException>(() => Svc(s => s.ScheduleItemAsync(plan.Id, item, true, past, S.Actor, default)));
        Assert.Equal(("past_target_date", "plannedDate"), (refused.Code, refused.FieldErrors.Keys.Single()));
        var overdue = await Svc(s => s.ScheduleItemAsync(plan.Id, item, true, past, S.Actor, default, confirmPast: true));
        Assert.Equal((past, true, (ItemAppointmentView?)null), (overdue.Items[0].PlannedDate, overdue.Items[0].TargetOverdue, overdue.Items[0].Appointment));
        await using var db = Fixture.CreateContext();
        Assert.Contains("(overdue, confirmed)", (await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "TreatmentPlanItemTargetDateSet")).Details);
        var cleared = await Svc(s => s.ScheduleItemAsync(plan.Id, item, false, null, S.Actor, default));
        Assert.Equal(((DateOnly?)null, false), (cleared.Items[0].PlannedDate, cleared.Items[0].TargetOverdue));
    }

    [Fact]
    public async Task A_rename_needs_a_reason_and_keeps_it_in_the_history_and_revision()
    {
        var plan = await OneItemPlanAsync();
        var e = await Assert.ThrowsAsync<TreatmentPlanException>(() => Svc(s => s.RenameAsync(plan.Id, "Gum care", " ", plan.RowVersion, S.Actor, default)));
        Assert.Equal(("reason_required", "reason"), (e.Code, e.FieldErrors.Keys.Single()));
        var renamed = await Svc(s => s.RenameAsync(plan.Id, "Gum care", "The patient asked for plainer words", plan.RowVersion, S.Actor, default));
        Assert.Equal(plan.CurrentRevision + 1, renamed.CurrentRevision);
        var history = await Svc(s => s.HistoryAsync(plan.Id, default));
        Assert.Equal(("Gum care", "The patient asked for plainer words"), (history[^1].Title, history[^1].Reason));
        var revisions = await Svc(s => s.RevisionsAsync(plan.Id, default));
        Assert.Equal("The patient asked for plainer words", revisions[^1].Reason);
    }

    [Fact]
    public async Task Adopt_is_offered_only_with_an_old_style_completion_and_the_unanswered_count_is_reported()
    {
        var proc = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var plan = await CreateAsync([Item(await DiagnosisAsync(), proc.Summary.Id)]);
        Assert.Equal((false, 1, PlanStates.Proposed), (plan.HasAdoptable, plan.UnansweredCount, plan.PlanState));
    }

    [Fact]
    public void The_performed_after_encounter_note_counts_practice_days_and_is_absent_when_not_after()
    {
        var clock = SchedulingTestSupport.Clock;
        var encounter = new DateTimeOffset(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, CompletionDates.DaysAfter(clock, encounter.AddDays(2), encounter));
        Assert.Null(CompletionDates.DaysAfter(clock, encounter.AddHours(2), encounter));                                   // same practice day
        Assert.Null(CompletionDates.DaysAfter(clock, encounter.AddDays(-1), encounter));                                   // before: no note
        Assert.Null(CompletionDates.DaysAfter(clock, encounter, default));                                                 // unknown encounter
    }
}
