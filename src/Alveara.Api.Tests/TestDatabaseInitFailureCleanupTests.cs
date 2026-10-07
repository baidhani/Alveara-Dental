using Microsoft.Data.SqlClient;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// TESTSPEED-P2 remediation: when <see cref="TestDatabaseFixture.InitializeAsync"/> fails, xUnit never calls DisposeAsync, so the fixture removes the half-created database itself and rethrows
/// the ORIGINAL exception. These tests force the failure through two separately named test-only hooks (before the database is created, after it is migrated) that exist only on the internal
/// constructor. In the serial collection: they count databases on the shared server and run master-level cleanup.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class TestDatabaseInitFailureCleanupTests
{
    private const string MasterCs = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class MarkerException(string message) : Exception(message);

    private static string DatabaseOf(TestDatabaseFixture f) => new SqlConnectionStringBuilder(f.ConnectionString).InitialCatalog;

    private static async Task<int> ScalarAsync(string sql, string? database = null)
    {
        await using var c = new SqlConnection(MasterCs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (database is not null) cmd.Parameters.AddWithValue("@n", database);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task<bool> ExistsAsync(string database) =>
        ScalarAsync("SELECT CASE WHEN DB_ID(@n) IS NULL THEN 0 ELSE 1 END", database).ContinueWith(t => t.Result == 1);

    private static Task<int> SessionsAsync(string database) => ScalarAsync("SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID(@n)", database);

    private static Task<int> FixtureDatabasesAsync() => ScalarAsync("SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'AlveraTest[_]%'");

    [Fact]
    public async Task A_failure_after_the_migration_removes_the_database_and_rethrows_the_original_exception()
    {
        var before = await FixtureDatabasesAsync();
        var marker = new MarkerException("forced failure after migrate");
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(afterMigrateForTest: () => throw marker, logForTest: log);

        var thrown = await Assert.ThrowsAsync<MarkerException>(() => fixture.InitializeAsync());   // the test still FAILS: it never passes

        Assert.Same(marker, thrown);
        var database = DatabaseOf(fixture);
        Assert.False(await ExistsAsync(database));                                                   // the half-created database is gone
        Assert.Equal(0, await SessionsAsync(database));                                              // and nothing is left connected to it
        Assert.Equal(before, await FixtureDatabasesAsync());                                         // no growth of fixture databases (this class is serial, so nothing else creates one meanwhile)
        Assert.Contains("was removed", log.ToString());
        Assert.DoesNotContain("Server=", log.ToString());
    }

    [Fact]
    public async Task A_failure_before_the_database_exists_has_nothing_to_remove_and_still_rethrows_the_original_exception()
    {
        var before = await FixtureDatabasesAsync();
        var marker = new MarkerException("forced failure before create");
        var fixture = new TestDatabaseFixture(beforeCreateForTest: () => throw marker, logForTest: new StringWriter());

        var thrown = await Assert.ThrowsAsync<MarkerException>(() => fixture.InitializeAsync());

        Assert.Same(marker, thrown);
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
        Assert.Equal(before, await FixtureDatabasesAsync());
    }

    [Fact]
    public async Task A_failing_cleanup_is_logged_and_never_replaces_the_original_exception()
    {
        var marker = new MarkerException("forced failure after migrate, cleanup also fails");
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(afterMigrateForTest: () => throw marker, cleanupDropForTest: _ => throw new IOException("cleanup boom"), logForTest: log);

        try
        {
            var thrown = await Assert.ThrowsAsync<MarkerException>(() => fixture.InitializeAsync());

            Assert.Same(marker, thrown);                                                             // the cause, not the IOException from the cleanup
            Assert.Contains("cleanup after a failed initialisation failed", log.ToString());
            Assert.Contains("IOException", log.ToString());
            Assert.True(await ExistsAsync(DatabaseOf(fixture)));                                     // the faked cleanup removed nothing, as set up
        }
        finally
        {
            await fixture.DisposeAsync();                                                            // this test removes its own database through the normal drop
        }
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
    }

    [Fact]
    public async Task A_successful_initialisation_is_not_touched_by_the_cleanup_path()
    {
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(logForTest: log);
        await fixture.InitializeAsync();
        try
        {
            Assert.True(await ExistsAsync(DatabaseOf(fixture)));
            Assert.Empty(log.ToString());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
    }

    // ---------- TESTSPEED-P2 Mode B: bounded retry of the initialisation and of the drop on the pool-acquisition timeout ----------

    private const string PoolTimeoutMessage = "Timeout expired.  The timeout period elapsed prior to obtaining a connection from the pool.  This may have occurred because all pooled connections were in use and max pool size was reached.";

    private static InvalidOperationException PoolTimeout(string tag) => new($"{PoolTimeoutMessage} [{tag}]");

    [Fact]
    public async Task A_pool_timeout_on_the_first_attempt_is_retried_after_a_cleanup_and_the_second_attempt_succeeds()
    {
        var events = new List<string>();
        var attempts = 0;
        var waits = new List<TimeSpan>();
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(
            beforeCreateForTest: () => { events.Add($"attempt{++attempts}"); return attempts == 1 ? Task.FromException(PoolTimeout("first")) : Task.CompletedTask; },
            cleanupDropForTest: _ => { events.Add("cleanup"); return Task.CompletedTask; },
            logForTest: log, delayForTest: w => { waits.Add(w); return Task.CompletedTask; });

        await fixture.InitializeAsync();                                                             // no exception: the retry recovered
        try
        {
            Assert.Equal(["attempt1", "cleanup", "attempt2"], events);                               // cleanup runs between the attempts
            Assert.True(await ExistsAsync(DatabaseOf(fixture)));
            Assert.Empty(waits);                                                                     // zero waits are not slept
            Assert.Contains("initialisation of", log.ToString());
            Assert.Contains("pool acquisition timeout (attempt 1 of 3)", log.ToString());
            Assert.DoesNotContain("Server=", log.ToString());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
    }

    [Fact]
    public async Task A_database_created_by_a_failed_attempt_is_really_removed_before_the_retry_creates_it_again()
    {
        var before = await FixtureDatabasesAsync();
        var attempts = 0;
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(afterMigrateForTest: () => ++attempts == 1 ? Task.FromException(PoolTimeout("after migrate")) : Task.CompletedTask, logForTest: log);

        await fixture.InitializeAsync();                                                             // real cleanup (real drop), then a real second creation under the same name
        try
        {
            Assert.Equal(2, attempts);
            Assert.True(await ExistsAsync(DatabaseOf(fixture)));
            Assert.Equal(before + 1, await FixtureDatabasesAsync());                                 // exactly one database exists for this fixture: the first attempt's was removed
            Assert.Single(log.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), l => l.Contains("was removed"));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
        Assert.Equal(before, await FixtureDatabasesAsync());
    }

    [Fact]
    public async Task When_every_attempt_hits_the_pool_timeout_the_first_exception_is_rethrown_after_exactly_three_attempts_and_nothing_is_left_behind()
    {
        var before = await FixtureDatabasesAsync();
        var attempts = 0;
        InvalidOperationException? first = null;
        var fixture = new TestDatabaseFixture(
            afterMigrateForTest: () => { var ex = PoolTimeout($"attempt {++attempts}"); first ??= ex; return Task.FromException(ex); },
            logForTest: new StringWriter());
        // the real cleanup drop is used here, so a leak would show in the database count below

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync());

        Assert.Same(first, thrown);                                                                  // the FIRST failure, unchanged
        Assert.Contains("[attempt 1]", thrown.Message);
        Assert.Equal(3, attempts);                                                                   // exactly three, never a fourth
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
        Assert.Equal(before, await FixtureDatabasesAsync());
    }

    [Fact]
    public async Task Cleanup_runs_before_every_retry_and_once_more_for_the_final_failure()
    {
        var events = new List<string>();
        var attempts = 0;
        var fixture = new TestDatabaseFixture(
            beforeCreateForTest: () => { events.Add($"attempt{++attempts}"); return Task.FromException(PoolTimeout("always")); },
            cleanupDropForTest: _ => { events.Add("cleanup"); return Task.CompletedTask; },
            logForTest: new StringWriter());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync());

        Assert.Equal(["attempt1", "cleanup", "attempt2", "cleanup", "attempt3", "cleanup"], events);
    }

    [Fact]
    public async Task A_failure_that_is_not_a_pool_timeout_is_not_retried_by_the_initialisation()
    {
        var attempts = 0;
        var marker = new MarkerException("not transient");
        var fixture = new TestDatabaseFixture(afterMigrateForTest: () => { attempts++; return Task.FromException(marker); }, logForTest: new StringWriter());

        var thrown = await Assert.ThrowsAsync<MarkerException>(() => fixture.InitializeAsync());

        Assert.Same(marker, thrown);
        Assert.Equal(1, attempts);
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
    }

    [Fact]
    public async Task A_pool_timeout_while_opening_the_master_connection_for_the_drop_is_retried_as_a_whole()
    {
        var opens = 0;
        var waits = new List<TimeSpan>();
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(beforeMasterOpenForTest: () => ++opens == 1 ? Task.FromException(PoolTimeout("master open")) : Task.CompletedTask, logForTest: log,
            delayForTest: w => { waits.Add(w); return Task.CompletedTask; });
        await fixture.InitializeAsync();

        await fixture.DisposeAsync();                                                                // the first open fails before any batch runs; the retry opens again and drops

        Assert.Equal(2, opens);
        Assert.Equal([TimeSpan.FromMilliseconds(250)], waits);
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
        Assert.Contains("drop of", log.ToString());
        Assert.Contains("pool acquisition timeout (attempt 1 of 4)", log.ToString());
    }

    [Fact]
    public async Task When_the_master_open_keeps_failing_the_drop_gives_up_after_four_attempts_with_the_first_exception()
    {
        var opens = 0;
        var failing = true;
        var waits = new List<TimeSpan>();
        var fixture = new TestDatabaseFixture(beforeMasterOpenForTest: () => { opens++; return failing ? Task.FromException(PoolTimeout($"open {opens}")) : Task.CompletedTask; }, logForTest: new StringWriter(),
            delayForTest: w => { waits.Add(w); return Task.CompletedTask; });
        await fixture.InitializeAsync();
        try
        {
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.DisposeAsync());

            Assert.Contains("[open 1]", thrown.Message);                                             // the first failure
            Assert.Equal(4, opens);
            Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)], waits);
            Assert.True(await ExistsAsync(DatabaseOf(fixture)));                                     // the drop never ran
        }
        finally
        {
            failing = false;
            await fixture.DisposeAsync();                                                            // this test removes its own database
        }
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
    }

    [Fact]
    public async Task A_pool_timeout_while_opening_master_for_the_cleanup_drop_is_retried_and_the_original_initialisation_exception_still_wins()
    {
        var opens = 0;
        var marker = new MarkerException("failure after migrate");
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(afterMigrateForTest: () => Task.FromException(marker),
            beforeMasterOpenForTest: () => ++opens == 1 ? Task.FromException(PoolTimeout("cleanup open")) : Task.CompletedTask, logForTest: log, delayForTest: _ => Task.CompletedTask);

        var thrown = await Assert.ThrowsAsync<MarkerException>(() => fixture.InitializeAsync());

        Assert.Same(marker, thrown);
        Assert.Equal(2, opens);                                                                      // the cleanup drop was retried
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
        Assert.Contains("was removed", log.ToString());
    }
}
