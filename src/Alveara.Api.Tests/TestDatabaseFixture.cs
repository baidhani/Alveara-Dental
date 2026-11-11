using System.Runtime.ExceptionServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// Creates a uniquely-named real SQL Server database per test class (its files inside Test-Data\Database of the one data root, see <see cref="AlveraTestEnvironment"/>), applies the actual
/// EF Core migrations against it, and drops it afterward. This exercises the real migration
/// pipeline and real SQL Server transaction/constraint behavior — not an in-memory provider,
/// which does not enforce unique indexes, foreign keys, or real transaction semantics the same
/// way and would not actually prove ALV-N002's migration/transaction acceptance items.
/// </summary>
public partial class TestDatabaseFixture : IAsyncLifetime
{
    private static string MasterConnectionString => AlveraTestEnvironment.MasterConnectionString;

    /// <summary>TESTSPEED-P2: at most this many attempts in total (the first try plus three retries) for the drop.</summary>
    internal const int MaxDropAttempts = 4;

    /// <summary>TESTSPEED-P2: at most this many attempts in total (the first try plus two retries) for the creation and migration of the database.</summary>
    internal const int MaxInitializationAttempts = 3;

    /// <summary>Waits before drop retry 1, 2 and 3.</summary>
    internal static readonly TimeSpan[] DropRetryDelays = [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(750)];

    /// <summary>Waits before initialisation retry 1 and 2: none, because the cleanup that runs first takes longer than the race it recovers from.</summary>
    internal static readonly TimeSpan[] InitializationRetryDelays = [TimeSpan.Zero, TimeSpan.Zero];

    private readonly string _databaseName = $"AlveraTest_{Guid.NewGuid():N}";

    private static int _realMigrationFixtures;
    /// <summary>ALV-N016: how many fixtures of this run used the real create-and-migrate path (the named inventory, and fallbacks), for the run's telemetry summary.</summary>
    internal static int RealMigrationFixtureCount => Volatile.Read(ref _realMigrationFixtures);

    // ALV-N016: an ordinary fixture restores a clone of the run's template; a fixture of the real-migration inventory (and every fixture built through the internal hook constructor) keeps the real path.
    private readonly TestDatabaseTemplate? _template;
    private readonly Func<Task>? _afterRestoreForTest;

    // Test-only hooks. They are set only through the internal constructor below (xUnit constructs fixtures through the public parameterless one), so they cannot change production behaviour.
    private readonly Func<Task>? _beforeCreateForTest;
    private readonly Func<Task>? _afterMigrateForTest;
    private readonly Func<string, Task>? _cleanupDropForTest;
    private readonly Func<Task>? _beforeMasterOpenForTest;
    private readonly Func<int, string?>? _dropStatementForTest;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly bool _warmUp = true;                                                         // ALV-N019 R04: test-only switch (the control arm of the forced-pruning proof turns it off)
    private readonly TextWriter _log;
    private int _holderChecks;                                                                    // how many failed-drop holder checks this fixture has logged (one per failed attempt)

    public string ConnectionString { get; private set; } = string.Empty;

    public TestDatabaseFixture() : this(useTemplate: true) { }

    /// <summary>The constructor of <see cref="RealMigrationTestDatabaseFixture"/> passes false: the fixture creates and migrates its own database exactly as before the template existed.</summary>
    protected TestDatabaseFixture(bool useTemplate)
    {
        _delay = Task.Delay;
        _log = Console.Error;
        if (useTemplate && TestDatabaseTemplate.Enabled) _template = TestDatabaseTemplate.Default;
    }

    /// <summary>
    /// Test-only: lets the remediation tests force a failure before the database is created or after it is migrated (both hooks run on EVERY attempt), fail the opening of the master connection
    /// that every drop starts with, replace the cleanup drop, record the waits instead of sleeping, and capture the log.
    /// </summary>
    internal TestDatabaseFixture(Func<Task>? beforeCreateForTest = null, Func<Task>? afterMigrateForTest = null, Func<string, Task>? cleanupDropForTest = null, TextWriter? logForTest = null,
        Func<Task>? beforeMasterOpenForTest = null, Func<TimeSpan, Task>? delayForTest = null, TestDatabaseTemplate? templateForTest = null, Func<Task>? afterRestoreForTest = null,
        Func<int, string?>? dropStatementForTest = null, bool warmUp = true)
    {
        _warmUp = warmUp;
        _dropStatementForTest = dropStatementForTest;
        _beforeCreateForTest = beforeCreateForTest;
        _afterMigrateForTest = afterMigrateForTest;
        _cleanupDropForTest = cleanupDropForTest;
        _beforeMasterOpenForTest = beforeMasterOpenForTest;
        _delay = delayForTest ?? Task.Delay;
        _log = logForTest ?? Console.Error;
        _template = templateForTest;                                                                  // only a test that supplies a template uses one through this constructor
        _afterRestoreForTest = afterRestoreForTest;
    }

