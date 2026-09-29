using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>Each [Fact] gets its own fresh LocalDB database (rather than sharing one via
/// IClassFixture) so that every test bootstraps its own one-time first-admin cleanly.</summary>
public class AuthControllerRbacTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        });

    private static async Task<(HttpClient Client, string CsrfToken, Guid UserId)> CreateLoggedInAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        var bootstrapResponse = await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest(username, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
        var created = await bootstrapResponse.Content.ReadFromJsonAsync<AccountResponse>();

        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "admin-password"));
        var csrfResponse = await client.GetAsync("/api/auth/csrf-token");
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return (client, csrf, created!.Id);
    }

    private static HttpRequestMessage WithCsrf(HttpMethod method, string url, string csrfToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", csrfToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    /// <summary>Registers, role-assigns, and enables a non-admin account via the admin client, then logs in as it on a fresh client.</summary>
    private static async Task<HttpClient> CreateLoggedInUserClientAsync(WebApplicationFactory<Program> factory, HttpClient adminClient, string csrf, string role)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var registerResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"));
        var created = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetGuid();

        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/role", csrf, new ChangeRoleRequest(role)));
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/enabled", csrf, new SetEnabledRequest(true)));

        var userClient = factory.CreateClient();
        await userClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return userClient;
    }

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
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        var userClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "FrontDesk");

        var response = await userClient.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GET_whoami_logged_in_as_admin_returns_200_with_the_caller_identity()
    {
        await using var factory = CreateFactory();
        var (adminClient, _, _) = await CreateLoggedInAdminClientAsync(factory);

        var response = await adminClient.GetAsync("/api/auth/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Admin", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task POST_logout_ends_the_session_so_a_later_whoami_call_returns_401_again()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/auth/whoami")).StatusCode);

        var logoutResponse = await adminClient.SendAsync(WithCsrf(HttpMethod.Post, "/api/auth/logout", csrf));
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var afterLogout = await adminClient.GetAsync("/api/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task A_session_whose_configured_timeout_has_already_elapsed_is_genuinely_rejected()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        var username = $"user-{Guid.NewGuid():N}";
        var registerResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"));
        var created = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetGuid();
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/role", csrf, new ChangeRoleRequest("Admin")));
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/enabled", csrf, new SetEnabledRequest(true)));

        await using (var db = _fixture.CreateContext())
        {
            var account = await db.UserAccounts.SingleAsync(u => u.Id == userId);
            account.SessionTimeoutMinutes = -1; // already expired the instant it's issued
            await db.SaveChangesAsync();
        }

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
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        var userClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "FrontDesk");
        var userCsrfResponse = await userClient.GetAsync("/api/auth/csrf-token");
        var userCsrf = (await userCsrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var response = await userClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{Guid.NewGuid()}/role", userCsrf, new ChangeRoleRequest("Admin")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PUT_role_as_admin_changes_the_target_s_role_and_the_change_is_audited()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, adminId) = await CreateLoggedInAdminClientAsync(factory);

        var targetUsername = $"user-{Guid.NewGuid():N}";
        var targetRegisterResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(targetUsername, "password"));
        var target = await targetRegisterResponse.Content.ReadFromJsonAsync<JsonElement>();
        var targetId = target.GetProperty("id").GetGuid();
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{targetId}/role", csrf, new ChangeRoleRequest("Assistant")));

        var response = await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{targetId}/role", csrf, new ChangeRoleRequest("Hygienist")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal("Hygienist", updated!.Role);
    }

    [Fact]
    public async Task GET_users_as_admin_lists_accounts_and_is_denied_to_non_admins()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);

        var adminListResponse = await adminClient.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);

        var userClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "Billing");
        var userListResponse = await userClient.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Forbidden, userListResponse.StatusCode);
    }

    [Fact]
    public async Task PUT_enabled_as_admin_disables_a_user_who_can_then_no_longer_log_in()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        var username = $"user-{Guid.NewGuid():N}";
        var registerResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"));
        var created = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = created.GetProperty("id").GetGuid();
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/role", csrf, new ChangeRoleRequest("Billing")));
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/enabled", csrf, new SetEnabledRequest(true)));

        var freshClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await freshClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"))).StatusCode);

        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/enabled", csrf, new SetEnabledRequest(false)));

        var afterDisableClient = factory.CreateClient();
        var response = await afterDisableClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task POST_revoke_sessions_invalidates_the_target_s_existing_session_cookie()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, adminId) = await CreateLoggedInAdminClientAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/api/auth/whoami")).StatusCode); // session valid before revoke

        var revokeResponse = await adminClient.SendAsync(WithCsrf(HttpMethod.Post, $"/api/auth/{adminId}/revoke-sessions", csrf));
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var afterRevoke = await adminClient.GetAsync("/api/auth/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode); // the cookie's SecurityStamp no longer matches
    }
}
