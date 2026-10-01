using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>A notifier whose delivery can be switched to fail, to prove delivery trouble never changes backup status.</summary>
public sealed class TogglableNotifier : IBackupNotifier
{
    public bool Fail { get; set; }
    public List<BackupNotification> Delivered { get; } = [];

    public Task DeliverAsync(BackupNotification notification, CancellationToken cancellationToken)
    {
        if (Fail) throw new IOException("notification channel down");
        Delivered.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>An asset source whose staging can be made to fail (disk full, I/O error) to exercise failure paths.</summary>
public sealed class FailingAssetSource(Exception exception) : IBackupAssetSource
{
    public string AssetClass => "failing";
    public Task<IReadOnlyList<StagedFile>> StageAsync(string stagingDirectory, CancellationToken cancellationToken) => throw exception;
}

/// <summary>Delegates to the real SQL Server provider but fails the actual restore step (an interrupted restore).</summary>
public sealed class FailingRestoreProvider(IDatabaseRestoreProvider inner) : IDatabaseRestoreProvider
{
    public Task<bool> VerifyBackupAsync(string backupFilePath, CancellationToken ct) => inner.VerifyBackupAsync(backupFilePath, ct);
    public Task RestoreToNewDatabaseAsync(string backupFilePath, string targetDatabase, string targetDirectory, CancellationToken ct) => throw new IOException("disk failure during restore");
    public Task DropDatabaseAsync(string databaseName, CancellationToken ct) => inner.DropDatabaseAsync(databaseName, ct);
    public Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken ct) => inner.DatabaseExistsAsync(databaseName, ct);
    public string ConnectionStringFor(string databaseName) => inner.ConnectionStringFor(databaseName);
}

/// <summary>
/// A complete, isolated backup environment against a REAL SQL Server (LocalDB) database: its own temp
/// folders for destination/staging/restore/notifications/documents/keys, a real recovery key pair, and
/// real services. Seeds representative data for every managed asset class.
/// </summary>
public sealed class BackupTestEnvironment : IDisposable
{
    public const string Passphrase = "correct horse battery staple 42";
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"alveara-backup-tests-{Guid.NewGuid():N}");
    public string SetsDirectory => Path.Combine(Root, "sets");
    public string BlobRoot => Path.Combine(Root, "blobs");
    public string KeysRoot => Path.Combine(Root, "keys");
    public BackupPaths Paths { get; }
    public TestDatabaseFixture Fixture { get; }
    public TogglableNotifier Notifier { get; } = new();
    public IDatabaseRestoreProvider RestoreProvider { get; }
    public List<IBackupAssetSource> Sources { get; }
    public IDataProtectionProvider DataProtection { get; }
    public RecoveryKeyMaterial Key { get; private set; } = null!;

    public BackupTestEnvironment(TestDatabaseFixture fixture)
    {
        Fixture = fixture;
        foreach (var d in new[] { SetsDirectory, BlobRoot, KeysRoot }) Directory.CreateDirectory(d);
        var databaseName = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        Paths = new BackupPaths(SetsDirectory, Path.Combine(Root, "staging"), Path.Combine(Root, "restore"), Path.Combine(Root, "notifications"), fixture.ConnectionString, databaseName);
        RestoreProvider = new SqlServerRestoreProvider(fixture.ConnectionString);
        Sources =
        [
            new FileTreeAssetSource(ManagedAssetClasses.Documents, BlobRoot, rel => rel.Contains(".tmp-", StringComparison.Ordinal)),
            new FileTreeAssetSource(ManagedAssetClasses.DataProtectionKeys, KeysRoot),
        ];
        DataProtection = DataProtectionProvider.Create(new DirectoryInfo(KeysRoot), o => o.SetApplicationName(BackupConstants.DataProtectionApplicationName));
    }

    public AlveraDbContext NewDb() => Fixture.CreateContext();

    public BackupService NewBackupService(AlveraDbContext db, IBackupNotifier? notifier = null, IEnumerable<IBackupAssetSource>? sources = null) =>
        new(db, Paths, new SqlServerBackupSnapshotProvider(Fixture.ConnectionString, Paths.DatabaseName), sources ?? Sources, notifier ?? Notifier);

    public BackupRestoreService NewRestoreService(AlveraDbContext db, IDatabaseRestoreProvider? provider = null, IBackupNotifier? notifier = null) =>
        new(db, Paths, provider ?? RestoreProvider, notifier ?? Notifier);

    /// <summary>Starts from a clean slate for backup bookkeeping (history, drills, notifications, settings) so tests do not see each other's rows.</summary>
    public async Task ResetBackupStateAsync()
    {
        await using var db = NewDb();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BackupNotifications; DELETE FROM RestoreDrills; DELETE FROM BackupRecords; DELETE FROM BackupSettings; DELETE FROM BackgroundJobEffectReceipts; DELETE FROM BackgroundJobs; UPDATE UserAccounts SET MfaSecretProtected = NULL;"); // secrets from other tests were protected with ANOTHER test's key ring
        foreach (var f in Directory.EnumerateFiles(SetsDirectory)) File.Delete(f);
    }

