using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// ALV-008-C02 Gate D: counts, never patients. Each number is a kind of disagreement between the completion records, the ledger and the chart; the healthy value of every one is zero. Reads only.
/// <list type="bullet">
/// <item><c>LegacyCompletedNotAdopted</c>: procedures completed the old way that still have no completion record (work for adoption, not a defect).</item>
/// <item><c>CompletedMissingCharge</c>: a recorded completion with a fee above zero and no charge.</item>
/// <item><c>CompletedWithUnexpectedCharge</c>: a recorded completion with no fee and a charge.</item>
/// <item><c>OrphanedCharges</c>: a charge for a procedure that is neither recorded as completed nor completed the old way (or no longer exists).</item>
/// <item><c>ChartMismatch</c>: a recorded completion that names a finding whose own history, at the instant the completion was finalized, was not an active Completed state.</item>
/// <item><c>DuplicateCharges</c>: procedures with more than one charge.</item>
/// </list>
/// </summary>
public sealed record CompletionReconciliation(
    int LegacyCompletedNotAdopted, int CompletedMissingCharge, int CompletedWithUnexpectedCharge, int OrphanedCharges, int ChartMismatch, int DuplicateCharges, DateTimeOffset GeneratedAtUtc)
{
    /// <summary>True when nothing is waiting for adoption and no disagreement exists.</summary>
    public bool Clean => LegacyCompletedNotAdopted + CompletedMissingCharge + CompletedWithUnexpectedCharge + OrphanedCharges + ChartMismatch + DuplicateCharges == 0;
}

public partial class CompletedProcedureService
{
    public async Task<CompletionReconciliation> ReconcileAsync(CancellationToken ct)
    {
        var recorded = await db.CompletedProcedures.AsNoTracking().Select(c => new { c.PlanItemId, c.ToothFindingId, c.FinalizedAtUtc }).ToListAsync(ct);
        var recordedIds = recorded.Select(r => r.PlanItemId).ToHashSet();
        var items = await db.TreatmentPlanItems.AsNoTracking().Select(i => new { i.Id, i.Fee, i.WithdrawnAtUtc }).ToDictionaryAsync(i => i.Id, ct);
        var charges = await db.BillingEntries.AsNoTracking().Where(e => e.Kind == BillingKinds.Charge && e.PlanItemId != null).Select(e => e.PlanItemId!.Value).ToListAsync(ct);
        var chargeCounts = charges.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        // the latest state of each procedure: Completed in the old way when it is a Completed state row (the same reading the plan uses)
        var states = await db.TreatmentPlanItemStates.AsNoTracking().OrderBy(s => s.ItemId).ThenBy(s => s.Sequence).ToListAsync(ct);
        var completedOldWay = states.GroupBy(s => s.ItemId).Where(g => TreatmentPlanService.CompletionOfState(g.Last()) == ItemCompletion.Completed).Select(g => g.Key).ToHashSet();

        var legacy = completedOldWay.Count(id => !recordedIds.Contains(id) && items.TryGetValue(id, out var i) && i.WithdrawnAtUtc is null);
        var missing = recordedIds.Count(id => items.TryGetValue(id, out var i) && i.Fee > 0m && !chargeCounts.ContainsKey(id));
        var unexpected = recordedIds.Count(id => items.TryGetValue(id, out var i) && i.Fee <= 0m && chargeCounts.ContainsKey(id));
        var orphaned = chargeCounts.Keys.Count(id => !recordedIds.Contains(id) && !completedOldWay.Contains(id));
        var duplicates = chargeCounts.Count(c => c.Value > 1);

        return new CompletionReconciliation(legacy, missing, unexpected, orphaned, await ChartMismatchesAsync(recorded.Where(r => r.ToothFindingId != null).Select(r => (r.ToothFindingId!.Value, r.FinalizedAtUtc)).ToList(), ct), duplicates, clock.UtcNow);
    }

    /// <summary>
    /// C2: the chart evidence is read from the finding's own version history at the completion instant, not from its state today (a later legitimate change to the chart must not turn a good completion into a defect):
    /// the latest version at or before the time the completion was finalized has to be an active Completed state. No schema change is needed, and the same reading covers completions recorded by the C01 workflow.
    /// </summary>
    private async Task<int> ChartMismatchesAsync(IReadOnlyList<(Guid FindingId, DateTimeOffset At)> completions, CancellationToken ct)
    {
        if (completions.Count == 0) return 0;
        var findingIds = completions.Select(c => c.FindingId).Distinct().ToList();
        var versions = (await db.ToothFindingVersions.AsNoTracking().Where(v => findingIds.Contains(v.FindingId)).Select(v => new { v.FindingId, v.VersionNumber, v.State, v.Status, v.OccurredAtUtc }).ToListAsync(ct))
            .ToLookup(v => v.FindingId);
        return completions.Count(c =>
        {
            var at = versions[c.FindingId].Where(v => v.OccurredAtUtc <= c.At).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            return at is null || at.State != FindingStates.Completed || at.Status != FindingStatuses.Active;
        });
    }
}
