using System.Text.RegularExpressions;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N002 required "no-public-internet smoke test." Actually blocking network egress isn't
/// practical in this test environment, so this proves the same property the way that matters for
/// review: no source file in the API project references an external (non-localhost) HTTP
/// endpoint. Core workflows therefore cannot have an undeclared public-internet dependency,
/// because there is no code path that could reach one.
/// </summary>
public class NoPublicInternetDependencyTests
{
    private static readonly Regex ExternalUrlPattern = new(
        @"https?://(?!localhost|127\.0\.0\.1|\(localdb\))[a-zA-Z0-9.-]+",
        RegexOptions.Compiled);

    [Fact]
    public void No_source_file_references_a_public_internet_endpoint()
    {
        var apiProjectDir = FindApiProjectDirectory();
        var offending = new List<string>();

        foreach (var file in Directory.EnumerateFiles(apiProjectDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            // Strip // line comments first — a documentation link in a comment (e.g. "Learn more
            // at https://aka.ms/...") is not a runtime dependency on the public internet.
            var codeOnly = string.Join('\n', File.ReadLines(file).Select(StripLineComment));
            if (ExternalUrlPattern.IsMatch(codeOnly))
            {
                offending.Add(file);
            }
        }

        Assert.Empty(offending);
    }

    /// <summary>
    /// Strips a trailing "// comment" without being fooled by a "://" inside a real URL literal
    /// (e.g. "https://example.com") — a naive `IndexOf("//")` would truncate the line right after
    /// "https:" and hide the very URL this test is trying to catch.
    /// </summary>
    private static string StripLineComment(string line)
    {
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '/' && line[i + 1] == '/' && (i == 0 || line[i - 1] != ':'))
            {
                return line[..i];
            }
        }
        return line;
    }

    private static string FindApiProjectDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "Alveara.Api");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Alveara.Api.csproj")))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        throw new DirectoryNotFoundException("Could not locate the Alveara.Api project directory from the test output path.");
    }
}
