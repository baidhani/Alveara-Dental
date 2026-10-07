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

    /// <summary>Waits before retry 1, 2 and 3.</summary>
    internal static readonly TimeSpan[] DropRetryDelays = [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)];

    private readonly string _databaseName = $"AlveraTest_{Guid.NewGuid():N}";

    // Test-only hooks. They are set only through the internal constructor below (xUnit constructs fixtures through the public parameterless one), so they cannot change production behaviour.
    private readonly Func<Task>? _beforeCreateForTest;
    private readonly Func<Task>? _afterMigrateForTest;
    private readonly Func<string, Task>? _cleanupDropForTest;
    private readonly TextWriter _log;

    public string ConnectionString { get; private set; } = string.Empty;

    public TestDatabaseFixture()
    {
        _log = Console.Error;
    }

    /// <summary>Test-only: lets TestDatabaseInitFailureCleanupTests force a failure before the database is created or after it is migrated, replace the cleanup drop, and capture the log.</summary>
    internal TestDatabaseFixture(Func<Task>? beforeCreateForTest = null, Func<Task>? afterMigrateForTest = null, Func<string, Task>? cleanupDropForTest = null, TextWriter? logForTest = null)
    {
        _beforeCreateForTest = beforeCreateForTest;
        _afterMigrateForTest = afterMigrateForTest;
        _cleanupDropForTest = cleanupDropForTest;
        _log = logForTest ?? Console.Error;
    }

    public async Task InitializeAsync()
    {
        ConnectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";

        try
        {
            if (_beforeCreateForTest is not null) await _beforeCreateForTest();

            var options = new DbContextOptionsBuilder<AlveraDbContext>()
                .UseSqlServer(ConnectionString)
                .Options;

            await using var db = new AlveraDbContext(options);
            await db.Database.MigrateAsync();

            if (_afterMigrateForTest is not null) await _afterMigrateForTest();
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
    /// TESTSPEED-P2: the drop (<c>ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ...</c>) can be chosen as the victim of a deadlock with an EF Core session of a
    /// still-running in-process host that is inside the same database (deadlock graphs in the Gate 2 root-cause evidence). Only that error is retried.
    /// </summary>
    private async Task DropDatabaseAsync(bool onlyIfExists)
    {
        var statement = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
        if (onlyIfExists) statement = $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN {statement} END";

        await RunWithDeadlockRetryAsync(async () =>
        {
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }, IsDeadlock, _databaseName, Task.Delay, _log);
    }

    /// <summary>
    /// The retry loop: at most <see cref="MaxDropAttempts"/> attempts in total, waiting <see cref="DropRetryDelays"/> between them, every retry logged (database name and error code only, never a
    /// connection string). When the last attempt also fails with a retryable error, the FIRST (original) exception is rethrown unchanged, so the failure looks exactly as it did before the retry
    /// existed. Anything the predicate does not accept propagates at once.
    /// </summary>
    internal static async Task RunWithDeadlockRetryAsync(Func<Task> operation, Func<Exception, bool> isRetryable, string database, Func<TimeSpan, Task> delay, TextWriter log)
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
                var code = ex is SqlException sql ? $"SQL {sql.Number}" : ex.GetType().Name;
                if (attempt >= MaxDropAttempts)
                {
                    log.WriteLine($"[TestDatabaseFixture] drop of {database} hit {code} (attempt {attempt} of {MaxDropAttempts}); giving up after {MaxDropAttempts} attempts.");
                    original.Throw();
                }

                var wait = DropRetryDelays[attempt - 1];
                log.WriteLine($"[TestDatabaseFixture] drop of {database} hit {code} (attempt {attempt} of {MaxDropAttempts}); retrying in {wait.TotalMilliseconds:0} ms.");
                await delay(wait);
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
