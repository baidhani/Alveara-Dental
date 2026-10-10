using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 against real SQL Server: a patient's answer and what follows. A procedure is accepted or declined (a decline says why), with the staff member who entered it and, if given, who gave the answer; the
/// whole plan can be accepted or declined at once but never by overwriting an opposite decision; accepted procedures are scheduled and completed; every change appends a state row, a history entry and a PHI-free
/// audit entry in one save and nothing is ever rewritten; repeats are quiet, stale answers are conflicts, and a completed procedure is final.
/// </summary>
public class TreatmentPlanStateServiceTests : TreatmentPlanTestBase
{
    private Task<PlanView> Decide(PlanView plan, int item, string decision, string? reason = null, string? by = null, string? version = null) =>
        Svc(s => s.DecideItemAsync(plan.Id, plan.Items[item].Id, decision, reason, by, version ?? plan.RowVersion, S.Actor, default));

    private Task<PlanView> DecidePlan(PlanView plan, string decision, string? reason = null, string? by = null, string? version = null) =>
        Svc(s => s.DecidePlanAsync(plan.Id, decision, reason, by, version ?? plan.RowVersion, S.Actor, default));

    private Task<PlanView> Schedule(PlanView plan, int item, bool scheduled, DateOnly? date = null) => Svc(s => s.ScheduleItemAsync(plan.Id, plan.Items[item].Id, scheduled, date, S.Actor, default));
    private Task<PlanView> Complete(PlanView plan, int item) => Svc(s => s.CompleteItemAsync(plan.Id, plan.Items[item].Id, S.Actor, default));

    private async Task<PlanView> ThreeProcedurePlanAsync()
    {
        var diagnosis = await DiagnosisAsync();
        var a = await ProcedureAsync("LOCAL-100", fee: 100m);
        var b = await ProcedureAsync("LOCAL-200", fee: 60m, description: "Scaling");
        var c = await ProcedureAsync("LOCAL-300", fee: 40m, description: "Polish");
        return await CreateAsync([Item(diagnosis, a.Summary.Id), Item(diagnosis, b.Summary.Id), Item(diagnosis, c.Summary.Id)]);
    }

    private Task<int> StateRowsAsync() => WithDb(db => db.TreatmentPlanItemStates.CountAsync());

    // ---------- accepting and declining one procedure ----------

    [Fact]
    public async Task Accepting_records_who_entered_it_and_who_answered_the_revision_shown_and_a_history_and_audit_entry()
    {
        var plan = await ThreeProcedurePlanAsync();
        Assert.Equal(("Pending", 0m), (plan.AcceptanceStatus, plan.AcceptedEstimateTotal));

        var accepted = await Decide(plan, 0, "Accepted", by: "  Ann   Alder (patient) ");
        var item = accepted.Items[0];
        Assert.Equal(("Accepted", "Ann Alder (patient)", "Unscheduled", "NotStarted", 1), (item.Acceptance, item.DecidedByName, item.Scheduling, item.Completion, item.DecisionRevision));
        Assert.False(string.IsNullOrWhiteSpace(item.RecordedByName));
        Assert.NotNull(item.RecordedAtUtc);
        Assert.Equal(("Partial", 100m, 200m), (accepted.AcceptanceStatus, accepted.AcceptedEstimateTotal, accepted.EstimateTotal));
        Assert.Equal(["Pending", "Pending"], accepted.Items.Skip(1).Select(i => i.Acceptance).ToArray());
        Assert.Equal(plan.RowVersion, accepted.RowVersion);                                                      // a decision is not a change to the plan's content

        var history = await Svc(s => s.HistoryAsync(plan.Id, default));
        Assert.Equal(("ItemAccepted", plan.Items[0].Id, 1), (history[^1].ChangeType, history[^1].ItemId, history[^1].RevisionNumber));
        var audit = await WithDb(db => db.AuditLogEntries.Where(a => a.EventType == "TreatmentPlanItemAccepted").ToListAsync());
        var entry = Assert.Single(audit);
        Assert.DoesNotContain("Ann", entry.Details ?? "");                                                    // the patient's name is not in the log
    }

