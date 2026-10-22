using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N016: the template database. One fresh template is built with the real EF migrations, backed up and dropped; every ordinary fixture restores a unique clone of it. These tests prove the clone carries the
/// migration state and settings of a real migration, that clones are isolated, that a missing or corrupt backup, a failed restore and a failed template build are handled as specified (fallback with telemetry,
/// partial-clone cleanup, a loud failure), that nothing is left behind by a successful run, and that leftovers of an earlier run are reported and never deleted. Each test builds its own template in its own
/// backup folder, so none of them touches the template of the run itself.
/// </summary>
public class TestDatabaseTemplateTests : IAsyncLifetime
{
    private readonly List<IDisposable> _templates = [];
    private readonly List<TestDatabaseFixture> _fixtures = [];
    private readonly List<string> _folders = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var f in _fixtures) await f.DisposeAsync();
        foreach (var t in _templates) t.Dispose();
        foreach (var folder in _folders) TestStorageCleanup.Delete(folder);
    }

    private (TestDatabaseTemplate Template, StringWriter Log) NewTemplate(Func<IReadOnlyList<string>>? expectedMigrations = null, Func<Task>? beforeBuild = null)
    {
        var folder = Path.Combine(AlveraTestEnvironment.TempFolder, $"template-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        var log = new StringWriter();
        var template = new TestDatabaseTemplate(folder, log, expectedMigrations, beforeBuild);
        _templates.Add(template);
        return (template, log);
    }

    private TestDatabaseFixture NewFixture(TestDatabaseTemplate template, StringWriter log, Func<Task>? afterRestore = null)
    {
        return new TestDatabaseFixture(templateForTest: template, logForTest: log, afterRestoreForTest: afterRestore);
    }

    /// <summary>A fixture that initialised is registered for disposal; one that failed to initialise was already cleaned up by its own initialisation and must not be disposed.</summary>
    private async Task<TestDatabaseFixture> StartAsync(TestDatabaseTemplate template, StringWriter log, Func<Task>? afterRestore = null)
    {
        var fixture = NewFixture(template, log, afterRestore);
        await fixture.InitializeAsync();
        _fixtures.Add(fixture);
        return fixture;
    }

    private static string NameOf(TestDatabaseFixture f) => new SqlConnectionStringBuilder(f.ConnectionString).InitialCatalog;

    private static async Task<T> ScalarAsync<T>(string sql, string? database = null)
    {
        await using var c = new SqlConnection(database is null ? AlveraTestEnvironment.MasterConnectionString : AlveraTestEnvironment.ConnectionStringFor(database));
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    private static Task<int> DatabasesLikeAsync(string prefix) => ScalarAsync<int>($"SELECT COUNT(*) FROM sys.databases WHERE name LIKE N'{prefix}%'");

    private static IReadOnlyList<string> CodeMigrations()
    {
        using var db = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(AlveraTestEnvironment.ConnectionStringFor("master")).Options);
        return db.Database.GetMigrations().ToList();
    }

    [Fact]
    public async Task A_clone_carries_the_migration_history_the_snapshot_setting_and_its_files_of_a_real_migration()
    {
        var (template, log) = NewTemplate();
        var fixture = await StartAsync(template, log);

        await using var db = fixture.CreateContext();
        Assert.Equal(CodeMigrations(), (await db.Database.GetAppliedMigrationsAsync()).ToList());                       // the same history, in order, as a database the real migrations built
        Assert.True(await ScalarAsync<int>("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()", NameOf(fixture)) == 1);
        var files = new List<string>();
        await using (var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(N'{NameOf(fixture)}')";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) files.Add(r.GetString(0));
        }
        var (data, logFile) = AlveraTestEnvironment.FilesOf(NameOf(fixture));
        Assert.Equal([data, logFile], files.OrderByDescending(f => f.EndsWith(".mdf")).ToList());                         // inside Test-Data\Database, named for the clone
        Assert.NotEqual(0, await ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables", NameOf(fixture)));
        Assert.Equal(1, template.Clones);
    }

    private const string SettingsSql = @"SELECT CONCAT(compatibility_level, '|', collation_name COLLATE DATABASE_DEFAULT, '|', recovery_model_desc COLLATE DATABASE_DEFAULT, '|', is_read_committed_snapshot_on, '|', snapshot_isolation_state, '|', is_auto_close_on, '|', is_auto_shrink_on,
        '|', page_verify_option_desc COLLATE DATABASE_DEFAULT, '|', is_ansi_nulls_on, '|', is_quoted_identifier_on, '|', is_arithabort_on, '|', is_auto_create_stats_on, '|', is_auto_update_stats_on, '|', containment_desc COLLATE DATABASE_DEFAULT, '|', is_trustworthy_on)
        FROM sys.databases WHERE name = DB_NAME()";

    /// <summary>Everything the migrations create, as text: columns, indexes, check constraints, triggers (their definitions), foreign keys, the migration history and the number of rows each table holds (seed data).</summary>
    private const string SchemaFingerprintSql = @"SELECT STRING_AGG(CAST(x AS nvarchar(max)), CHAR(10)) WITHIN GROUP (ORDER BY x) FROM (
        SELECT CONCAT('col:', t.name COLLATE DATABASE_DEFAULT, '.', c.name COLLATE DATABASE_DEFAULT, ':', ty.name COLLATE DATABASE_DEFAULT, ':', c.max_length, ':', c.precision, ':', c.scale, ':', c.is_nullable, ':', c.is_identity, ':', ISNULL(c.collation_name COLLATE DATABASE_DEFAULT, '')) AS x FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        UNION ALL SELECT CONCAT('idx:', t.name COLLATE DATABASE_DEFAULT, '.', i.name COLLATE DATABASE_DEFAULT, ':', i.type_desc COLLATE DATABASE_DEFAULT, ':', i.is_unique, ':', ISNULL(i.filter_definition COLLATE DATABASE_DEFAULT, '')) FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id WHERE i.name IS NOT NULL
        UNION ALL SELECT CONCAT('chk:', o.name COLLATE DATABASE_DEFAULT, ':', cc.definition COLLATE DATABASE_DEFAULT) FROM sys.check_constraints cc JOIN sys.objects o ON o.object_id = cc.object_id
        UNION ALL SELECT CONCAT('trg:', tr.name COLLATE DATABASE_DEFAULT, ':', m.definition COLLATE DATABASE_DEFAULT) FROM sys.triggers tr JOIN sys.sql_modules m ON m.object_id = tr.object_id
        UNION ALL SELECT CONCAT('fk:', fk.name COLLATE DATABASE_DEFAULT, ':', fk.delete_referential_action, ':', fk.update_referential_action) FROM sys.foreign_keys fk
        UNION ALL SELECT CONCAT('hist:', MigrationId COLLATE DATABASE_DEFAULT) FROM __EFMigrationsHistory
        UNION ALL SELECT CONCAT('rows:', t.name COLLATE DATABASE_DEFAULT, ':', SUM(ps.row_count)) FROM sys.tables t JOIN sys.dm_db_partition_stats ps ON ps.object_id = t.object_id AND ps.index_id IN (0, 1) GROUP BY t.name
    ) q";

    [Fact]
    public async Task A_clone_has_the_same_database_settings_schema_and_seed_data_as_a_database_the_real_migrations_built()
    {
        var (template, log) = NewTemplate();
        var clone = await StartAsync(template, log);
        var real = new RealMigrationTestDatabaseFixture();
        await real.InitializeAsync();
        try
        {
            Assert.Equal(await ScalarAsync<string>(SettingsSql, NameOf(real)), await ScalarAsync<string>(SettingsSql, NameOf(clone)));      // compatibility level, collation, recovery model, snapshot isolation, ...
            var fromReal = await ScalarAsync<string>(SchemaFingerprintSql, NameOf(real));
            var fromClone = await ScalarAsync<string>(SchemaFingerprintSql, NameOf(clone));
            Assert.True(fromReal.Length > 10_000, "the fingerprint should cover the whole schema");
            Assert.Equal(fromReal, fromClone);                                                                          // every table, column, index, check, trigger body, foreign key, history row and seeded row count
        }
        finally { await real.DisposeAsync(); }
    }

    [Fact]
    public async Task Clones_are_distinct_databases_and_data_written_to_one_never_appears_in_another()
    {
        var (template, log) = NewTemplate();
        var a = await StartAsync(template, log);
        var b = await StartAsync(template, log);
        Assert.NotEqual(NameOf(a), NameOf(b));

        var arranged = new SchedulingTestSupport(a);
        await arranged.ArrangeAsync();
        await arranged.PatientAsync();                                                                                   // a patient, written into clone A only

        await using var inA = a.CreateContext();
        await using var inB = b.CreateContext();
        Assert.Equal(1, await inA.Patients.CountAsync());
        Assert.Equal(0, await inB.Patients.CountAsync());
        Assert.Equal(2, template.Clones);
    }

    [Fact]
    public async Task The_template_is_built_once_by_the_real_migrations_and_dropped_at_once_and_every_clone_reuses_its_backup()
    {
        var (template, log) = NewTemplate();
        var before = await DatabasesLikeAsync("AlveraTemplate_");
        for (var i = 0; i < 3; i++) await StartAsync(template, log);

        Assert.Equal(1, template.Builds);
        Assert.Equal(3, template.Clones);
        Assert.Equal(0, template.Fallbacks);
        Assert.Equal(before, await DatabasesLikeAsync("AlveraTemplate_"));                                              // the template database itself is gone, only its backup remains
        var info = await template.EnsureBuiltAsync();
        Assert.True(File.Exists(info.BackupPath));
        Assert.StartsWith(template.BackupFolder, info.BackupPath);
        Assert.Equal(1, template.Builds);
    }

    [Fact]
    public async Task A_missing_backup_cleans_up_falls_back_to_create_and_migrate_and_is_observable()
    {
        var (template, log) = NewTemplate();
        var info = await template.EnsureBuiltAsync();
        File.Delete(info.BackupPath);
        var fixture = await StartAsync(template, log);                                                                   // it still works, through the real path

        await using var db = fixture.CreateContext();
        Assert.Equal(CodeMigrations(), (await db.Database.GetAppliedMigrationsAsync()).ToList());
        Assert.Equal(1, template.Fallbacks);
        Assert.Equal(0, template.Clones);
        var line = Assert.Single(log.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries), l => l.Contains("template_restore_fallback"));
        Assert.Contains(NameOf(fixture), line);
        Assert.Contains("\"reason\"", line);
        Assert.DoesNotContain("Server=", log.ToString());                                                                // telemetry never carries a connection string
    }

    [Fact]
    public async Task A_corrupt_backup_falls_back_and_the_partial_clone_is_removed()
    {
        var (template, log) = NewTemplate();
        var info = await template.EnsureBuiltAsync();
        await File.WriteAllBytesAsync(info.BackupPath, new byte[4096]);                                                  // not a backup set
        var fixture = await StartAsync(template, log);

        Assert.Equal(1, template.Fallbacks);
        Assert.Contains("template_restore_fallback", log.ToString());
        Assert.Equal(1, await DatabasesLikeAsync(NameOf(fixture)));                                                      // exactly the one the real path made
    }

    [Fact]
    public async Task An_injected_restore_failure_removes_the_partial_clone_and_falls_back()
    {
        var (template, log) = NewTemplate();
        var observed = new List<bool>();
        var partialCreated = DateTime.MinValue;
        TestDatabaseFixture? fixture = null;
        fixture = NewFixture(template, log, afterRestore: async () =>
        {
            observed.Add(await DatabasesLikeAsync(NameOf(fixture!)) == 1);                                               // the clone exists at this moment
            partialCreated = await ScalarAsync<DateTime>($"SELECT create_date FROM sys.databases WHERE name = N'{NameOf(fixture!)}'");
            throw new IOException("injected failure after the restore");
        });

        await fixture.InitializeAsync();
        _fixtures.Add(fixture);

        Assert.Equal([true], observed);                                                                                  // the hook ran once, on the template path only
        Assert.True(await ScalarAsync<DateTime>($"SELECT create_date FROM sys.databases WHERE name = N'{NameOf(fixture)}'") > partialCreated);   // the partial clone was removed and the database re-created, not reused
        Assert.Equal(1, template.Fallbacks);
        await using var db = fixture.CreateContext();
        Assert.Equal(CodeMigrations(), (await db.Database.GetAppliedMigrationsAsync()).ToList());                        // the database that exists now is the real-migration one
        var (data, logFile) = AlveraTestEnvironment.FilesOf(NameOf(fixture));
        Assert.True(File.Exists(data) && File.Exists(logFile));
        Assert.Equal(1, await DatabasesLikeAsync(NameOf(fixture)));
    }

    [Fact]
    public async Task A_template_build_failure_fails_loudly_for_every_fixture_and_leaves_nothing_behind()
    {
        var before = await DatabasesLikeAsync("AlveraTemplate_");
        var (template, log) = NewTemplate(beforeBuild: () => throw new InvalidOperationException("injected build failure"));
        var first = NewFixture(template, log);
        var second = NewFixture(template, log);

        var e1 = await Assert.ThrowsAsync<TemplateBuildException>(() => first.InitializeAsync());
        var e2 = await Assert.ThrowsAsync<TemplateBuildException>(() => second.InitializeAsync());

        Assert.Contains("injected build failure", e1.InnerException!.Message);
        Assert.Contains("injected build failure", e2.InnerException!.Message);                                          // not retried quietly, not turned into a fallback
        Assert.Equal(0, template.Fallbacks);
        Assert.Equal(before, await DatabasesLikeAsync("AlveraTemplate_"));
        Assert.Empty(Directory.GetFiles(template.BackupFolder));
        Assert.Contains("template_build_failed", log.ToString());
        Assert.Equal(0, await DatabasesLikeAsync(NameOf(first)));                                                        // and no half-created clone
    }

    [Fact]
    public async Task A_migration_list_that_no_longer_matches_is_detected_and_a_wrong_template_is_never_used()
    {
        IReadOnlyList<string> expected = CodeMigrations();
        var (template, log) = NewTemplate(expectedMigrations: () => expected);
        await StartAsync(template, log);
        Assert.Equal(1, template.Builds);

        expected = [.. CodeMigrations(), "99999999999999_ANewMigrationTheTemplateDoesNotHave"];                          // the code now has a migration the template does not
        var late = NewFixture(template, log);

        var e = await Assert.ThrowsAsync<TemplateBuildException>(() => late.InitializeAsync());                          // the rebuild finds the real migrations still lack it
        Assert.Contains("migration", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, template.Rebuilds);
        Assert.Contains("template_migration_mismatch", log.ToString());
    }

    [Fact]
    public async Task A_successful_runs_artifacts_are_removed_when_the_template_is_disposed()
    {
        var (template, log) = NewTemplate();
        var info = await template.EnsureBuiltAsync();
        var fixture = await StartAsync(template, log);
        var cloneFiles = AlveraTestEnvironment.FilesOf(NameOf(fixture));
        Assert.True(File.Exists(info.BackupPath));

        _fixtures.Remove(fixture);
        await fixture.DisposeAsync();
        template.Dispose();

        Assert.False(File.Exists(info.BackupPath));
        Assert.False(File.Exists(cloneFiles.Data) || File.Exists(cloneFiles.Log));
        Assert.Equal(0, await DatabasesLikeAsync(NameOf(fixture)));
        Assert.Contains("template_summary", log.ToString());
    }

    [Fact]
    public async Task Leftovers_of_an_earlier_run_are_reported_and_never_deleted()
    {
        var (template, log) = NewTemplate();
        var stale = Path.Combine(template.BackupFolder, "AlveraTemplate_00000000000000000000000000000000.bak");
        await File.WriteAllTextAsync(stale, "left by a killed run");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-5));

        var reported = template.ReportLeftovers();

        Assert.Contains(reported, r => r.Contains("AlveraTemplate_00000000000000000000000000000000.bak"));
        Assert.True(File.Exists(stale));                                                                                 // reported only
        Assert.Contains("template_leftover_reported", log.ToString());
        await template.EnsureBuiltAsync();                                                                               // a build does not delete it either
        template.Dispose();
        Assert.True(File.Exists(stale));
    }

    [Fact]
    public async Task A_real_migration_fixture_never_touches_the_template()
    {
        var (template, log) = NewTemplate();
        var fixture = new RealMigrationTestDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var db = fixture.CreateContext();
            Assert.Equal(CodeMigrations(), (await db.Database.GetAppliedMigrationsAsync()).ToList());
            Assert.Equal(0, template.Builds);
            Assert.Equal(0, template.Clones);
        }
        finally { await fixture.DisposeAsync(); }
    }
}
