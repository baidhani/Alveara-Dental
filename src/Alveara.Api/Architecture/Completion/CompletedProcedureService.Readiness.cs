using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// What a completion would do, before anyone submits it. The review screen shows this instead of working the rules out itself: which procedures can be completed (and why not), what each would do to the chart
/// and whether each would be charged. It uses the same refusal rule and the same chart resolution as the completion, reads only, and writes nothing. The server remains the authority: a submission is judged
/// again, and an ambiguous chart match is refused there too.
/// </summary>
public partial class CompletedProcedureService
{
    /// <summary>Readiness of the given procedures, or of every procedure that has not been withdrawn when none are given.</summary>
    public async Task<CompletionReadiness> ReadinessAsync(Guid planId, IReadOnlyList<Guid>? itemIds, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw TreatmentPlanService.MissingPlan();
        var view = await plans.GetAsync(planId, ct);
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId).OrderBy(i => i.ItemNumber).ToListAsync(ct);
        var wanted = itemIds is null || itemIds.Count == 0 ? items.Where(i => i.WithdrawnAtUtc is null).ToList() : items.Where(i => itemIds.Contains(i.Id)).ToList();
        if (itemIds is { Count: > 0 } && wanted.Count != itemIds.Distinct().Count())
            throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["itemIds"] = "Some of the selected procedures are not on this plan." });

        var latest = (await plans.StateHistoryAsync(planId, ct)).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Sequence).First());
        var reasons = wanted.ToDictionary(i => i.Id, i => RefusalReason(i, latest.GetValueOrDefault(i.Id)));
        var ready = wanted.Where(i => reasons[i.Id] is null).ToList();
        var choices = await ResolveChartAsync(plan, ready, ct);

        var ids = wanted.Select(i => i.Id).ToList();
        var charged = (await db.BillingEntries.AsNoTracking().Where(e => e.PlanItemId != null && ids.Contains(e.PlanItemId.Value)).Select(e => e.PlanItemId!.Value).ToListAsync(ct)).ToHashSet();
        var rows = wanted.Select(i =>
        {
            var v = view.Items.Single(x => x.Id == i.Id);
            var choice = choices.GetValueOrDefault(i.Id);
            return new ItemReadiness(i.Id, i.ItemNumber, v.ProcedureCode, v.ProcedureDescription, i.ToothKey, i.Surface, reasons[i.Id] is null && choice?.Outcome != Ambiguous, reasons[i.Id] ?? (choice?.Outcome == Ambiguous ? "ambiguous_chart_finding" : null),
                choice?.Outcome ?? ChartOutcomes.NotApplicable, choice?.Matches ?? 0, IsBillable(i), i.Fee, charged.Contains(i.Id));
        }).ToList();
        var providers = await (from p in db.ProviderProfiles.AsNoTracking() join s in db.StaffProfiles.AsNoTracking() on p.StaffProfileId equals s.Id where p.IsActive orderby s.DisplayName select new ProviderChoice(p.Id, s.DisplayName, p.Specialty)).ToListAsync(ct);
        var encounters = await db.Encounters.AsNoTracking().Where(e => e.PatientId == plan.PatientId).OrderByDescending(e => e.EncounterAtUtc).Take(50).Select(e => new EncounterChoice(e.Id, e.EncounterAtUtc, e.Status)).ToListAsync(ct);
        return new CompletionReadiness(planId, view.RowVersion, rows, providers, encounters);
    }
}
