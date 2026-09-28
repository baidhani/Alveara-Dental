using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.BackgroundWork;

/// <summary>
/// A background job handler's effect. <paramref name="transactionalDb"/> is the SAME
/// <see cref="AlveraDbContext"/> and database transaction the runner uses to write the job's
/// completion receipt — any local-database effect a handler needs to make MUST go through this
/// context (add entities, but do not call <c>SaveChangesAsync</c> yourself) so the effect and the
/// "this job is done" bookkeeping commit atomically. This closes the crash window where a
/// separately-committed effect could be silently re-applied by a recovered retry
/// (see N002-R01-01).
///
/// For an effect that is NOT representable as a row in this database (e.g. a future handler that
/// calls an external system), the handler itself is responsible for idempotency: check whether
/// its own external effect was already applied (e.g. via a domain-specific marker it writes
/// through <paramref name="transactionalDb"/>) before performing the non-transactional action.
/// </summary>
public interface IBackgroundJobHandler
{
    string JobType { get; }
    Task ExecuteAsync(BackgroundJob job, AlveraDbContext transactionalDb, CancellationToken cancellationToken);
}

/// <summary>
/// Enqueues jobs idempotently: enqueuing the same <c>idempotencyKey</c> twice returns the
/// existing job rather than creating a duplicate.
/// </summary>
public interface IBackgroundJobQueue
{
    Task<BackgroundJob> EnqueueAsync(string idempotencyKey, string jobType, string? payloadJson, CancellationToken cancellationToken = default);
}

public sealed class BackgroundJobQueue(AlveraDbContext db) : IBackgroundJobQueue
{
    public async Task<BackgroundJob> EnqueueAsync(string idempotencyKey, string jobType, string? payloadJson, CancellationToken cancellationToken = default)
    {
        var existing = await db.BackgroundJobs.SingleOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null) return existing;

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            JobType = jobType,
            PayloadJson = payloadJson,
            Status = BackgroundJobStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return job;
    }
}

