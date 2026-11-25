using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling.Checkout;

/// <summary>
/// ALV-N021 A2 (OD-24, OD-26 to OD-28, OD-31): records one checkout decision for a visit that is ready for checkout. Writes exactly one <see cref="VisitCheckoutOutcome"/>, one appointment history event
/// and one PHI-free audit entry, in one save; never a payment, charge, claim, waiver or any ledger row (the ledger is only read to verify).
///
/// - <b>Who</b>: patient left, patient declined and deferred: <see cref="Permission.UpdateVisitFlow"/>. Not clinically required: only the treating provider (the account linked to the visit's provider).
///   Payment recorded and waived: UpdateVisitFlow, <see cref="Permission.CollectPayments"/> or <see cref="Permission.ManageBilling"/>. Payment plan, insurance to bill, send statement, waiver requested:
///   UpdateVisitFlow or ManageBilling, never a clinical permission (OD-28). Anyone else: 403.
/// - <b>Verified outcomes</b>: payment recorded names a live (unreversed) Payment of the patient's account recorded on the visit's practice day; waived names a live negative Adjustment of that day
///   (adjustments are made only by ManageBilling holders). Otherwise 409 <c>checkout_outcome_unverified</c>.
/// - <b>Corrections</b> are new rows superseding the current outcome of the item (OD-31); recording the outcome the item already has is a no-op. Two corrections racing for the same current outcome:
///   one wins, the other is 409 <c>checkout_stale</c> (the database allows a row to be superseded once).
/// - <b>Retries</b>: the same actor and Idempotency-Key return the first result and write nothing; the same key with a different request is 409 <c>idempotency_conflict</c>.
/// Failure modes handled: unknown appointment or item (404), malformed outcome or reason (400), visit not ready for checkout (409 <c>checkout_not_open</c>), forbidden actor (403), unverifiable ledger
/// entry, racing correction, conflicting retry. A failed save writes nothing.
/// </summary>
public sealed class CheckoutRecorder(AlveraDbContext db, IPracticeClock clock, IEffectivePermissions permissions)
{
    public const string AuditEvent = "VisitCheckoutRecorded";
    public const string HistoryEvent = "CheckoutRecorded";

