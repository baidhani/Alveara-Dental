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

    /// <summary>The exact server login this instance created (used by LeastPrivilegeLoginCleanupTests to prove it is gone after cleanup).</summary>
    internal string LoginName => _loginName;

    /// <summary>The connection string of the restricted login (used by LeastPrivilegeLoginCleanupTests to leave pooled sessions behind).</summary>
    internal string RestrictedConnectionString => _restrictedConnectionString;

    /// <summary>Test-only: replaces the wait between drop attempts so a test can release a held session at exactly that moment.</summary>
    internal Func<TimeSpan, Task> DelayForTest { get; set; } = Task.Delay;

    /// <summary>Test-only: lets a test replace the statement that removes the login (to force a failure that is not "still logged in").</summary>
    internal Func<string, string> DropStatementForTest { get; set; } = statement => statement;

    /// <summary>TESTSPEED-P2: at most this many attempts to drop the login while a session of it is still closing.</summary>
    internal const int MaxLoginDropAttempts = 5;

    /// <summary>SQL Server 15434: "Could not drop login ... as the user is currently logged in".</summary>
    private const int LoginStillLoggedInError = 15434;

    /// <summary>
    /// Releases the pooled sessions that belong to the restricted login. They are what kept the login "currently logged in" and made DROP LOGIN fail (a failure the old cleanup swallowed, leaking
    /// two server logins per run). Both forms of connection used by the tests are cleared: the one EF builds and the one built directly from the string, which do not share a pool.
    /// </summary>
    private void ReleaseRestrictedPools()
    {
        if (_restrictedConnectionString.Length == 0) return;

        using (var direct = new SqlConnection(_restrictedConnectionString)) SqlConnection.ClearPool(direct);

        var options = new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_restrictedConnectionString).Options;
        using var viaEf = new AlveraDbContext(options);
        SqlConnection.ClearPool((SqlConnection)viaEf.Database.GetDbConnection());
    }

    /// <summary>
    /// Removes the user and the server login and PROVES it: a drop that fails is retried a bounded number of times only for the "still logged in" error (the pooled session is closed
    /// asynchronously), any other failure, or the same failure after the last attempt, is thrown (never swallowed), and the login must be absent from sys.server_principals afterwards.
    /// </summary>
    public async Task DisposeAsync()
    {
        ReleaseRestrictedPools();

        await using var admin = new SqlConnection(_fixture.ConnectionString);
        await admin.OpenAsync();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var drop = admin.CreateCommand();
                // DROP LOGIN has no IF EXISTS form (T-SQL rejects it with "Incorrect syntax near the keyword 'IF'"); the old cleanup used it, so the whole batch failed every time and the swallowed
                // error hid that the login was never dropped.
                drop.CommandText = DropStatementForTest($"DROP USER IF EXISTS [{_loginName}]; IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{_loginName}') DROP LOGIN [{_loginName}];");
                await drop.ExecuteNonQueryAsync();
                break;
            }
            catch (SqlException ex) when (ex.Number == LoginStillLoggedInError && attempt < MaxLoginDropAttempts)
            {
                await DelayForTest(TimeSpan.FromMilliseconds(100));      // a session that pool clearing cannot close (held open) is waited for; the pools were already released above
            }
        }

        await using var check = admin.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sys.server_principals WHERE name = @name";
        check.Parameters.AddWithValue("@name", _loginName);
        var remaining = (int)(await check.ExecuteScalarAsync())!;
        if (remaining != 0) throw new InvalidOperationException($"The server login {_loginName} is still present after cleanup.");
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
