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
            Console.Error.WriteLine($"[TestDatabaseFixture] releasing pooled connections failed ({ex.GetType().Name}); the drop may be slower. Database {_databaseName}.");
        }

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
