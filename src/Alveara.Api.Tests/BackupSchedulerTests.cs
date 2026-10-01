using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N004 scheduled-backup tests through the REAL durable background-job mechanism: slot
/// idempotency, restart recovery, retry after failure, and exactly-one-backup guarantees.
/// </summary>
public class BackupSchedulerTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly BackupTestEnvironment _env;
    private readonly Guid _actor = Guid.NewGuid();

    public BackupSchedulerTests(TestDatabaseFixture fixture)
    {
        _env = new BackupTestEnvironment(fixture);
        _env.ResetBackupStateAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _env.Dispose();

    private async Task EnableScheduleAsync(int intervalHours = 24)
    {
        await _env.ConfigureRecoveryKeyAsync();
        await using var db = _env.NewDb();
        var s = await db.BackupSettings.SingleAsync();
        await _env.NewBackupService(db).SaveSettingsAsync(true, intervalHours, 7, null, 2, 30, Convert.ToBase64String(s.RowVersion), _actor, default);
    }

    private ServiceProvider BuildProvider(IEnumerable<IBackupAssetSource>? sources = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AlveraDbContext>(o => o.UseSqlServer(_env.Fixture.ConnectionString));
        services.AddSingleton(_env.Paths);
        services.AddSingleton<IBackupSnapshotProvider>(new SqlServerBackupSnapshotProvider(_env.Fixture.ConnectionString, _env.Paths.DatabaseName));
        services.AddSingleton<IBackupNotifier>(_env.Notifier);
        foreach (var source in sources ?? _env.Sources) services.AddSingleton(source);
        services.AddScoped<BackupService>();
        return services.BuildServiceProvider();
    }

    /// <summary>Processes pending jobs exactly as the hosted service does, with the real handler resolving its own scope.</summary>
    private async Task<int> ProcessJobsAsync(ServiceProvider provider, bool recoverFirst = false)
    {
        await using var db = _env.NewDb();
        var runner = new BackgroundJobRunner(db, [new ScheduledBackupJobHandler(provider.GetRequiredService<IServiceScopeFactory>())]);
        if (recoverFirst) await runner.RecoverStuckJobsAsync();
        return await runner.ProcessOnceAsync();
    }

    private async Task<int> EnqueueAsync(DateTimeOffset now)
    {
        await using var db = _env.NewDb();
        var key = await new BackupScheduler(db, new BackgroundJobQueue(db)).EnqueueDueAsync(now, default);
        return key is null ? 0 : 1;
    }

    // ---------- scheduling ----------

    [Fact]
    public async Task Nothing_is_scheduled_unless_scheduling_is_enabled_and_a_recovery_key_exists()
    {
        Assert.Equal(0, await EnqueueAsync(DateTimeOffset.UtcNow));            // no settings at all
        await _env.ConfigureRecoveryKeyAsync();
        Assert.Equal(0, await EnqueueAsync(DateTimeOffset.UtcNow));            // key but schedule off
        await using var db = _env.NewDb();
        Assert.Equal(0, await db.BackgroundJobs.CountAsync());
    }

    [Fact]
    public async Task The_same_schedule_slot_enqueues_one_job_however_often_the_scheduler_polls_and_the_next_slot_enqueues_another()
    {
        await EnableScheduleAsync(24);
        var now = new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.Zero);

        await EnqueueAsync(now);
        await EnqueueAsync(now.AddMinutes(1));
        await EnqueueAsync(now.AddHours(10)); // still the same 24h slot
        await using (var db = _env.NewDb())
            Assert.Equal(1, await db.BackgroundJobs.CountAsync());

        await EnqueueAsync(now.AddHours(24));
        await using var verify = _env.NewDb();
        var keys = await verify.BackgroundJobs.Select(j => j.IdempotencyKey).ToListAsync();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.StartsWith("backup:scheduled:24h:", k));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(168)]
    public void Slot_keys_are_stable_within_an_interval_and_change_across_it(int hours)
    {
        var t = new DateTimeOffset(2026, 10, 5, 3, 17, 0, TimeSpan.Zero);
        Assert.Equal(BackupScheduler.SlotKey(t, hours), BackupScheduler.SlotKey(t.AddSeconds(1), hours)); // same slot a moment later
        Assert.NotEqual(BackupScheduler.SlotKey(t, hours), BackupScheduler.SlotKey(t.AddHours(hours), hours));
    }

    // ---------- running through the job mechanism ----------

    [Fact]
    public async Task A_scheduled_job_produces_exactly_one_encrypted_backup_and_re_running_the_same_slot_adds_none()
    {
        await EnableScheduleAsync();
        await using var provider = BuildProvider();
        var now = DateTimeOffset.UtcNow;

        await EnqueueAsync(now);
        Assert.Equal(1, await ProcessJobsAsync(provider));
        await EnqueueAsync(now);                       // second poll, same slot
        Assert.Equal(0, await ProcessJobsAsync(provider));

        await using var db = _env.NewDb();
        var record = await db.BackupRecords.SingleAsync();
        Assert.Equal((BackupKind.Scheduled, BackupStatus.Succeeded, null), (record.Kind, record.Status, record.InitiatedByUserAccountId));
        Assert.Single(Directory.EnumerateFiles(_env.SetsDirectory, "*.abk"));
        Assert.Equal(BackgroundJobStatus.Succeeded, (await db.BackgroundJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_job_that_was_in_flight_when_the_server_restarted_is_recovered_and_completes_without_a_duplicate_backup()
    {
        await EnableScheduleAsync();
        await using var provider = BuildProvider();
        await EnqueueAsync(DateTimeOffset.UtcNow);
        await ProcessJobsAsync(provider); // the backup completes...

        // ...but the process "died" before the runner recorded it: the job is stuck InProgress with its effect already done.
        await using (var db = _env.NewDb())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM BackgroundJobEffectReceipts");
            await db.BackgroundJobs.ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BackgroundJobStatus.InProgress)
                .SetProperty(j => j.StartedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-30)));
        }

        Assert.Equal(1, await ProcessJobsAsync(provider, recoverFirst: true)); // recovered to Pending, re-run, completes

        await using var verify = _env.NewDb();
        Assert.Equal(1, await verify.BackupRecords.CountAsync());                         // the target honored its idempotency key
        Assert.Single(Directory.EnumerateFiles(_env.SetsDirectory, "*.abk"));
        Assert.Equal(BackgroundJobStatus.Succeeded, (await verify.BackgroundJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_failing_scheduled_backup_is_retried_by_the_job_runner_and_then_succeeds_into_the_same_history_row()
    {
        await EnableScheduleAsync();
        await EnqueueAsync(DateTimeOffset.UtcNow);

        await using (var broken = BuildProvider([.._env.Sources, new FailingAssetSource(new IOException("disk went away"))]))
            Assert.Equal(1, await ProcessJobsAsync(broken)); // attempt 1 fails

        await using (var db = _env.NewDb())
        {
            var job = await db.BackgroundJobs.SingleAsync();
            Assert.Equal(BackgroundJobStatus.Pending, job.Status);  // not exhausted: waiting for retry
            Assert.Equal(1, job.AttemptCount);
            Assert.DoesNotContain("disk went away", job.LastError); // PHI/secret-safe summary only
            Assert.Equal(BackupStatus.Failed, (await db.BackupRecords.SingleAsync()).Status); // visible, not silent
        }

        await using (var healthy = BuildProvider())
            Assert.Equal(1, await ProcessJobsAsync(healthy));       // attempt 2 succeeds

        await using var verify = _env.NewDb();
        var record = await verify.BackupRecords.SingleAsync();
        Assert.Equal(BackupStatus.Succeeded, record.Status);
        Assert.Null(record.FailureCode);
        Assert.Single(Directory.EnumerateFiles(_env.SetsDirectory, "*.abk"));
        Assert.Equal(2, (await verify.BackgroundJobs.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task A_scheduled_backup_that_keeps_failing_ends_as_a_failed_job_with_a_visible_failed_backup_and_notifications()
    {
        await EnableScheduleAsync();
        await EnqueueAsync(DateTimeOffset.UtcNow);
        await using var broken = BuildProvider([.._env.Sources, new FailingAssetSource(new IOException("persistent"))]);

        for (var i = 0; i < 4; i++) await ProcessJobsAsync(broken);

        await using var db = _env.NewDb();
        var job = await db.BackgroundJobs.SingleAsync();
        Assert.Equal((BackgroundJobStatus.Failed, 3), (job.Status, job.AttemptCount)); // capped retries, no infinite loop
        Assert.Equal(BackupStatus.Failed, (await db.BackupRecords.SingleAsync()).Status);
        Assert.True(_env.Notifier.Delivered.Count(n => n.Kind == "BackupFailed") >= 3);
        Assert.True((await _env.NewBackupService(db).GetStatusAsync(default)).LatestAttemptFailed);
    }
}
