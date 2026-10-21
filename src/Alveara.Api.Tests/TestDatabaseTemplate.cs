using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>Raised when the template database cannot be built or verified. It is never turned into a quiet fallback: a run whose template is wrong or missing fails loudly.</summary>
internal sealed class TemplateBuildException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A built template: the backup file, the logical names of its data and log files (discovered from the backup itself) and the migrations it was built from.</summary>
internal sealed record TemplateInfo(string BackupPath, string DataLogicalName, string LogLogicalName, IReadOnlyList<string> Migrations);

/// <summary>
/// ALV-N016. Applying every EF migration to a new database costs about 2.5 s and a full backend run creates about 3,200 databases, so the run builds ONE template with the real migrations, backs it up
/// and drops it, and each ordinary fixture restores a unique clone of that backup (about 0.15 s) instead of migrating its own database. Test infrastructure only; no production behaviour changes.
///
/// What it guarantees:
/// <list type="bullet">
/// <item>The template is built once per process by the real migration path, then checked: its applied migrations equal the code's (in order) and READ_COMMITTED_SNAPSHOT is on. A build or check failure is
/// raised as <see cref="TemplateBuildException"/> for every fixture, never converted into a fallback.</item>
/// <item>The logical file names used by <c>WITH MOVE</c> are read from the backup (<c>RESTORE FILELISTONLY</c>), not assumed.</item>
/// <item>If the code's migration list no longer equals the template's, the template is rebuilt (counted); a template that still does not match is a build failure.</item>
/// <item>Telemetry is one structured JSON line per event (to the log writer and, when <c>ALVERA_TEST_TEMPLATE_TELEMETRY</c> names a file, appended to it); it never carries a connection string or row data.</item>
/// <item>The backup is removed when the template is disposed (the run's default template is disposed when the process exits). Leftovers of an earlier abnormal run are REPORTED, never deleted.</item>
/// </list>
/// </summary>
internal sealed class TestDatabaseTemplate : IDisposable
{
    public const string TelemetryFileVariable = "ALVERA_TEST_TEMPLATE_TELEMETRY";
    /// <summary>Set to "off" to run every fixture on the real create-and-migrate path (a measurement switch for before/after timings; the default is on).</summary>
    public const string ModeVariable = "ALVERA_TEST_TEMPLATE";
    internal static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The template of this test run, built on first use and disposed (its backup deleted) when the process exits.</summary>
    public static TestDatabaseTemplate Default { get; }

    static TestDatabaseTemplate()
    {
        Default = new TestDatabaseTemplate();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Default.Dispose();
    }

    public static bool Enabled => !string.Equals(Environment.GetEnvironmentVariable(ModeVariable), "off", StringComparison.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TextWriter _log;
    private readonly Func<IReadOnlyList<string>>? _expectedMigrations;
    private readonly Func<Task>? _beforeBuild;
    private readonly object _writeLock = new();
    private TemplateInfo? _info;
    private Exception? _buildFailure;
    private bool _leftoversReported;
    private bool _disposed;
    private int _builds, _rebuilds, _clones, _fallbacks;
    private long _buildMs, _restoreMs;

    public string BackupFolder { get; }

    internal TestDatabaseTemplate(string? backupFolder = null, TextWriter? log = null, Func<IReadOnlyList<string>>? expectedMigrations = null, Func<Task>? beforeBuild = null)
    {
        BackupFolder = backupFolder ?? AlveraTestEnvironment.TempFolder;
        _log = log ?? Console.Error;
        _expectedMigrations = expectedMigrations;
        _beforeBuild = beforeBuild;
    }

    public int Builds => Volatile.Read(ref _builds);
    public int Rebuilds => Volatile.Read(ref _rebuilds);
    public int Clones => Volatile.Read(ref _clones);
    public int Fallbacks => Volatile.Read(ref _fallbacks);

    private IReadOnlyList<string> Expected()
    {
        if (_expectedMigrations is not null) return _expectedMigrations();
        using var db = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(AlveraTestEnvironment.ConnectionStringFor("master")).Options);
        return db.Database.GetMigrations().ToList();
    }

