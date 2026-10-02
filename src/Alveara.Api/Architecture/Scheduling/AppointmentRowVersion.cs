using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// The optimistic-concurrency check shared by every change to an appointment (the lifecycle actions and, from STORY-011, patient flow).
/// The caller must have read the CURRENT version. A stale one is the shared concurrency conflict, reported before any other rule so the user is told
/// "someone changed this" rather than a confusing consequence of it. The version is also pinned for the save, so a change that lands between this
/// check and the write is caught too.
/// </summary>
internal static class AppointmentRowVersion
{
    public static void EnsureCurrent(AlveraDbContext db, Appointment appointment, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new SchedulingException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new SchedulingException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
        if (!supplied.AsSpan().SequenceEqual(appointment.RowVersion)) throw new ConcurrencyConflictException(nameof(Appointment), appointment.Id);
        db.Entry(appointment).Property(nameof(Appointment.RowVersion)).OriginalValue = supplied;
    }
}
