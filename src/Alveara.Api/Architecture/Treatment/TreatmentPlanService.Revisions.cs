using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-015-C01: revisions. Every change to a plan's content (create, add or withdraw a procedure, rename, or an explicit revision of the rationale, explanation, phases and placement) saves a new
/// <see cref="TreatmentPlanRevision"/> that is a copy of the previous one with the change applied, in the same save as the change, its history entry and its audit entry. An earlier revision is never
/// touched (the database refuses it), so every version a patient or a colleague saw stays readable. The plan row only remembers which revision is current.
/// </summary>
public partial class TreatmentPlanService
{
    /// <summary>
    /// An explicit revision: the rationale, the patient explanation, the phases and where each procedure sits. Null parts are kept as they are. It needs a reason and the plan's current version, and a
    /// withdrawn plan cannot be revised. Sending what the plan already says saves nothing and returns the plan.
    /// </summary>
    public async Task<PlanView> ReviseAsync(Guid planId, ReviseInput input, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        RequireProposed(plan);

        var errors = new Dictionary<string, string>();
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        if (reason is null) errors["reason"] = "Say why this plan is being revised.";
        else if (reason.Length > TreatmentPlanRules.ReasonMax) errors["reason"] = $"The reason can be at most {TreatmentPlanRules.ReasonMax} characters; this one has {reason.Length}.";
        var rationale = TreatmentPlanRevisionRules.CleanText(errors, "clinicalRationale", input.ClinicalRationale, TreatmentPlanRules.TextMax);
        var explanation = TreatmentPlanRevisionRules.CleanText(errors, "patientExplanation", input.PatientExplanation, TreatmentPlanRules.TextMax);
        var phases = TreatmentPlanRevisionRules.CleanPhases(errors, input.Phases, Today());

        var current = await LayoutAsync(planId, plan.CurrentRevision, ct);
        var active = (await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId && i.WithdrawnAtUtc == null).Select(i => i.Id).ToListAsync(ct)).ToHashSet();
        IReadOnlyDictionary<Guid, int?>? placement = null;
        if (input.Phases is null || phases is not null) placement = TreatmentPlanRevisionRules.CleanPlacement(errors, input.Placements, current.Placement, active, phases ?? current.Phases);
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);

        var next = new RevisionLayout(TreatmentPlanRevisionRules.Resolve(rationale, current.Rationale), TreatmentPlanRevisionRules.Resolve(explanation, current.Explanation), phases ?? current.Phases, placement!);
        if (next.SameAs(current)) return await GetAsync(planId, ct);

        db.Entry(plan).Property(p => p.RowVersion).OriginalValue = version;
        var now = clock.UtcNow;
        var number = plan.CurrentRevision + 1;
        var eventNumber = await NextEventAsync(planId, ct);
        Touch(plan, actor, now);
        StageEvent(plan, eventNumber, TreatmentPlanChanges.Revised, null, null, reason, actor, now, number);
        AuditService.Record(db, "TreatmentPlanRevised", nameof(TreatmentPlan), planId, actor, $"Treatment plan revised (revision {number}).", reason);
        await StageRevisionAsync(plan, number, TreatmentPlanChanges.Revised, eventNumber, reason, actor, now, next, ct);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>The content of one revision of a plan; empty for a plan that has none yet.</summary>
    private async Task<RevisionLayout> LayoutAsync(Guid planId, int number, CancellationToken ct)
    {
        var revision = await db.TreatmentPlanRevisions.AsNoTracking().SingleOrDefaultAsync(r => r.PlanId == planId && r.RevisionNumber == number, ct);
        if (revision is null) return new RevisionLayout(null, null, [], new Dictionary<Guid, int?>());
        var phases = await db.TreatmentPlanRevisionPhases.AsNoTracking().Where(p => p.RevisionId == revision.Id).OrderBy(p => p.PhaseNumber)
            .Select(p => new LayoutPhase(p.PhaseNumber, p.Name, p.PlannedVisit, p.TargetDate)).ToListAsync(ct);
        var placement = await db.TreatmentPlanRevisionItems.AsNoTracking().Where(i => i.RevisionId == revision.Id).ToDictionaryAsync(i => i.ItemId, i => i.PhaseNumber, ct);
        return new RevisionLayout(revision.ClinicalRationale, revision.PatientExplanation, phases, placement);
    }

    /// <summary>
    /// Stages revision <paramref name="number"/> in the same save as the change that caused it: the plan's title and procedures as they stand after the change (the caller has already applied it), with the
    /// rationale, explanation, phases and placement of <paramref name="layout"/> (the previous revision's when null). A procedure not in the layout is not yet placed.
    /// </summary>
    private async Task StageRevisionAsync(TreatmentPlan plan, int number, string changeType, int eventNumber, string? reason, Guid actor, DateTimeOffset now, RevisionLayout? layout, CancellationToken ct)
    {
        layout ??= number == 1 ? new RevisionLayout(null, null, [], new Dictionary<Guid, int?>()) : await LayoutAsync(plan.Id, number - 1, ct);
        await db.TreatmentPlanItems.Where(i => i.PlanId == plan.Id).LoadAsync(ct);                       // so a procedure withdrawn in this very save is seen as withdrawn
        var items = db.TreatmentPlanItems.Local.Where(i => i.PlanId == plan.Id).OrderBy(i => i.ItemNumber).ToList();

        var revision = new TreatmentPlanRevision
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = plan.PatientId, RevisionNumber = number, ChangeType = changeType, EventNumber = eventNumber, Title = plan.Title,
            ClinicalRationale = layout.Rationale, PatientExplanation = layout.Explanation, Reason = reason, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.TreatmentPlanRevisions.Add(revision);
        foreach (var phase in layout.Phases)
            db.TreatmentPlanRevisionPhases.Add(new TreatmentPlanRevisionPhase { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, PhaseNumber = phase.Number, Name = phase.Name, PlannedVisit = phase.PlannedVisit, TargetDate = phase.TargetDate });
        foreach (var item in items)
        {
            var phase = layout.Placement.GetValueOrDefault(item.Id);
            if (phase is { } n && layout.Phases.All(p => p.Number != n)) phase = null;
            db.TreatmentPlanRevisionItems.Add(new TreatmentPlanRevisionItem { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, ItemId = item.Id, PhaseNumber = phase, IsWithdrawn = item.WithdrawnAtUtc is not null });
        }
        plan.CurrentRevision = number;
    }
}