    [Fact]
    public async Task Declining_needs_a_reason_and_a_decision_can_be_changed_later_with_every_step_kept()
    {
        var plan = await ThreeProcedurePlanAsync();
        var refused = await Refused(() => Decide(plan, 0, "Declined"));
        Assert.Equal("Say why it was declined.", refused.FieldErrors["reason"]);
        Assert.Equal(0, await StateRowsAsync());

        var declined = await Decide(plan, 0, "declined", reason: "Cannot afford it now");
        Assert.Equal(("Declined", "Cannot afford it now"), (declined.Items[0].Acceptance, declined.Items[0].DecisionReason));
        var changed = await Decide(plan, 0, "Accepted", by: "Ann Alder");
        Assert.Equal(("Accepted", null), (changed.Items[0].Acceptance, changed.Items[0].DecisionReason));

        var rows = await WithDb(db => db.TreatmentPlanItemStates.Where(s => s.ItemId == plan.Items[0].Id).OrderBy(s => s.Sequence).Select(s => new { s.Sequence, s.Acceptance, s.Reason }).ToListAsync());
        Assert.Equal([(1, "Declined", "Cannot afford it now"), (2, "Accepted", (string?)null)], rows.Select(r => (r.Sequence, r.Acceptance, r.Reason)).ToArray());
    }

