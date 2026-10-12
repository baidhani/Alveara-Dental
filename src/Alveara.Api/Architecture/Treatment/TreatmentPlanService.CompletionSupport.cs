using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-008-C01: the pieces of plan-level completion that the completion-set service shares with STORY-008's completion, so both stage exactly the same state row, history event and audit entry for a completed
/// procedure and map a failed save the same way. Internal on purpose: the public surface of the plan service does not grow.
/// </summary>
public partial class TreatmentPlanService
{
    /// <summary>
    /// Stages, for each procedure, its Completed state row, its <c>ItemCompleted</c> history event (numbered in order after the plan's latest) and its audit entry. The plan is not touched here (STORY-008's completion does not move the plan's version; the completion-set service does, itself).
    /// Nothing is saved here: the caller saves, so the completion is in the same save as whatever else it stages.
    /// </summary>
    internal async Task StageCompletionsAsync(TreatmentPlan plan, IReadOnlyList<TreatmentPlanItem> items, IReadOnlyDictionary<Guid, TreatmentPlanItemState> latestStates, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var number = await NextEventAsync(plan.Id, ct);
        foreach (var item in items)
        {
            var previous = latestStates[item.Id];
            StageState(plan, item.Id, previous, ItemAcceptance.Accepted, SchedulingOf(previous), ItemCompletion.Completed, previous.PlannedDate, null, previous.DecidedByName, actor, now);
            StageEvent(plan, number++, TreatmentPlanChanges.ItemCompleted, item.Id, null, null, actor, now, plan.CurrentRevision);
            AuditService.Record(db, "TreatmentPlanItemCompleted", nameof(TreatmentPlan), plan.Id, actor, $"Item {item.ItemNumber} completed in a treatment plan.");
        }
    }

    /// <summary>Every state row of a plan's procedures, oldest first within each procedure (so "when was this accepted" can be answered from the history, not just the latest row).</summary>
    internal async Task<ILookup<Guid, TreatmentPlanItemState>> StateHistoryAsync(Guid planId, CancellationToken ct) =>
        (await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.PlanId == planId).OrderBy(s => s.ItemId).ThenBy(s => s.Sequence).ToListAsync(ct)).ToLookup(s => s.ItemId);

    /// <summary>The same save the plan's own changes use: a stale version is a conflict, a duplicate is a conflict, and any other failed save stores nothing and says so (<c>save_failed</c>, 503).</summary>
    internal Task SaveStagedAsync(Guid planId, CancellationToken ct) => SaveAsync(planId, ct);

    internal static TreatmentPlanException MissingPlan() => PlanNotFound();
    internal static void RequireOpen(TreatmentPlan plan) => RequireProposed(plan);
    internal static void RequireVersion(TreatmentPlan plan, byte[] version) => RequireCurrent(plan, version);
    internal static void MarkChanged(TreatmentPlan plan, Guid actor, DateTimeOffset now) => Touch(plan, actor, now);
    internal static string AcceptanceOfState(TreatmentPlanItemState? state) => AcceptanceOf(state);
    internal static string CompletionOfState(TreatmentPlanItemState? state) => CompletionOf(state);
}
