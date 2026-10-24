using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-009, the recovery drill for follow-up data against a real SQL Server restore: tasks (open, completed, and the recall made by a completion), their append-only history, the reminder deliveries, the reminder
/// notification and the database guards (the immutable-event and delivery triggers, the final-task trigger and the one-recall-per-task index) are backed up and restored into an isolated target, and the restored
/// database holds exactly what the backup held. A change made after the backup is not in the restore, and the restored database still refuses what the live one refuses.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class FollowUpBackupRestoreTests : SafetyTestBase
{
    private BackupTestEnvironment _env = null!;
    private readonly List<string> _restored = [];

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _env = new BackupTestEnvironment(Fixture);
        await _env.ResetBackupStateAsync();
        await _env.ConfigureRecoveryKeyAsync();
    }

    public override async Task DisposeAsync()
    {
        foreach (var name in _restored) await _env.RestoreProvider.DropDatabaseAsync(name, default);
        _env.Dispose();
        await base.DisposeAsync();
    }

    private async Task<BackupRecord> BackupAsync()
    {
        await using var db = _env.NewDb();
        return await _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", S.Actor, default);
    }

    private async Task<RestoreDrillRecord> DrillAsync(Guid recordId)
    {
        await using var db = _env.NewDb();
        var drill = await _env.NewRestoreService(db).RestoreDrillAsync(recordId, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, S.Actor, default);
        if (drill.TargetDatabase is not null) _restored.Add(drill.TargetDatabase);
        return drill;
    }

    private AlveraDbContext RestoredDb(RestoreDrillRecord drill) =>
        new(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);

    private async Task<T> WithService<T>(Func<FollowUpService, Task<T>> action)
    {
        await using var db = Fixture.CreateContext();
        return await action(new FollowUpService(db, new TestClock(DateTimeOffset.UtcNow), PolicyTestSupport.Defaults()));
    }

    [Fact]
    public async Task Follow_up_tasks_history_deliveries_and_guards_survive_a_backup_and_restore_and_a_later_change_is_not_in_it()
    {
        var hygienist = Guid.NewGuid();
        await using (var db = Fixture.CreateContext())
        {
            db.UserAccounts.Add(new UserAccount { Id = hygienist, Username = $"hyg-{Guid.NewGuid():N}", Role = Role.Hygienist, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        // an open task, and a completed one with a recall interval (so the recall exists), made through the service
        var open = (await WithService(s => s.CreateAsync(S.Actor, Ann, new CreateFollowUpRequest("Call about the crown", "Private note", DateTimeOffset.UtcNow.AddDays(3), hygienist, null), "k-open", default))).Task;
        var repeating = (await WithService(s => s.CreateAsync(S.Actor, Ann, new CreateFollowUpRequest("Six-month recall", null, DateTimeOffset.UtcNow.AddMinutes(-10), hygienist, 180), "k-repeat", default))).Task;
        var completed = await WithService(s => s.CompleteAsync(S.Actor, repeating.Id, new CompleteFollowUpRequest("Done", repeating.RowVersion), default));
        Assert.NotNull(completed.RecallTaskId);

        // a reminder for a due task: its delivery, its event and its notification
        await using (var db = Fixture.CreateContext())
        {
            var clock = new TestClock(DateTimeOffset.UtcNow);
            var due = new FollowUpTask { Id = Guid.NewGuid(), PatientId = Bo, AssigneeUserId = hygienist, Title = "Due now", DueAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), Status = FollowUpStatuses.Open, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1) };
            db.FollowUpTasks.Add(due);
            await db.SaveChangesAsync();
            await new FollowUpReminderService(db, clock, PolicyTestSupport.Defaults(), [new InAppReminderChannel(db, clock)]).ProcessOnceAsync(default);
        }

        await using var before = Fixture.CreateContext();
        var expected = (Tasks: await before.FollowUpTasks.CountAsync(), Events: await before.FollowUpTaskEvents.CountAsync(), Deliveries: await before.FollowUpReminderDeliveries.CountAsync(),
                        Reminders: await before.Notifications.CountAsync(n => n.Kind == NotificationKinds.FollowUpDue), Audit: await before.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("FollowUp")));
        Assert.Equal((4, 6, 1, 1), (expected.Tasks, expected.Events, expected.Deliveries, expected.Reminders));

        var record = await BackupAsync();

        // life goes on after the backup: the open task is cancelled. The restore returns the state of the backup, not of today.
        await WithService(s => s.CancelAsync(S.Actor, open.Id, new CancelFollowUpRequest("Patient moved away", open.RowVersion), default));

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var checks = JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;
        Assert.All(checks.Where(c => c.Blocking), c => Assert.True(c.Passed, c.Name + ": " + c.Detail));

        await using var restored = RestoredDb(drill);
        Assert.Equal(expected.Tasks, await restored.FollowUpTasks.CountAsync());
        Assert.Equal(FollowUpStatuses.Open, (await restored.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == open.Id)).Status);                 // the later cancellation is not in the restore
        Assert.Equal(expected.Events, await restored.FollowUpTaskEvents.CountAsync());                                                                 // and neither is its event
        Assert.Equal(expected.Deliveries, await restored.FollowUpReminderDeliveries.CountAsync());
        Assert.Equal(expected.Reminders, await restored.Notifications.CountAsync(n => n.Kind == NotificationKinds.FollowUpDue));
        Assert.Equal(expected.Audit, await restored.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("FollowUp")));
        var recall = await restored.FollowUpTasks.AsNoTracking().SingleAsync(t => t.RecallOfTaskId == repeating.Id);
        Assert.Equal((completed.RecallTaskId, 180, FollowUpStatuses.Open), ((Guid?)recall.Id, recall.RecallIntervalDays ?? 0, recall.Status));
        var done = await restored.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == repeating.Id);
        Assert.Equal((FollowUpStatuses.Completed, S.Actor, "Done"), (done.Status, done.ClosedByUserId, done.ClosureNote));

        // the restored database still holds the guards and refuses what the live one refuses
        var triggers = await restored.Database.SqlQuery<string>($"SELECT name AS [Value] FROM sys.triggers WHERE name IN ('TR_FollowUpTaskEvents_Immutable','TR_FollowUpReminderDeliveries_Immutable','TR_FollowUpTasks_Guard','TR_FollowUpTasks_Links')").ToListAsync();
        Assert.Equal(4, triggers.Count);
        Assert.Contains("51209", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"DELETE FROM [FollowUpTaskEvents]"))).ToString());
        Assert.Contains("51212", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"UPDATE [FollowUpReminderDeliveries] SET [Outcome] = N'Fallback'"))).ToString());
        Assert.Contains("51210", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Title] = N'Rewritten' WHERE [Id] = {repeating.Id}"))).ToString());
        var second = new FollowUpTask { Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = hygienist, Title = "A second recall", DueAtUtc = DateTimeOffset.UtcNow.AddDays(9), Status = FollowUpStatuses.Open, RecallOfTaskId = repeating.Id, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow };
        restored.FollowUpTasks.Add(second);
        Assert.Contains("unique", (await Assert.ThrowsAnyAsync<Exception>(() => restored.SaveChangesAsync())).ToString(), StringComparison.OrdinalIgnoreCase);   // one recall per task

        // and the live database is untouched by the drill
        await using var live = Fixture.CreateContext();
        Assert.Equal(FollowUpStatuses.Cancelled, (await live.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == open.Id)).Status);
    }
}
