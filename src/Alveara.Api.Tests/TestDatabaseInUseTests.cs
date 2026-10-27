using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-017-C01 R02 (review finding ALV-017-C01-R01-02): the one cleanup failure of the R01 Stage 2 run 2 was SQL Server error 3702, "Cannot drop database ... because it is currently in use", thrown by the
/// fixture's drop and not retried. These tests meet a REAL error 3702 (not a fake): a test-only hook makes one attempt of the drop use a plain DROP DATABASE while another session is connected to the database,
/// which is exactly what SQL Server refuses with 3702. They prove that the fixture reports who holds the database (safe metadata only), recovers by retrying the real drop (ALTER ... ROLLBACK IMMEDIATE removes the
/// holder) and leaves neither the database nor its files behind, that the retry is bounded and a holder that never leaves is still a failure, and that disposing the API harness waits for the host's background
/// services to stop (the lifecycle part of the fix). None of them asserts how long anything takes.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class TestDatabaseInUseTests
{
    private const string HolderProgram = "R02-holder-3702";

    private static string MasterCs => AlveraTestEnvironment.MasterConnectionString;

    private static string DatabaseOf(TestDatabaseFixture f) => new SqlConnectionStringBuilder(f.ConnectionString).InitialCatalog;

    private static async Task<bool> ExistsAsync(string database)
    {
        await using var c = new SqlConnection(MasterCs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN DB_ID(@n) IS NULL THEN 0 ELSE 1 END";
        cmd.Parameters.AddWithValue("@n", database);
        return (int)(await cmd.ExecuteScalarAsync())! == 1;
    }

    private static async Task<List<string>> FilesOfAsync(string database)
    {
        await using var c = new SqlConnection(MasterCs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(@n)";
        cmd.Parameters.AddWithValue("@n", database);
        var files = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) files.Add(reader.GetString(0));
        return files;
    }

    /// <summary>Removes a database a test left on purpose (the exhausted-retries case), the way the fixture's own drop does.</summary>
    private static async Task ForceDropAsync(string database)
    {
        await using var c = new SqlConnection(MasterCs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A second session connected to the fixture's database under its own program name and its own pool, so it stays connected through the fixture's release of pooled connections.</summary>
    private static async Task<SqlConnection> HoldAsync(TestDatabaseFixture f)
    {
        var holder = new SqlConnection(new SqlConnectionStringBuilder(f.ConnectionString) { ApplicationName = HolderProgram, Pooling = false }.ConnectionString);
        await holder.OpenAsync();
        await using var cmd = holder.CreateCommand();
        cmd.CommandText = "SELECT 1";
        _ = await cmd.ExecuteScalarAsync();
        return holder;
    }

    private static void AssertNothingSensitiveLogged(string log, TestDatabaseFixture f)
    {
        Assert.DoesNotContain("Password", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Data Source", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Integrated Security", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(f.ConnectionString, log);
        Assert.DoesNotContain("DROP DATABASE", log);                                            // no statement text
        Assert.DoesNotContain("SELECT", log);
    }

    [Fact]
    public async Task A_drop_that_meets_error_3702_names_the_holder_retries_and_removes_the_database_and_its_files()
    {
        var log = new StringWriter();
        TestDatabaseFixture? fixture = null;
        fixture = new TestDatabaseFixture(logForTest: log, delayForTest: _ => Task.CompletedTask,
            dropStatementForTest: attempt => attempt == 1 ? $"DROP DATABASE [{DatabaseOf(fixture!)}];" : null);        // attempt 1: a plain DROP while another session is connected = a real 3702
        await fixture.InitializeAsync();
        var database = DatabaseOf(fixture);
        var files = await FilesOfAsync(database);
        Assert.NotEmpty(files);
        var holder = await HoldAsync(fixture);
        try
        {
            await fixture.DisposeAsync();                                                       // does not throw: the second attempt is the real drop
        }
        finally
        {
            await holder.DisposeAsync();
            if (await ExistsAsync(database)) await ForceDropAsync(database);                     // only if the test failed: never leave a database behind
        }

        Assert.False(await ExistsAsync(database));
        Assert.All(files, file => Assert.False(File.Exists(file), $"{file} was left behind"));
        var text = log.ToString();
        Assert.Contains($"drop of {database} (SQL 3702): session ", text);
        Assert.Contains($"program '{HolderProgram}'", text);                                   // the holder is identified
        Assert.Contains("(attempt 1 of 4); retrying", text);
        Assert.DoesNotContain("giving up", text);
        AssertNothingSensitiveLogged(text, fixture);
    }

    [Fact]
    public async Task A_holder_that_never_leaves_is_a_failure_after_the_bounded_attempts_with_the_first_3702_unchanged()
    {
        var log = new StringWriter();
        var attempts = 0;
        TestDatabaseFixture? fixture = null;
        fixture = new TestDatabaseFixture(logForTest: log, delayForTest: _ => Task.CompletedTask,
            dropStatementForTest: _ => { attempts++; return $"DROP DATABASE [{DatabaseOf(fixture!)}];"; });             // every attempt is refused
        await fixture.InitializeAsync();
        var database = DatabaseOf(fixture);
        var holder = await HoldAsync(fixture);
        try
        {
            var failure = await Assert.ThrowsAsync<SqlException>(() => fixture.DisposeAsync());
            Assert.Equal(TestDatabaseFixture.DatabaseInUseErrorNumber, failure.Number);          // the original error, not a replacement
            Assert.Equal(TestDatabaseFixture.MaxDropAttempts, attempts);                         // bounded: no more, no fewer
            Assert.True(await ExistsAsync(database));                                            // and it is reported, not hidden: the database is still there
        }
        finally
        {
            await holder.DisposeAsync();
            await ForceDropAsync(database);                                                      // the test cleans up what it left on purpose
        }

        Assert.False(await ExistsAsync(database));
        var text = log.ToString();
        Assert.Contains($"giving up after {TestDatabaseFixture.MaxDropAttempts} attempts", text);
        AssertHolderCheckPerAttempt(text, database, minimumSessionsPerCheck: 1);                // the holder was checked and named on every failure including the last
        AssertNothingSensitiveLogged(text, fixture);
    }

    /// <summary>
    /// ALV-018-C01 R02: the per-failure diagnostic is the numbered marker line, not the number of session lines. Asserts exactly one marker for each attempt (1..MaxDropAttempts, in order, each "of" the maximum),
    /// that each marker reports at least the expected number of sessions (SQL Server may report more), and that exactly the reported number of session lines is logged.
    /// </summary>
    private static void AssertHolderCheckPerAttempt(string text, string database, int minimumSessionsPerCheck)
    {
        var prefix = $"drop of {database} (SQL 3702): ";
        var lines = text.Split((char)10).Select(l => l.TrimEnd((char)13)).Where(l => l.Contains(prefix)).ToList();
        var markers = lines.Where(l => l.Contains("holder check ")).ToList();
        Assert.Equal(TestDatabaseFixture.MaxDropAttempts, markers.Count);                        // one diagnostic for each failed attempt, no more, no fewer
        var reported = 0;
        for (var i = 0; i < markers.Count; i++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(markers[i], @"holder check (\d+) of (\d+): (\d+) session\(s\)\.$");
            Assert.True(match.Success, $"not a holder check marker: {markers[i]}");
            Assert.Equal(i + 1, int.Parse(match.Groups[1].Value));                               // numbered 1..MaxDropAttempts, in order
            Assert.Equal(TestDatabaseFixture.MaxDropAttempts, int.Parse(match.Groups[2].Value));
            var sessions = int.Parse(match.Groups[3].Value);
            Assert.True(sessions >= minimumSessionsPerCheck, $"check {i + 1} saw {sessions} session(s), expected at least {minimumSessionsPerCheck}");   // SQL Server may report more than the test's own holders
            reported += sessions;
        }

        Assert.Equal(reported, lines.Count(l => l.Contains("holds it;")));                       // every reported session is named, however many there were
    }

    [Fact]
    public async Task Several_holders_are_each_named_and_the_diagnostic_is_still_one_check_for_each_failed_attempt()
    {
        var log = new StringWriter();
        TestDatabaseFixture? fixture = null;
        fixture = new TestDatabaseFixture(logForTest: log, delayForTest: _ => Task.CompletedTask,
            dropStatementForTest: _ => $"DROP DATABASE [{DatabaseOf(fixture!)}];");              // every attempt is refused
        await fixture.InitializeAsync();
        var database = DatabaseOf(fixture);
        var first = await HoldAsync(fixture);
        var second = await HoldAsync(fixture);                                                  // two sessions hold the database at once
        try
        {
            var failure = await Assert.ThrowsAsync<SqlException>(() => fixture.DisposeAsync());
            Assert.Equal(TestDatabaseFixture.DatabaseInUseErrorNumber, failure.Number);          // the original error is still the one reported
            Assert.True(await ExistsAsync(database));                                            // the database remains until explicit cleanup
        }
        finally
        {
            await second.DisposeAsync();
            await first.DisposeAsync();
            await ForceDropAsync(database);
        }

        Assert.False(await ExistsAsync(database));
        var text = log.ToString();
        Assert.Contains($"giving up after {TestDatabaseFixture.MaxDropAttempts} attempts", text);
        AssertHolderCheckPerAttempt(text, database, minimumSessionsPerCheck: 2);               // 4 checks, each seeing at least both holders
        AssertNothingSensitiveLogged(text, fixture);
    }

    [Fact]
    public void Only_error_3702_joins_the_deadlock_and_the_pool_timeout_as_a_transient_drop_failure()
    {
        Assert.False(TestDatabaseFixture.IsTransientDropFailure(new InvalidOperationException("Cannot drop database because it is currently in use.")));   // the words alone are not enough: it must be the SQL error
        Assert.False(TestDatabaseFixture.IsTransientDropFailure(new TimeoutException()));
        Assert.False(TestDatabaseFixture.IsDatabaseInUse(new ArgumentException("x")));
    }

    // ---------- the harness lifecycle ----------

    private sealed class SlowStopService(string connectionString) : IHostedService
    {
        private SqlConnection? _connection;
        public volatile bool Stopped;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _connection = new SqlConnection(connectionString);
            await _connection.OpenAsync(cancellationToken);                                       // a background service that holds a connection to the database while it runs
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(1500, cancellationToken);                                            // it takes a while to finish
            if (_connection is not null) await _connection.CloseAsync();
            Stopped = true;
        }
    }

    [Fact]
    public async Task Disposing_the_harness_returns_only_after_the_hosts_background_services_have_stopped_and_the_database_is_then_dropped()
    {
        var fixture = new RealMigrationTestDatabaseFixture();
        await fixture.InitializeAsync();
        var database = DatabaseOf(fixture);
        var service = new SlowStopService(new SqlConnectionStringBuilder(fixture.ConnectionString) { ApplicationName = "R02-slow-stop", Pooling = false }.ConnectionString);
        var harness = new SchedulingApiHarness(fixture);
        try
        {
            using var client = harness.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IHostedService>(service))).CreateClient();    // starts a host with the slow service
            harness.Dispose();

            Assert.True(service.Stopped, "Dispose returned while a background service was still running");
        }
        finally
        {
            harness.Dispose();
            await fixture.DisposeAsync();
        }

        Assert.False(await ExistsAsync(database));
    }

    /// <summary>A policy observer that takes a while and uses the database while it does: the invalid-row report that was still running when a host stopped.</summary>
    private sealed class SlowObserver(string connectionString) : IPolicyObserver
    {
        public volatile bool Started;
        public volatile bool Finished;

        public async Task OnPublishedAsync(PolicySnapshot snapshot, CancellationToken ct)
        {
            Started = true;
            await Task.Delay(1500, CancellationToken.None);                                       // the report outlives the response that started it
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);                                   // and then reads the database, as the audit report does
            Finished = true;
        }
    }

    [Fact]
    public async Task A_policy_report_still_running_when_the_host_is_disposed_is_finished_first_so_the_database_drops_without_a_holder()
    {
        var log = new StringWriter();
        var fixture = new TestDatabaseFixture(logForTest: log);
        await fixture.InitializeAsync();
        var database = DatabaseOf(fixture);
        var observer = new SlowObserver(new SqlConnectionStringBuilder(fixture.ConnectionString) { ApplicationName = "R02-slow-report", Pooling = false }.ConnectionString);
        var harness = new SchedulingApiHarness(fixture) { ConfigureServices = s => s.AddSingleton<IPolicyObserver>(observer) };
        try
        {
            var admin = await harness.SessionAsync("Admin");
            var dentist = await harness.SessionAsync("Dentist");
            await using (var db = fixture.CreateContext())
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
                db.RolePermissionOverrides.Add(new RolePermissionOverride
                {
                    Id = Guid.NewGuid(), Role = "Wizard", Permission = "ViewBilling", Effect = OverrideEffects.Revoke, Reason = "corrupt", ChangedByUserId = admin.UserId, ChangedAtUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            var response = await dentist.SendAsync(HttpMethod.Get, "/api/patients");              // reloads the policy: it is degraded, so the report starts in the background
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.Item1);
            for (var waited = 0; !observer.Started && waited < 5000; waited += 20) await Task.Delay(20);        // the report starts on a background task after the response: wait (bounded) for it to begin
            Assert.True(observer.Started, "the degraded policy should have started a report");

            harness.Dispose();                                                                    // right away, while the report is still running
            Assert.True(observer.Finished, "the host was disposed while its policy report was still running");
        }
        finally
        {
            harness.Dispose();
            await fixture.DisposeAsync();
        }

        Assert.False(await ExistsAsync(database));
        Assert.DoesNotContain("SQL 3702", log.ToString());                                         // nobody held the database: no retry was needed
    }
}
