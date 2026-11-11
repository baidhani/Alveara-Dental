using Microsoft.Data.SqlClient;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N019 R04: owner-side failure cleanup. xUnit never calls DisposeAsync when an owner InitializeAsync throws, so an owner whose setup fails AFTER the fixture is ready used to leak its clone and two files
/// (both R03 Stage 2 failures did). <see cref="TestDatabaseFixture.InitializeOwnerAsync"/> removes ONLY that fixture's own clone and rethrows the original exception. Both observed owner shapes are covered:
/// a direct owner (the ImportIntakeServiceTests shape) and a <see cref="SafetyTestBase"/> derivative. In the serial collection: it counts databases on the shared server.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class FixtureOwnerCleanupTests
{
    private sealed class MarkerException(string message) : Exception(message);

    private static string DatabaseOf(TestDatabaseFixture f) => new SqlConnectionStringBuilder(f.ConnectionString).InitialCatalog;

    private static async Task<int> ScalarAsync(string sql, string? database = null)
    {
        await using var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        if (database is not null) cmd.Parameters.AddWithValue("@n", database);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task<int> FixtureDatabasesAsync() => ScalarAsync("SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'AlveraTest[_]%'");
    private static async Task<bool> ExistsAsync(string database) => await ScalarAsync("SELECT CASE WHEN DB_ID(@n) IS NULL THEN 0 ELSE 1 END", database) == 1;

    private static int FixtureFiles()
    {
        var (dataFile, _) = AlveraTestEnvironment.FilesOf("AlveraTest_" + new string('0', 32));
        var folder = Path.GetDirectoryName(dataFile)!;
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "AlveraTest_*").Length : 0;
    }

    private static TestDatabaseFixture NewFixture(TextWriter log, Func<string, Task>? cleanupDrop = null) =>
        new(logForTest: log, templateForTest: TestDatabaseTemplate.Default, cleanupDropForTest: cleanupDrop);

    [Fact]
    public async Task A_direct_owner_whose_setup_fails_after_the_fixture_is_ready_leaves_no_database_and_no_files_and_the_original_exception_propagates()
    {
        var (databasesBefore, filesBefore) = (await FixtureDatabasesAsync(), FixtureFiles());
        var marker = new MarkerException("the owner setup failed");
        var log = new StringWriter();
        var fixture = NewFixture(log);
        var existedDuringSetup = false;

        var thrown = await Assert.ThrowsAsync<MarkerException>(() => fixture.InitializeOwnerAsync(async () =>
        {
            existedDuringSetup = await ExistsAsync(DatabaseOf(fixture));                       // the clone really existed when the setup failed
            throw marker;
        }));

        Assert.True(existedDuringSetup);
        Assert.Same(marker, thrown);                                                           // the same object, not a wrapper
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
        Assert.Equal((databasesBefore, filesBefore), (await FixtureDatabasesAsync(), FixtureFiles()));
        Assert.Contains("owner setup of", log.ToString());
        Assert.Contains("were removed", log.ToString());
        Assert.DoesNotContain("Server=", log.ToString());
    }

    [Fact]
    public async Task A_SafetyTestBase_owner_whose_setup_fails_leaves_no_database_and_no_files_and_the_original_exception_propagates()
    {
        var (databasesBefore, filesBefore) = (await FixtureDatabasesAsync(), FixtureFiles());
        var marker = new MarkerException("the SafetyTestBase setup failed");
        var owner = new FailingSafetyOwner { SetupFailureForTest = () => throw marker };

        var thrown = await Assert.ThrowsAsync<MarkerException>(() => owner.InitializeAsync());

        Assert.Same(marker, thrown);
        Assert.Equal((databasesBefore, filesBefore), (await FixtureDatabasesAsync(), FixtureFiles()));
    }

    [Fact]
    public async Task A_failing_owner_cleanup_is_logged_on_its_own_line_and_never_replaces_the_original_exception()
    {
        var marker = new MarkerException("the owner setup failed, and so does the cleanup");
        var log = new StringWriter();
        var fixture = NewFixture(log, cleanupDrop: _ => throw new IOException("cleanup boom"));

        try
        {
            var thrown = await Assert.ThrowsAsync<MarkerException>(() => fixture.InitializeOwnerAsync(() => throw marker));

            Assert.Same(marker, thrown);                                                       // the cause, not the IOException from the cleanup
            Assert.Contains("owner-failure cleanup of", log.ToString());
            Assert.Contains("FAILED (IOException)", log.ToString());
            Assert.DoesNotContain("cleanup boom", log.ToString());                              // the class only, never the message
            Assert.True(await ExistsAsync(DatabaseOf(fixture)));                               // the faked cleanup removed nothing, as set up
        }
        finally
        {
            await fixture.DisposeAsync();                                                      // this test removes its own database through the normal drop
        }
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
    }

    [Fact]
    public async Task A_successful_owner_setup_is_left_alone()
    {
        var log = new StringWriter();
        var fixture = NewFixture(log);
        var ran = false;
        await fixture.InitializeOwnerAsync(() => { ran = true; return Task.CompletedTask; });
        try
        {
            Assert.True(ran);
            Assert.True(await ExistsAsync(DatabaseOf(fixture)));
            Assert.DoesNotContain("owner", log.ToString());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));
    }

    [Fact]
    public async Task The_owner_setup_is_never_retried_even_when_it_fails_with_the_pool_timeout_text()
    {
        var calls = 0;
        var fixture = NewFixture(new StringWriter());
        var poolTimeout = new InvalidOperationException("Timeout expired.  The timeout period elapsed prior to obtaining a connection from the pool.  This may have occurred because all pooled connections were in use and max pool size was reached.");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeOwnerAsync(() => { calls++; throw poolTimeout; }));

        Assert.Same(poolTimeout, thrown);
        Assert.Equal(1, calls);                                                                // the owner setup is not the fixture's to retry
        Assert.False(await ExistsAsync(DatabaseOf(fixture)));                                  // and it still cleaned up
    }

    /// <summary>A real SafetyTestBase derivative with no tests of its own, used only to drive the shared base failure path.</summary>
    private sealed class FailingSafetyOwner : SafetyTestBase;
}
