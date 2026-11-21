using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 (PD-25, D-06) against real SQL Server: backup failures and restore results reach every enabled administrator's inbox exactly once per alert, whatever happens to the file drop and
/// however often delivery runs; when one channel fails the other still delivers and the failed channel is named; acknowledging is audited, happens once and is history the database will not
/// change; and System Status keeps warning until a later success resolves a failure, whatever was acknowledged.
/// </summary>
public class BackupAlertTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private string _drop = "";

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync()
    {
        if (_drop.Length > 0 && Directory.Exists(_drop)) Directory.Delete(_drop, recursive: true);
        return _fixture.DisposeAsync();
    }

    private async Task<(UserAccount Admin, Guid OtherAdmin, Guid DisabledAdmin, Guid Manager)> SeedAsync()
    {
        await using var db = _fixture.CreateContext();
        var accounts = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.BootstrapAdminAsync(accounts);
        var other = (await IdentityTestHelpers.RegisterEnabledAsync(accounts, db, $"ad-{Guid.NewGuid():N}", "password-1234", Role.Admin, admin)).Id;
        var manager = (await IdentityTestHelpers.RegisterEnabledAsync(accounts, db, $"om-{Guid.NewGuid():N}", "password-1234", Role.OfficeManager, admin)).Id;
        var disabled = Guid.NewGuid();
        db.UserAccounts.Add(new UserAccount { Id = disabled, Username = $"ad-off-{Guid.NewGuid():N}", Role = Role.Admin, IsDisabled = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (admin, other, disabled, manager);
    }

    private async Task<BackupNotificationRecord> AlertAsync(string kind, DateTimeOffset? at = null)
    {
        await using var db = _fixture.CreateContext();
        var row = new BackupNotificationRecord { Id = Guid.NewGuid(), Kind = kind, Message = $"{kind} for a test.", CreatedAtUtc = at ?? DateTimeOffset.UtcNow, Delivery = BackupNotificationDelivery.Delivered };
        db.BackupNotifications.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>A notifier wired like the running application: the real file drop (into a fresh folder, or a path that cannot be a folder) and the real in-app alert on this database.</summary>
    private CompositeBackupNotifier Composite(bool fileDropBroken = false, bool inboxBroken = false)
    {
        _drop = Path.Combine(Path.GetTempPath(), $"alv-drop-{Guid.NewGuid():N}");
        var dropFolder = _drop;
        if (fileDropBroken) { Directory.CreateDirectory(_drop); dropFolder = Path.Combine(_drop, "not-a-folder.txt"); File.WriteAllText(dropFolder, "x"); }
        var paths = new BackupPaths("x", "x", "x", dropFolder, "x", "x", "x");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => _fixture.CreateContext());
        if (!inboxBroken) services.AddScoped<InAppBackupAlert>();                                     // a missing registration makes the inbox step fail, as an outage would
        var provider = services.BuildServiceProvider();
        return new CompositeBackupNotifier(new FileDropBackupNotifier(paths), provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CompositeBackupNotifier>.Instance);
    }

    private async Task<List<Notification>> InboxAsync(Guid alertId)
    {
        await using var db = _fixture.CreateContext();
        var key = InAppBackupAlert.SourceKeyFor(alertId);
        return await db.Notifications.AsNoTracking().Where(n => n.SourceKey == key).ToListAsync();
    }

    // ---------- delivery ----------

    [Fact]
    public async Task A_failure_reaches_every_enabled_administrator_once_urgently_and_no_one_else_however_often_it_is_delivered()
    {
        var (admin, other, disabled, manager) = await SeedAsync();
        var alert = await AlertAsync(BackupAlertKinds.BackupFailed);
        var notifier = Composite();
        await notifier.DeliverAsync(new BackupNotification(alert.Kind, alert.Message, null, alert.Id), default);
        await notifier.DeliverAsync(new BackupNotification(alert.Kind, alert.Message, null, alert.Id), default);   // delivered again: nothing doubles

        var inbox = await InboxAsync(alert.Id);
        Assert.Equal(new[] { admin.Id, other }.Order(), inbox.Select(n => n.RecipientUserId).Order());
        Assert.DoesNotContain(inbox, n => n.RecipientUserId == disabled || n.RecipientUserId == manager);
        Assert.All(inbox, n => Assert.Equal((NotificationKinds.BackupAlert, NotificationPriorities.Urgent, (Guid?)null, (Guid?)null), (n.Kind, n.Priority, n.RelatedPatientId, n.MessageId)));
        Assert.Equal(2, Directory.GetFiles(_drop).Length);                                              // the file drop is kept, once per delivery as before
    }

    [Fact]
    public async Task A_restore_drill_success_reaches_administrators_as_normal_and_a_plain_backup_success_stays_a_file_only()
    {
        await SeedAsync();
        var drill = await AlertAsync(BackupAlertKinds.RestoreDrillSucceeded);
        var backup = await AlertAsync("BackupSucceeded");
        var notifier = Composite();
        await notifier.DeliverAsync(new BackupNotification(drill.Kind, drill.Message, null, drill.Id), default);
        await notifier.DeliverAsync(new BackupNotification(backup.Kind, backup.Message, null, backup.Id), default);
        Assert.All(await InboxAsync(drill.Id), n => Assert.Equal(NotificationPriorities.Normal, n.Priority));
        Assert.NotEmpty(await InboxAsync(drill.Id));
        Assert.Empty(await InboxAsync(backup.Id));
    }

    [Fact]
    public async Task When_the_inbox_fails_the_file_drop_still_happens_and_the_failed_channel_is_named()
    {
        await SeedAsync();
        var alert = await AlertAsync(BackupAlertKinds.RestoreDrillFailed);
        var failure = await Assert.ThrowsAsync<BackupDeliveryException>(() => Composite(inboxBroken: true).DeliverAsync(new BackupNotification(alert.Kind, alert.Message, null, alert.Id), default));
        Assert.Equal("inbox_failed", failure.Code);
        Assert.Single(Directory.GetFiles(_drop));
        Assert.Empty(await InboxAsync(alert.Id));
    }

    [Fact]
    public async Task When_the_file_drop_fails_the_inbox_still_delivers_and_the_failed_channel_is_named()
    {
        var (admin, _, _, _) = await SeedAsync();
        var alert = await AlertAsync(BackupAlertKinds.BackupVerificationFailed);
        var failure = await Assert.ThrowsAsync<BackupDeliveryException>(() => Composite(fileDropBroken: true).DeliverAsync(new BackupNotification(alert.Kind, alert.Message, null, alert.Id), default));
        Assert.Equal("file_drop_failed", failure.Code);
        Assert.Contains(await InboxAsync(alert.Id), n => n.RecipientUserId == admin.Id);
    }

    // ---------- acknowledging ----------

    [Fact]
    public async Task Acknowledging_is_audited_once_hides_the_alert_from_the_banner_and_is_history_the_database_will_not_change()
    {
        var (admin, _, _, _) = await SeedAsync();
        var alert = await AlertAsync(BackupAlertKinds.BackupFailed);
        await using (var db = _fixture.CreateContext())
        {
            Assert.Contains(await new BackupAlertService(db).UnacknowledgedFailuresAsync(default), a => a.Id == alert.Id);
            var ack = await new BackupAlertService(db).AcknowledgeAsync(alert.Id, admin.Id, default);
            Assert.True(ack.Acknowledged);
        }
        await using (var db = _fixture.CreateContext())
        {
            await new BackupAlertService(db).AcknowledgeAsync(alert.Id, admin.Id, default);                 // again: nothing more
            Assert.DoesNotContain(await new BackupAlertService(db).UnacknowledgedFailuresAsync(default), a => a.Id == alert.Id);
            Assert.Equal(1, await db.BackupAlertAcknowledgements.CountAsync());
            Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == "BackupAlertAcknowledged"));
        }
        foreach (var sql in new[] { "UPDATE [BackupAlertAcknowledgements] SET [AcknowledgedAtUtc] = SYSDATETIMEOFFSET()", "DELETE FROM [BackupAlertAcknowledgements]" })
        {
            await using var db = _fixture.CreateContext();
            var refused = await Assert.ThrowsAnyAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(51303, refused.Number);
        }
    }

    [Fact]
    public async Task Only_a_failure_is_acknowledged_and_an_unknown_alert_is_not_found()
    {
        var (admin, _, _, _) = await SeedAsync();
        var success = await AlertAsync(BackupAlertKinds.RestoreDrillSucceeded);
        await using var db = _fixture.CreateContext();
        Assert.Equal("alert_not_acknowledgeable", (await Assert.ThrowsAsync<BackupException>(() => new BackupAlertService(db).AcknowledgeAsync(success.Id, admin.Id, default))).Code);
        Assert.Equal("alert_not_found", (await Assert.ThrowsAsync<BackupException>(() => new BackupAlertService(db).AcknowledgeAsync(Guid.NewGuid(), admin.Id, default))).Code);
    }

    // ---------- System Status ----------

    [Fact]
    public async Task A_failure_keeps_System_Status_warning_until_a_later_success_even_when_acknowledged()
    {
        var (admin, _, _, _) = await SeedAsync();
        var at = DateTimeOffset.UtcNow.AddHours(-2);
        await using (var db = _fixture.CreateContext())
        {
            db.BackupSettings.RemoveRange(db.BackupSettings);
            db.BackupSettings.Add(new BackupSettings { RecoveryPublicKeyPem = "-----BEGIN PGP PUBLIC KEY BLOCK-----", ScheduleEnabled = false });
            await db.SaveChangesAsync();
        }
        var alert = await AlertAsync(BackupAlertKinds.BackupFailed, at);
        await using (var db = _fixture.CreateContext()) await new BackupAlertService(db).AcknowledgeAsync(alert.Id, admin.Id, default);

        await using (var db = _fixture.CreateContext())
        {
            var health = await new BackupHealth(db).ReadAsync(DateTimeOffset.UtcNow, default);
            Assert.Equal((1, 0, true), (health.UnresolvedFailures, health.UnacknowledgedAlerts, health.Warning));      // acknowledged, still unresolved
            db.BackupRecords.Add(new BackupRecord { Id = Guid.NewGuid(), Status = BackupStatus.Succeeded, StartedAtUtc = at.AddMinutes(30), CompletedAtUtc = at.AddMinutes(31), FileName = $"b-{Guid.NewGuid():N}.abk", IdempotencyKey = $"k-{Guid.NewGuid():N}" });
            await db.SaveChangesAsync();
        }
        await using (var db = _fixture.CreateContext())
        {
            var health = await new BackupHealth(db).ReadAsync(DateTimeOffset.UtcNow, default);
            Assert.Equal((0, false), (health.UnresolvedFailures, health.Warning));                                       // a later success resolved it
        }
    }

    [Fact]
    public async Task A_schedule_with_no_backup_inside_its_interval_plus_an_hour_is_overdue()
    {
        await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        await using var db = _fixture.CreateContext();
        db.BackupSettings.RemoveRange(db.BackupSettings);
        db.BackupSettings.Add(new BackupSettings { RecoveryPublicKeyPem = "-----BEGIN PGP PUBLIC KEY BLOCK-----", ScheduleEnabled = true, ScheduleIntervalHours = 24 });
        db.BackupRecords.Add(new BackupRecord { Id = Guid.NewGuid(), Status = BackupStatus.Succeeded, StartedAtUtc = now.AddHours(-24.5), CompletedAtUtc = now.AddHours(-24.5), FileName = $"b-{Guid.NewGuid():N}.abk", IdempotencyKey = $"k-{Guid.NewGuid():N}" });
        await db.SaveChangesAsync();
        Assert.False((await new BackupHealth(db).ReadAsync(now, default)).Overdue);                                     // within interval + 1 hour
        Assert.True((await new BackupHealth(db).ReadAsync(now.AddHours(1), default)).Overdue);                          // 25.5 hours: overdue
    }
}
