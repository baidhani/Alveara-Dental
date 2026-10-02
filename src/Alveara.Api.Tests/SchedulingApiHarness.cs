using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Controllers;

namespace Alveara.Api.Tests;

/// <summary>
/// A real API (real SQL Server, real cookie sessions) with one signed-in session per role, for the HTTP tests of the visit workflow. The older scheduling API
/// tests carry their own private copies of this; new tests use this one so the boilerplate exists once.
/// </summary>
public sealed class SchedulingApiHarness(TestDatabaseFixture fixture) : IDisposable
{
    private WebApplicationFactory<Program>? _factory;
    private Session? _admin;

    public sealed record Session(HttpClient Client, string Csrf)
    {
        public async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string url, object? body = null, string? key = null, bool csrf = true)
        {
            var request = new HttpRequestMessage(method, url);
            if (csrf) request.Headers.Add("X-CSRF-Token", Csrf);
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            if (body is not null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }
    }

    public WebApplicationFactory<Program> Factory => _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("ConnectionStrings:Alveara", fixture.ConnectionString);
        builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        builder.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
    });

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    /// <summary>A signed-in session for the role ("Admin" is the bootstrapped administrator; others are provisioned by it). Each call for a non-admin role makes a new user.</summary>
    public async Task<Session> SessionAsync(string role)
    {
        if (_admin is null)
        {
            var first = Factory.CreateClient();
            var adminName = $"admin-{Guid.NewGuid():N}";
            await first.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(adminName, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
            await first.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminName, "admin-password"));
            _admin = new Session(first, await CsrfAsync(first));
        }
        if (role == "Admin") return _admin;

        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await _admin.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/role", new ChangeRoleRequest(role));
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/enabled", new SetEnabledRequest(true));
        var client = Factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return new Session(client, await CsrfAsync(client));
    }

    public void Dispose() => _factory?.Dispose();
}
