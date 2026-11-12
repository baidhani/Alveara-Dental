using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.LoginPresentation;

/// <summary>
/// ALV-N020-C01: the folder of administrator-added background derivatives (<c>StorageRoot/login-backgrounds</c>, its own backup asset class). Names are always
/// server-generated (<c>32 hex digits + .png|.jpg</c>) and checked again before any path is built, so no request value can ever reach the file system.
/// Writes go to a temporary file in the same folder and are moved into place, so a crash never leaves a half-written derivative under a real name.
/// </summary>
public sealed partial class LoginBackgroundStore(string root)
{
    public const string FolderName = "login-backgrounds";

    [GeneratedRegex("^[0-9a-f]{32}\\.(png|jpg)$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerName();

    public string Root => root;

    public static string NewStorageName(string extension) => $"{Guid.NewGuid():N}.{extension}";

    private string PathFor(string storageName)
    {
        if (!ServerName().IsMatch(storageName)) throw new InvalidOperationException("Not a server-generated background file name.");
        return Path.Combine(root, storageName);
    }

    public async Task SaveAsync(string storageName, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var final = PathFor(storageName);
        var temp = Path.Combine(root, $".tmp-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, final, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public bool Exists(string storageName) => File.Exists(PathFor(storageName));

    public Stream OpenRead(string storageName) => new FileStream(PathFor(storageName), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    /// <summary>Only for undoing a save whose database row could not be written (nothing references the file yet).</summary>
    public void DeleteUnreferenced(string storageName)
    {
        var path = PathFor(storageName);
        if (File.Exists(path)) File.Delete(path);
    }
}
