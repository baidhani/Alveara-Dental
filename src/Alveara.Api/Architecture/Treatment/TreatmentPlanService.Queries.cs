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
        return events.Select(e => new PlanEventView(e.EventNumber, e.ChangeType, e.ItemId, e.Title, e.Reason, ClinicalNames.Name(names, e.ActorUserId), e.OccurredAtUtc, e.RevisionNumber)).ToList();
    }

    /// <summary>Where the plan stands as a whole, from its procedures that have not been withdrawn: all accepted, all declined, all still pending, or a mixture (partial).</summary>
    private static string StatusOf(IReadOnlyList<PlanItemView> active)
    {
        if (active.Count == 0 || active.All(v => v.Acceptance == ItemAcceptance.Pending)) return ItemAcceptance.Pending;
        if (active.All(v => v.Acceptance == ItemAcceptance.Accepted)) return ItemAcceptance.Accepted;
        if (active.All(v => v.Acceptance == ItemAcceptance.Declined)) return ItemAcceptance.Declined;
        return "Partial";
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
        // the latest acceptance, scheduling and completion state of each procedure (a procedure with no row is Pending, Unscheduled and NotStarted)
        var states = (await db.TreatmentPlanItemStates.AsNoTracking().Where(s => planIds.Contains(s.PlanId)).ToListAsync(ct))
            .GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Sequence).First());
        var people = plans.SelectMany(p => new Guid?[] { p.CreatedByUserId, p.UpdatedByUserId, p.WithdrawnByUserId }).Concat(items.SelectMany(i => new Guid?[] { i.CreatedByUserId, i.WithdrawnByUserId }))
            .Concat(states.Values.Select(s => (Guid?)s.RecordedByUserId));
        var names = await ClinicalNames.ResolveAsync(db, people, ct);
        var itemsByPlan = items.ToLookup(i => i.PlanId);

        // the plan's current revision: its words, its phases and where each procedure sits
        var current = await (from r in db.TreatmentPlanRevisions.AsNoTracking() join p in db.TreatmentPlans.AsNoTracking() on r.PlanId equals p.Id
                             where planIds.Contains(p.Id) && r.RevisionNumber == p.CurrentRevision select r).ToDictionaryAsync(r => r.PlanId, ct);
        var revisionIds = current.Values.Select(r => r.Id).ToList();
        var phasesByRevision = (await db.TreatmentPlanRevisionPhases.AsNoTracking().Where(p => revisionIds.Contains(p.RevisionId)).OrderBy(p => p.PhaseNumber).ToListAsync(ct)).ToLookup(p => p.RevisionId);
        var placed = (await db.TreatmentPlanRevisionItems.AsNoTracking().Where(i => revisionIds.Contains(i.RevisionId)).ToListAsync(ct)).ToLookup(i => i.RevisionId);

        return plans.Select(p =>
        {
            current.TryGetValue(p.Id, out var revision);
            var phase = revision is null ? new Dictionary<Guid, int?>() : placed[revision.Id].ToDictionary(x => x.ItemId, x => x.PhaseNumber);
            var views = itemsByPlan[p.Id].Select(i => new PlanItemView(
                i.Id, i.ItemNumber, i.DiagnosisId, labels.GetValueOrDefault(i.DiagnosisId, ""), i.ProcedureId, i.ProcedureVersionId, versions[i.ProcedureVersionId].VersionNumber,
                procedures[i.ProcedureId].CodeSystem, procedures[i.ProcedureId].Code, versions[i.ProcedureVersionId].Description, i.ToothKey, i.Surface, i.Fee, Alveara.Api.Architecture.Money.Money.Currency,
                i.WithdrawnAtUtc is not null, i.WithdrawnReason, i.WithdrawnAtUtc, ClinicalNames.Name(names, i.WithdrawnByUserId), i.CreatedAtUtc, ClinicalNames.Name(names, i.CreatedByUserId),
                phase.GetValueOrDefault(i.Id), AcceptanceOf(states.GetValueOrDefault(i.Id)), SchedulingOf(states.GetValueOrDefault(i.Id)), CompletionOf(states.GetValueOrDefault(i.Id)),
                states.GetValueOrDefault(i.Id)?.PlannedDate, states.GetValueOrDefault(i.Id)?.Reason, states.GetValueOrDefault(i.Id)?.DecidedByName,
                ClinicalNames.Name(names, states.GetValueOrDefault(i.Id)?.RecordedByUserId), states.GetValueOrDefault(i.Id)?.RecordedAtUtc, states.GetValueOrDefault(i.Id)?.RevisionNumber)).ToList();
            var active = views.Where(v => !v.IsWithdrawn).ToList();
            return new PlanView(p.Id, p.PatientId, p.Title, p.Status, active.Count, active.Sum(v => v.Fee), Alveara.Api.Architecture.Money.Money.Currency, TreatmentPlanEstimate.Label, views,
                p.CreatedAtUtc, ClinicalNames.Name(names, p.CreatedByUserId), p.UpdatedAtUtc, ClinicalNames.Name(names, p.UpdatedByUserId), p.WithdrawnReason, p.WithdrawnAtUtc,
                ClinicalNames.Name(names, p.WithdrawnByUserId), Convert.ToBase64String(p.RowVersion),
                p.CurrentRevision, p.ReferenceCode, revision?.ClinicalRationale, revision?.PatientExplanation,
                revision is null ? [] : phasesByRevision[revision.Id].Select(x => new PlanPhaseView(x.PhaseNumber, x.Name, x.PlannedVisit, x.TargetDate)).ToList(),
                StatusOf(active), active.Where(v => v.Acceptance == ItemAcceptance.Accepted).Sum(v => v.Fee));
        }).ToList();
    }
}
