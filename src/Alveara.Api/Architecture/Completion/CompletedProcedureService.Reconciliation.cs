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
/// <item><c>ChartMismatch</c>: a recorded completion that names a finding but whose recorded finding version (the one it wrote or observed) is missing, does not exist, does not show the finding as an active Completed state, or post-dates the completion. The version is exact evidence; no timestamp chooses it.</item>
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
        var recorded = await db.CompletedProcedures.AsNoTracking().Select(c => new { c.PlanItemId, c.ToothFindingId, c.ToothFindingVersionNumber, c.FinalizedAtUtc }).ToListAsync(ct);
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

        return new CompletionReconciliation(legacy, missing, unexpected, orphaned, await ChartMismatchesAsync(recorded.Where(r => r.ToothFindingId != null).Select(r => (r.ToothFindingId!.Value, r.ToothFindingVersionNumber, r.FinalizedAtUtc)).ToList(), ct), duplicates, clock.UtcNow);
    }

    /// <summary>
    /// C2, corrected in R02 (review finding ALV-008-C02-R01-01): the chart evidence is the exact finding version the completion recorded when it was made (the one it wrote or observed), not a version chosen by
    /// timestamp, so a later legitimate chart change can never be taken for the completion, even at the same clock instant. A completion is a mismatch when it has no recorded version, the version does not exist
    /// for that finding, it does not show the finding as an active Completed state, or it post-dates the completion. The version is immutable history, so what it shows today is what it showed then.
    /// </summary>
    private async Task<int> ChartMismatchesAsync(IReadOnlyList<(Guid FindingId, int? Version, DateTimeOffset At)> completions, CancellationToken ct)
    {
        if (completions.Count == 0) return 0;
        var findingIds = completions.Select(c => c.FindingId).Distinct().ToList();
        var versions = (await db.ToothFindingVersions.AsNoTracking().Where(v => findingIds.Contains(v.FindingId)).Select(v => new { v.FindingId, v.VersionNumber, v.State, v.Status, v.OccurredAtUtc }).ToListAsync(ct))
            .ToDictionary(v => (v.FindingId, v.VersionNumber));
        return completions.Count(c =>
            c.Version is not int number || !versions.TryGetValue((c.FindingId, number), out var v)
            || v.State != FindingStates.Completed || v.Status != FindingStatuses.Active || v.OccurredAtUtc > c.At);
    }
}
