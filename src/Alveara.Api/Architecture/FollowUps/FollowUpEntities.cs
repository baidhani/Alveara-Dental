namespace Alveara.Api.Architecture.FollowUps;

/// <summary>STORY-009: the states of a follow-up task. An open task is the only one that can change; completed and cancelled are final (the database refuses any change afterwards).</summary>
public static class FollowUpStatuses
{
    public const string Open = "Open";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public static readonly IReadOnlyList<string> All = [Open, Completed, Cancelled];
}

/// <summary>The kinds of event in a task's append-only history. Together with the audit entry written beside each one, this is the completion history and the user-and-timestamp log.</summary>
public static class FollowUpEventKinds
{
    public const string Created = "Created";
    public const string Edited = "Edited";
    public const string Reassigned = "Reassigned";
    public const string DueChanged = "DueChanged";
    public const string Reminded = "Reminded";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string RecallCreated = "RecallCreated";
    // ALV-009-C01
    public const string Assigned = "Assigned";
    public const string PriorityChanged = "PriorityChanged";
    /// <summary>A task made by the system (a recall that came due, an accepted treatment nobody has scheduled): no person is behind it.</summary>
    public const string SystemCreated = "SystemCreated";
    /// <summary>A task the system closed because what it was about is settled (the treatment was scheduled, completed or declined).</summary>
    public const string SystemClosed = "SystemClosed";
    public static readonly IReadOnlyList<string> All = [Created, Edited, Reassigned, DueChanged, Reminded, Completed, Cancelled, RecallCreated, Assigned, PriorityChanged, SystemCreated, SystemClosed];
    /// <summary>The kinds written by the system rather than by a person (the only ones with no actor).</summary>
    public static readonly IReadOnlyList<string> BySystem = [Reminded, SystemCreated, SystemClosed];
}

/// <summary>The channels a reminder can be delivered by. Only the in-app channel exists now; an email channel is designed for and not built (docs/design/FOLLOW_UP_TASKS_DESIGN.md, "Later channels").</summary>
public static class FollowUpChannels
{
    public const string InApp = "InApp";
    public static readonly IReadOnlyList<string> All = [InApp];
}

/// <summary>How a reminder reached someone: to the responsible party, or to the office managers because the responsible party could not be reminded.</summary>
public static class FollowUpDeliveryOutcomes
{
    public const string Delivered = "Delivered";
    public const string Fallback = "Fallback";
    /// <summary>ALV-009-C01: the task has no owner, so the office managers were told (this is how an unassigned task is reminded, not a fallback).</summary>
    public const string Unassigned = "Unassigned";
    public static readonly IReadOnlyList<string> All = [Delivered, Fallback, Unassigned];
}

/// <summary>ALV-009-C01: how urgent a task is. Shown in words as well as colour; the queue sorts by it.</summary>
public static class FollowUpPriorities
{
    public const string Low = "Low";
    public const string Normal = "Normal";
    public const string High = "High";
    public const string Urgent = "Urgent";
    public static readonly IReadOnlyList<string> All = [Low, Normal, High, Urgent];
    /// <summary>Sort weight, most urgent first.</summary>
    public static int Weight(string priority) => priority switch { Urgent => 0, High => 1, Normal => 2, _ => 3 };
}

/// <summary>ALV-009-C01: where a task came from. A person's own task is Manual (including the next task a completion makes from a recall interval); the system makes Recall and UnscheduledTreatment tasks.</summary>
public static class FollowUpKinds
{
    public const string Manual = "Manual";
    public const string Recall = "Recall";
    public const string UnscheduledTreatment = "UnscheduledTreatment";
    /// <summary>ALV-009-C02: the system's task to chase a referral that has reached its follow-up date, and to chase a lab case that has reached its due date.</summary>
    public const string ReferralFollowUp = "ReferralFollowUp";
    public const string LabCaseDue = "LabCaseDue";
    public static readonly IReadOnlyList<string> All = [Manual, Recall, UnscheduledTreatment, ReferralFollowUp, LabCaseDue];
}

