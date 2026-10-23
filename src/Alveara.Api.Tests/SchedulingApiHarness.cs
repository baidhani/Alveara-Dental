using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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

    public sealed record Session(HttpClient Client, string Csrf, Guid UserId = default)
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

    /// <summary>Set before the first request to see what the API logs (a test that asserts a structured log event); left null, logging is as the host configures it.</summary>
    public Action<Microsoft.Extensions.Logging.ILoggingBuilder>? ConfigureLogging { get; set; }

    private string? _storageRoot;
    private bool _ownsStorageRoot;

    /// <summary>
    /// The API's document storage folder. Left alone, each host gets its own folder under the test data root and removes it when disposed (the shared folder every host once used was never emptied). A test that sets it
    /// (it reads or damages the stored files, or lists the files nothing points to) names a folder it removes itself. Set before the first request.
    /// </summary>
    public string StorageRoot
    {
        get
        {
            if (_storageRoot is null) { _storageRoot = Path.Combine(AlveraTestEnvironment.StorageFolder, $"api-{Guid.NewGuid():N}"); _ownsStorageRoot = true; }
            return _storageRoot;
        }
        set { _storageRoot = value; _ownsStorageRoot = false; }
    }

    public WebApplicationFactory<Program> Factory => _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        if (ConfigureLogging is not null) builder.ConfigureLogging(ConfigureLogging);
        builder.UseSetting("ConnectionStrings:Alveara", fixture.ConnectionString);
        builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        builder.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        builder.UseSetting("StorageRoot", StorageRoot);
        builder.UseSetting("Notifications:ConsumerPollSeconds", "0");                                  // tests drive the consumer themselves
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
            var adminId = (await (await first.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(adminName, "admin-password", IdentityTestHelpers.TestBootstrapSecret))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await first.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminName, "admin-password"));
            _admin = new Session(first, await CsrfAsync(first), adminId);
        }
        if (role == "Admin") return _admin;

        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await _admin.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/role", new ChangeRoleRequest(role));
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/enabled", new SetEnabledRequest(true));
        var client = Factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return new Session(client, await CsrfAsync(client), id);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
        if (_ownsStorageRoot && _storageRoot is not null) TestStorageCleanup.Delete(_storageRoot);
    }
}
