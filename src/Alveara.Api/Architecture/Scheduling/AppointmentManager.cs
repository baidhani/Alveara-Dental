using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// ALV-004-C01: what can happen to a booked appointment - reschedule, cancel with a reason, no-show, and a working note.
///
/// - <b>Every change carries the row version the caller read.</b> If someone else changed the appointment first, the change is refused as the shared
///   409 concurrency conflict (never applied on top of their change), whether that someone else rescheduled, cancelled or marked it.
/// - <b>Reschedule</b> applies exactly STORY-004's rules to the new place and time (a real future time, a valid duration, an available provider, and no
///   provider, operatory or patient overlap with any OTHER scheduled appointment), under the same locks, so two people moving different appointments into
///   the same slot at once still produce one winner. The appointment keeps its identity; where it was is kept in its history.
/// - <b>Cancel</b> needs a reason; <b>no-show</b> is only possible once the start time has passed. Both keep the appointment on the record and in the
///   calendar (distinctly), and neither holds time any more. Only a Scheduled appointment can be rescheduled, cancelled or marked no-show.
/// - Repeating a cancel, no-show or unchanged note/reschedule changes nothing (idempotent), so a retried request is harmless.
/// - Each action is staged with its history entry and PHI-free audit entry in ONE save: if the audit write fails the change is not stored either. A refused
///   reschedule (conflict or unavailability) is audited and measured too, exactly like a refused booking.
/// </summary>
public class AppointmentManager(
    AlveraDbContext db, IPracticeClock clock, SchedulingConfiguration configuration,
    IMeasurementEventSink? measurements = null, ILogger<AppointmentManager>? logger = null)
{
    private readonly AppointmentViewBuilder _views = new(db, clock);
    private readonly SchedulingRecorder _recorder = new(db, measurements, logger);

    // ---------- reschedule ----------

    public async Task<AppointmentView> RescheduleAsync(Guid id, RescheduleRequest request, Guid actor, CancellationToken ct)
    {
        try
        {
            return await RescheduleCoreAsync(id, request, actor, ct);
        }
        catch (SchedulingException ex) when (ex.IsSchedulingDecision)
        {
            await _recorder.RecordRejectionAsync(ex, actor, ct);
            throw;
        }
    }

    private async Task<AppointmentView> RescheduleCoreAsync(Guid id, RescheduleRequest request, Guid actor, CancellationToken ct)
    {
        var appointment = await LoadAsync(id, ct);
        EnsureCurrent(appointment, request.RowVersion);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("appointment_not_scheduled", $"Only a scheduled appointment can be rescheduled; this one is {Describe(appointment.Status)}.", 409);
        EnsureNotArrived(appointment, "rescheduled");

        DateTimeOffset startUtc;
        try
        {
            startUtc = clock.FromPracticeLocal(request.StartLocal).ToUniversalTime();
        }
        catch (LocalTimeConversionException ex)
        {
            throw new SchedulingException("invalid_local_time", ex.Message, 400);
        }
        var duration = request.DurationMinutes ?? appointment.DurationMinutes;
        try
        {
            PracticeConfigurationService.ValidateDuration(duration);
        }
        catch (ConfigurationException ex)
        {
            throw new SchedulingException("invalid_duration", ex.Message, 400);
        }
        var endUtc = startUtc.AddMinutes(duration);

        // nothing to change: a repeated request is harmless
        if (appointment.ProviderProfileId == request.ProviderId && appointment.OperatoryId == request.OperatoryId && appointment.StartUtc == startUtc && appointment.DurationMinutes == duration)
        {
            db.ChangeTracker.Clear();
            return await _views.ViewAsync(id, ct);
        }

        if (startUtc <= clock.UtcNow)
            throw new SchedulingException("start_in_past", "An appointment must start in the future.", 400);
        var provider = await db.ProviderProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.ProviderId, ct)
            ?? throw new SchedulingException("provider_not_found", "That provider was not found.", 404);
        var operatory = await db.Operatories.AsNoTracking().Include(o => o.Location).SingleOrDefaultAsync(o => o.Id == request.OperatoryId, ct)
            ?? throw new SchedulingException("operatory_not_found", "That operatory was not found.", 404);
        if (!operatory.IsActive || operatory.Location is { IsActive: false })
            throw new SchedulingException("operatory_inactive", "That operatory is inactive.", 409);

        var availability = await configuration.CheckProviderAvailabilityAsync(provider.Id, startUtc, duration, ct);
        if (!availability.Available)
            throw new SchedulingException("provider_unavailable", "The provider is not available at that time.", 409, reason: availability.Reason);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SchedulingGuards.AcquireLocksAsync(db, provider.Id, operatory.Id, appointment.PatientId, ct);
        await SchedulingGuards.ThrowIfConflictAsync(db, provider.Id, operatory.Id, appointment.PatientId, startUtc, endUtc, appointment.Id, ct);

        var now = clock.UtcNow;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = AppointmentEventTypes.Rescheduled, ActorUserId = actor, OccurredAtUtc = now,
            PreviousStartUtc = appointment.StartUtc, PreviousProviderProfileId = appointment.ProviderProfileId, PreviousOperatoryId = appointment.OperatoryId,
            Detail = $"Rescheduled for {duration} minutes.",
        });
        appointment.ProviderProfileId = provider.Id;
        appointment.OperatoryId = operatory.Id;
        appointment.StartUtc = startUtc;
        appointment.EndUtc = endUtc;
        appointment.DurationMinutes = duration;
        AuditService.Record(db, SchedulingAuditEvents.Rescheduled, nameof(Appointment), id, actor, $"Appointment rescheduled ({duration} minutes).");
        await SaveAsync(id, ct);
        await transaction.CommitAsync(ct);

        await _recorder.MeasureAsync("appointment.rescheduled", new { outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    // ---------- cancel ----------

    public async Task<AppointmentView> CancelAsync(Guid id, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = (reason ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (why.Length == 0) throw new SchedulingException("reason_required", "A reason for the cancellation is required.", 400);
        if (why.Length > 400) throw new SchedulingException("reason_too_long", "The reason can be at most 400 characters.", 400);

        var appointment = await LoadAsync(id, ct);
        if (appointment.Status == AppointmentStatuses.Cancelled) { db.ChangeTracker.Clear(); return await _views.ViewAsync(id, ct); } // already cancelled: nothing to do
        EnsureCurrent(appointment, rowVersion);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("appointment_not_scheduled", $"Only a scheduled appointment can be cancelled; this one is {Describe(appointment.Status)}.", 409);
        EnsureNotArrived(appointment, "cancelled");

        var now = clock.UtcNow;
        appointment.Status = AppointmentStatuses.Cancelled;
        appointment.FlowState = VisitStates.Scheduled; // flow only exists while Scheduled (a Confirmed appointment that is cancelled keeps its Confirmed event in the history)
        appointment.CancelReason = why;
        appointment.StatusChangedAtUtc = now;
        appointment.StatusChangedByUserId = actor;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = AppointmentEventTypes.Cancelled, ActorUserId = actor, OccurredAtUtc = now, Detail = why,
        });
        AuditService.Record(db, SchedulingAuditEvents.Cancelled, nameof(Appointment), id, actor, "Appointment cancelled.");
        await SaveAsync(id, ct);
        await _recorder.MeasureAsync("appointment.cancelled", new { outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    // ---------- no-show ----------

    public async Task<AppointmentView> MarkNoShowAsync(Guid id, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var appointment = await LoadAsync(id, ct);
        if (appointment.Status == AppointmentStatuses.NoShow) { db.ChangeTracker.Clear(); return await _views.ViewAsync(id, ct); }
        EnsureCurrent(appointment, rowVersion);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("appointment_not_scheduled", $"Only a scheduled appointment can be marked as a no-show; this one is {Describe(appointment.Status)}.", 409);
        EnsureNotArrived(appointment, "marked as a no-show");
        var now = clock.UtcNow;
        if (now < appointment.StartUtc)
            throw new SchedulingException("no_show_too_early", "An appointment can only be marked as a no-show once its start time has passed.", 409);

        appointment.Status = AppointmentStatuses.NoShow;
        appointment.FlowState = VisitStates.Scheduled; // see CancelAsync
        appointment.StatusChangedAtUtc = now;
        appointment.StatusChangedByUserId = actor;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = AppointmentEventTypes.NoShow, ActorUserId = actor, OccurredAtUtc = now, Detail = "Marked as a no-show.",
        });
        AuditService.Record(db, SchedulingAuditEvents.NoShow, nameof(Appointment), id, actor, "Appointment marked as a no-show.");
        await SaveAsync(id, ct);
        await _recorder.MeasureAsync("appointment.no-show", new { outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    // ---------- notes ----------

    /// <summary>Replaces the appointment's working note (blank clears it). Allowed whatever the status. The audit entry says a note changed, never what it says.</summary>
    public async Task<AppointmentView> UpdateNotesAsync(Guid id, string? notes, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var clean = AppointmentScheduler.NormalizeNotes(notes);
        var appointment = await LoadAsync(id, ct);
        EnsureCurrent(appointment, rowVersion);
        if (string.Equals(appointment.Notes, clean, StringComparison.Ordinal)) { db.ChangeTracker.Clear(); return await _views.ViewAsync(id, ct); }

        appointment.Notes = clean;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = AppointmentEventTypes.NotesChanged, ActorUserId = actor, OccurredAtUtc = clock.UtcNow,
            Detail = clean is null ? "Note removed." : "Note updated.",
        });
        AuditService.Record(db, SchedulingAuditEvents.NotesChanged, nameof(Appointment), id, actor, clean is null ? "Appointment note removed." : "Appointment note updated.");
        await SaveAsync(id, ct);
        return await _views.ViewAsync(id, ct);
    }

    // ---------- helpers ----------

    private async Task<Appointment> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new SchedulingException("appointment_not_found", "That appointment was not found.", 404);

    private void EnsureCurrent(Appointment appointment, string? rowVersion) => AppointmentRowVersion.EnsureCurrent(db, appointment, rowVersion);

    private Task SaveAsync(Guid id, CancellationToken ct) => ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Appointment), id, ct);

    /// <summary>STORY-011: once the patient has checked in the visit is under way, so it can no longer be moved, cancelled or called a no-show.</summary>
    private static void EnsureNotArrived(Appointment appointment, string action)
    {
        if (VisitStates.HasArrived(appointment.FlowState)) // Scheduled and Confirmed have not arrived yet; check-in and everything after it has
            throw new SchedulingException("appointment_in_progress", $"This appointment cannot be {action}: the patient has already checked in.", 409);
    }

    private static string Describe(string status) => status switch
    {
        AppointmentStatuses.Cancelled => "cancelled",
        AppointmentStatuses.NoShow => "marked as a no-show",
        _ => status.ToLowerInvariant(),
    };
}
