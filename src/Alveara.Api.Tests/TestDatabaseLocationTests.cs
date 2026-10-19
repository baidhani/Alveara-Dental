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
    public void The_test_data_folders_all_sit_under_the_single_data_root()
    {
        var root = Path.GetFullPath(AlveraTestEnvironment.DataRoot);
        foreach (var folder in new[] { AlveraTestEnvironment.TestData, AlveraTestEnvironment.DatabaseFolder, AlveraTestEnvironment.BackupFolder, AlveraTestEnvironment.StorageFolder, AlveraTestEnvironment.KeysFolder, AlveraTestEnvironment.TempFolder })
            Assert.StartsWith(root, Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(root, "Test-Data"), AlveraTestEnvironment.TestData, ignoreCase: true);
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
        Assert.StartsWith("IF DB_ID(N'AlveraTest_probe') IS NULL CREATE DATABASE", sql);
        Assert.Contains(AlveraTestEnvironment.DatabaseFolder, sql, StringComparison.OrdinalIgnoreCase);
    }
}
