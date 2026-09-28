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
    private readonly string _databaseName = $"AlveraTest_{Guid.NewGuid():N}";
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AlveraDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new AlveraDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        var masterConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    public AlveraDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AlveraDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AlveraDbContext(options);
    }
}
