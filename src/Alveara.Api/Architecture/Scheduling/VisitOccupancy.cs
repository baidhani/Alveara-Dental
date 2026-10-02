using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// ALV-011-C01: one operatory holds one patient at a time. A visit that is Seated or InTreatment occupies the operatory it is actually in (the visit-time
/// operatory if one was assigned, otherwise the booked one); no other visit may be seated or started in that operatory until it moves on.
///
/// This is about who is physically in the room NOW, not about booked time (that is <see cref="SchedulingGuards"/>), so it looks only at visit state.
/// It is checked under an exclusive database application lock on the operatory, taken inside the caller's transaction, so two receptionists seating
/// two patients into the same room at the same moment produce one winner and one clear refusal naming the visit that holds the room.
/// </summary>
internal static class VisitOccupancy
{
    public static string LockResource(Guid operatoryId) => $"visit:operatory:{operatoryId:N}";

    /// <summary>The visit-time operatory if one was assigned, otherwise the booked operatory.</summary>
    public static Guid EffectiveOperatory(Appointment a) => a.VisitOperatoryId ?? a.OperatoryId;

    public static Guid EffectiveProvider(Appointment a) => a.VisitProviderProfileId ?? a.ProviderProfileId;

    /// <summary>Throws <c>operatory_occupied</c> (409, naming the occupying appointment) if another visit is seated or in treatment in the operatory.</summary>
    public static async Task ThrowIfOccupiedAsync(AlveraDbContext db, Guid operatoryId, Guid selfId, CancellationToken ct)
    {
        var occupier = await db.Appointments.AsNoTracking()
            .Where(a => a.Id != selfId && a.Status == AppointmentStatuses.Scheduled
                        && (a.FlowState == VisitStates.Seated || a.FlowState == VisitStates.InTreatment)
                        && (a.VisitOperatoryId ?? a.OperatoryId) == operatoryId)
            .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (occupier is not null)
            throw new SchedulingException("operatory_occupied", "That operatory is occupied by a patient who is seated or in treatment.", 409, occupier);
    }
}
