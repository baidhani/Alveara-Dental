using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// STORY-011: moves a booked appointment through the visit - check in, start treatment, complete - and records every move.
///
/// - <b>Only forward, one rule.</b> <see cref="PatientFlowRules"/> decides; this class never decides anything itself. Only an appointment whose Status is
///   Scheduled has a flow (a cancelled or no-show appointment is refused with <c>appointment_not_scheduled</c>).
/// - <b>Every move is logged in the same save as the state change</b>: an appointment history entry (who, when, from -> to) and a PHI-free audit entry. If
///   either write fails nothing is stored, so the state can never change without a record, and a failed save leaves the patient exactly where they were.
/// - <b>Repeats are harmless.</b> Asking for the state the appointment is already in changes nothing and writes nothing (a retried request, or two people
///   pressing Check in at once, ends in one check-in).
/// - <b>Every real move carries the row version the caller read</b>; a stale one is the shared 409 concurrency conflict, never applied on top of a
///   change someone else made.
/// - A refused or failed move is measured (<c>appointment.flow</c>, outcome failure) but never changes the refusal: a measurement failure is logged as a
///   warning by the recorder and swallowed.
/// </summary>
public class AppointmentFlowService(
    AlveraDbContext db, IPracticeClock clock, IMeasurementEventSink? measurements = null, ILogger<AppointmentFlowService>? logger = null)
{
    private readonly AppointmentViewBuilder _views = new(db, clock);
    private readonly SchedulingRecorder _recorder = new(db, measurements, logger);

    public Task<AppointmentView> CheckInAsync(Guid id, string? rowVersion, Guid actor, CancellationToken ct) =>
        AdvanceAsync(id, PatientFlowStates.CheckedIn, rowVersion, actor, ct);

    public Task<AppointmentView> StartTreatmentAsync(Guid id, string? rowVersion, Guid actor, CancellationToken ct) =>
        AdvanceAsync(id, PatientFlowStates.InTreatment, rowVersion, actor, ct);

    public Task<AppointmentView> CompleteAsync(Guid id, string? rowVersion, Guid actor, CancellationToken ct) =>
        AdvanceAsync(id, PatientFlowStates.Completed, rowVersion, actor, ct);

    private async Task<AppointmentView> AdvanceAsync(Guid id, string target, string? rowVersion, Guid actor, CancellationToken ct)
    {
        try
        {
            return await AdvanceCoreAsync(id, target, rowVersion, actor, ct);
        }
        catch (Exception ex) when (ex is SchedulingException or ConcurrencyConflictException)
        {
            var code = ex is SchedulingException s ? s.Code : "concurrency_conflict";
            await _recorder.MeasureAsync("appointment.flow", new { category = code, outcome = "failure" }, ct);
            throw;
        }
    }

    private async Task<AppointmentView> AdvanceCoreAsync(Guid id, string target, string? rowVersion, Guid actor, CancellationToken ct)
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
        if (PatientFlowRules.Decide(from, target) != FlowDecision.Move)
            throw new SchedulingException("invalid_flow_transition", $"A visit cannot go from {Words(from)} to {Words(target)}.", 409);

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

        await _recorder.MeasureAsync("appointment.flow", new { category = target, outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    private static string EventTypeFor(string target) => target switch
    {
        PatientFlowStates.CheckedIn => AppointmentEventTypes.CheckedIn,
        PatientFlowStates.InTreatment => AppointmentEventTypes.TreatmentStarted,
        _ => AppointmentEventTypes.Completed,
    };

    private static string AuditEventFor(string target) => target switch
    {
        PatientFlowStates.CheckedIn => SchedulingAuditEvents.PatientCheckedIn,
        PatientFlowStates.InTreatment => SchedulingAuditEvents.TreatmentStarted,
        _ => SchedulingAuditEvents.TreatmentCompleted,
    };

    private static string Words(string state) => state switch
    {
        PatientFlowStates.CheckedIn => "checked in",
        PatientFlowStates.InTreatment => "in treatment",
        _ => state.ToLowerInvariant(),
    };

    private static string Describe(string status) => status switch
    {
        AppointmentStatuses.Cancelled => "cancelled",
        AppointmentStatuses.NoShow => "marked as a no-show",
        _ => status.ToLowerInvariant(),
    };
}
