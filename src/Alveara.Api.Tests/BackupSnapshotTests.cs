using Alveara.Api.Architecture.Backup;
using Xunit;

namespace Alveara.Api.Tests;

[Collection(ParallelismCollections.SerialServer)]
public class BackupSnapshotTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly TestDatabaseFixture _fixture;
    private readonly string _backupPath = Path.Combine(Path.GetTempPath(), $"alveara-backup-test-{Guid.NewGuid():N}.bak");

    public BackupSnapshotTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Creates_a_real_SQL_Server_consistent_backup_file_not_a_raw_file_copy()
    {
        var databaseName = ExtractDatabaseName(_fixture.ConnectionString);
        var provider = new SqlServerBackupSnapshotProvider(_fixture.ConnectionString, databaseName);

        var size = await provider.CreateSnapshotAsync(_backupPath);

        Assert.True(File.Exists(_backupPath));
        Assert.True(size > 0);
        Assert.Equal(size, new FileInfo(_backupPath).Length);
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        return builder.InitialCatalog;
    }

    public void Dispose()
    {
        if (File.Exists(_backupPath)) File.Delete(_backupPath);
    }
}
