namespace Alveara.Api.Architecture.LoginPresentation;

/// <summary>One background bundled with the application (served by the client bundle, same origin). <see cref="TileAllowed"/> comes from the tile-readiness gate.</summary>
public sealed record BundledBackground(string Key, string DisplayName, bool TileAllowed, string SuggestedDisplayMode);

/// <summary>
/// ALV-N020-C01: the five owner-supplied backgrounds bundled with the application. Bundled keys cannot be inactivated. The client holds the matching asset map
/// (src/alveara-client/src/pages/login/loginBackgrounds.ts); an architecture test keeps the two lists equal.
/// </summary>
public static class LoginBackgroundCatalog
{
    public const string DefaultKey = "teal-doodle";

    public static readonly IReadOnlyList<BundledBackground> Bundled =
    [
        new("teal-doodle", "Teal doodle", true, LoginDisplayModes.Tile),
        new("blue-doodle", "Blue doodle", true, LoginDisplayModes.Tile),
        new("blue-operatory", "Blue operatory", true, LoginDisplayModes.Tile),
        new("violet-operatory", "Violet operatory", true, LoginDisplayModes.Tile),
        new("neon-doodle", "Neon doodle", true, LoginDisplayModes.Tile),
    ];

    public static BundledBackground? Find(string? key) => Bundled.FirstOrDefault(b => b.Key == key);

    public static BundledBackground Default => Find(DefaultKey)!;
}
