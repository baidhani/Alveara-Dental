using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// TESTSPEED-P2 Gate 2 correction: LeastPrivilegeAccessTests used to swallow a failed DROP LOGIN (its own pooled session was still logged in), leaking two server logins per run. These tests
/// prove the cleanup of that class: the exact server login is gone afterwards even with pooled sessions of both kinds left behind, a drop that really cannot succeed is thrown and not swallowed,
/// and a session that is still closing is waited for a bounded number of times. In the serial collection: they create and drop server-level logins.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class LeastPrivilegeLoginCleanupTests
{
    private const string MasterCs = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private static async Task<int> ScalarAsync(string sql, string name)
    {
        await using var c = new SqlConnection(MasterCs);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@n", name);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static Task<bool> LoginExistsAsync(string login) => ScalarAsync("SELECT COUNT(*) FROM sys.server_principals WHERE name = @n", login).ContinueWith(t => t.Result > 0);

    private static Task<int> SessionsOfAsync(string login) => ScalarAsync("SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE login_name = @n", login);

    [Fact]
    public async Task The_exact_login_is_gone_after_cleanup_even_with_pooled_sessions_of_both_kinds_left_behind()
    {
        var fixture = new TestDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            var subject = new LeastPrivilegeAccessTests(fixture);
            await subject.InitializeAsync();
            Assert.True(await LoginExistsAsync(subject.LoginName));

            // leave behind exactly what the real tests leave: a pooled session built by EF and a pooled session built directly from the string
            var options = new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(subject.RestrictedConnectionString).Options;
            await using (var viaEf = new AlveraDbContext(options)) _ = await viaEf.UserAccounts.CountAsync();
            await using (var direct = new SqlConnection(subject.RestrictedConnectionString)) await direct.OpenAsync();
            Assert.True(await SessionsOfAsync(subject.LoginName) > 0);                              // the leak scenario really exists: sessions of the login are alive

            await subject.DisposeAsync();

            Assert.False(await LoginExistsAsync(subject.LoginName));                                 // the exact login is absent from the server
            Assert.Equal(0, await SessionsOfAsync(subject.LoginName));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_drop_that_cannot_succeed_is_thrown_not_swallowed_and_succeeds_once_the_session_is_released()
    {
        var fixture = new TestDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            var subject = new LeastPrivilegeAccessTests(fixture);
            await subject.InitializeAsync();
            var waits = 0;
            subject.DelayForTest = _ => { waits++; return Task.CompletedTask; };

            // a session that no pool clearing can close: not pooled, held open by the test
            var held = new SqlConnection(new SqlConnectionStringBuilder(subject.RestrictedConnectionString) { Pooling = false }.ConnectionString);
            await held.OpenAsync();
            try
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => subject.DisposeAsync());
                Assert.Equal(15434, error.Number);                                                   // the real failure surfaces
                Assert.Equal(5, LeastPrivilegeAccessTests.MaxLoginDropAttempts);                  // the approved cap, as a literal
                Assert.Equal(4, waits);                                                              // bounded: four waits between five attempts, then it gives up
                Assert.True(await LoginExistsAsync(subject.LoginName));                              // and the login is still there, which the failure honestly reports
            }
            finally
            {
                await held.DisposeAsync();
            }

            await subject.DisposeAsync();                                                            // with the session gone the same cleanup now succeeds
            Assert.False(await LoginExistsAsync(subject.LoginName));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_session_that_is_still_closing_is_waited_for_and_the_second_attempt_succeeds()
    {
        var fixture = new TestDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            var subject = new LeastPrivilegeAccessTests(fixture);
            await subject.InitializeAsync();
            var held = new SqlConnection(new SqlConnectionStringBuilder(subject.RestrictedConnectionString) { Pooling = false }.ConnectionString);
            await held.OpenAsync();
            var waits = 0;
            subject.DelayForTest = async _ => { waits++; await held.DisposeAsync(); };               // the session closes exactly while the cleanup waits

            await subject.DisposeAsync();                                                            // first attempt fails with 15434, the wait releases the session, the retry succeeds

            Assert.Equal(1, waits);
            Assert.False(await LoginExistsAsync(subject.LoginName));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
