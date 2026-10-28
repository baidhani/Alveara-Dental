using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 6: starts a commit again when it is stuck. A confirmation that is repeated while a batch is Committing only makes sure the round's jobs are on the queue, and the queue returns a job that already exists even
/// when it has failed for good, so a round whose finish job failed (or whose jobs were lost) would never move again. A batch is <b>stuck</b> when it is Committing and no job of its current round is waiting or running. Retrying a
/// stuck batch starts a NEW round: fresh jobs that skip every row already Accepted (they are keyed and replay-safe, so nothing is registered, scheduled or loaded twice) and settle the batch. Anything else is refused (409
/// <c>not_stuck</c>), so a retry can never run alongside live work. The caller who retries becomes the confirmer (what the rows are recorded under), which is why a balance batch also needs ManageBilling. The row version
/// stops two people retrying at once (409 <c>batch_changed</c>); a job left over from an older round does nothing. The audit entry carries the round and the status only.
/// Failure modes handled: a failed finish job, jobs lost between the save and the queue, a double click, a repeat. Not handled: a rows job that keeps failing (the retry ends Interrupted again, which the counters and the reconciliation show).
/// </summary>
public class ImportRetryService(AlveraDbContext db, IBackgroundJobQueue queue, IOptions<ImportOptions> options, IPracticeClock clock, ImportIntakeService intake)
{
    private readonly ImportOptions _options = options.Value;

    /// <summary>True when the batch is Committing and no job of its current round is Pending or InProgress.</summary>
    public static async Task<bool> IsStuckAsync(AlveraDbContext db, ImportBatch batch, CancellationToken ct)
    {
        if (batch.Status != ImportBatchStatus.Committing) return false;
        var rows = ImportCommitJobs.RowsKeyPrefix(batch.Id, batch.CommitRound);
        var finish = ImportCommitJobs.FinishKeyPrefix(batch.Id, batch.CommitRound);
        return !await db.BackgroundJobs.AsNoTracking().AnyAsync(j => j.JobType == ImportCommitJobs.JobType && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.InProgress)
            && (j.IdempotencyKey.StartsWith(rows) || j.IdempotencyKey.StartsWith(finish)), ct);
    }

    public async Task<ImportBatchView> RetryAsync(Guid batchId, Guid actor, bool canManageBilling, CancellationToken ct)
    {
        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new ImportException("batch_not_found", "There is no import batch with that id.", 404);
        if (batch.SourceKind == ImportSourceKind.OpeningBalances && !canManageBilling)
            throw new ImportException("billing_permission_required", "Loading opening balances writes to patients' billing ledgers, which needs the Manage billing permission as well.", 403);
        if (!await IsStuckAsync(db, batch, ct)) throw new ImportException("not_stuck", $"Only a commit that has stopped can be retried; this batch is {batch.Status} and its commit is not stuck.", 409);

        var now = clock.UtcNow;
        batch.CommitRound++;
        batch.CommitStartedAtUtc = now;
        batch.CommitConfirmedByUserId = actor;
        batch.CommitOutcome = null;
        batch.UpdatedAtUtc = now;
        AuditService.Record(db, AuditEventTypes.ImportCommitRetried, nameof(ImportBatch), batch.Id, actor, $"Import commit retried (round {batch.CommitRound}): the batch was {ImportBatchStatus.Committing} with no job running.", null, batch.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ImportException("batch_changed", "Someone else changed this batch. Reload it and try again.", 409);
        }

        await ImportCommitJobs.EnqueueRoundAsync(queue, batch, _options.CommitChunkRows, ct);
        return (await intake.GetAsync(batchId, ct))!;
    }
}
