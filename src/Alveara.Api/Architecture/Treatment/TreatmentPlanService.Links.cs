using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Scheduling;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-N023 (owner decisions DR-1 and OQ-1; briefing revision 2 sections 2.1 S2, 4): links a real appointment to an accepted plan item, or removes that link with a reason. This is an
/// administrative scheduling relationship: it writes only a link row, a plan history event and a PHI-free audit row - never an answer, a state row, a fee, a revision or any clinical field -
/// so holders of ManageAppointments (front desk, office manager) may do it as well as holders of ManageTreatmentPlans. The result is deliberately minimal (no plan title or diagnosis).
/// Linking the item's current appointment again is quiet; the database refuses another patient's appointment (51291) and any edit of a link row (51292).
/// </summary>
public partial class TreatmentPlanService
{
    /// <summary>The database guard numbers (migration AddTreatmentPlanItemAppointmentLinks).</summary>
    internal const int LinkSamePatientGuard = 51291;
    internal const int LinkAppendOnlyGuard = 51292;

    public async Task<ItemAppointmentResult> LinkAppointmentAsync(Guid planId, Guid itemId, Guid? appointmentId, Guid actor, CancellationToken ct)
    {
        var (plan, item) = await LoadLinkableAsync(planId, itemId, ct);
        if (appointmentId is null) throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["appointmentId"] = "Choose the appointment that books this procedure." });
        var appointment = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId.Value, ct);
        if (appointment is null || appointment.PatientId != plan.PatientId)
            throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["appointmentId"] = "Choose one of this patient's appointments." });
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new TreatmentPlanException("appointment_not_active", "That appointment was cancelled or marked no-show, so it cannot book this procedure. Choose another appointment.", 409);

        var latest = await LatestLinkAsync(itemId, ct);
        if (latest is { Action: AppointmentLinkActions.Linked } && latest.AppointmentId == appointment.Id) return await LinkResultAsync(plan.Id, itemId, ct);   // already so: quiet

        var now = clock.UtcNow;
        StageLink(plan, item, appointment.Id, (latest?.Sequence ?? 0) + 1, AppointmentLinkActions.Linked, null, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.AppointmentLinked, itemId, null, null, actor, now, plan.CurrentRevision);
        AuditService.Record(db, "TreatmentPlanItemAppointmentLinked", nameof(TreatmentPlan), planId, actor,
            $"{await ItemLabelAsync(plan, item, ct)} booked by an appointment on {DayOf(appointment.StartUtc)}.");
        await SaveLinkAsync(planId, itemId, appointment.Id, ct);
        return await LinkResultAsync(plan.Id, itemId, ct);
    }

    public async Task<ItemAppointmentResult> UnlinkAppointmentAsync(Guid planId, Guid itemId, string? reason, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var item = await db.TreatmentPlanItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.PlanId == planId, ct) ?? throw ItemNotFound();
        RequireProposed(plan);
        await RequireUnfinishedAsync(plan, ct);
        var why = TreatmentPlanRules.RequireReason(reason, "Say why this appointment no longer books the procedure.");
        var latest = await LatestLinkAsync(itemId, ct);
        if (latest is not { Action: AppointmentLinkActions.Linked }) return await LinkResultAsync(plan.Id, itemId, ct);   // nothing linked: quiet

        var appointment = await db.Appointments.AsNoTracking().Where(a => a.Id == latest.AppointmentId).Select(a => new { a.StartUtc }).SingleAsync(ct);
        var now = clock.UtcNow;
        StageLink(plan, item, latest.AppointmentId, latest.Sequence + 1, AppointmentLinkActions.Unlinked, why, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.AppointmentUnlinked, itemId, null, why, actor, now, plan.CurrentRevision);
        AuditService.Record(db, "TreatmentPlanItemAppointmentUnlinked", nameof(TreatmentPlan), planId, actor,
            $"{await ItemLabelAsync(plan, item, ct)} no longer booked by the appointment on {DayOf(appointment.StartUtc)}.", why);
        await SaveLinkAsync(planId, itemId, null, ct);
        return await LinkResultAsync(plan.Id, itemId, ct);
    }

    /// <summary>The current appointment of each item (latest link row when Linked), read live from the appointment, for the plan view and the link result.</summary>
    internal async Task<Dictionary<Guid, ItemAppointmentView>> CurrentAppointmentsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];
        var rows = (await db.TreatmentPlanItemAppointmentLinks.AsNoTracking().Where(l => itemIds.Contains(l.ItemId)).ToListAsync(ct))
            .GroupBy(l => l.ItemId).Select(g => g.OrderByDescending(l => l.Sequence).First()).Where(l => l.Action == AppointmentLinkActions.Linked).ToList();
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.AppointmentId).Distinct().ToList();
        var appointments = await (from a in db.Appointments.AsNoTracking()
                                  join pp in db.ProviderProfiles.AsNoTracking() on a.ProviderProfileId equals pp.Id into pj
                                  from pp in pj.DefaultIfEmpty()
                                  join sp in db.StaffProfiles.AsNoTracking() on pp.StaffProfileId equals sp.Id into sj
                                  from sp in sj.DefaultIfEmpty()
                                  where ids.Contains(a.Id)
                                  select new { a.Id, a.StartUtc, a.Status, Provider = sp == null ? null : sp.DisplayName }).ToDictionaryAsync(a => a.Id, ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.Select(r => (Guid?)r.RecordedByUserId), ct);
        return rows.ToDictionary(r => r.ItemId, r =>
        {
            var a = appointments[r.AppointmentId];
            return new ItemAppointmentView(a.Id, a.StartUtc, a.Provider, a.Status, a.Status == AppointmentStatuses.Scheduled, ClinicalNames.Name(names, r.RecordedByUserId), r.RecordedAtUtc);
        });
    }

    // ---------- shared ----------

    private async Task<(TreatmentPlan Plan, TreatmentPlanItem Item)> LoadLinkableAsync(Guid planId, Guid itemId, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var item = await db.TreatmentPlanItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.PlanId == planId, ct) ?? throw ItemNotFound();
        RequireProposed(plan);
        await RequireUnfinishedAsync(plan, ct);
        if (item.WithdrawnAtUtc is not null) throw new TreatmentPlanException("item_withdrawn", "That procedure has been withdrawn.", 409);
        var state = (await CurrentStatesAsync(planId, ct)).GetValueOrDefault(itemId);
        if (CompletionOf(state) == ItemCompletion.Completed) throw ItemCompleted();
        if (AcceptanceOf(state) != ItemAcceptance.Accepted) throw new TreatmentPlanException("item_not_accepted", "Only an accepted procedure can be booked. The patient's answer must be Accepted first.", 409);
        return (plan, item);
    }

    private Task<TreatmentPlanItemAppointmentLink?> LatestLinkAsync(Guid itemId, CancellationToken ct) =>
        db.TreatmentPlanItemAppointmentLinks.AsNoTracking().Where(l => l.ItemId == itemId).OrderByDescending(l => l.Sequence).FirstOrDefaultAsync(ct);

    private void StageLink(TreatmentPlan plan, TreatmentPlanItem item, Guid appointmentId, int sequence, string action, string? reason, Guid actor, DateTimeOffset now) =>
        db.TreatmentPlanItemAppointmentLinks.Add(new TreatmentPlanItemAppointmentLink
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, ItemId = item.Id, PatientId = plan.PatientId, AppointmentId = appointmentId, Sequence = sequence, Action = action, Reason = reason,
            RecordedByUserId = actor, RecordedAtUtc = now,
        });

    /// <summary>DR-7: the audit names the procedure by code and item number and the plan by its reference code - never the plan title or diagnosis.</summary>
    private async Task<string> ItemLabelAsync(TreatmentPlan plan, TreatmentPlanItem item, CancellationToken ct)
    {
        var code = await db.ProcedureDefinitions.AsNoTracking().Where(d => d.Id == item.ProcedureId).Select(d => d.Code).SingleAsync(ct);
        return $"Procedure {code} (item {item.ItemNumber}) of the treatment plan {(plan.ReferenceCode is null ? "without a reference code" : $"with reference {plan.ReferenceCode}")}";
    }

    private string DayOf(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-dd");

    /// <summary>Saves a link change. A same-moment duplicate (the unique item/sequence) re-reads: if the item now holds what was asked, the request was a retry and is quiet; otherwise a conflict.</summary>
    private async Task SaveLinkAsync(Guid planId, Guid itemId, Guid? wantedAppointment, CancellationToken ct)
    {
        try
        {
            await SaveAsync(planId, ct);
        }
        catch (ConcurrencyConflictException)
        {
            db.ChangeTracker.Clear();
            var latest = await LatestLinkAsync(itemId, ct);
            var holds = wantedAppointment is { } a ? latest is { Action: AppointmentLinkActions.Linked } && latest.AppointmentId == a : latest is not { Action: AppointmentLinkActions.Linked };
            if (!holds) throw;
        }
        catch (TreatmentPlanException ex) when (ex.InnerException is DbUpdateException { InnerException: SqlException { Number: LinkSamePatientGuard } })
        {
            throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["appointmentId"] = "Choose one of this patient's appointments." });
        }
    }

    private async Task<ItemAppointmentResult> LinkResultAsync(Guid planId, Guid itemId, CancellationToken ct) =>
        new(planId, itemId, (await CurrentAppointmentsAsync([itemId], ct)).GetValueOrDefault(itemId));
}
