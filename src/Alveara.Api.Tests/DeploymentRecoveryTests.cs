using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030-C01 slice 4 (brief 18.6, owner Q6; review R10): S37 - Deployment Administrator recovery with a one-time console token. The CLI refuses before setup;
/// with no Deployment Administrator a valid token creates exactly one; with a disabled, locked or MFA-protected one a valid token and its username reset it
/// (new password, enabled, lockout cleared, MFA off and recovery codes removed, stamp rotated, sessions closed); a wrong username is the same failure; five
/// failures void the token atomically; the token never reaches a log or the audit; outside Normal mode the route answers 503. C01 adds no enrolment gate.
/// </summary>
public class DeploymentRecoveryTests : IAsyncLifetime
{
    private const string OldPassword = "Seeded-Password-1";
    private const string NewPassword = "Recovered-Password-2026";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _factory = Factory();
    }

    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
        b.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        b.UseSetting("SetupAttemptRateLimit:PermitLimit", "500");
        b.ConfigureLogging(l => l.AddProvider(_logs));
    });

    public async Task DisposeAsync() { await _factory.DisposeAsync(); await _fixture.DisposeAsync(); }

    // ---------- helpers ----------

    private async Task<T> WithDbAsync<T>(Func<AlveraDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AlveraDbContext>());
    }

    /// <summary>Runs the CLI command and returns (exit code, the token line or null, everything written).</summary>
    private async Task<(int Exit, string? Token, string Output)> IssueAsync()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await RecoveryCommand.RunAsync(_factory.Services, output, error, "http://localhost:5173/recovery");
        var token = output.ToString().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length == 64 && l.All(Uri.IsHexDigit));
        return (exit, token, output + error.ToString());
    }

    private Task<HttpResponseMessage> RecoverAsync(string? token, string username, string password = NewPassword) =>
        _factory.CreateClient().PostAsJsonAsync("/api/recovery/deployment-administrator", new { token, username, newPassword = password });

    private Task<Guid> SeedDeploymentAdministratorAsync(bool disabled = false, bool locked = false) => WithDbAsync(async db =>
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = "deploy.r", PasswordHash = Pbkdf2PasswordHasher.Hash(OldPassword), Role = Role.Admin, IsDisabled = disabled,
            AccountKind = AccountKind.DeploymentAdministrator, CreatedAtUtc = DateTimeOffset.UtcNow,
            FailedLoginAttempts = locked ? 5 : 0, LockedOutUntilUtc = locked ? DateTimeOffset.UtcNow.AddHours(1) : null,
        };
        db.UserAccounts.Add(account);
        // a second enabled Administrator, so the last-administrator rule (and its database trigger) never blocks disabling the Deployment Administrator in a test
        db.UserAccounts.Add(new UserAccount
        {
            Id = Guid.NewGuid(), Username = "backup.admin", PasswordHash = Pbkdf2PasswordHasher.Hash(OldPassword), Role = Role.Admin, CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return account.Id;
    });

    private static async Task<string?> ErrorAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    // ---------- the CLI ----------

    [Fact]
    public async Task S37_the_cli_refuses_before_setup_and_issues_a_one_time_recovery_token_after_it()
    {
        Assert.Equal(0, await WithDbAsync(db => db.SetupTokens.CountAsync()));   // start the host first, in Normal mode (the mode is decided once, at start-up)
        await _fixture.ClearSetupCompletedAsync();
        var refused = await IssueAsync();
        Assert.Equal(RecoveryCommand.ExitNotSetUp, refused.Exit);
        Assert.Null(refused.Token);
        Assert.Equal(0, await WithDbAsync(db => db.SetupTokens.CountAsync()));

        await SeedDeploymentAdministratorAsync();
        var first = await IssueAsync();
        Assert.Equal(RecoveryCommand.ExitIssued, first.Exit);
        Assert.NotNull(first.Token);
        Assert.Contains("http://localhost:5173/recovery", first.Output);
        var second = await IssueAsync();
        var rows = await WithDbAsync(db => db.SetupTokens.AsNoTracking().OrderBy(t => t.IssuedAtUtc).ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(SetupTokenPurpose.Recovery, r.Purpose));
        Assert.NotNull(rows[0].VoidedAtUtc);   // issuing a new token voids the earlier unused one
        Assert.Null(rows[1].VoidedAtUtc);
        Assert.Equal(DeploymentSetupService.TokenLifetime, rows[1].ExpiresAtUtc - rows[1].IssuedAtUtc);
        Assert.Equal("recovery_failed", await ErrorAsync(await RecoverAsync(first.Token, "deploy.r")));   // the voided one no longer works
        Assert.Equal(HttpStatusCode.OK, (await RecoverAsync(second.Token, "deploy.r")).StatusCode);
    }

    // ---------- R1: no Deployment Administrator ----------

    [Fact]
    public async Task S37_with_no_deployment_administrator_a_valid_token_creates_exactly_one_even_under_two_concurrent_requests()
    {
        var (_, token, _) = await IssueAsync();   // the fixture is set up (BootstrapState) but has no Deployment Administrator: an upgraded installation (R1)
        var results = await Task.WhenAll(RecoverAsync(token, "new.deploy"), RecoverAsync(token, "other.deploy"));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);

        var created = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator));
        Assert.Equal(Role.Admin, created.Role);
        Assert.False(created.IsDisabled);
        Assert.False(created.MustChangePassword);
        var audit = await WithDbAsync(db => db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.DeploymentAdministratorRecovered));
        Assert.Equal($"accountId={created.Id}; method=Created", audit.Details);
        var signIn = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = created.Username, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    // ---------- R2/R3: reset an existing one ----------

    [Fact]
    public async Task S37_a_disabled_locked_deployment_administrator_with_mfa_is_reset_and_its_sessions_end()
    {
        var id = await SeedDeploymentAdministratorAsync();
        // MFA on with recovery codes, and an open session
        using (var scope = _factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var (secret, _) = await accounts.BeginMfaEnrollmentAsync(id);
            await accounts.ConfirmMfaEnrollmentAsync(id, Totp.GenerateCodeForTests(Totp.FromBase32(secret)));
        }
        var session = _factory.CreateClient();
        var login = await session.PostAsJsonAsync("/api/auth/login", new { username = "deploy.r", password = OldPassword });
        var challenge = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeToken").GetString();
        Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);   // MFA is on: the password alone pauses for a code
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [UserAccounts] SET [IsDisabled] = 1, [FailedLoginAttempts] = 5, [LockedOutUntilUtc] = {DateTimeOffset.UtcNow.AddHours(1)} WHERE [Id] = {id}"));
        var stampBefore = await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == id).Select(u => u.SecurityStamp).SingleAsync());
        Assert.True(await WithDbAsync(db => db.MfaRecoveryCodes.AnyAsync(c => c.UserAccountId == id)));

        var (_, token, _) = await IssueAsync();
        var response = await RecoverAsync(token, "deploy.r");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Reset", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("method").GetString());

        var account = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == id));
        Assert.False(account.IsDisabled);
        Assert.Equal(0, account.FailedLoginAttempts);
        Assert.Null(account.LockedOutUntilUtc);
        Assert.False(account.MfaEnabled);
        Assert.Null(account.MfaSecretProtected);
        Assert.Null(account.PendingMfaSecretProtected);
        Assert.NotEqual(stampBefore, account.SecurityStamp);
        Assert.False(await WithDbAsync(db => db.MfaRecoveryCodes.AnyAsync(c => c.UserAccountId == id)));
        Assert.Equal(1, await WithDbAsync(db => db.UserAccounts.CountAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator)));   // never a second one

        // the pending MFA challenge from before is dead (stamp rotated); the old password no longer works; the new one signs in with no MFA step (no enrolment gate in C01)
        Assert.NotEqual(HttpStatusCode.OK, (await session.PostAsJsonAsync("/api/auth/mfa/challenge", new { challengeToken = challenge, code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "deploy.r", password = OldPassword })).StatusCode);
        var fresh = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/api/auth/login", new { username = "deploy.r", password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/auth/permissions")).StatusCode);

        var audit = await WithDbAsync(db => db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.DeploymentAdministratorRecovered));
        Assert.StartsWith($"accountId={id}; method=Reset; mfaTurnedOff=true; recoveryCodesRevoked=", audit.Details);
        Assert.DoesNotContain("deploy.r", audit.Details);
    }

    [Fact]
    public async Task S37_a_wrong_username_is_the_same_failure_counts_against_the_token_and_changes_nothing()
    {
        var id = await SeedDeploymentAdministratorAsync(disabled: true);
        var (_, token, _) = await IssueAsync();
        var wrong = await RecoverAsync(token, "someone.else");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("recovery_failed", await ErrorAsync(wrong));
        Assert.True(await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == id).Select(u => u.IsDisabled).SingleAsync()));
        Assert.Equal(1, await WithDbAsync(db => db.SetupTokens.Where(t => t.Purpose == SetupTokenPurpose.Recovery).Select(t => t.FailedAttempts).SingleAsync()));
        Assert.False(await WithDbAsync(db => db.UserAccounts.AnyAsync(u => u.Username == "someone.else")));
        // the token itself was not consumed by the refused attempt: the right username still works
        Assert.Equal(HttpStatusCode.OK, (await RecoverAsync(token, "deploy.r")).StatusCode);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    public async Task S37_simultaneous_failures_all_count_and_the_fifth_voids_the_recovery_token_once(int attempts)
    {
        await SeedDeploymentAdministratorAsync();
        var (_, token, _) = await IssueAsync();
        await Task.WhenAll(Enumerable.Range(0, attempts).Select(i => RecoverAsync($"wrong-token-{i}", "deploy.r")));

        var row = await WithDbAsync(db => db.SetupTokens.AsNoTracking().SingleAsync(t => t.Purpose == SetupTokenPurpose.Recovery));
        Assert.Equal(DeploymentSetupService.MaxFailedAttempts, row.FailedAttempts);
        Assert.NotNull(row.VoidedAtUtc);
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.RecoveryTokenRejected)));
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.SetupTokenRejected)));
        Assert.Equal("recovery_failed", await ErrorAsync(await RecoverAsync(token, "deploy.r")));   // the right token is refused once voided
    }

    [Fact]
    public async Task S37_the_token_the_username_tried_and_the_password_never_reach_a_log_or_the_audit()
    {
        await SeedDeploymentAdministratorAsync();
        var (_, token, _) = await IssueAsync();
        await RecoverAsync(token, "probe.username");          // wrong username: a failure
        await RecoverAsync(token, "deploy.r");                // success
        var audit = await WithDbAsync(db => db.AuditLogEntries.AsNoTracking().Select(a => a.Details + " " + a.Reason).ToListAsync());
        var logs = _logs.Entries.Select(e => e.Message).ToList();
        foreach (var secret in new[] { token!, NewPassword, "probe.username" })
        {
            Assert.DoesNotContain(audit, line => line.Contains(secret));
            Assert.DoesNotContain(logs, line => line.Contains(secret));
        }
    }

    // ---------- the mode rule ----------

    [Fact]
    public async Task S37_outside_normal_mode_the_recovery_route_answers_503()
    {
        await _fixture.ClearSetupCompletedAsync();   // a never-set-up database: the host starts in Setup mode
        await using var setupHost = Factory();
        var response = await setupHost.CreateClient().PostAsJsonAsync("/api/recovery/deployment-administrator", new { token = "x", username = "y", newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.SetupTokens.CountAsync()));
    }
}
