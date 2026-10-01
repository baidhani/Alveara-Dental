using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Backup;

/// <summary>
/// Turns the configured schedule into durable background jobs. Each schedule slot has a stable
/// idempotency key (interval + slot number since the Unix epoch), so enqueuing the same slot twice
/// - a second poll, two server instances, a restart - yields exactly one job, and therefore (via
/// <see cref="BackupService.RunBackupAsync"/>'s own idempotency) exactly one backup.
/// </summary>
public class BackupScheduler(AlveraDbContext db, IBackgroundJobQueue queue)
{
    public const string JobType = "backup.scheduled";

    /// <summary>Enqueues the job for the slot containing <paramref name="nowUtc"/> if scheduling is on and a recovery key exists. Returns the job's key, or null if nothing is due.</summary>
    public async Task<string?> EnqueueDueAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        var settings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        if (settings is not { ScheduleEnabled: true, RecoveryPublicKeyPem: not null }) return null;

        var slot = SlotKey(nowUtc, settings.ScheduleIntervalHours);
        await queue.EnqueueAsync(slot, JobType, null, ct);
        return slot;
    }

    public static string SlotKey(DateTimeOffset nowUtc, int intervalHours)
    {
        var slotNumber = (long)Math.Floor((nowUtc.UtcDateTime - DateTime.UnixEpoch).TotalHours / intervalHours);
        return $"backup:scheduled:{intervalHours}h:{slotNumber}";
    }
}

/// <summary>
/// The background-job handler for a scheduled backup. A backup is an EXTERNAL effect (files on disk),
/// not a row in the runner's transaction, so crash-safety comes from the target's own idempotency
/// (the contract documented on <see cref="IBackgroundJobHandler"/>): the job's idempotency key is the
/// BackupRecord's key, so a retry after a crash is a no-op if that backup already succeeded, and
/// otherwise safely redoes it into a fresh partial file. It runs in its OWN scope/DbContext, so its
/// history rows commit independently of the runner's transaction and survive a rollback of it.
/// A failed backup throws, so the runner's retry/MaxAttempts policy applies.
/// </summary>
public sealed class ScheduledBackupJobHandler(IServiceScopeFactory scopeFactory) : IBackgroundJobHandler
{
    public string JobType => BackupScheduler.JobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext transactionalDb, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupService>();
        await backups.RunBackupAsync(BackupKind.Scheduled, job.IdempotencyKey, actor: null, cancellationToken);
    }
}

/// <summary>Polls once a minute: enqueue the due slot, and clean up any run the server abandoned.</summary>
public sealed class BackupSchedulerHostedService(IServiceScopeFactory scopeFactory, ILogger<BackupSchedulerHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BackupService>().RecoverInterruptedAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<BackupScheduler>().EnqueueDueAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("Backup scheduler poll failed ({ExceptionType}); will retry next interval.", ex.GetType().Name);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }
}
