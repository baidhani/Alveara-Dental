using Microsoft.Data.SqlClient;

namespace Alveara.Api.Architecture.Backup;

/// <summary>
/// Database-consistent snapshot seam for ALV-N002, consumed by the actual backup story
/// (ALV-N004). Uses SQL Server's own <c>BACKUP DATABASE</c>, which is transactionally consistent
/// even while the database is in use — never a raw copy of the live .mdf/.ldf files, which could
/// capture a torn/inconsistent state mid-write.
/// </summary>
public interface IBackupSnapshotProvider
{
    /// <summary>Creates a consistent backup file at <paramref name="destinationPath"/> and returns its size.</summary>
    Task<long> CreateSnapshotAsync(string destinationPath, CancellationToken cancellationToken = default);
}

public sealed class SqlServerBackupSnapshotProvider : IBackupSnapshotProvider
{
    private readonly string _connectionString;
    private readonly string _databaseName;

    public SqlServerBackupSnapshotProvider(string connectionString, string databaseName)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;
    }

    public async Task<long> CreateSnapshotAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Database/file names cannot be parameterized in T-SQL DDL; both are server-controlled
        // configuration values (never user input), so this is not a SQL-injection risk.
        var sql = $"BACKUP DATABASE [{_databaseName}] TO DISK = @path WITH INIT, CHECKSUM";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@path", destinationPath);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new FileInfo(destinationPath).Length;
    }
}
