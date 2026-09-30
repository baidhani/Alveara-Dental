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

    // ALV-N009: the shell needs the caller's own username (for the account menu) and the real
    // cookie expiry (for session-expiry UX) alongside role/permissions.
    [Fact]
    public async Task GET_permissions_also_returns_the_caller_s_username_and_a_future_session_expiry()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        var billingClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "Billing");

        var response = await billingClient.GetAsync("/api/auth/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("username").GetString()));
        var sessionExpiresAtUtc = body.GetProperty("sessionExpiresAtUtc").GetDateTimeOffset();
        Assert.True(sessionExpiresAtUtc > DateTimeOffset.UtcNow, "session expiry must be in the future for a freshly logged-in session");
    }

    // ALV-N009 R03 (review finding ALV-N009-R02-01): a background client-side poll observing
    // session state (see AuthContext.tsx's revalidation poll) must never itself keep an otherwise
    // idle session alive - that would defeat the admin-configured SessionTimeoutMinutes. Proven
    // deterministically here by repeating the exact authenticated call the poll makes and asserting
    // the server-reported expiry is byte-identical across calls - with SlidingExpiration left at
    // its ASP.NET Core default (true), the second call's expiry would be later than the first.
    [Fact]
    public async Task GET_permissions_does_not_extend_the_session_expiry_on_repeated_calls_SlidingExpiration_is_off()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, _) = await CreateLoggedInAdminClientAsync(factory);
        var billingClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "Billing");

        var first = await billingClient.GetAsync("/api/auth/permissions");
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstExpiry = firstBody.GetProperty("sessionExpiresAtUtc").GetDateTimeOffset();

        // Several more calls, exactly like AuthContext.tsx's background revalidation poll and
        // focus/visibility handlers would make while the caller is otherwise idle.
        for (var i = 0; i < 3; i++)
        {
            var repeat = await billingClient.GetAsync("/api/auth/permissions");
            var repeatBody = await repeat.Content.ReadFromJsonAsync<JsonElement>();
            var repeatExpiry = repeatBody.GetProperty("sessionExpiresAtUtc").GetDateTimeOffset();
            Assert.Equal(firstExpiry, repeatExpiry);
        }
    }

    // STORY-002 acceptance: "Given a user attempts to [access] an audit log, when they do not have
    // permission, then the action is denied." OfficeManager holds ViewAuditLog (PermissionMatrix.cs);
    // FrontDesk does not, so it is the negative case here.
    [Fact]
    public async Task GET_audit_log_is_allowed_for_a_role_holding_ViewAuditLog_and_denied_for_one_that_does_not()
    {
        await using var factory = CreateFactory();
        var (adminClient, csrf, adminId) = await CreateLoggedInAdminClientAsync(factory);

        var officeManagerClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "OfficeManager");
        var allowedResponse = await officeManagerClient.GetAsync("/api/auth/audit-log");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var entries = await allowedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(entries.GetArrayLength() > 0);
        // The admin's own bootstrap/login already wrote at least one entry naming them.
        Assert.Contains(entries.EnumerateArray(), e =>
            e.TryGetProperty("performedByUserAccountId", out var performedBy) && performedBy.ValueKind != JsonValueKind.Null
            && performedBy.GetGuid() == adminId
            || e.TryGetProperty("targetUserAccountId", out var target) && target.GetGuid() == adminId);

        var frontDeskClient = await CreateLoggedInUserClientAsync(factory, adminClient, csrf, "FrontDesk");
        var deniedResponse = await frontDeskClient.GetAsync("/api/auth/audit-log");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
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
