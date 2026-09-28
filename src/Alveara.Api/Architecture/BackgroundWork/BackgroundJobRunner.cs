using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Alveara.Api.Architecture.Logging;
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
/// calls an external system), a local "check a marker, then act" sequence is NOT sufficient on
/// its own (per N002-R02-03): a crash after the external action but before the marker commits
/// still allows a duplicate on retry, for the same reason N002-R01-01 existed. Such a handler
/// must instead give the external system a stable idempotency token it honors itself (so a
/// duplicate call is a safe no-op at the target), or use a proper outbox/inbox delivery pattern.
/// This story does not implement such a handler — it only establishes the local-effect contract
/// above; the external-effect contract is future work for whichever story adds the first
/// non-transactional handler.
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
    // SQL Server error numbers for a unique-constraint/unique-index violation.
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public async Task<BackgroundJob> EnqueueAsync(string idempotencyKey, string jobType, string? payloadJson, CancellationToken cancellationToken = default)
    {
        var existing = await db.BackgroundJobs.AsNoTracking().SingleOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, cancellationToken);
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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return job;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // N002-R02-03: two concurrent callers can both pass the read-check above before
            // either commits. The unique index on IdempotencyKey then makes exactly one insert
            // win; the loser converges on the winner's row instead of surfacing a raw database
            // exception, so EnqueueAsync keeps its documented "idempotent enqueue" contract even
            // under a genuine race, not only when calls happen to be sequential.
            db.ChangeTracker.Clear(); // discard the failed insert attempt from this context
            return await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.IdempotencyKey == idempotencyKey, cancellationToken);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        sqlEx.Errors.Cast<SqlError>().Any(e => e.Number is UniqueConstraintViolation or UniqueIndexViolation);
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
public sealed class BackgroundJobRunner(
    AlveraDbContext db,
    IEnumerable<IBackgroundJobHandler> handlers,
    Microsoft.Extensions.Logging.ILogger<BackgroundJobRunner>? logger = null)
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
            var safeSummary = SafeErrorSummary(ex, jobId);
            await SetStatusAsync(jobId, statusAfterFailure, safeSummary, cancellationToken);
            // Safe-summary only (never ex.Message) — see SafeErrorSummary's doc comment.
            logger?.LogWarning("Background job execution failed: {SafeSummary}", safeSummary);
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

    /// <summary>
    /// PHI-safe diagnostic boundary (N002-R02-02): a handler's exception message is caller-
    /// supplied text this runner cannot assume is free of patient content — a future handler
    /// might throw something like "Failed to process appointment for Jane Doe." This never
    /// persists <c>ex.Message</c> anywhere; it persists only the exception's .NET type name
    /// (a developer-controlled identifier, not caller data) plus a correlation token, which is
    /// enough for an operator to find the job and its real stack trace in a dedicated,
    /// access-controlled diagnostics sink (not yet built — out of this story's scope) without
    /// exposing potentially sensitive text in an ordinary database column or log line.
    /// </summary>
    private static string SafeErrorSummary(Exception ex, Guid jobId) =>
        $"{ex.GetType().Name} ({PhiSafeLog.Correlate("BackgroundJob", jobId)})";
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
                //
                // Logged as the exception TYPE only, not the full exception/ex.Message
                // (N002-R02-02): this catch is a defense-in-depth backstop for anything
                // unexpected escaping ExecuteSingleJobAsync's own try/catch, so this runner
                // cannot assume the escaped exception's message is free of caller-supplied
                // (potentially patient-related) content either.
                logger.LogError("Background job poll failed ({ExceptionType}); will retry next interval.", ex.GetType().Name);
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