/// <summary>
/// Processes durable background jobs with an explicit crash-safety contract (N002-R01-01,
/// N002-R01-02):
///
/// 1. <b>Atomic claim.</b> Pending -> InProgress is a single conditional UPDATE
///    (<see cref="TryClaimAsync"/>), never a read-then-write, so two runner instances can never
///    both believe they claimed the same job.
/// 2. <b>Effect receipt.</b> A handler's database effect, the job's completion receipt, and the
///    job's Succeeded status are written in one transaction. If the process crashes after that
///    transaction commits but before anything else, the effect already happened exactly once. If
///    it crashes before the transaction commits, nothing happened and a retry is safe.
/// 3. <b>Recovery checks the receipt first.</b> A job recovered from a stale InProgress state is
///    only re-run through the handler if no receipt exists yet; otherwise it is completed without
///    re-invoking the handler.
/// 4. <b>Recovery respects MaxAttempts.</b> A stuck job that has already exhausted its attempts is
///    marked Failed on recovery, not silently reset to Pending forever.
///
/// Exposes granular, individually-testable steps rather than only an opaque polling loop, so
/// restart-recovery and idempotency are deterministically testable without an actual process
/// restart.
/// </summary>
public sealed class BackgroundJobRunner(AlveraDbContext db, IEnumerable<IBackgroundJobHandler> handlers)
{
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Recovery for jobs that were InProgress when the process previously stopped (crash, restart,
    /// deploy). A job that has not exhausted its attempts is reset to Pending so it gets picked up
    /// again; a job that has already exhausted <see cref="BackgroundJob.MaxAttempts"/> is marked
    /// Failed instead of being reset forever (N002-R01-02).
    /// </summary>
    public async Task<int> RecoverStuckJobsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - StuckThreshold;
        var stuck = await db.BackgroundJobs
            .Where(j => j.Status == BackgroundJobStatus.InProgress && j.StartedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var job in stuck)
        {
            job.Status = job.AttemptCount >= job.MaxAttempts
                ? BackgroundJobStatus.Failed
                : BackgroundJobStatus.Pending;
            if (job.Status == BackgroundJobStatus.Failed)
            {
                job.LastError = "Exhausted MaxAttempts while stuck InProgress across a process restart.";
            }
        }
        if (stuck.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return stuck.Count;
    }

    /// <summary>Processes every currently-Pending job once. Returns the number actually claimed and processed.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        var pendingIds = await db.BackgroundJobs
            .Where(j => j.Status == BackgroundJobStatus.Pending)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var jobId in pendingIds)
        {
            if (await ExecuteSingleJobAsync(jobId, cancellationToken)) processed++;
        }
        return processed;
    }

    /// <summary>Atomically claims a job (Pending -> InProgress) in one conditional UPDATE. Returns false if another runner already claimed it, or it is no longer Pending.</summary>
    private async Task<bool> TryClaimAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var affected = await db.BackgroundJobs
            .Where(j => j.Id == jobId && j.Status == BackgroundJobStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, BackgroundJobStatus.InProgress)
                    .SetProperty(j => j.StartedAtUtc, DateTimeOffset.UtcNow)
                    .SetProperty(j => j.AttemptCount, j => j.AttemptCount + 1),
                cancellationToken);
        return affected == 1;
    }

    /// <returns>true if this call actually claimed and finished processing the job.</returns>
    private async Task<bool> ExecuteSingleJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (!await TryClaimAsync(jobId, cancellationToken)) return false;

        // Fetch a clean, untracked snapshot for handler lookup / attempt-count reads —
        // ExecuteUpdateAsync above did not track the entity.
        var snapshot = await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, cancellationToken);

        var alreadyApplied = await db.BackgroundJobEffectReceipts.AsNoTracking().AnyAsync(r => r.JobId == jobId, cancellationToken);
        if (alreadyApplied)
        {
            // The effect committed on a prior attempt (crash happened after that commit but
            // before this bookkeeping) — do not re-invoke the handler, just finish.
            await CompleteAsync(jobId, cancellationToken);
            return true;
        }

        var handler = handlers.FirstOrDefault(h => h.JobType == snapshot.JobType);
        if (handler is null)
        {
            await SetStatusAsync(jobId, BackgroundJobStatus.Failed, $"No handler registered for job type '{snapshot.JobType}'.", cancellationToken);
            return true;
        }

        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await handler.ExecuteAsync(snapshot, db, cancellationToken); // handler may add entities to `db`; must not call SaveChanges itself

            db.BackgroundJobEffectReceipts.Add(new BackgroundJobEffectReceipt { JobId = jobId, RecordedAtUtc = DateTimeOffset.UtcNow });

            var trackedJob = await db.BackgroundJobs.SingleAsync(j => j.Id == jobId, cancellationToken);
            trackedJob.Status = BackgroundJobStatus.Succeeded;
            trackedJob.CompletedAtUtc = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken); // effect + receipt + status commit together
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Explicitly roll back first: any partial handler writes and the receipt must never
            // persist. This also ends the ambient transaction *before* writing the failure
            // status below — leaving it open would silently roll back that write too once the
            // `await using` disposes it.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var exhausted = snapshot.AttemptCount >= snapshot.MaxAttempts;
            var statusAfterFailure = exhausted ? BackgroundJobStatus.Failed : BackgroundJobStatus.Pending;
            await SetStatusAsync(jobId, statusAfterFailure, ex.Message, cancellationToken);
        }

        return true;
    }

    private async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await db.BackgroundJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, BackgroundJobStatus.Succeeded)
                    .SetProperty(j => j.CompletedAtUtc, DateTimeOffset.UtcNow),
                cancellationToken);
    }

    private async Task SetStatusAsync(Guid jobId, BackgroundJobStatus status, string error, CancellationToken cancellationToken)
    {
        await db.BackgroundJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(j => j.Status, status)
                    .SetProperty(j => j.LastError, error),
                cancellationToken);
    }
}

/// <summary>
/// Thin IHostedService wrapper that periodically calls <see cref="BackgroundJobRunner"/>'s
/// testable methods. The actual logic lives in BackgroundJobRunner, which is what the tests
/// exercise directly.
///
/// Recovery runs on <b>every</b> poll cycle, not only once at startup (N002-R01-02): a process
/// that restarts within the 5-minute stuck threshold would otherwise never re-check a job it
/// claimed just before crashing. Both recovery and processing are inside the same guarded loop
/// (N002-R01-03): a database exception during recovery no longer escapes and crashes the host —
/// it is logged and retried next interval, the same way a processing exception already was.
/// </summary>
public sealed class BackgroundJobHostedService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobRunnerHeartbeat heartbeat,
    ILogger<BackgroundJobHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<BackgroundJobRunner>();

                var recovered = await runner.RecoverStuckJobsAsync(stoppingToken);
                if (recovered > 0) logger.LogInformation("Recovered {Count} stuck background job(s).", recovered);

                await runner.ProcessOnceAsync(stoppingToken);
                heartbeat.RecordPoll();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Must never escape: an unhandled exception here would stop the whole host
                // (N002-R01-03), e.g. if the database is briefly unavailable at startup.
                logger.LogError(ex, "Background job poll failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }
}
