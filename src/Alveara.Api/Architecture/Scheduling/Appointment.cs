namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// STORY-004: one scheduled appointment - a patient seen by a provider in an operatory for a period of time.
///
/// The period is stored as UTC instants, half-open: it occupies [StartUtc, EndUtc), so one appointment may end at the exact
/// moment the next one starts. <see cref="DurationMinutes"/> is stored as well as the end so the duration that was actually
/// booked (the type's default or an explicit override) is never inferred later. Overlap rules and the practice's local-time
/// rules live in <see cref="AppointmentScheduler"/>; this class is only the record.
///
/// STORY-004 only ever created <see cref="AppointmentStatuses.Scheduled"/> appointments. ALV-004-C01 adds Cancelled and NoShow
/// (reschedule edits a Scheduled appointment in place, with a history entry); only a Scheduled appointment ever blocks a
/// provider, operatory or patient, so a cancelled or no-show appointment stays on the record and in the calendar without
/// holding time.
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

    /// <summary>Free-text note about the appointment (front-desk working note; never copied into the audit log).</summary>
    public string? Notes { get; set; }
    /// <summary>Why a Cancelled appointment was cancelled (required when cancelling).</summary>
    public string? CancelReason { get; set; }
    /// <summary>When and by whom the appointment became Cancelled or NoShow.</summary>
    public DateTimeOffset? StatusChangedAtUtc { get; set; }
    public Guid? StatusChangedByUserId { get; set; }

    /// <summary>STORY-011: how far the visit has got (see <see cref="PatientFlowStates"/>). Only meaningful while Status is Scheduled.</summary>
    public string FlowState { get; set; } = PatientFlowStates.Scheduled;
    /// <summary>When and by whom the flow last moved (null until the first move).</summary>
    public DateTimeOffset? FlowChangedAtUtc { get; set; }
    public Guid? FlowChangedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class AppointmentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
}

/// <summary>
/// ALV-004-C01: one entry of an appointment's history - what happened, who did it and when. Append-only. A reschedule records where the
/// appointment WAS (previous start, provider and operatory), so a rescheduled, cancelled or no-show appointment stays historically visible.
/// </summary>
public class AppointmentEvent
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public required string EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? PreviousStartUtc { get; set; }
    public Guid? PreviousProviderProfileId { get; set; }
    public Guid? PreviousOperatoryId { get; set; }
    public string? Detail { get; set; }
}

public static class AppointmentEventTypes
{
    public const string Scheduled = "Scheduled";
    public const string Rescheduled = "Rescheduled";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public const string NotesChanged = "NotesChanged";
    // STORY-011: patient flow. Each event's Detail holds "<from> -> <to>".
    public const string CheckedIn = "CheckedIn";
    public const string TreatmentStarted = "TreatmentStarted";
    public const string Completed = "Completed";
}
