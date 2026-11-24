using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 3 (brief section 5, test row S10): the forced-change gate, the self-service change-password route and the identity fields.
/// <list type="bullet">
/// <item>While <c>MustChangePassword</c> is set, a sample of ten signed-in routes answers 403 <c>password_change_required</c>; the five exempt actions still work.</item>
/// <item>The exempt set is exactly five actions (reflection over every controller).</item>
/// <item>A change clears the flag, rotates the stamp (the old cookie is rejected), re-issues the session and audits once; the sampled routes are then no longer gated.</item>
/// <item>Failure paths: wrong current password, too short, unchanged; no CSRF token. A completed administrator reset clears the flag. One refusal row per session.</item>
/// </list>
/// </summary>
public class PasswordChangeGateTests : IAsyncLifetime
{
    private const string TemporaryPassword = "Temporary-Pass-1";
    private const string NewPassword = "Chosen-New-Password-2";

    private readonly TestDatabaseFixture _fixture = new();
    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _factory = null!;

    // Ten representative signed-in routes, across the auth, patient, scheduling and configuration areas; an Admin may use every one of them normally.
    private static readonly (HttpMethod Method, string Url)[] Sample =
    [
        (HttpMethod.Get, "/api/auth/whoami"),
        (HttpMethod.Get, "/api/auth/users"),
        (HttpMethod.Get, "/api/auth/permission-matrix"),
        (HttpMethod.Get, "/api/auth/audit-log"),
        (HttpMethod.Get, "/api/patients"),
        (HttpMethod.Get, "/api/appointments"),
        (HttpMethod.Get, "/api/config/practice"),
        (HttpMethod.Get, "/api/config/staff"),
        (HttpMethod.Get, "/api/config/providers"),
        (HttpMethod.Post, "/api/auth/mfa/enroll"),
    ];

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            b.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
            b.ConfigureLogging(l => l.AddProvider(_logs));
        });
    }

    public async Task DisposeAsync() { await _factory.DisposeAsync(); await _fixture.DisposeAsync(); }

    private async Task<Guid> SeedAccountAsync(string username, bool mustChange, Role role = Role.Admin)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(TemporaryPassword), Role = role,
            AccountKind = AccountKind.Staff, MustChangePassword = mustChange, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task<HttpClient> SignInAsync(string username, string password = TemporaryPassword)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/csrf-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string csrf, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", csrf);
        if (method != HttpMethod.Get) request.Content = JsonContent.Create(body ?? new { });
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, string csrf, string? current, string? next) =>
        SendAsync(client, HttpMethod.Post, "/api/auth/password/change", csrf, new ChangePasswordRequest(current, next));

    private async Task<T> WithDbAsync<T>(Func<AlveraDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AlveraDbContext>());
    }

    [Fact]
    public void The_exempt_set_is_exactly_the_five_named_actions()
    {
        var exempt = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AllowedWhilePasswordChangeRequiredAttribute>() is not null)
                .Select(m => $"{t.Name}.{m.Name}"))
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "AuthController.ChangePassword", "AuthController.CompleteMfaChallenge", "AuthController.GetCsrfToken",
            "AuthController.Logout", "AuthController.MyPermissions",
        }, exempt);
    }

    [Fact]
    public async Task A_forced_account_is_refused_everywhere_but_the_exempt_routes_until_it_changes_its_password()
    {
        var id = await SeedAccountAsync("forced.admin", mustChange: true);
        var client = await SignInAsync("forced.admin");
        var csrf = await CsrfAsync(client);   // exempt route 1

        // Exempt route 2: the identity call works and reports the flag and the account facts.
        var me = await (await client.GetAsync("/api/auth/permissions")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal("Staff", me.GetProperty("accountKind").GetString());
        Assert.False(me.GetProperty("isPracticeOwner").GetBoolean());
        Assert.False(me.GetProperty("practiceOwnerExists").GetBoolean());

        foreach (var (method, url) in Sample)
        {
            var refused = await SendAsync(client, method, url, csrf);
            Assert.True(refused.StatusCode == HttpStatusCode.Forbidden, $"{method} {url} answered {(int)refused.StatusCode}");
            Assert.Equal("password_change_required", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }
        // One refusal row for this session, not one per request.
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PasswordChangeRequiredRefusal)));

        var stampBefore = await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == id).Select(u => u.SecurityStamp).SingleAsync());
        var oldCookie = await CookieOfAnotherSessionAsync("forced.admin");   // a second session of the same account, issued before the change

        var changed = await ChangeAsync(client, csrf, TemporaryPassword, NewPassword);   // exempt route 3
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var account = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == id));
        Assert.False(account.MustChangePassword);
        Assert.NotEqual(stampBefore, account.SecurityStamp);
        Assert.True(Pbkdf2PasswordHasher.Verify(NewPassword, account.PasswordHash!));
        var audit = await WithDbAsync(db => db.AuditLogEntries.SingleAsync(a => a.EventType == AuditEventTypes.PasswordChanged));
        Assert.Contains("required change", audit.Details);
        Assert.DoesNotContain(NewPassword, audit.Details);
        Assert.DoesNotContain(TemporaryPassword, audit.Details);

        // The session was re-issued: the same client now passes the sample; the cookie from before the change is rejected.
        var freshCsrf = await CsrfAsync(client);
        foreach (var (method, url) in Sample.Where(s => s.Method == HttpMethod.Get))
            Assert.NotEqual(HttpStatusCode.Forbidden, (await SendAsync(client, method, url, freshCsrf)).StatusCode);
        var stale = new HttpRequestMessage(HttpMethod.Get, "/api/auth/permissions");
        stale.Headers.Add("Cookie", oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateDefaultClient().SendAsync(stale)).StatusCode);

        // The new password signs in; the temporary one no longer does.
        await SignInAsync("forced.admin", NewPassword);
        var withOld = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "forced.admin", password = TemporaryPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);

        // No secret in any log line.
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains(NewPassword) || e.Message.Contains(TemporaryPassword));
    }

    /// <summary>Signs in on a client without a cookie jar and returns the auth cookie it was given, so the test can replay it after the stamp rotates.</summary>
    private async Task<string> CookieOfAnotherSessionAsync(string username)
    {
        var login = await _factory.CreateDefaultClient().PostAsJsonAsync("/api/auth/login", new { username, password = TemporaryPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
    }

    [Fact]
    public async Task A_forced_account_can_sign_out()
    {
        await SeedAccountAsync("forced.out", mustChange: true, role: Role.FrontDesk);
        var client = await SignInAsync("forced.out");
        var csrf = await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, "/api/auth/logout", csrf)).StatusCode);   // exempt route 4
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/permissions")).StatusCode);
    }

    [Fact]
    public async Task A_rejected_change_changes_nothing()
    {
        var id = await SeedAccountAsync("forced.bad", mustChange: true, role: Role.FrontDesk);
        var client = await SignInAsync("forced.bad");
        var csrf = await CsrfAsync(client);
        var before = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == id));

        async Task<string> ErrorOf(HttpResponseMessage r) { Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!; }
        Assert.Equal("invalid_current_password", await ErrorOf(await ChangeAsync(client, csrf, "not-the-password", NewPassword)));
        Assert.Equal("invalid_current_password", await ErrorOf(await ChangeAsync(client, csrf, null, NewPassword)));
        Assert.Equal("password_too_short", await ErrorOf(await ChangeAsync(client, csrf, TemporaryPassword, "short-11chr")));
        Assert.Equal("password_unchanged", await ErrorOf(await ChangeAsync(client, csrf, TemporaryPassword, TemporaryPassword)));

        // Without the CSRF header the change is refused before it runs.
        var noCsrf = await client.PostAsJsonAsync("/api/auth/password/change", new ChangePasswordRequest(TemporaryPassword, NewPassword));
        Assert.NotEqual(HttpStatusCode.OK, noCsrf.StatusCode);

        var after = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == id));
        Assert.True(after.MustChangePassword);
        Assert.Equal(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PasswordChanged)));
        // Unauthenticated callers cannot use the route at all.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/auth/password/change", new ChangePasswordRequest("a", "b"))).StatusCode);
    }

    [Fact]
    public async Task An_account_without_the_flag_is_not_gated_and_can_still_change_its_password()
    {
        await SeedAccountAsync("free.admin", mustChange: false);
        var client = await SignInAsync("free.admin");
        var csrf = await CsrfAsync(client);
        Assert.False((await (await client.GetAsync("/api/auth/permissions")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        Assert.NotEqual(HttpStatusCode.Forbidden, (await client.GetAsync("/api/auth/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(client, csrf, TemporaryPassword, NewPassword)).StatusCode);
        var audit = await WithDbAsync(db => db.AuditLogEntries.SingleAsync(a => a.EventType == AuditEventTypes.PasswordChanged));
        Assert.DoesNotContain("required change", audit.Details);
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PasswordChangeRequiredRefusal)));
    }

    [Fact]
    public async Task A_completed_administrator_reset_clears_the_flag_and_an_issued_reset_alone_does_not_set_it()
    {
        var adminId = await SeedAccountAsync("reset.admin", mustChange: false);
        var forcedId = await SeedAccountAsync("lost.temp", mustChange: true, role: Role.FrontDesk);
        var normalId = await SeedAccountAsync("normal.user", mustChange: false, role: Role.FrontDesk);
        var admin = await SignInAsync("reset.admin");
        var csrf = await CsrfAsync(admin);

        foreach (var target in new[] { forcedId, normalId })
        {
            var issued = await SendAsync(admin, HttpMethod.Post, $"/api/auth/{target}/reset-password", csrf);
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
            var token = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
            var complete = await _factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password/complete", new CompleteResetRequest(target, token, NewPassword));
            Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        }
        Assert.False(await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == forcedId).Select(u => u.MustChangePassword).SingleAsync()));
        Assert.False(await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == normalId).Select(u => u.MustChangePassword).SingleAsync()));
        _ = adminId;

        // The recovered account signs in with its own new password and is not gated.
        var recovered = await SignInAsync("lost.temp", NewPassword);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await recovered.GetAsync("/api/patients")).StatusCode);
    }
}
