using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N016 R02: the prefetch pool. While one test runs, the run's template restores the next clones in the background, so the restore time overlaps the test instead of adding to it. Tests still run strictly
/// one after another; only the preparation of databases overlaps. These tests prove the pool hands out distinct, complete clones, keeps filling behind the tests, falls back when a background restore failed,
/// removes every clone nobody claimed, and leaves nothing behind. Each test builds its own template and pool, so none touches the run's.
/// </summary>
public class TestDatabasePrefetcherTests : IAsyncLifetime
{
    private readonly List<IDisposable> _templates = [];
    private readonly List<TestDatabaseFixture> _fixtures = [];
    private readonly List<string> _folders = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var f in _fixtures) await f.DisposeAsync();
        foreach (var t in _templates) t.Dispose();
        foreach (var folder in _folders) TestStorageCleanup.Delete(folder);
    }

    private (TestDatabaseTemplate Template, StringWriter Log) NewTemplate(int depth)
    {
        var folder = Path.Combine(AlveraTestEnvironment.TempFolder, $"template-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        var log = new StringWriter();
        var template = new TestDatabaseTemplate(folder, log, prefetchDepth: depth);
        _templates.Add(template);
        return (template, log);
    }

    private async Task<TestDatabaseFixture> StartAsync(TestDatabaseTemplate template, StringWriter log)
    {
        var fixture = new TestDatabaseFixture(templateForTest: template, logForTest: log);
        await fixture.InitializeAsync();
        _fixtures.Add(fixture);
        return fixture;
    }

    private static string NameOf(TestDatabaseFixture f) => new SqlConnectionStringBuilder(f.ConnectionString).InitialCatalog;

    private static async Task<int> ScalarAsync(string sql)
    {
        await using var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static Task<int> PoolDatabasesAsync() => ScalarAsync("SELECT COUNT(*) FROM sys.databases WHERE name LIKE N'AlveraPool[_]%'");

    private static IReadOnlyList<string> CodeMigrations()
    {
        using var db = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(AlveraTestEnvironment.ConnectionStringFor("master")).Options);
        return db.Database.GetMigrations().ToList();
    }

    /// <summary>Waits (bounded) until the pool has prepared everything it is going to prepare, so a test can look at a settled pool.</summary>
    private static async Task SettledAsync(TestDatabaseTemplate template, int ready)
    {
        for (var i = 0; i < 300 && template.PrefetchReady < ready; i++) await Task.Delay(100);
        Assert.True(template.PrefetchReady >= ready, $"the pool prepared {template.PrefetchReady} of the expected {ready} clones");
    }

    [Fact]
    public async Task A_claimed_prefetched_clone_is_a_complete_clone_under_its_own_name_and_the_pool_refills_behind_it()
    {
        var (template, log) = NewTemplate(depth: 2);
        await template.EnsureBuiltAsync();
        await SettledAsync(template, 2);

        var fixture = await StartAsync(template, log);                                                                   // takes a prepared clone

        Assert.StartsWith("AlveraPool_", NameOf(fixture));
        await using var db = fixture.CreateContext();
        Assert.Equal(CodeMigrations(), (await db.Database.GetAppliedMigrationsAsync()).ToList());
        Assert.Equal(1, template.PrefetchHits);
        Assert.Equal(0, template.PrefetchMisses);
        await SettledAsync(template, 2);                                                                                 // two more are prepared behind the one that was taken
        Assert.Equal(3, template.Clones);
        Assert.Equal(0, template.Fallbacks);
    }

    [Fact]
    public async Task Claims_are_distinct_databases_and_data_written_to_one_never_appears_in_another()
    {
        var (template, log) = NewTemplate(depth: 2);
        await template.EnsureBuiltAsync();
        await SettledAsync(template, 2);
        var a = await StartAsync(template, log);
        var b = await StartAsync(template, log);
        var c = await StartAsync(template, log);
        Assert.Equal(3, new[] { NameOf(a), NameOf(b), NameOf(c) }.Distinct().Count());

        var arranged = new SchedulingTestSupport(a);
        await arranged.ArrangeAsync();
        await arranged.PatientAsync();

        await using var inA = a.CreateContext();
        await using var inB = b.CreateContext();
        Assert.Equal(1, await inA.Patients.CountAsync());
        Assert.Equal(0, await inB.Patients.CountAsync());
    }

    [Fact]
    public async Task The_first_claim_before_anything_is_prepared_restores_synchronously_and_the_pool_fills_for_the_next()
    {
        var (template, log) = NewTemplate(depth: 2);
        var first = await StartAsync(template, log);                                                                     // the template was not built yet: nothing to claim

        Assert.StartsWith("AlveraTest_", NameOf(first));                                                                 // a synchronous clone keeps the ordinary name
        Assert.Equal(1, template.PrefetchMisses);
        await SettledAsync(template, 2);
        var second = await StartAsync(template, log);
        Assert.StartsWith("AlveraPool_", NameOf(second));
        Assert.Equal(1, template.PrefetchHits);
    }

    [Fact]
    public async Task A_background_restore_that_failed_falls_back_for_the_fixture_that_claimed_it_and_leaves_nothing_behind()
    {
        var (template, log) = NewTemplate(depth: 1);
        var info = await template.EnsureBuiltAsync();
        await SettledAsync(template, 1);
        await File.WriteAllBytesAsync(info.BackupPath, new byte[4096]);                                                  // from now on every background restore fails
        await template.DiscardPreparedAsync();                                                                           // drop what was prepared from the good backup
        template.RestartPrefetch();                                                                                      // and prepare again from the corrupt one
        await Task.Delay(1500);

        var before = await PoolDatabasesAsync();
        var fixture = await StartAsync(template, log);                                                                   // claims a clone whose restore failed

        Assert.StartsWith("AlveraTest_", NameOf(fixture));                                                               // it fell back to its own real-migration database
        await using var db = fixture.CreateContext();
        Assert.Equal(CodeMigrations(), (await db.Database.GetAppliedMigrationsAsync()).ToList());
        Assert.True(template.Fallbacks >= 1);
        Assert.Contains("template_restore_fallback", log.ToString());
        Assert.Equal(before, await PoolDatabasesAsync());                                                                // no partial pool database survived the claim
    }

    [Fact]
    public async Task Clones_nobody_claimed_are_dropped_with_their_files_when_the_template_is_disposed()
    {
        var (template, log) = NewTemplate(depth: 3);
        await template.EnsureBuiltAsync();
        await SettledAsync(template, 3);
        Assert.True(await PoolDatabasesAsync() >= 3);

        template.Dispose();

        Assert.Equal(0, await PoolDatabasesAsync());
        Assert.Empty(Directory.GetFiles(AlveraTestEnvironment.DatabaseFolder, "AlveraPool_*"));
        Assert.Contains("template_prefetch_dropped", log.ToString());
    }

    [Fact]
    public async Task With_a_depth_of_zero_nothing_is_prepared_ahead_and_every_clone_is_restored_when_asked_for()
    {
        var (template, log) = NewTemplate(depth: 0);
        var fixture = await StartAsync(template, log);

        Assert.StartsWith("AlveraTest_", NameOf(fixture));
        Assert.Equal(0, template.PrefetchHits);
        Assert.Equal(0, await PoolDatabasesAsync());
        Assert.Equal(1, template.Clones);
    }

    [Fact]
    public async Task The_summary_reports_how_often_the_pool_had_a_clone_ready()
    {
        var (template, log) = NewTemplate(depth: 2);
        await template.EnsureBuiltAsync();
        await SettledAsync(template, 2);
        await StartAsync(template, log);
        template.Dispose();

        var summary = log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Single(l => l.Contains("template_summary"));
        Assert.Contains("\"prefetch_hits\":1", summary);
        Assert.Contains("\"prefetch_misses\":0", summary);
    }
}
