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
    /// <summary>ALV-N021 A2 (OD-19): the controlled reason class of a cancellation (<see cref="AppointmentCancelCategories"/>). Required by the API from A2 on; null on cancellations made before it ("Not categorised").</summary>
    public string? CancelCategory { get; set; }
    /// <summary>When and by whom the appointment became Cancelled or NoShow.</summary>
    public DateTimeOffset? StatusChangedAtUtc { get; set; }
    public Guid? StatusChangedByUserId { get; set; }

    /// <summary>STORY-011: how far the visit has got (see <see cref="PatientFlowStates"/>). Only meaningful while Status is Scheduled.</summary>
    public string FlowState { get; set; } = PatientFlowStates.Scheduled;
    /// <summary>When and by whom the flow last moved (null until the first move).</summary>
    public DateTimeOffset? FlowChangedAtUtc { get; set; }
    public Guid? FlowChangedByUserId { get; set; }

    // ALV-011-C01: who and where the patient is ACTUALLY with during the visit, as opposed to the booked provider and operatory above. Null means "as booked".
    // Changing them never moves the booking: the calendar and every conflict rule keep using ProviderProfileId and OperatoryId.
    public Guid? VisitProviderProfileId { get; set; }
    public Guid? VisitOperatoryId { get; set; }

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
    /// <summary>ALV-N021 A2: the reason the actor gave (undo check-in, reassignment, block override, keep despite block, cancellation note). Never PHI by design: staff wording, kept out of the audit log.</summary>
    public string? Reason { get; set; }
    /// <summary>ALV-N021 A2: the controlled category of the move where one applies (a cancellation's <see cref="AppointmentCancelCategories"/>).</summary>
    public string? Category { get; set; }
    // ALV-N021 A2 (migration M4, owner decision 2026-11-15): where a move took the appointment TO, written in the same save as the move (never derived later from other events or the
    // appointment's current state). Only on Rescheduled, ProviderReassigned and RoomChanged; null on every event written before M4 (their destination is not known). The names are the
    // display names at the time of the move, so a later rename does not rewrite history.
    public DateTimeOffset? NewStartUtc { get; set; }
    public Guid? NewProviderProfileId { get; set; }
    public string? NewProviderName { get; set; }
    public Guid? NewOperatoryId { get; set; }
    public string? NewOperatoryName { get; set; }
}

/// <summary>ALV-N021 A2 (M4): the destination of a move as it is recorded on its history event.</summary>
public static class MoveDestination
{
    public const int NameMaxLength = 120;   // the length of a staff display name and an operatory name

    /// <summary>The event types that may carry a destination.</summary>
    public static readonly IReadOnlyList<string> EventTypes = [AppointmentEventTypes.Rescheduled, AppointmentEventTypes.ProviderReassigned, AppointmentEventTypes.RoomChanged];

    /// <summary>The provider's display name now (the caller loaded the staff profile with it); "Staff member" if it is missing, so the recorded destination is never blank.</summary>
    public static string ProviderName(Alveara.Api.Architecture.Identity.ProviderProfile provider) =>
        string.IsNullOrWhiteSpace(provider.StaffProfile?.DisplayName) ? Alveara.Api.Architecture.Clinical.ClinicalNames.Fallback : provider.StaffProfile.DisplayName;
}

/// <summary>ALV-N021 A2 (OD-19): why an appointment was cancelled, as a controlled list (the cancellations report counts them).</summary>
public static class AppointmentCancelCategories
{
    public const string Patient = "patient";
    public const string Office = "office";
    public const string Provider = "provider";
    public const string Weather = "weather";
    public const string Other = "other";
    public static readonly IReadOnlyList<string> All = [Patient, Office, Provider, Weather, Other];
    /// <summary>The report's and the drill's name for a cancellation made before categories existed (shown as "Not categorised").</summary>
    public const string Uncategorised = "uncategorised";
}

/// <summary>
/// ALV-N021 A2 (OD-19): "short notice" is derived, never stored: a cancellation made less than <see cref="ShortNoticeMinutes"/> before the start. Minutes are counted the way the cancellations
/// report counts them in SQL (minute boundaries crossed, like <c>DATEDIFF(minute, ...)</c>), so the appointment panel and the report always agree. No recorded time means not known (null).
/// </summary>
public static class CancellationRules
{
    public const int ShortNoticeMinutes = 24 * 60;
    public const int NoteMaxLength = 400;

    public static int MinutesBetween(DateTimeOffset from, DateTimeOffset to) =>
        (int)((to.UtcTicks / TimeSpan.TicksPerMinute) - (from.UtcTicks / TimeSpan.TicksPerMinute));

    public static bool? IsShortNotice(Appointment a) =>
        a.Status != AppointmentStatuses.Cancelled || a.StatusChangedAtUtc is null ? null : MinutesBetween(a.StatusChangedAtUtc.Value, a.StartUtc) < ShortNoticeMinutes;
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
    // ALV-011-C01: the rest of the visit. A move's Detail holds "<from> -> <to>"; an assignment change records the previous provider/operatory.
    public const string Confirmed = "Confirmed";
    public const string Ready = "Ready";
    public const string Seated = "Seated";
    public const string CheckedOut = "CheckedOut";
    /// <summary>ALV-011-C01's "Seen by" change. Retired by ALV-N021 A2: no longer written; existing rows stay in the history read-only.</summary>
    public const string AssignmentChanged = "AssignmentChanged";
    // ALV-N021 A2: each carries the actor and the reason; a reassignment and a room change record the previous provider and room
    public const string CheckInUndone = "CheckInUndone";
    public const string ProviderReassigned = "ProviderReassigned";
    public const string RoomChanged = "RoomChanged";
    /// <summary>OD-18: the provider will see the patient despite a blocked time over the appointment (reason required); clears the flag for the blocks that existed then.</summary>
    public const string KeptDespiteBlock = "KeptDespiteBlock";
}

/// <summary>ALV-N021 A2: the one rule for a reason a person types (undo check-in, reassignment, room change): trimmed, line endings normalised, at most 500 characters (the history column).</summary>
public static class VisitReasons
{
    public const int MaxLength = 500;

    public static string Required(string? text, string what) =>
        Optional(text) ?? throw new SchedulingException("reason_required", $"Give a reason for {what}.", 400);

    public static string? Optional(string? text)
    {
        var clean = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (clean.Length > MaxLength) throw new SchedulingException("reason_too_long", $"Keep the reason to {MaxLength} characters.", 400);
        return clean.Length == 0 ? null : clean;
    }
}
