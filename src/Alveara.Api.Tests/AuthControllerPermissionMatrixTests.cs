using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>API-level proof that [RequirePermission] genuinely gates access (not just that the
/// PermissionMatrix mapping is correct in isolation - see PermissionMatrixTests for that).</summary>
public class AuthControllerPermissionMatrixTests : IAsyncLifetime
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
    public async Task GET_permissions_returns_the_caller_s_own_role_appropriate_permission_set()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        var billingClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "Billing");

        var response = await billingClient.GetAsync("/api/auth/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Billing", body.GetProperty("role").GetString());
        var permissions = body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Contains("ManageBilling", permissions);
        Assert.DoesNotContain("ManageUsers", permissions);
    }

    [Fact]
    public async Task PUT_role_is_allowed_for_a_role_holding_ManageRoles_and_denied_for_one_that_does_not()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory); // Admin holds every permission, including ManageRoles
        var targetUsername = $"user-{Guid.NewGuid():N}";
        var targetRegisterResponse = await adminClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest(targetUsername, "password"));
        var target = await targetRegisterResponse.Content.ReadFromJsonAsync<JsonElement>();
        var targetId = target.GetProperty("id").GetGuid();

        var allowedResponse = await adminClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{targetId}/role", csrf, new ChangeRoleRequest("Assistant")));
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

        // No role in this system holds ManageRoles except Admin, so any non-admin role is denied.
        var frontDeskClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "FrontDesk");
        var frontDeskCsrfResponse = await frontDeskClient.GetAsync("/api/auth/csrf-token");
        var frontDeskCsrf = (await frontDeskCsrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var deniedResponse = await frontDeskClient.SendAsync(WithCsrf(HttpMethod.Put, $"/api/auth/{targetId}/role", frontDeskCsrf, new ChangeRoleRequest("Billing")));

        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        var body = await deniedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ManageRoles", body.GetProperty("required").GetString());
    }

    [Fact]
    public async Task GET_permission_matrix_is_visible_to_a_role_holding_ViewPermissionMatrix_and_denied_to_one_that_does_not()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);

        var officeManagerClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "OfficeManager");
        var allowedResponse = await officeManagerClient.GetAsync("/api/auth/permission-matrix");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var matrix = await allowedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(matrix.GetArrayLength() >= Enum.GetValues<Role>().Length);

        var billingClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "Billing");
        var deniedResponse = await billingClient.GetAsync("/api/auth/permission-matrix");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }
}
