using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-015-C01: reading a plan's saved revisions and comparing two of them. Every revision is exactly as it was saved (the database never lets one be edited), so a comparison is a plain difference of two
/// stored versions: nothing is reconstructed from later state. The fees are the ones copied onto each procedure when it was proposed, so a comparison never shows a later catalog price.
/// </summary>
public partial class TreatmentPlanService
{
    public async Task<IReadOnlyList<RevisionSummaryView>> RevisionsAsync(Guid planId, CancellationToken ct)
    {
        var plan = await GetAsync(planId, ct);
        var revisions = await db.TreatmentPlanRevisions.AsNoTracking().Where(r => r.PlanId == planId).OrderBy(r => r.RevisionNumber).ToListAsync(ct);
        return await SummariesAsync(plan, revisions, ct);
    }

    public async Task<RevisionView> RevisionAsync(Guid planId, int number, CancellationToken ct)
    {
        var plan = await GetAsync(planId, ct);
        return (await DetailsAsync(plan, [number], ct))[0];
    }

    /// <summary>What changed from revision <paramref name="from"/> to revision <paramref name="to"/> (either order; the comparison reads from the first named to the second).</summary>
    public async Task<RevisionComparison> CompareAsync(Guid planId, int from, int to, CancellationToken ct)
    {
        var plan = await GetAsync(planId, ct);
        var pair = await DetailsAsync(plan, [from, to], ct);
        var (a, b) = (pair[0], pair[1]);

        FieldChange? Change(string? x, string? y) => x == y ? null : new FieldChange(x, y);
        var live = (RevisionView r) => r.Items.Where(i => !i.IsWithdrawn).ToDictionary(i => i.ItemId);
        var (liveA, liveB) = (live(a), live(b));
        var phasesA = a.Phases.ToDictionary(p => p.PhaseNumber);
        var phasesB = b.Phases.ToDictionary(p => p.PhaseNumber);

        var moved = liveA.Values.Where(i => liveB.TryGetValue(i.ItemId, out var j) && j.PhaseNumber != i.PhaseNumber).Select(i => new ItemMove(liveB[i.ItemId], i.PhaseNumber, liveB[i.ItemId].PhaseNumber)).ToList();
        return new RevisionComparison(
            a.Summary, b.Summary, Change(a.Summary.Title, b.Summary.Title), Change(a.ClinicalRationale, b.ClinicalRationale), Change(a.PatientExplanation, b.PatientExplanation),
            b.Phases.Where(p => !phasesA.ContainsKey(p.PhaseNumber)).ToList(), a.Phases.Where(p => !phasesB.ContainsKey(p.PhaseNumber)).ToList(),
            b.Phases.Where(p => phasesA.TryGetValue(p.PhaseNumber, out var q) && q != p).Select(p => new PhaseChange(p.PhaseNumber, phasesA[p.PhaseNumber], p)).ToList(),
            liveB.Values.Where(i => !liveA.ContainsKey(i.ItemId)).OrderBy(i => i.ItemNumber).ToList(), liveA.Values.Where(i => !liveB.ContainsKey(i.ItemId)).OrderBy(i => i.ItemNumber).ToList(), moved,
            a.Summary.EstimateTotal, b.Summary.EstimateTotal, b.Summary.EstimateTotal - a.Summary.EstimateTotal, TreatmentPlanEstimate.Label);
    }

    private async Task<List<RevisionSummaryView>> SummariesAsync(PlanView plan, IReadOnlyList<TreatmentPlanRevision> revisions, CancellationToken ct)
    {
        var ids = revisions.Select(r => r.Id).ToList();
        var held = (await db.TreatmentPlanRevisionItems.AsNoTracking().Where(i => ids.Contains(i.RevisionId)).ToListAsync(ct)).ToLookup(i => i.RevisionId);
        var phases = (await db.TreatmentPlanRevisionPhases.AsNoTracking().Where(p => ids.Contains(p.RevisionId)).ToListAsync(ct)).ToLookup(p => p.RevisionId);
        var fees = plan.Items.ToDictionary(i => i.Id, i => i.Fee);
        var names = await ClinicalNames.ResolveAsync(db, revisions.Select(r => (Guid?)r.CreatedByUserId), ct);
        return revisions.Select(r =>
        {
            var live = held[r.Id].Where(i => !i.IsWithdrawn).ToList();
            return new RevisionSummaryView(r.RevisionNumber, r.ChangeType, r.Title, r.Reason, ClinicalNames.Name(names, r.CreatedByUserId), r.CreatedAtUtc, live.Count, live.Sum(i => fees.GetValueOrDefault(i.ItemId)), phases[r.Id].Count());
        }).ToList();
    }

    private async Task<List<RevisionView>> DetailsAsync(PlanView plan, IReadOnlyList<int> numbers, CancellationToken ct)
    {
        var wanted = numbers.Distinct().ToList();
        var revisions = await db.TreatmentPlanRevisions.AsNoTracking().Where(r => r.PlanId == plan.Id && wanted.Contains(r.RevisionNumber)).ToListAsync(ct);
        if (revisions.Count != wanted.Count) throw new TreatmentPlanException("revision_not_found", "That revision of the treatment plan was not found.", 404);
        var summaries = (await SummariesAsync(plan, revisions, ct)).ToDictionary(s => s.RevisionNumber);
        var ids = revisions.Select(r => r.Id).ToList();
        var held = (await db.TreatmentPlanRevisionItems.AsNoTracking().Where(i => ids.Contains(i.RevisionId)).ToListAsync(ct)).ToLookup(i => i.RevisionId);
        var phases = (await db.TreatmentPlanRevisionPhases.AsNoTracking().Where(p => ids.Contains(p.RevisionId)).OrderBy(p => p.PhaseNumber).ToListAsync(ct)).ToLookup(p => p.RevisionId);
        var items = plan.Items.ToDictionary(i => i.Id);
        var byNumber = revisions.ToDictionary(r => r.RevisionNumber);

        return numbers.Select(n =>
        {
            var r = byNumber[n];
            var rows = held[r.Id].Select(h => (h, i: items[h.ItemId])).OrderBy(x => x.i.ItemNumber)
                .Select(x => new RevisionItemView(x.i.Id, x.i.ItemNumber, x.i.ProcedureCode, x.i.ProcedureDescription, x.i.DiagnosisLabel, x.i.ToothKey, x.i.Surface, x.i.Fee, x.h.IsWithdrawn, x.h.PhaseNumber)).ToList();
            return new RevisionView(summaries[n], r.ClinicalRationale, r.PatientExplanation, phases[r.Id].Select(p => new PlanPhaseView(p.PhaseNumber, p.Name, p.PlannedVisit, p.TargetDate)).ToList(), rows, TreatmentPlanEstimate.Label);
        }).ToList();
    }
}
