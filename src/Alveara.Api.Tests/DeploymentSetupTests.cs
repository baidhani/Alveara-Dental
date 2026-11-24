using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Startup;
using Alveara.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 2 (brief S2-S5, S7, service level): first-run setup with a controllable clock. A token is issued once (only its hash is kept), expires after
/// exactly 15 minutes, is single use, is voided by five failures or by a newer token; every token failure is the same refusal; inputs are checked only after
/// the token verifies; setup succeeds exactly once even under a race; nothing secret reaches the audit log.
/// </summary>
public class DeploymentSetupServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
    private const string Password = "a-long-enough-password";

    public async Task InitializeAsync() { await _fixture.InitializeAsync(); await _fixture.ClearSetupCompletedAsync(); }   // ALV-N030 slice 7: a never-set-up database
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private DeploymentSetupService Service(AlveraDbContextScope scope, DateTimeOffset at) => new(scope.Db, new TestClock(at));

    private sealed class AlveraDbContextScope(TestDatabaseFixture f) : IAsyncDisposable
    {
        public Data.AlveraDbContext Db { get; } = f.CreateContext();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private async Task<string> IssueAsync(DateTimeOffset at)
    {
        await using var s = new AlveraDbContextScope(_fixture);
        return (await Service(s, at).IssueTokenAsync(default)).Token;
    }

    private async Task<UserAccount> CompleteAsync(string? token, DateTimeOffset at, string? username = "deploy", string? password = Password)
    {
        await using var s = new AlveraDbContextScope(_fixture);
        return await Service(s, at).CompleteAsync(token, username, password, default);
    }

    [Fact]
    public async Task A_valid_token_creates_the_account_only_Deployment_Administrator_once_and_nothing_secret_is_audited()
    {
        var token = await IssueAsync(T0);
        Assert.Matches("^[0-9a-f]{64}$", token);
        var account = await CompleteAsync(token, T0.AddMinutes(1));

        await using var db = _fixture.CreateContext();
        var stored = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id);
        Assert.Equal((AccountKind.DeploymentAdministrator, Role.Admin, false, false), (stored.AccountKind, stored.Role, stored.IsDisabled, stored.MustChangePassword));
        Assert.False(await db.StaffProfiles.AnyAsync(p => p.UserAccountId == account.Id));   // account-only
        Assert.True(await db.BootstrapStates.AnyAsync());
        var row = await db.SetupTokens.AsNoTracking().SingleAsync();
        Assert.Equal(DeploymentSetupService.Hash(token), row.TokenHash);   // only the hash is stored
        Assert.NotNull(row.ConsumedAtUtc);
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.SetupTokenIssued || a.EventType == AuditEventTypes.DeploymentAdministratorCreated).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => { Assert.DoesNotContain(token, a.Details); Assert.DoesNotContain(Password, a.Details); });

        await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync(token, T0.AddMinutes(2), "again"));   // used, and setup is no longer available
        await using var s = new AlveraDbContextScope(_fixture);
        await Assert.ThrowsAsync<SetupAlreadyCompletedException>(() => Service(s, T0.AddMinutes(3)).IssueTokenAsync(default));
    }

    [Fact]
    public async Task The_token_works_until_just_before_fifteen_minutes_and_not_at_fifteen()
    {
        var token = await IssueAsync(T0);
        await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync(token, T0.AddMinutes(15)));
        Assert.NotNull(await CompleteAsync(token, T0.AddMinutes(15).AddSeconds(-1)));   // the failure above was one of five; the token still works
    }

    [Fact]
    public async Task Five_failures_void_the_token_and_record_one_rejection_four_do_not()
    {
        var token = await IssueAsync(T0);
        for (var i = 0; i < 4; i++) await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync("wrong-" + i, T0));
        await using (var db = _fixture.CreateContext()) Assert.Null((await db.SetupTokens.AsNoTracking().SingleAsync()).VoidedAtUtc);
        await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync("wrong-5", T0));
        await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync(token, T0));   // voided: the right token no longer works

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.SetupTokenRejected));
        Assert.False(await check.UserAccounts.AnyAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator));
    }

    /// <summary>
    /// ALV-N030 R02 (review CR-1): simultaneous wrong attempts, each on its own context, released together. With exactly five, a lost update would leave
    /// the count below five and the token usable; with twelve, the count must stop at five. Either way the token is voided, the right token is refused,
    /// there is exactly one rejection row, later failures add none, and nothing attempted reaches the audit.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    public async Task Simultaneous_failures_are_all_counted_and_the_fifth_voids_the_token_once(int attempts)
    {
        var token = await IssueAsync(T0);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, attempts).Select(i => Task.Run(async () =>
        {
            await using var s = new AlveraDbContextScope(_fixture);
            var service = Service(s, T0);
            await gate.Task;
            await Assert.ThrowsAsync<SetupFailedException>(() => service.CompleteAsync($"wrong-{i}", $"tried-user-{i}", $"tried-password-{i}", default));
        })).ToList();
        gate.SetResult();
        await Task.WhenAll(tasks);

        await using (var db = _fixture.CreateContext())
        {
            var row = await db.SetupTokens.AsNoTracking().SingleAsync();
            Assert.NotNull(row.VoidedAtUtc);
            Assert.Equal(DeploymentSetupService.MaxFailedAttempts, row.FailedAttempts);
            Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.SetupTokenRejected));
        }
        await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync(token, T0));                 // the right token is refused now
        for (var i = 0; i < 3; i++) await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync("later-" + i, T0));

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.SetupTokenRejected));   // later failures add none
        Assert.Equal(DeploymentSetupService.MaxFailedAttempts, (await check.SetupTokens.AsNoTracking().SingleAsync()).FailedAttempts);
        Assert.False(await check.UserAccounts.AnyAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator));
        var details = await check.AuditLogEntries.AsNoTracking().Select(a => a.Details).ToListAsync();
        Assert.All(details, d =>
        {
            Assert.DoesNotContain(token, d);
            Assert.DoesNotContain("wrong-", d);
            Assert.DoesNotContain("tried-user-", d);
            Assert.DoesNotContain("tried-password-", d);
        });
    }

    [Fact]
    public async Task Issuing_a_new_token_voids_the_previous_one()
    {
        var first = await IssueAsync(T0);
        var second = await IssueAsync(T0.AddMinutes(1));
        await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync(first, T0.AddMinutes(2)));
        Assert.NotNull(await CompleteAsync(second, T0.AddMinutes(2)));
    }

    [Fact]
    public async Task Inputs_are_checked_only_after_the_token_and_a_bad_input_does_not_use_the_token()
    {
        var token = await IssueAsync(T0);
        await Assert.ThrowsAsync<SetupFailedException>(() => CompleteAsync("wrong", T0, username: "", password: "x"));   // no token: no hint about the inputs
        Assert.Equal("password_too_short", (await Assert.ThrowsAsync<SetupInputInvalidException>(() => CompleteAsync(token, T0, password: "short"))).Code);
        Assert.Equal("username_required", (await Assert.ThrowsAsync<SetupInputInvalidException>(() => CompleteAsync(token, T0, username: "  "))).Code);
        Assert.NotNull(await CompleteAsync(token, T0));   // still usable
    }

    [Fact]
    public async Task Two_concurrent_setups_with_the_same_token_create_exactly_one_account()
    {
        var token = await IssueAsync(T0);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            try { await CompleteAsync(token, T0, "deploy-" + i); return true; }
            catch (SetupFailedException) { return false; }
        })));
        Assert.Single(results, ok => ok);
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.UserAccounts.CountAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator));
        Assert.Equal(1, await db.BootstrapStates.CountAsync());
    }
}

