using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// TESTSPEED-P2 remediation: the bounded retry around the fixture's database drop (SQL error 1205 only). The loop is tested with ordinary controlled exceptions and a predicate (no SqlException
/// construction, no reflection) plus one real, engineered LocalDB deadlock that proves an actual error 1205 is classified correctly. No test asserts on elapsed time: the delay function is a
/// recorder. In the serial collection: the real-deadlock test works on a database of its own through master-level cleanup.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class TestDatabaseDeadlockRetryTests
{
    private sealed class FakeTransientException(string message) : Exception(message);

    private sealed class Harness
    {
        public readonly List<TimeSpan> Delays = [];
        public readonly StringWriter Log = new();
        public int Calls;

        public Task DelayAsync(TimeSpan wait)
        {
            Delays.Add(wait);
            return Task.CompletedTask;
        }

        public Func<Task> Operation(params Exception?[] outcomes) => () =>
        {
            var outcome = outcomes[Math.Min(Calls, outcomes.Length - 1)];
            Calls++;
            return outcome is null ? Task.CompletedTask : Task.FromException(outcome);
        };

        public Task RunAsync(Func<Task> operation, Func<Exception, bool> isRetryable) =>
            TestDatabaseFixture.RunWithDeadlockRetryAsync(operation, isRetryable, "AlveraTest_demo", DelayAsync, Log);

        public string[] Lines => Log.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsFake(Exception e) => e is FakeTransientException;

    [Fact]
    public async Task One_retryable_failure_then_success_retries_once_with_the_first_delay()
    {
        var h = new Harness();
        await h.RunAsync(h.Operation(new FakeTransientException("one"), null), IsFake);

        Assert.Equal(2, h.Calls);
        Assert.Equal([TimeSpan.FromMilliseconds(250)], h.Delays);
        var line = Assert.Single(h.Lines);
        Assert.Contains("AlveraTest_demo", line);
        Assert.Contains("attempt 1 of 4", line);
        Assert.DoesNotContain("Server=", line);                      // never a connection string
        Assert.DoesNotContain("Trusted_Connection", line);
    }

    [Fact]
    public async Task Three_retryable_failures_then_success_uses_all_three_delays()
    {
        var h = new Harness();
        await h.RunAsync(h.Operation(new FakeTransientException("1"), new FakeTransientException("2"), new FakeTransientException("3"), null), IsFake);

        Assert.Equal(4, h.Calls);
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)], h.Delays);
        Assert.Equal(3, h.Lines.Length);
        Assert.All(h.Lines, l => Assert.Contains("retrying in", l));
    }

    [Fact]
    public async Task Exhaustion_stops_at_exactly_four_attempts_and_rethrows_the_first_exception_unchanged()
    {
        var h = new Harness();
        var first = new FakeTransientException("first");
        var thrown = await Assert.ThrowsAsync<FakeTransientException>(() =>
            h.RunAsync(h.Operation(first, new FakeTransientException("second"), new FakeTransientException("third"), new FakeTransientException("fourth")), IsFake));

        Assert.Same(first, thrown);                                  // the original failure, not the last one, not a wrapper
        Assert.Equal(4, h.Calls);                                    // exactly four operations: this is the cap, so a loop that never ends cannot pass
        Assert.Equal([TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)], h.Delays);
        Assert.Equal(4, h.Lines.Length);
        Assert.Equal(3, h.Lines.Count(l => l.Contains("retrying in")));
        Assert.Single(h.Lines, l => l.Contains("giving up after 4 attempts"));
    }

    [Fact]
    public async Task A_failure_the_predicate_does_not_accept_is_not_retried()
    {
        var h = new Harness();
        var other = new InvalidOperationException("not a deadlock");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync(h.Operation(other), IsFake));

        Assert.Same(other, thrown);
        Assert.Equal(1, h.Calls);
        Assert.Empty(h.Delays);
        Assert.Empty(h.Lines);
    }

    [Fact]
    public async Task Success_on_the_first_attempt_logs_nothing_and_waits_for_nothing()
    {
        var h = new Harness();
        await h.RunAsync(h.Operation((Exception?)null), IsFake);

        Assert.Equal(1, h.Calls);
        Assert.Empty(h.Delays);
        Assert.Empty(h.Lines);
    }

    [Fact]
    public async Task The_deadlock_predicate_accepts_only_SQL_error_1205()
    {
        Assert.False(TestDatabaseFixture.IsDeadlock(new InvalidOperationException("x")));
        Assert.False(TestDatabaseFixture.IsDeadlock(new TimeoutException()));

        // a real SqlException that is not a deadlock (divide by zero, error 8134)
        var fixture = new TestDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var context = fixture.CreateContext();
            var other = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync("SELECT 1/0"));
            Assert.Equal(8134, other.Number);
            Assert.False(TestDatabaseFixture.IsDeadlock(other));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_real_LocalDB_deadlock_is_classified_as_1205_retried_once_and_then_succeeds()
    {
        var fixture = new TestDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using (var setup = fixture.CreateContext())
                await setup.Database.ExecuteSqlRawAsync("CREATE TABLE dl_a (id int PRIMARY KEY, v int); CREATE TABLE dl_b (id int PRIMARY KEY, v int); INSERT dl_a VALUES (1, 0); INSERT dl_b VALUES (1, 0);");

            var h = new Harness();
            var attempt = 0;
            await h.RunAsync(async () =>
            {
                attempt++;
                if (attempt == 1) await EngineerDeadlockAsync(fixture.ConnectionString);   // the genuine 1205 comes out of this call, from the server
                // the second attempt does nothing: it succeeds
            }, TestDatabaseFixture.IsDeadlock);

            Assert.Equal(2, attempt);
            Assert.Equal([TimeSpan.FromMilliseconds(250)], h.Delays);
            var line = Assert.Single(h.Lines);
            Assert.Contains("SQL 1205", line);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// A genuine, repeatable deadlock between two sessions with crossed updates. The victim is fixed by construction (the second session has the lower deadlock priority), the second update
    /// only starts once the first is observed waiting on the lock (a condition poll with a guard against hanging, never a measured duration), and both sessions are cleaned up before returning.
    /// Throws the victim's SqlException (error 1205).
    /// </summary>
    private static async Task EngineerDeadlockAsync(string connectionString)
    {
        await using var winner = new SqlConnection(connectionString);
        await using var victim = new SqlConnection(connectionString);
        await winner.OpenAsync();
        await victim.OpenAsync();

        async Task ExecAsync(SqlConnection c, string sql)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        await ExecAsync(victim, "SET DEADLOCK_PRIORITY LOW; BEGIN TRAN; UPDATE dl_b SET v = 1 WHERE id = 1;");
        await ExecAsync(winner, "SET DEADLOCK_PRIORITY HIGH; BEGIN TRAN; UPDATE dl_a SET v = 1 WHERE id = 1;");

        var winnerSpid = await ScalarAsync(winner, "SELECT CAST(@@SPID AS int)");
        var winnerWaits = ExecAsync(winner, "UPDATE dl_b SET v = 2 WHERE id = 1;");          // blocks: the victim holds dl_b

        for (var i = 0; i < 400 && await ScalarAsync(victim, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = {winnerSpid} AND blocking_session_id <> 0") == 0; i++)
            await Task.Delay(25);                                                              // guard: 10 s at most, then the deadlock below simply cannot form and the test fails with a clear cause
        Assert.Equal(1, await ScalarAsync(victim, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = {winnerSpid} AND blocking_session_id <> 0"));

        try
        {
            await ExecAsync(victim, "UPDATE dl_a SET v = 2 WHERE id = 1;");                    // closes the cycle: this session is the victim
        }
        finally
        {
            await winnerWaits;                                                                  // proceeds once the victim's transaction has been rolled back by the server
            await ExecAsync(winner, "COMMIT");
        }
    }

    private static async Task<int> ScalarAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
