using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Recall;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C01, the recovery drill for the work queue and recall data against a real SQL Server restore: the practice's recall settings, a patient's own recall arrangement, and a system-made recall task with its history
/// and audit entry are backed up and restored into an isolated target, and the restored database holds exactly what the backup held. A change made after the backup is not in the restore, and the restored database still
/// refuses what the live one refuses (a second task for the same source key, a second settings row, an empty recall arrangement, an edit of a closed task).
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class WorkQueueBackupRestoreTests : SafetyTestBase
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
    public async Task Recall_settings_plans_and_system_made_tasks_survive_a_backup_and_restore_and_a_later_change_is_not_in_it()
    {
        var patient = Ann;
        string key = FollowUpRules.RecallSourceKey(patient, new DateOnly(2027, 3, 1));
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE [RecallSettings] SET [Enabled] = 1, [DefaultIntervalDays] = 90, [LeadDays] = 14, [MaxVisitAgeMonths] = 36, [DueSoonDays] = 5");
            db.PatientRecallPlans.Add(new PatientRecallPlan { PatientId = Bo, IntervalDays = null, OptedOut = true, Reason = "Moved away", UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor });
            SystemTaskWriter.Create(db, FollowUpKinds.Recall, key, patient, "Patient recall: schedule the next visit", DateTimeOffset.UtcNow.AddDays(10), FollowUpPriorities.High, null, DateTimeOffset.UtcNow, "Recall due 2027-03-01 (90 days)");
            await db.SaveChangesAsync();
        }

        await using var before = Fixture.CreateContext();
        var expected = (Tasks: await before.FollowUpTasks.CountAsync(), Events: await before.FollowUpTaskEvents.CountAsync(), Plans: await before.PatientRecallPlans.CountAsync(), Audit: await before.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("FollowUp")));
        Assert.Equal((1, 1, 1), (expected.Tasks, expected.Events, expected.Plans));

        var record = await BackupAsync();

        // life goes on after the backup: recall is switched off, the plan is removed and the task is cancelled by a person. The restore returns the state of the backup, not of today.
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE [RecallSettings] SET [Enabled] = 0, [DefaultIntervalDays] = 365");
            await db.Database.ExecuteSqlAsync($"DELETE FROM [PatientRecallPlans]");
        }
        string version;
        await using (var db = Fixture.CreateContext()) version = Convert.ToBase64String((await db.FollowUpTasks.AsNoTracking().SingleAsync(x => x.SourceKey == key)).RowVersion);
        await WithService(s => s.CancelAsync(S.Actor, SystemTaskWriter.IdFor(key), new CancelFollowUpRequest("Handled by phone", version), default));

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var checks = JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;
        Assert.All(checks.Where(c => c.Blocking), c => Assert.True(c.Passed, c.Name + ": " + c.Detail));

        await using var restored = RestoredDb(drill);
        var settings = await restored.RecallSettings.AsNoTracking().SingleAsync();
        Assert.Equal((true, 90, 14, 36, 5), (settings.Enabled, settings.DefaultIntervalDays, settings.LeadDays, settings.MaxVisitAgeMonths, settings.DueSoonDays));
        var plan = await restored.PatientRecallPlans.AsNoTracking().SingleAsync();
        Assert.Equal((Bo, true, "Moved away"), (plan.PatientId, plan.OptedOut, plan.Reason));
        var task = await restored.FollowUpTasks.AsNoTracking().SingleAsync(t => t.SourceKey == key);
        Assert.Equal((FollowUpStatuses.Open, FollowUpKinds.Recall, FollowUpPriorities.High, (Guid?)null, (Guid?)null), (task.Status, task.Kind, task.Priority, task.AssigneeUserId, task.CreatedByUserId));   // the later cancellation is not in the restore
        Assert.Equal(expected.Events, await restored.FollowUpTaskEvents.CountAsync());
        Assert.Equal(expected.Audit, await restored.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("FollowUp")));

        // the restored database still refuses what the live one refuses
        var duplicate = SystemTaskWriter.Create(restored, FollowUpKinds.Recall, key, patient, "Second", DateTimeOffset.UtcNow.AddDays(10), FollowUpPriorities.Normal, null, DateTimeOffset.UtcNow, "again");
        duplicate.Id = Guid.NewGuid();
        Assert.Contains("unique", (await Assert.ThrowsAnyAsync<Exception>(() => restored.SaveChangesAsync())).ToString(), StringComparison.OrdinalIgnoreCase);                      // one task per source key
        restored.ChangeTracker.Clear();
        Assert.Contains("CK_RecallSettings_OneRow", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"INSERT INTO [RecallSettings] ([Id], [Enabled], [DefaultIntervalDays], [LeadDays], [MaxVisitAgeMonths], [DueSoonDays], [UpdatedAtUtc]) VALUES (2, 0, 180, 30, 24, 7, SYSDATETIMEOFFSET())"))).ToString());
        Assert.Contains("CK_PatientRecallPlans_Means", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"UPDATE [PatientRecallPlans] SET [OptedOut] = 0, [IntervalDays] = NULL"))).ToString());
        await restored.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Status] = N'Cancelled', [ClosedAtUtc] = DATEADD(YEAR, 5, SYSDATETIMEOFFSET()), [ClosedBySystem] = 1, [ClosureNote] = N'x' WHERE [SourceKey] = {key}");
        Assert.Contains("51210", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Title] = N'Rewritten' WHERE [SourceKey] = {key}"))).ToString());   // a closed task never changes

        // and the live database is untouched by the drill
        await using var live = Fixture.CreateContext();
        Assert.False((await live.RecallSettings.AsNoTracking().SingleAsync()).Enabled);
        Assert.Equal(FollowUpStatuses.Cancelled, (await live.FollowUpTasks.AsNoTracking().SingleAsync(t => t.SourceKey == key)).Status);
    }
}
