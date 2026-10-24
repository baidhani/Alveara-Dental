namespace Alveara.Api.Architecture.FollowUps;

/// <summary>A refusal with a stable code the client can show in words: <c>title_invalid</c>, <c>note_invalid</c>, <c>due_required</c>, <c>due_out_of_range</c>, <c>interval_invalid</c>, <c>assignee_invalid</c>, <c>patient_not_found</c>,
/// <c>task_not_found</c>, <c>idempotency_key_required</c>, <c>idempotency_key_reused</c>, <c>row_version_required</c>, <c>task_changed</c> (409, a stale row version), <c>task_closed</c> (409), <c>no_change</c> (409), <c>note_required</c>.</summary>
public sealed class FollowUpException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record CreateFollowUpRequest(string? Title, string? Note, DateTimeOffset? DueAtUtc, Guid? AssigneeUserId, int? RecallIntervalDays);

/// <summary>An edit replaces the editable fields as a whole and carries the version the person saw.</summary>
public sealed record UpdateFollowUpRequest(string? Title, string? Note, DateTimeOffset? DueAtUtc, Guid? AssigneeUserId, int? RecallIntervalDays, string? RowVersion);

public sealed record CompleteFollowUpRequest(string? Note, string? RowVersion);

/// <summary>Cancelling needs a written reason (kept as the closure note) so a task never disappears without one.</summary>
public sealed record CancelFollowUpRequest(string? Note, string? RowVersion);

public sealed record FollowUpView(
    Guid Id, Guid PatientId, string Title, string? Note, DateTimeOffset DueAtUtc, string Status, bool Overdue, Guid? AssigneeUserId, string? AssigneeName, int? RecallIntervalDays,
    Guid? RecallOfTaskId, Guid? RecallTaskId, Guid? CreatedByUserId, DateTimeOffset CreatedAtUtc, DateTimeOffset? ClosedAtUtc, Guid? ClosedByUserId, string? ClosureNote, string RowVersion,
    string Priority, string Kind, Guid? AppointmentId, Guid? SourcePlanItemId, bool ClosedBySystem,
    Guid? SourceReferralCaseId = null, Guid? SourceLabCaseId = null);

public sealed record FollowUpEventView(string Kind, Guid? ActorUserId, string? ActorName, DateTimeOffset OccurredAtUtc, string? Detail);

public sealed record FollowUpDetail(FollowUpView Task, IReadOnlyList<FollowUpEventView> Events);

/// <summary>The outcome of a write. <see cref="Replayed"/> is true when the same request had already been applied and nothing changed.</summary>
public sealed record FollowUpResult(FollowUpView Task, bool Replayed, Guid? RecallTaskId);

public sealed record FollowUpPage(IReadOnlyList<FollowUpView> Items, int Count, bool More);

/// <summary>A person a task can be assigned to: an enabled account whose role can see follow-ups. The username only, for choosing from a list.</summary>
public sealed record FollowUpAssignee(Guid Id, string Name, string Role);