    [Fact]
    public async Task Every_wrong_field_of_an_answer_is_named_and_nothing_is_stored()
    {
        var plan = await ThreeProcedurePlanAsync();
        var e = await Refused(() => Decide(plan, 0, "Maybe", reason: new string('x', 501), by: "Bell\u0007"));
        Assert.Equal(["decidedByName", "decision", "reason"], e.FieldErrors.Keys.Order().ToArray());
        Assert.Equal("plan_not_found", (await Refused(() => Svc(s => s.DecideItemAsync(Guid.NewGuid(), plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("item_not_found", (await Refused(() => Svc(s => s.DecideItemAsync(plan.Id, Guid.NewGuid(), "Accepted", null, null, plan.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("row_version_required", (await Refused(() => Decide(plan, 0, "Accepted", version: ""))).Code);
        Assert.Equal(0, await StateRowsAsync());
    }

    [Fact]
    public async Task Repeating_an_answer_is_quiet_even_from_a_stale_copy_but_a_new_answer_from_a_stale_copy_is_a_conflict()
    {
        var plan = await ThreeProcedurePlanAsync();
        await Decide(plan, 0, "Accepted");
        var renamed = await Svc(s => s.RenameAsync(plan.Id, "A different plan", plan.RowVersion, S.Actor, default));          // the content changed after `plan` was read
        Assert.NotEqual(plan.RowVersion, renamed.RowVersion);

        await Decide(plan, 0, "Accepted");                                                                                    // the same answer again: nothing to do
        Assert.Equal(1, await StateRowsAsync());
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Decide(plan, 1, "Accepted"));                              // a new answer about content that has since changed
        Assert.Equal(1, await StateRowsAsync());
        await Decide(renamed, 1, "Accepted");
        Assert.Equal(2, await StateRowsAsync());
    }

    [Fact]
    public async Task Two_people_answering_at_once_leave_whole_numbered_history_and_no_duplicates()
    {
        var plan = await ThreeProcedurePlanAsync();
        var results = await Task.WhenAll(
            Decide(plan, 0, "Accepted").ContinueWith(t => t.IsCompletedSuccessfully),
            Decide(plan, 0, "Declined", reason: "No").ContinueWith(t => t.IsCompletedSuccessfully));
        Assert.Contains(true, results);
        var sequences = await WithDb(db => db.TreatmentPlanItemStates.Where(s => s.ItemId == plan.Items[0].Id).Select(s => s.Sequence).OrderBy(x => x).ToListAsync());
        Assert.Equal(Enumerable.Range(1, sequences.Count).ToArray(), sequences.ToArray());
    }

    // ---------- the whole plan ----------

    [Fact]
    public async Task Accepting_the_plan_decides_every_pending_procedure_in_one_history_entry_and_a_repeat_is_quiet()
    {
        var plan = await ThreeProcedurePlanAsync();
        var accepted = await DecidePlan(plan, "Accepted", by: "Ann Alder (patient)");
        Assert.Equal(("Accepted", 200m), (accepted.AcceptanceStatus, accepted.AcceptedEstimateTotal));
        Assert.All(accepted.Items, i => Assert.Equal(("Accepted", "Ann Alder (patient)"), (i.Acceptance, i.DecidedByName)));
        Assert.Equal(3, await StateRowsAsync());
        var history = await Svc(s => s.HistoryAsync(plan.Id, default));
        Assert.Equal("PlanAccepted", history[^1].ChangeType);
        Assert.Equal(1, history.Count(h => h.ChangeType == "PlanAccepted"));

        await DecidePlan(plan, "Accepted");                                                                                   // stale copy, same answer: quiet
        Assert.Equal((3, history.Count), (await StateRowsAsync(), (await Svc(s => s.HistoryAsync(plan.Id, default))).Count));
    }

    [Fact]
    public async Task A_plan_decision_never_overwrites_an_opposite_one_and_fills_in_only_what_is_still_pending()
    {
        var plan = await ThreeProcedurePlanAsync();
        var mixed = await Decide(plan, 0, "Declined", reason: "Not now");
        var e = await Refused(() => DecidePlan(mixed, "Accepted"));
        Assert.Equal(("partial_acceptance_conflict", 409), (e.Code, e.StatusCode));
        Assert.Equal(1, await StateRowsAsync());                                                                              // nothing was recorded
        Assert.Equal("Partial", (await Svc(s => s.GetAsync(plan.Id, default))).AcceptanceStatus);

        var declined = await DecidePlan(mixed, "Declined", reason: "Not this year");                                          // the same direction: only the two pending ones change
        Assert.Equal(("Declined", 3), (declined.AcceptanceStatus, await StateRowsAsync()));
        Assert.Equal(["Not now", "Not this year", "Not this year"], declined.Items.Select(i => i.DecisionReason).ToArray());
        Assert.Equal("Say why it was declined.", (await Refused(() => DecidePlan(plan, "Declined"))).FieldErrors["reason"]);
    }

    [Fact]
    public async Task Withdrawn_procedures_are_left_out_and_a_withdrawn_plan_or_a_plan_with_nothing_left_cannot_be_decided()
    {
        var plan = await ThreeProcedurePlanAsync();
        var oneGone = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[2].Id, "Entered in error", plan.RowVersion, S.Actor, default));
        var e = await Refused(() => Decide(plan, 2, "Accepted", version: oneGone.RowVersion));
        Assert.Equal(("item_withdrawn", 409), (e.Code, e.StatusCode));

        var accepted = await DecidePlan(oneGone, "Accepted");
        Assert.Equal(("Accepted", 160m, 2), (accepted.AcceptanceStatus, accepted.AcceptedEstimateTotal, await StateRowsAsync()));           // the withdrawn one is not counted

        var cleared = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "x", accepted.RowVersion, S.Actor, default));
        cleared = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[1].Id, "y", cleared.RowVersion, S.Actor, default));
        Assert.Equal("no_procedures", (await Refused(() => DecidePlan(cleared, "Accepted"))).Code);

        var withdrawn = await Svc(s => s.WithdrawAsync(plan.Id, "Entered in error", cleared.RowVersion, S.Actor, default));
        Assert.Equal("plan_withdrawn", (await Refused(() => DecidePlan(withdrawn, "Declined", reason: "x"))).Code);
        Assert.Equal("plan_withdrawn", (await Refused(() => Schedule(withdrawn, 0, true))).Code);
    }

    // ---------- scheduling and completion ----------

    [Fact]
    public async Task Only_an_accepted_procedure_is_scheduled_and_scheduling_can_be_changed_and_cleared_with_the_history_kept()
    {
        var plan = await ThreeProcedurePlanAsync();
        Assert.Equal("item_not_accepted", (await Refused(() => Schedule(plan, 0, true))).Code);
        await Decide(plan, 0, "Accepted");

        var date = new DateOnly(2030, 5, 20);
        var scheduled = await Schedule(plan, 0, true, date);
        Assert.Equal(("Scheduled", date), (scheduled.Items[0].Scheduling, scheduled.Items[0].PlannedDate));
        await Schedule(plan, 0, true, date);                                                                                  // the same again: quiet
        Assert.Equal(2, await StateRowsAsync());
        var moved = await Schedule(plan, 0, true, date.AddDays(7));
        Assert.Equal(date.AddDays(7), moved.Items[0].PlannedDate);
        var cleared = await Schedule(plan, 0, false);
        Assert.Equal(("Unscheduled", null), (cleared.Items[0].Scheduling, cleared.Items[0].PlannedDate));
        Assert.Equal(["Accepted", "Scheduled", "Scheduled", "Unscheduled"], (await WithDb(db => db.TreatmentPlanItemStates.Where(s => s.ItemId == plan.Items[0].Id).OrderBy(s => s.Sequence).Select(s => s.Scheduling).ToListAsync()))
            .Select((s, i) => i == 0 ? "Accepted" : s).ToArray());

        Assert.Equal("plannedDate", (await Refused(() => Schedule(plan, 0, true, new DateOnly(1990, 1, 1)))).FieldErrors.Keys.Single());
        var types = (await Svc(s => s.HistoryAsync(plan.Id, default))).Select(h => h.ChangeType).Where(t => t.StartsWith("ItemSch") || t.StartsWith("ItemUns")).ToArray();
        Assert.Equal(["ItemScheduled", "ItemScheduled", "ItemUnscheduled"], types);
    }

    [Fact]
    public async Task Declining_a_scheduled_procedure_unschedules_it_and_completing_is_final()
    {
        var plan = await ThreeProcedurePlanAsync();
        await Decide(plan, 0, "Accepted");
        await Schedule(plan, 0, true, new DateOnly(2030, 5, 20));
        var declined = await Decide(plan, 0, "Declined", reason: "Changed their mind");
        Assert.Equal(("Declined", "Unscheduled", null), (declined.Items[0].Acceptance, declined.Items[0].Scheduling, declined.Items[0].PlannedDate));

        Assert.Equal("item_not_accepted", (await Refused(() => Complete(plan, 0))).Code);
        await Decide(plan, 0, "Accepted");
        var done = await Complete(plan, 0);
        Assert.Equal(("Accepted", "Completed"), (done.Items[0].Acceptance, done.Items[0].Completion));
        var rows = await StateRowsAsync();
        await Complete(plan, 0);                                                                                              // quiet
        Assert.Equal(rows, await StateRowsAsync());

        Assert.Equal("item_completed", (await Refused(() => Decide(plan, 0, "Declined", reason: "x"))).Code);
        Assert.Equal("item_completed", (await Refused(() => Schedule(plan, 0, true))).Code);
        Assert.Equal("partial_acceptance_conflict", (await Refused(() => DecidePlan(plan, "Declined", reason: "x"))).Code);                   // a completed procedure holds an Accepted answer, so declining the plan would overwrite it
        Assert.Equal("ItemCompleted", (await Svc(s => s.HistoryAsync(plan.Id, default)))[^1].ChangeType);
    }

    // ---------- failure: the log is unavailable ----------

    [Fact]
    public async Task A_failed_audit_write_on_any_answer_or_progress_change_changes_nothing_and_says_so()
    {
        var plan = await ThreeProcedurePlanAsync();
        await Decide(plan, 0, "Accepted");
        var before = (await CountsAsync(), await StateRowsAsync());
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseStateAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType IN ('TreatmentPlanItemAccepted', 'TreatmentPlanItemDeclined', 'TreatmentPlanAccepted', 'TreatmentPlanDeclined', 'TreatmentPlanItemScheduled', 'TreatmentPlanItemCompleted')) THROW 59020, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var failures = new Func<Task>[] { () => Decide(plan, 1, "Accepted"), () => DecidePlan(plan, "Accepted"), () => Schedule(plan, 0, true), () => Complete(plan, 0) };
        foreach (var attempt in failures)
        {
            var e = await Refused(async () => { await attempt(); return 0; });
            Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
            Assert.DoesNotContain("audit", e.Message);
        }
        Assert.Equal(before, (await CountsAsync(), await StateRowsAsync()));

        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefuseStateAudit]");
        await Decide(plan, 1, "Accepted");
        Assert.Equal(before.Item2 + 1, await StateRowsAsync());
    }

    [Fact]
    public async Task A_completed_procedure_cannot_be_withdrawn_but_the_others_still_can()
    {
        var plan = await ThreeProcedurePlanAsync();
        await Decide(plan, 0, "Accepted");
        await Complete(plan, 0);

        var e = await Refused(() => Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Mistake", plan.RowVersion, S.Actor, default)));
        Assert.Equal(("item_completed", 409), (e.Code, e.StatusCode));
        Assert.False((await Svc(s => s.GetAsync(plan.Id, default))).Items[0].IsWithdrawn);

        var other = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[1].Id, "Patient declined", plan.RowVersion, S.Actor, default));
        Assert.Equal((false, true), (other.Items[0].IsWithdrawn, other.Items[1].IsWithdrawn));
    }
}
