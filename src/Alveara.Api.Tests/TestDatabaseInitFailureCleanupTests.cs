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
}