    public async Task<CheckoutReadiness> RecordAsync(Guid appointmentId, string? item, RecordCheckoutOutcomeRequest request, Guid actor, Role role, string? idempotencyKey, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > CheckoutOutcomes.KeyMax
            ? throw new CheckoutException("idempotency_key_required", $"Send an Idempotency-Key of 1 to {CheckoutOutcomes.KeyMax} characters.", 400) : idempotencyKey.Trim();
        if (item is null || !CheckoutOutcomes.ByItem.TryGetValue(item, out var allowed))
            throw new CheckoutException("checkout_item_unknown", "That checkout item does not exist (departure, follow_up or financial).", 404);
        var outcome = request.Outcome?.Trim();
        if (outcome is null || !allowed.Contains(outcome))
            throw new CheckoutException("validation_failed", $"The outcome for {item} must be one of: {string.Join(", ", allowed)}.", 400);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > CheckoutOutcomes.ReasonMax }) throw new CheckoutException("validation_failed", $"Keep the reason to {CheckoutOutcomes.ReasonMax} characters.", 400);
        if (reason is null && CheckoutOutcomes.NeedReason.Contains(outcome)) throw new CheckoutException("reason_required", "Give a reason for this outcome.", 400);
        var needsLedger = CheckoutOutcomes.NeedLedgerEntry.Contains(outcome);
        if (needsLedger != request.LedgerEntryNumber is not null)
            throw new CheckoutException("validation_failed", needsLedger ? "Name the ledger entry (its number) this outcome relies on." : "Only a recorded payment or waiver names a ledger entry.", 400);

        var replay = await db.VisitCheckoutOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.ActorUserId == actor && o.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            if (replay.AppointmentId != appointmentId || replay.ItemCode != item || replay.OutcomeCode != outcome || replay.Reason != reason || replay.LinkedLedgerEntryNumber != request.LedgerEntryNumber)
                throw new CheckoutException("idempotency_conflict", "That Idempotency-Key was already used for a different checkout request.", 409);
            return await new CheckoutEvaluator(db, clock).EvaluateAsync(appointmentId, ct);
        }

        var a = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == appointmentId, ct)
            ?? throw new CheckoutException("appointment_not_found", "That appointment was not found.", 404);
        await AuthorizeAsync(a, outcome, actor, role, ct);
        if (a.Status != AppointmentStatuses.Scheduled || a.FlowState != VisitStates.CheckedOut)
            throw new CheckoutException("checkout_not_open", "Checkout decisions are recorded once treatment has ended and before the visit is completed.", 409);

        var evaluator = new CheckoutEvaluator(db, clock);
        if (needsLedger) await VerifyLedgerAsync(evaluator, a, outcome, request.LedgerEntryNumber!.Value, ct);

        var rows = await db.VisitCheckoutOutcomes.AsNoTracking().Where(o => o.AppointmentId == appointmentId && o.ItemCode == item).ToListAsync(ct);
        var current = CheckoutEvaluator.Current(rows).GetValueOrDefault(item);
        if (current is not null && current.OutcomeCode == outcome && current.Reason == reason && current.LinkedLedgerEntryNumber == request.LedgerEntryNumber)
            return await evaluator.EvaluateAsync(appointmentId, ct);                                        // already exactly this: nothing to record

        var now = clock.UtcNow;
        var row = new VisitCheckoutOutcome
        {
            Id = Guid.NewGuid(), AppointmentId = appointmentId, ItemCode = item, OutcomeCode = outcome, Reason = reason, LinkedLedgerEntryNumber = request.LedgerEntryNumber,
            ActorUserId = actor, RecordedAtUtc = current is not null && current.RecordedAtUtc > now ? current.RecordedAtUtc : now, SupersedesId = current?.Id, IdempotencyKey = key,
        };
        db.VisitCheckoutOutcomes.Add(row);
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = appointmentId, EventType = HistoryEvent, ActorUserId = actor, OccurredAtUtc = now, Reason = reason,
            Detail = current is null ? $"{item}: {outcome}" : $"{item}: {current.OutcomeCode} -> {outcome}",
        });
        AuditService.Record(db, AuditEvent, nameof(Appointment), appointmentId, actor, current is null ? $"Checkout {item} recorded as {outcome}." : $"Checkout {item} corrected to {outcome}.");
        try
        {
            // under the visit's checkout lock, so a completion and this decision never interleave; the visit must still be ready for checkout once the lock is held
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            await SchedulingGuards.AcquireLockAsync(db, AppointmentFlowService.CheckoutLock(appointmentId), ct);
            var state = await db.Appointments.AsNoTracking().Where(x => x.Id == appointmentId).Select(x => new { x.Status, x.FlowState }).SingleAsync(ct);
            if (state.Status != AppointmentStatuses.Scheduled || state.FlowState != VisitStates.CheckedOut)
                throw new CheckoutException("checkout_not_open", "Checkout decisions are recorded once treatment has ended and before the visit is completed.", 409);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sql)
        {
            db.ChangeTracker.Clear();
            if (sql.Message.Contains("IdempotencyKey")) return await RecordAsync(appointmentId, item, request, actor, role, key, ct);   // a concurrent retry won: answer as a replay
            throw new CheckoutException("checkout_stale", "Someone else changed this checkout item at the same moment. Look at it again and retry.", 409);
        }
        return await evaluator.EvaluateAsync(appointmentId, ct);
    }

    private async Task AuthorizeAsync(Appointment a, string outcome, Guid actor, Role role, CancellationToken ct)
    {
        bool allowed;
        if (outcome == CheckoutOutcomes.NotClinicallyRequired)
        {
            var provider = a.VisitProviderProfileId ?? a.ProviderProfileId;
            // ALV-N030-C01 (brief 18.9, R08-2): Dentist, Hygienist or - so a Practice Owner who signs in as an Administrator can still act as the booked
            // dentist - Admin; ALWAYS with the actor's account linked to the visit's ACTIVE booked provider. Other roles stay refused even if wrongly linked: the
            // provider record has no controlled professional classification yet (ALV-N027 may replace the role part once it supplies one).
            allowed = role is Role.Dentist or Role.Hygienist or Role.Admin && await db.ProviderProfiles.AsNoTracking()
                .AnyAsync(p => p.Id == provider && p.IsActive && p.StaffProfile != null && p.StaffProfile.UserAccountId == actor, ct);
        }
        else if (CheckoutOutcomes.NeedLedgerEntry.Contains(outcome))
            allowed = await AnyAsync(role, ct, Permission.UpdateVisitFlow, Permission.CollectPayments, Permission.ManageBilling);
        else if (outcome is CheckoutOutcomes.PaymentPlan or CheckoutOutcomes.InsuranceToBill or CheckoutOutcomes.SendStatement or CheckoutOutcomes.WaiverRequested)
            allowed = await AnyAsync(role, ct, Permission.UpdateVisitFlow, Permission.ManageBilling);
        else
            allowed = await AnyAsync(role, ct, Permission.UpdateVisitFlow);
        if (!allowed) throw new CheckoutException("forbidden", "Your role may not record this checkout outcome.", 403);
    }

    private async Task<bool> AnyAsync(Role role, CancellationToken ct, params Permission[] any)
    {
        foreach (var p in any) if (await permissions.HasAsync(role, p, ct)) return true;
        return false;
    }

    private static async Task VerifyLedgerAsync(CheckoutEvaluator evaluator, Appointment a, string outcome, int number, CancellationToken ct)
    {
        var ledger = await evaluator.LedgerAsync(a.PatientId, ct);
        var entry = ledger.Entries.SingleOrDefault(e => e.EntryNumber == number);
        var kindFits = outcome == CheckoutOutcomes.PaymentRecorded ? entry?.Kind == BillingKinds.Payment : entry?.Kind == BillingKinds.Adjustment && entry.Amount < 0m;
        if (entry is null || !kindFits || !ledger.Live.Contains(number) || evaluator.DayOf(entry.RecordedAtUtc) != evaluator.VisitDay(a))
            throw new CheckoutException("checkout_outcome_unverified",
                outcome == CheckoutOutcomes.PaymentRecorded
                    ? "No payment with that number was posted to this patient's account on the day of the visit (or it was reversed)."
                    : "No waiver adjustment with that number was posted to this patient's account on the day of the visit (or it was reversed).", 409);
    }
}
