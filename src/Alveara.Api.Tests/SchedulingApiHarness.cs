using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Alveara.Api.Controllers;

namespace Alveara.Api.Tests;

/// <summary>
/// A real API (real SQL Server, real cookie sessions) with one signed-in session per role, for the HTTP tests of the visit workflow. The older scheduling API
/// tests carry their own private copies of this; new tests use this one so the boilerplate exists once.
/// </summary>
public sealed class SchedulingApiHarness(TestDatabaseFixture fixture) : IDisposable, IAsyncDisposable
{
    /// <summary>How long a host gets to stop its background services when the harness is disposed.</summary>
    internal static readonly TimeSpan HostShutdownLimit = TimeSpan.FromSeconds(30);

    private WebApplicationFactory<Program>? _factory;
    private readonly ConcurrentBag<IHost> _hosts = [];

    /// <summary>Hands every host this harness starts (the first and any derived through WithWebHostBuilder) to the harness, so disposing it can stop them first.</summary>
    private sealed class HostHandle(IHost host, ConcurrentBag<IHost> hosts) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) { hosts.Add(host); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
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

    /// <summary>Set before the first request to replace or add services of the host (a test that needs a slow or recording collaborator); left null, the host's services are as the application registers them.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

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
        builder.ConfigureServices(services => services.AddSingleton<IHostedService>(sp => new HostHandle(sp.GetRequiredService<IHost>(), _hosts)));
        if (ConfigureServices is not null) builder.ConfigureServices(ConfigureServices);
        builder.UseSetting("ConnectionStrings:Alveara", fixture.ConnectionString);
        builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        builder.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        builder.UseSetting("StorageRoot", StorageRoot);
        builder.UseSetting("Notifications:ConsumerPollSeconds", "0");                                  // tests drive the consumer themselves
        builder.UseSetting("FollowUps:ReminderPollSeconds", "0");                                      // and the follow-up reminder pass (STORY-009)
        builder.UseSetting("FollowUps:WorkPollSeconds", "0");                                          // and the recall and treatment passes (ALV-009-C01)
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

    /// <summary>The synchronous form waits for <see cref="DisposeAsync"/>: callers that cannot await still get a harness whose hosts have stopped when it returns.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// ALV-017-C01 R02: stops every host and WAITS for its background services before the host is disposed. Disposing a generic host disposes its services but does not wait for a background service that is still
    /// running, so such a service could still be connected to the test database when the fixture dropped it (the cause suspected of the one cleanup failure of the R01 Stage 2 run 2; the diagnostics added
    /// to TestDatabaseFixture identify any holder that remains). A host that has not stopped within <see cref="HostShutdownLimit"/> is reported and then disposed anyway; any other failure of a host to stop
    /// is thrown after the factory has been disposed, so nothing is left running and the failure is not hidden.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var factory = _factory;
        _factory = null;
        try
        {
            foreach (var host in _hosts)
            {
                using var limit = new CancellationTokenSource(HostShutdownLimit);
                try
                {
                    await host.StopAsync(limit.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine($"[SchedulingApiHarness] a host did not stop its background services within {HostShutdownLimit.TotalSeconds:0} s; disposing it anyway.");
                }
            }

            _hosts.Clear();
        }
        finally
        {
            if (factory is not null) await factory.DisposeAsync();
            if (_ownsStorageRoot && _storageRoot is not null) TestStorageCleanup.Delete(_storageRoot);
        }
    }
}
