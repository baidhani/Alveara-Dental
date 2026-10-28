using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 3: the operator's confirmation of a commit. It checks everything that can be checked before any patient is written, records the confirmation (who, when, the policy) and puts the work on the durable
/// job queue (<see cref="ImportCommitEngine"/> does it). The order is deliberate:
/// 1. the batch must be a dry-run patient batch; the confirmation must name the policy and the number of valid rows the operator was shown, and that number must still be the number of valid rows (409 results_changed otherwise:
///    the operator is looking at a screen that is out of date); there must be at least one valid row;
/// 2. all-or-nothing is refused while any row is rejected or unresolved (409 batch_has_problems: fix the file or decide the rows) and when it has more valid rows than one transaction should carry (409 too_large_for_all_or_nothing,
///    <see cref="ImportOptions.MaxAllOrNothingRows"/>: commit the valid rows only instead); committing only the valid rows has no such limit and leaves the other rows in their queues;
/// 3. the stored file must still be the uploaded one (its hash is checked); then the batch becomes Committing with its confirmation in ONE save (the row version stops two people confirming at once: 409 batch_changed), and the
///    jobs go on the queue: with all-or-nothing one job for the whole file, otherwise one job for each window of <see cref="ImportOptions.CommitChunkRows"/> rows, then a finish job. The jobs' keys name the batch, the round and the
///    window, so the same request twice (or a crash between the save and the queue) puts nothing twice: confirming again while a batch is Committing just makes sure its jobs are on the queue.
/// Failure modes handled: the above, an enqueue that fails after the save (confirm again), a repeated confirmation. The audit entry carries the policy and counts. No external call is made.
/// </summary>
public class ImportCommitService(AlveraDbContext db, IBackgroundJobQueue queue, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock, ImportIntakeService intake)
{
    private readonly ImportOptions _options = options.Value;

    /// <param name="canManageBilling">Whether the caller also holds <c>ManageBilling</c>: loading opening balances writes to patients' ledgers, which <c>BulkImportData</c> alone does not allow (403 billing_permission_required, checked before anything changes).</param>
    public async Task<ImportBatchView> CommitAsync(Guid batchId, string? policy, int? expectedValidRows, Guid actor, CancellationToken ct, bool canManageBilling = false)
    {
        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new ImportException("batch_not_found", "There is no import batch with that id.", 404);
        if (batch.SourceKind is not (ImportSourceKind.Patients or ImportSourceKind.Appointments or ImportSourceKind.OpeningBalances)) throw new ImportException("kind_not_supported_yet", $"Committing is not available for {batch.SourceKind} files yet.", 409);
        if (batch.SourceKind == ImportSourceKind.OpeningBalances && !canManageBilling)
            throw new ImportException("billing_permission_required", "Loading opening balances writes to patients' billing ledgers, which needs the Manage billing permission as well.", 403);

        if (batch.Status == ImportBatchStatus.Committing)                                // a repeated confirmation: make sure the jobs are on the queue and say where things are
        {
            await EnqueueAsync(batch, ct);
            return (await intake.GetAsync(batchId, ct))!;
        }

        if (batch.Status == ImportBatchStatus.Uploaded) throw new ImportException("dry_run_required", "Run the dry-run before committing.", 409);
        if (batch.Status != ImportBatchStatus.DryRunComplete) throw new ImportException("batch_not_open", $"This batch is {batch.Status} and cannot be committed.", 409);
        if (policy is null || !ImportBatchPolicy.All.Contains(policy)) throw new ImportException("invalid_policy", $"The policy must be one of {string.Join(", ", ImportBatchPolicy.All)}.", 400);
        if (batch.SourceKind is ImportSourceKind.Appointments or ImportSourceKind.OpeningBalances && policy == ImportBatchPolicy.AllOrNothing)
            throw new ImportException("policy_not_supported", $"{batch.SourceKind} are recorded one at a time, each in its own transaction, so all-or-nothing is not available for them. Commit the valid rows only.", 409);
        if (expectedValidRows is null) throw new ImportException("expected_valid_rows_required", "Send the number of valid rows you were shown, to confirm the commit.", 400);

        var valid = await db.ImportBatchRows.AsNoTracking().CountAsync(r => r.BatchId == batchId && r.Status == ImportRowStatus.Valid, ct);
        if (valid != expectedValidRows) throw new ImportException("results_changed", "The results changed since you looked at them. Reload the batch and confirm again.", 409);
        if (valid == 0) throw new ImportException("nothing_to_commit", "No row is valid, so there is nothing to commit.", 409);
        if (policy == ImportBatchPolicy.AllOrNothing)
        {
            if (batch.RejectedRows > 0 || batch.UnresolvedRows > 0) throw new ImportException("batch_has_problems", "All-or-nothing needs every row to be valid or skipped. Fix the file or decide the unresolved rows, or commit the valid rows only.", 409);
            if (valid > _options.MaxAllOrNothingRows) throw new ImportException("too_large_for_all_or_nothing", $"All-or-nothing can carry at most {_options.MaxAllOrNothingRows} valid rows. Commit the valid rows only.", 409);
        }

        if (!await files.Blobs.VerifyIntegrityAsync(batch.FileBlobId, batch.FileSha256, ct)) throw new ImportException("import_file_changed", "The stored import file no longer matches the file that was uploaded.", 409);

        var now = clock.UtcNow;
        batch.Policy = policy;
        batch.Status = ImportBatchStatus.Committing;
        batch.CommitRound++;
        batch.CommitStartedAtUtc = now;
        batch.CommitConfirmedByUserId = actor;
        batch.CommitOutcome = null;
        batch.UpdatedAtUtc = now;
        AuditService.Record(db, AuditEventTypes.ImportCommitStarted, nameof(ImportBatch), batch.Id, actor, $"Import commit confirmed ({policy}): {valid} valid row(s) of {batch.RowCount}.", null, batch.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ImportException("batch_changed", "Someone else changed this batch. Reload it and try again.", 409);
        }

        await EnqueueAsync(batch, ct);
        return (await intake.GetAsync(batchId, ct))!;
    }

    /// <summary>Puts the round's jobs on the queue: the rows jobs first, then the finish job. Every key is unique for the work, so doing this twice leaves one of each.</summary>
    private async Task EnqueueAsync(ImportBatch batch, CancellationToken ct)
    {
        var last = batch.RowCount + 1;                                                   // the rows are numbered 2 to RowCount + 1
        var size = batch.Policy == ImportBatchPolicy.AllOrNothing ? last : Math.Max(1, _options.CommitChunkRows);
        for (var from = 2; from <= last; from += size)
        {
            var job = new ImportCommitJob(batch.Id, batch.CommitRound, ImportCommitJobs.RowsStep, from, Math.Min(from + size - 1, last), 0);
            await queue.EnqueueAsync(ImportCommitJobs.RowsKey(batch.Id, batch.CommitRound, from), ImportCommitJobs.JobType, ImportCommitJobs.Payload(job), ct);
        }

        var finish = new ImportCommitJob(batch.Id, batch.CommitRound, ImportCommitJobs.FinishStep, 0, 0, 0);
        await queue.EnqueueAsync(ImportCommitJobs.FinishKey(batch.Id, batch.CommitRound, 0), ImportCommitJobs.JobType, ImportCommitJobs.Payload(finish), ct);
    }
}
