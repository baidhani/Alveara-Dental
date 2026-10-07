using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// N002-R01-05: the story requires "least-privilege service configuration" and "database
/// permissions restricted to the server/service identity and approved maintenance path" — not
/// merely a design intention. This test actually creates a SQL Server login scoped to only
/// db_datareader + db_datawriter on a real database, connects through it (not the admin/Windows-
/// integrated connection the rest of the suite uses), proves ordinary application CRUD succeeds,
/// and proves a schema-changing/administrative operation is genuinely rejected — i.e. the
/// application does not actually need, and cannot exercise, elevated rights.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class LeastPrivilegeAccessTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private readonly string _loginName = $"alveara_test_app_{Guid.NewGuid():N}"[..30];
    private const string Password = "Test-Only-P@ssw0rd-Not-A-Secret-1";
    private string _restrictedConnectionString = string.Empty;

    public LeastPrivilegeAccessTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var admin = new SqlConnection(_fixture.ConnectionString);
        await admin.OpenAsync();

        async Task Exec(string sql)
        {
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        // Create a SQL login scoped ONLY to this test database, with ONLY read+write data roles —
        // explicitly no db_owner, no db_ddladmin, no db_backupoperator, no sysadmin.
        await Exec($"CREATE LOGIN [{_loginName}] WITH PASSWORD = '{Password}', CHECK_POLICY = OFF;");
        await Exec($"CREATE USER [{_loginName}] FOR LOGIN [{_loginName}];");
        await Exec($"ALTER ROLE db_datareader ADD MEMBER [{_loginName}];");
        await Exec($"ALTER ROLE db_datawriter ADD MEMBER [{_loginName}];");

        var builder = new SqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            IntegratedSecurity = false,
            UserID = _loginName,
            Password = Password,
        };
        _restrictedConnectionString = builder.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var admin = new SqlConnection(_fixture.ConnectionString);
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP USER IF EXISTS [{_loginName}]; DROP LOGIN IF EXISTS [{_loginName}];";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task A_login_restricted_to_db_datareader_and_db_datawriter_can_perform_ordinary_application_CRUD()
    {
        var options = new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_restrictedConnectionString).Options;
        await using var restrictedDb = new AlveraDbContext(options);

        var user = new Alveara.Api.Architecture.Identity.UserAccount
        {
            Id = Guid.NewGuid(),
            Username = $"least-priv-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        restrictedDb.UserAccounts.Add(user);
        await restrictedDb.SaveChangesAsync(); // ordinary write must succeed with only db_datawriter

        var readBack = await restrictedDb.UserAccounts.FindAsync(user.Id); // ordinary read must succeed with only db_datareader
        Assert.NotNull(readBack);
    }

    [Fact]
    public async Task A_login_restricted_to_db_datareader_and_db_datawriter_cannot_perform_schema_changing_or_administrative_operations()
    {
        await using var restrictedConnection = new SqlConnection(_restrictedConnectionString);
        await restrictedConnection.OpenAsync();

        // DDL must be rejected — the application identity has no db_ddladmin/db_owner rights.
        await using (var ddl = restrictedConnection.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE ShouldNotBeAllowed (Id INT);";
            await Assert.ThrowsAsync<SqlException>(() => ddl.ExecuteNonQueryAsync());
        }

        // BACKUP must be rejected — the application identity has no db_backupoperator/sysadmin
        // rights; only the approved maintenance path (a separately-privileged operator/service
        // account) may run BACKUP DATABASE, per the story's "approved maintenance path" rule.
        await using (var backup = restrictedConnection.CreateCommand())
        {
            backup.CommandText = "BACKUP DATABASE [master] TO DISK = 'NUL';";
            await Assert.ThrowsAsync<SqlException>(() => backup.ExecuteNonQueryAsync());
        }
    }
}
