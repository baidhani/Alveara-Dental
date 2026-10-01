using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Backup;

public sealed record BackupSettingsView(
    bool ScheduleEnabled, int ScheduleIntervalHours, int RetentionCount, string DestinationDirectory, bool DestinationIsDefault,
    bool RecoveryKeyConfigured, string? RecoveryKeyFingerprint, DateTimeOffset? RecoveryKeyConfiguredAtUtc,
    int RequiredSuccessfulVerifications, int VerificationCadenceDays, string RowVersion);

public sealed record BackupStatusView(
    BackupSettingsView Settings,
    BackupRecord? LastSuccess,
    BackupRecord? LastFailure,
    /// <summary>True when the most recent attempt failed (so the UI shows a prominent failure state, not a stale success).</summary>
    bool LatestAttemptFailed,
    int SuccessfulVerificationCount,
    bool Trusted,
    DateTimeOffset? LastFullVerificationAtUtc,
    bool VerificationOverdue,
    IReadOnlyList<string> RequiredAssetClasses,
    bool LastSuccessCoversAllAssetClasses,
    IReadOnlyList<string> MissingAssetClasses);

/// <summary>
/// ALV-N004: creates, records, retains and hash-verifies encrypted full-state backups. A backup set
/// is one file: a ZIP (SQL Server BACKUP DATABASE output + every registered asset source + a
/// manifest with per-file SHA-256) encrypted as a standard OpenPGP message (AES-256 with integrity protection) to the
/// recovery PUBLIC key (see <see cref="BackupCrypto"/>). The server can therefore create backups
/// unattended but cannot read them back - restoring needs the recovery key held by the administrator.
/// </summary>
public class BackupService(
    AlveraDbContext db,
    BackupPaths paths,
    IBackupSnapshotProvider snapshotProvider,
    IEnumerable<IBackupAssetSource> assetSources,
    IBackupNotifier notifier,
    IDeploymentSettingsProvider deploymentSettings)
{
    private static readonly TimeSpan StaleRunningThreshold = TimeSpan.FromMinutes(30);
    public static readonly string AppVersion = typeof(BackupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    // ---------- Settings + recovery key ----------

    private async Task<BackupSettings> LoadOrCreateSettingsAsync(CancellationToken ct)
    {
        var settings = await db.BackupSettings.SingleOrDefaultAsync(ct);
        if (settings is not null) return settings;
        settings = new BackupSettings { UpdatedAtUtc = DateTimeOffset.UtcNow };
        db.BackupSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    public string EffectiveDestination(BackupSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DestinationDirectory) ? paths.DefaultBackupDirectory : settings.DestinationDirectory;

    private BackupSettingsView ToView(BackupSettings s) => new(
        s.ScheduleEnabled, s.ScheduleIntervalHours, s.RetentionCount, EffectiveDestination(s), string.IsNullOrWhiteSpace(s.DestinationDirectory),
        s.RecoveryPublicKeyPem is not null, s.RecoveryKeyFingerprint, s.RecoveryKeyConfiguredAtUtc,
        s.RequiredSuccessfulVerifications, s.VerificationCadenceDays, Convert.ToBase64String(s.RowVersion));

    public async Task<BackupSettingsView> GetSettingsAsync(CancellationToken ct)
    {
        var settings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        return ToView(settings ?? new BackupSettings());
    }

    public async Task<BackupSettingsView> SaveSettingsAsync(
        bool scheduleEnabled, int intervalHours, int retentionCount, string? destinationDirectory,
        int requiredVerifications, int cadenceDays, string? rowVersion, Guid actor, CancellationToken ct)
    {
        if (intervalHours is < 1 or > 168) throw new BackupException("invalid_interval", "The backup interval must be between 1 and 168 hours.");
        if (retentionCount is < 1 or > 365) throw new BackupException("invalid_retention", "Retention must keep between 1 and 365 backups.");
        if (requiredVerifications is < 0 or > 10) throw new BackupException("invalid_probation", "The required verification count must be between 0 and 10.");
        if (cadenceDays is < 1 or > 365) throw new BackupException("invalid_cadence", "The verification cadence must be between 1 and 365 days.");

        var destination = string.IsNullOrWhiteSpace(destinationDirectory) ? null : destinationDirectory.Trim();
        if (destination is not null) ValidateDestination(destination);

        var settings = await db.BackupSettings.SingleOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new BackupSettings { UpdatedAtUtc = DateTimeOffset.UtcNow };
            db.BackupSettings.Add(settings);
        }
        else
        {
            ConfigurationWrite.ApplyExpectedVersion(db, settings, rowVersion);
        }

        if (scheduleEnabled && settings.RecoveryPublicKeyPem is null)
            throw new BackupException("recovery_key_required", "Set up the recovery key before enabling scheduled backups.", 409);

        settings.ScheduleEnabled = scheduleEnabled;
        settings.ScheduleIntervalHours = intervalHours;
        settings.RetentionCount = retentionCount;
        settings.DestinationDirectory = destination;
        settings.RequiredSuccessfulVerifications = requiredVerifications;
        settings.VerificationCadenceDays = cadenceDays;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AuditService.Record(db, BackupAuditEvents.SettingsChanged, nameof(BackupSettings), BackupSettings.SingletonId, actor, "Backup schedule/retention/destination settings changed.");
        await ConfigurationWrite.SaveAsync(db, nameof(BackupSettings), BackupSettings.SingletonId, ct);
        return ToView(settings);
    }

    private void ValidateDestination(string directory)
    {
        if (!Path.IsPathRooted(directory))
            throw new BackupException("invalid_destination", "The backup destination must be an absolute folder path.");
        var full = Path.GetFullPath(directory);
        foreach (var forbidden in new[] { paths.StagingRoot, paths.RestoreRoot })
        {
            var f = Path.GetFullPath(forbidden).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if ((full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(f, StringComparison.OrdinalIgnoreCase))
                throw new BackupException("invalid_destination", "The backup destination cannot be inside the staging or restore areas.");
        }
        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BackupException("destination_unavailable", "The backup destination is not available or not writable.");
        }
    }

    /// <summary>
    /// Generates the recovery key pair (an OpenPGP key) and a high-entropy passphrase for it. Only the PUBLIC half is
    /// stored; the passphrase-protected private key and the passphrase are returned ONCE to the caller for the
    /// administrator's offline custody and are never persisted, logged, or written into any backup - so required
    /// recovery material cannot exist only inside the backups it unlocks. Replacing an existing key is explicit:
    /// older backups still need the old key. <paramref name="passphrase"/> is for tests; production passes null so
    /// the passphrase is generated (never person-chosen).
    /// </summary>
    public async Task<(string PrivateKey, string Passphrase)> ConfigureRecoveryKeyAsync(string? passphrase, bool replaceExisting, Guid actor, CancellationToken ct)
    {
        var settings = await LoadOrCreateSettingsAsync(ct);
        if (settings.RecoveryPublicKeyPem is not null && !replaceExisting)
            throw new BackupException("recovery_key_exists", "A recovery key is already configured. Replacing it means older backups still need the OLD key.", 409);

        RecoveryKeyMaterial material;
        try
        {
            material = await Task.Run(() => BackupCrypto.GenerateRecoveryKey(passphrase), ct);
        }
        catch (BackupCryptoException ex)
        {
            throw new BackupException(ex.Code, ex.Message);
        }

        settings.RecoveryPublicKeyPem = material.PublicKeyPem;
        settings.RecoveryKeyFingerprint = material.Fingerprint;
        settings.RecoveryKeyConfiguredAtUtc = DateTimeOffset.UtcNow;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        AuditService.Record(db, BackupAuditEvents.RecoveryKeyConfigured, nameof(BackupSettings), BackupSettings.SingletonId, actor,
            replaceExisting ? "Recovery key replaced (fingerprint recorded; no key material logged)." : "Recovery key configured (fingerprint recorded; no key material logged).");
        await ConfigurationWrite.SaveAsync(db, nameof(BackupSettings), BackupSettings.SingletonId, ct);
        return (material.EncryptedPrivateKeyPem, material.Passphrase);
    }

    // ---------- Creating a backup ----------

    /// <summary>
    /// Runs one backup. Idempotent by <paramref name="idempotencyKey"/>: the same key never produces a second
    /// backup (a Succeeded row is returned as-is; a Failed/stale-Running row is retried in place).
    /// </summary>
    /// <summary>
    /// The manifest, the deployment asset and the captured database must tell ONE story. The database records the
    /// settings its data was created under; if this host runs under different ones the backup would be internally
    /// contradictory (R02-02), so it fails visibly instead. A database with no record yet (legacy) is recorded from
    /// the running settings first - this host is then, by definition, the one the data belongs to.
    /// </summary>
    private async Task EnsureDeploymentAgreesAsync(DeploymentSettings running, CancellationToken ct)
    {
        await using var fresh = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(paths.ConnectionString).Options);
        var record = await DeploymentInvariantStore.EnsureRecordedAsync(fresh, running, ct);
        var differences = DeploymentInvariantStore.AsSettings(record, running.Currency).DifferencesFrom(running);
        if (differences.Count > 0)
            throw new BackupException("deployment_mismatch",
                $"This server is configured differently from the settings its data was created under ({string.Join(", ", differences.Select(d => d.Setting))}). Apply the recorded settings (see System Status) before backing up.", 409);
    }

    public async Task<BackupRecord> RunBackupAsync(BackupKind kind, string idempotencyKey, Guid? actor, CancellationToken ct)
    {
        var settings = await LoadOrCreateSettingsAsync(ct);
        if (settings.RecoveryPublicKeyPem is null)
            throw new BackupException("recovery_key_required", "Set up the recovery key before creating backups.", 409);

        var existing = await db.BackupRecords.SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.Status is BackupStatus.Succeeded or BackupStatus.Purged) return existing;
            if (existing.Status == BackupStatus.Running && DateTimeOffset.UtcNow - existing.StartedAtUtc < StaleRunningThreshold) return existing;
        }

        var record = existing ?? new BackupRecord { Id = Guid.NewGuid(), IdempotencyKey = idempotencyKey, Kind = kind, Status = BackupStatus.Running, StartedAtUtc = DateTimeOffset.UtcNow };
        record.Kind = kind;
        record.Status = BackupStatus.Running;
        record.StartedAtUtc = DateTimeOffset.UtcNow;
        record.CompletedAtUtc = null;
        record.FailureCode = null;
        record.FailureMessage = null;
        record.InitiatedByUserAccountId = actor;
        if (existing is null) db.BackupRecords.Add(record);
        await db.SaveChangesAsync(ct);

        var recordId = record.Id;
        var destination = EffectiveDestination(settings);
        var work = Path.Combine(paths.StagingRoot, recordId.ToString("N"));
        string? partialPath = null;
        try
        {
            try
            {
                Directory.CreateDirectory(destination);
                var probe = Path.Combine(destination, $".write-test-{Guid.NewGuid():N}");
                await File.WriteAllTextAsync(probe, "ok", ct);
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (ex is IOException io && (io.HResult & 0xFFFF) is 0x70 or 0x27) throw;
                throw new BackupException("destination_unavailable", "The backup destination is not available or not writable.");
            }

            Directory.CreateDirectory(Path.Combine(work, "content", "database"));
            var content = Path.Combine(work, "content");

            var captured = deploymentSettings.Current;
            await EnsureDeploymentAgreesAsync(captured, ct); // never snapshot a database from a host configured against its recorded settings

            var countsBefore = await DatabaseFacts.TableRowCountsAsync(paths.ConnectionString, ct);
            var dbFile = Path.Combine(content, "database", "alveara.bak");
            await snapshotProvider.CreateSnapshotAsync(dbFile, ct);
            await EnsureDeploymentAgreesAsync(captured, ct); // and the record did not change under the snapshot
            var countsAfter = await DatabaseFacts.TableRowCountsAsync(paths.ConnectionString, ct);

            var components = new List<BackupManifestComponent>
            {
                new(ManagedAssetClasses.Database, BackupManifest.DatabaseComponentPath, await FileTreeAssetSource.HashFileAsync(dbFile, ct), new FileInfo(dbFile).Length),
            };
            var classes = new List<string> { ManagedAssetClasses.Database };
            foreach (var source in assetSources)
            {
                foreach (var file in await source.StageAsync(content, ct))
                    components.Add(new BackupManifestComponent(file.AssetClass, file.RelativePath, file.Sha256, file.SizeBytes));
                classes.Add(source.AssetClass);
            }

            var migration = await DatabaseFacts.LatestMigrationAsync(paths.ConnectionString, ct);
            var manifest = new BackupManifest(BackupManifest.CurrentFormatVersion, recordId, DateTimeOffset.UtcNow, AppVersion, migration,
                paths.DatabaseName, classes, components, countsBefore, countsAfter, captured);
            await File.WriteAllTextAsync(Path.Combine(content, BackupManifest.FileName), JsonSerializer.Serialize(manifest, BackupManifest.Json), ct);

            var zipPath = Path.Combine(work, "set.zip");
            ZipFile.CreateFromDirectory(content, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            var fileName = $"alveara-backup-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}Z-{recordId.ToString("N")[..8]}.abk";
            var finalPath = Path.Combine(destination, fileName);
            partialPath = finalPath + ".partial";
            await using (var zipStream = File.OpenRead(zipPath))
            await using (var output = File.Create(partialPath))
            {
                await BackupCrypto.EncryptAsync(zipStream, output, settings.RecoveryPublicKeyPem, ct);
            }
            File.Move(partialPath, finalPath, overwrite: false); // atomic publish: a crash earlier leaves no file under the real name
            partialPath = null;

            var hash = await FileTreeAssetSource.HashFileAsync(finalPath, ct);
            var rehash = await FileTreeAssetSource.HashFileAsync(finalPath, ct); // re-read from disk: what was written is what is recorded
            if (hash != rehash) throw new InvalidDataException("Hash changed between reads.");

            await FinishAsync(recordId, r =>
            {
                r.Status = BackupStatus.Succeeded;
                r.CompletedAtUtc = DateTimeOffset.UtcNow;
                r.DestinationDirectory = destination;
                r.FileName = fileName;
                r.SizeBytes = new FileInfo(finalPath).Length;
                r.Sha256 = hash;
                r.IncludedAssetClasses = string.Join(',', classes);
                r.SchemaMigration = migration;
                r.AppVersion = AppVersion;
                r.RecoveryKeyFingerprint = settings.RecoveryKeyFingerprint;
                r.VerificationStatus = BackupVerificationStatus.HashVerified;
                r.VerifiedAtUtc = DateTimeOffset.UtcNow;
            }, BackupAuditEvents.Created, "Encrypted backup created.", actor, ct: CancellationToken.None);

            await ApplyRetentionAsync(CancellationToken.None);
            await NotifyAsync("BackupSucceeded", $"Backup {recordId:N} completed and was written to the configured destination.", recordId, CancellationToken.None);
            return await db.BackupRecords.AsNoTracking().SingleAsync(r => r.Id == recordId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (partialPath is not null && File.Exists(partialPath)) TryDelete(partialPath); // never leave a partial file behind
            var (code, message) = BackupFailure.Classify(ex);
            await FinishAsync(recordId, r =>
            {
                r.Status = BackupStatus.Failed;
                r.CompletedAtUtc = DateTimeOffset.UtcNow;
                r.FailureCode = code;
                r.FailureMessage = message;
            }, BackupAuditEvents.Failed, $"Backup failed: {code}.", actor, ct: CancellationToken.None);
            await NotifyAsync("BackupFailed", $"Backup {recordId:N} FAILED ({code}): {message}", recordId, CancellationToken.None);
            throw;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    private async Task FinishAsync(Guid recordId, Action<BackupRecord> apply, string auditEvent, string auditDetails, Guid? actor, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // a failed run may have left the context in an unknown state
        var record = await db.BackupRecords.SingleAsync(r => r.Id == recordId, ct);
        apply(record);
        AuditService.Record(db, auditEvent, nameof(BackupRecord), recordId, actor, auditDetails);
        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyAsync(string kind, string message, Guid? backupId, CancellationToken ct)
    {
        var row = new BackupNotificationRecord { Id = Guid.NewGuid(), BackupRecordId = backupId, Kind = kind, Message = message, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.BackupNotifications.Add(row);
        try
        {
            await notifier.DeliverAsync(new BackupNotification(kind, message, backupId), ct);
            row.Delivery = BackupNotificationDelivery.Delivered;
        }
        catch (Exception)
        {
            // Delivery trouble is its own recorded fact. It must never turn a successful backup into a failed one (or vice versa).
            row.Delivery = BackupNotificationDelivery.Failed;
            row.DeliveryFailureCode = "delivery_failed";
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<BackupNotificationRecord> SendTestNotificationAsync(Guid actor, CancellationToken ct)
    {
        await NotifyAsync("TestNotification", "This is a test of the backup notification channel.", null, ct);
        var row = await db.BackupNotifications.AsNoTracking().OrderByDescending(n => n.CreatedAtUtc).FirstAsync(ct);
        AuditService.Record(db, BackupAuditEvents.TestNotificationSent, nameof(BackupNotificationRecord), row.Id, actor, $"Test notification {row.Delivery}.");
        await db.SaveChangesAsync(ct);
        return row;
    }

    // ---------- Retention ----------

    /// <summary>Keeps the newest <see cref="BackupSettings.RetentionCount"/> successful backups; never purges the newest fully-verified one.</summary>
    public async Task<int> ApplyRetentionAsync(CancellationToken ct)
    {
        var settings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        if (settings is null) return 0;
        var succeeded = await db.BackupRecords.Where(r => r.Status == BackupStatus.Succeeded).OrderByDescending(r => r.StartedAtUtc).ToListAsync(ct);
        var newestVerified = succeeded.FirstOrDefault(r => r.VerificationStatus == BackupVerificationStatus.FullyVerified && r.RestoreProvenAtUtc != null)
                           ?? succeeded.FirstOrDefault(r => r.VerificationStatus == BackupVerificationStatus.FullyVerified); // prefer the one proven by a drill
        var purged = 0;
        foreach (var old in succeeded.Skip(Math.Max(1, settings.RetentionCount)))
        {
            if (old.Id == newestVerified?.Id) continue;
            try { TryDelete(ResolveBackupPath(old)); } catch (BackupException) { /* no recorded file: nothing to delete */ }
            old.Status = BackupStatus.Purged;
            AuditService.Record(db, BackupAuditEvents.Purged, nameof(BackupRecord), old.Id, null, "Backup removed by the retention policy.");
            purged++;
        }
        if (purged > 0) await db.SaveChangesAsync(ct);
        return purged;
    }

    // ---------- Hash verification (needs no recovery key) ----------

    public static string ResolveBackupPath(BackupRecord record)
    {
        if (record.FileName is null || record.DestinationDirectory is null || record.FileName != Path.GetFileName(record.FileName))
            throw new BackupException("backup_file_unknown", "This backup has no recorded file.", 404);
        return Path.Combine(record.DestinationDirectory, record.FileName);
    }

    /// <summary>Recomputes the file's SHA-256 and compares it with the hash recorded at creation. Proves the bytes are unchanged; it cannot prove restorability (that needs the recovery key).</summary>
    public async Task<BackupRecord> VerifyHashAsync(Guid recordId, Guid actor, CancellationToken ct)
    {
        var record = await db.BackupRecords.SingleOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new BackupException("not_found", "Backup not found.", 404);
        if (record.Status != BackupStatus.Succeeded) throw new BackupException("backup_not_available", "Only a successful, retained backup can be verified.", 409);

        string? failure = null;
        try
        {
            var path = ResolveBackupPath(record);
            if (!File.Exists(path)) failure = "backup_file_missing";
            else if (await FileTreeAssetSource.HashFileAsync(path, ct) != record.Sha256) failure = "hash_mismatch";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = "destination_unavailable";
        }

        if (failure is null)
        {
            if (!record.HasArchiveDefect) // a passing byte check says nothing about a defect already proven inside the archive
            {
                if (record.VerificationStatus != BackupVerificationStatus.FullyVerified) record.VerificationStatus = BackupVerificationStatus.HashVerified;
                record.VerifiedAtUtc = DateTimeOffset.UtcNow;
                record.VerificationFailureCode = null;
            }
            AuditService.Record(db, BackupAuditEvents.HashVerified, nameof(BackupRecord), recordId, actor,
                record.HasArchiveDefect ? $"Backup file hash verified; the recorded archive defect ({record.ArchiveDefectCode}) remains." : "Backup file hash verified.");
        }
        else
        {
            record.MarkVerificationFailed(failure, DateTimeOffset.UtcNow);
            AuditService.Record(db, BackupAuditEvents.VerificationFailed, nameof(BackupRecord), recordId, actor, $"Backup verification failed: {failure}.");
        }
        await db.SaveChangesAsync(ct);
        if (failure is not null) await NotifyAsync("BackupVerificationFailed", $"Backup {recordId:N} failed verification ({failure}).", recordId, ct);
        return record;
    }

    // ---------- Interrupted runs ----------

    /// <summary>Marks runs that were Running when the process died as Failed(interrupted) and removes their partial files.</summary>
    public async Task<int> RecoverInterruptedAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - StaleRunningThreshold;
        var stale = await db.BackupRecords.Where(r => r.Status == BackupStatus.Running && r.StartedAtUtc < cutoff).ToListAsync(ct);
        foreach (var record in stale)
        {
            record.Status = BackupStatus.Failed;
            record.CompletedAtUtc = DateTimeOffset.UtcNow;
            record.FailureCode = "interrupted";
            record.FailureMessage = "The backup was interrupted before it finished (the server stopped).";
            TryDeleteDirectory(Path.Combine(paths.StagingRoot, record.Id.ToString("N")));
            AuditService.Record(db, BackupAuditEvents.Failed, nameof(BackupRecord), record.Id, null, "Backup failed: interrupted.");
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    // ---------- Status ----------

    public async Task<IReadOnlyList<BackupRecord>> HistoryAsync(int take, CancellationToken ct) =>
        await db.BackupRecords.AsNoTracking().OrderByDescending(r => r.StartedAtUtc).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);

    public async Task<IReadOnlyList<BackupNotificationRecord>> NotificationsAsync(int take, CancellationToken ct) =>
        await db.BackupNotifications.AsNoTracking().OrderByDescending(n => n.CreatedAtUtc).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);

    public async Task<BackupStatusView> GetStatusAsync(CancellationToken ct)
    {
        var settings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new BackupSettings();
        var records = db.BackupRecords.AsNoTracking();
        var lastSuccess = await records.Where(r => r.Status == BackupStatus.Succeeded).OrderByDescending(r => r.CompletedAtUtc).FirstOrDefaultAsync(ct);
        var lastFailure = await records.Where(r => r.Status == BackupStatus.Failed).OrderByDescending(r => r.CompletedAtUtc).FirstOrDefaultAsync(ct);
        var latest = await records.Where(r => r.Status != BackupStatus.Purged).OrderByDescending(r => r.StartedAtUtc).FirstOrDefaultAsync(ct);

        // Trust is earned ONLY by restore drills (application-level proof), and only while the backup is still not known to be defective.
        // "Verify only" (media/inventory) is a useful check but never counts, so a backup later proven contradictory cannot have earned credit through it.
        var proven = await records
            .Where(r => r.RestoreProvenAtUtc != null && r.VerificationStatus == BackupVerificationStatus.FullyVerified)
            .Select(r => r.RestoreProvenAtUtc!.Value).ToListAsync(ct);
        var lastProof = proven.Count == 0 ? (DateTimeOffset?)null : proven.Max();
        var count = proven.Count;

        var included = (lastSuccess?.IncludedAssetClasses ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var missing = lastSuccess is null ? ManagedAssetClasses.All.ToList() : ManagedAssetClasses.All.Where(c => !included.Contains(c)).ToList();

        return new BackupStatusView(
            ToView(settings), lastSuccess, lastFailure,
            latest is { Status: BackupStatus.Failed },
            count, count >= settings.RequiredSuccessfulVerifications,
            lastProof,
            lastSuccess is not null && (lastProof is null || DateTimeOffset.UtcNow - lastProof > TimeSpan.FromDays(settings.VerificationCadenceDays)),
            ManagedAssetClasses.All, lastSuccess is not null && missing.Count == 0, missing);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort; a leftover is reported by the next verification */ }
    }

    internal static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort scratch cleanup */ }
    }
}

public static class BackupAuditEvents
{
    public const string Created = "BackupCreated";
    public const string Failed = "BackupFailed";
    public const string HashVerified = "BackupHashVerified";
    public const string FullyVerified = "BackupFullyVerified";
    public const string VerificationFailed = "BackupVerificationFailed";
    public const string Purged = "BackupPurgedByRetention";
    public const string RestoreDrillCompleted = "RestoreDrillCompleted";
    public const string RestoreDrillFailed = "RestoreDrillFailed";
    public const string RestoreTargetRemoved = "RestoreTargetRemoved";
    public const string RecoveryKeyConfigured = "RecoveryKeyConfigured";
    public const string SettingsChanged = "BackupSettingsChanged";
    public const string TestNotificationSent = "BackupTestNotificationSent";
}
