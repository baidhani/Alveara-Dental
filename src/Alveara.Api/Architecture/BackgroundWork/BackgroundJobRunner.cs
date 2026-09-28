using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.BackgroundWork;

public interface IBackgroundJobHandler
{
    string JobType { get; }
    Task ExecuteAsync(BackgroundJob job, CancellationToken cancellationToken);
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
/// Processes durable background jobs. Exposes granular, individually-testable steps
/// (<see cref="RecoverStuckJobsAsync"/>, <see cref="ProcessOnceAsync"/>) rather than only an
/// opaque polling loop, so restart-recovery and idempotency can be tested deterministically
/// without an actual process restart.
/// </summary>
public sealed class BackgroundJobRunner(AlveraDbContext db, IEnumerable<IBackgroundJobHandler> handlers)
{
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Recovery for jobs that were InProgress when the process previously stopped (crash, restart,
    /// deploy) — resets them to Pending so they get picked up again, rather than leaving them
    /// stuck forever. This is what "restart recovery" means for this mechanism.
    /// </summary>
    public async Task<int> RecoverStuckJobsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - StuckThreshold;
        var stuck = await db.BackgroundJobs
            .Where(j => j.Status == BackgroundJobStatus.InProgress && j.StartedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        foreach (var job in stuck)
        {
            job.Status = BackgroundJobStatus.Pending;
        }
        if (stuck.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return stuck.Count;
    }

    /// <summary>Processes every currently-Pending job once. Returns the number processed.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        var pending = await db.BackgroundJobs
            .Where(j => j.Status == BackgroundJobStatus.Pending)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var job in pending)
        {
            await ExecuteSingleJobAsync(job, cancellationToken);
            processed++;
        }
        return processed;
    }

    private async Task ExecuteSingleJobAsync(BackgroundJob job, CancellationToken cancellationToken)
    {
        // Claim the job (Pending -> InProgress) before executing, so a concurrent runner instance
        // never double-dispatches it — this update is the linearization point.
        job.Status = BackgroundJobStatus.InProgress;
        job.StartedAtUtc = DateTimeOffset.UtcNow;
        job.AttemptCount++;
        await db.SaveChangesAsync(cancellationToken);

        var handler = handlers.FirstOrDefault(h => h.JobType == job.JobType);
        if (handler is null)
        {
            job.Status = BackgroundJobStatus.Failed;
            job.LastError = $"No handler registered for job type '{job.JobType}'.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await handler.ExecuteAsync(job, cancellationToken);
            job.Status = BackgroundJobStatus.Succeeded;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            job.LastError = ex.Message;
            job.Status = job.AttemptCount >= job.MaxAttempts ? BackgroundJobStatus.Failed : BackgroundJobStatus.Pending;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Thin IHostedService wrapper that periodically calls <see cref="BackgroundJobRunner"/>'s
/// testable methods. The actual logic lives in BackgroundJobRunner, which is what the tests
/// exercise directly.
/// </summary>
public sealed class BackgroundJobHostedService(
    IServiceScopeFactory scopeFactory,
    BackgroundJobRunnerHeartbeat heartbeat,
    ILogger<BackgroundJobHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopeFactory.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<BackgroundJobRunner>();
            var recovered = await runner.RecoverStuckJobsAsync(stoppingToken);
            if (recovered > 0) logger.LogInformation("Recovered {Count} stuck background job(s) on startup.", recovered);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<BackgroundJobRunner>();
                await runner.ProcessOnceAsync(stoppingToken);
                heartbeat.RecordPoll();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
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
