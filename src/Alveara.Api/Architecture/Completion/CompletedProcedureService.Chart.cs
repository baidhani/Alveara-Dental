using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// The chart effect of a completion. A procedure finds its finding only through the plan: the plan item names a diagnosis, the diagnosis is explicitly linked to a finding of the same patient, and that finding is on
/// the procedure's tooth (and on its surface when the procedure names one). A finding is never guessed from a tooth and surface alone. The same resolution answers both "what will happen" (readiness) and "what
/// happened" (the record), so the two cannot disagree.
/// </summary>
public partial class CompletedProcedureService
{
    internal const string Ambiguous = "Ambiguous";

    /// <summary>What completing one procedure does to the chart: one of <see cref="ChartOutcomes"/> or <see cref="Ambiguous"/>, the finding it concerns and how many findings matched.</summary>
    internal sealed record ChartChoice(string Outcome, Guid? FindingId, int Matches);

    /// <summary>Resolves the chart effect of each procedure, in item order. Reads only. A finding moved by an earlier procedure of the same set counts as already completed for a later one.</summary>
    internal async Task<IReadOnlyDictionary<Guid, ChartChoice>> ResolveChartAsync(TreatmentPlan plan, IReadOnlyList<TreatmentPlanItem> items, CancellationToken ct)
    {
        var diagnosisIds = items.Select(i => i.DiagnosisId).Distinct().ToList();
        var links = await db.DiagnosisLinks.AsNoTracking().Where(l => diagnosisIds.Contains(l.DiagnosisId) && l.LinkType == DiagnosisLinkTypes.Finding && l.PatientId == plan.PatientId).ToListAsync(ct);
        var findingIds = links.Select(l => l.TargetId).Distinct().ToList();
        var findings = await db.ToothFindings.AsNoTracking().Where(f => findingIds.Contains(f.Id) && f.PatientId == plan.PatientId && f.Status == FindingStatuses.Active).ToDictionaryAsync(f => f.Id, ct);

        var movedInThisSet = new HashSet<Guid>();
        var choices = new Dictionary<Guid, ChartChoice>();
        foreach (var item in items.OrderBy(i => i.ItemNumber))
        {
            var choice = Choose(item, links, findings, movedInThisSet);
            if (choice.Outcome == ChartOutcomes.Updated) movedInThisSet.Add(choice.FindingId!.Value);
            choices[item.Id] = choice;
        }
        return choices;
    }

    private static ChartChoice Choose(TreatmentPlanItem item, IReadOnlyList<DiagnosisLink> links, IReadOnlyDictionary<Guid, ToothFinding> findings, ISet<Guid> movedInThisSet)
    {
        var none = new ChartChoice(ChartOutcomes.NotApplicable, null, 0);
        if (item.ToothKey is null) return none;                                                    // whole-mouth work has no tooth to chart

        var candidates = links.Where(l => l.DiagnosisId == item.DiagnosisId)
            .Select(l => findings.GetValueOrDefault(l.TargetId))
            .Where(f => f is not null && f.ToothKey == item.ToothKey && (item.Surface is null || f.Surface == item.Surface))
            .Select(f => f!).Distinct().ToList();
        var done = candidates.Where(f => f.State == FindingStates.Completed || movedInThisSet.Contains(f.Id)).ToList();
        var movable = candidates.Except(done).Where(f => f.State is FindingStates.Diagnosed or FindingStates.Planned).ToList();

        if (movable.Count > 1) return new ChartChoice(Ambiguous, null, movable.Count);
        if (movable.Count == 1) return new ChartChoice(ChartOutcomes.Updated, movable[0].Id, 1);
        if (done.Count == 1) return new ChartChoice(ChartOutcomes.AlreadyCompleted, done[0].Id, 1);
        if (done.Count > 1) return new ChartChoice(Ambiguous, null, done.Count);
        return none;                                                                               // no linked finding, or one that cannot move (an existing condition)
    }

    /// <summary>More than one finding could be the procedure's: nothing is completed, and each procedure is named with how many findings matched (no clinical detail).</summary>
    private static void RequireNoAmbiguity(IReadOnlyList<TreatmentPlanItem> items, IReadOnlyDictionary<Guid, ChartChoice> choices)
    {
        var ambiguous = items.Where(i => choices[i.Id].Outcome == Ambiguous)
            .Select(i => new ChartAmbiguity(i.Id, i.ItemNumber, i.ToothKey!, i.Surface, choices[i.Id].Matches)).OrderBy(a => a.ItemNumber).ToList();
        if (ambiguous.Count > 0) throw new AmbiguousChartFindingException(ambiguous);
    }

    /// <summary>Stages the move of each matched finding to Completed. A refusal by the chart's own rules (for example completing an extraction while other findings stand on the tooth) refuses the whole set.</summary>
    private async Task StageChartAsync(IReadOnlyList<TreatmentPlanItem> items, IReadOnlyDictionary<Guid, ChartChoice> choices, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var item in items.OrderBy(i => i.ItemNumber).Where(i => choices[i.Id].Outcome == ChartOutcomes.Updated))
        {
            try
            {
                await chart.StageCompletedAsync(choices[item.Id].FindingId!.Value, actor, now, ct);
            }
            catch (OdontogramException ex)
            {
                throw new TreatmentPlanException("chart_update_refused", $"The chart could not be updated for procedure {item.ItemNumber}, so nothing was completed. {ex.Message}", 409, inner: ex);
            }
        }
    }
}
