using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// Moves a booked appointment through the visit and records every move. STORY-011 shipped check-in, start-treatment and complete; ALV-011-C01 grew this into the
/// whole chain (confirm, check in, ready, seat, start treatment, check out, complete) driven by <see cref="VisitStateMachine"/>. STORY-011's three methods are kept
/// and call the same code, so its tests exercise the new rules.
///
/// ALV-N021 A2: every route goes through <see cref="VisitMovePolicy"/> (who), <see cref="VisitStateMachine"/> with the practice's two settings (no shortcuts), the check-in day rule, and for
/// completion the checkout gate evaluated in the same transaction under the visit's checkout lock (409 <c>checkout_incomplete</c>, nothing written). A cancelled, no-show or completed
/// visit is final (409 <c>terminal_state</c>); a move not on the path is 409 <c>illegal_transition</c>.
///
/// - <b>One rule.</b> <see cref="VisitStateMachine"/> decides; this class never decides anything itself. Only an appointment whose Status is Scheduled has a
///   flow (a cancelled or no-show appointment is refused with <c>appointment_not_scheduled</c>).
/// - <b>Every move is logged in the same save as the state change</b>: an appointment history entry (who, when, from -> to) and a PHI-free audit entry. If either
///   write fails nothing is stored, so the state can never change without a record, and a failed save leaves the patient exactly where they were.
/// - <b>Repeats are harmless.</b> Asking for the state the appointment is already in changes nothing and writes nothing (a retried request, or two people
///   pressing the same button at once, ends in one move).
/// - <b>Every real move carries the row version the caller read</b>; a stale one is the shared 409 concurrency conflict, never applied on top of a change
///   someone else made.
/// - <b>One patient per room.</b> Seating a patient (or starting treatment without seating) takes the operatory lock and refuses with <c>operatory_occupied</c>
///   if another patient is seated or in treatment there.
/// - A refused or failed move is measured (<c>appointment.flow</c>, outcome failure) but never changes the refusal: a measurement failure is logged as a
///   warning by the recorder and swallowed.
/// </summary>
public class AppointmentFlowService(
    AlveraDbContext db, IPracticeClock clock, IEffectivePermissions permissions, IMeasurementEventSink? measurements = null, ILogger<AppointmentFlowService>? logger = null)
{
    private readonly AppointmentViewBuilder _views = new(db, clock);
    private readonly SchedulingRecorder _recorder = new(db, measurements, logger);

    /// <summary>ALV-N021 A2: the lock completion and every checkout decision take, so a decision and a completion of the same visit never interleave.</summary>
    public static string CheckoutLock(Guid appointmentId) => $"visit:checkout:{appointmentId:N}";

    // STORY-011's three routes, now under the same policy and rules as every other move (briefing 5.4: legacy routes, same policy)
    public Task<AppointmentView> CheckInAsync(Guid id, string? rowVersion, VisitActor actor, CancellationToken ct) => TransitionAsync(id, VisitStates.CheckedIn, rowVersion, actor, ct);
    public Task<AppointmentView> StartTreatmentAsync(Guid id, string? rowVersion, VisitActor actor, CancellationToken ct) => TransitionAsync(id, VisitStates.InTreatment, rowVersion, actor, ct);
    public Task<AppointmentView> CompleteAsync(Guid id, string? rowVersion, VisitActor actor, CancellationToken ct) => TransitionAsync(id, VisitStates.Completed, rowVersion, actor, ct);

    /// <summary>Moves the visit to <paramref name="target"/>; the policy, the state machine, the day rule and (for completion) the checkout gate decide whether that is allowed.</summary>
    public async Task<AppointmentView> TransitionAsync(Guid id, string target, string? rowVersion, VisitActor actor, CancellationToken ct)
    {
        try
        {
            if (!VisitStates.IsKnown(target)) throw new SchedulingException("illegal_transition", $"{target} is not a state of the visit.", 409);
            return await TransitionCoreAsync(id, target, rowVersion, actor, ct);
        }
        catch (Exception ex) when (ex is SchedulingException or ConcurrencyConflictException)
        {
            var code = ex is SchedulingException s ? s.Code : "concurrency_conflict";
            await _recorder.MeasureAsync("appointment.flow", new { category = code, outcome = "failure" }, ct);
            throw;
        }
    }

    private async Task<AppointmentView> TransitionCoreAsync(Guid id, string target, string? rowVersion, VisitActor actor, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new SchedulingException("appointment_not_found", "That appointment was not found.", 404);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("terminal_state", $"Nothing more can happen to this visit: the appointment is {Describe(appointment.Status)}.", 409);
        await new VisitMovePolicy(db, permissions).AuthorizeAsync(appointment, target, actor, ct);

        // already there: nothing to do, whatever version the caller holds (a retry, or two people pressing the same button)
        if (appointment.FlowState == target)
        {
            db.ChangeTracker.Clear();
            return await _views.ViewAsync(id, ct);
        }

        AppointmentRowVersion.EnsureCurrent(db, appointment, rowVersion);
        var from = appointment.FlowState;
        if (from == VisitStates.Completed) throw new SchedulingException("terminal_state", "This visit is completed; nothing more can happen to it.", 409);
        var settings = await VisitSettings.ReadAsync(db, ct);
        if (VisitStateMachine.Decide(from, target, settings) != FlowDecision.Move)
            throw new SchedulingException("illegal_transition", $"A visit cannot go from {Words(from)} to {Words(target)}.", 409);

        var now = clock.UtcNow;
        var detail = $"{from} -> {target}";
        if (target == VisitStates.CheckedIn)
        {
            // the day rule (briefing 5.5): check-in only on the appointment's practice-local day; a late arrival is recorded with its minutes
            if (DayOf(appointment.StartUtc) != DayOf(now))
                throw new SchedulingException("check_in_wrong_day", "A patient can be checked in only on the day of the appointment.", 409);
            var late = (int)Math.Floor((now - appointment.StartUtc).TotalMinutes);
            if (late > 0) detail += $" ({late} minutes late)";
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        if (target == VisitStates.CheckedIn)
        {
            // OD-18: a flagged appointment (a blocked time over it, not resolved) cannot become a visit; checked under the provider lock blocked time also takes, so the two never interleave
            await SchedulingGuards.AcquireLockAsync(db, SchedulingGuards.ProviderLock(appointment.ProviderProfileId), ct);
            if (await BlockFlags.IsFlaggedAsync(db, appointment, ct))
                throw new SchedulingException("block_flag_unresolved", "The provider has blocked this time. Reschedule, reassign, cancel or keep the appointment before checking the patient in.", 409);
        }
        // taking a room: the patient is entering a state that occupies an operatory from one that did not
        if (VisitStates.OccupiesOperatory(target) && !VisitStates.OccupiesOperatory(from))
        {
            var operatory = VisitOccupancy.EffectiveOperatory(appointment);
            await SchedulingGuards.AcquireLockAsync(db, VisitOccupancy.LockResource(operatory), ct);
            await VisitOccupancy.ThrowIfOccupiedAsync(db, operatory, id, ct);
        }
        if (target == VisitStates.Completed)
        {
            // the checkout gate (OD-22, OD-29, OD-30): evaluated inside this transaction, under the lock every checkout decision also takes
            await SchedulingGuards.AcquireLockAsync(db, CheckoutLock(id), ct);
            var readiness = await new Checkout.CheckoutEvaluator(db, clock).EvaluateAsync(id, ct);
            if (!readiness.Ready)
                throw new SchedulingException("checkout_incomplete", "The visit cannot be completed until every checkout item is resolved: " + string.Join(", ", readiness.OpenItemCodes) + ".", 409,
                    reason: string.Join(",", readiness.OpenItemCodes));
        }

        appointment.FlowState = target;
        appointment.FlowChangedAtUtc = now;
        appointment.FlowChangedByUserId = actor.UserId;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = EventTypeFor(target), ActorUserId = actor.UserId, OccurredAtUtc = now, Detail = detail,
        });
        AuditService.Record(db, AuditEventFor(target), nameof(Appointment), id, actor.UserId, $"Patient flow moved from {from} to {target}.");
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Appointment), id, ct);
        await transaction.CommitAsync(ct);

        await _recorder.MeasureAsync("appointment.flow", new { category = target, outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    /// <summary>
    /// OD-11: undo a check-in from Checked in or Ready (before seating), back to the state held before check-in (Scheduled or Confirmed, read from the check-in event). Front desk; reason
    /// required. The check-in event stays; a CheckInUndone event (actor, from, to, reason, time) and an audit entry are added in the same save. From Seated on: 409 <c>undo_check_in_too_late</c>.
    /// A retry of the same undo (the visit is back before check-in and the last event is this undo with this reason) changes nothing.
    /// </summary>
    public async Task<AppointmentView> UndoCheckInAsync(Guid id, string? reason, string? rowVersion, VisitActor actor, CancellationToken ct)
    {
        try
        {
            return await UndoCheckInCoreAsync(id, reason, rowVersion, actor, ct);
        }
        catch (Exception ex) when (ex is SchedulingException or ConcurrencyConflictException)
        {
            var code = ex is SchedulingException s ? s.Code : "concurrency_conflict";
            await _recorder.MeasureAsync("appointment.flow", new { category = code, outcome = "failure" }, ct);
            throw;
        }
    }

    private async Task<AppointmentView> UndoCheckInCoreAsync(Guid id, string? reason, string? rowVersion, VisitActor actor, CancellationToken ct)
    {
        var why = VisitReasons.Required(reason, "undoing the check-in");
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new SchedulingException("appointment_not_found", "That appointment was not found.", 404);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("terminal_state", $"Nothing more can happen to this visit: the appointment is {Describe(appointment.Status)}.", 409);
        await new VisitMovePolicy(db, permissions).AuthorizeAsync(appointment, VisitMovePolicy.UndoCheckIn, actor, ct);

        var from = appointment.FlowState;
        if (from is VisitStates.Scheduled or VisitStates.Confirmed)
        {
            var last = await db.AppointmentEvents.AsNoTracking().Where(e => e.AppointmentId == id).OrderByDescending(e => e.OccurredAtUtc).FirstOrDefaultAsync(ct);
            if (last is { EventType: AppointmentEventTypes.CheckInUndone } && last.Reason == why)
            {
                db.ChangeTracker.Clear();   // the same undo again: nothing to do
                return await _views.ViewAsync(id, ct);
            }
            throw new SchedulingException("illegal_transition", "The patient is not checked in, so there is no check-in to undo.", 409);
        }
        if (from is not (VisitStates.CheckedIn or VisitStates.Ready))
            throw new SchedulingException("undo_check_in_too_late", "A check-in can be undone only before the patient is seated.", 409);
        AppointmentRowVersion.EnsureCurrent(db, appointment, rowVersion);

        var back = await StateBeforeCheckInAsync(id, ct);
        var now = clock.UtcNow;
        appointment.FlowState = back;
        appointment.FlowChangedAtUtc = now;
        appointment.FlowChangedByUserId = actor.UserId;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = AppointmentEventTypes.CheckInUndone, ActorUserId = actor.UserId, OccurredAtUtc = now, Detail = $"{from} -> {back}", Reason = why,
        });
        AuditService.Record(db, SchedulingAuditEvents.CheckInUndone, nameof(Appointment), id, actor.UserId, $"Check-in undone: patient flow moved from {from} to {back}.");
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Appointment), id, ct);

        await _recorder.MeasureAsync("appointment.flow", new { category = "UndoCheckIn", outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    /// <summary>
    /// OD-18 "Keep": the provider will see the patient despite the blocked time. Front office; reason required; only while the appointment is flagged (409 <c>not_flagged</c> otherwise). Writes a
    /// KeptDespiteBlock event (actor, reason) and an audit entry, which clears the flag for every block that exists now; a block added later flags it again. Runs under the provider lock, like
    /// blocked time. A retry of the same keep (not flagged any more, last such event has this reason) changes nothing.
    /// </summary>
    public async Task<AppointmentView> KeepDespiteBlockAsync(Guid id, string? reason, string? rowVersion, VisitActor actor, CancellationToken ct)
    {
        try
        {
            return await KeepCoreAsync(id, reason, rowVersion, actor, ct);
        }
        catch (Exception ex) when (ex is SchedulingException or ConcurrencyConflictException)
        {
            var code = ex is SchedulingException s ? s.Code : "concurrency_conflict";
            await _recorder.MeasureAsync("appointment.block_flag", new { category = code, outcome = "failure" }, ct);
            throw;
        }
    }

    private async Task<AppointmentView> KeepCoreAsync(Guid id, string? reason, string? rowVersion, VisitActor actor, CancellationToken ct)
    {
        var why = VisitReasons.Required(reason, "keeping the appointment despite the blocked time");
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new SchedulingException("appointment_not_found", "That appointment was not found.", 404);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("terminal_state", $"Nothing more can happen to this appointment: it is {Describe(appointment.Status)}.", 409);
        await new VisitMovePolicy(db, permissions).AuthorizeAsync(appointment, VisitMovePolicy.KeepDespiteBlock, actor, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SchedulingGuards.AcquireLockAsync(db, SchedulingGuards.ProviderLock(appointment.ProviderProfileId), ct);
        if (!await BlockFlags.IsFlaggedAsync(db, appointment, ct))
        {
            var last = await db.AppointmentEvents.AsNoTracking().Where(e => e.AppointmentId == id).OrderByDescending(e => e.OccurredAtUtc).FirstOrDefaultAsync(ct);
            if (last is { EventType: AppointmentEventTypes.KeptDespiteBlock } && last.Reason == why)
            {
                db.ChangeTracker.Clear();   // the same keep again: nothing to do
                return await _views.ViewAsync(id, ct);
            }
            throw new SchedulingException("not_flagged", "This appointment is not under a blocked time, so there is nothing to keep.", 409);
        }
        AppointmentRowVersion.EnsureCurrent(db, appointment, rowVersion);

        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = AppointmentEventTypes.KeptDespiteBlock, ActorUserId = actor.UserId, OccurredAtUtc = clock.UtcNow,
            Detail = "Kept despite blocked time.", Reason = why,
        });
        AuditService.Record(db, SchedulingAuditEvents.KeptDespiteBlock, nameof(Appointment), id, actor.UserId, "Appointment kept despite blocked time.");
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Appointment), id, ct);
        await transaction.CommitAsync(ct);

        await _recorder.MeasureAsync("appointment.block_flag", new { category = "keep", outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    /// <summary>The state the latest check-in moved FROM (its event's Detail is "&lt;from&gt; -&gt; CheckedIn"); Scheduled when that cannot be read (a check-in recorded before details were kept).</summary>
    private async Task<string> StateBeforeCheckInAsync(Guid id, CancellationToken ct)
    {
        var detail = await db.AppointmentEvents.AsNoTracking().Where(e => e.AppointmentId == id && e.EventType == AppointmentEventTypes.CheckedIn)
            .OrderByDescending(e => e.OccurredAtUtc).Select(e => e.Detail).FirstOrDefaultAsync(ct);
        var before = detail?.Split(" -> ")[0];
        return before is VisitStates.Confirmed ? VisitStates.Confirmed : VisitStates.Scheduled;
    }

    private DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(clock.ToPracticeLocal(instant).DateTime);

    private static string EventTypeFor(string target) => target switch
    {
        VisitStates.Confirmed => AppointmentEventTypes.Confirmed,
        VisitStates.CheckedIn => AppointmentEventTypes.CheckedIn,
        VisitStates.Ready => AppointmentEventTypes.Ready,
        VisitStates.Seated => AppointmentEventTypes.Seated,
        VisitStates.InTreatment => AppointmentEventTypes.TreatmentStarted,
        VisitStates.CheckedOut => AppointmentEventTypes.CheckedOut,
        _ => AppointmentEventTypes.Completed,
    };

    private static string AuditEventFor(string target) => target switch
    {
        VisitStates.Confirmed => SchedulingAuditEvents.PatientConfirmed,
        VisitStates.CheckedIn => SchedulingAuditEvents.PatientCheckedIn,
        VisitStates.Ready => SchedulingAuditEvents.PatientReady,
        VisitStates.Seated => SchedulingAuditEvents.PatientSeated,
        VisitStates.InTreatment => SchedulingAuditEvents.TreatmentStarted,
        VisitStates.CheckedOut => SchedulingAuditEvents.PatientCheckedOut,
        _ => SchedulingAuditEvents.TreatmentCompleted,
    };

    private static string Words(string state) => state switch
    {
        VisitStates.CheckedIn => "checked in",
        VisitStates.InTreatment => "in treatment",
        VisitStates.CheckedOut => "checked out",
        _ => state.ToLowerInvariant(),
    };

    private static string Describe(string status) => status switch
    {
        AppointmentStatuses.Cancelled => "cancelled",
        AppointmentStatuses.NoShow => "marked as a no-show",
        _ => status.ToLowerInvariant(),
    };
}
