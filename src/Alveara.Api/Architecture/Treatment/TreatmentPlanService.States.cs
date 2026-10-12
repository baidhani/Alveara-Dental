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
        AuditService.Record(db, choice == ItemAcceptance.Accepted ? "TreatmentPlanItemAccepted" : "TreatmentPlanItemDeclined", nameof(TreatmentPlan), planId, actor, $"Item {item.ItemNumber} {choice.ToLowerInvariant()} in a treatment plan.", why);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>
    /// Accepts or declines the whole plan: every procedure not yet withdrawn that is still Pending gets the decision. If any of them already holds the opposite decision the request is refused as a
    /// partial-acceptance conflict and nothing is recorded, so a plan is never half-overwritten; the person decides those procedures one by one.
    /// </summary>
    public async Task<PlanView> DecidePlanAsync(Guid planId, string? decision, string? reason, string? decidedBy, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        RequireProposed(plan);
        var (choice, why, by) = CleanDecision(decision, reason, decidedBy);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);

        var live = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId && i.WithdrawnAtUtc == null).OrderBy(i => i.ItemNumber).ToListAsync(ct);
        if (live.Count == 0) throw new TreatmentPlanException("no_procedures", "This plan has no procedures left to accept or decline.", 409);
        var states = await CurrentStatesAsync(planId, ct);
        var opposite = live.Where(i => AcceptanceOf(states.GetValueOrDefault(i.Id)) is var a && a != ItemAcceptance.Pending && a != choice).ToList();
        if (opposite.Count > 0)
            throw new TreatmentPlanException("partial_acceptance_conflict", $"{opposite.Count} of the {live.Count} procedures already {(choice == ItemAcceptance.Accepted ? "declined" : "accepted")}. Decide those one by one, or leave them as they are.", 409);
        var pending = live.Where(i => AcceptanceOf(states.GetValueOrDefault(i.Id)) == ItemAcceptance.Pending).ToList();
        if (pending.Count == 0) return await GetAsync(planId, ct);
        RequireCurrent(plan, version);

        var now = clock.UtcNow;
        foreach (var i in pending) StageState(plan, i.Id, states.GetValueOrDefault(i.Id), choice, ItemScheduling.Unscheduled, ItemCompletion.NotStarted, null, why, by, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), choice == ItemAcceptance.Accepted ? TreatmentPlanChanges.PlanAccepted : TreatmentPlanChanges.PlanDeclined, null, null, why, actor, now, plan.CurrentRevision);
        AuditService.Record(db, choice == ItemAcceptance.Accepted ? "TreatmentPlanAccepted" : "TreatmentPlanDeclined", nameof(TreatmentPlan), planId, actor, $"{pending.Count} procedure{(pending.Count == 1 ? "" : "s")} {choice.ToLowerInvariant()} in a treatment plan.", why);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>Marks an accepted procedure scheduled (with an optional planned date) or unscheduled. Saying what is already so changes nothing; a completed procedure cannot be rescheduled.</summary>
    public async Task<PlanView> ScheduleItemAsync(Guid planId, Guid itemId, bool scheduled, DateOnly? plannedDate, Guid actor, CancellationToken ct)
    {
        var (plan, item, previous) = await LoadForProgressAsync(planId, itemId, ct);
        if (CompletionOf(previous) == ItemCompletion.Completed) throw ItemCompleted();
        var wanted = scheduled ? ItemScheduling.Scheduled : ItemScheduling.Unscheduled;
        var date = scheduled ? plannedDate : null;
        if (date is { } d && (d < new DateOnly(2000, 1, 1) || d > Today().AddYears(10)))
            throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["plannedDate"] = "Choose a date between 2000 and ten years from now." });
        if (SchedulingOf(previous) == wanted && previous?.PlannedDate == date) return await GetAsync(planId, ct);

        var now = clock.UtcNow;
        StageState(plan, item.Id, previous, ItemAcceptance.Accepted, wanted, ItemCompletion.NotStarted, date, null, previous?.DecidedByName, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), scheduled ? TreatmentPlanChanges.ItemScheduled : TreatmentPlanChanges.ItemUnscheduled, itemId, null, null, actor, now, plan.CurrentRevision);
        AuditService.Record(db, scheduled ? "TreatmentPlanItemScheduled" : "TreatmentPlanItemUnscheduled", nameof(TreatmentPlan), planId, actor, $"Item {item.ItemNumber} {(scheduled ? "scheduled" : "unscheduled")} in a treatment plan.");
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>Marks an accepted procedure completed. Completed is final; completing a completed procedure changes nothing.</summary>
    public async Task<PlanView> CompleteItemAsync(Guid planId, Guid itemId, Guid actor, CancellationToken ct)
    {
        var (plan, item, previous) = await LoadForProgressAsync(planId, itemId, ct);
        if (CompletionOf(previous) == ItemCompletion.Completed) return await GetAsync(planId, ct);

        var now = clock.UtcNow;
        StageState(plan, item.Id, previous, ItemAcceptance.Accepted, SchedulingOf(previous), ItemCompletion.Completed, previous?.PlannedDate, null, previous?.DecidedByName, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.ItemCompleted, itemId, null, null, actor, now, plan.CurrentRevision);
        AuditService.Record(db, "TreatmentPlanItemCompleted", nameof(TreatmentPlan), planId, actor, $"Item {item.ItemNumber} completed in a treatment plan.");
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>
    /// Completes every accepted, not yet completed procedure of a plan in one all-or-nothing save: each gets the same state row,
    /// history event and audit entry as a single completion. The plan must be as the person last read it (<paramref name="rowVersion"/>).
    /// Replaying after everything is completed changes nothing; a plan with nothing accepted to complete is refused.
    /// </summary>
    public async Task<PlanView> CompleteAcceptedAsync(Guid planId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        RequireProposed(plan);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);
        var live = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId && i.WithdrawnAtUtc == null).OrderBy(i => i.ItemNumber).ToListAsync(ct);
        var states = await CurrentStatesAsync(planId, ct);
        var accepted = live.Where(i => AcceptanceOf(states.GetValueOrDefault(i.Id)) == ItemAcceptance.Accepted).ToList();
        var open = accepted.Where(i => CompletionOf(states.GetValueOrDefault(i.Id)) != ItemCompletion.Completed).ToList();
        if (open.Count == 0)
        {
            if (accepted.Count > 0) return await GetAsync(planId, ct);
            throw new TreatmentPlanException("nothing_to_complete", "No procedure in this plan has been accepted, so there is nothing to complete.", 409);
        }
        RequireCurrent(plan, version);

        await StageCompletionsAsync(plan, open, states, actor, clock.UtcNow, ct);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    // ---------- shared ----------

    private async Task<(TreatmentPlan Plan, TreatmentPlanItem Item, TreatmentPlanItemState? State)> LoadForProgressAsync(Guid planId, Guid itemId, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var item = await db.TreatmentPlanItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.PlanId == planId, ct) ?? throw ItemNotFound();
        RequireProposed(plan);
        if (item.WithdrawnAtUtc is not null) throw new TreatmentPlanException("item_withdrawn", "That procedure has been withdrawn.", 409);
        var state = (await CurrentStatesAsync(planId, ct)).GetValueOrDefault(itemId);
        if (AcceptanceOf(state) != ItemAcceptance.Accepted) throw new TreatmentPlanException("item_not_accepted", "Only an accepted procedure can be scheduled or completed. Accept it first.", 409);
        return (plan, item, state);
    }

    /// <summary>The latest state row of every procedure of a plan that has one.</summary>
    private async Task<Dictionary<Guid, TreatmentPlanItemState>> CurrentStatesAsync(Guid planId, CancellationToken ct) =>
        (await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.PlanId == planId).ToListAsync(ct)).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Sequence).First());

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

    private static TreatmentPlanException ItemNotFound() => new("item_not_found", "That item was not found in this treatment plan.", 404);
    private static TreatmentPlanException ItemCompleted() => new("item_completed", "That procedure is completed, so its acceptance and schedule are final.", 409);
}
