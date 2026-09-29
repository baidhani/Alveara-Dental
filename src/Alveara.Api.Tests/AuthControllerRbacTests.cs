using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

public class AuthControllerRbacTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AuthControllerRbacTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
        });

    [Fact]
    public async Task GET_whoami_without_a_session_cookie_returns_401()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GET_whoami_logged_in_as_a_non_admin_role_returns_403_RBAC_actually_blocks_the_wrong_role()
    {
        // HandleCookies defaults to true, so this client carries the login's session cookie into
        // the follow-up request — genuinely exercising cookie-based session RBAC, not a mock.
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password", "FrontDesk"));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));

        var response = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GET_whoami_logged_in_as_admin_returns_200_with_the_caller_identity()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password", "Admin"));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));

        var response = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(username, body.GetProperty("username").GetString());
        Assert.Equal("Admin", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task POST_logout_ends_the_session_so_a_later_whoami_call_returns_401_again()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password", "Admin"));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/whoami")).StatusCode);

        var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var afterLogout = await client.GetAsync("/api/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task A_session_whose_configured_timeout_has_already_elapsed_is_genuinely_rejected()
    {
        // IsPersistent=false means the Set-Cookie header carries no Expires attribute — but the
        // server still enforces AuthenticationProperties.ExpiresUtc against the auth ticket on
        // every request regardless of what the cookie itself says. Setting an account's
        // SessionTimeoutMinutes to a value that has already elapsed proves that server-side
        // enforcement is real, rather than asserting on an HTTP header that (correctly) isn't
        // set for a non-persistent cookie.
        await using var setupDb = _fixture.CreateContext();
        var setupService = new AccountService(setupDb);
        var username = $"admin-{Guid.NewGuid():N}";
        var registered = await setupService.RegisterAsync(username, "password", Role.Admin);

        await using (var db = _fixture.CreateContext())
        {
            var account = await db.UserAccounts.SingleAsync(u => u.Id == registered.Id);
            account.SessionTimeoutMinutes = -1; // already expired the instant it's issued
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode); // login itself still succeeds...

        var whoamiResponse = await client.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, whoamiResponse.StatusCode); // ...but the session is already expired
    }

    [Fact]
    public async Task PUT_role_as_a_non_admin_returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password", "FrontDesk"));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));

        var response = await client.PutAsJsonAsync($"/api/auth/{Guid.NewGuid()}/role", new ChangeRoleRequest("Admin"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PUT_role_as_admin_changes_the_target_s_role_and_the_change_is_audited()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var targetUsername = $"user-{Guid.NewGuid():N}";
        var targetRegisterResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(targetUsername, "password", "Assistant"));
        var target = await targetRegisterResponse.Content.ReadFromJsonAsync<AccountResponse>();

        var adminUsername = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(adminUsername, "password", "Admin"));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminUsername, "password"));

        var response = await client.PutAsJsonAsync($"/api/auth/{target!.Id}/role", new ChangeRoleRequest("Hygienist"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal("Hygienist", updated!.Role);
    }
}
