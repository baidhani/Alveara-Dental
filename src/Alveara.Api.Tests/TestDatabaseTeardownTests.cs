using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// TESTSPEED-P1A: the fixture releases the pooled connections that belong to ITS OWN database before it drops it. These are functional tests: they count the sessions a database has (an idle pooled
/// connection is a session) and check that databases exist or do not. <b>None of them asserts how long anything takes</b> (the only time limit is a generous timeout while waiting for the server to
/// register a closed connection); the speed-up is measured separately on an idle machine. They prove that the release really happens (T1, T2), that it is scoped to the fixture's own pool and does
/// not touch another fixture's (T3), and that the drop still succeeds when connections are still in use (T4, T5) or belong to a different pool (T6).
/// Each test disposes everything it creates in a finally block, so a failure cannot leak a database.
/// </summary>
public class TestDatabaseTeardownTests
{
    private const string MasterCs = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private static string DatabaseOf(TestDatabaseFixture f) => new SqlConnectionStringBuilder(f.ConnectionString).InitialCatalog;

    private static async Task<int> SessionsAsync(string database)
    {
        await using var c = new SqlConnection(MasterCs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID(@n)";
        cmd.Parameters.AddWithValue("@n", database);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ExistsAsync(string database)
    {
        await using var c = new SqlConnection(MasterCs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN DB_ID(@n) IS NULL THEN 0 ELSE 1 END";
        cmd.Parameters.AddWithValue("@n", database);
        return (int)(await cmd.ExecuteScalarAsync())! == 1;
    }

    /// <summary>Waits (up to a generous timeout, only to let the server register a closed connection) until the number of sessions satisfies <paramref name="done"/>; returns the last count seen.</summary>
    private static async Task<int> SettledSessionsAsync(string database, Func<int, bool> done, int timeoutMs = 10_000)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var n = await SessionsAsync(database);
            if (done(n) || clock.ElapsedMilliseconds > timeoutMs) return n;
            await Task.Delay(50);
        }
    }

    private static async Task<int> SpidAsync(TestDatabaseFixture f)
    {
        await using var context = f.CreateContext();
        return await context.Database.SqlQueryRaw<int>("SELECT CAST(@@SPID AS int) AS [Value]").SingleAsync();
    }

    /// <summary>A fixture that is always disposed exactly once, whatever the test does.</summary>
    private sealed class Owned : IAsyncDisposable
    {
        public TestDatabaseFixture Fixture { get; } = new();
        private bool _disposed;
        public async Task StartAsync() => await Fixture.InitializeAsync();
        public async Task DisposeNowAsync() { if (_disposed) return; _disposed = true; await Fixture.DisposeAsync(); }
        public ValueTask DisposeAsync() => new(DisposeNowAsync());
    }

    [Fact]
    public async Task T1_A_direct_context_leaves_pooled_sessions_that_ReleasePooledConnections_closes_and_the_database_is_then_dropped()
    {
        await using var owned = new Owned();
        await owned.StartAsync();
        var f = owned.Fixture;
        var db = DatabaseOf(f);

        await using (var context = f.CreateContext()) _ = await context.Patients.CountAsync();
        Assert.True(await SessionsAsync(db) >= 1, "using the database through a context should leave an idle pooled session");

        f.ReleasePooledConnections();
        Assert.Equal(0, await SettledSessionsAsync(db, n => n == 0));        // the release really closed this database's pooled connections

        await owned.DisposeNowAsync();
        Assert.False(await ExistsAsync(db));
    }

    [Fact]
    public async Task T2_An_API_host_that_signed_people_in_leaves_pooled_sessions_that_are_released_and_the_database_is_dropped()
    {
        await using var owned = new Owned();
        await owned.StartAsync();
        var f = owned.Fixture;
        var db = DatabaseOf(f);

        var harness = new SchedulingApiHarness(f);
        try
        {
            _ = await harness.SessionAsync("Dentist");                        // a real host and real sign-ins: pooled connections created through dependency injection
            Assert.True(await SessionsAsync(db) >= 1, "the host's database use should leave pooled sessions");
        }
        finally
        {
            harness.Dispose();
        }

        f.ReleasePooledConnections();
        Assert.Equal(0, await SettledSessionsAsync(db, n => n == 0));        // the host shares the pool the release clears

        await owned.DisposeNowAsync();
        Assert.False(await ExistsAsync(db));
    }

    [Fact]
    public async Task T3_Releasing_one_fixtures_pool_does_not_touch_another_fixtures_pooled_connection()
    {
        await using var a = new Owned();
        await using var b = new Owned();
        await a.StartAsync();
        await b.StartAsync();
        var dbA = DatabaseOf(a.Fixture);
        var dbB = DatabaseOf(b.Fixture);

        var spidBefore = await SpidAsync(b.Fixture);                          // b borrows its pooled connection and returns it to its pool
        Assert.True(await SessionsAsync(dbB) >= 1);
        Assert.True(await SessionsAsync(dbA) >= 1);

        a.Fixture.ReleasePooledConnections();
        Assert.Equal(0, await SettledSessionsAsync(dbA, n => n == 0));        // a's pool is released ...

        Assert.True(await SessionsAsync(dbB) >= 1, "b's idle pooled session must still exist: the release is scoped to a's pool");
        Assert.Equal(spidBefore, await SpidAsync(b.Fixture));                 // ... and b gets the very same physical connection back
        Assert.True(await ExistsAsync(dbB));

        await a.DisposeNowAsync();
        await b.DisposeNowAsync();
        Assert.False(await ExistsAsync(dbA));
        Assert.False(await ExistsAsync(dbB));
    }

    [Fact]
    public async Task T4_Disposing_the_fixture_while_a_host_is_still_alive_still_drops_the_database_and_the_host_disposes_without_error()
    {
        await using var owned = new Owned();
        await owned.StartAsync();
        var f = owned.Fixture;
        var db = DatabaseOf(f);

        var harness = new SchedulingApiHarness(f);
        try
        {
            _ = await harness.SessionAsync("Dentist");
            await owned.DisposeNowAsync();                                    // abnormal order: the host is still alive
            Assert.False(await ExistsAsync(db));
        }
        finally
        {
            Assert.Null(Record.Exception(() => harness.Dispose()));
        }
    }

    [Fact]
    public async Task T5_A_connection_held_open_with_a_transaction_is_terminated_by_the_drop_and_nothing_is_left_behind()
    {
        await using var owned = new Owned();
        await owned.StartAsync();
        var f = owned.Fixture;
        var db = DatabaseOf(f);

        var held = f.CreateContext();
        try
        {
            await held.Database.BeginTransactionAsync();
            _ = await held.Patients.CountAsync();                             // a connection from the same pool, checked out with a transaction open

            await owned.DisposeNowAsync();

            Assert.False(await ExistsAsync(db));
            await Assert.ThrowsAnyAsync<Exception>(() => held.Patients.CountAsync());   // the held connection was terminated by the drop
        }
        finally
        {
            try { await held.DisposeAsync(); } catch { /* its connection was terminated by design */ }
        }
    }

    [Fact]
    public async Task T6_A_session_from_a_different_pool_is_not_released_and_the_drop_still_succeeds()
    {
        await using var owned = new Owned();
        await owned.StartAsync();
        var f = owned.Fixture;
        var db = DatabaseOf(f);

        // a different connection string is a different pool (as the restricted-login string in LeastPrivilegeAccessTests is)
        var other = new SqlConnectionStringBuilder(f.ConnectionString) { ApplicationName = "TestDatabaseTeardownTests-other-pool" }.ConnectionString;
        var otherConnection = new SqlConnection(other);
        try
        {
            await otherConnection.OpenAsync();
            await using (var cmd = otherConnection.CreateCommand()) { cmd.CommandText = "SELECT 1"; _ = await cmd.ExecuteScalarAsync(); }
            await otherConnection.CloseAsync();                                // back to ITS pool, idle but alive

            f.ReleasePooledConnections();
            await SettledSessionsAsync(db, n => n == 1);                       // the fixture's own pool is gone; only the other pool's session remains
            Assert.True(await SessionsAsync(db) >= 1, "the other pool's idle session is not part of the fixture's pool and must not be released");

            await owned.DisposeNowAsync();                                     // the retained ROLLBACK IMMEDIATE still drops the database
            Assert.False(await ExistsAsync(db));
        }
        finally
        {
            try { SqlConnection.ClearPool(otherConnection); } catch { /* best effort: only our own test pool */ }
            await otherConnection.DisposeAsync();
        }
    }
}
