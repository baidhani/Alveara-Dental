using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 7 (brief section 16, rows S18 to S20): the start-up mode, what each mode answers, and the controlled restart.
/// <list type="bullet">
/// <item>S18: a missing database is NotInstalled; a pending migration is UpgradeRequired and is still pending afterwards (the API never migrates); a current schema
/// with no Deployment Administrator and no BootstrapState is Setup; a set-up database is Normal. "One migration behind" is made by removing the newest row of
/// __EFMigrationsHistory, which is exactly what the probe reads (no real down-migration is run).</item>
/// <item>S19: outside Normal, none of the application's background services is registered; sampled /api routes answer 503 with only the mode's error; the client's
/// index.html still loads for "/" and a deep link; the status route answers exactly one of four bodies, never cached; in Setup the setup routes work.</item>
/// <item>S20: a real chain-host process answers a successful setup with 201 and then exits with code 75; a failed setup leaves it running; a new process on the same
/// database starts Normal; both entry points keep the shape that carries the exit code.</item>
/// </list>
/// </summary>
public class StartupModeTests : IAsyncLifetime
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _clientDir = Path.Combine(Path.GetTempPath(), "alveara-startup-mode-client-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        Directory.CreateDirectory(_clientDir);
        await File.WriteAllTextAsync(Path.Combine(_clientDir, "index.html"), "<!doctype html><title>DentalKeep</title><div id=\"root\">client-shell</div>");
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        try { Directory.Delete(_clientDir, recursive: true); }
        catch (IOException ex) { Console.WriteLine($"The temporary client folder could not be removed: {ex.Message}"); }
    }

    private string MissingDatabase => new SqlConnectionStringBuilder(_fixture.ConnectionString) { InitialCatalog = "AlveraMissing_" + Guid.NewGuid().ToString("N") }.ConnectionString;

    private async Task<string> RemoveNewestMigrationRowAsync()
    {
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DECLARE @id nvarchar(150) = (SELECT MAX(MigrationId) FROM __EFMigrationsHistory); DELETE FROM __EFMigrationsHistory WHERE MigrationId = @id; SELECT @id;";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    // ---------- S18: the decision ----------

    [Fact]
    public async Task S18_each_condition_decides_its_mode_and_the_probe_never_migrates()
    {
        Assert.Equal(StartupMode.NotInstalled, (await StartupModeProbe.DetectAsync(MissingDatabase)).Mode);
        Assert.Equal(StartupMode.NotInstalled, (await StartupModeProbe.DetectAsync(null)).Mode);

        Assert.Equal(new StartupModeState(StartupMode.Normal, 0), await StartupModeProbe.DetectAsync(_fixture.ConnectionString));   // the test database is set up

        await _fixture.ClearSetupCompletedAsync();
        Assert.Equal(StartupMode.Setup, (await StartupModeProbe.DetectAsync(_fixture.ConnectionString)).Mode);
        await using (var db = _fixture.CreateContext())
        {
            db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), Username = "deploy", PasswordHash = "x", Role = Role.Admin, AccountKind = AccountKind.DeploymentAdministrator, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.Equal(StartupMode.Normal, (await StartupModeProbe.DetectAsync(_fixture.ConnectionString)).Mode);   // a Deployment Administrator alone also means set up

        var removed = await RemoveNewestMigrationRowAsync();
        var upgrade = await StartupModeProbe.DetectAsync(_fixture.ConnectionString);
        Assert.Equal(new StartupModeState(StartupMode.UpgradeRequired, 1), upgrade);
        await using (var db = _fixture.CreateContext())
            Assert.Equal([removed], await db.Database.GetPendingMigrationsAsync());   // still pending: the API never migrates
    }

    // ---------- S19: what each mode answers ----------

    private WebApplicationFactory<Program> Host(string connectionString) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("ConnectionStrings:Alveara", connectionString);
        b.UseSetting("ClientBuildPath", _clientDir);
        b.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        b.ConfigureServices(services => services.AddSingleton<ISetupRestart>(new DeploymentSetupApiTests.RecordingSetupRestart()));
    });

    private static readonly string[] SampleRoutes =
    [
        "/api/patients", "/api/appointments", "/api/config/practice", "/api/config/staff", "/api/auth/permissions", "/api/auth/users",
        "/api/practice-owner", "/api/follow-ups/assignees", "/api/backup/status", "/api/system-status",
    ];

    public static IEnumerable<object[]> NonNormalModes() => [["installation_incomplete"], ["upgrade_required"], ["setup_required"]];

    [Theory]
    [MemberData(nameof(NonNormalModes))]
    public async Task S19_outside_normal_mode_only_the_mode_routes_and_the_client_files_answer(string error)
    {
        var connection = _fixture.ConnectionString;
        if (error == "installation_incomplete") connection = MissingDatabase;
        else if (error == "upgrade_required") await RemoveNewestMigrationRowAsync();
        else await _fixture.ClearSetupCompletedAsync();

        await using var host = Host(connection);
        var client = host.CreateClient();

        // no application background service is registered at all
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), h => h.GetType().Assembly == typeof(Program).Assembly);

        foreach (var route in SampleRoutes)
        {
            var refused = await client.GetAsync(route);
            Assert.True(refused.StatusCode == HttpStatusCode.ServiceUnavailable, $"{route} answered {(int)refused.StatusCode}");
            Assert.Equal($"{{\"error\":\"{error}\"}}", await refused.Content.ReadAsStringAsync());
        }
        foreach (var page in new[] { "/", "/patients/abc" })
        {
            var html = await client.GetAsync(page);
            Assert.Equal(HttpStatusCode.OK, html.StatusCode);
            Assert.Contains("client-shell", await html.Content.ReadAsStringAsync());
        }

        var status = await client.GetAsync("/api/setup/status");
        Assert.Equal("no-store", status.Headers.CacheControl?.ToString());
        var expected = error switch
        {
            "installation_incomplete" => (HttpStatusCode.ServiceUnavailable, "{\"mode\":\"installation_incomplete\"}"),
            "upgrade_required" => (HttpStatusCode.ServiceUnavailable, "{\"mode\":\"upgrade_required\"}"),
            _ => (HttpStatusCode.OK, "{\"mode\":\"setup\",\"setupAvailable\":true}"),
        };
        Assert.Equal(expected, (status.StatusCode, await status.Content.ReadAsStringAsync()));

        // Setup mode: the CSRF token and the setup route itself answer (a wrong token gets the ordinary refusal, not 503)
        var csrf = await client.GetAsync("/api/auth/csrf-token");
        var attempt = await client.PostAsJsonAsync("/api/setup/deployment-administrator", new { token = "wrong", username = "x", password = "y" });
        if (error == "setup_required")
        {
            Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, csrf.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, attempt.StatusCode);
        }
    }

    [Fact]
    public async Task S19_normal_mode_is_unchanged_with_its_background_services_and_the_normal_status()
    {
        await using var host = Host(_fixture.ConnectionString);
        var client = host.CreateClient();
        Assert.Contains(host.Services.GetServices<IHostedService>(), h => h.GetType().Assembly == typeof(Program).Assembly);
        var status = await client.GetAsync("/api/setup/status");
        Assert.Equal((HttpStatusCode.OK, "{\"mode\":\"normal\",\"setupAvailable\":false}"), (status.StatusCode, await status.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/patients")).StatusCode);   // ordinary behaviour: sign-in required, not 503
    }

    // ---------- S20: the controlled restart, in a real process ----------

    [Fact]
    public void S20_both_entry_points_keep_the_shape_that_carries_the_exit_code()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "src", "Alveara.Api", "Program.cs"));
        Assert.Matches(@"\napp\.Run\(\);", program);                                           // ends by running the app; no int-returning Main
        Assert.DoesNotMatch(@"\breturn\s+\d+\s*;", program);
        var chain = File.ReadAllText(Path.Combine(RepoRoot, "src", "Alveara.Api.ChainHost", "ChainHostMain.cs"));
        Assert.Contains("return Environment.ExitCode;", chain);                                 // passes the application's code on
        Assert.Contains("ApplicationStopping.Register", chain);
        Assert.Equal(75, StartupModeState.RestartRequestedExitCode);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private Process StartChainHost(int port)
    {
        var dll = Path.Combine(RepoRoot, "src", "Alveara.Api.ChainHost", "bin", "Debug", "net10.0", "Alveara.Api.ChainHost.dll");
        Assert.True(File.Exists(dll), $"build the chain host first: {dll}");
        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(dll)! };
        psi.ArgumentList.Add(dll);
        psi.Environment["CHAIN_URL"] = $"http://127.0.0.1:{port}";
        psi.Environment["CHAIN_CLOCK_MODE"] = "real";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Test";
        psi.Environment["ConnectionStrings__Alveara"] = _fixture.ConnectionString;
        psi.Environment["ClientBuildPath"] = _clientDir;
        psi.Environment.Remove("ASPNETCORE_URLS");
        var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<HttpClient> WaitHealthyAsync(int port, Process process)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < 60; i++)
        {
            Assert.False(process.HasExited, $"the chain host exited at start-up with code {(process.HasExited ? process.ExitCode : 0)}");
            try { if ((await client.GetAsync("/api/setup/status")).StatusCode is HttpStatusCode.OK) return client; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(1000);
        }
        Assert.Fail("the chain host did not answer in time");
        return client;
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        process.WaitForExit(15000);
        process.Dispose();
    }

    [Fact]
    public async Task S20_a_successful_setup_answers_201_then_the_process_exits_75_and_a_new_process_starts_normal()
    {
        await _fixture.ClearSetupCompletedAsync();
        string token;
        await using (var db = _fixture.CreateContext())
            token = (await new DeploymentSetupService(db, new TestClock(DateTimeOffset.UtcNow)).IssueTokenAsync(default)).Token;

        var port = FreePort();
        var first = StartChainHost(port);
        try
        {
            var client = await WaitHealthyAsync(port, first);
            Assert.Equal("{\"mode\":\"setup\",\"setupAvailable\":true}", await (await client.GetAsync("/api/setup/status")).Content.ReadAsStringAsync());

            var failed = await client.PostAsJsonAsync("/api/setup/deployment-administrator", new { token = "wrong", username = "deploy", password = "a-long-enough-password" });
            Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.False(first.HasExited, "a failed setup must not stop the process");

            var done = await client.PostAsJsonAsync("/api/setup/deployment-administrator", new { token, username = "deploy", password = "a-long-enough-password" });
            Assert.Equal(HttpStatusCode.Created, done.StatusCode);                              // the client gets its answer first
            Assert.True(first.WaitForExit(30_000), "the process did not exit after setup");
            Assert.Equal(StartupModeState.RestartRequestedExitCode, first.ExitCode);
        }
        finally { Stop(first); }

        var second = StartChainHost(port);
        try
        {
            var client = await WaitHealthyAsync(port, second);
            Assert.Equal("{\"mode\":\"normal\",\"setupAvailable\":false}", await (await client.GetAsync("/api/setup/status")).Content.ReadAsStringAsync());
        }
        finally { Stop(second); }
    }
}
