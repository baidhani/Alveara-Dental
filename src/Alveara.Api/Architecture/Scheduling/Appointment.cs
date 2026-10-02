namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// STORY-004: one scheduled appointment - a patient seen by a provider in an operatory for a period of time.
///
/// The period is stored as UTC instants, half-open: it occupies [StartUtc, EndUtc), so one appointment may end at the exact
/// moment the next one starts. <see cref="DurationMinutes"/> is stored as well as the end so the duration that was actually
/// booked (the type's default or an explicit override) is never inferred later. Overlap rules and the practice's local-time
/// rules live in <see cref="AppointmentScheduler"/>; this class is only the record.
///
/// Only <see cref="AppointmentStatuses.Scheduled"/> exists in this story. Reschedule, cancel and no-show belong to
/// ALV-004-C01, which will add statuses; only a Scheduled appointment ever blocks a provider or operatory.
/// </summary>
public class Appointment
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid ProviderProfileId { get; set; }
    public Guid OperatoryId { get; set; }
    public Guid AppointmentTypeId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public int DurationMinutes { get; set; }
    public required string Status { get; set; }

    /// <summary>The caller's Idempotency-Key for the request that created this appointment (unique), so a retry returns it instead of booking twice.</summary>
    public string? ScheduleKey { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class AppointmentStatuses
{
    public const string Scheduled = "Scheduled";
}
