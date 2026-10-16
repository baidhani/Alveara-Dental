using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-016-C01 R02 (review finding ALV-016-C01-R01-01): a completion or an adoption that SQL Server chooses as a deadlock victim (error 1205) must end in a domain-safe outcome: run again a bounded number of times,
/// and after the last attempt the documented <c>save_failed</c>; never a provider exception, never a half-recorded set, never a duplicate.
///
/// The deadlocks here are REAL and deterministic, not mocked exceptions and not lucky races. A second session holds an exclusive lock on the billing accounts table (the operation charges inside its transaction,
/// after it has recorded its set and procedures, so it blocks there while holding its other locks). Once the operation is seen waiting, the second session asks for a lock the operation holds, closing the cycle
/// at a moment the test controls. The second session runs at the higher deadlock priority, so the operation is the victim. Between attempts the policy's pause is replaced by a hook that either arms the next blocker
/// (so the retry deadlocks again) or lets the retry run, so no test depends on timing. A real <see cref="SqlException"/> 1205 for the classifier tests is captured from a deadlock between two plain connections.
/// </summary>
public class CompletionDeadlockTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
    }

    // ---------- the machinery ----------

    private static async Task ExecAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private sealed class RecordingLogger : ILogger<CompletedProcedureService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    /// <summary>The service the way a request builds it, with the retry policy and logger a test chooses.</summary>
    private Task<T> With<T>(DeadlockRetryPolicy policy, RecordingLogger log, Func<CompletedProcedureService, Task<T>> action) => WithDb(db =>
        action(new CompletedProcedureService(db, Clock, new TreatmentPlanService(db, Clock, CatalogFor(db)), new OdontogramService(db, Clock), new BillingService(db, Clock), log, policy)));

    private sealed record Run(Exception? Outcome, int Retries, RecordingLogger Log);

    /// <summary>
    /// Runs <paramref name="start"/> against <paramref name="cycles"/> engineered deadlocks in a row: attempt k (k up to cycles) is made the victim; the attempt after that, if the policy allows one, runs freely.
    /// <paramref name="beforeRetry"/> runs in the pause between a failed attempt and the next one (after the blocker of the failed attempt is gone).
    /// </summary>
    private async Task<Run> RunAgainstEngineeredDeadlocksAsync(Func<DeadlockRetryPolicy, RecordingLogger, Task> start, int cycles, int maxAttempts = 3, Func<int, Task>? beforeRetry = null)
    {
        var armed = new TaskCompletionSource<SqlConnection>[cycles + 1];
        for (var i = 0; i < armed.Length; i++) armed[i] = new TaskCompletionSource<SqlConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retries = 0;
        var log = new RecordingLogger();

        async Task<SqlConnection> ArmAsync()
        {
            var other = new SqlConnection(Fixture.ConnectionString);
            await other.OpenAsync();
            await ExecAsync(other, "SET DEADLOCK_PRIORITY HIGH; BEGIN TRAN; SELECT COUNT(*) FROM BillingAccounts WITH (TABLOCKX, HOLDLOCK);");
            return other;
        }

        var policy = new DeadlockRetryPolicy(maxAttempts, delay: async (_, _) =>
        {
            retries++;
            if (beforeRetry is not null) await beforeRetry(retries);
            if (retries < cycles) armed[retries].SetResult(await ArmAsync());                  // the next attempt will run into a new blocker
        });

        armed[0].SetResult(await ArmAsync());
        await using var watcher = new SqlConnection(Fixture.ConnectionString);
        await watcher.OpenAsync();
        var run = start(policy, log);
        try
        {
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                var other = await armed[cycle].Task.WaitAsync(TimeSpan.FromSeconds(20));
                var spid = await ScalarAsync(other, "SELECT @@SPID");
                var blocked = 0;
                for (var i = 0; i < 400 && blocked == 0; i++)                                                      // at most 10 s: then the operation never reached the blocker and the test fails with a clear cause
                {
                    blocked = await ScalarAsync(watcher, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {spid}");
                    if (blocked == 0) await Task.Delay(25);
                }
                Assert.True(blocked > 0, $"attempt {cycle + 1}: the operation did not reach the point where it waits for the billing accounts (it ended early: {run.IsCompleted})");
                await ExecAsync(other, "SELECT COUNT(*) FROM CompletedProcedures WITH (TABLOCKX);");            // asks for what the operation holds: the cycle closes and the server picks the operation as the victim
                await ExecAsync(other, "IF @@TRANCOUNT > 0 ROLLBACK;");
                await other.DisposeAsync();
            }
        }
        finally
        {
            foreach (var a in armed.Where(a => a.Task.IsCompletedSuccessfully))
            {
                try { await ExecAsync(a.Task.Result, "IF @@TRANCOUNT > 0 ROLLBACK;"); }
                catch (InvalidOperationException) { /* the connection was already closed above */ }
            }
        }
        var outcome = await run.ContinueWith(t => t.Exception?.GetBaseException());
        return new Run(outcome, retries, log);
    }

    private Task<CompletionResult> Submit(DeadlockRetryPolicy policy, RecordingLogger log, PlanView plan, int[] items, string key) =>
        With(policy, log, s => s.CompleteAsync(plan.Id, RequestFor(plan, items, AnnEncounter, _provider.Id, key, Performed), S.Actor, default));

    private async Task<(int Sets, int Procedures, int Charges, int Adopted)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.CompletionSets.CountAsync(), await db.CompletedProcedures.CountAsync(), await db.BillingEntries.CountAsync(), await db.CompletionSets.CountAsync(s => s.AdoptionReason != null));
    }

    private DateTimeOffset? _performed;
    /// <summary>One performed time per test, taken the first time it is needed (after the plan exists), so a repeated request is the same request.</summary>
    private DateTimeOffset Performed => _performed ??= DateTimeOffset.UtcNow.AddSeconds(2);

    private async Task<PlanView> LegacyPlanAsync()
    {
        var plan = await AcceptedPlanAsync(2);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        return await PlanNowAsync(plan.Id);
    }

    private Task<CompletionResult> Adopt(DeadlockRetryPolicy policy, RecordingLogger log, PlanView plan, string key = "adopt-1") =>
        With(policy, log, s => s.AdoptAsync(plan.Id, plan.Items[0].Id, new AdoptCompletionRequest(key, plan.RowVersion, AnnEncounter, _provider.Id, Performed, "Completed before procedure records existed"), S.Actor, default));

    // ---------- a completion that is a deadlock victim ----------

    [Fact]
    public async Task A_completion_that_is_a_deadlock_victim_is_run_again_and_succeeds_exactly_once()
    {
        var plan = await AcceptedPlanAsync(3);
        CompletionResult? result = null;

        var run = await RunAgainstEngineeredDeadlocksAsync(async (policy, log) => result = await Submit(policy, log, plan, [0, 1], "deadlock-1"), cycles: 1);

        Assert.Null(run.Outcome);                                                                                  // the retry succeeded
        Assert.Equal(1, run.Retries);                                                                              // and a deadlock really happened: the policy was asked to retry once
        Assert.Contains(run.Log.Messages, m => m.StartsWith("completion_deadlock_retry operation=completion attempt=1 max_attempts=3 error_class=DeadlockVictim"));
        Assert.False(result!.Replayed);
        Assert.Equal((1, 2, 2, 0), await CountsAsync());                                                           // one set, two procedures, two charges: nothing twice, nothing missing
        var again = await Submit(new DeadlockRetryPolicy(), run.Log, plan, [0, 1], "deadlock-1");
        Assert.True(again.Replayed);                                                                               // the same key now replays the saved set
        Assert.Equal(result.SetId, again.SetId);
        Assert.Equal((1, 2, 2, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_completion_that_is_a_deadlock_victim_on_every_attempt_ends_in_save_failed_and_records_nothing()
    {
        var plan = await AcceptedPlanAsync(3);
        var before = await SnapshotAsync(plan.Id);

        var run = await RunAgainstEngineeredDeadlocksAsync((policy, log) => Submit(policy, log, plan, [0, 1], "deadlock-2"), cycles: 3);

        var refused = Assert.IsType<TreatmentPlanException>(run.Outcome);                                          // the documented, safe-to-show failure: never a provider exception
        Assert.Equal(("save_failed", 503), (refused.Code, refused.StatusCode));
        Assert.Contains("database was busy, so nothing was completed", refused.Message);
        Assert.DoesNotContain("deadlock", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", refused.Message);
        Assert.Equal(2, run.Retries);                                                                              // three attempts in all
        Assert.Contains(run.Log.Messages, m => m.StartsWith("completion_deadlock_retries_exhausted operation=completion attempts=3"));
        Assert.True(DeadlockVictim.Is(refused.InnerException));                                                    // the cause is kept for the log, not shown
        Assert.Equal(before, await SnapshotAsync(plan.Id));                                                        // the server rolled every attempt back: nothing of any of them remains
    }

    [Fact]
    public async Task After_a_deadlock_the_retry_meets_the_winners_result_so_exactly_one_set_wins_and_nothing_is_duplicated()
    {
        var plan = await AcceptedPlanAsync(3);

        // while the victim waits to retry, an overlapping request commits (item 1 is in both)
        var run = await RunAgainstEngineeredDeadlocksAsync((policy, log) => Submit(policy, log, plan, [0, 1], "victim"), cycles: 1,
            beforeRetry: async _ => await Submit(new DeadlockRetryPolicy(), new RecordingLogger(), plan, [1, 2], "winner"));

        Assert.True(run.Outcome is ConcurrencyConflictException or ItemsNotCompletableException, run.Outcome?.ToString());     // the documented outcome of losing a race, not a provider exception
        Assert.Equal((1, 2, 2, 0), await CountsAsync());                                                           // only the winner's set: two procedures, two charges
        await using var db = Fixture.CreateContext();
        var completed = await db.TreatmentPlanItemStates.Where(s => s.Completion == ItemCompletion.Completed).Select(s => s.ItemId).ToListAsync();
        Assert.Equal(completed.Distinct().Count(), completed.Count);
        Assert.Equal(completed.Count, await db.CompletedProcedures.CountAsync());
    }

    [Fact]
    public async Task After_a_deadlock_a_double_submit_of_the_same_request_replays_the_saved_set_instead_of_recording_it_twice()
    {
        var plan = await AcceptedPlanAsync(3);
        CompletionResult? result = null;

        // while the victim waits to retry, the identical request (same key) commits
        var run = await RunAgainstEngineeredDeadlocksAsync(async (policy, log) => result = await Submit(policy, log, plan, [0, 1], "same-key"), cycles: 1,
            beforeRetry: async _ => await Submit(new DeadlockRetryPolicy(), new RecordingLogger(), plan, [0, 1], "same-key"));

        Assert.Null(run.Outcome);
        Assert.True(result!.Replayed);                                                                             // the retry found the saved set and changed nothing
        Assert.Equal((1, 2, 2, 0), await CountsAsync());
    }

    // ---------- an adoption that is a deadlock victim ----------

    [Fact]
    public async Task An_adoption_that_is_a_deadlock_victim_is_run_again_and_succeeds_exactly_once()
    {
        var plan = await LegacyPlanAsync();
        CompletionResult? result = null;

        var run = await RunAgainstEngineeredDeadlocksAsync(async (policy, log) => result = await Adopt(policy, log, plan), cycles: 1);

        Assert.Null(run.Outcome);
        Assert.Equal(1, run.Retries);
        Assert.Contains(run.Log.Messages, m => m.StartsWith("completion_deadlock_retry operation=adoption attempt=1"));
        Assert.False(result!.Replayed);
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task An_adoption_that_is_a_deadlock_victim_on_every_attempt_ends_in_save_failed_and_records_nothing()
    {
        var plan = await LegacyPlanAsync();
        var before = await SnapshotAsync(plan.Id);

        var run = await RunAgainstEngineeredDeadlocksAsync((policy, log) => Adopt(policy, log, plan, "adopt-2"), cycles: 3);

        var refused = Assert.IsType<TreatmentPlanException>(run.Outcome);
        Assert.Equal(("save_failed", 503), (refused.Code, refused.StatusCode));
        Assert.Contains("database was busy, so nothing was adopted", refused.Message);
        Assert.Equal(2, run.Retries);
        Assert.Equal(before, await SnapshotAsync(plan.Id));
    }

    // ---------- what is not retried ----------

    [Fact]
    public async Task A_domain_refusal_is_not_retried_and_passes_through_unchanged()
    {
        var plan = await AcceptedPlanAsync(2);
        var retries = 0;
        var policy = new DeadlockRetryPolicy(delay: (_, _) => { retries++; return Task.CompletedTask; });
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Submit(policy, new RecordingLogger(), plan with { RowVersion = Convert.ToBase64String(new byte[8]) }, [0], "stale"));
        Assert.Equal(0, retries);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Racing_completions_of_one_plan_never_surface_a_provider_exception_however_the_server_orders_them()
    {
        Exception? escaped = null;
        for (var round = 0; round < 12 && escaped is null; round++)
        {
            var plan = await AcceptedPlanAsync(4, firstCode: 500 + round * 10);
            var outcomes = await Task.WhenAll(new[] { ("a", new[] { 0, 1 }), ("b", new[] { 1, 2 }), ("c", new[] { 2, 3 }), ("d", new[] { 0, 3 }) }
                .Select(x => Submit(new DeadlockRetryPolicy(), new RecordingLogger(), plan, x.Item2, $"stress-{round}-{x.Item1}").ContinueWith(t => t.Exception?.GetBaseException())));
            escaped = outcomes.FirstOrDefault(o => o is not null && o is not (TreatmentPlanException or ConcurrencyConflictException));        // ItemsNotCompletableException is a TreatmentPlanException
        }
        Assert.True(escaped is null, $"a racing completion escaped as {escaped?.GetType().Name}: {escaped?.Message}");
    }

    // ---------- the classifier and the policy, with a real SQL Server 1205 ----------

    /// <summary>A genuine <see cref="SqlException"/> 1205, captured from a deadlock between two plain connections.</summary>
    private async Task<SqlException> RealDeadlockAsync()
    {
        await using var setup = new SqlConnection(Fixture.ConnectionString);
        await setup.OpenAsync();
        await ExecAsync(setup, "IF OBJECT_ID('dl_a') IS NULL BEGIN CREATE TABLE dl_a (id int PRIMARY KEY, v int); INSERT dl_a VALUES (1, 0); CREATE TABLE dl_b (id int PRIMARY KEY, v int); INSERT dl_b VALUES (1, 0); END");
        await using var winner = new SqlConnection(Fixture.ConnectionString);
        await using var victim = new SqlConnection(Fixture.ConnectionString);
        await winner.OpenAsync();
        await victim.OpenAsync();
        await ExecAsync(victim, "SET DEADLOCK_PRIORITY LOW; BEGIN TRAN; UPDATE dl_b SET v = 1 WHERE id = 1;");
        await ExecAsync(winner, "SET DEADLOCK_PRIORITY HIGH; BEGIN TRAN; UPDATE dl_a SET v = 1 WHERE id = 1;");
        var winnerSpid = await ScalarAsync(winner, "SELECT @@SPID");
        var winnerWaits = ExecAsync(winner, "UPDATE dl_b SET v = 2 WHERE id = 1;");
        for (var i = 0; i < 400 && await ScalarAsync(victim, $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = {winnerSpid} AND blocking_session_id <> 0") == 0; i++) await Task.Delay(25);
        try
        {
            await ExecAsync(victim, "UPDATE dl_a SET v = 2 WHERE id = 1;");
            throw new InvalidOperationException("no deadlock formed");
        }
        catch (SqlException e) { return e; }
        finally { await winnerWaits; await ExecAsync(winner, "COMMIT"); }
    }

    [Fact]
    public async Task The_classifier_recognises_a_real_1205_however_it_is_wrapped_and_nothing_else()
    {
        var real = await RealDeadlockAsync();
        Assert.Equal(1205, real.Number);
        Assert.True(DeadlockVictim.Is(real));
        Assert.True(DeadlockVictim.Is(new DbUpdateException("save", real)));
        Assert.True(DeadlockVictim.Is(new InvalidOperationException("transient", new DbUpdateException("save", real))));        // the chain EF's execution strategy produces
        Assert.True(DeadlockVictim.Is(new AggregateException(new InvalidOperationException("x"), new InvalidOperationException("transient", real))));

        await using var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        var divide = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(c, "SELECT 1 / 0"));
        Assert.NotEqual(1205, divide.Number);
        Assert.False(DeadlockVictim.Is(divide));
        Assert.False(DeadlockVictim.Is(new InvalidOperationException("transient", new DbUpdateException("save", divide))));
        Assert.False(DeadlockVictim.Is(new TimeoutException()));
        Assert.False(DeadlockVictim.Is(null));
    }

    [Fact]
    public async Task The_policy_retries_only_a_deadlock_victim_a_bounded_number_of_times_and_wraps_the_last_failure_when_it_gives_up()
    {
        var real = await RealDeadlockAsync();
        var observed = new List<int>();
        var paused = new List<TimeSpan>();
        var policy = new DeadlockRetryPolicy(3, (p, _) => { paused.Add(p); return Task.CompletedTask; }, (n, _) => observed.Add(n));

        var calls = 0;
        Assert.Equal("done", await policy.RunAsync(() => ++calls < 3 ? throw new InvalidOperationException("transient", new DbUpdateException("save", real)) : Task.FromResult("done"), default));
        Assert.Equal(3, calls);
        Assert.Equal([1, 2], observed);
        Assert.Equal(2, paused.Count);
        Assert.InRange(paused[0].TotalMilliseconds, 40, 80);                                                       // grows with each failure and carries jitter
        Assert.InRange(paused[1].TotalMilliseconds, 80, 120);

        calls = 0;
        var gaveUp = await Assert.ThrowsAsync<DeadlockRetriesExhaustedException>(() => policy.RunAsync<string>(() => { calls++; throw real; }, default));
        Assert.Equal((3, 3), (calls, gaveUp.Attempts));
        Assert.Same(real, gaveUp.InnerException);

        calls = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => policy.RunAsync<string>(() => { calls++; throw new ArgumentException("not a deadlock"); }, default));
        Assert.Equal(1, calls);                                                                                    // anything else passes through on the first attempt
        calls = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => policy.RunAsync<string>(() => { calls++; throw new OperationCanceledException(); }, default));
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeadlockRetryPolicy(0));
    }
}