    /// <summary>Generates the recovery key and stores its public half, exactly as an administrator would.</summary>
    public async Task<RecoveryKeyMaterial> ConfigureRecoveryKeyAsync(bool replace = false)
    {
        await using var db = NewDb();
        var privatePem = await NewBackupService(db).ConfigureRecoveryKeyAsync(Passphrase, replace, Guid.NewGuid(), default);
        var settings = await db.BackupSettings.AsNoTracking().SingleAsync();
        Key = new RecoveryKeyMaterial(settings.RecoveryPublicKeyPem!, privatePem, settings.RecoveryKeyFingerprint!);
        return Key;
    }

    /// <summary>One admin account with an MFA secret protected by the keys directory's real Data Protection keys, plus some audit history.</summary>
    public async Task<(Guid AccountId, string Username, string ProtectedSecret)> SeedAccountsAndAuditAsync()
    {
        await using var db = NewDb();
        var accounts = new AccountService(db, DataProtection);
        var admin = await accounts.RegisterAsync($"backup-user-{Guid.NewGuid():N}", "bootstrap-admin-password"); // bootstrap is one-time per database; a plain account is enough to test restore
        var protectedSecret = DataProtection.CreateProtector(AccountService.MfaSecretProtectorPurpose).Protect(Convert.ToBase64String(new byte[20]));
        await db.UserAccounts.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.MfaSecretProtected, protectedSecret));
        db.StaffProfiles.Add(new StaffProfile { Id = Guid.NewGuid(), DisplayName = $"Backup Staff {Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (admin.Id, admin.Username, protectedSecret);
    }

    /// <summary>Stores real blobs through the real blob storage and returns their ids and hashes.</summary>
    public async Task<List<StoredBlob>> SeedBlobsAsync(int count = 3, int sizeBytes = 200_000)
    {
        var storage = new LocalDiskBlobStorage(BlobRoot);
        var stored = new List<StoredBlob>();
        for (var i = 0; i < count; i++)
        {
            var content = new byte[sizeBytes + i * 1000];
            Random.Shared.NextBytes(content);
            stored.Add(await storage.StoreAsync(new MemoryStream(content)));
        }
        return stored;
    }

    // ---------- crafting an authentic-but-altered backup (for missing/tampered/incompatible tests) ----------

    /// <summary>
    /// Decrypts an existing backup with the real key, lets the caller alter the extracted contents, then
    /// re-zips and re-encrypts it for the same recovery key and registers it as a new Succeeded history
    /// row. The result authenticates perfectly - only its CONTENTS are wrong - which is exactly what a
    /// restore's inventory/compatibility checks must catch.
    /// </summary>
    public async Task<Guid> CraftAlteredBackupAsync(Guid sourceRecordId, Action<string> alterExtractedContent)
    {
        await using var db = NewDb();
        var source = await db.BackupRecords.AsNoTracking().SingleAsync(r => r.Id == sourceRecordId);
        var work = Path.Combine(Root, $"craft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, "set.zip");
        await using (var input = File.OpenRead(BackupService.ResolveBackupPath(source)))
        await using (var output = File.Create(zip))
            await BackupCrypto.DecryptAsync(input, output, Key.EncryptedPrivateKeyPem, Passphrase);
        var content = Path.Combine(work, "content");
        ZipFile.ExtractToDirectory(zip, content);
        File.Delete(zip);

        alterExtractedContent(content);

        var newZip = Path.Combine(work, "new.zip");
        ZipFile.CreateFromDirectory(content, newZip, CompressionLevel.Optimal, includeBaseDirectory: false);
        var id = Guid.NewGuid();
        var fileName = $"crafted-{id:N}.abk";
        var path = Path.Combine(SetsDirectory, fileName);
        await using (var zin = File.OpenRead(newZip))
        await using (var o = File.Create(path))
            await BackupCrypto.EncryptAsync(zin, o, Key.PublicKeyPem);
        Directory.Delete(work, recursive: true);

        db.BackupRecords.Add(new BackupRecord
        {
            Id = id, IdempotencyKey = $"crafted:{id:N}", Kind = BackupKind.Manual, Status = BackupStatus.Succeeded,
            StartedAtUtc = DateTimeOffset.UtcNow, CompletedAtUtc = DateTimeOffset.UtcNow, DestinationDirectory = SetsDirectory, FileName = fileName,
            SizeBytes = new FileInfo(path).Length, Sha256 = await FileTreeAssetSource.HashFileAsync(path, default),
            IncludedAssetClasses = source.IncludedAssetClasses, SchemaMigration = source.SchemaMigration, AppVersion = source.AppVersion, RecoveryKeyFingerprint = Key.Fingerprint,
        });
        await db.SaveChangesAsync();
        return id;
    }

    public static BackupManifest ReadManifest(string contentRoot) =>
        JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(contentRoot, BackupManifest.FileName)), BackupManifest.Json)!;

    public static void WriteManifest(string contentRoot, BackupManifest manifest) =>
        File.WriteAllText(Path.Combine(contentRoot, BackupManifest.FileName), JsonSerializer.Serialize(manifest, BackupManifest.Json));

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch (IOException) { /* temp folder; best effort */ }
    }
}
