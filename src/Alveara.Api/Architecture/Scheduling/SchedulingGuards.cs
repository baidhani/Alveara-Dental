using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// The conflict rules and the locking that makes them hold under a race, shared by booking (STORY-004) and rescheduling (ALV-004-C01) so the
/// two can never disagree about what a conflict is.
///
/// Three resources can only be in one place at a time: a provider, an operatory and a patient. Only <see cref="AppointmentStatuses.Scheduled"/>
/// appointments hold time, and periods are half-open, so back-to-back appointments are fine. A conflict is reported in this order - provider,
/// operatory, patient - and names the appointment already holding the time (no patient details).
///
/// "Check, then write" is not safe when two requests run at once, so the caller takes an exclusive SQL Server application lock on each of the three
/// resources inside its transaction, always in the same sorted order (so two requests cannot deadlock), and only then checks. The second request
/// waits, then sees the first one's appointment and is refused. Locks are released when the transaction ends, committed or not.
/// </summary>
internal static class SchedulingGuards
{
    private const int LockTimeoutMilliseconds = 10_000;

    public static async Task AcquireLocksAsync(AlveraDbContext db, Guid providerId, Guid operatoryId, Guid patientId, CancellationToken ct)
    {
        var resources = new[] { $"appointment:provider:{providerId:N}", $"appointment:operatory:{operatoryId:N}", $"appointment:patient:{patientId:N}" }.Order(StringComparer.Ordinal);
        foreach (var resource in resources) await AcquireLockAsync(db, resource, ct);
    }

    /// <summary>One exclusive, transaction-scoped application lock (waits up to 10 seconds, then 503 schedule_busy). Also used by ALV-011-C01's visit-occupancy rule.</summary>
    public static async Task AcquireLockAsync(AlveraDbContext db, string resource, CancellationToken ct)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {1}",
            [resource, LockTimeoutMilliseconds, result], ct);
        if (result.Value is not int code || code < 0)
            throw new SchedulingException("schedule_busy", "The schedule is busy right now. Please try again in a moment.", 503);
    }

    /// <summary>Throws the first conflict found for the period. <paramref name="excludeAppointmentId"/> is the appointment being rescheduled (it must not conflict with itself).</summary>
    public static async Task ThrowIfConflictAsync(
        AlveraDbContext db, Guid providerId, Guid operatoryId, Guid patientId, DateTimeOffset startUtc, DateTimeOffset endUtc, Guid? excludeAppointmentId, CancellationToken ct)
    {
        IQueryable<Appointment> Holding() => db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatuses.Scheduled && a.StartUtc < endUtc && startUtc < a.EndUtc && a.Id != excludeAppointmentId);

        var providerClash = await Holding().Where(a => a.ProviderProfileId == providerId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (providerClash is not null)
            throw new SchedulingException("provider_double_booked", "The provider already has an appointment during that time.", 409, providerClash);

        var operatoryClash = await Holding().Where(a => a.OperatoryId == operatoryId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (operatoryClash is not null)
            throw new SchedulingException("operatory_conflict", "The operatory is already in use during that time.", 409, operatoryClash);

        var patientClash = await Holding().Where(a => a.PatientId == patientId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        if (patientClash is not null)
            throw new SchedulingException("patient_double_booked", "The patient already has an appointment during that time.", 409, patientClash);
    }
}
