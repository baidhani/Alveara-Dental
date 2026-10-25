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
        nameof(TestDatabaseFixture), nameof(RealMigrationTestDatabaseFixture), nameof(TestDatabaseTemplate), nameof(TestDatabaseTemplateTests), nameof(RealMigrationInventory),
        nameof(RealMigrationInventoryTests), nameof(RealMigrationsAttribute), nameof(SafetyTestBase),
    ];

    // The internal hook constructor (new TestDatabaseFixture(afterMigrateForTest: ...)) never uses a template unless a template is passed to it, so a class that builds fixtures through it is on the real path.
    private static readonly Regex RealFixtureUse = new(@"new RealMigrationTestDatabaseFixture\(|IClassFixture<RealMigrationTestDatabaseFixture>|\[RealMigrations\]|new TestDatabaseFixture\(\s*[a-zA-Z]+ForTest:");
    private static readonly Regex TemplateFixtureUse = new(@"new TestDatabaseFixture\(\s*\)|IClassFixture<TestDatabaseFixture>|TestDatabaseFixture\s+\w+\s*=\s*new\(\)");
    private static readonly Regex MigratorCall = new(@"IMigrator|\.MigrateAsync\(");

    private static object? FixtureOf(object testClass) =>
        typeof(SafetyTestBase).GetField("Fixture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(testClass);

    [Fact]
    public void Every_class_using_the_real_path_is_named_in_the_inventory_and_every_entry_in_that_group_uses_it()
    {
        var realPath = RealMigrationInventory.All.Where(e => e.UsesRealCreateAndMigrate).Select(e => e.TestClass.Name).ToHashSet();
        var usingReal = Classes().Where(c => !Infrastructure.Contains(c.Name) && RealFixtureUse.IsMatch(c.Text)).Select(c => c.Name).ToHashSet();

        var unlisted = usingReal.Except(realPath).ToList();
        var notBypassing = realPath.Except(usingReal).ToList();
        Assert.True(unlisted.Count == 0, "Use the real path but are not listed as doing so: " + string.Join(", ", unlisted));                // nothing bypasses the template without being listed
        Assert.True(notBypassing.Count == 0, "Are listed as using the real path but do not: " + string.Join(", ", notBypassing));            // nothing is listed as bypassing without really bypassing
        Assert.Equal(RealMigrationInventory.All.Count, RealMigrationInventory.All.Select(e => e.TestClass).Distinct().Count());              // no class is listed twice
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
    public void An_entry_listed_as_calling_the_migrator_still_does_and_each_entry_uses_the_fixture_of_its_group()
    {
        var classes = Classes().ToDictionary(c => c.Name);
        foreach (var entry in RealMigrationInventory.All)
        {
            var source = classes[entry.TestClass.Name];
            if (entry.CallsMigratorDirectly) Assert.True(MigratorCall.IsMatch(source.Text), $"{entry.TestClass.Name} is listed as calling the migrator but no longer does.");
            if (entry.UsesRealCreateAndMigrate) Assert.False(TemplateFixtureUse.IsMatch(source.Text), $"{entry.TestClass.Name} is listed as using the real path but still builds an ordinary template fixture.");
            else Assert.False(RealFixtureUse.IsMatch(source.Text), $"{entry.TestClass.Name} is listed as rolling back on a clone but still bypasses the template.");
        }
    }

    [Fact]
    public void A_class_gets_the_fixture_of_its_group_a_real_migration_fixture_or_a_template_clone()
    {
        foreach (var entry in RealMigrationInventory.All.Where(e => typeof(SafetyTestBase).IsAssignableFrom(e.TestClass)))
        {
            Assert.Equal(entry.UsesRealCreateAndMigrate, entry.TestClass.IsDefined(typeof(RealMigrationsAttribute), inherit: true));
            var fixture = FixtureOf(Activator.CreateInstance(entry.TestClass)!);
            if (entry.UsesRealCreateAndMigrate) Assert.IsType<RealMigrationTestDatabaseFixture>(fixture);
            else Assert.IsNotType<RealMigrationTestDatabaseFixture>(fixture);
        }

        Assert.False(typeof(DocumentImportServiceTests).IsDefined(typeof(RealMigrationsAttribute), inherit: true));
        Assert.IsNotType<RealMigrationTestDatabaseFixture>(FixtureOf(Activator.CreateInstance(typeof(DocumentImportServiceTests))!));
    }

    [Fact]
    public void The_inventory_names_all_three_groups_with_the_real_path_kept_for_the_migration_pipeline_and_the_fixture()
    {
        Assert.Equal(2, RealMigrationInventory.All.Count(e => e.Reason == RealMigrationReason.MigratesFromAnEmptyDatabase));
        Assert.Equal(4, RealMigrationInventory.All.Count(e => e.Reason == RealMigrationReason.TestsTheFixtureItself));
        Assert.Equal(22, RealMigrationInventory.All.Count(e => e.Reason == RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone));   // ALV-N015 added NotificationSchemaTests; STORY-009 added FollowUpSchemaTests; ALV-009-C01 added WorkQueueSchemaTests; ALV-009-C02 added ExternalCaseSchemaTests; STORY-017 added PatientMergeSchemaTests; ALV-017-C01 added PatientDuplicateReviewTests RetiredRecordGuardTests and PatientUnmergeSchemaTests
        Assert.All(RealMigrationInventory.All.Where(e => e.Reason == RealMigrationReason.RollsTheSchemaBackAndForwardOnAClone), e => Assert.True(e.CallsMigratorDirectly));   // every rollback class really calls the migrator
    }
}
