using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-015-C01: what happens to a proposed procedure after it is proposed: a patient accepts or declines it (alone, or the whole plan at once), it is scheduled, and it is completed. Nothing here edits
/// anything: every change appends a <see cref="TreatmentPlanItemState"/> row (the full state after the change), a history entry and a PHI-free audit entry in one save, so the current state is the latest row
/// and the whole story stays readable. A decision records the staff member who entered it (from the signed-in account) and, if given, the name of the person who gave the answer. Accepting or declining needs
/// the plan's current version, so what is recorded is what was on screen; a retry of the same decision is quiet. Scheduling and completion are states only: nothing here talks to the appointment module.
/// </summary>
public partial class TreatmentPlanService
{
    /// <summary>Accepts or declines one procedure. Declining needs a reason. Deciding what is already decided changes nothing; a completed procedure cannot be re-decided.</summary>
    public async Task<PlanView> DecideItemAsync(Guid planId, Guid itemId, string? decision, string? reason, string? decidedBy, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var item = await db.TreatmentPlanItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.PlanId == planId, ct) ?? throw ItemNotFound();
        RequireProposed(plan);
        await RequireUnfinishedAsync(plan, ct);
        if (item.WithdrawnAtUtc is not null) throw new TreatmentPlanException("item_withdrawn", "That procedure has been withdrawn, so it cannot be accepted or declined.", 409);
        var (choice, why, by) = CleanDecision(decision, reason, decidedBy);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);

        var previous = (await CurrentStatesAsync(planId, ct)).GetValueOrDefault(itemId);
        if (AcceptanceOf(previous) == choice) return await GetAsync(planId, ct);
        if (CompletionOf(previous) == ItemCompletion.Completed) throw ItemCompleted();
        RequireCurrent(plan, version);

        var now = clock.UtcNow;
        StageState(plan, item.Id, previous, choice, choice == ItemAcceptance.Accepted ? SchedulingOf(previous) : ItemScheduling.Unscheduled, ItemCompletion.NotStarted,
            choice == ItemAcceptance.Accepted ? previous?.PlannedDate : null, why, by, actor, now);
        var type = choice == ItemAcceptance.Accepted ? TreatmentPlanChanges.ItemAccepted : TreatmentPlanChanges.ItemDeclined;
        StageEvent(plan, await NextEventAsync(planId, ct), type, itemId, null, why, actor, now, plan.CurrentRevision);
        AuditService.Record(db, choice == ItemAcceptance.Accepted ? "TreatmentPlanItemAccepted" : "TreatmentPlanItemDeclined", nameof(TreatmentPlan), planId, actor,
            $"{await ItemLabelAsync(plan, item, ct)} {choice.ToLowerInvariant()}{AnswerSource(by)}.", why);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>
    /// ALV-N023 (owner decision DR-4): accepts or declines every procedure of the plan that has no answer yet (not withdrawn, not completed). Procedures that already have an answer are left exactly
    /// as they are - never overwritten. The plan view returned says how many were changed (<c>ChangedCount</c>); with nothing unanswered the request is refused (nothing_unanswered) and nothing is recorded.
    /// </summary>
    public async Task<PlanView> DecidePlanAsync(Guid planId, string? decision, string? reason, string? decidedBy, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        RequireProposed(plan);
        await RequireUnfinishedAsync(plan, ct);
        var (choice, why, by) = CleanDecision(decision, reason, decidedBy);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);

        var live = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId && i.WithdrawnAtUtc == null).OrderBy(i => i.ItemNumber).ToListAsync(ct);
        var states = await CurrentStatesAsync(planId, ct);
        var pending = live.Where(i => AcceptanceOf(states.GetValueOrDefault(i.Id)) == ItemAcceptance.Pending && CompletionOf(states.GetValueOrDefault(i.Id)) != ItemCompletion.Completed).ToList();
        if (pending.Count == 0) throw new TreatmentPlanException("nothing_unanswered", "No procedure on this plan is waiting for an answer, so nothing was recorded.", 409);
        RequireCurrent(plan, version);

        var now = clock.UtcNow;
        foreach (var i in pending) StageState(plan, i.Id, states.GetValueOrDefault(i.Id), choice, ItemScheduling.Unscheduled, ItemCompletion.NotStarted, null, why, by, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), choice == ItemAcceptance.Accepted ? TreatmentPlanChanges.PlanAccepted : TreatmentPlanChanges.PlanDeclined, null, null, why, actor, now, plan.CurrentRevision);
        var codes = await db.ProcedureDefinitions.AsNoTracking().Where(d => pending.Select(i => i.ProcedureId).Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct);
        AuditService.Record(db, choice == ItemAcceptance.Accepted ? "TreatmentPlanAccepted" : "TreatmentPlanDeclined", nameof(TreatmentPlan), planId, actor,
            $"{pending.Count} unanswered procedure{(pending.Count == 1 ? "" : "s")} ({string.Join(", ", pending.Select(i => $"item {i.ItemNumber} {codes[i.ProcedureId]}"))}) {choice.ToLowerInvariant()} in the treatment plan {PlanRef(plan)}{AnswerSource(by)}.", why);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct) with { ChangedCount = pending.Count };
    }

    /// <summary>
    /// ALV-N023 (owner decisions DR-1 and PD-15): sets or clears an accepted procedure's TARGET date. A target date is only a target - it never books the procedure and never settles its
    /// unscheduled-treatment follow-up (only a linked appointment does). Setting one needs a date; a date before today needs <paramref name="confirmPast"/> and is then shown as overdue.
    /// Saying what is already so changes nothing; a completed procedure cannot be changed.
    /// </summary>
    public async Task<PlanView> ScheduleItemAsync(Guid planId, Guid itemId, bool scheduled, DateOnly? plannedDate, Guid actor, CancellationToken ct, bool confirmPast = false)
    {
        var (plan, item, previous) = await LoadForProgressAsync(planId, itemId, ct);
        if (CompletionOf(previous) == ItemCompletion.Completed) throw ItemCompleted();
        var wanted = scheduled ? ItemScheduling.Scheduled : ItemScheduling.Unscheduled;
        var date = scheduled ? plannedDate : null;
        if (scheduled && date is null) throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["plannedDate"] = "Choose the target date, or clear it." });
        if (date is { } d && (d < new DateOnly(2000, 1, 1) || d > Today().AddYears(10)))
            throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["plannedDate"] = "Choose a date between 2000 and ten years from now." });
        if (date is { } past && past < Today() && !confirmPast)
            throw new TreatmentPlanException("past_target_date", "That date has passed. Confirm to save it as an overdue target date.", 400,
                new Dictionary<string, string> { ["plannedDate"] = "That date has passed. Confirm to save it as an overdue target date." });
        if (SchedulingOf(previous) == wanted && previous?.PlannedDate == date) return await GetAsync(planId, ct);

        var now = clock.UtcNow;
        StageState(plan, item.Id, previous, ItemAcceptance.Accepted, wanted, ItemCompletion.NotStarted, date, null, previous?.DecidedByName, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), scheduled ? TreatmentPlanChanges.TargetDateSet : TreatmentPlanChanges.TargetDateCleared, itemId, null, null, actor, now, plan.CurrentRevision);
        AuditService.Record(db, scheduled ? "TreatmentPlanItemTargetDateSet" : "TreatmentPlanItemTargetDateCleared", nameof(TreatmentPlan), planId, actor,
            $"{await ItemLabelAsync(plan, item, ct)} {(scheduled ? $"target date set to {date:yyyy-MM-dd}{(date < Today() ? " (overdue, confirmed)" : "")}" : "target date cleared")}.");
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    // ALV-008-C02: the state-only completions of STORY-008 (CompleteItemAsync and CompleteAcceptedAsync) were removed with their routes. A completion is made only by CompletedProcedureService (which stages its state
    // row through StageCompletionsAsync, see TreatmentPlanService.CompletionSupport.cs); a procedure completed the old way is resolved by CompletedProcedureService.AdoptAsync.

    // ---------- shared ----------

    private async Task<(TreatmentPlan Plan, TreatmentPlanItem Item, TreatmentPlanItemState? State)> LoadForProgressAsync(Guid planId, Guid itemId, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var item = await db.TreatmentPlanItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.PlanId == planId, ct) ?? throw ItemNotFound();
        RequireProposed(plan);
        await RequireUnfinishedAsync(plan, ct);
        if (item.WithdrawnAtUtc is not null) throw new TreatmentPlanException("item_withdrawn", "That procedure has been withdrawn.", 409);
        var state = (await CurrentStatesAsync(planId, ct)).GetValueOrDefault(itemId);
        if (AcceptanceOf(state) != ItemAcceptance.Accepted) throw new TreatmentPlanException("item_not_accepted", "Only an accepted procedure can be scheduled or completed. Accept it first.", 409);
        return (plan, item, state);
    }

    /// <summary>The latest state row of every procedure of a plan that has one.</summary>
    private async Task<Dictionary<Guid, TreatmentPlanItemState>> CurrentStatesAsync(Guid planId, CancellationToken ct) =>
        (await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.PlanId == planId).ToListAsync(ct)).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Sequence).First());

    /// <summary>
    /// ALV-N021 A1 (F-184, F-218): the row in which the current answer was entered: the latest row whose acceptance differs from the row before it. This is exact, not a guess: giving the answer an item
    /// already has writes no row (DecideItemAsync, DecidePlanAsync), so every answer changes the acceptance, while scheduling, unscheduling and completion append rows that keep it (Accepted). Null while
    /// no answer has been given. Rows in sequence order.
    /// </summary>
    private static TreatmentPlanItemState? AnswerRow(IReadOnlyList<TreatmentPlanItemState> rows)
    {
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i].Acceptance == ItemAcceptance.Pending) return null;
            if (i == 0 || rows[i - 1].Acceptance != rows[i].Acceptance) return rows[i];
        }
        return null;
    }

    private static string AcceptanceOf(TreatmentPlanItemState? s) => s?.Acceptance ?? ItemAcceptance.Pending;
    private static string SchedulingOf(TreatmentPlanItemState? s) => s?.Scheduling ?? ItemScheduling.Unscheduled;
    private static string CompletionOf(TreatmentPlanItemState? s) => s?.Completion ?? ItemCompletion.NotStarted;

    private void StageState(TreatmentPlan plan, Guid itemId, TreatmentPlanItemState? previous, string acceptance, string scheduling, string completion, DateOnly? plannedDate, string? reason, string? decidedBy, Guid actor, DateTimeOffset now) =>
        db.TreatmentPlanItemStates.Add(new TreatmentPlanItemState
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = plan.PatientId, ItemId = itemId, Sequence = (previous?.Sequence ?? 0) + 1, Acceptance = acceptance, Scheduling = scheduling, Completion = completion,
            PlannedDate = scheduling == ItemScheduling.Scheduled ? plannedDate : null, Reason = reason, DecidedByName = decidedBy, RevisionNumber = plan.CurrentRevision, RecordedByUserId = actor, RecordedAtUtc = now,
        });

    /// <summary>A change is made from the copy the person was looking at: the plan must be as they last read it, otherwise what they decided is not what is now proposed.</summary>
    private static void RequireCurrent(TreatmentPlan plan, byte[] version)
    {
        if (!plan.RowVersion.AsSpan().SequenceEqual(version)) throw new ConcurrencyConflictException(nameof(TreatmentPlan), plan.Id);
    }

    private static (string Choice, string? Reason, string? DecidedBy) CleanDecision(string? decision, string? reason, string? decidedBy)
    {
        var errors = new Dictionary<string, string>();
        var choice = string.Equals(decision?.Trim(), ItemAcceptance.Accepted, StringComparison.OrdinalIgnoreCase) ? ItemAcceptance.Accepted
            : string.Equals(decision?.Trim(), ItemAcceptance.Declined, StringComparison.OrdinalIgnoreCase) ? ItemAcceptance.Declined : null;
        if (choice is null) errors["decision"] = "Choose Accepted or Declined.";
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (choice == ItemAcceptance.Declined && why is null) errors["reason"] = "Say why it was declined.";
        if (why is not null && why.Length > TreatmentPlanRules.ReasonMax) errors["reason"] = $"The reason can be at most {TreatmentPlanRules.ReasonMax} characters; this one has {why.Length}.";
        var by = string.IsNullOrWhiteSpace(decidedBy) ? null : Regex.Replace(decidedBy.Trim(), "\\s+", " ");
        if (by is not null && (by.Length > TreatmentPlanRules.DecidedByMax || by.Any(char.IsControl))) errors["decidedByName"] = $"Use a single line of at most {TreatmentPlanRules.DecidedByMax} characters.";
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        return (choice!, why, by);
    }

    /// <summary>
    /// ALV-N023 (owner decision, addendum 01, ambiguity 3): the audit log stays free of patient and representative identity. It says only whether the person who gave the answer was named;
    /// the name itself stays in the plan's own answer history, visible to staff who may read plans.
    /// </summary>
    private static string AnswerSource(string? decidedBy) => decidedBy is null ? "" : "; the person who gave the answer is recorded on the plan";

    private static TreatmentPlanException ItemNotFound() => new("item_not_found", "That item was not found in this treatment plan.", 404);
    private static TreatmentPlanException ItemCompleted() => new("item_completed", "That procedure is completed, so its acceptance and schedule are final.", 409);
}
