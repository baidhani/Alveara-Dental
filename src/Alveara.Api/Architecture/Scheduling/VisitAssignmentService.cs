using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>The provider and room the appointment should be booked with. Both are required; a provider change needs a reason, a room-only change may give one.</summary>
public record AssignVisitRequest(Guid ProviderId, Guid OperatoryId, string? RowVersion, string? Reason = null);

/// <summary>
/// ALV-N021 A2 (OD-13 to OD-16): reassigning an appointment's provider and/or room. This replaces ALV-011-C01's "Seen by" override: it changes the <b>booking</b> itself, so the calendar,
/// every conflict rule and the visit board all follow the new provider and room. Existing "Seen by" rows (the override columns and AssignmentChanged events) are left as they were and are
/// never written again; a reassignment of an appointment that still carries an old override clears it, so the booking alone says where the patient is (recorded in the event's previous values).
///
/// - <b>Who</b> (<see cref="VisitMovePolicy.AuthorizeAssignmentAsync"/>): a provider change needs <see cref="Permission.ReassignAppointmentProvider"/>; a room-only change needs that or
///   <see cref="Permission.UpdateChairsideFlow"/>. 403 <c>visit_move_forbidden</c> otherwise.
/// - <b>When</b>: before treatment starts (Scheduled, Confirmed, Checked in, Ready, Seated). From In treatment on: 409 <c>visit_already_started</c>. Cancelled or no-show: 409 <c>terminal_state</c>.
/// - <b>Reason</b>: always required for a provider change (400 <c>reason_required</c>); optional for a room-only change.
/// - <b>Guards as booking</b>: the new provider must be active and available (working, not blocked: 409 <c>provider_unavailable</c> with the reason) and nothing may overlap on the provider, the
///   room or the patient (the shared conflict payload), checked under the booking locks in one transaction. If the patient is Seated, the room must also be free of another seated or treated
///   patient, under the operatory lock (409 <c>operatory_occupied</c>), so the lock moves with the room.
/// - <b>Recorded</b>: a ProviderReassigned or RoomChanged event (actor, previous provider and room, reason) and a PHI-free audit entry in the same save.
/// - <b>Notified</b> (provider change only): one Urgent <see cref="NotificationKinds.AppointmentProviderReassigned"/> notification to each enabled account linked to the old and the new provider,
///   staged in the same save. The source key names the appointment and the event, and asking for what the appointment already has writes nothing at all, so a retry sends nothing new.
///   A provider with no linked enabled account gets no notification (nobody to send it to); that is the only case with fewer than two.
/// - <b>Repeats are harmless</b>: asking for the provider and room the appointment already has changes nothing and writes nothing, whatever version the caller holds. Any real change needs
///   the version the caller read (stale: the shared 409).
/// </summary>
public class VisitAssignmentService(
    AlveraDbContext db, IPracticeClock clock, SchedulingConfiguration configuration, IEffectivePermissions permissions,
    IMeasurementEventSink? measurements = null, ILogger<VisitAssignmentService>? logger = null)
{
    private readonly AppointmentViewBuilder _views = new(db, clock);
    private readonly SchedulingRecorder _recorder = new(db, measurements, logger);

    /// <summary>Letters, digits and colons only (the house style for source keys): the appointment and the reassignment event.</summary>
    public static string SourceKeyFor(Guid appointmentId, Guid eventId) => $"AppointmentProviderReassigned:{appointmentId:N}:{eventId:N}";

    public async Task<AppointmentView> AssignAsync(Guid id, AssignVisitRequest request, VisitActor actor, CancellationToken ct)
    {
        try
        {
            return await AssignCoreAsync(id, request, actor, ct);
        }
        catch (Exception ex) when (ex is SchedulingException or ConcurrencyConflictException)
        {
            await _recorder.MeasureAsync("appointment.assignment", new { category = ex is SchedulingException s ? s.Code : "concurrency_conflict", outcome = "failure" }, ct);
            throw;
        }
    }

    private async Task<AppointmentView> AssignCoreAsync(Guid id, AssignVisitRequest request, VisitActor actor, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new SchedulingException("appointment_not_found", "That appointment was not found.", 404);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("terminal_state", $"Nothing more can happen to this appointment: it is {Describe(appointment.Status)}.", 409);

        var previousProvider = appointment.ProviderProfileId;
        var previousRoom = VisitOccupancy.EffectiveOperatory(appointment);
        var providerChanges = request.ProviderId != previousProvider;
        var roomChanges = request.OperatoryId != previousRoom;
        await new VisitMovePolicy(db, permissions).AuthorizeAssignmentAsync(providerChanges, actor, ct);
        if (!providerChanges && !roomChanges)
        {
            db.ChangeTracker.Clear();   // already booked exactly this way: nothing to do
            return await _views.ViewAsync(id, ct);
        }

        AppointmentRowVersion.EnsureCurrent(db, appointment, request.RowVersion);
        if (appointment.FlowState is VisitStates.InTreatment or VisitStates.CheckedOut or VisitStates.Completed)
            throw new SchedulingException("visit_already_started", "The provider and room can no longer be changed: treatment has started.", 409);
        var reason = providerChanges ? VisitReasons.Required(request.Reason, "changing the provider") : VisitReasons.Optional(request.Reason);

        var provider = await db.ProviderProfiles.AsNoTracking().Include(p => p.StaffProfile).SingleOrDefaultAsync(p => p.Id == request.ProviderId, ct)
            ?? throw new SchedulingException("provider_not_found", "That provider was not found.", 404);
        if (!provider.IsActive || provider.StaffProfile is { IsActive: false })
            throw new SchedulingException("provider_inactive", "That provider is inactive.", 409);
        var operatory = await db.Operatories.AsNoTracking().Include(o => o.Location).SingleOrDefaultAsync(o => o.Id == request.OperatoryId, ct)
            ?? throw new SchedulingException("operatory_not_found", "That operatory was not found.", 404);
        if (!operatory.IsActive || operatory.Location is { IsActive: false })
            throw new SchedulingException("operatory_inactive", "That operatory is inactive.", 409);
        if (providerChanges)
        {
            var availability = await configuration.CheckProviderAvailabilityAsync(provider.Id, appointment.StartUtc, appointment.DurationMinutes, ct);
            if (!availability.Available)
                throw new SchedulingException("provider_unavailable", "The provider is not available at that time.", 409, reason: availability.Reason);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SchedulingGuards.AcquireLocksAsync(db, provider.Id, operatory.Id, appointment.PatientId, ct);
        await SchedulingGuards.ThrowIfConflictAsync(db, provider.Id, operatory.Id, appointment.PatientId, appointment.StartUtc, appointment.EndUtc, id, ct);
        if (roomChanges && VisitStates.OccupiesOperatory(appointment.FlowState))
        {
            // the patient is in a chair: they take the new room's occupancy lock (OD-16; briefing 5.6 "the operatory lock moves")
            await SchedulingGuards.AcquireLockAsync(db, VisitOccupancy.LockResource(operatory.Id), ct);
            await VisitOccupancy.ThrowIfOccupiedAsync(db, operatory.Id, id, ct);
        }

        var now = clock.UtcNow;
        var eventId = Guid.NewGuid();
        appointment.ProviderProfileId = provider.Id;
        appointment.OperatoryId = operatory.Id;
        appointment.VisitProviderProfileId = null;   // a retired "Seen by" override no longer applies once the booking itself is changed
        appointment.VisitOperatoryId = null;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = eventId, AppointmentId = id, EventType = providerChanges ? AppointmentEventTypes.ProviderReassigned : AppointmentEventTypes.RoomChanged,
            ActorUserId = actor.UserId, OccurredAtUtc = now, PreviousProviderProfileId = previousProvider, PreviousOperatoryId = previousRoom, Reason = reason,
            // ALV-N021 A2 (M4): what changed TO, recorded with the move (only the parts that changed)
            NewProviderProfileId = providerChanges ? provider.Id : null, NewProviderName = providerChanges ? MoveDestination.ProviderName(provider) : null,
            NewOperatoryId = roomChanges ? operatory.Id : null, NewOperatoryName = roomChanges ? operatory.Name : null,
            Detail = providerChanges && roomChanges ? "Provider and room changed." : providerChanges ? "Provider changed." : "Room changed.",
        });
        AuditService.Record(db, providerChanges ? SchedulingAuditEvents.ProviderReassigned : SchedulingAuditEvents.RoomChanged, nameof(Appointment), id, actor.UserId,
            providerChanges ? "Appointment provider reassigned." : "Appointment room changed.");
        if (providerChanges) await StageNotificationsAsync(id, eventId, previousProvider, provider.Id, now, ct);
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Appointment), id, ct);
        await transaction.CommitAsync(ct);

        await _recorder.MeasureAsync("appointment.assignment", new { category = providerChanges ? "provider" : "room", outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    /// <summary>OD-15: one Urgent notification per enabled account linked to the old or the new provider; PHI-free (no patient, message or document; the source key is ids only).</summary>
    private async Task StageNotificationsAsync(Guid appointmentId, Guid eventId, Guid oldProvider, Guid newProvider, DateTimeOffset now, CancellationToken ct)
    {
        var linked = await db.ProviderProfiles.AsNoTracking()
            .Where(p => (p.Id == oldProvider || p.Id == newProvider) && p.StaffProfile != null && p.StaffProfile.UserAccountId != null)
            .Select(p => p.StaffProfile!.UserAccountId!.Value).Distinct().ToListAsync(ct);
        var recipients = await db.UserAccounts.AsNoTracking().Where(a => linked.Contains(a.Id) && !a.IsDisabled).Select(a => a.Id).ToListAsync(ct);
        var sourceKey = SourceKeyFor(appointmentId, eventId);
        foreach (var recipient in recipients)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(), RecipientUserId = recipient, Kind = NotificationKinds.AppointmentProviderReassigned, Priority = NotificationPriorities.Urgent,
                SourceKey = sourceKey, CreatedAtUtc = now,
            });
        }
        if (recipients.Count < 2)
            logger?.LogInformation("provider_reassigned_notification_recipients appointment={AppointmentId} recipients={Count}", appointmentId, recipients.Count);
    }

    private static string Describe(string status) => status switch
    {
        AppointmentStatuses.Cancelled => "cancelled",
        AppointmentStatuses.NoShow => "marked as a no-show",
        _ => status.ToLowerInvariant(),
    };
}
