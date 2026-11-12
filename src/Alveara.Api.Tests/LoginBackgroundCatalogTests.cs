using System.Text.RegularExpressions;
using Alveara.Api.Architecture.LoginPresentation;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020-C01: the server's bundled catalog and the client's asset map list the same keys in the same order (the first is the deployment default), and every
/// bundled asset file exists, carries no metadata (no JPEG APP1/APP13, no PNG text or eXIf chunk) and stays within its size target.
/// </summary>
public class LoginBackgroundCatalogTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root (the folder with CLAUDE.md) was not found above the test output.");
    }

    private static string ClientLogin => Path.Combine(Root(), "src", "alveara-client", "src", "pages", "login", "loginBackgrounds.ts");
    private static string Assets => Path.Combine(Root(), "src", "alveara-client", "src", "assets", "login");

    [Fact]
    public void The_client_asset_map_lists_exactly_the_server_catalog_keys_in_order()
    {
        var source = File.ReadAllText(ClientLogin);
        var block = source[source.IndexOf("BUNDLED_BACKGROUNDS", StringComparison.Ordinal)..];
        var clientKeys = Regex.Matches(block, "\\{ key: \"([a-z0-9-]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(LoginBackgroundCatalog.Bundled.Select(b => b.Key), clientKeys);
        Assert.Equal(LoginBackgroundCatalog.DefaultKey, clientKeys[0]);
    }

    [Fact]
    public void Every_bundled_tile_exists_carries_no_metadata_and_is_within_its_size_target()
    {
        foreach (var bundled in LoginBackgroundCatalog.Bundled)
        {
            var path = Path.Combine(Assets, $"tile-{bundled.Key}.jpg");
            Assert.True(File.Exists(path), $"{bundled.Key}: missing asset");
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length <= 250_000, $"{bundled.Key}: {bytes.Length} bytes is over the 250 KB target");
            var markers = LoginTestImages.JpegHeaderMarkers(bytes);
            Assert.DoesNotContain((byte)0xE1, markers);                                                     // EXIF / XMP
            Assert.DoesNotContain((byte)0xED, markers);                                                     // IPTC
        }
    }

    [Fact]
    public void The_bundled_logo_carries_no_metadata_chunks()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Assets, "dentalkeep-logo.png"));
        var chunks = LoginTestImages.PngChunkTypes(bytes);
        foreach (var forbidden in new[] { "tEXt", "iTXt", "zTXt", "eXIf", "tIME" }) Assert.DoesNotContain(forbidden, chunks);
        Assert.True(bytes.Length <= 60_000, $"logo is {bytes.Length} bytes");
    }
}
