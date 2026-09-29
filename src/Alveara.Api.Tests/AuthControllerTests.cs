using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>Each [Fact] gets its own fresh LocalDB database (rather than sharing one via
/// IClassFixture) since several tests here bootstrap the one-time first-admin directly via the
/// HTTP endpoint and assert on its one-time-only behavior.</summary>
public class AuthControllerTests : IAsyncLifetime
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

    /// <summary>Bootstraps an admin, logs in as them, and returns an HttpClient carrying that
    /// session cookie plus the CSRF header value to use on further state-changing requests.</summary>
    private static async Task<(HttpClient Client, string CsrfToken)> CreateLoggedInAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        var bootstrapResponse = await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest(username, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
        bootstrapResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "admin-password"));
        loginResponse.EnsureSuccessStatusCode();

        var csrfResponse = await client.GetAsync("/api/auth/csrf-token");
        var csrf = (await csrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;
        return (client, csrf);
    }

    private static HttpRequestMessage WithCsrf(HttpMethod method, string url, string csrfToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", csrfToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    [Fact]
    public async Task POST_register_creates_a_disabled_unassigned_account_no_caller_selected_role()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(username, "a-strong-password"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(username, body.GetProperty("username").GetString());
        Assert.Equal("Unassigned", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task An_unauthenticated_caller_cannot_select_a_role_at_registration_the_request_shape_has_no_role_field()
    {
        // ALV-001-C01's core fix, proven at the type level: RegisterRequest has no Role property
        // at all anymore, so there is no field a caller could set to "Admin" even by guessing.
        var properties = typeof(RegisterRequest).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Role", properties);
    }

    [Fact]
    public async Task POST_register_with_an_already_taken_username_returns_409()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password-1"));

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password-2"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task POST_bootstrap_admin_with_the_correct_secret_creates_an_enabled_admin()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest($"admin-{Guid.NewGuid():N}", "password", IdentityTestHelpers.TestBootstrapSecret));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task POST_bootstrap_admin_with_the_wrong_secret_returns_401()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest($"admin-{Guid.NewGuid():N}", "password", "wrong-secret"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task POST_bootstrap_admin_a_second_time_returns_409()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest($"admin-{Guid.NewGuid():N}", "password", IdentityTestHelpers.TestBootstrapSecret));

        var response = await client.PostAsJsonAsync("/api/auth/bootstrap-admin",
            new BootstrapAdminRequest($"admin-{Guid.NewGuid():N}", "password", IdentityTestHelpers.TestBootstrapSecret));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task POST_login_with_the_correct_password_returns_200_for_an_enabled_account()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf) = await CreateLoggedInAdminClientAsync(factory);

        var username = $"user-{Guid.NewGuid():N}";
        var registerResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "right-password"));
        var created = await registerResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var userId = created.GetProperty("id").GetGuid();

        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/role", csrf, new ChangeRoleRequest("Assistant")));
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/enabled", csrf, new SetEnabledRequest(true)));

        var freshClient = factory.CreateClient();
        var response = await freshClient.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "right-password"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task POST_login_for_a_still_disabled_self_registered_account_returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "right-password"));

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "right-password"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task POST_login_with_an_unknown_username_and_a_wrong_password_produce_identical_responses_no_enumeration()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf) = await CreateLoggedInAdminClientAsync(factory);
        var username = $"user-{Guid.NewGuid():N}";
        var registerResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "right-password"));
        var created = await registerResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var userId = created.GetProperty("id").GetGuid();
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/role", csrf, new ChangeRoleRequest("FrontDesk")));
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/enabled", csrf, new SetEnabledRequest(true)));

        var client = factory.CreateClient();
        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "wrong-password"));
        var unknownUserResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest($"nobody-{Guid.NewGuid():N}", "irrelevant"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUserResponse.StatusCode);
        var wrongPasswordBody = await wrongPasswordResponse.Content.ReadAsStringAsync();
        var unknownUserBody = await unknownUserResponse.Content.ReadAsStringAsync();
        Assert.Equal(wrongPasswordBody, unknownUserBody); // identical response either way — no username-enumeration signal
    }

    [Fact]
    public async Task POST_login_five_times_wrong_then_locks_the_account_and_returns_423()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf) = await CreateLoggedInAdminClientAsync(factory);
        var username = $"user-{Guid.NewGuid():N}";
        var registerResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "right-password"));
        var created = await registerResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var userId = created.GetProperty("id").GetGuid();
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/role", csrf, new ChangeRoleRequest("OfficeManager")));
        await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{userId}/enabled", csrf, new SetEnabledRequest(true)));

        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "wrong-password"));
        }

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "right-password"));

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task A_state_changing_admin_request_without_a_CSRF_token_is_rejected()
    {
        await using var factory = CreateFactory();
        var (adminClient, _) = await CreateLoggedInAdminClientAsync(factory);

        var response = await adminClient.PutAsJsonAsync($"/api/auth/{Guid.NewGuid()}/enabled", new SetEnabledRequest(true)); // no X-CSRF-Token header

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