public static class FollowUpRules
{
    /// <summary>The longest key that makes a system task unique (see <see cref="FollowUpTask.SourceKey"/>).</summary>
    public const int SourceKeyMaxLength = 100;
    public static string RecallSourceKey(Guid patientId, DateOnly dueDate) => $"recall:{patientId:N}:{dueDate:yyyyMMdd}";
    public static string UnscheduledSourceKey(Guid planItemId, int stateSequence) => $"unscheduled:{planItemId:N}:{stateSequence}";
    /// <summary>ALV-009-C02: one chase task per referral or lab case and due date (a changed due date is a new chase).</summary>
    public static string ReferralSourceKey(Guid caseId, DateOnly dueOn) => $"referral:{caseId:N}:{dueOn:yyyyMMdd}";
    public static string LabSourceKey(Guid caseId, DateOnly dueOn) => $"lab:{caseId:N}:{dueOn:yyyyMMdd}";
    public const int TitleMaxLength = 120;
    public const int NoteMaxLength = 500;
    public const int DetailMaxLength = 200;
    public const int MaxRecallIntervalDays = 3650;
    /// <summary>A due date may be at most this many days in the past (back-dating a task is allowed, a typo is not) ...</summary>
    public const int MaxPastDays = 366;
    /// <summary>... and this many days in the future (about ten years).</summary>
    public const int MaxFutureDays = 3660;
    /// <summary>The most tasks a list returns.</summary>
    public const int MaxListSize = 200;
    /// <summary>The key of the in-app reminder notification for one task at one due time. Dots, not hyphens: the notification source-key check (CK_Notifications_SourceKey) refuses a hyphen in practice, because its character class reads "_-]" as a range.</summary>
    public static string ReminderSourceKey(Guid taskId, DateTimeOffset dueAtUtc) => $"followup.due:{taskId:N}:{dueAtUtc.UtcTicks}";
}

/// <summary>
/// STORY-009: one follow-up task for one patient, assigned to a responsible person. Plain text only (a title and an optional note, bounded). Open until completed or cancelled, then final. A recall interval
/// makes completion create the next task once (<see cref="RecallOfTaskId"/> is unique, so a repeat cannot create a second). <see cref="RowVersion"/> is the concurrency token.
/// </summary>
public class FollowUpTask
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>The owner. Null means unassigned (a task the system made, waiting for someone to take it); STORY-009's own create route still requires one.</summary>
    public Guid? AssigneeUserId { get; set; }
    public required string Title { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public required string Status { get; set; }
    public int? RecallIntervalDays { get; set; }
    /// <summary>When this task is the recall created by completing another, that task's id (unique: a task creates at most one recall).</summary>
    public Guid? RecallOfTaskId { get; set; }
    /// <summary>ALV-009-C01: <see cref="FollowUpPriorities"/>.</summary>
    public string Priority { get; set; } = FollowUpPriorities.Normal;
    /// <summary>ALV-009-C01: <see cref="FollowUpKinds"/>.</summary>
    public string Kind { get; set; } = FollowUpKinds.Manual;
    /// <summary>ALV-009-C01: an appointment of the same patient this task is about, if any (a database trigger checks the patient).</summary>
    public Guid? AppointmentId { get; set; }
    /// <summary>ALV-009-C01: for an UnscheduledTreatment task, the accepted treatment-plan item it is about.</summary>
    public Guid? SourcePlanItemId { get; set; }
    /// <summary>ALV-009-C02: for a ReferralFollowUp task, the referral case it chases (the database refuses a case of another patient).</summary>
    public Guid? SourceReferralCaseId { get; set; }
    /// <summary>ALV-009-C02: for a LabCaseDue task, the lab case it chases.</summary>
    public Guid? SourceLabCaseId { get; set; }
    /// <summary>ALV-009-C01: for a task the system made, a key naming what it was made for (a patient's due date, a plan item's acceptance); unique, so a retry or a restart can never make the same task twice.</summary>
    public string? SourceKey { get; set; }
    /// <summary>ALV-009-C01: true when the system closed the task (then <see cref="ClosedByUserId"/> is null).</summary>
    public bool ClosedBySystem { get; set; }
    /// <summary>The person who made the task; null only for a task the system made (a Recall or UnscheduledTreatment task).</summary>
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>Set together with <see cref="ClosedByUserId"/> when the task is completed or cancelled, and only then.</summary>
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string? ClosureNote { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>An immutable entry in a task's history. <see cref="ActorUserId"/> is null only for the system's own reminder. <see cref="Detail"/> is a short, bounded fact (for example the new due time), never the task's note.</summary>
public class FollowUpTaskEvent
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public required string Kind { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? Detail { get; set; }
}

/// <summary>The record that a reminder for one task at one due time was delivered by one channel: unique on (task, due time, channel), so a repeat of the reminder pass cannot send a second.</summary>
public class FollowUpReminderDelivery
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public required string Channel { get; set; }
    public Guid RecipientUserId { get; set; }
    public required string Outcome { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
