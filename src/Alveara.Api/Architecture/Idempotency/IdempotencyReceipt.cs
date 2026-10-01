namespace Alveara.Api.Architecture.Idempotency;

/// <summary>
/// ALV-002-C01: durable record that a specific consequential command already ran, keyed by a
/// caller-supplied idempotency key scoped to a command type - generalizes the pattern
/// Architecture/BackgroundWork's BackgroundJob.IdempotencyKey/BackgroundJobEffectReceipt already
/// established for background jobs, to any consequential command (HTTP-triggered or otherwise).
/// A unique index on (CommandType, IdempotencyKey) (see AlveraDbContext) makes duplicate detection
/// a real database-enforced guarantee, not an application-level check-then-act race.
/// </summary>
public class IdempotencyReceipt
{
    public Guid Id { get; set; }
    public required string CommandType { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}
