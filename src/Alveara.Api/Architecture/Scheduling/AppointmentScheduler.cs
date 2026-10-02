using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// STORY-004: schedules an appointment without conflicts.
///
/// A request is accepted only if, in this order: the people and places exist and are active; the duration is valid (the appointment
/// type's default unless the caller gives one); the start is a real future practice-local time; the provider is available (inside a
/// weekly window, not blocked - ALV-N003's rule, reused); the provider is not already booked for any part of the period; and the
/// operatory is not already in use for any part of it. Periods are half-open, so back-to-back appointments are fine.
///
/// The no-double-booking guarantee must hold when two people book the same slot at the same moment, so a plain "check, then insert"
/// is not enough. The checks and the insert run in one transaction that first takes an exclusive database application lock on the
/// provider and on the operatory (always in the same order, so two requests cannot deadlock). The second request waits, then sees
/// the first one's appointment and is refused. The locks are released when the transaction ends, committed or not.
///
/// The appointment, its audit entry and nothing else commit together: if the audit write fails the appointment is not stored either.
/// A retried request (same Idempotency-Key) returns the first result instead of booking again. A conflict or unavailability refusal is
/// itself audited (who tried what, and why it was refused) and measured; a malformed request is not.
/// </summary>
public class AppointmentScheduler(
    AlveraDbContext db, IPracticeClock clock, SchedulingConfiguration configuration,
    IMeasurementEventSink? measurements = null, ILogger<AppointmentScheduler>? logger = null)
{
    private readonly AppointmentViewBuilder _views = new(db, clock);
    private readonly SchedulingRecorder _recorder = new(db, measurements, logger);

    public async Task<ScheduleResult> ScheduleAsync(ScheduleAppointmentRequest request, string? idempotencyKey, Guid actor, CancellationToken ct)
    {
        var key = (idempotencyKey ?? string.Empty).Trim();
        if (key.Length is < 8 or > 100)
            throw new SchedulingException("idempotency_key_required", "Scheduling needs an Idempotency-Key of 8-100 characters so a retried request can never book twice.", 400);

        try
        {
            return await ScheduleCoreAsync(request, key, actor, ct);
        }
        catch (SchedulingException ex) when (ex.IsSchedulingDecision)
        {
            await _recorder.RecordRejectionAsync(ex, actor, ct);
            throw;
        }
    }

    private async Task<ScheduleResult> ScheduleCoreAsync(ScheduleAppointmentRequest request, string key, Guid actor, CancellationToken ct)
    {
        // ---- the request itself ----
        DateTimeOffset startUtc;
        try
        {
            startUtc = clock.FromPracticeLocal(request.StartLocal).ToUniversalTime(); // stored and compared as UTC instants
        }
        catch (LocalTimeConversionException ex)
        {
            throw new SchedulingException("invalid_local_time", ex.Message, 400);
        }

        var type = await db.AppointmentTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.AppointmentTypeId, ct)
            ?? throw new SchedulingException("appointment_type_not_found", "That appointment type was not found.", 404);
        var duration = request.DurationMinutes ?? type.DefaultDurationMinutes;
        try
        {
            PracticeConfigurationService.ValidateDuration(duration);
        }
        catch (ConfigurationException ex)
        {
            throw new SchedulingException("invalid_duration", ex.Message, 400);
        }
        var endUtc = startUtc.AddMinutes(duration);
        var notes = NormalizeNotes(request.Notes);

        // ---- a retry of a request that already succeeded returns that appointment ----
        var earlier = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.ScheduleKey == key, ct);
        if (earlier is not null)
        {
            if (!SameRequest(earlier, request, startUtc, duration))
                throw new SchedulingException("idempotency_key_reused", "That Idempotency-Key was already used for a different appointment.", 409);
            return new ScheduleResult(await _views.ViewAsync(earlier.Id, ct), false);
        }

        // ---- the people and places ----
        if (startUtc <= clock.UtcNow)
            throw new SchedulingException("start_in_past", "An appointment must start in the future.", 400);
        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == request.PatientId).Select(p => new { p.IsActive }).SingleOrDefaultAsync(ct)
            ?? throw new SchedulingException("patient_not_found", "That patient was not found.", 404);
        if (!patient.IsActive) throw new SchedulingException("patient_inactive", "This patient is inactive. Reactivate them before scheduling.", 409);
        if (!type.IsActive) throw new SchedulingException("appointment_type_inactive", "That appointment type is inactive.", 409);

        var provider = await db.ProviderProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.ProviderId, ct)
            ?? throw new SchedulingException("provider_not_found", "That provider was not found.", 404);
        var operatory = await db.Operatories.AsNoTracking().Include(o => o.Location).SingleOrDefaultAsync(o => o.Id == request.OperatoryId, ct)
            ?? throw new SchedulingException("operatory_not_found", "That operatory was not found.", 404);
        if (!operatory.IsActive || operatory.Location is { IsActive: false })
            throw new SchedulingException("operatory_inactive", "That operatory is inactive.", 409);

        // ---- is the provider working then? (inactive provider, outside weekly hours, blocked time, daylight-saving transition) ----
        var availability = await configuration.CheckProviderAvailabilityAsync(provider.Id, startUtc, duration, ct);
        if (!availability.Available)
            throw new SchedulingException("provider_unavailable", "The provider is not available at that time.", 409, reason: availability.Reason);

        // ---- the checks and the insert: one transaction, serialized per provider, per operatory and per patient ----
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SchedulingGuards.AcquireLocksAsync(db, provider.Id, operatory.Id, request.PatientId, ct);
        await SchedulingGuards.ThrowIfConflictAsync(db, provider.Id, operatory.Id, request.PatientId, startUtc, endUtc, null, ct);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), PatientId = request.PatientId, ProviderProfileId = provider.Id, OperatoryId = operatory.Id, AppointmentTypeId = type.Id,
            StartUtc = startUtc, EndUtc = endUtc, DurationMinutes = duration, Status = AppointmentStatuses.Scheduled, ScheduleKey = key,
            Notes = notes, CreatedAtUtc = clock.UtcNow, CreatedByUserId = actor,
        };
        db.Appointments.Add(appointment);
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = appointment.Id, EventType = AppointmentEventTypes.Scheduled, ActorUserId = actor, OccurredAtUtc = appointment.CreatedAtUtc,
            Detail = $"Scheduled for {duration} minutes.",
        });
        AuditService.Record(db, SchedulingAuditEvents.Scheduled, nameof(Appointment), appointment.Id, actor, $"Appointment scheduled ({duration} minutes).");
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // the same request raced itself past the early check; the other one won
            db.ChangeTracker.Clear();
            var winner = await db.Appointments.AsNoTracking().SingleAsync(a => a.ScheduleKey == key, ct);
            return SameRequest(winner, request, startUtc, duration)
                ? new ScheduleResult(await _views.ViewAsync(winner.Id, ct), false)
                : throw new SchedulingException("idempotency_key_reused", "That Idempotency-Key was already used for a different appointment.", 409);
        }

        await _recorder.MeasureAsync("appointment.scheduled", new { outcome = "success" }, ct);
        return new ScheduleResult(await _views.ViewAsync(appointment.Id, ct), true);
    }

    // ---------- reading ----------

    public async Task<AppointmentView?> GetAsync(Guid id, CancellationToken ct) =>
        await db.Appointments.AsNoTracking().AnyAsync(a => a.Id == id, ct) ? await _views.ViewAsync(id, ct) : null;

    /// <summary>Scheduled appointments whose period overlaps [from, to), earliest first; optionally for one provider or operatory (cancelled and no-show appointments are not listed here - see <see cref="CalendarAsync"/>).</summary>
    public Task<IReadOnlyList<AppointmentView>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? providerId, Guid? operatoryId, CancellationToken ct) =>
        QueryAsync(fromUtc, toUtc, providerId, operatoryId, scheduledOnly: true, ct);

    /// <summary>ALV-004-C01: every appointment in the range whatever its status (cancelled and no-show ones stay visible, so the calendar can show them distinctly).</summary>
    public Task<IReadOnlyList<AppointmentView>> CalendarAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? providerId, Guid? operatoryId, CancellationToken ct) =>
        QueryAsync(fromUtc, toUtc, providerId, operatoryId, scheduledOnly: false, ct);

    private async Task<IReadOnlyList<AppointmentView>> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? providerId, Guid? operatoryId, bool scheduledOnly, CancellationToken ct)
    {
        var query = db.Appointments.AsNoTracking().Where(a => a.StartUtc < toUtc && fromUtc < a.EndUtc);
        if (scheduledOnly) query = query.Where(a => a.Status == AppointmentStatuses.Scheduled);
        if (providerId is not null) query = query.Where(a => a.ProviderProfileId == providerId);
        if (operatoryId is not null) query = query.Where(a => a.OperatoryId == operatoryId);
        var ids = await query.OrderBy(a => a.StartUtc).Select(a => a.Id).Take(500).ToListAsync(ct);
        var views = new List<AppointmentView>();
        foreach (var id in ids) views.Add(await _views.ViewAsync(id, ct));
        return views;
    }

    public async Task<IReadOnlyList<AppointmentEventView>?> HistoryAsync(Guid id, CancellationToken ct) =>
        await db.Appointments.AsNoTracking().AnyAsync(a => a.Id == id, ct) ? await _views.HistoryAsync(id, ct) : null;

    // ---------- helpers ----------

    private static bool SameRequest(Appointment a, ScheduleAppointmentRequest r, DateTimeOffset startUtc, int duration) =>
        a.PatientId == r.PatientId && a.ProviderProfileId == r.ProviderId && a.OperatoryId == r.OperatoryId
        && a.AppointmentTypeId == r.AppointmentTypeId && a.StartUtc == startUtc && a.DurationMinutes == duration;

    /// <summary>Trims the note; blank means none. Notes are limited to 1000 characters.</summary>
    internal static string? NormalizeNotes(string? notes)
    {
        var n = notes?.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrEmpty(n)) return null;
        if (n.Length > 1000) throw new SchedulingException("invalid_notes", "A note can be at most 1000 characters.", 400);
        return n;
    }
}
