using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Backup;

/// <summary>One post-restore (or preflight) check. A non-blocking check is a warning, not a failure.</summary>
public sealed record ValidationCheck(string Name, bool Passed, string Detail, bool Blocking = true);

public sealed record PreflightResult(bool CanRestore, IReadOnlyList<ValidationCheck> Checks);

/// <summary>A retained backup archive found on disk, independent of any database row.</summary>
public sealed record ArchiveInfo(
    string Ref, string Location, string FileName, long SizeBytes, DateTimeOffset ModifiedUtc,
    bool Readable, string? RecipientKeyId, bool KeyMatchesConfiguredRecoveryKey, bool InHistory);

/// <summary>
/// ALV-N004: verification and restore. Restore is ALWAYS into an isolated target (a new, differently
/// named database plus a private directory) - there is no code path here, or anywhere in the
/// application, that restores over the live database or live files. Promoting a verified restore to
/// production is an explicit offline procedure (see the recovery runbook), not an in-app operation.
/// </summary>
public class BackupRestoreService(
    AlveraDbContext db, BackupPaths paths, IDatabaseRestoreProvider restoreProvider, IBackupNotifier notifier, IDeploymentSettingsProvider deploymentSettings)
{
    private sealed record Extracted(BackupManifest Manifest, string Root);

    /// <summary>
    /// A backup file to read: either a recorded backup (its recorded hash is checked) or a RETAINED ARCHIVE found on
    /// disk with no database row at all (disaster recovery: the lost server's history is gone). Authenticity of an
    /// archive comes from its authenticated decryption and the inventory inside it, never from a hash that lived in
    /// the lost database.
    /// </summary>
    private sealed record Source(string Path, string Name, string? ExpectedSha256, BackupRecord? Record);

    private static Source SourceOf(BackupRecord record) => new(BackupService.ResolveBackupPath(record), record.FileName ?? "", record.Sha256, record);

    private string DestinationDirectory(BackupSettings? settings) =>
        string.IsNullOrWhiteSpace(settings?.DestinationDirectory) ? paths.DefaultBackupDirectory : settings.DestinationDirectory;

    /// <summary>Resolves "destination:NAME.abk" / "import:NAME.abk" to a file inside one of the two archive folders - never an arbitrary path.</summary>
    private async Task<Source> ArchiveSourceAsync(string archiveRef, CancellationToken ct)
    {
        var colon = archiveRef?.IndexOf(':') ?? -1;
        if (colon <= 0) throw new BackupException("archive_unknown", "Choose one of the listed backup files.", 404);
        var location = archiveRef![..colon];
        var name = archiveRef[(colon + 1)..];
        if (name != Path.GetFileName(name) || !name.EndsWith(".abk", StringComparison.OrdinalIgnoreCase))
            throw new BackupException("archive_unknown", "Choose one of the listed backup files.", 404);

        var settings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var directory = location switch
        {
            "destination" => DestinationDirectory(settings),
            "import" => paths.ImportDirectory,
            _ => throw new BackupException("archive_unknown", "Choose one of the listed backup files.", 404),
        };
        var path = Path.Combine(directory, name);
        if (!File.Exists(path)) throw new BackupException("backup_file_missing", "That backup file is no longer in its folder.", 404);
        return new Source(path, name, null, null);
    }

    // ---------- Archive discovery (needs no history) ----------

    /// <summary>
    /// Lists the retained <c>.abk</c> files in the backup destination and the import folder, reading only each file's
    /// OpenPGP header (the recipient key id) - no secret and no database row required. This is the disaster-recovery
    /// entry point: on a replacement server with an empty history, copy the retained archive into the import folder.
    /// </summary>
    public async Task<IReadOnlyList<ArchiveInfo>> ListArchivesAsync(CancellationToken ct)
    {
        var settings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var configuredKeyId = settings?.RecoveryKeyFingerprint is { Length: >= 16 } fp ? fp[^16..].ToUpperInvariant() : null;
        var known = await db.BackupRecords.AsNoTracking().Where(r => r.FileName != null).Select(r => r.FileName!).ToListAsync(ct);

        var folders = new List<(string Location, string Directory)> { ("destination", DestinationDirectory(settings)), ("import", paths.ImportDirectory) };
        var found = new List<ArchiveInfo>();
        foreach (var (location, directory) in folders.DistinctBy(f => Path.GetFullPath(f.Directory)))
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.abk").OrderByDescending(f => f.LastWriteTimeUtc).Take(200))
            {
                string? recipient = null;
                try
                {
                    await using var stream = file.OpenRead();
                    recipient = await BackupCrypto.ReadRecipientKeyIdAsync(stream, ct);
                }
                catch (Exception ex) when (ex is BackupCryptoException or IOException) { /* listed as unreadable */ }
                found.Add(new ArchiveInfo($"{location}:{file.Name}", location, file.Name, file.Length, file.LastWriteTimeUtc, recipient is not null,
                    recipient, recipient is not null && recipient == configuredKeyId, known.Contains(file.Name)));
            }
        }
        return found;
    }

    // ---------- Preflight ----------

    /// <summary>Checks everything that can be checked cheaply BEFORE any restore: the backup row, file, hash, and that the supplied recovery material is the right key.</summary>
    public async Task<PreflightResult> PreflightAsync(Guid recordId, string privateKeyPem, string passphrase, CancellationToken ct)
    {
        var record = await db.BackupRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new BackupException("not_found", "Backup not found.", 404);
        var checks = new List<ValidationCheck>
        {
            new("backup_available", record.Status == BackupStatus.Succeeded, record.Status == BackupStatus.Succeeded ? "The backup completed successfully." : $"The backup is {record.Status}."),
        };

        var included = (record.IncludedAssetClasses ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var missingClasses = ManagedAssetClasses.All.Where(c => !included.Contains(c)).ToList();
        checks.Add(new("covers_all_asset_classes", missingClasses.Count == 0,
            missingClasses.Count == 0 ? "The backup covers every currently managed asset class." : $"Not included: {string.Join(", ", missingClasses)}.", Blocking: false));

        if (record.Status == BackupStatus.Succeeded)
        {
            string? path = null;
            try { path = BackupService.ResolveBackupPath(record); } catch (BackupException) { /* reported below */ }
            var exists = path is not null && File.Exists(path);
            checks.Add(new("backup_file_present", exists, exists ? "The backup file is present." : "The backup file is missing."));
            if (exists)
            {
                var hashOk = await FileTreeAssetSource.HashFileAsync(path!, ct) == record.Sha256;
                checks.Add(new("file_hash_matches", hashOk, hashOk ? "The file matches the hash recorded at creation." : "The file does not match its recorded hash: it is corrupt or has been altered."));
                checks.Add(await CheckRecoveryMaterialAsync(path!, privateKeyPem, passphrase, ct));
            }
        }
        return new PreflightResult(checks.All(c => c.Passed || !c.Blocking), checks);
    }

    /// <summary>
    /// Preflight for a RETAINED ARCHIVE with no history row. There is no recorded hash to compare (it lived in the lost
    /// database); the checks are that the file is a readable backup, which recovery key it was made for, and that the
    /// supplied key and passphrase are that key - the archive's contents are authenticated when it is decrypted.
    /// </summary>
    public async Task<PreflightResult> PreflightArchiveAsync(string archiveRef, string privateKeyPem, string passphrase, CancellationToken ct)
    {
        var source = await ArchiveSourceAsync(archiveRef, ct);
        var checks = new List<ValidationCheck> { new("archive_present", true, "The backup file is present.") };
        var material = await CheckRecoveryMaterialAsync(source.Path, privateKeyPem, passphrase, ct);
        checks.Add(new("archive_is_a_backup", material.Name != "recovery_material_matches" || material.Passed || !material.Detail.Contains("not an Alveara backup"), "The file is an Alveara backup."));
        checks.Add(material);
        checks.Add(new("integrity_verified_on_decryption", true, "There is no recorded hash for a retained archive; its contents are verified by authenticated decryption and the manifest when you restore.", Blocking: false));
        return new PreflightResult(checks.All(c => c.Passed || !c.Blocking), checks);
    }

    private static async Task<ValidationCheck> CheckRecoveryMaterialAsync(string path, string privateKeyPem, string passphrase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPem) || string.IsNullOrEmpty(passphrase))
            return new ValidationCheck("recovery_material_present", false, "The recovery key file and its passphrase are required.");
        try
        {
            await using var stream = File.OpenRead(path);
            var recipient = await BackupCrypto.ReadRecipientKeyIdAsync(stream, ct);
            if (BackupCrypto.EncryptionKeyIdOfSecretKey(privateKeyPem) != recipient)
                return new ValidationCheck("recovery_material_matches", false, "This recovery key does not belong to this backup.");
            BackupCrypto.EnsurePassphraseUnlocks(privateKeyPem, passphrase);
            return new ValidationCheck("recovery_material_matches", true, "The recovery key and passphrase match this backup.");
        }
        catch (BackupCryptoException ex)
        {
            return new ValidationCheck("recovery_material_matches", false, ex.Message);
        }
    }

    // ---------- Decrypt + extract + inventory ----------

    private async Task<Extracted> ExtractAsync(Source source, string privateKeyPem, string passphrase, string workDirectory, CancellationToken ct)
    {
        if (source.Record is { } record && record.Status != BackupStatus.Succeeded)
            throw new BackupException("backup_not_available", "Only a successful, retained backup can be restored.", 409);
        var path = source.Path;
        if (!File.Exists(path)) throw new BackupException("backup_file_missing", "The backup file is missing from its destination.");
        if (source.ExpectedSha256 is not null && await FileTreeAssetSource.HashFileAsync(path, ct) != source.ExpectedSha256)
            throw new BackupException("hash_mismatch", "The backup file does not match its recorded hash: it is corrupt or has been altered.");

        Directory.CreateDirectory(workDirectory);
        var zipPath = Path.Combine(workDirectory, "set.zip");
        await using (var input = File.OpenRead(path))
        await using (var zip = File.Create(zipPath))
        {
            await BackupCrypto.DecryptAsync(input, zip, privateKeyPem, passphrase, ct); // wrong key / corrupt / tampered all throw here, before anything is extracted
        }

        var root = Path.Combine(workDirectory, "extracted");
        try
        {
            ZipFile.ExtractToDirectory(zipPath, root); // rejects entries that would escape the target directory
        }
        catch (InvalidDataException)
        {
            throw new BackupException("corrupt_or_tampered", "The backup contents are damaged.");
        }
        File.Delete(zipPath);

        var manifestPath = Path.Combine(root, BackupManifest.FileName);
        if (!File.Exists(manifestPath)) throw new BackupException("manifest_missing", "The backup has no manifest.");
        BackupManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(manifestPath, ct), BackupManifest.Json)
                ?? throw new BackupException("manifest_invalid", "The backup manifest is unreadable.");
        }
        catch (JsonException)
        {
            throw new BackupException("manifest_invalid", "The backup manifest is unreadable.");
        }
        return new Extracted(manifest, root);
    }

    /// <summary>Compatibility + inventory checks that need the manifest but touch no database.</summary>
    private async Task<List<ValidationCheck>> InventoryChecksAsync(Extracted extracted, CancellationToken ct)
    {
        var manifest = extracted.Manifest;
        var checks = new List<ValidationCheck>();

        checks.Add(new("format_supported", manifest.FormatVersion == BackupManifest.CurrentFormatVersion,
            manifest.FormatVersion == BackupManifest.CurrentFormatVersion ? "Backup format is supported." : $"Backup format {manifest.FormatVersion} is not supported by this version."));

        var known = db.Database.GetMigrations().ToHashSet();
        var schemaKnown = known.Contains(manifest.SchemaMigration);
        checks.Add(new("schema_compatible", schemaKnown,
            schemaKnown ? $"Schema {manifest.SchemaMigration} is known to this version." : $"Schema {manifest.SchemaMigration} is newer than (or unknown to) this version of the application."));

        var missing = new List<string>();
        var mismatched = new List<string>();
        var rootFull = Path.GetFullPath(extracted.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var component in manifest.Components)
        {
            var file = Path.GetFullPath(Path.Combine(extracted.Root, component.Path.Replace('/', Path.DirectorySeparatorChar)));
            // A manifest is authenticated, but a path that climbs out of the extracted set is still never read.
            if (!file.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) { missing.Add(component.Path); continue; }
            if (!File.Exists(file)) { missing.Add(component.Path); continue; }
            if (await FileTreeAssetSource.HashFileAsync(file, ct) != component.Sha256) mismatched.Add(component.Path);
        }
        var missingDocs = missing.Where(p => p.StartsWith(ManagedAssetClasses.Documents + "/", StringComparison.Ordinal)).ToList();
        checks.Add(new("documents_complete", missingDocs.Count == 0,
            missingDocs.Count == 0 ? "Every document/blob listed in the manifest is present." : $"{missingDocs.Count} document/blob file(s) listed in the manifest are missing from the backup set."));
        var missingOther = missing.Except(missingDocs).ToList();
        checks.Add(new("components_present", missingOther.Count == 0,
            missingOther.Count == 0 ? "Every other component is present." : $"Missing from the backup set: {string.Join(", ", missingOther)}."));
        checks.Add(new("component_hashes_match", mismatched.Count == 0,
            mismatched.Count == 0 ? "Every component matches the hash in the manifest." : $"{mismatched.Count} component(s) do not match the manifest hashes."));

        checks.AddRange(DeploymentChecks(manifest));
        checks.Add(await DeploymentAssetAgreementAsync(extracted, ct));

        var missingClasses = ManagedAssetClasses.All.Where(c => !manifest.AssetClasses.Contains(c)).ToList();
        checks.Add(new("covers_all_asset_classes", missingClasses.Count == 0,
            missingClasses.Count == 0 ? "The backup covers every currently managed asset class." : $"Not covered by this backup: {string.Join(", ", missingClasses)}.", Blocking: false));
        return checks;
    }

    /// <summary>
    /// R01-04: the deployment settings the data was created under must be recorded in the backup AND match this server,
    /// otherwise recovery is blocked with the exact settings to apply (a restored schedule must not be silently
    /// reinterpreted in another time zone, nor MFA secrets left undecryptable).
    /// </summary>
    private List<ValidationCheck> DeploymentChecks(BackupManifest manifest)
    {
        if (manifest.Deployment is not { } recorded)
            return [new("deployment_settings_recorded", false, "This backup does not record the deployment settings (practice time zone, Data Protection application name); it cannot be restored without risking misread schedules and unusable MFA.")];

        var differences = recorded.DifferencesFrom(deploymentSettings.Current);
        var required = string.Join(" and ", recorded.RequiredSettings().Select(s => $"{s.Setting} = {s.Value}"));
        return
        [
            new("deployment_settings_recorded", true, $"The backup records its deployment settings ({required})."),
            new("deployment_settings_match_this_server", differences.Count == 0,
                differences.Count == 0
                    ? "This server is configured with the deployment settings the data was created under."
                    : $"This server is configured differently ({string.Join(", ", differences.Select(d => d.Setting))}). Set {required} on this server, restart, and run recovery again."),
        ];
    }

    /// <summary>The deployment asset file inside the set must say exactly what the manifest says (R02-02).</summary>
    private static async Task<ValidationCheck> DeploymentAssetAgreementAsync(Extracted extracted, CancellationToken ct)
    {
        const string name = "deployment_asset_matches_manifest";
        var path = Path.Combine(extracted.Root, ManagedAssetClasses.DeploymentConfiguration, DeploymentSettings.FileName);
        if (extracted.Manifest.Deployment is null) return new(name, true, "No deployment settings are recorded (reported by deployment_settings_recorded).", Blocking: false);
        if (!File.Exists(path)) return new(name, false, "The deployment settings file listed for this backup is missing from the set.");
        try
        {
            var asset = JsonSerializer.Deserialize<DeploymentSettings>(await File.ReadAllTextAsync(path, ct), DeploymentSettings.Json);
            return asset is not null && asset.DifferencesFrom(extracted.Manifest.Deployment).Count == 0
                ? new(name, true, "The deployment settings file and the manifest agree.")
                : new(name, false, "The deployment settings file contradicts the manifest: this backup is internally inconsistent.");
        }
        catch (JsonException)
        {
            return new(name, false, "The deployment settings file in the set is unreadable.");
        }
    }

    // ---------- Full verification (decrypt + inventory + RESTORE VERIFYONLY; nothing is restored) ----------

    public async Task<BackupRecord> VerifyFullAsync(Guid recordId, string privateKeyPem, string passphrase, Guid actor, CancellationToken ct)
    {
        var record = await db.BackupRecords.SingleOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new BackupException("not_found", "Backup not found.", 404);
        var work = Path.Combine(paths.StagingRoot, $"verify-{Guid.NewGuid():N}");
        string? failureCode = null;
        try
        {
            var extracted = await ExtractAsync(SourceOf(record), privateKeyPem, passphrase, work, ct);
            var checks = await InventoryChecksAsync(extracted, ct);
            var failed = checks.FirstOrDefault(c => !c.Passed && c.Blocking);
            if (failed is not null) failureCode = failed.Name;
            else if (!await restoreProvider.VerifyBackupAsync(Path.Combine(extracted.Root, "database", "alveara.bak"), ct)) failureCode = "database_backup_damaged";
        }
        catch (Exception ex)
        {
            failureCode = BackupFailure.Classify(ex).Code;
        }
        finally
        {
            BackupService.TryDeleteDirectory(work);
        }

        record.VerifiedAtUtc = DateTimeOffset.UtcNow;
        if (failureCode is null)
        {
            record.VerificationStatus = BackupVerificationStatus.FullyVerified;
            record.VerificationFailureCode = null;
            AuditService.Record(db, BackupAuditEvents.FullyVerified, nameof(BackupRecord), recordId, actor, "Backup fully verified with the recovery key (decrypted, inventory and SQL Server media check passed).");
        }
        else
        {
            record.VerificationStatus = BackupVerificationStatus.VerificationFailed;
            record.VerificationFailureCode = failureCode;
            AuditService.Record(db, BackupAuditEvents.VerificationFailed, nameof(BackupRecord), recordId, actor, $"Backup verification failed: {failureCode}.");
        }
        await db.SaveChangesAsync(ct);
        if (failureCode is not null) await NotifyFailureAsync("BackupVerificationFailed", $"Backup {recordId:N} failed full verification ({failureCode}).", recordId, ct);
        return record;
    }

    // ---------- Restore drill (isolated target) ----------

    public static string TargetDatabaseName(Guid drillId) => $"AlveraRestore_{drillId:N}";

    public async Task<RestoreDrillRecord> RestoreDrillAsync(Guid recordId, string privateKeyPem, string passphrase, Guid actor, CancellationToken ct)
    {
        var record = await db.BackupRecords.SingleOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new BackupException("not_found", "Backup not found.", 404);
        var drill = new RestoreDrillRecord { Id = Guid.NewGuid(), BackupRecordId = recordId, SourceKind = "History", StartedAtUtc = DateTimeOffset.UtcNow, InitiatedByUserAccountId = actor };
        return await RunDrillAsync(drill, SourceOf(record), record, privateKeyPem, passphrase, actor, ct);
    }

    /// <summary>
    /// Disaster-recovery entry point (R01-01): restores a RETAINED ARCHIVE into an isolated target with no backup history
    /// in this database at all. The archive's SHA-256 is recorded on the drill for traceability.
    /// </summary>
    public async Task<RestoreDrillRecord> RestoreDrillFromArchiveAsync(string archiveRef, string privateKeyPem, string passphrase, Guid actor, CancellationToken ct)
    {
        var source = await ArchiveSourceAsync(archiveRef, ct);
        var drill = new RestoreDrillRecord
        {
            Id = Guid.NewGuid(), BackupRecordId = null, SourceKind = "Archive", ArchiveFileName = source.Name,
            ArchiveSha256 = await FileTreeAssetSource.HashFileAsync(source.Path, ct), StartedAtUtc = DateTimeOffset.UtcNow, InitiatedByUserAccountId = actor,
        };
        return await RunDrillAsync(drill, source, null, privateKeyPem, passphrase, actor, ct);
    }

    private async Task<RestoreDrillRecord> RunDrillAsync(
        RestoreDrillRecord drill, Source source, BackupRecord? record, string privateKeyPem, string passphrase, Guid actor, CancellationToken ct)
    {
        var recordId = record?.Id;
        var targetDirectory = Path.Combine(paths.RestoreRoot, drill.Id.ToString("N"));
        var targetDatabase = TargetDatabaseName(drill.Id);
        drill.TargetDirectory = targetDirectory;
        drill.TargetDatabase = targetDatabase;
        db.RestoreDrills.Add(drill);
        await db.SaveChangesAsync(ct);

        var checks = new List<ValidationCheck>();
        string? failureCode = null;
        string? failureMessage = null;
        try
        {
            var extracted = await ExtractAsync(source, privateKeyPem, passphrase, targetDirectory, ct);
            checks.AddRange(await InventoryChecksAsync(extracted, ct));
            var blocking = checks.FirstOrDefault(c => !c.Passed && c.Blocking);
            if (blocking is not null)
            {
                failureCode = blocking.Name is "schema_compatible" or "format_supported" ? "incompatible_backup" : blocking.Name;
                failureMessage = blocking.Detail;
            }
            else
            {
                var dbFile = Path.Combine(extracted.Root, "database", "alveara.bak");
                if (!await restoreProvider.VerifyBackupAsync(dbFile, ct))
                {
                    failureCode = "database_backup_damaged";
                    failureMessage = "SQL Server rejected the database backup inside the set as damaged.";
                }
                else
                {
                    await restoreProvider.RestoreToNewDatabaseAsync(dbFile, targetDatabase, Path.Combine(targetDirectory, "db"), ct);
                    File.Delete(dbFile); // the live copy now exists inside SQL Server; keep disk usage down
                    checks.AddRange(await ValidateRestoredApplicationAsync(extracted, targetDatabase, ct));
                    var postFailure = checks.FirstOrDefault(c => !c.Passed && c.Blocking);
                    if (postFailure is not null) { failureCode = "restore_validation_failed"; failureMessage = postFailure.Detail; }
                }
            }
        }
        catch (Exception ex)
        {
            (failureCode, failureMessage) = BackupFailure.Classify(ex);
        }

        drill.CompletedAtUtc = DateTimeOffset.UtcNow;
        drill.ValidationJson = JsonSerializer.Serialize(checks, BackupManifest.Json);
        if (failureCode is null)
        {
            drill.Outcome = "Succeeded";
            if (record is not null)
            {
                record.VerificationStatus = BackupVerificationStatus.FullyVerified;
                record.VerifiedAtUtc = DateTimeOffset.UtcNow;
                record.VerificationFailureCode = null;
            }
            AuditService.Record(db, BackupAuditEvents.RestoreDrillCompleted, nameof(RestoreDrillRecord), drill.Id, actor, "Restore drill into an isolated target completed and validated.");
            await db.SaveChangesAsync(ct);
        }
        else
        {
            drill.Outcome = "Failed";
            drill.FailureCode = failureCode;
            drill.FailureMessage = failureMessage;
            // Nothing half-restored is left lying around: remove the isolated target we created.
            await SafeRemoveTargetAsync(drill, CancellationToken.None);
            AuditService.Record(db, BackupAuditEvents.RestoreDrillFailed, nameof(RestoreDrillRecord), drill.Id, actor, $"Restore drill failed: {failureCode}.");
            await db.SaveChangesAsync(CancellationToken.None);
            await NotifyFailureAsync("RestoreDrillFailed", $"Restore drill {drill.Id:N} FAILED ({failureCode}).", recordId, CancellationToken.None);
        }
        return drill;
    }

    /// <summary>Post-restore validation: schema, row counts, representative records, and that the restored Data Protection keys decrypt the restored MFA secrets.</summary>
    private async Task<List<ValidationCheck>> ValidateRestoredApplicationAsync(Extracted extracted, string targetDatabase, CancellationToken ct)
    {
        var checks = new List<ValidationCheck>();
        var connectionString = restoreProvider.ConnectionStringFor(targetDatabase);

        var migration = await DatabaseFacts.LatestMigrationAsync(connectionString, ct);
        checks.Add(new("restored_schema_matches", migration == extracted.Manifest.SchemaMigration,
            migration == extracted.Manifest.SchemaMigration ? $"Restored database is at {migration}." : $"Restored database is at {migration}, expected {extracted.Manifest.SchemaMigration}."));

        var restored = await DatabaseFacts.TableRowCountsAsync(connectionString, ct);
        var outside = new List<string>();
        foreach (var (table, before) in extracted.Manifest.TableRowCountsBefore)
        {
            var after = extracted.Manifest.TableRowCountsAfter.TryGetValue(table, out var a) ? a : before;
            var got = restored.TryGetValue(table, out var g) ? g : -1;
            if (got < Math.Min(before, after) || got > Math.Max(before, after)) outside.Add(table);
        }
        checks.Add(new("row_counts_match_snapshot_window", outside.Count == 0,
            outside.Count == 0 ? "Every table's restored row count lies within the counts recorded around the snapshot." : $"Row counts differ from the manifest for: {string.Join(", ", outside)}."));

        await using var restoredDb = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(connectionString).Options);
        var accounts = await restoredDb.UserAccounts.AsNoTracking().CountAsync(ct);
        var audits = await restoredDb.AuditLogEntries.AsNoTracking().CountAsync(ct);
        var latestAudit = audits == 0 ? null : await restoredDb.AuditLogEntries.AsNoTracking().OrderByDescending(a => a.TimestampUtc).Select(a => a.EventType).FirstAsync(ct);
        checks.Add(new("representative_records_readable", true,
            $"Read {accounts} account(s) and {audits} audit entr{(audits == 1 ? "y" : "ies")} from the restored database" + (latestAudit is null ? "." : $" (latest event: {latestAudit}).")));

        checks.Add(await RestoredDeploymentRecordAsync(extracted, connectionString, ct));

        var keysDirectory = Path.Combine(extracted.Root, ManagedAssetClasses.DataProtectionKeys);
        var secrets = await restoredDb.UserAccounts.AsNoTracking().Where(u => u.MfaSecretProtected != null).Select(u => u.MfaSecretProtected!).Take(5).ToListAsync(ct);
        if (secrets.Count == 0)
        {
            checks.Add(new("restored_keys_decrypt_restored_data", true, "No MFA secrets exist yet to test against; the keys directory was restored."));
        }
        else
        {
            try
            {
                // The backup recorded the application discriminator its secrets were protected under; use exactly that (a replacement server must be configured with it).
                var applicationName = extracted.Manifest.Deployment?.DataProtectionApplicationName;
                var provider = DataProtectionProvider.Create(new DirectoryInfo(keysDirectory), o => { if (!string.IsNullOrEmpty(applicationName)) o.SetApplicationName(applicationName); });
                var protector = provider.CreateProtector(AccountService.MfaSecretProtectorPurpose);
                foreach (var secret in secrets) protector.Unprotect(secret);
                checks.Add(new("restored_keys_decrypt_restored_data", true, $"The restored Data Protection keys decrypted {secrets.Count} restored MFA secret(s)."));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                checks.Add(new("restored_keys_decrypt_restored_data", false, "The restored keys could NOT decrypt the restored MFA secrets: restored accounts would lose their second factor."));
            }
        }
        return checks;
    }

    /// <summary>
    /// The restored database records the settings its data was created under; they must equal what the manifest told the
    /// operator to apply, or applying them would make the recovered application reject its own data (R02-02). A missing
    /// record is a failure too: a backup of this format is only produced after the record exists.
    /// </summary>
    private static async Task<ValidationCheck> RestoredDeploymentRecordAsync(Extracted extracted, string connectionString, CancellationToken ct)
    {
        const string name = "restored_database_deployment_matches_manifest";
        if (extracted.Manifest.Deployment is not { } recorded) return new(name, false, "The backup records no deployment settings to compare with the restored database.");
        try
        {
            await using var db = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(connectionString).Options);
            var row = await db.DeploymentInvariants.AsNoTracking().SingleOrDefaultAsync(ct);
            if (row is null) return new(name, false, "The restored database has no deployment-settings record, so the settings the manifest asks you to apply cannot be confirmed against its data.");
            var differences = DeploymentInvariantStore.AsSettings(row, recorded.Currency).DifferencesFrom(recorded);
            return differences.Count == 0
                ? new(name, true, "The restored database's recorded deployment settings equal the backup manifest's.")
                : new(name, false, $"The restored database records different deployment settings than the backup manifest ({string.Join(", ", differences.Select(d => d.Setting))}); the backup is internally contradictory.");
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return new(name, false, "The restored database has no readable deployment-settings record.");
        }
    }

    /// <summary>Drops the isolated restore database and deletes its directory (only ever the target this drill created).</summary>
    public async Task<RestoreDrillRecord> RemoveTargetAsync(Guid drillId, Guid actor, CancellationToken ct)
    {
        var drill = await db.RestoreDrills.SingleOrDefaultAsync(d => d.Id == drillId, ct) ?? throw new BackupException("not_found", "Restore drill not found.", 404);
        await SafeRemoveTargetAsync(drill, ct);
        AuditService.Record(db, BackupAuditEvents.RestoreTargetRemoved, nameof(RestoreDrillRecord), drill.Id, actor, "Isolated restore target removed.");
        await db.SaveChangesAsync(ct);
        return drill;
    }

    private async Task SafeRemoveTargetAsync(RestoreDrillRecord drill, CancellationToken ct)
    {
        if (drill.TargetDatabase is not null && drill.TargetDatabase == TargetDatabaseName(drill.Id))
        {
            try { await restoreProvider.DropDatabaseAsync(drill.TargetDatabase, ct); } catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException) { /* nothing to drop / already gone */ }
        }
        if (drill.TargetDirectory is not null && drill.TargetDirectory == Path.Combine(paths.RestoreRoot, drill.Id.ToString("N")))
            BackupService.TryDeleteDirectory(drill.TargetDirectory);
        drill.TargetRemoved = true;
    }

    public async Task<IReadOnlyList<RestoreDrillRecord>> DrillsAsync(int take, CancellationToken ct) =>
        await db.RestoreDrills.AsNoTracking().OrderByDescending(d => d.StartedAtUtc).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);

    private async Task NotifyFailureAsync(string kind, string message, Guid? backupId, CancellationToken ct)
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
            row.Delivery = BackupNotificationDelivery.Failed;
            row.DeliveryFailureCode = "delivery_failed";
        }
        await db.SaveChangesAsync(ct);
    }
}
