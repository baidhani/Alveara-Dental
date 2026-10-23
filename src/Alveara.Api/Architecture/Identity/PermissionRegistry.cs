using System.Reflection;
using System.Text.Json;

namespace Alveara.Api.Architecture.Identity;

public enum PermissionStatus { Active, Retired }

/// <summary>
/// ALV-N017: the committed registry of every permission name ever shipped (<c>PermissionRegistry.json</c>, embedded). Stored policy rows name a permission by its stable name; the registry says whether a name is
/// a current permission (<see cref="PermissionStatus.Active"/>), a removed one whose rows are harmless and removable (<see cref="PermissionStatus.Retired"/>) or unknown (a stored row naming it is invalid data and
/// never restores access). <c>PermissionRegistryTests</c> fails when the enum and the registry disagree, so a permission cannot be removed or renamed silently.
/// </summary>
public static class PermissionRegistry
{
    private sealed record Entry(string Name, string Status);
    private sealed record Document(List<Entry> Permissions);

    private static readonly Lazy<IReadOnlyDictionary<string, PermissionStatus>> Loaded = new(Load);

    public static IReadOnlyDictionary<string, PermissionStatus> Entries => Loaded.Value;

    public static bool IsActive(string name) => Entries.TryGetValue(name, out var s) && s == PermissionStatus.Active;

    public static bool IsRetired(string name) => Entries.TryGetValue(name, out var s) && s == PermissionStatus.Retired;

    public static bool IsKnown(string name) => Entries.ContainsKey(name);

    private static IReadOnlyDictionary<string, PermissionStatus> Load()
    {
        using var stream = typeof(PermissionRegistry).Assembly.GetManifestResourceStream("Alveara.Api.PermissionRegistry.json")
            ?? throw new InvalidOperationException("The permission registry resource is missing from the assembly.");
        var doc = JsonSerializer.Deserialize<Document>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The permission registry could not be read.");
        var result = new Dictionary<string, PermissionStatus>(StringComparer.Ordinal);
        foreach (var e in doc.Permissions)
        {
            if (!Enum.TryParse<PermissionStatus>(e.Status, out var status) || !result.TryAdd(e.Name, status))
                throw new InvalidOperationException("The permission registry has a bad or duplicate entry.");
        }
        return result;
    }
}
