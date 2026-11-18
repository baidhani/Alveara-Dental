using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// ALV-N023 (owner decision OQ-1; briefing revision 2, S14): the front desk names the patient's accepted, not-yet-booked procedures while booking. The appointment and its link rows (with a plan
/// history event and a PHI-free audit row each) are written in the SAME save, so a retry with the same Idempotency-Key never makes a second appointment or link, and a refused item books nothing.
/// It never changes an answer, a state, a fee, a revision or any clinical field. The database refuses a link to another patient's appointment (51291) whatever happens here.
/// </summary>
internal static class BookingTreatmentLinks
{
    public const int MaxItems = 20;
    private const string Refusal = "Choose accepted procedures of this patient that are not completed, not withdrawn and not already booked.";

    public sealed record Checked(TreatmentPlanItem Item, TreatmentPlan Plan, string Code, int NextLinkSequence);

    /// <summary>Checks every named item before anything is written; any wrong item refuses the whole booking (400 plan_item_invalid).</summary>
    public static async Task<IReadOnlyList<Checked>> CheckAsync(AlveraDbContext db, Guid patientId, IReadOnlyList<Guid>? itemIds, CancellationToken ct)
    {
        var ids = (itemIds ?? []).Distinct().ToList();
        if (ids.Count == 0) return [];
        if (ids.Count > MaxItems) throw new SchedulingException("plan_item_invalid", $"An appointment can book at most {MaxItems} procedures at once.", 400);
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        if (items.Count != ids.Count || items.Any(i => i.PatientId != patientId || i.WithdrawnAtUtc is not null)) throw new SchedulingException("plan_item_invalid", Refusal, 400);
        var planIds = items.Select(i => i.PlanId).Distinct().ToList();
        var plans = await db.TreatmentPlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (plans.Values.Any(p => p.Status != TreatmentPlanStatuses.Proposed)) throw new SchedulingException("plan_item_invalid", Refusal, 400);
        var states = (await db.TreatmentPlanItemStates.AsNoTracking().Where(s => ids.Contains(s.ItemId)).ToListAsync(ct)).GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Sequence).First());
        if (ids.Any(id => states.GetValueOrDefault(id) is not { Acceptance: ItemAcceptance.Accepted } s || s.Completion == ItemCompletion.Completed)) throw new SchedulingException("plan_item_invalid", Refusal, 400);
        var links = await Recall.UnscheduledTreatmentRules.LinksAsync(db, ids, ct);
        if (links.Values.Any(l => l.Live)) throw new SchedulingException("plan_item_invalid", Refusal, 400);
        var procedureIds = items.Select(i => i.ProcedureId).Distinct().ToList();
        var codes = await db.ProcedureDefinitions.AsNoTracking().Where(d => procedureIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct);
        return items.OrderBy(i => i.PlanId).ThenBy(i => i.ItemNumber)
            .Select(i => new Checked(i, plans[i.PlanId], codes[i.ProcedureId], (links.GetValueOrDefault(i.Id)?.Sequence ?? 0) + 1)).ToList();
    }

    /// <summary>Stages the link rows, one plan history event per item and a PHI-free audit row per item, in the caller's save.</summary>
    public static async Task StageAsync(AlveraDbContext db, IPracticeClock clock, Appointment appointment, IReadOnlyList<Checked> items, Guid actor, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var planIds = items.Select(c => c.Plan.Id).Distinct().ToList();
        var nextEvent = (await db.TreatmentPlanEvents.AsNoTracking().Where(e => planIds.Contains(e.PlanId)).GroupBy(e => e.PlanId).Select(g => new { g.Key, Max = g.Max(e => e.EventNumber) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Max + 1);
        var day = clock.ToPracticeLocal(appointment.StartUtc).ToString("yyyy-MM-dd");
        foreach (var c in items)
        {
            db.TreatmentPlanItemAppointmentLinks.Add(new TreatmentPlanItemAppointmentLink
            {
                Id = Guid.NewGuid(), PlanId = c.Plan.Id, ItemId = c.Item.Id, PatientId = appointment.PatientId, AppointmentId = appointment.Id, Sequence = c.NextLinkSequence,
                Action = AppointmentLinkActions.Linked, RecordedByUserId = actor, RecordedAtUtc = appointment.CreatedAtUtc,
            });
            var number = nextEvent.GetValueOrDefault(c.Plan.Id, 1);
            nextEvent[c.Plan.Id] = number + 1;
            db.TreatmentPlanEvents.Add(new TreatmentPlanEvent
            {
                Id = Guid.NewGuid(), PlanId = c.Plan.Id, PatientId = c.Plan.PatientId, EventNumber = number, ChangeType = TreatmentPlanChanges.AppointmentLinked, ItemId = c.Item.Id,
                RevisionNumber = c.Plan.CurrentRevision >= 1 ? c.Plan.CurrentRevision : null, ActorUserId = actor, OccurredAtUtc = appointment.CreatedAtUtc,
            });
            AuditService.Record(db, "TreatmentPlanItemAppointmentLinked", nameof(TreatmentPlan), c.Plan.Id, actor,
                $"Procedure {c.Code} (item {c.Item.ItemNumber}) of the treatment plan {(c.Plan.ReferenceCode is null ? "without a reference code" : $"with reference {c.Plan.ReferenceCode}")} booked by an appointment on {day} (while booking).");
        }
    }
}
