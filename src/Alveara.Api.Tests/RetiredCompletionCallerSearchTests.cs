using System.Text.RegularExpressions;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 stop condition 5, as a test rather than a one-off search: no production source in this repository calls a retired state-only completion. "Production" is the API (<c>src/Alveara.Api</c>, excluding build
/// output and migrations' generated designers) and the client's own source (<c>src/alveara-client/src</c>, excluding test files). The only places allowed to name a retired route are its two 410 stubs in
/// <c>TreatmentPlansController.cs</c>, and comments are ignored. The test-only <see cref="LegacyCompletionFixture"/> lives in the test project, which this search does not read, so it cannot leak into production.
/// </summary>
public class RetiredCompletionCallerSearchTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root (the folder with CLAUDE.md) was not found above the test output.");
    }

    private static readonly Regex[] Retired =
    [
        new(@"\bCompleteItemAsync\b", RegexOptions.Compiled),
        new(@"\bCompleteAcceptedAsync\b", RegexOptions.Compiled),
        new(@"\bcompleteItem\b", RegexOptions.Compiled),
        new(@"\bcompleteAcceptedProcedures\b", RegexOptions.Compiled),
        new(@"complete-accepted", RegexOptions.Compiled),
        new(@"/items/[^""'`\s]*/complete(?![-\w])", RegexOptions.Compiled),
    ];

    private static IEnumerable<string> ProductionFiles(string root)
    {
        static bool Skip(string p) => p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            || p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") || p.EndsWith(".Designer.cs", StringComparison.Ordinal);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src", "Alveara.Api"), "*.cs", SearchOption.AllDirectories).Where(f => !Skip(f))) yield return f;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src", "alveara-client", "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal)) && !Skip(f) && !Regex.IsMatch(f, @"\.test\.tsx?$") && !f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}")))
            yield return f;
    }

    /// <summary>A line with its trailing <c>//</c> comment removed, and nothing at all for a comment-only line (<c>///</c>, <c>//</c>, or the inside of a block comment).</summary>
    private static string Code(string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)) return "";
        var at = line.IndexOf("//", StringComparison.Ordinal);
        return at < 0 || line[..at].Count(c => c == '"') % 2 == 1 ? line : line[..at];
    }

    private static List<string> Hits(string root) =>
        ProductionFiles(root).SelectMany(file => File.ReadLines(file).Select((line, i) => (file, n: i + 1, code: Code(line)))
            .Where(x => Retired.Any(r => r.IsMatch(x.code))).Select(x => $"{Path.GetRelativePath(root, x.file)}:{x.n}: {x.code.Trim()}")).ToList();

    [Fact]
    public void No_production_source_calls_a_retired_completion_and_only_the_two_410_stubs_name_the_routes()
    {
        var root = Root();
        var hits = Hits(root);

        // the routes themselves are declared in exactly one file, as exactly the two stubs
        Assert.All(hits, h => Assert.StartsWith(Path.Combine("src", "Alveara.Api", "Controllers", "TreatmentPlansController.cs"), h));
        Assert.Equal(2, hits.Count(h => h.Contains("HttpPost(", StringComparison.Ordinal)));                     // the two route declarations
        Assert.Contains(hits, h => h.Contains("HttpPost(\"api/treatment-plans/{planId:guid}/complete-accepted\")", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("HttpPost(\"api/treatment-plans/{planId:guid}/items/{itemId:guid}/complete\")", StringComparison.Ordinal));
        Assert.Equal(3, hits.Count);                                                                                // plus the one stub body that names its route in a string
    }

    [Fact]
    public void Both_stubs_do_nothing_but_answer_410()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "src", "Alveara.Api", "Controllers", "TreatmentPlansController.cs"));
        Assert.Matches(@"public IActionResult CompleteAccepted\(Guid planId\) => RetiredCompletion\(""complete-accepted""\);", text);
        Assert.Matches(@"public IActionResult CompleteItem\(Guid planId, Guid itemId\) => RetiredCompletion\(""items/complete""\);", text);
        Assert.Contains("return StatusCode(410, new", text);
    }

    [Fact]
    public void The_search_itself_finds_a_reintroduced_call_in_a_scratch_copy_of_the_tree()
    {
        var dir = Directory.CreateTempSubdirectory("retired-caller-search-");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "src", "Alveara.Api", "Architecture"));
            Directory.CreateDirectory(Path.Combine(dir.FullName, "src", "alveara-client", "src"));
            File.WriteAllText(Path.Combine(dir.FullName, "src", "Alveara.Api", "Architecture", "A.cs"), "// plans.CompleteItemAsync(x) in a comment is fine\nvar y = await plans.CompleteAcceptedAsync(id, v, ct);\n");
            File.WriteAllText(Path.Combine(dir.FullName, "src", "alveara-client", "src", "a.ts"), "export const bad = (id: string) => post(`/api/treatment-plans/${id}/items/${x}/complete`);\n");
            var hits = Hits(dir.FullName);
            Assert.Equal(2, hits.Count);
            Assert.Contains(hits, h => h.Contains("CompleteAcceptedAsync", StringComparison.Ordinal));
            Assert.Contains(hits, h => h.Contains("/items/", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}
