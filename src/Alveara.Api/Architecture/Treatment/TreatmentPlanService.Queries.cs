using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// The read side of treatment plans: a plan with its items, a patient's plans, and a plan's history. Everything a screen needs to show an item (the diagnosis label, the procedure code and wording)
/// is read here, so nothing on screen has to be looked up separately. The fee shown is always the fee copied onto the item, never today's catalog fee.
/// </summary>
public partial class TreatmentPlanService
{
    public async Task<PlanView> GetAsync(Guid planId, CancellationToken ct) =>
        (await ViewsAsync(db.TreatmentPlans.AsNoTracking().Where(p => p.Id == planId), ct)).SingleOrDefault() ?? throw PlanNotFound();

    /// <summary>A patient's plans, newest first. Withdrawn plans are left out unless asked for.</summary>
    public async Task<IReadOnlyList<PlanView>> ListAsync(Guid patientId, bool includeWithdrawn, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw PatientNotFound();
        return await ViewsAsync(db.TreatmentPlans.AsNoTracking().Where(p => p.PatientId == patientId && (includeWithdrawn || p.Status != TreatmentPlanStatuses.Withdrawn)), ct);
    }

    public async Task<IReadOnlyList<PlanEventView>> HistoryAsync(Guid planId, CancellationToken ct)
    {
        if (!await db.TreatmentPlans.AsNoTracking().AnyAsync(p => p.Id == planId, ct)) throw PlanNotFound();
        var events = await db.TreatmentPlanEvents.AsNoTracking().Where(e => e.PlanId == planId).OrderBy(e => e.EventNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, events.Select(e => (Guid?)e.ActorUserId), ct);
        return events.Select(e => new PlanEventView(e.EventNumber, e.ChangeType, e.ItemId, e.Title, e.Reason, ClinicalNames.Name(names, e.ActorUserId), e.OccurredAtUtc)).ToList();
    }

    private async Task<IReadOnlyList<PlanView>> ViewsAsync(IQueryable<TreatmentPlan> query, CancellationToken ct)
    {
        var plans = await query.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id).ToListAsync(ct);
        if (plans.Count == 0) return [];
        var planIds = plans.Select(p => p.Id).ToList();
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => planIds.Contains(i.PlanId)).OrderBy(i => i.ItemNumber).ToListAsync(ct);

        var diagnosisIds = items.Select(i => i.DiagnosisId).Distinct().ToList();
        var labels = await db.Diagnoses.AsNoTracking().Where(d => diagnosisIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Label, ct);
        var procedureIds = items.Select(i => i.ProcedureId).Distinct().ToList();
        var procedures = await db.ProcedureDefinitions.AsNoTracking().Where(p => procedureIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.CodeSystem, p.Code }, ct);
        var versionIds = items.Select(i => i.ProcedureVersionId).Distinct().ToList();
        var versions = await db.ProcedureVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => new { v.VersionNumber, v.Description }, ct);
        var people = plans.SelectMany(p => new Guid?[] { p.CreatedByUserId, p.UpdatedByUserId, p.WithdrawnByUserId }).Concat(items.SelectMany(i => new Guid?[] { i.CreatedByUserId, i.WithdrawnByUserId }));
        var names = await ClinicalNames.ResolveAsync(db, people, ct);
        var itemsByPlan = items.ToLookup(i => i.PlanId);

        return plans.Select(p =>
        {
            var views = itemsByPlan[p.Id].Select(i => new PlanItemView(
                i.Id, i.ItemNumber, i.DiagnosisId, labels.GetValueOrDefault(i.DiagnosisId, ""), i.ProcedureId, i.ProcedureVersionId, versions[i.ProcedureVersionId].VersionNumber,
                procedures[i.ProcedureId].CodeSystem, procedures[i.ProcedureId].Code, versions[i.ProcedureVersionId].Description, i.ToothKey, i.Surface, i.Fee, Alveara.Api.Architecture.Money.Money.Currency,
                i.WithdrawnAtUtc is not null, i.WithdrawnReason, i.WithdrawnAtUtc, ClinicalNames.Name(names, i.WithdrawnByUserId), i.CreatedAtUtc, ClinicalNames.Name(names, i.CreatedByUserId))).ToList();
            var active = views.Where(v => !v.IsWithdrawn).ToList();
            return new PlanView(p.Id, p.PatientId, p.Title, p.Status, active.Count, active.Sum(v => v.Fee), Alveara.Api.Architecture.Money.Money.Currency, TreatmentPlanEstimate.Label, views,
                p.CreatedAtUtc, ClinicalNames.Name(names, p.CreatedByUserId), p.UpdatedAtUtc, ClinicalNames.Name(names, p.UpdatedByUserId), p.WithdrawnReason, p.WithdrawnAtUtc,
                ClinicalNames.Name(names, p.WithdrawnByUserId), Convert.ToBase64String(p.RowVersion));
        }).ToList();
    }
}
