using System.Runtime.ExceptionServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// Creates a uniquely-named real SQL Server (LocalDB) database per test class, applies the actual
/// EF Core migrations against it, and drops it afterward. This exercises the real migration
/// pipeline and real SQL Server transaction/constraint behavior — not an in-memory provider,
/// which does not enforce unique indexes, foreign keys, or real transaction semantics the same
/// way and would not actually prove ALV-N002's migration/transaction acceptance items.
/// </summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private const string MasterConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>SQL Server "chosen as the deadlock victim".</summary>
    internal const int DeadlockErrorNumber = 1205;

    /// <summary>TESTSPEED-P2: at most this many attempts in total (the first try plus three retries) for the drop.</summary>
    internal const int MaxDropAttempts = 4;

    /// <summary>TESTSPEED-P2: at most this many attempts in total (the first try plus two retries) for the creation and migration of the database.</summary>
    internal const int MaxInitializationAttempts = 3;

    /// <summary>Waits before drop retry 1, 2 and 3.</summary>
    internal static readonly TimeSpan[] DropRetryDelays = [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)];

    /// <summary>Waits before initialisation retry 1 and 2: none, because the cleanup that runs first takes longer than the race it recovers from.</summary>
    internal static readonly TimeSpan[] InitializationRetryDelays = [TimeSpan.Zero, TimeSpan.Zero];

    /// <summary>The start of the message SqlClient uses for its pool-acquisition timeout (whitespace normalised before comparing).</summary>
    private const string PoolAcquisitionTimeoutPrefix = "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.";

    private readonly string _databaseName = $"AlveraTest_{Guid.NewGuid():N}";

    // Test-only hooks. They are set only through the internal constructor below (xUnit constructs fixtures through the public parameterless one), so they cannot change production behaviour.
    private readonly Func<Task>? _beforeCreateForTest;
    private readonly Func<Task>? _afterMigrateForTest;
    private readonly Func<string, Task>? _cleanupDropForTest;
    private readonly Func<Task>? _beforeMasterOpenForTest;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly TextWriter _log;

    public string ConnectionString { get; private set; } = string.Empty;

    public TestDatabaseFixture()
    {
        _delay = Task.Delay;
        _log = Console.Error;
    }

    /// <summary>
    /// Test-only: lets the remediation tests force a failure before the database is created or after it is migrated (both hooks run on EVERY attempt), fail the opening of the master connection
    /// that every drop starts with, replace the cleanup drop, record the waits instead of sleeping, and capture the log.
    /// </summary>
    internal TestDatabaseFixture(Func<Task>? beforeCreateForTest = null, Func<Task>? afterMigrateForTest = null, Func<string, Task>? cleanupDropForTest = null, TextWriter? logForTest = null,
        Func<Task>? beforeMasterOpenForTest = null, Func<TimeSpan, Task>? delayForTest = null)
    {
        _beforeCreateForTest = beforeCreateForTest;
        _afterMigrateForTest = afterMigrateForTest;
        _cleanupDropForTest = cleanupDropForTest;
        _beforeMasterOpenForTest = beforeMasterOpenForTest;
        _delay = delayForTest ?? Task.Delay;
        _log = logForTest ?? Console.Error;
    }

    public async Task InitializeAsync()
    {
        ConnectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AlveraDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        try
        {
            // SqlClient prunes empty connection pools from a timer; if a tick lands while EF polls for the database it has just created (the pool of this connection string is still empty), the open
            // fails with the pool-acquisition timeout message (see IsPoolAcquisitionTimeout). Nothing durable is lost by that, so creation is retried a bounded number of times, each retry after
            // removing the half-created database; when the attempts run out the FIRST failure is rethrown unchanged.
            await RunWithTransientRetryAsync(async () =>
            {
                if (_beforeCreateForTest is not null) await _beforeCreateForTest();

                await using var db = new AlveraDbContext(options);
                await db.Database.MigrateAsync();

                if (_afterMigrateForTest is not null) await _afterMigrateForTest();
            }, IsPoolAcquisitionTimeout, "initialisation", _databaseName, MaxInitializationAttempts, InitializationRetryDelays, _delay, _log, CleanUpFailedInitializationAsync);
        }
        catch
        {
            // xUnit never calls DisposeAsync when InitializeAsync throws, so a half-created database would leak. Remove it (best effort), then let the ORIGINAL exception through:
            // a failed initialisation must still fail its test, and nothing in the cleanup may replace or hide the cause.
            await CleanUpFailedInitializationAsync();
            throw;
        }
    }

    /// <summary>
    /// Phase 1a (TESTSPEED-P1A): releases the pooled connections that belong to THIS fixture's database, so the drop in <see cref="DisposeAsync"/> does not wait for them
    /// (measured: about 3 s with the pool still holding connections to the database, about 25 ms without).
    ///
    /// The pool is identified through the connection EF itself builds for this database (<c>CreateContext()</c> then <c>Database.GetDbConnection()</c>), which shares its pool with every
    /// context and every <c>WebApplicationFactory</c> host created from <see cref="ConnectionString"/>. A connection built directly from the same string does NOT release that pool
    /// (measured), and <c>SqlConnection.ClearAllPools()</c> would close other fixtures' pooled connections, so neither is used. Scoping is proven by TestDatabaseTeardownTests.
    /// </summary>
    internal void ReleasePooledConnections()
    {
        using var context = CreateContext();
        SqlConnection.ClearPool((SqlConnection)context.Database.GetDbConnection());
    }

    public async Task DisposeAsync()
    {
        try
        {
            ReleasePooledConnections();
        }
        catch (Exception ex)
        {
            // The optimisation failing must never fail a test or hide a leak: say so, then fall through to the unchanged drop below.
            _log.WriteLine($"[TestDatabaseFixture] releasing pooled connections failed ({ex.GetType().Name}); the drop may be slower. Database {_databaseName}.");
        }

        await DropDatabaseAsync(onlyIfExists: false);
    }

    public AlveraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AlveraDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AlveraDbContext(options);
    }

    /// <summary>TESTSPEED-P2: true only for SQL Server error 1205 (this session was chosen as a deadlock victim; the whole batch was rolled back and may be run again).</summary>
    internal static bool IsDeadlock(Exception exception) => exception is SqlException { Number: DeadlockErrorNumber };

    /// <summary>
    /// TESTSPEED-P2: true for the <see cref="InvalidOperationException"/> SqlClient throws when a connection cannot be obtained from a pool ("Timeout expired. The timeout period elapsed prior to
    /// obtaining a connection from the pool ..."). Investigated cause: SqlClient 7.0.0's pool-pruning timer shuts down an empty pool while a caller is acquiring a connection from it, and the
    /// caller gets this same message at once ("Pool is shutting down; abandoning wait"). LIMIT: the message is shared with genuine pool exhaustion, so this classifier can also match that; a
    /// bounded retry of a real exhaustion fails again and the first exception is then rethrown, so nothing is hidden.
    /// </summary>
    internal static bool IsPoolAcquisitionTimeout(Exception exception) =>
        exception is InvalidOperationException && string.Join(' ', exception.Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).StartsWith(PoolAcquisitionTimeoutPrefix, StringComparison.Ordinal);

    /// <summary>What the drop retries: the deadlock of the Gate 2 root-cause evidence and the pool-acquisition timeout (the drop opens a connection to master first, which can hit the same pool race).</summary>
    internal static bool IsTransientDropFailure(Exception exception) => IsDeadlock(exception) || IsPoolAcquisitionTimeout(exception);

    /// <summary>
    /// The drop (<c>ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ...</c>), retried as a WHOLE (opening the master connection included) on the failures of
    /// <see cref="IsTransientDropFailure"/>. The batch itself is unchanged.
    /// </summary>
    private async Task DropDatabaseAsync(bool onlyIfExists)
    {
        var statement = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
        if (onlyIfExists) statement = $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN {statement} END";

        await RunWithTransientRetryAsync(async () =>
        {
            if (_beforeMasterOpenForTest is not null) await _beforeMasterOpenForTest();

            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }, IsTransientDropFailure, "drop", _databaseName, MaxDropAttempts, DropRetryDelays, _delay, _log, beforeRetry: null);
    }

    /// <summary>
    /// The bounded retry loop: at most <paramref name="maxAttempts"/> attempts in total, waiting <paramref name="delays"/> between them (a zero wait is not slept), <paramref name="beforeRetry"/> (if any)
    /// run after each failed attempt and before the next one, every retry logged (operation, database name, attempt numbers and a safe failure classification only; never a connection string).
    /// When the last attempt also fails with a retryable error, the FIRST (original) exception is rethrown unchanged (same object), so the failure looks exactly as it did before the retry
    /// existed. A failure <paramref name="isRetryable"/> does not accept propagates at once.
    /// </summary>
    internal static async Task RunWithTransientRetryAsync(Func<Task> operation, Func<Exception, bool> isRetryable, string operationName, string database, int maxAttempts, TimeSpan[] delays,
        Func<TimeSpan, Task> delay, TextWriter log, Func<Task>? beforeRetry)
    {
        ExceptionDispatchInfo? original = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (isRetryable(ex))
            {
                original ??= ExceptionDispatchInfo.Capture(ex);
                var code = ex is SqlException sql ? $"SQL {sql.Number}" : IsPoolAcquisitionTimeout(ex) ? "pool acquisition timeout" : ex.GetType().Name;
                if (attempt >= maxAttempts)
                {
                    log.WriteLine($"[TestDatabaseFixture] {operationName} of {database} hit {code} (attempt {attempt} of {maxAttempts}); giving up after {maxAttempts} attempts.");
                    original.Throw();
                }

                var wait = delays[attempt - 1];
                log.WriteLine($"[TestDatabaseFixture] {operationName} of {database} hit {code} (attempt {attempt} of {maxAttempts}); retrying in {wait.TotalMilliseconds:0} ms.");
                if (beforeRetry is not null) await beforeRetry();
                if (wait > TimeSpan.Zero) await delay(wait);
            }
        }
    }

    /// <summary>Never throws: a cleanup failure is logged and must not replace the initialisation failure that is being propagated.</summary>
    private async Task CleanUpFailedInitializationAsync()
    {
        try
        {
            ReleasePooledConnections();
        }
        catch (Exception ex)
        {
            _log.WriteLine($"[TestDatabaseFixture] releasing pooled connections after a failed initialisation failed ({ex.GetType().Name}). Database {_databaseName}.");
        }

        try
        {
            if (_cleanupDropForTest is not null) await _cleanupDropForTest(_databaseName);
            else await DropDatabaseAsync(onlyIfExists: true);
            _log.WriteLine($"[TestDatabaseFixture] initialisation of {_databaseName} failed; any partially created database was removed.");
        }
        catch (Exception ex)
        {
            _log.WriteLine($"[TestDatabaseFixture] cleanup after a failed initialisation failed ({ex.GetType().Name}); database {_databaseName} may remain.");
        }
    }
}
