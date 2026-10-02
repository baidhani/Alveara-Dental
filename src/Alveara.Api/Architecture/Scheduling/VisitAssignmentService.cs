using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>Where the patient is actually being seen during the visit. Both are required: this replaces the visit's assignment as a whole.</summary>
public record AssignVisitRequest(Guid ProviderId, Guid OperatoryId, string? RowVersion);

/// <summary>
/// ALV-011-C01: who and where the patient is ACTUALLY with during the visit (a hygienist seeing the patient before the dentist, a patient moved to
/// another room), kept apart from the booked provider and operatory.
///
/// - <b>The booking never moves.</b> The assignment is stored in its own columns; the calendar and every conflict rule still use the booked provider and
///   operatory. Assigning the booked provider or operatory back clears the override, so there is one canonical form of "as booked".
/// - <b>Rooms are not shared.</b> If the patient is Seated or InTreatment, moving them into an operatory that another seated or in-treatment patient holds
///   is refused (<c>operatory_occupied</c>), checked under the operatory lock so two simultaneous moves produce one winner.
/// - <b>Every change is recorded in the same save</b> as the change: an AssignmentChanged history entry (who, when, where the patient WAS) and a PHI-free
///   audit entry; if either cannot be written nothing changes. A finished (Completed) visit cannot be reassigned, nor can a cancelled or no-show appointment.
/// - <b>Repeats are harmless</b>: assigning what the visit already has changes nothing and writes nothing, whatever version the caller holds. Any real
///   change needs the version the caller read; a stale one is the shared 409 concurrency conflict.
/// </summary>
public class VisitAssignmentService(
    AlveraDbContext db, IPracticeClock clock, IMeasurementEventSink? measurements = null, ILogger<VisitAssignmentService>? logger = null)
{
    private readonly AppointmentViewBuilder _views = new(db, clock);
    private readonly SchedulingRecorder _recorder = new(db, measurements, logger);

    public async Task<AppointmentView> AssignAsync(Guid id, AssignVisitRequest request, Guid actor, CancellationToken ct)
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

    private async Task<AppointmentView> AssignCoreAsync(Guid id, AssignVisitRequest request, Guid actor, CancellationToken ct)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new SchedulingException("appointment_not_found", "That appointment was not found.", 404);

        // canonical form: assigning the booked provider/operatory means "as booked" (null)
        Guid? newProvider = request.ProviderId == appointment.ProviderProfileId ? null : request.ProviderId;
        Guid? newOperatory = request.OperatoryId == appointment.OperatoryId ? null : request.OperatoryId;
        if (newProvider == appointment.VisitProviderProfileId && newOperatory == appointment.VisitOperatoryId)
        {
            db.ChangeTracker.Clear(); // already assigned exactly this way: nothing to do
            return await _views.ViewAsync(id, ct);
        }

        AppointmentRowVersion.EnsureCurrent(db, appointment, request.RowVersion);
        if (appointment.Status != AppointmentStatuses.Scheduled)
            throw new SchedulingException("appointment_not_scheduled", $"Only a scheduled appointment can be reassigned; this one is {Describe(appointment.Status)}.", 409);
        if (appointment.FlowState == VisitStates.Completed)
            throw new SchedulingException("visit_completed", "A completed visit can no longer be reassigned.", 409);

        var provider = await db.ProviderProfiles.AsNoTracking().Include(p => p.StaffProfile).SingleOrDefaultAsync(p => p.Id == request.ProviderId, ct)
            ?? throw new SchedulingException("provider_not_found", "That provider was not found.", 404);
        if (!provider.IsActive || provider.StaffProfile is { IsActive: false })
            throw new SchedulingException("provider_inactive", "That provider is inactive.", 409);
        var operatory = await db.Operatories.AsNoTracking().Include(o => o.Location).SingleOrDefaultAsync(o => o.Id == request.OperatoryId, ct)
            ?? throw new SchedulingException("operatory_not_found", "That operatory was not found.", 404);
        if (!operatory.IsActive || operatory.Location is { IsActive: false })
            throw new SchedulingException("operatory_inactive", "That operatory is inactive.", 409);

        var previousProvider = VisitOccupancy.EffectiveProvider(appointment);
        var previousOperatory = VisitOccupancy.EffectiveOperatory(appointment);
        var movesRoom = request.OperatoryId != previousOperatory;
        var needsRoom = movesRoom && VisitStates.OccupiesOperatory(appointment.FlowState);

        await using var transaction = needsRoom ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        if (needsRoom)
        {
            await SchedulingGuards.AcquireLockAsync(db, VisitOccupancy.LockResource(request.OperatoryId), ct);
            await VisitOccupancy.ThrowIfOccupiedAsync(db, request.OperatoryId, id, ct);
        }

        var changedProvider = request.ProviderId != previousProvider;
        appointment.VisitProviderProfileId = newProvider;
        appointment.VisitOperatoryId = newOperatory;
        db.AppointmentEvents.Add(new AppointmentEvent
        {
            Id = Guid.NewGuid(), AppointmentId = id, EventType = AppointmentEventTypes.AssignmentChanged, ActorUserId = actor, OccurredAtUtc = clock.UtcNow,
            PreviousProviderProfileId = previousProvider, PreviousOperatoryId = previousOperatory,
            Detail = changedProvider && movesRoom ? "Provider and operatory changed." : changedProvider ? "Provider changed." : "Operatory changed.",
        });
        AuditService.Record(db, SchedulingAuditEvents.VisitAssignmentChanged, nameof(Appointment), id, actor, "Visit provider/operatory assignment changed.");
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Appointment), id, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        await _recorder.MeasureAsync("appointment.assignment", new { outcome = "success" }, ct);
        return await _views.ViewAsync(id, ct);
    }

    private static string Describe(string status) => status switch
    {
        AppointmentStatuses.Cancelled => "cancelled",
        AppointmentStatuses.NoShow => "marked as a no-show",
        _ => status.ToLowerInvariant(),
    };
}