/// <summary>
/// ALV-N030 slice 2 at the HTTP boundary and the installer command (brief S1, S2, S5, S6, S7): sign-up and the old first-admin route are gone (404); the status
/// route discloses only whether setup is available; every token failure has the identical body; the setup route is rate-limited; the token and the password
/// appear in no log line; the CLI prints the token once, voids an older one on re-issue, and refuses (exit 2) once setup is complete.
/// </summary>
public class DeploymentSetupApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly RecordingSetupRestart _restart = new();
    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>ALV-N030 slice 7: stands in for the real restart (exit code 75 and a stop), which would end this test's host and set the test run's exit code.</summary>
    public sealed class RecordingSetupRestart : ISetupRestart
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        public void RequestRestart() => Interlocked.Increment(ref _requests);
    }

    private WebApplicationFactory<Program> NewHost() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
        b.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        b.UseSetting("SetupAttemptRateLimit:PermitLimit", "8");
        b.ConfigureLogging(l => l.AddProvider(_logs));
        b.ConfigureServices(services => services.AddSingleton<ISetupRestart>(_restart));
    });

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await _fixture.ClearSetupCompletedAsync();   // a never-set-up installation: the host starts in Setup mode
        _factory = NewHost();
    }

    public async Task DisposeAsync() { await _factory.DisposeAsync(); await _fixture.DisposeAsync(); }

    private async Task<(int Exit, string Out, string Err)> IssueViaCliAsync()
    {
        var output = new StringWriter(); var error = new StringWriter();
        var exit = await SetupCommand.RunAsync(_factory.Services, output, error, "http://localhost:5173/setup");
        return (exit, output.ToString(), error.ToString());
    }

    private static string TokenFrom(string cliOutput) => cliOutput.Split('\n').Select(l => l.Trim()).Single(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^[0-9a-f]{64}$"));

    private async Task<(HttpStatusCode Status, string Body)> SetupAsync(HttpClient client, string? token, string username = "deploy", string password = "a-long-enough-password")
    {
        var response = await client.PostAsJsonAsync("/api/setup/deployment-administrator", new CompleteSetupRequest(token, username, password));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sign_up_and_the_old_first_admin_route_no_longer_exist()
    {
        // No controller owns these paths any more. The answer is 404, or 405 when a built client exists locally: the SPA fallback
        // (Program.cs, MapFallbackToFile) is GET-only, so a POST to an unowned path meets it and is refused with 405. Either way nothing handles it.
        // ALV-N030 slice 7: asked of a SET-UP installation (Normal mode); in Setup mode every other /api route answers 503 anyway.
        await _fixture.MarkSetupCompletedAsync();
        var client = _factory.CreateClient();
        HttpStatusCode[] refused = [HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed];
        Assert.Contains((await client.PostAsJsonAsync("/api/auth/register", new { username = "x", password = "y" })).StatusCode, refused);
        Assert.Contains((await client.PostAsJsonAsync("/api/auth/bootstrap-admin", new { username = "x", password = "y", secret = "z" })).StatusCode, refused);
        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<Alveara.Api.Data.AlveraDbContext>().UserAccounts.AnyAsync(u => u.Username == "x"));
    }

    [Fact]
    public async Task Setup_through_the_cli_and_the_route_with_identical_failures_and_no_secret_in_any_log()
    {
        var client = _factory.CreateClient();
        var status = await client.GetAsync("/api/setup/status");
        Assert.Equal("no-store", status.Headers.CacheControl?.ToString());
        Assert.Equal("{\"mode\":\"setup\",\"setupAvailable\":true}", await status.Content.ReadAsStringAsync());   // ALV-N030 slice 7 (16.2a)

        var (exit, output, _) = await IssueViaCliAsync();
        Assert.Equal(SetupCommand.ExitIssued, exit);
        var stale = TokenFrom(output);
        var (exit2, output2, _) = await IssueViaCliAsync();   // a re-issue voids the first
        Assert.Equal(SetupCommand.ExitIssued, exit2);
        var token = TokenFrom(output2);

        var wrong = await SetupAsync(client, "0000");
        var voided = await SetupAsync(client, stale);
        var missing = await SetupAsync(client, null);
        Assert.All(new[] { wrong, voided, missing }, r => { Assert.Equal(HttpStatusCode.BadRequest, r.Status); Assert.Equal("{\"error\":\"setup_failed\"}", r.Body); });
        Assert.Equal(0, _restart.Requests);                                                    // a failed setup never asks for a restart

        Assert.Equal(HttpStatusCode.Created, (await SetupAsync(client, token)).Status);
        await WaitForAsync(() => _restart.Requests == 1);                                       // asked once, after the 201 was sent
        var used = await SetupAsync(client, token, "second");
        Assert.Equal((HttpStatusCode.BadRequest, "{\"error\":\"setup_failed\"}"), used);
        Assert.Equal(1, _restart.Requests);

        // the restarted process (a new host on the same database) comes up Normal
        await using (var restarted = NewHost())
            Assert.Equal("{\"mode\":\"normal\",\"setupAvailable\":false}", await (await restarted.CreateClient().GetAsync("/api/setup/status")).Content.ReadAsStringAsync());

        var (exit3, _, err3) = await IssueViaCliAsync();
        Assert.Equal(SetupCommand.ExitAlreadyCompleted, exit3);
        Assert.DoesNotContain(token, err3);

        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains(token) || e.Message.Contains(stale) || e.Message.Contains("a-long-enough-password"));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await Task.Delay(100);
        Assert.True(condition());
    }

    [Fact]
    public async Task The_setup_route_is_rate_limited()
    {
        var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 9; i++) statuses.Add((await SetupAsync(client, "wrong-" + i)).Status);
        Assert.True(statuses.Count(s => s == HttpStatusCode.BadRequest) == 8, "statuses: " + string.Join(",", statuses.Select(s => (int)s)));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }
}
