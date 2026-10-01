using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Idempotency;

/// <summary>
/// ALV-002-C01: application-level half of the idempotency primitive. The real guarantee against a
/// genuine race (two concurrent callers submitting the same command/key at once) is the database's
/// own unique index on (CommandType, IdempotencyKey) - <see cref="MarkProcessed"/> can still throw
/// a unique-constraint <see cref="DbUpdateException"/> if two callers race past
/// <see cref="AlreadyProcessedAsync"/> simultaneously. A caller MUST check
/// <see cref="IsDuplicateReceiptViolation"/> before treating a caught <see cref="DbUpdateException"/>
/// as "already processed" - R02 (review finding ALV-002-C01-R01-04's "Preserve and clarify" note):
/// a broad catch on the exception TYPE alone would also swallow a genuinely failed, unrelated
/// write in the same SaveChanges call (e.g. a constraint violation on the command's own business
/// effect) and misreport it as a successfully-deduplicated command.
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

    /// <summary>True only if the exception is genuinely this receipt's own unique-index violation
    /// (SQL Server error 2601/2627 naming the IdempotencyReceipts unique index) - never true for an
    /// unrelated constraint violation or any other failure that happened to occur in the same
    /// SaveChanges call. A caller must only treat the command as "already processed" when this
    /// returns true; otherwise the exception is a genuine failure and must propagate.</summary>
    public static bool IsDuplicateReceiptViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        sqlEx.Errors.Cast<SqlError>().Any(e =>
            (e.Number is 2601 or 2627) &&
            e.Message.Contains("IX_IdempotencyReceipts_CommandType_IdempotencyKey", StringComparison.OrdinalIgnoreCase));
}