    public async Task InitializeAsync()
    {
        ConnectionString = AlveraTestEnvironment.ConnectionStringFor(_databaseName);

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

                // ALV-N016: an ordinary fixture restores a clone of the run's template. A restore failure removes the partial clone, is logged and counted, and this fixture falls back to the real path below;
                // a template that cannot be built is NOT a fallback: it fails loudly (TemplateBuildException), as does a transient failure the retry loop owns.
                if (_template is not null && await TryRestoreCloneAsync()) return;
                Interlocked.Increment(ref _realMigrationFixtures);

                // The database is created here, with its files inside Test-Data\Database, instead of by MigrateAsync (which would leave the files to the server's default folders).
                // MigrateAsync then finds it and only applies the migrations. Repeating this on a retry is safe: it creates nothing when the database already exists.
                await using (var master = new SqlConnection(MasterConnectionString))
                {
                    await master.OpenAsync();
                    await using var create = master.CreateCommand();
                    create.CommandText = AlveraTestEnvironment.CreateDatabaseIfMissingSql(_databaseName);
                    await create.ExecuteNonQueryAsync();
                }

                await using var db = new AlveraDbContext(options);
                await db.Database.MigrateAsync();

                if (_afterMigrateForTest is not null) await _afterMigrateForTest();
            }, IsPoolAcquisitionTimeout, "initialisation", _databaseName, MaxInitializationAttempts, InitializationRetryDelays, _delay, _log, CleanUpFailedInitializationAsync);

            // ALV-N019 R04: warm the pool of the clone's own connection string BEFORE the fixture is released. SqlClient's pruning timer removes only EMPTY pools, so one idle pooled connection keeps the
            // pool alive for the first pooled open the owner's setup makes next (the open that failed in the two R03 Stage 2 runs). A pruning race at this open is retried by the same narrow classifier
            // (pool-acquisition timeout only, bounded; the first exception is rethrown unchanged); nothing else is retried here.
            if (_warmUp) await RunWithTransientRetryAsync(WarmPoolAsync, IsPoolAcquisitionTimeout, "pool warm-up", _databaseName, MaxInitializationAttempts, InitializationRetryDelays, _delay, _log, beforeRetry: null);
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
    /// Restores this fixture's clone from the template. Returns true when the clone exists; false after a fallback (the partial clone is removed, the event is logged and counted). Raises a
    /// <see cref="TemplateBuildException"/> unchanged and lets a transient failure (the retry loop's) through.
    /// </summary>
    private async Task<bool> TryRestoreCloneAsync()
    {
        try
        {
            await _template!.RestoreCloneAsync(_databaseName);
            if (_afterRestoreForTest is not null) await _afterRestoreForTest();
            return true;
        }
        catch (TemplateBuildException) { throw; }
        catch (Exception ex) when (IsPoolAcquisitionTimeout(ex)) { throw; }
        catch (Exception ex)
        {
            var reason = ex is SqlException sql ? $"SQL {sql.Number}" : ex.GetType().Name;           // a class and a number only: never a message, a path or a connection string
            try { ReleasePooledConnections(); } catch (Exception releaseFailure) { _log.WriteLine($"[TestDatabaseFixture] releasing pooled connections after a failed restore failed ({releaseFailure.GetType().Name}). Database {_databaseName}."); }
            try { await DropDatabaseAsync(onlyIfExists: true); }
            catch (Exception dropFailure) { _log.WriteLine($"[TestDatabaseFixture] removing the partial clone {_databaseName} failed ({dropFailure.GetType().Name}); it may remain."); }
            _template!.RecordFallback(_databaseName, reason);
            return false;
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

    /// <summary>
    /// The drop (<c>ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ...</c>), retried as a WHOLE (opening the master connection included) on the failures of
    /// <see cref="IsTransientDropFailure"/>. The batch itself is unchanged.
    /// </summary>
    private async Task DropDatabaseAsync(bool onlyIfExists)
    {
        var statement = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
        if (onlyIfExists) statement = $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN {statement} END";

        var attempt = 0;
        await RunWithTransientRetryAsync(async () =>
        {
            attempt++;
            if (_beforeMasterOpenForTest is not null) await _beforeMasterOpenForTest();

            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = _dropStatementForTest?.Invoke(attempt) ?? statement;             // a test can make one attempt use a different statement (to meet a real error 3702); production never does
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (ex.Number == DatabaseInUseErrorNumber)
            {
                await LogDatabaseHoldersAsync();                                                   // who holds it, on every such failure including the last; then the failure goes on to the retry loop
                throw;
            }
        }, IsTransientDropFailure, "drop", _databaseName, MaxDropAttempts, DropRetryDelays, _delay, _log, beforeRetry: null);
    }

    /// <summary>
    /// ALV-017-C01 R02, corrected by ALV-018-C01 R02: after a drop failed because the database is in use, writes ONE marker line for that failed attempt ("holder check N of M: K session(s)", or the class of the
    /// failure if the holders could not be read) followed by one line for each session that holds it: session id, program name, host name, host process id and status. The marker is the per-failure record: a
    /// failed attempt can see any number of holder sessions (SQL Server may report several for one drop), so the number of session lines says nothing about the number of attempts. Nothing else is
    /// read or written (no connection string, no statement text, no request or patient data), the values are stripped of control characters and cut to 128 characters, and the method never throws: a failing
    /// diagnostic is itself logged by class and must not replace the drop failure it is explaining.
    /// </summary>
    private async Task LogDatabaseHoldersAsync()
    {
        var check = Interlocked.Increment(ref _holderChecks);
        var prefix = $"[TestDatabaseFixture] drop of {_databaseName} (SQL {DatabaseInUseErrorNumber})";
        var sessions = new List<string>();
        string? failure = null;
        try
        {
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = "SELECT session_id, program_name, host_name, host_process_id, status FROM sys.dm_exec_sessions WHERE database_id = DB_ID(@name) AND session_id <> @@SPID ORDER BY session_id";
            command.Parameters.AddWithValue("@name", _databaseName);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sessions.Add($"{prefix}: session {reader.GetInt16(0)} holds it; program '{Safe(reader, 1)}', host '{Safe(reader, 2)}', host process {(reader.IsDBNull(3) ? 0 : reader.GetInt32(3))}, status {Safe(reader, 4)}.");
            }
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name;
        }

        _log.WriteLine(failure is null
            ? $"{prefix}: holder check {check} of {MaxDropAttempts}: {sessions.Count} session(s)."
            : $"{prefix}: holder check {check} of {MaxDropAttempts}: reading the holders failed ({failure}).");
        foreach (var line in sessions) _log.WriteLine(line);
        if (failure is null && sessions.Count == 0) _log.WriteLine($"{prefix}: no session holds it any more (the holder had gone again).");

        static string Safe(SqlDataReader reader, int column)
        {
            var text = reader.IsDBNull(column) ? "" : reader.GetString(column);
            var clean = new string(text.Where(c => !char.IsControl(c) && c != '\'').ToArray());
            return clean.Length > 128 ? clean[..128] : clean;
        }
    }

    /// <summary>Opens and closes one pooled connection through the pool EF uses for this fixture's database, leaving one idle connection in that pool.</summary>
    private async Task WarmPoolAsync()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.CloseConnectionAsync();
    }

    /// <summary>
    /// ALV-N019 R04: the initialisation an OWNER of a fixture (a test class, <see cref="SafetyTestBase"/>) uses when it has setup of its own after the fixture is ready. xUnit never calls DisposeAsync when
    /// InitializeAsync throws, so a failure of that setup would leak this fixture's database and its two files. Here the owner's setup runs after the fixture initialisation; on ANY exception the fixture
    /// removes ONLY its own clone (best effort) and the ORIGINAL exception is rethrown unchanged (same object). A cleanup failure is logged on its own line (class only) and never replaces the original.
    /// </summary>
    public async Task InitializeOwnerAsync(Func<Task> setupAfterFixture)
    {
        await InitializeAsync();
        try
        {
            await setupAfterFixture();
        }
        catch
        {
            await CleanUpFailedOwnerSetupAsync();
            throw;
        }
    }

    /// <summary>Never throws: removes this fixture's own clone after its owner's setup failed; a failure of the cleanup itself is logged distinctly (class only) and must not hide the setup failure.</summary>
    private async Task CleanUpFailedOwnerSetupAsync()
    {
        try { ReleasePooledConnections(); }
        catch (Exception ex) { _log.WriteLine($"[TestDatabaseFixture] owner-failure cleanup of {_databaseName}: releasing pooled connections failed ({ex.GetType().Name})."); }

        try
        {
            if (_cleanupDropForTest is not null) await _cleanupDropForTest(_databaseName);
            else await DropDatabaseAsync(onlyIfExists: true);
            _log.WriteLine($"[TestDatabaseFixture] owner setup of {_databaseName} failed; its database and files were removed.");
        }
        catch (Exception ex)
        {
            _log.WriteLine($"[TestDatabaseFixture] owner-failure cleanup of {_databaseName} FAILED ({ex.GetType().Name}); the database and its files may remain.");
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