    /// <summary>Builds the template on first use (or rebuilds it when the code's migrations changed); every later call returns the same backup. A failure is cached and raised for every caller.</summary>
    public async Task<TemplateInfo> EnsureBuiltAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var expected = Expected();
            if (_info is not null && _info.Migrations.SequenceEqual(expected)) return _info;
            if (_buildFailure is not null) throw new TemplateBuildException("The template database could not be built earlier in this run; every fixture fails the same way.", _buildFailure);

            if (_info is not null)
            {
                Event("template_migration_mismatch", "warn", new { template_migrations = _info.Migrations.Count, code_migrations = expected.Count });
                TryDeleteBackup(_info.BackupPath);
                _info = null;
                Interlocked.Increment(ref _rebuilds);
            }

            if (!_leftoversReported) { _leftoversReported = true; ReportLeftovers(); }
            try
            {
                _info = await BuildAsync(expected);
                return _info;
            }
            catch (Exception ex)
            {
                _buildFailure = ex;
                Event("template_build_failed", "error", new { error_class = ex.GetType().Name }, outcome: "failure");
                throw ex is TemplateBuildException ? ex : new TemplateBuildException("The template database could not be built: " + ex.GetType().Name, ex);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<TemplateInfo> BuildAsync(IReadOnlyList<string> expected)
    {
        var sw = Stopwatch.StartNew();
        if (_beforeBuild is not null) await _beforeBuild();

        var name = $"AlveraTemplate_{Guid.NewGuid():N}";
        var backup = Path.Combine(BackupFolder, name + ".bak");
        var created = false;
        try
        {
            await ExecuteMasterAsync(AlveraTestEnvironment.CreateDatabaseIfMissingSql(name));
            created = true;

            var options = new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(AlveraTestEnvironment.ConnectionStringFor(name)).Options;
            var migrateMs = sw.ElapsedMilliseconds;
            await using (var db = new AlveraDbContext(options))
            {
                await db.Database.MigrateAsync();                                                                       // the real migration path, once
                var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
                if (!applied.SequenceEqual(expected))
                    throw new TemplateBuildException($"migration_mismatch: the template applied {applied.Count} migrations and the code expects {expected.Count}.");
                SqlConnection.ClearPool((SqlConnection)db.Database.GetDbConnection());
            }
            migrateMs = sw.ElapsedMilliseconds - migrateMs;

            if (await ScalarAsync("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()", name) != 1)
                throw new TemplateBuildException("snapshot_setting: READ_COMMITTED_SNAPSHOT is not on in the template.");

            Directory.CreateDirectory(BackupFolder);
            await ExecuteMasterAsync($"BACKUP DATABASE [{name}] TO DISK = N'{Q(backup)}' WITH INIT, FORMAT");
            var (data, log) = await FileListAsync(backup);
            await ExecuteMasterAsync($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
            created = false;

            Interlocked.Increment(ref _builds);
            _buildMs += sw.ElapsedMilliseconds;
            Event("template_built", "info", new { migrations = expected.Count, migrate_ms = migrateMs, total_ms = sw.ElapsedMilliseconds, backup_bytes = new FileInfo(backup).Length });
            return new TemplateInfo(backup, data, log, expected.ToList());
        }
        catch
        {
            if (created) { try { await ExecuteMasterAsync($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END"); } catch (Exception ex) { Event("template_cleanup_failed", "warn", new { error_class = ex.GetType().Name }); } }
            TryDeleteBackup(backup);
            throw;
        }
    }

    /// <summary>Restores a unique clone: <c>RESTORE DATABASE ... WITH MOVE</c> to the clone's own files under Test-Data\Database, using the logical names read from the backup. Throws on any failure; the caller cleans up and falls back.</summary>
    public async Task RestoreCloneAsync(string database)
    {
        var info = await EnsureBuiltAsync();
        var (data, log) = AlveraTestEnvironment.FilesOf(database);
        var sw = Stopwatch.StartNew();
        await ExecuteMasterAsync($"RESTORE DATABASE [{database}] FROM DISK = N'{Q(info.BackupPath)}' WITH MOVE N'{Q(info.DataLogicalName)}' TO N'{Q(data)}', MOVE N'{Q(info.LogLogicalName)}' TO N'{Q(log)}'", RestoreTimeout);
        Interlocked.Add(ref _restoreMs, sw.ElapsedMilliseconds);
        Interlocked.Increment(ref _clones);
    }

    /// <summary>One observable fallback: the clone could not be restored, the partial clone was removed and the real create-and-migrate path is used for this fixture. Any fallback fails the final qualification.</summary>
    public void RecordFallback(string database, string reason)
    {
        Interlocked.Increment(ref _fallbacks);
        Event("template_restore_fallback", "warn", new { database, reason }, outcome: "partial");
    }

    /// <summary>Lists, and reports as telemetry, backups and databases of earlier abnormal runs (named AlveraTemplate_*) that are not this template's. Nothing is ever deleted.</summary>
    public IReadOnlyList<string> ReportLeftovers()
    {
        var found = new List<string>();
        try
        {
            var own = _info?.BackupPath;
            if (Directory.Exists(BackupFolder))
                foreach (var file in Directory.GetFiles(BackupFolder, "AlveraTemplate_*.bak").Where(f => !string.Equals(f, own, StringComparison.OrdinalIgnoreCase)))
                    found.Add($"backup {Path.GetFileName(file)} ({(DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalHours:0.0} h old)");
            using var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT name FROM sys.databases WHERE name LIKE N'AlveraTemplate[_]%'";
            using var r = cmd.ExecuteReader();
            while (r.Read()) found.Add($"database {r.GetString(0)}");
        }
        catch (Exception ex) { Event("template_leftover_scan_failed", "warn", new { error_class = ex.GetType().Name }); }
        if (found.Count > 0) Event("template_leftover_reported", "warn", new { count = found.Count, leftovers = found });
        return found;
    }

    public void Dispose()
    {
        lock (_writeLock) { if (_disposed) return; _disposed = true; }
        if (_info is not null) TryDeleteBackup(_info.BackupPath);
        Event("template_summary", "info", new { builds = Builds, rebuilds = Rebuilds, clones = Clones, fallbacks = Fallbacks, real_migration_fixtures = TestDatabaseFixture.RealMigrationFixtureCount, build_ms = _buildMs, restore_ms_total = Volatile.Read(ref _restoreMs) });
    }

    // ---------------------------------------------------------------- helpers

    private static string Q(string s) => s.Replace("'", "''");

    private void TryDeleteBackup(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Event("template_cleanup_failed", "warn", new { error_class = ex.GetType().Name }); }
    }

    private static async Task ExecuteMasterAsync(string sql, TimeSpan? timeout = null)
    {
        await using var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)(timeout ?? TimeSpan.FromSeconds(120)).TotalSeconds;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(string sql, string database)
    {
        await using var c = new SqlConnection(AlveraTestEnvironment.ConnectionStringFor(database));
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Explicit logical-file discovery: reads the file list of the backup itself and requires exactly one data file and one log file.</summary>
    private static async Task<(string Data, string Log)> FileListAsync(string backup)
    {
        await using var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"RESTORE FILELISTONLY FROM DISK = N'{Q(backup)}'";
        var data = new List<string>();
        var log = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            var nameOrdinal = r.GetOrdinal("LogicalName");
            var typeOrdinal = r.GetOrdinal("Type");
            while (await r.ReadAsync())
            {
                var type = r.GetString(typeOrdinal);
                if (type == "D") data.Add(r.GetString(nameOrdinal));
                else if (type == "L") log.Add(r.GetString(nameOrdinal));
            }
        }
        if (data.Count != 1 || log.Count != 1)
            throw new TemplateBuildException($"file_list: the template backup has {data.Count} data and {log.Count} log files; exactly one of each is expected.");
        return (data[0], log[0]);
    }

    private void Event(string name, string level, object context, string outcome = "success")
    {
        var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, level, service = "backend-tests", @event = name, outcome, context });
        lock (_writeLock)
        {
            _log.WriteLine("[TestDatabaseTemplate] " + line);
            if (Environment.GetEnvironmentVariable(TelemetryFileVariable) is { Length: > 0 } file)
            {
                try { File.AppendAllText(file, line + Environment.NewLine); }
                catch (IOException ex) { _log.WriteLine($"[TestDatabaseTemplate] writing telemetry to the file failed ({ex.GetType().Name}); the line above is the record."); }
            }
        }
    }
}
