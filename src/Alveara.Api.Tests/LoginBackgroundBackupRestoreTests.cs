using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.LoginPresentation;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020-C01 (review correction 6), a real SQL Server backup and restore drill: an added sign-in background's derivative is in the backup's own
/// loginBackgrounds component, comes back byte for byte with the hash recorded at upload, and the restored database still names it; a backup that lacks the
/// file of an available background fails the restore validation instead of being trusted.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class LoginBackgroundBackupRestoreTests : SafetyTestBase
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

    private async Task<LoginBackgroundImage> AddBackgroundAsync()
    {
        await using var db = _env.NewDb();
        var service = new LoginPresentationService(db, new LoginBackgroundStore(_env.LoginBackgroundsRoot), new ConfigurationBuilder().Build(), NullLogger<LoginPresentationService>.Instance);
        var view = await service.AddImageAsync(LoginTestImages.Jpeg(900, 600), "front-desk.jpg", "Front desk", LoginDisplayModes.Fill, publicAcknowledged: true, S.Actor, default);
        return await db.LoginBackgroundImages.AsNoTracking().SingleAsync(i => i.Id == view.Id);
    }

    private async Task<RestoreDrillRecord> BackupAndDrillAsync()
    {
        BackupRecord record;
        await using (var db = _env.NewDb())
            record = await _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", S.Actor, default);
        await using var drillDb = _env.NewDb();
        var drill = await _env.NewRestoreService(drillDb).RestoreDrillAsync(record.Id, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, S.Actor, default);
        if (drill.TargetDatabase is not null) _restored.Add(drill.TargetDatabase);
        return drill;
    }

    private static ValidationCheck CheckOf(RestoreDrillRecord drill) =>
        JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!.Single(c => c.Name == LoginBackgroundRestoreCheck.Name);

    [Fact]
    public async Task An_added_background_survives_a_backup_and_a_restore_byte_for_byte()
    {
        var image = await AddBackgroundAsync();
        var original = await File.ReadAllBytesAsync(Path.Combine(_env.LoginBackgroundsRoot, image.StorageName));

        var drill = await BackupAndDrillAsync();
        File.Delete(Path.Combine(_env.LoginBackgroundsRoot, image.StorageName));                            // life goes on after the backup; the restore does not depend on it

        Assert.Equal("Succeeded", drill.Outcome);
        Assert.True(CheckOf(drill).Passed, CheckOf(drill).Detail);
        var restoredFile = Path.Combine(drill.TargetDirectory!, "extracted", ManagedAssetClasses.LoginBackgrounds, image.StorageName);
        var restoredBytes = await File.ReadAllBytesAsync(restoredFile);
        Assert.Equal(original, restoredBytes);
        Assert.Equal(image.Sha256, Convert.ToHexStringLower(SHA256.HashData(restoredBytes)));
        await using var restored = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);
        var row = await restored.LoginBackgroundImages.AsNoTracking().SingleAsync(i => i.Id == image.Id);
        Assert.Equal((image.StorageName, image.Sha256, image.SizeBytes, true), (row.StorageName, row.Sha256, row.SizeBytes, row.IsActive));
    }

    [Fact]
    public async Task A_backup_that_lacks_an_available_backgrounds_file_fails_the_restore_validation()
    {
        var image = await AddBackgroundAsync();
        File.Delete(Path.Combine(_env.LoginBackgroundsRoot, image.StorageName));                            // gone before the backup: the set does not contain it

        var drill = await BackupAndDrillAsync();
        Assert.Equal(("Failed", "restore_validation_failed"), (drill.Outcome, drill.FailureCode));
        var check = CheckOf(drill);
        Assert.False(check.Passed);
        Assert.Contains(image.Id.ToString(), check.Detail);
        Assert.DoesNotContain("front-desk", check.Detail);                                                  // ids and counts, never a file name
    }
}
