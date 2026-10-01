using System.Text;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N004 backup creation, history, retention, verification-state, failure-path and notification
/// tests, against a real SQL Server database (BACKUP DATABASE), real files and real encryption.
/// </summary>
public class BackupServiceTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly BackupTestEnvironment _env;
    private readonly Guid _actor = Guid.NewGuid();

    public BackupServiceTests(TestDatabaseFixture fixture)
    {
        _env = new BackupTestEnvironment(fixture);
        _env.ResetBackupStateAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _env.Dispose();

    private async Task<BackupRecord> BackupAsync(string? key = null, BackupKind kind = BackupKind.Manual, IBackupNotifier? notifier = null)
    {
        await using var db = _env.NewDb();
        return await _env.NewBackupService(db, notifier).RunBackupAsync(kind, key ?? $"manual:{Guid.NewGuid():N}", _actor, default);
    }

    // ---------- the happy path ----------

    [Fact]
    public async Task A_manual_backup_creates_one_encrypted_file_covering_every_asset_class_and_records_it_truthfully()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await _env.SeedAccountsAndAuditAsync();
        await _env.SeedBlobsAsync();

        var record = await BackupAsync();

        Assert.Equal(BackupStatus.Succeeded, record.Status);
        var path = BackupService.ResolveBackupPath(record);
        Assert.True(File.Exists(path));
        Assert.Equal(record.SizeBytes, new FileInfo(path).Length);
        Assert.Equal(record.Sha256, await FileTreeAssetSource.HashFileAsync(path, default));
        Assert.Equal(string.Join(',', ManagedAssetClasses.All), string.Join(',', record.IncludedAssetClasses!.Split(',').OrderBy(c => Array.IndexOf(ManagedAssetClasses.All.ToArray(), c))));
        Assert.Equal(BackupVerificationStatus.HashVerified, record.VerificationStatus); // written and re-read; NOT claimed fully verified
        Assert.Equal(_env.Key.Fingerprint, record.RecoveryKeyFingerprint);
        Assert.NotNull(record.SchemaMigration);

        Assert.Empty(Directory.EnumerateFiles(_env.SetsDirectory, "*.partial"));
        Assert.Empty(Directory.Exists(_env.Paths.StagingRoot) ? Directory.EnumerateDirectories(_env.Paths.StagingRoot) : []); // scratch cleaned

        await using var db = _env.NewDb();
        var audit = await db.AuditLogEntries.Where(a => a.TargetUserAccountId == record.Id).ToListAsync();
        Assert.Contains(audit, a => a.EventType == BackupAuditEvents.Created && a.EntityType == "BackupRecord" && a.PerformedByUserAccountId == _actor);
    }

    [Fact]
    public async Task The_backup_file_is_genuinely_encrypted_no_database_header_no_key_material_no_document_bytes()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await _env.SeedAccountsAndAuditAsync();
        var blobs = await _env.SeedBlobsAsync(1, 5000);
        var blobBytes = await File.ReadAllBytesAsync(Path.Combine(_env.BlobRoot, blobs[0].Id.ToString("N")));

        var record = await BackupAsync();
        var file = await File.ReadAllBytesAsync(BackupService.ResolveBackupPath(record));
        var text = Encoding.Latin1.GetString(file);

        Assert.StartsWith("ALVBK", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TAPE", text, StringComparison.Ordinal);                  // SQL Server backup media header
        Assert.DoesNotContain("<key id=", text, StringComparison.Ordinal);              // Data Protection key XML
        Assert.DoesNotContain("PK\u0003\u0004", text, StringComparison.Ordinal);         // ZIP local-file signature
        Assert.DoesNotContain(Encoding.Latin1.GetString(blobBytes[..64]), text, StringComparison.Ordinal); // document bytes
    }

    [Fact]
    public async Task No_secret_needed_to_restore_exists_only_inside_the_backup_or_anywhere_the_server_stores_it()
    {
        var privatePem = (await _env.ConfigureRecoveryKeyAsync()).EncryptedPrivateKeyPem;
        await _env.SeedAccountsAndAuditAsync();
        var record = await BackupAsync();

        // 1. The server stores only the public half.
        await using var db = _env.NewDb();
        var settings = await db.BackupSettings.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("PRIVATE", settings.RecoveryPublicKeyPem!);
        Assert.NotEqual(privatePem, settings.RecoveryPublicKeyPem);

        // 2. Nothing in the audit trail or history carries key material.
        foreach (var entry in await db.AuditLogEntries.AsNoTracking().ToListAsync())
        {
            Assert.DoesNotContain("BEGIN", entry.Details);
            Assert.DoesNotContain("PRIVATE", entry.Details);
        }

        // 3. Decrypt the backup (with the offline key) and scan EVERYTHING in it: the private key is not inside.
        var work = Path.Combine(_env.Root, "scan");
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, "s.zip");
        await using (var input = File.OpenRead(BackupService.ResolveBackupPath(record)))
        await using (var output = File.Create(zip))
            await BackupCrypto.DecryptAsync(input, output, privatePem, BackupTestEnvironment.Passphrase);
        System.IO.Compression.ZipFile.ExtractToDirectory(zip, Path.Combine(work, "x"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(work, "x"), "*", SearchOption.AllDirectories))
        {
            var content = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(file));
            Assert.DoesNotContain("ENCRYPTED PRIVATE KEY", content);
        }
    }

    [Fact]
    public async Task A_backup_cannot_be_created_before_a_recovery_key_exists_and_no_history_row_is_left()
    {
        await using var db = _env.NewDb();
        var ex = await Assert.ThrowsAsync<BackupException>(() => _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, "manual:nokey", _actor, default));
        Assert.Equal("recovery_key_required", ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(0, await db.BackupRecords.CountAsync());
    }

    // ---------- idempotency ----------

    [Fact]
    public async Task The_same_idempotency_key_never_produces_a_second_backup()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var first = await BackupAsync("scheduled:slot-1", BackupKind.Scheduled);
        var second = await BackupAsync("scheduled:slot-1", BackupKind.Scheduled);

        Assert.Equal(first.Id, second.Id);
        await using var db = _env.NewDb();
        Assert.Equal(1, await db.BackupRecords.CountAsync());
        Assert.Single(Directory.EnumerateFiles(_env.SetsDirectory, "*.abk"));
    }

    [Fact]
    public async Task A_failed_attempt_is_retried_in_place_under_the_same_key_and_then_succeeds_once()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await using (var db = _env.NewDb())
        {
            var failing = _env.NewBackupService(db, sources: [new FailingAssetSource(new IOException("boom"))]);
            await Assert.ThrowsAnyAsync<Exception>(() => failing.RunBackupAsync(BackupKind.Scheduled, "scheduled:retry", null, default));
        }

        await using (var db = _env.NewDb())
        {
            var failed = await db.BackupRecords.SingleAsync();
            Assert.Equal(BackupStatus.Failed, failed.Status);
        }

        var retried = await BackupAsync("scheduled:retry", BackupKind.Scheduled);
        Assert.Equal(BackupStatus.Succeeded, retried.Status);
        await using var verify = _env.NewDb();
        Assert.Equal(1, await verify.BackupRecords.CountAsync()); // one row, reused - not a duplicate
        Assert.Single(Directory.EnumerateFiles(_env.SetsDirectory, "*.abk"));
    }

    // ---------- failure paths ----------

    [Fact]
    public async Task An_unavailable_destination_fails_visibly_with_a_safe_code_leaves_no_partial_file_and_notifies()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var good = await BackupAsync(); // an earlier success must remain the "last success"

        // The destination becomes unusable: a regular FILE now sits where the folder is expected.
        var blocked = Path.Combine(_env.Root, "blocked-destination");
        await File.WriteAllTextAsync(blocked, "not a folder");
        await using (var db = _env.NewDb())
        {
            var settings = await db.BackupSettings.SingleAsync();
            await _env.NewBackupService(db).SaveSettingsAsync(false, 24, 7, null, 2, 30, Convert.ToBase64String(settings.RowVersion), _actor, default); // valid default for now
            await db.Database.ExecuteSqlRawAsync("UPDATE BackupSettings SET DestinationDirectory = {0}", blocked);
        }

        await using (var db = _env.NewDb())
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, "manual:blocked", _actor, default));
            Assert.Equal("destination_unavailable", BackupFailure.Classify(ex).Code);
        }

        await using var verify = _env.NewDb();
        var failed = await verify.BackupRecords.SingleAsync(r => r.IdempotencyKey == "manual:blocked");
        Assert.Equal(BackupStatus.Failed, failed.Status);
        Assert.Equal("destination_unavailable", failed.FailureCode);
        Assert.DoesNotContain(blocked, failed.FailureMessage!); // fixed message: no paths, no exception text
        Assert.Empty(Directory.EnumerateFiles(_env.SetsDirectory, "*.partial"));
        Assert.Contains(_env.Notifier.Delivered, n => n.Kind == "BackupFailed" && n.BackupRecordId == failed.Id);

        var status = await _env.NewBackupService(verify).GetStatusAsync(default);
        Assert.True(status.LatestAttemptFailed);                       // a prominent failure state, not a stale success
        Assert.Equal(good.Id, status.LastSuccess!.Id);                // the older success is still reported as such
        Assert.Equal(failed.Id, status.LastFailure!.Id);
    }

    [Theory]
    [InlineData(0x70, "destination_full")]    // ERROR_DISK_FULL
    [InlineData(0x27, "destination_full")]    // ERROR_HANDLE_DISK_FULL
    [InlineData(0x05, "io_error")]
    public void Low_level_failures_map_to_stable_codes_and_fixed_messages(int win32, string expectedCode)
    {
        var (code, message) = BackupFailure.Classify(new IOException("C:\\secret\\path leaked in text", unchecked((int)(0x80070000 | (uint)win32))));
        Assert.Equal(expectedCode, code);
        Assert.DoesNotContain("secret", message);
        Assert.Equal("interrupted", BackupFailure.Classify(new OperationCanceledException()).Code);
        Assert.Equal("unexpected_error", BackupFailure.Classify(new InvalidOperationException("server=prod;password=x")).Code);
    }

    [Fact]
    public async Task A_disk_full_failure_mid_run_leaves_no_partial_or_final_file_and_no_scratch()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var diskFull = new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
        await using (var db = _env.NewDb())
        {
            await Assert.ThrowsAsync<IOException>(() =>
                _env.NewBackupService(db, sources: [.._env.Sources, new FailingAssetSource(diskFull)]).RunBackupAsync(BackupKind.Manual, "manual:full", _actor, default));
        }

        await using var verify = _env.NewDb();
        Assert.Equal("destination_full", (await verify.BackupRecords.SingleAsync()).FailureCode);
        Assert.Empty(Directory.EnumerateFiles(_env.SetsDirectory));
        Assert.Empty(Directory.EnumerateDirectories(_env.Paths.StagingRoot));
    }

    [Fact]
    public async Task A_run_interrupted_by_a_process_death_is_recovered_as_failed_and_can_then_be_retried_once()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var stalled = Guid.NewGuid();
        await using (var db = _env.NewDb())
        {
            db.BackupRecords.Add(new BackupRecord { Id = stalled, IdempotencyKey = "scheduled:crash", Kind = BackupKind.Scheduled, Status = BackupStatus.Running, StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) });
            await db.SaveChangesAsync();
            Directory.CreateDirectory(Path.Combine(_env.Paths.StagingRoot, stalled.ToString("N"), "content")); // scratch the dead process left behind
            File.WriteAllText(Path.Combine(_env.SetsDirectory, "left-behind.abk.partial"), "partial");

            Assert.Equal(1, await _env.NewBackupService(db).RecoverInterruptedAsync(default));
            var recovered = await db.BackupRecords.AsNoTracking().SingleAsync();
            Assert.Equal((BackupStatus.Failed, "interrupted"), (recovered.Status, recovered.FailureCode));
            Assert.False(Directory.Exists(Path.Combine(_env.Paths.StagingRoot, stalled.ToString("N"))));
        }

        var retried = await BackupAsync("scheduled:crash", BackupKind.Scheduled);
        Assert.Equal((stalled, BackupStatus.Succeeded), (retried.Id, retried.Status)); // same row, now succeeded
        await using var verify = _env.NewDb();
        Assert.Equal(1, await verify.BackupRecords.CountAsync());
    }

    [Fact]
    public async Task A_notification_delivery_failure_does_not_mark_a_successful_backup_failed_and_is_recorded_on_its_own()
    {
        await _env.ConfigureRecoveryKeyAsync();
        _env.Notifier.Fail = true;

        var record = await BackupAsync();

        Assert.Equal(BackupStatus.Succeeded, record.Status);
        await using var db = _env.NewDb();
        var note = await db.BackupNotifications.SingleAsync();
        Assert.Equal((BackupNotificationDelivery.Failed, "BackupSucceeded"), (note.Delivery, note.Kind));
        Assert.Equal("delivery_failed", note.DeliveryFailureCode);
    }

    [Fact]
    public async Task Notifications_are_testable_and_the_default_local_channel_writes_a_drop_file()
    {
        await using var db = _env.NewDb();
        var service = _env.NewBackupService(db, new FileDropBackupNotifier(_env.Paths));
        var sent = await service.SendTestNotificationAsync(_actor, default);

        Assert.Equal(BackupNotificationDelivery.Delivered, sent.Delivery);
        var file = Assert.Single(Directory.EnumerateFiles(_env.Paths.NotificationDirectory));
        Assert.Contains("test of the backup notification channel", await File.ReadAllTextAsync(file));

        _env.Notifier.Fail = true;
        var failed = await _env.NewBackupService(db).SendTestNotificationAsync(_actor, default);
        Assert.Equal(BackupNotificationDelivery.Failed, failed.Delivery); // a broken channel is visible, not silent
    }

    // ---------- retention ----------

    [Fact]
    public async Task Retention_keeps_the_newest_backups_purges_older_files_keeps_their_history_and_protects_the_newest_verified_one()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await using (var db = _env.NewDb())
        {
            var s = await db.BackupSettings.SingleAsync();
            await _env.NewBackupService(db).SaveSettingsAsync(false, 24, 2, null, 2, 30, Convert.ToBase64String(s.RowVersion), _actor, default);
        }

        var ids = new List<Guid>();
        await using var verify = _env.NewDb();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await BackupAsync($"manual:r{i}")).Id);
            if (i == 0)
            {
                // The OLDEST becomes the newest fully verified one: it must survive retention even once it is outside the window.
                await verify.BackupRecords.Where(r => r.Id == ids[0]).ExecuteUpdateAsync(s => s.SetProperty(r => r.VerificationStatus, BackupVerificationStatus.FullyVerified));
            }
            await Task.Delay(15); // distinct StartedAtUtc ordering
        }
        await _env.NewBackupService(verify).ApplyRetentionAsync(default);

        var rows = await verify.BackupRecords.AsNoTracking().OrderBy(r => r.StartedAtUtc).ToListAsync();
        Assert.Equal(5, rows.Count); // history is never deleted
        Assert.Equal(BackupStatus.Succeeded, rows[0].Status);               // protected: newest fully verified
        Assert.Equal(new[] { BackupStatus.Purged, BackupStatus.Purged }, new[] { rows[1].Status, rows[2].Status });
        Assert.Equal(new[] { BackupStatus.Succeeded, BackupStatus.Succeeded }, new[] { rows[3].Status, rows[4].Status });
        Assert.False(File.Exists(BackupService.ResolveBackupPath(rows[1])));
        Assert.True(File.Exists(BackupService.ResolveBackupPath(rows[0])));
        Assert.True(File.Exists(BackupService.ResolveBackupPath(rows[4])));
        Assert.Contains(await verify.AuditLogEntries.Where(a => a.EventType == BackupAuditEvents.Purged).ToListAsync(), a => a.TargetUserAccountId == rows[1].Id);
    }

    // ---------- hash verification ----------

    [Fact]
    public async Task Hash_verification_confirms_an_intact_file_and_distinguishes_a_changed_or_missing_one()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var record = await BackupAsync();
        await using var db = _env.NewDb();
        var service = _env.NewBackupService(db);

        Assert.Equal(BackupVerificationStatus.HashVerified, (await service.VerifyHashAsync(record.Id, _actor, default)).VerificationStatus);

        var path = BackupService.ResolveBackupPath(record);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);
        var corrupt = await service.VerifyHashAsync(record.Id, _actor, default);
        Assert.Equal((BackupVerificationStatus.VerificationFailed, "hash_mismatch"), (corrupt.VerificationStatus, corrupt.VerificationFailureCode));
        Assert.Contains(_env.Notifier.Delivered, n => n.Kind == "BackupVerificationFailed");

        File.Delete(path);
        var missing = await service.VerifyHashAsync(record.Id, _actor, default);
        Assert.Equal("backup_file_missing", missing.VerificationFailureCode);
    }

    // ---------- settings + recovery key ----------

    [Fact]
    public async Task Settings_are_validated_and_scheduling_requires_a_recovery_key()
    {
        await using var db = _env.NewDb();
        var service = _env.NewBackupService(db);

        async Task<string> Code(Func<Task> action) => (await Assert.ThrowsAsync<BackupException>(action)).Code;
        Assert.Equal("invalid_interval", await Code(() => service.SaveSettingsAsync(false, 0, 7, null, 2, 30, null, _actor, default)));
        Assert.Equal("invalid_interval", await Code(() => service.SaveSettingsAsync(false, 169, 7, null, 2, 30, null, _actor, default)));
        Assert.Equal("invalid_retention", await Code(() => service.SaveSettingsAsync(false, 24, 0, null, 2, 30, null, _actor, default)));
        Assert.Equal("invalid_probation", await Code(() => service.SaveSettingsAsync(false, 24, 7, null, 11, 30, null, _actor, default)));
        Assert.Equal("invalid_cadence", await Code(() => service.SaveSettingsAsync(false, 24, 7, null, 2, 0, null, _actor, default)));
        Assert.Equal("invalid_destination", await Code(() => service.SaveSettingsAsync(false, 24, 7, "relative\\path", 2, 30, null, _actor, default)));
        Assert.Equal("invalid_destination", await Code(() => service.SaveSettingsAsync(false, 24, 7, Path.Combine(_env.Paths.StagingRoot, "inside"), 2, 30, null, _actor, default)));
        Assert.Equal("recovery_key_required", await Code(() => service.SaveSettingsAsync(true, 24, 7, null, 2, 30, null, _actor, default)));
    }

    [Fact]
    public async Task Settings_changes_are_audited_versioned_and_a_stale_edit_is_a_conflict()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await using var db = _env.NewDb();
        var service = _env.NewBackupService(db);
        var view = await service.GetSettingsAsync(default);

        var custom = Path.Combine(_env.Root, "custom-destination");
        var saved = await service.SaveSettingsAsync(true, 12, 5, custom, 3, 14, view.RowVersion, _actor, default);
        Assert.Equal((true, 12, 5, custom, false), (saved.ScheduleEnabled, saved.ScheduleIntervalHours, saved.RetentionCount, saved.DestinationDirectory, saved.DestinationIsDefault));
        Assert.True(Directory.Exists(custom));
        Assert.Contains(await db.AuditLogEntries.AsNoTracking().ToListAsync(), a => a.EventType == BackupAuditEvents.SettingsChanged && a.EntityType == "BackupSettings");

        await using var other = _env.NewDb();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            _env.NewBackupService(other).SaveSettingsAsync(true, 6, 5, null, 3, 14, view.RowVersion, _actor, default)); // stale version
        await Assert.ThrowsAsync<ConfigurationException>(() =>
            _env.NewBackupService(other).SaveSettingsAsync(true, 6, 5, null, 3, 14, null, _actor, default)); // version required
    }

    [Fact]
    public async Task Configuring_the_recovery_key_returns_the_private_half_once_stores_only_the_public_half_and_replacement_is_explicit()
    {
        await using var db = _env.NewDb();
        var service = _env.NewBackupService(db);
        var auditedBefore = await db.AuditLogEntries.CountAsync(a => a.EventType == BackupAuditEvents.RecoveryKeyConfigured);

        var privatePem = await service.ConfigureRecoveryKeyAsync(BackupTestEnvironment.Passphrase, false, _actor, default);
        var settings = await db.BackupSettings.AsNoTracking().SingleAsync();
        Assert.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----", privatePem);
        Assert.Equal(BackupCrypto.FingerprintOfPublicKeyPem(settings.RecoveryPublicKeyPem!), settings.RecoveryKeyFingerprint);

        var ex = await Assert.ThrowsAsync<BackupException>(() => service.ConfigureRecoveryKeyAsync(BackupTestEnvironment.Passphrase, false, _actor, default));
        Assert.Equal(("recovery_key_exists", 409), (ex.Code, ex.StatusCode));

        var weak = await Assert.ThrowsAsync<BackupException>(() => service.ConfigureRecoveryKeyAsync("short", true, _actor, default));
        Assert.Equal("weak_passphrase", weak.Code);

        await service.ConfigureRecoveryKeyAsync(BackupTestEnvironment.Passphrase, true, _actor, default);
        Assert.NotEqual(settings.RecoveryKeyFingerprint, (await db.BackupSettings.AsNoTracking().SingleAsync()).RecoveryKeyFingerprint);
        var audited = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == BackupAuditEvents.RecoveryKeyConfigured).ToListAsync();
        Assert.Equal(auditedBefore + 2, audited.Count);
        Assert.All(audited, a => Assert.DoesNotContain("PRIVATE", a.Details));
    }

    // ---------- status: probation, verification cadence, asset coverage ----------

    [Fact]
    public async Task Status_reports_probation_trust_verification_age_and_whether_the_last_backup_covers_every_asset_class()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await using var db = _env.NewDb();
        var service = _env.NewBackupService(db);

        var empty = await service.GetStatusAsync(default);
        Assert.Null(empty.LastSuccess);
        Assert.False(empty.LastSuccessCoversAllAssetClasses);
        Assert.False(empty.Trusted);                                       // required verifications default to 2, none done
        Assert.Equal(ManagedAssetClasses.All, empty.MissingAssetClasses);

        var record = await BackupAsync();
        var afterBackup = await service.GetStatusAsync(default);
        Assert.True(afterBackup.LastSuccessCoversAllAssetClasses);
        Assert.True(afterBackup.VerificationOverdue);                      // never fully verified: overdue even though a hash check passed
        Assert.Equal(0, afterBackup.SuccessfulVerificationCount);

        await db.BackupRecords.Where(r => r.Id == record.Id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.VerificationStatus, BackupVerificationStatus.FullyVerified).SetProperty(r => r.VerifiedAtUtc, DateTimeOffset.UtcNow));
        var verified = await service.GetStatusAsync(default);
        Assert.Equal(1, verified.SuccessfulVerificationCount);
        Assert.False(verified.VerificationOverdue);
        Assert.False(verified.Trusted);                                     // 1 of 2: still in probation

        await using var settingsDb = _env.NewDb();
        var s = await settingsDb.BackupSettings.SingleAsync();
        await _env.NewBackupService(settingsDb).SaveSettingsAsync(false, 24, 7, null, 1, 30, Convert.ToBase64String(s.RowVersion), _actor, default);
        Assert.True((await service.GetStatusAsync(default)).Trusted);       // requirement lowered to 1: probation over

        // A backup written before a new asset class existed is visibly incomplete.
        await db.BackupRecords.Where(r => r.Id == record.Id).ExecuteUpdateAsync(s2 => s2.SetProperty(r => r.IncludedAssetClasses, "database,documents"));
        var incomplete = await service.GetStatusAsync(default);
        Assert.False(incomplete.LastSuccessCoversAllAssetClasses);
        Assert.Equal(new[] { ManagedAssetClasses.DataProtectionKeys }, incomplete.MissingAssetClasses);
    }
}
