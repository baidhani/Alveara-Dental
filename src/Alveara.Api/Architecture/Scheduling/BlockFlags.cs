using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// ALV-N021 A2 (OD-17, OD-18; briefing 5.6 "no new table: the flag is derived from blocks and events"): an appointment is flagged "Provider unavailable — needs attention" when ALL hold:
/// it is booked (Status Scheduled) and has not arrived (Scheduled or Confirmed); its booked provider has a blocked time overlapping its period; and that block was created after the
/// appointment's latest "kept despite block" decision (or there is none).
///
/// The four ways out (OD-18) follow from that definition: a reschedule or a provider reassignment moves the appointment off the block (both go through the booking guards, which refuse blocked
/// time), a cancellation ends the booking, and Keep records a <see cref="AppointmentEventTypes.KeptDespiteBlock"/> event with a reason. A block added later flags it again.
/// </summary>
public static class BlockFlags
{
    /// <summary>Visits under way: a block over any of these is refused outright (409 <c>block_overlaps_active_visit</c>).</summary>
    public static readonly IReadOnlyList<string> ActiveStates = [VisitStates.CheckedIn, VisitStates.Ready, VisitStates.Seated, VisitStates.InTreatment, VisitStates.CheckedOut];

    public static async Task<bool> IsFlaggedAsync(AlveraDbContext db, Appointment a, CancellationToken ct)
    {
        if (a.Status != AppointmentStatuses.Scheduled || a.FlowState is not (VisitStates.Scheduled or VisitStates.Confirmed)) return false;
        var keptAt = await db.AppointmentEvents.AsNoTracking()
            .Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.KeptDespiteBlock).MaxAsync(e => (DateTimeOffset?)e.OccurredAtUtc, ct);
        return await db.ProviderBlockedTimes.AsNoTracking().AnyAsync(b =>
            b.ProviderProfileId == a.ProviderProfileId && b.StartUtc < a.EndUtc && a.StartUtc < b.EndUtc && (keptAt == null || b.CreatedAtUtc > keptAt), ct);
    }
}
