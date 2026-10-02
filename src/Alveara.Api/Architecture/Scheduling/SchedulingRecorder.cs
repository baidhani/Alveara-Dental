using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// Audits and measures the outcome of a scheduling action that did not change anything: a refused attempt (a conflict or unavailability - "who tried what
/// and why was it refused"). It never changes the refusal: if the audit write fails the caller still gets the refusal, because nothing was booked.
/// Also records privacy-safe measurement events, whose failure never changes a decision.
/// </summary>
internal sealed class SchedulingRecorder(AlveraDbContext db, IMeasurementEventSink? measurements, ILogger? logger)
{
    public async Task RecordRejectionAsync(SchedulingException refusal, Guid actor, CancellationToken ct)
    {
        try
        {
            db.ChangeTracker.Clear();
            AuditService.Record(db, SchedulingAuditEvents.Rejected, nameof(Appointment), refusal.ConflictingAppointmentId ?? Guid.Empty, actor,
                $"Appointment refused: {refusal.Code}{(refusal.Reason is null ? "" : $" ({refusal.Reason})")}.");
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger?.LogWarning(ex, "The refusal of a scheduling request ({Code}) could not be audited; the refusal stands.", refusal.Code);
        }
        await MeasureAsync("appointment.rejected", new { category = refusal.Code, outcome = "failure" }, ct);
    }

    public async Task MeasureAsync(string name, object properties, CancellationToken ct)
    {
        if (measurements is null) return;
        try
        {
            await measurements.RecordAsync(name, 1, properties, ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or MeasurementEventValidationException or InvalidOperationException)
        {
            logger?.LogWarning(ex, "{Event} measurement event was not recorded; the scheduling decision stands.", name);
        }
    }
}
