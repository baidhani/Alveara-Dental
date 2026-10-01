using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Idempotency;

/// <summary>
/// ALV-002-C01: application-level half of the idempotency primitive. The real guarantee against a
/// genuine race (two concurrent callers submitting the same command/key at once) is the database's
/// own unique index on (CommandType, IdempotencyKey) - <see cref="MarkProcessed"/> can still throw
/// a unique-constraint <see cref="DbUpdateException"/> if two callers race past
/// <see cref="AlreadyProcessedAsync"/> simultaneously, and callers should treat that exception the
/// same as "already processed" (the command's effect was, or is about to be, applied by whichever
/// caller's receipt actually committed).
/// </summary>
public static class IdempotencyGuard
{
    public static Task<bool> AlreadyProcessedAsync(AlveraDbContext db, string commandType, string idempotencyKey, CancellationToken cancellationToken = default) =>
        db.IdempotencyReceipts.AnyAsync(r => r.CommandType == commandType && r.IdempotencyKey == idempotencyKey, cancellationToken);

    /// <summary>Stages the receipt on the change tracker only - callers add this to the same
    /// AlveraDbContext as the command's own effect and save once, exactly like AuditService.Record,
    /// so the receipt and the effect either both commit or neither does.</summary>
    public static void MarkProcessed(AlveraDbContext db, string commandType, string idempotencyKey)
    {
        db.IdempotencyReceipts.Add(new IdempotencyReceipt
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            IdempotencyKey = idempotencyKey,
            RecordedAtUtc = DateTimeOffset.UtcNow,
        });
    }
}
