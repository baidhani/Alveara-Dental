using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N019 R04 guards that need no database: (1) the repository scan that keeps every fixture OWNER on <see cref="TestDatabaseFixture.InitializeOwnerAsync"/> (so a new owner cannot bring back the leak where
/// a setup failure after the fixture is ready left a database and two files behind), and (2) the narrow classification of the warm-up retry: only the known pool-acquisition timeout is retried.
/// </summary>
public class FixtureOwnerGuardTests
{
    private static readonly Regex RawInitialise = new(@"await\s+\w*[Ff]ixture\.InitializeAsync\(\)\s*;", RegexOptions.Compiled);

    /// <summary>Files that MAY call the fixture's initialisation directly, each for a stated reason and each required to put a try right after it.</summary>
    private static readonly string[] DirectCallers =
    [
        "LeastPrivilegeLoginCleanupTests.cs",   // builds the fixture inline in each test method and wraps everything after the call in try/finally with its own DisposeAsync
        "FixtureForcedPruningTests.cs",         // the proof loop disposes every cycle's fixture in a finally
    ];

    private static string TestProjectFolder([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    [Fact]
    public void No_test_class_runs_setup_after_the_fixture_without_the_owner_wrapper()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(TestProjectFolder(), "*.cs"))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("TestDatabase", StringComparison.Ordinal)) continue;                     // the tests OF the fixture initialise fixtures deliberately
            var text = File.ReadAllText(file);
            foreach (Match m in RawInitialise.Matches(text))
            {
                var after = text[(m.Index + m.Length)..].TrimStart();
                var allowed = DirectCallers.Contains(name) && after.StartsWith("try", StringComparison.Ordinal);
                if (!allowed) offenders.Add($"{name}: '{m.Value}' is followed by '{after[..Math.Min(40, after.Length)].Replace('\n', ' ')}'");
            }
        }

        Assert.True(offenders.Count == 0, "Call the owner wrapper instead (fixture.InitializeOwnerAsync(async () => { ...setup... })) so a failing setup removes the clone:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_shared_SafetyTestBase_and_the_named_failing_paths_use_the_owner_wrapper()
    {
        Assert.Contains("Fixture.InitializeOwnerAsync(", File.ReadAllText(Path.Combine(TestProjectFolder(), "SafetyTestBase.cs")));
        Assert.Contains("_fixture.InitializeOwnerAsync(", File.ReadAllText(Path.Combine(TestProjectFolder(), "ImportIntakeServiceTests.cs")));
    }

    private static Task<(int Calls, Exception? Thrown)> RunAsync(Func<int, Exception?> failureOnCall)
    {
        return Run();

        async Task<(int, Exception?)> Run()
        {
            var calls = 0;
            try
            {
                await TestDatabaseFixture.RunWithTransientRetryAsync(() =>
                {
                    calls++;
                    var failure = failureOnCall(calls);
                    return failure is null ? Task.CompletedTask : Task.FromException(failure);
                }, TestDatabaseFixture.IsPoolAcquisitionTimeout, "pool warm-up", "AlveraTest_demo", TestDatabaseFixture.MaxInitializationAttempts, TestDatabaseFixture.InitializationRetryDelays, _ => Task.CompletedTask, new StringWriter(), beforeRetry: null);
                return (calls, null);
            }
            catch (Exception ex)
            {
                return (calls, ex);
            }
        }
    }

    private static InvalidOperationException PoolTimeout() => new("Timeout expired.  The timeout period elapsed prior to obtaining a connection from the pool.  This may have occurred because all pooled connections were in use and max pool size was reached.");

    [Fact]
    public async Task The_warm_up_retry_recovers_from_the_pool_timeout_and_is_bounded_and_rethrows_the_first_exception()
    {
        var (recoveredCalls, recoveredThrown) = await RunAsync(call => call < 3 ? PoolTimeout() : null);
        Assert.Equal(3, recoveredCalls);
        Assert.Null(recoveredThrown);

        var first = PoolTimeout();
        var (exhaustedCalls, exhaustedThrown) = await RunAsync(call => call == 1 ? first : PoolTimeout());
        Assert.Equal(TestDatabaseFixture.MaxInitializationAttempts, exhaustedCalls);
        Assert.Same(first, exhaustedThrown);                                                              // the FIRST exception, unchanged
    }

    private static Exception ThrownBy(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("the action did not throw");
    }

    public static TheoryData<string> NonPoolFailures() => new() { "product", "other-invalid-operation", "timeout", "assertion", "io", "marker-with-pool-text" };

    [Theory]
    [MemberData(nameof(NonPoolFailures))]
    public async Task The_warm_up_retry_does_not_retry_product_sql_invalid_operation_or_assertion_failures(string kind)
    {
        Exception failure = kind switch
        {
            "product" => new ArgumentException("a product error"),
            "other-invalid-operation" => new InvalidOperationException("The connection pool has been exhausted."),
            "timeout" => new TimeoutException("Timeout expired."),
            "assertion" => ThrownBy(() => Assert.Equal(1, 2)),
            "io" => new IOException("disk"),
            _ => new ApplicationException("Timeout expired.  The timeout period elapsed prior to obtaining a connection from the pool."),   // the words alone, from a different exception type
        };

        var (calls, thrown) = await RunAsync(_ => failure);

        Assert.Equal(1, calls);                                                                          // never retried
        Assert.Same(failure, thrown);                                                                    // and never replaced
    }

    [Fact]
    public async Task The_warm_up_retry_does_not_retry_an_arbitrary_SQL_error()
    {
        await using var connection = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1/0";                                                              // a real SqlException that is not a pool timeout (error 8134)
        var real = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteScalarAsync());

        var (calls, thrown) = await RunAsync(_ => real);

        Assert.Equal(1, calls);
        Assert.Same(real, thrown);
    }
}
