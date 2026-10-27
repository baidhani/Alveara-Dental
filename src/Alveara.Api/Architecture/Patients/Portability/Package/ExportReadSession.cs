using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: how the database is read for one export, held on ONE connection from the start of the read to the end. For one patient it only keeps the connection open. For all patients it also: takes a database lock so
/// two all-patients exports never run together (409 <c>export_in_progress</c>, the second is refused, not queued); starts a snapshot transaction when the database allows snapshot isolation, so every table and every
/// readable record is read at one moment (the manifest says <c>Snapshot</c>), and otherwise reads table by table (<c>TableByTable</c>); and saves the ids of the patients in scope (everyone not merged away) in a
/// temporary table, so every table and every page uses the SAME patients even if one is registered while the export runs. The count is checked against the patient limit before anything else is read (413
/// <c>too_many_patients</c>). Disposing it ends the transaction, drops the temporary table and releases the lock, each on its own so one failure does not stop the rest (each failure is logged with its error class); the
/// connection's own reset also drops the table and the lock if even that fails. The ids are never written anywhere but that table.
/// </summary>
public sealed class ExportReadSession : IAsyncDisposable
{
    private const string ScopeTable = "#ExportScope";
    private const string LockName = "alveara-bulk-patient-export";

    private readonly AlveraDbContext _db;
    private readonly DataExportOptions _options;
    private readonly ILogger _log;
    private IDbContextTransaction? _transaction;
    private bool _connectionOpened, _lockHeld, _scopeTableCreated;

    private ExportReadSession(AlveraDbContext db, DataExportOptions options, ILogger log)
    {
        (_db, _options, _log) = (db, options, log);
        Connection = db.Database.GetDbConnection();
    }

    public DbConnection Connection { get; }

    /// <summary>The snapshot transaction the reads belong to, or null when they are not in one (commands must be given it).</summary>
    public DbTransaction? Transaction => _transaction?.GetDbTransaction();

    /// <summary>A SQL fragment selecting the ids of the patients in scope.</summary>
    public string PatientIdsSql { get; private set; } = "";

    public int PatientCount { get; private set; }

    /// <summary>A <see cref="ReadMode"/>: whether the reads share one snapshot.</summary>
    public string ReadConsistency { get; private set; } = ReadMode.TableByTable;

    public static async Task<ExportReadSession> OpenAsync(AlveraDbContext db, PackageScope scope, DataExportOptions options, ILogger log, CancellationToken ct)
    {
        var session = new ExportReadSession(db, options, log);
        try
        {
            await session.StartAsync(scope, ct);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private async Task StartAsync(PackageScope scope, CancellationToken ct)
    {
        await _db.Database.OpenConnectionAsync(ct);
        _connectionOpened = true;
        if (scope.Kind != DataExportScope.AllPatients)
        {
            (PatientIdsSql, PatientCount) = (scope.PatientIdsSql, scope.PatientCount);
            return;
        }

        var lockResult = await ScalarAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'{LockName}', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 0; SELECT @r;", ct);
        if (lockResult < 0) throw new PatientException("export_in_progress", "Another all-patients export is running. Try again when it has finished.", 409);
        _lockHeld = true;

        if (await ScalarAsync("SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()", ct) == 1)
        {
            _transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Snapshot, ct);                   // the moment is fixed by the first read below
            ReadConsistency = ReadMode.Snapshot;
        }

        await ExecuteAsync($"CREATE TABLE [{ScopeTable}] ([Id] uniqueidentifier NOT NULL PRIMARY KEY); INSERT INTO [{ScopeTable}] ([Id]) SELECT [Id] FROM [Patients] WHERE [MergedAtUtc] IS NULL;", ct);
        _scopeTableCreated = true;
        PatientCount = await ScalarAsync($"SELECT COUNT(*) FROM [{ScopeTable}]", ct);
        if (PatientCount > _options.MaxPatients)
            throw new PatientException("too_many_patients", $"There are {PatientCount} patients, over the limit of {_options.MaxPatients} for one export. Nothing was exported.", 413);
        PatientIdsSql = $"SELECT [Id] FROM [{ScopeTable}]";
    }

    private async Task<int> ScalarAsync(string sql, CancellationToken ct)
    {
        await using var command = NewCommand(sql);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var command = NewCommand(sql);
        await command.ExecuteNonQueryAsync(ct);
    }

    private DbCommand NewCommand(string sql)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = _options.ReadTimeoutSeconds;
        command.Transaction = Transaction;
        return command;
    }

    public async ValueTask DisposeAsync()
    {
        var transaction = _transaction;
        _transaction = null;                                                                                     // the commands below must not be given a finished transaction
        await Quietly("transaction", async () => { if (transaction is not null) await transaction.DisposeAsync(); });
        if (_scopeTableCreated) await Quietly("scope_table", () => ExecuteAsync($"IF OBJECT_ID(N'tempdb..{ScopeTable}') IS NOT NULL DROP TABLE [{ScopeTable}];", CancellationToken.None));
        if (_lockHeld) await Quietly("lock", () => ExecuteAsync($"EXEC sp_releaseapplock @Resource = N'{LockName}', @LockOwner = N'Session';", CancellationToken.None));
        if (_connectionOpened) await Quietly("connection", () => _db.Database.CloseConnectionAsync());
    }

    private async Task Quietly(string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _log.LogError("{Event} step={Step} error_class={ErrorClass}", "export_read_session_cleanup_failed", what, ex.GetType().Name);
        }
    }
}
