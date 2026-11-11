using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N019 R04: proof that the fixture's pool warm-up protects the first pooled open that the owner's setup makes after the fixture is released, against the REAL SqlClient pool-pruning condition
/// (see <see cref="SqlClientPoolPruner"/>; no injected exception stands in for it). A cycle is one real fixture lifecycle: a background loop ticks the pruning callback every 20 ms, the fixture restores a clone
/// of the run's template and is initialised, then the "owner" makes its first pooled open, then the fixture is disposed. The CONTROL arm turns the warm-up off (the test-only switch) and must show the
/// authentic first-open timeout; the TREATMENT arm (warm-up on) must show every first open succeeding and zero fixture databases and files after every iteration.
/// The default sizes are small so the ordinary suite stays fast; the preflight (scripts/Invoke-FixturePreflight.ps1) sets the environment variables below for the 800-cycle control and the 60 x 15 treatment.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class FixtureForcedPruningTests(Xunit.Abstractions.ITestOutputHelper output)
{
    internal const string ControlCyclesVariable = "ALVERA_R04_CONTROL_CYCLES";
    internal const string IterationsVariable = "ALVERA_R04_ITERATIONS";
    internal const string CyclesVariable = "ALVERA_R04_CYCLES";
    internal const string RequireControlFailureVariable = "ALVERA_R04_REQUIRE_CONTROL_FAILURE";
    internal const string OutputFolderVariable = "ALVERA_R04_PREFLIGHT_OUT";
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(20);

    internal sealed record CycleResult(int Cycle, string Outcome, int InitializationRetries, int WarmUpRetries, double Milliseconds);
    internal sealed record IterationResult(string Arm, int Iteration, int Cycles, int Ok, int FirstOpenTimeouts, int InitFailures, int DatabasesAfter, int FilesAfter, bool Passed, List<CycleResult> Detail);

    private static int Int(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v >= 0 ? v : fallback;

    [Fact]
    public void The_real_pruning_mechanism_is_found_in_the_installed_SqlClient()
    {
        Assert.True(SqlClientPoolPruner.IsAvailable, SqlClientPoolPruner.Describe());
        SqlClientPoolPruner.Tick();                                                       // one tick runs without throwing
    }

    [Fact]
    public async Task Control_arm_without_the_warm_up_reaches_the_real_first_open_timeout_when_the_preflight_demands_it()
    {
        var cycles = Int(ControlCyclesVariable, 0);
        var results = await RunArmAsync("control", warmUp: false, iterations: 1, cyclesPerIteration: cycles);
        var timeouts = results.Sum(r => r.FirstOpenTimeouts);
        output.WriteLine($"control arm: {cycles} cycles, {timeouts} authentic first-open pool-acquisition timeouts");
        if (Environment.GetEnvironmentVariable(RequireControlFailureVariable) == "1")
        {
            Assert.True(cycles >= 800, "the control arm must run at least 800 forced cycles");
            Assert.True(timeouts >= 1, $"the warm-up-disabled control arm showed no first-open timeout in {cycles} forced cycles, so the forcing is not proven to reach the race");
        }
    }

    [Fact]
    public async Task Treatment_arm_with_the_warm_up_succeeds_at_every_first_open_and_leaves_nothing_after_every_iteration()
    {
        var iterations = Int(IterationsVariable, 2);
        var cycles = Int(CyclesVariable, 3);
        var results = await RunArmAsync("treatment", warmUp: true, iterations, cycles);
        Assert.Equal(iterations, results.Count);
        foreach (var r in results)
        {
            Assert.True(r.Passed, $"iteration {r.Iteration}: ok {r.Ok} of {r.Cycles}, first-open timeouts {r.FirstOpenTimeouts}, initialisation failures {r.InitFailures}, databases left {r.DatabasesAfter}, files left {r.FilesAfter}");
        }
    }

    private async Task<List<IterationResult>> RunArmAsync(string arm, bool warmUp, int iterations, int cyclesPerIteration)
    {
        if (cyclesPerIteration > 0 && iterations > 0) await TestDatabaseTemplate.Default.EnsureBuiltAsync();       // the template is built BEFORE any forced tick: the proof concerns the first open of a clone, not the one-off template build
        var results = new List<IterationResult>();
        var folder = Environment.GetEnvironmentVariable(OutputFolderVariable);
        StreamWriter? raw = null;
        if (!string.IsNullOrWhiteSpace(folder) && cyclesPerIteration > 0 && iterations > 0)
        {
            Directory.CreateDirectory(folder);
            raw = new StreamWriter(Path.Combine(folder, arm + ".jsonl"), append: false);
        }

        try
        {
            for (var i = 1; i <= iterations; i++)
            {
                var detail = new List<CycleResult>();
                await using (SqlClientPoolPruner.StartTicking(TickInterval))
                {
                    for (var c = 1; c <= cyclesPerIteration; c++) detail.Add(await RunCycleAsync(c, warmUp));
                }

                var (databases, files) = await LeftoversAsync();
                var ok = detail.Count(d => d.Outcome == "ok");
                var timeouts = detail.Count(d => d.Outcome == "first_open_pool_timeout");
                var initFailures = detail.Count(d => d.Outcome.StartsWith("init_failed", StringComparison.Ordinal));
                var passed = ok == cyclesPerIteration && databases == 0 && files == 0;
                var result = new IterationResult(arm, i, cyclesPerIteration, ok, timeouts, initFailures, databases, files, passed, detail);
                results.Add(result);
                if (raw is not null)
                {
                    await raw.WriteLineAsync(JsonSerializer.Serialize(result));
                    await raw.FlushAsync();
                }
                output.WriteLine($"{arm} iteration {i}/{iterations}: cycles {cyclesPerIteration}, ok {ok}, first-open timeouts {timeouts}, init failures {initFailures}, fixture databases left {databases}, files left {files}");
            }
        }
        finally
        {
            raw?.Dispose();
        }

        return results;
    }

    private static async Task<CycleResult> RunCycleAsync(int cycle, bool warmUp)
    {
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(logForTest: log, templateForTest: TestDatabaseTemplate.Default, warmUp: warmUp);
        var started = DateTime.UtcNow;
        string outcome;
        try
        {
            try
            {
                await fixture.InitializeAsync();
                try
                {
                    // the owner's first pooled open after the fixture was released (the open that failed in both R03 Stage 2 runs)
                    await using var context = fixture.CreateContext();
                    await context.Database.OpenConnectionAsync();
                    await context.Database.CloseConnectionAsync();
                    outcome = "ok";
                }
                catch (Exception ex) when (TestDatabaseFixture.IsPoolAcquisitionTimeout(ex))
                {
                    outcome = "first_open_pool_timeout";
                }
            }
            catch (Exception ex)
            {
                outcome = "init_failed:" + ex.GetType().Name + (ex is TemplateBuildException ? " (" + ex.Message + (ex.InnerException is null ? "" : "; inner " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message) + ")" : "");
            }
        }
        finally
        {
            try { await fixture.DisposeAsync(); } catch (SqlException) { /* a failed drop shows up in the leftovers check right after */ }
        }

        var text = log.ToString();
        var initRetries = text.Split('\n').Count(l => l.Contains("initialisation of", StringComparison.Ordinal) && l.Contains("retrying", StringComparison.Ordinal));
        var warmRetries = text.Split('\n').Count(l => l.Contains("pool warm-up of", StringComparison.Ordinal) && l.Contains("retrying", StringComparison.Ordinal));
        return new CycleResult(cycle, outcome, initRetries, warmRetries, Math.Round((DateTime.UtcNow - started).TotalMilliseconds));
    }

    /// <summary>Independent read-only check: fixture databases on the server and the files in the test database folder.</summary>
    private static async Task<(int Databases, int Files)> LeftoversAsync()
    {
        await using var connection = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'AlveraTest[_]%'";
        var databases = (int)(await command.ExecuteScalarAsync())!;
        var (dataFile, _) = AlveraTestEnvironment.FilesOf("AlveraTest_" + new string('0', 32));
        var folder = Path.GetDirectoryName(dataFile)!;
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "AlveraTest_*").Length : 0;
        return (databases, files);
    }
}
