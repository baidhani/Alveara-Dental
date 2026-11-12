using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.LoginPresentation;

/// <summary>
/// ALV-N020-C01 (review correction 6): a restore check that every AVAILABLE administrator-added background in the restored database has its derivative in the
/// backup's <see cref="ManagedAssetClasses.LoginBackgrounds"/> component with the recorded size and hash. Unavailable ones are not shown, so a missing file for
/// one of them does not block a restore. An older database without the table passes ("nothing to check").
/// </summary>
public static class LoginBackgroundRestoreCheck
{
    public const string Name = "restored_login_backgrounds_have_their_files";

    public static async Task<ValidationCheck> RunAsync(IReadOnlyList<BackupManifestComponent> components, AlveraDbContext restoredDb, CancellationToken ct)
    {
        try
        {
            var images = await restoredDb.LoginBackgroundImages.AsNoTracking().Where(i => i.IsActive)
                .Select(i => new { i.Id, i.StorageName, i.Sha256, i.SizeBytes }).ToListAsync(ct);
            var files = components.Where(c => c.AssetClass == ManagedAssetClasses.LoginBackgrounds).ToDictionary(c => c.Path, StringComparer.OrdinalIgnoreCase);
            var bad = images.Where(i => !files.TryGetValue($"{ManagedAssetClasses.LoginBackgrounds}/{i.StorageName}", out var file)
                                        || !string.Equals(file.Sha256, i.Sha256, StringComparison.OrdinalIgnoreCase) || file.SizeBytes != i.SizeBytes).Select(i => i.Id).ToList();
            if (bad.Count == 0)
                return new(Name, true, images.Count == 0 ? "There are no added sign-in backgrounds to check." : $"All {images.Count} available added sign-in background(s) have their file in the backup with the recorded size and hash.");
            return new(Name, false, $"{bad.Count} available added sign-in background(s) have no file in the backup, or a file that differs from the recorded size or hash (ids: {string.Join(", ", bad.Take(5))}{(bad.Count > 5 ? ", …" : "")}).");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208)
        {
            return new(Name, true, "The restored database predates sign-in backgrounds, so there is nothing to check.");
        }
    }
}
