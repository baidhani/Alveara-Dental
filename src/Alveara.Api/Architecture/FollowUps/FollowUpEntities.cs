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
    public static readonly IReadOnlyList<string> All = [Created, Edited, Reassigned, DueChanged, Reminded, Completed, Cancelled, RecallCreated];
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
    public static readonly IReadOnlyList<string> All = [Delivered, Fallback];
}

public static class FollowUpRules
{
    public const int TitleMaxLength = 120;
    public const int NoteMaxLength = 500;
    public const int DetailMaxLength = 200;
    public const int MaxRecallIntervalDays = 3650;
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
    public Guid AssigneeUserId { get; set; }
    public required string Title { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public required string Status { get; set; }
    public int? RecallIntervalDays { get; set; }
    /// <summary>When this task is the recall created by completing another, that task's id (unique: a task creates at most one recall).</summary>
    public Guid? RecallOfTaskId { get; set; }
    public Guid CreatedByUserId { get; set; }
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
