using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N004 verification, restore and recovery-drill tests: a REAL SQL Server restore into an
/// isolated target, post-restore validation of every persistent asset class, corrupt / incompatible /
/// wrong-key rejection, safety (restore never touches live data), and a full-state drill that starts
/// the recovered application against the restored database, documents and keys.
/// </summary>
public class BackupRestoreTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly BackupTestEnvironment _env;
    private readonly Guid _actor = Guid.NewGuid();
    private readonly List<string> _restoredDatabases = [];

    public BackupRestoreTests(TestDatabaseFixture fixture)
    {
        _env = new BackupTestEnvironment(fixture);
        _env.ResetBackupStateAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        foreach (var name in _restoredDatabases) _env.RestoreProvider.DropDatabaseAsync(name, default).GetAwaiter().GetResult();
        _env.Dispose();
    }

    private async Task<(BackupRecord Record, Guid AccountId, string Username, string ProtectedSecret, List<StoredBlob> Blobs)> SeededBackupAsync()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var (accountId, username, secret) = await _env.SeedAccountsAndAuditAsync();
        var blobs = await _env.SeedBlobsAsync(3, 600_000);
        await using var db = _env.NewDb();
        var record = await _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", _actor, default);
        return (record, accountId, username, secret, blobs);
    }

    private async Task<RestoreDrillRecord> DrillAsync(Guid recordId, string? privateKey = null, string? passphrase = null, IDatabaseRestoreProvider? provider = null)
    {
        await using var db = _env.NewDb();
        var drill = await _env.NewRestoreService(db, provider).RestoreDrillAsync(recordId, privateKey ?? _env.Key.EncryptedPrivateKeyPem, passphrase ?? BackupTestEnvironment.Passphrase, _actor, default);
        if (drill.TargetDatabase is not null) _restoredDatabases.Add(drill.TargetDatabase);
        return drill;
    }

    private static IReadOnlyList<ValidationCheck> ChecksOf(RestoreDrillRecord drill) =>
        System.Text.Json.JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;

    // ---------- preflight ----------

    [Fact]
    public async Task Preflight_confirms_the_backup_file_hash_and_that_the_supplied_recovery_material_matches()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        await using var db = _env.NewDb();
        var result = await _env.NewRestoreService(db).PreflightAsync(record.Id, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, default);

        Assert.True(result.CanRestore);
        Assert.All(result.Checks, c => Assert.True(c.Passed, c.Name + ": " + c.Detail));
        Assert.Contains(result.Checks, c => c.Name == "recovery_material_matches");
        Assert.Contains(result.Checks, c => c.Name == "covers_all_asset_classes");
    }

    [Fact]
    public async Task Preflight_flags_missing_wrong_and_foreign_recovery_material_without_restoring_anything()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        await using var db = _env.NewDb();
        var service = _env.NewRestoreService(db);

        var missing = await service.PreflightAsync(record.Id, "", "", default);
        Assert.False(missing.CanRestore);
        Assert.Contains(missing.Checks, c => c.Name == "recovery_material_present" && !c.Passed);

        var wrongPassphrase = await service.PreflightAsync(record.Id, _env.Key.EncryptedPrivateKeyPem, "an entirely wrong passphrase", default);
        Assert.Contains(wrongPassphrase.Checks, c => c.Name == "recovery_material_matches" && !c.Passed);

        var foreign = BackupCrypto.GenerateRecoveryKey(BackupTestEnvironment.Passphrase);
        var foreignKey = await service.PreflightAsync(record.Id, foreign.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, default);
        Assert.False(foreignKey.CanRestore);
        Assert.Contains(foreignKey.Checks, c => c.Name == "recovery_material_matches" && c.Detail.Contains("does not belong"));
        Assert.Empty(await db.RestoreDrills.ToListAsync()); // preflight restores nothing
    }

    [Fact]
    public async Task Preflight_detects_a_changed_or_missing_backup_file()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var path = BackupService.ResolveBackupPath(record);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[200] ^= 0x10;
        await File.WriteAllBytesAsync(path, bytes);

        await using var db = _env.NewDb();
        var corrupt = await _env.NewRestoreService(db).PreflightAsync(record.Id, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, default);
        Assert.False(corrupt.CanRestore);
        Assert.Contains(corrupt.Checks, c => c.Name == "file_hash_matches" && !c.Passed);

        File.Delete(path);
        var missing = await _env.NewRestoreService(db).PreflightAsync(record.Id, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, default);
        Assert.Contains(missing.Checks, c => c.Name == "backup_file_present" && !c.Passed);
    }

    // ---------- full verification ----------

    [Fact]
    public async Task Full_verification_with_the_recovery_key_proves_the_backup_is_restorable_without_restoring_it()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        await using var db = _env.NewDb();

        var verified = await _env.NewRestoreService(db).VerifyFullAsync(record.Id, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, _actor, default);

        Assert.Equal(BackupVerificationStatus.FullyVerified, verified.VerificationStatus);
        Assert.Null(verified.VerificationFailureCode);
        Assert.Contains(await db.AuditLogEntries.AsNoTracking().ToListAsync(), a => a.EventType == BackupAuditEvents.FullyVerified && a.TargetUserAccountId == record.Id);
        Assert.Empty(await db.RestoreDrills.ToListAsync());                               // nothing restored
        Assert.Empty(Directory.EnumerateDirectories(_env.Paths.StagingRoot));            // scratch cleaned
    }

    [Fact]
    public async Task Full_verification_with_missing_or_wrong_recovery_material_fails_visibly_and_safely()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        await using var db = _env.NewDb();
        var service = _env.NewRestoreService(db);

        var wrong = await service.VerifyFullAsync(record.Id, _env.Key.EncryptedPrivateKeyPem, "wrong passphrase value", _actor, default);
        Assert.Equal((BackupVerificationStatus.VerificationFailed, "wrong_recovery_key"), (wrong.VerificationStatus, wrong.VerificationFailureCode));

        var blank = await service.VerifyFullAsync(record.Id, "", "", _actor, default);
        Assert.Equal("wrong_recovery_key", blank.VerificationFailureCode);

        Assert.Contains(_env.Notifier.Delivered, n => n.Kind == "BackupVerificationFailed");
        var status = await _env.NewBackupService(db).GetStatusAsync(default);
        Assert.Equal(0, status.SuccessfulVerificationCount); // a failed verification never counts toward confidence
    }

    // ---------- restore drill ----------

    [Fact]
    public async Task A_restore_drill_restores_into_an_isolated_target_validates_every_asset_class_and_never_touches_live_data()
    {
        var (record, accountId, username, protectedSecret, blobs) = await SeededBackupAsync();
        await using var liveBefore = _env.NewDb();
        var liveAccounts = await liveBefore.UserAccounts.CountAsync();
        var liveAudits = await liveBefore.AuditLogEntries.CountAsync();

        var drill = await DrillAsync(record.Id);

        Assert.True(drill.Outcome == "Succeeded", $"{drill.FailureCode}: {drill.FailureMessage} | " + string.Join(" ; ", ChecksOf(drill).Where(c => !c.Passed).Select(c => c.Name + "=" + c.Detail)));
        Assert.Null(drill.FailureCode);
        Assert.StartsWith("AlveraRestore_", drill.TargetDatabase);
        Assert.NotEqual(_env.Paths.DatabaseName, drill.TargetDatabase);
        Assert.True(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));

        var checks = ChecksOf(drill);
        foreach (var name in new[] { "format_supported", "schema_compatible", "documents_complete", "components_present", "component_hashes_match",
                                     "restored_schema_matches", "row_counts_match_snapshot_window", "representative_records_readable", "restored_keys_decrypt_restored_data", "covers_all_asset_classes" })
            Assert.True(checks.Single(c => c.Name == name).Passed, name + ": " + checks.Single(c => c.Name == name).Detail);
        Assert.Contains("decrypted 1 restored MFA secret", checks.Single(c => c.Name == "restored_keys_decrypt_restored_data").Detail);

        // The restored database really contains the seeded records.
        await using var restored = new Alveara.Api.Data.AlveraDbContext(new DbContextOptionsBuilder<Alveara.Api.Data.AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);
        Assert.Equal(username, (await restored.UserAccounts.SingleAsync(u => u.Id == accountId)).Username);
        Assert.Equal(protectedSecret, (await restored.UserAccounts.SingleAsync(u => u.Id == accountId)).MfaSecretProtected);

        // The restored documents are byte-identical (verified by the blob storage's own integrity check).
        var restoredDocs = new LocalDiskBlobStorage(Path.Combine(drill.TargetDirectory!, "extracted", ManagedAssetClasses.Documents));
        foreach (var blob in blobs) Assert.True(await restoredDocs.VerifyIntegrityAsync(blob.Id, blob.Sha256Hash));

        // Live data is exactly as it was: restore only ever writes to the isolated target.
        await using var liveAfter = _env.NewDb();
        Assert.Equal(liveAccounts, await liveAfter.UserAccounts.CountAsync());
        Assert.True(await liveAfter.AuditLogEntries.CountAsync() >= liveAudits);
        Assert.Equal(BackupVerificationStatus.FullyVerified, (await liveAfter.BackupRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id)).VerificationStatus);
        Assert.Contains(await liveAfter.AuditLogEntries.AsNoTracking().ToListAsync(), a => a.EventType == BackupAuditEvents.RestoreDrillCompleted && a.TargetUserAccountId == drill.Id);

        // Probation: a successful drill counts toward confidence.
        Assert.True((await _env.NewBackupService(liveAfter).GetStatusAsync(default)).SuccessfulVerificationCount >= 1);
    }

    [Fact]
    public async Task Removing_a_restore_target_drops_the_database_and_its_files_and_only_that_target()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var drill = await DrillAsync(record.Id);
        var liveName = _env.Paths.DatabaseName;

        await using var db = _env.NewDb();
        var removed = await _env.NewRestoreService(db).RemoveTargetAsync(drill.Id, _actor, default);

        Assert.True(removed.TargetRemoved);
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
        Assert.False(Directory.Exists(drill.TargetDirectory));
        Assert.True(await _env.RestoreProvider.DatabaseExistsAsync(liveName, default)); // the live database is untouched
    }

    [Fact]
    public async Task The_restore_provider_can_never_overwrite_an_existing_database_including_the_live_one()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var dbFile = Path.Combine(_env.Root, "overwrite-attempt.bak");
        await new SqlServerBackupSnapshotProvider(_env.Fixture.ConnectionString, _env.Paths.DatabaseName).CreateSnapshotAsync(dbFile);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _env.RestoreProvider.RestoreToNewDatabaseAsync(dbFile, _env.Paths.DatabaseName, Path.Combine(_env.Root, "x"), default));
        Assert.Throws<ArgumentException>(() => _env.RestoreProvider.RestoreToNewDatabaseAsync(dbFile, "live; DROP DATABASE master", Path.Combine(_env.Root, "y"), default).GetAwaiter().GetResult());
        Assert.NotNull(record);
    }

    // ---------- failure paths ----------

    [Fact]
    public async Task A_drill_with_the_wrong_recovery_material_fails_with_its_own_code_creates_no_target_and_notifies()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();

        var wrongPass = await DrillAsync(record.Id, passphrase: "this is not the passphrase");
        Assert.Equal(("Failed", "wrong_recovery_key"), (wrongPass.Outcome, wrongPass.FailureCode));

        var foreign = BackupCrypto.GenerateRecoveryKey(BackupTestEnvironment.Passphrase);
        var wrongKey = await DrillAsync(record.Id, foreign.EncryptedPrivateKeyPem);
        Assert.Equal("wrong_recovery_key", wrongKey.FailureCode);

        foreach (var d in new[] { wrongPass, wrongKey })
        {
            Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(d.TargetDatabase!, default)); // nothing half-restored
            Assert.False(Directory.Exists(d.TargetDirectory));
            Assert.True(d.TargetRemoved);
        }
        Assert.Contains(_env.Notifier.Delivered, n => n.Kind == "RestoreDrillFailed");
        await using var db = _env.NewDb();
        Assert.Equal(0, (await _env.NewBackupService(db).GetStatusAsync(default)).SuccessfulVerificationCount);
    }

    [Fact]
    public async Task A_corrupt_or_altered_backup_file_is_rejected_before_anything_is_restored()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var path = BackupService.ResolveBackupPath(record);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[bytes.Length / 3] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);

        var drill = await DrillAsync(record.Id);

        Assert.Equal(("Failed", "hash_mismatch"), (drill.Outcome, drill.FailureCode));
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
    }

    [Fact]
    public async Task A_validly_encrypted_backup_that_was_altered_after_creation_is_still_detected_by_authentication()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var path = BackupService.ResolveBackupPath(record);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[bytes.Length / 2] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes);
        var hiddenHash = await FileTreeAssetSource.HashFileAsync(path, default);
        await using (var db = _env.NewDb())
            await db.BackupRecords.Where(r => r.Id == record.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Sha256, hiddenHash)); // hash "updated" to hide it

        var drill = await DrillAsync(record.Id);

        Assert.Equal(("Failed", "corrupt_or_tampered"), (drill.Outcome, drill.FailureCode)); // AES-GCM authentication catches it even when the hash was forged
    }

    [Fact]
    public async Task A_document_missing_from_the_backup_set_fails_the_restore_and_names_the_problem()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var crafted = await _env.CraftAlteredBackupAsync(record.Id, content =>
            File.Delete(Directory.EnumerateFiles(Path.Combine(content, ManagedAssetClasses.Documents)).First())); // manifest still lists it

        var drill = await DrillAsync(crafted);

        Assert.Equal(("Failed", "documents_complete"), (drill.Outcome, drill.FailureCode));
        Assert.Contains("missing from the backup set", drill.FailureMessage);
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
        Assert.False(ChecksOf(drill).Single(c => c.Name == "documents_complete").Passed);
    }

    [Fact]
    public async Task A_component_whose_content_no_longer_matches_the_manifest_hash_fails_the_restore()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var crafted = await _env.CraftAlteredBackupAsync(record.Id, content =>
            File.AppendAllText(Directory.EnumerateFiles(Path.Combine(content, ManagedAssetClasses.Documents)).First(), "tampered"));

        var drill = await DrillAsync(crafted);

        Assert.Equal(("Failed", "component_hashes_match"), (drill.Outcome, drill.FailureCode));
    }

    [Theory]
    [InlineData("future-schema")]
    [InlineData("future-format")]
    public async Task A_backup_from_an_incompatible_schema_or_format_is_rejected_before_any_restore(string kind)
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var crafted = await _env.CraftAlteredBackupAsync(record.Id, content =>
        {
            var manifest = BackupTestEnvironment.ReadManifest(content);
            BackupTestEnvironment.WriteManifest(content, kind == "future-schema"
                ? manifest with { SchemaMigration = "99991231235959_FromTheFuture" }
                : manifest with { FormatVersion = 99 });
        });

        var drill = await DrillAsync(crafted);

        Assert.Equal(("Failed", "incompatible_backup"), (drill.Outcome, drill.FailureCode));
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
    }

    [Fact]
    public async Task A_manifest_path_that_climbs_out_of_the_extracted_set_is_never_read_and_fails_the_restore()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var crafted = await _env.CraftAlteredBackupAsync(record.Id, content =>
        {
            var manifest = BackupTestEnvironment.ReadManifest(content);
            BackupTestEnvironment.WriteManifest(content, manifest with
            {
                Components = manifest.Components.Append(new BackupManifestComponent(ManagedAssetClasses.DataProtectionKeys, "../../outside.txt", "00", 1)).ToList(),
            });
        });

        var drill = await DrillAsync(crafted);

        Assert.Equal(("Failed", "components_present"), (drill.Outcome, drill.FailureCode));
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
    }

    [Fact]
    public async Task A_damaged_database_inside_an_authentic_backup_is_caught_by_SQL_Servers_own_media_check()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var crafted = await _env.CraftAlteredBackupAsync(record.Id, content =>
        {
            var bak = Path.Combine(content, "database", "alveara.bak");
            var bytes = File.ReadAllBytes(bak);
            foreach (var start in new[] { bytes.Length / 4, bytes.Length / 2, bytes.Length * 3 / 4 })
                for (var i = start; i < start + 65536 && i < bytes.Length; i++) bytes[i] ^= 0xFF; // corrupt pages across the media
            File.WriteAllBytes(bak, bytes);
            var manifest = BackupTestEnvironment.ReadManifest(content);
            var fixedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            BackupTestEnvironment.WriteManifest(content, manifest with
            {
                Components = manifest.Components.Select(c => c.Path == BackupManifest.DatabaseComponentPath ? c with { Sha256 = fixedHash } : c).ToList(),
            }); // the manifest hash is "fixed up", so only SQL Server's checksum can notice
        });

        var drill = await DrillAsync(crafted);

        Assert.Equal(("Failed", "database_backup_damaged"), (drill.Outcome, drill.FailureCode));
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
    }

    [Fact]
    public async Task A_restore_interrupted_mid_way_leaves_no_half_restored_database_or_directory()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();

        var drill = await DrillAsync(record.Id, provider: new FailingRestoreProvider(_env.RestoreProvider));

        Assert.Equal(("Failed", "io_error"), (drill.Outcome, drill.FailureCode));
        Assert.False(Directory.Exists(drill.TargetDirectory));
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
    }

    [Fact]
    public async Task Restored_keys_that_cannot_decrypt_the_restored_secrets_fail_validation_because_accounts_would_lose_their_second_factor()
    {
        var (record, _, _, _, _) = await SeededBackupAsync();
        var crafted = await _env.CraftAlteredBackupAsync(record.Id, content =>
        {
            // Replace the real keys with an unrelated set, and keep the manifest consistent so ONLY the key/data mismatch remains.
            var keysDir = Path.Combine(content, ManagedAssetClasses.DataProtectionKeys);
            foreach (var f in Directory.EnumerateFiles(keysDir)) File.Delete(f);
            var otherKeys = DataProtectionProvider.Create(new DirectoryInfo(keysDir), o => o.SetApplicationName(BackupTestEnvironment.DataProtectionApplicationName));
            otherKeys.CreateProtector("anything").Protect("force key generation");
            var manifest = BackupTestEnvironment.ReadManifest(content);
            var components = manifest.Components.Where(c => c.AssetClass != ManagedAssetClasses.DataProtectionKeys).Concat(
                Directory.EnumerateFiles(keysDir).Select(f => new BackupManifestComponent(ManagedAssetClasses.DataProtectionKeys, $"{ManagedAssetClasses.DataProtectionKeys}/{Path.GetFileName(f)}",
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))), new FileInfo(f).Length))).ToList();
            BackupTestEnvironment.WriteManifest(content, manifest with { Components = components });
        });

        var drill = await DrillAsync(crafted);

        Assert.Equal(("Failed", "restore_validation_failed"), (drill.Outcome, drill.FailureCode));
        Assert.False(ChecksOf(drill).Single(c => c.Name == "restored_keys_decrypt_restored_data").Passed);
    }

    // ---------- the full-state drill: start the recovered application ----------

    [Fact]
    public async Task Full_state_drill_the_recovered_application_starts_and_serves_the_restored_records_documents_and_keys()
    {
        var (record, accountId, username, protectedSecret, blobs) = await SeededBackupAsync();
        // A known login to prove the recovered app authenticates against restored data.
        const string password = "recovered-app-password-1";
        await using (var seed = _env.NewDb())
        {
            var accounts = new AccountService(seed, _env.DataProtection);
            var loginUser = $"drill-login-{Guid.NewGuid():N}";
            var created = await accounts.RegisterAsync(loginUser, password);
            await accounts.ChangeRoleAsync(created.Id, Role.Admin, accountId);
            await accounts.SetAccountEnabledAsync(created.Id, true, accountId);
            username = loginUser;
        }
        await using (var db = _env.NewDb())
            record = await _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", _actor, default); // a backup that includes the login account

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var extracted = Path.Combine(drill.TargetDirectory!, "extracted");

        // Boot the REAL application against the restored database, documents and keys - not the live ones.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!));
            builder.UseSetting("StorageRoot", Path.Combine(extracted, ManagedAssetClasses.Documents));
            builder.UseSetting("DataProtectionKeysPath", Path.Combine(extracted, ManagedAssetClasses.DataProtectionKeys));
            builder.UseSetting("Backup:Root", Path.Combine(_env.Root, "recovered-app-backup"));
            builder.UseSetting("DataProtection:ApplicationName", BackupTestEnvironment.DataProtectionApplicationName); // the recorded deployment setting, applied as the runbook says
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        });
        var client = factory.CreateClient();

        // 1. It authenticates a restored account and serves restored audit history.
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
        var permissions = await client.GetAsync("/api/auth/permissions");
        Assert.True(permissions.IsSuccessStatusCode);
        var audit = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/audit-log?take=500");
        Assert.Contains(audit.EnumerateArray(), e => e.GetProperty("eventType").GetString() == "AccountRegistered");

        // 2. Its own blob storage serves the restored documents with intact hashes.
        var storage = factory.Services.GetRequiredService<IBlobStorage>();
        foreach (var blob in blobs) Assert.True(await storage.VerifyIntegrityAsync(blob.Id, blob.Sha256Hash));

        // 3. Its own Data Protection (the restored keys) decrypts a restored MFA secret.
        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountService.MfaSecretProtectorPurpose);
        Assert.Equal(Convert.ToBase64String(new byte[20]), protector.Unprotect(protectedSecret));
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<Alveara.Api.Data.AlveraDbContext>().UserAccounts.SingleAsync(u => u.Id == accountId));
    }
}
