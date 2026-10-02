using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// Moves a booked appointment through the visit and records every move. STORY-011 shipped check-in, start-treatment and complete; ALV-011-C01 grew this into the
/// whole chain (confirm, check in, ready, seat, start treatment, check out, complete) driven by <see cref="VisitStateMachine"/>. STORY-011's three methods are kept
/// and call the same code, so its tests exercise the new rules.
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
    AlveraDbContext db, IPracticeClock clock, IMeasurementEventSink? measurements = null, ILogger<AppointmentFlowService>? logger = null)
{
    private readonly AppointmentViewBuilder _views = new(db, clock);
    private readonly SchedulingRecorder _recorder = new(db, measurements, logger);

    // STORY-011's three moves
    public Task<AppointmentView> CheckInAsync(Guid id, string? rowVersion, Guid actor, CancellationToken ct) => TransitionAsync(id, VisitStates.CheckedIn, rowVersion, actor, ct);
    public Task<AppointmentView> StartTreatmentAsync(Guid id, string? rowVersion, Guid actor, CancellationToken ct) => TransitionAsync(id, VisitStates.InTreatment, rowVersion, actor, ct);
    public Task<AppointmentView> CompleteAsync(Guid id, string? rowVersion, Guid actor, CancellationToken ct) => TransitionAsync(id, VisitStates.Completed, rowVersion, actor, ct);

    /// <summary>Moves the visit to <paramref name="target"/> (any state of the chain); the state machine decides whether that is allowed from where it is.</summary>
    public async Task<AppointmentView> TransitionAsync(Guid id, string target, string? rowVersion, Guid actor, CancellationToken ct)
    {
        try
        {
            if (!VisitStates.IsKnown(target)) throw new SchedulingException("invalid_flow_transition", $"{target} is not a state of the visit.", 409);
            return await TransitionCoreAsync(id, target, rowVersion, actor, ct);
        }
        catch (Exception ex) when (ex is SchedulingException or ConcurrencyConflictException)
        {
            var code = ex is SchedulingException s ? s.Code : "concurrency_conflict";
            await _recorder.MeasureAsync("appointment.flow", new { category = code, outcome = "failure" }, ct);
            throw;
        }
    }

    private async Task<AppointmentView> TransitionCoreAsync(Guid id, string target, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new SchedulingException("appointment_not_found", "That appointment was not found.", 404);

        // already there: nothing to do, whatever version the caller holds
        if (appointment.Status == AppointmentStatuses.Scheduled && appointment.FlowState == target)
        {
            db.ChangeTracker.Clear();
            return await _views.ViewAsync(id, ct);
        }

        AppointmentRowVersion.EnsureCurrent(db, appointment, rowVersion);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("appointment_not_scheduled", $"Patient flow applies only to a scheduled appointment; this one is {Describe(appointment.Status)}.", 409);

        var from = appointment.FlowState;
        if (VisitStateMachine.Decide(from, target) != FlowDecision.Move)
            throw new SchedulingException("invalid_flow_transition", $"A visit cannot go from {Words(from)} to {Words(target)}.", 409);

        // taking a room: the patient is entering a state that occupies an operatory from one that did not
        var needsRoom = VisitStates.OccupiesOperatory(target) && !VisitStates.OccupiesOperatory(from);
        var operatory = VisitOccupancy.EffectiveOperatory(appointment);
        await using var transaction = needsRoom ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        if (needsRoom)
        {
            await SchedulingGuards.AcquireLockAsync(db, VisitOccupancy.LockResource(operatory), ct);
            await VisitOccupancy.ThrowIfOccupiedAsync(db, operatory, id, ct);
        }

        var now = clock.UtcNow;
        appointment.FlowState = target;
        appointment.FlowChangedAtUtc = now;
        appointment.FlowChangedByUserId = actor;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = EventTypeFor(target), ActorUserId = actor, OccurredAtUtc = now,
            Detail = $"{from} -> {target}",
        });
        AuditService.Record(db, AuditEventFor(target), nameof(Appointment), id, actor, $"Patient flow moved from {from} to {target}.");
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Appointment), id, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        await _recorder.MeasureAsync("appointment.flow", new { category = target, outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

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
