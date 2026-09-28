using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// N002-R01-06: the previous migration coverage only exercised a fresh-database initial migration
/// and a re-run no-op — neither is an actual "upgrade an existing, populated database" scenario,
/// which the story's acceptance item explicitly requires.
/// </summary>
public class MigrationUpgradeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MigrationUpgradeTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Upgrading_a_database_already_on_an_older_migration_preserves_existing_data_and_adds_the_new_schema()
    {
        // The fixture already migrated to latest; roll the *test* database back to just the
        // first migration to set up a genuine "older version" starting point.
        var firstMigration = _fixture.CreateContext().Database.GetMigrations().OrderBy(m => m).First();

        await using (var db = _fixture.CreateContext())
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(firstMigration); // downgrade to only the initial migration
        }

        Guid seededUserId;
        await using (var db = _fixture.CreateContext())
        {
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Single(applied); // confirms we are genuinely starting from the older schema

            var user = new Alveara.Api.Architecture.Identity.UserAccount
            {
                Id = Guid.NewGuid(),
                Username = $"pre-upgrade-user-{Guid.NewGuid():N}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
            seededUserId = user.Id;
        }

        // The actual upgrade.
        await using (var db = _fixture.CreateContext())
        {
            await db.Database.MigrateAsync();
        }

        await using var verifyDb = _fixture.CreateContext();
        var pendingAfterUpgrade = await verifyDb.Database.GetPendingMigrationsAsync();
        Assert.Empty(pendingAfterUpgrade); // fully upgraded

        var preservedUser = await verifyDb.UserAccounts.FindAsync(seededUserId);
        Assert.NotNull(preservedUser); // pre-existing data survived the upgrade

        var canQueryNewTable = await verifyDb.BackgroundJobEffectReceipts.CountAsync(); // new table from the second migration exists and is queryable
        Assert.Equal(0, canQueryNewTable);
    }
}

/// <summary>
/// A separate, deliberately broken migration set (its own throwaway DbContext/model, its own
/// migrations, its own database) used only to prove SQL Server's per-migration transaction
/// behavior: a migration whose Up() fails must not leave the schema half-upgraded, and must not
/// destroy pre-existing data. This does not touch the real Alveara schema/migrations at all.
/// </summary>
public class MigrationFailureTests : IAsyncLifetime
{
    private readonly string _databaseName = $"AlveraMigrationFailureTest_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";
        await using var db = CreateContext();
        // Target the first migration explicitly — a plain MigrateAsync() would apply every
        // migration EF discovers in this assembly, including the deliberately-broken second one.
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("00000000000001_FailureTest_Initial");
    }

    public async Task DisposeAsync()
    {
        var masterConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID('{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }

    private FailureTestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FailureTestDbContext>().UseSqlServer(_connectionString,
            sql => sql.MigrationsAssembly(typeof(MigrationFailureTests).Assembly.FullName)).Options);

    [Fact]
    public async Task A_partially_executed_failing_migration_is_fully_rolled_back_not_left_half_applied_and_does_not_destroy_existing_data()
    {
        Guid seededId;
        await using (var db = CreateContext())
        {
            var row = new FailureTestRow { Id = Guid.NewGuid(), Note = "pre-existing data" };
            db.Rows.Add(row);
            await db.SaveChangesAsync();
            seededId = row.Id;
        }

        await using (var db = CreateContext())
        {
            var ex = await Record.ExceptionAsync(() => db.Database.MigrateAsync());
            Assert.NotNull(ex); // the deliberately-broken second migration must actually fail
        }

        await using var verifyDb = CreateContext();
        var appliedMigrations = (await verifyDb.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Single(appliedMigrations); // the failing migration was NOT recorded as applied
        Assert.DoesNotContain(appliedMigrations, m => m.Contains("BrokenSecondMigration"));

        var preservedRow = await verifyDb.Rows.FindAsync(seededId);
        Assert.NotNull(preservedRow); // pre-existing data survived the failed migration attempt
        Assert.Equal("pre-existing data", preservedRow!.Note);

        // The key N002-R02-04 assertion: statement 1 (ADD COLUMN) genuinely executed before
        // statement 2 failed. If SQL Server's per-migration transaction only protected against
        // "never ran" rather than actually rolling back a partially-executed migration, this
        // column would still exist here.
        var columnExists = await ColumnExistsAsync(verifyDb, "Rows", "AddedByPartialMigration");
        Assert.False(columnExists, "The first statement's effect must be rolled back along with the migration, not left half-applied.");
    }

    private static async Task<bool> ColumnExistsAsync(FailureTestDbContext db, string tableName, string columnName)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND COLUMN_NAME = @column";
            var tableParam = command.CreateParameter();
            tableParam.ParameterName = "@table";
            tableParam.Value = tableName;
            command.Parameters.Add(tableParam);
            var columnParam = command.CreateParameter();
            columnParam.ParameterName = "@column";
            columnParam.Value = columnName;
            command.Parameters.Add(columnParam);
            var count = (int)(await command.ExecuteScalarAsync())!;
            return count > 0;
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}

public class FailureTestRow
{
    public Guid Id { get; set; }
    public required string Note { get; set; }
}

public class FailureTestDbContext(DbContextOptions<FailureTestDbContext> options) : DbContext(options)
{
    public DbSet<FailureTestRow> Rows => Set<FailureTestRow>();
}

[DbContext(typeof(FailureTestDbContext))]
[Migration("00000000000001_FailureTest_Initial")]
public class FailureTestInitialMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Rows",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Note = table.Column<string>(nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Rows", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Rows");
    }
}

/// <summary>
/// Performs a REAL, observable DDL change first (adds a genuine column), then fails on a second
/// statement — so a passing test proves SQL Server's per-migration transaction actually rolls
/// back a *partially executed* migration, not merely that an immediately-failing single-statement
/// migration leaves no trace (N002-R02-04: the R02 version of this migration only had the second,
/// always-failing statement, so it could not distinguish "never ran" from "ran and rolled back").
/// </summary>
[DbContext(typeof(FailureTestDbContext))]
[Migration("00000000000002_BrokenSecondMigration")]
public class FailureTestBrokenMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Statement 1: succeeds — a real, checkable schema change.
        migrationBuilder.Sql("ALTER TABLE [Rows] ADD [AddedByPartialMigration] INT NULL;");
        // Statement 2: fails — forces the whole migration (including statement 1) to roll back.
        migrationBuilder.Sql("ALTER TABLE [Rows] DROP COLUMN [ThisColumnDoesNotExist];");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE [Rows] DROP COLUMN IF EXISTS [AddedByPartialMigration];");
    }
}
