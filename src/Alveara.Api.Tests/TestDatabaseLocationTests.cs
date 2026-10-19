using Microsoft.Data.SqlClient;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// Where a test database's files live: inside Test-Data\Database of the one data root (<see cref="AlveraTestEnvironment"/>), never in the SQL Server instance's default folders; and gone after the drop.
/// Each test disposes everything it creates in a finally block, so a failure cannot leak a database.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class TestDatabaseLocationTests
{
    private static async Task<List<string>> FilesOfAsync(string database)
    {
        await using var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID(@n)";
        cmd.Parameters.AddWithValue("@n", database);
        var files = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) files.Add(r.GetString(0));
        return files;
    }

    [Fact]
    public async Task A_fixture_database_keeps_both_its_files_in_the_test_data_database_folder_and_the_drop_removes_them()
    {
        var fixture = new TestDatabaseFixture();
        await fixture.InitializeAsync();
        string database;
        List<string> files;
        try
        {
            database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
            files = await FilesOfAsync(database);
            Assert.Equal(2, files.Count);
            Assert.All(files, f => Assert.Equal(AlveraTestEnvironment.DatabaseFolder, Path.GetDirectoryName(f), ignoreCase: true));
            Assert.Contains(files, f => f.EndsWith(".mdf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(files, f => f.EndsWith(".ldf", StringComparison.OrdinalIgnoreCase));
            Assert.All(files, f => Assert.True(File.Exists(f), $"{f} should exist while the database does"));
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }

        await fixture.DisposeAsync();
        Assert.Empty(await FilesOfAsync(database));
        Assert.All(files, f => Assert.False(File.Exists(f), $"{f} should be gone after the drop"));
    }

    [Fact]
    public async Task A_fixture_database_has_read_committed_snapshot_on_exactly_as_one_created_by_ef_core_does()
    {
        // The application's concurrency relies on it (see AlveraTestEnvironment.CreateDatabaseIfMissingSql): a database created without it deadlocks concurrent inserts that a trigger reads.
        var fixture = new TestDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
            await using var c = new SqlConnection(AlveraTestEnvironment.MasterConnectionString);
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = @n";
            cmd.Parameters.AddWithValue("@n", database);
            Assert.True((bool)(await cmd.ExecuteScalarAsync())!);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public void The_test_data_folders_all_sit_under_the_single_data_root()
    {
        var root = Path.GetFullPath(AlveraTestEnvironment.DataRoot);
        foreach (var folder in new[] { AlveraTestEnvironment.TestData, AlveraTestEnvironment.DatabaseFolder, AlveraTestEnvironment.BackupFolder, AlveraTestEnvironment.StorageFolder, AlveraTestEnvironment.KeysFolder, AlveraTestEnvironment.TempFolder })
            Assert.StartsWith(root, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(root, "Test-Data"), AlveraTestEnvironment.TestData, ignoreCase: true);
    }

    [Fact]
    public void The_applications_own_file_settings_point_into_test_data_for_every_api_host_a_test_starts()
    {
        var root = Path.GetFullPath(AlveraTestEnvironment.TestData);
        foreach (var name in new[] { "StorageRoot", "DataProtectionKeysPath", "Backup__Root" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            Assert.False(string.IsNullOrEmpty(value), $"{name} should be set for the test process");
            Assert.StartsWith(root, Path.GetFullPath(value!), StringComparison.OrdinalIgnoreCase);   // nothing falls back to App_Data inside the repository
        }
    }

    [Theory]
    [InlineData(@"relative\folder")]
    [InlineData(@".\here")]
    [InlineData("alvera-data")]
    public void A_data_root_that_is_not_an_absolute_path_is_refused(string root)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => AlveraTestEnvironment.ValidateRoot(root));
        Assert.Contains(AlveraTestEnvironment.DataRootVariable, refused.Message);
    }

    [Theory]
    [InlineData("x'; DROP DATABASE master;--")]
    [InlineData("name with space")]
    [InlineData("")]
    [InlineData("a]b")]
    public void A_database_name_that_is_not_a_plain_identifier_is_refused_before_any_sql_is_built(string name)
    {
        Assert.Throws<ArgumentException>(() => AlveraTestEnvironment.CreateDatabaseIfMissingSql(name));
    }

    [Fact]
    public void Creating_the_same_database_statement_twice_is_safe_to_repeat_because_it_tests_for_existence_first()
    {
        var sql = AlveraTestEnvironment.CreateDatabaseIfMissingSql("AlveraTest_probe");
        Assert.StartsWith("IF DB_ID(N'AlveraTest_probe') IS NULL BEGIN CREATE DATABASE", sql);
        Assert.Contains(AlveraTestEnvironment.DatabaseFolder, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET READ_COMMITTED_SNAPSHOT ON", sql);
    }
}
