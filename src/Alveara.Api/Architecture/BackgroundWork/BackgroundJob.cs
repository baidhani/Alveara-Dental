namespace Alveara.Api.Architecture.BackgroundWork;

public enum BackgroundJobStatus
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
}

/// <summary>
/// Persisted background-work record, per ALV-N002: a minimal durable mechanism with persisted job
/// state, restart recovery, observable failure, and idempotency — no distributed broker. Consumed
/// later by recall/reminder work (ALV-009-C01) and scheduled backups (ALV-N004).
/// </summary>
public class BackgroundJob
{
    public Guid Id { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key (e.g. "recall-reminder:{patientId}:{date}"). Enforced
    /// unique so the same logical job can never be enqueued twice, and so a job that already
    /// completed can be safely re-submitted as a no-op rather than re-executed.
    /// </summary>
    public required string IdempotencyKey { get; set; }

    public required string JobType { get; set; }
    public string? PayloadJson { get; set; }

    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
}
