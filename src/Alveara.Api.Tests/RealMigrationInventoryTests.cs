using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N016: proves the bypass inventory is complete and honest. No test class outside <see cref="RealMigrationInventory"/> uses the real create-and-migrate path, no class that calls the migrator is missing
/// from it, every entry really uses a <see cref="RealMigrationTestDatabaseFixture"/> and still calls the migrator when it is listed as doing so (so real migration and rollback stay exercised), and no entry
/// can quietly fall back to the template. The source files are read from the folder this file was compiled in.
/// </summary>
public class RealMigrationInventoryTests
{
    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private sealed record ClassSource(string Name, string File, string Text);

    /// <summary>Every top-level class of the test project's source with its text (found by brace matching; the project's classes contain no unbalanced braces in strings).</summary>
    private static IReadOnlyList<ClassSource> Classes()
    {
        var all = new List<ClassSource>();
        foreach (var file in Directory.GetFiles(SourceDirectory(), "*.cs"))
        {
            var text = File.ReadAllText(file);
            // The attributes written just above a class belong to it (the guard looks for [RealMigrations] there).
            foreach (Match m in Regex.Matches(text, @"((?:\[[^\]

]+\]\s*)*)(?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+)?(?:partial\s+)?class\s+(\w+)[^{;]*\{"))
            {
                var depth = 1;
                var i = m.Index + m.Length;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') depth--;
                    i++;
                }
                all.Add(new ClassSource(m.Groups[2].Value, Path.GetFileName(file), text[m.Index..i]));
            }
        }
        return all;
    }

    private static readonly HashSet<string> Infrastructure =
    [
        nameof(TestDatabaseFixture), nameof(RealMigrationTestDatabaseFixture), nameof(TestDatabaseTemplate), nameof(TestDatabaseTemplateTests), nameof(TestDatabasePrefetcherTests), nameof(DatabasePrefetcher), nameof(RealMigrationInventory),
        nameof(RealMigrationInventoryTests), nameof(RealMigrationsAttribute), nameof(SafetyTestBase),
    ];

    // The internal hook constructor (new TestDatabaseFixture(afterMigrateForTest: ...)) never uses a template unless a template is passed to it, so a class that builds fixtures through it is on the real path.
    private static readonly Regex RealFixtureUse = new(@"new RealMigrationTestDatabaseFixture\(|IClassFixture<RealMigrationTestDatabaseFixture>|\[RealMigrations\]|new TestDatabaseFixture\(\s*[a-zA-Z]+ForTest:");
    private static readonly Regex TemplateFixtureUse = new(@"new TestDatabaseFixture\(\s*\)|IClassFixture<TestDatabaseFixture>|TestDatabaseFixture\s+\w+\s*=\s*new\(\)");
    private static readonly Regex MigratorCall = new(@"IMigrator|\.MigrateAsync\(");

    private static object? FixtureOf(object testClass) =>
        typeof(SafetyTestBase).GetField("Fixture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(testClass);

    [Fact]
    public void Every_class_using_the_real_path_is_named_in_the_inventory_and_every_entry_uses_it()
    {
        var inventory = RealMigrationInventory.All.Select(e => e.TestClass.Name).ToHashSet();
        var usingReal = Classes().Where(c => !Infrastructure.Contains(c.Name) && RealFixtureUse.IsMatch(c.Text)).Select(c => c.Name).ToHashSet();

        var unlisted = usingReal.Except(inventory).ToList();
        var notBypassing = inventory.Except(usingReal).ToList();
        Assert.True(unlisted.Count == 0, "Use the real path but are not in the inventory: " + string.Join(", ", unlisted));                // nothing bypasses the template without being listed
        Assert.True(notBypassing.Count == 0, "Are in the inventory but do not use the real path: " + string.Join(", ", notBypassing));     // nothing is listed without really bypassing it
        Assert.Equal(RealMigrationInventory.All.Count, inventory.Count);                                                // and no class is listed twice
    }

    [Fact]
    public void No_class_that_calls_the_migrator_is_missing_from_the_inventory()
    {
        var inventory = RealMigrationInventory.All.Select(e => e.TestClass.Name).ToHashSet();
        var ownDatabase = RealMigrationInventory.OwnDatabaseNotAFixtureUser.Select(t => t.Name).ToHashSet();
        var callers = Classes().Where(c => !Infrastructure.Contains(c.Name) && MigratorCall.IsMatch(c.Text)).Select(c => c.Name).ToHashSet();

        var missing = callers.Except(inventory).Except(ownDatabase).ToList();
        Assert.True(missing.Count == 0, "Call the migrator but are not in the inventory: " + string.Join(", ", missing));
        foreach (var name in ownDatabase)
            Assert.DoesNotContain(Classes(), c => c.Name == name && (TemplateFixtureUse.IsMatch(c.Text) || RealFixtureUse.IsMatch(c.Text)));      // a class listed as using its own database uses no fixture at all
    }

    [Fact]
    public void An_entry_listed_as_calling_the_migrator_still_does_and_none_can_fall_back_to_the_template()
    {
        var classes = Classes().ToDictionary(c => c.Name);
        foreach (var entry in RealMigrationInventory.All)
        {
            var source = classes[entry.TestClass.Name];
            if (entry.CallsMigratorDirectly) Assert.True(MigratorCall.IsMatch(source.Text), $"{entry.TestClass.Name} is listed as calling the migrator but no longer does.");
            Assert.False(TemplateFixtureUse.IsMatch(source.Text), $"{entry.TestClass.Name} is in the inventory but still builds an ordinary template fixture.");
        }
    }

    [Fact]
    public void A_marked_class_gets_a_real_migration_fixture_and_an_ordinary_class_a_template_fixture()
    {
        foreach (var entry in RealMigrationInventory.All.Where(e => typeof(SafetyTestBase).IsAssignableFrom(e.TestClass)))
        {
            Assert.True(entry.TestClass.IsDefined(typeof(RealMigrationsAttribute), inherit: true), entry.TestClass.Name);
            Assert.IsType<RealMigrationTestDatabaseFixture>(FixtureOf(Activator.CreateInstance(entry.TestClass)!));
        }

        Assert.False(typeof(DocumentImportServiceTests).IsDefined(typeof(RealMigrationsAttribute), inherit: true));
        Assert.IsNotType<RealMigrationTestDatabaseFixture>(FixtureOf(Activator.CreateInstance(typeof(DocumentImportServiceTests))!));
    }

    [Fact]
    public void The_inventory_names_both_reasons_and_is_not_empty()
    {
        Assert.Contains(RealMigrationInventory.All, e => e.Reason == RealMigrationReason.RollsTheSchemaBackAndForward);
        Assert.Contains(RealMigrationInventory.All, e => e.Reason == RealMigrationReason.TestsTheFixtureItself);
        Assert.True(RealMigrationInventory.All.Count >= 19);
    }
}
