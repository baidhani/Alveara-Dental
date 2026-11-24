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
        var missing = MissingDatabase;
        Assert.Equal(StartupMode.NotInstalled, (await StartupModeProbe.DetectAsync(missing)).Mode);
        Assert.False(await DatabaseExistsAsync(new SqlConnectionStringBuilder(missing).InitialCatalog));   // R02 (CR-2): the probe creates nothing
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

    private async Task<bool> DatabaseExistsAsync(string name)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(_fixture.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_ID(@name)";
        command.Parameters.AddWithValue("@name", name);
        return await command.ExecuteScalarAsync() is not DBNull and not null;
    }

    // ---------- S21 (R02, review CR-2): an unreachable server ends the readiness check within its bound ----------

    /// <summary>
    /// The real timeout and retry path: a local listener accepts every connection and never answers, so each attempt runs to the probe's 15 s timeout.
    /// The probe must make 1 + 3 attempts with its 1/2/4 s backoff, answer NotInstalled, and finish within 4 x 15 s + 7 s (+ 15 s slack); the lower bound
    /// (4 x 15 s) shows the timeouts really ran. It cannot create or migrate anything: nothing on the other end is a database.
    /// </summary>
    [Fact]
    public async Task S21_a_silent_server_runs_every_attempt_to_its_timeout_and_the_check_ends_within_its_bound()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var held = new List<TcpClient>();
        using var stop = new CancellationTokenSource();
        var accepting = Task.Run(async () =>
        {
            try { while (true) { var c = await listener.AcceptTcpClientAsync(stop.Token); lock (held) held.Add(c); } }   // accept, never answer
            catch (OperationCanceledException) { }
        });

        var watch = Stopwatch.StartNew();
        var state = await StartupModeProbe.DetectAsync($"Server=tcp:127.0.0.1,{port};Database=AlveraSilent;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False");
        watch.Stop();
        stop.Cancel();
        await accepting;
        lock (held) { foreach (var c in held) c.Dispose(); }

        var attempts = StartupModeProbe.MaxRetries + 1;
        var upper = TimeSpan.FromSeconds(attempts * StartupModeProbe.Timeout.TotalSeconds + 1 + 2 + 4 + 15);
        var lower = TimeSpan.FromSeconds(attempts * StartupModeProbe.Timeout.TotalSeconds);
        Console.WriteLine($"S21 silent server: {watch.Elapsed.TotalSeconds:F1} s, {held.Count} connections, bound {lower.TotalSeconds}-{upper.TotalSeconds} s");
        Assert.Equal(StartupMode.NotInstalled, state.Mode);
        Assert.True(watch.Elapsed <= upper, $"took {watch.Elapsed}, over the bound {upper}");
        Assert.True(watch.Elapsed >= lower, $"took {watch.Elapsed}: the attempts did not run to their timeout");
        Assert.True(held.Count >= attempts, $"{held.Count} connections; expected at least {attempts} (the first attempt and {StartupModeProbe.MaxRetries} retries)");
    }

    /// <summary>The whole application on a server that refuses connections: the process starts, the readiness check ends within its bound, and the status route
    /// answers "installation incomplete" - it neither hangs nor tries anything else.</summary>
    [Fact]
    public async Task S21_the_application_on_a_refusing_server_starts_not_installed_within_its_bound()
    {
        var refused = FreePort();   // nothing listens here
        var port = FreePort();
        var dll = Path.Combine(RepoRoot, "src", "Alveara.Api.ChainHost", "bin", "Debug", "net10.0", "Alveara.Api.ChainHost.dll");
        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(dll)! };
        psi.ArgumentList.Add(dll);
        psi.Environment["CHAIN_URL"] = $"http://127.0.0.1:{port}";
        psi.Environment["CHAIN_CLOCK_MODE"] = "real";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Test";
        psi.Environment["ConnectionStrings__Alveara"] = $"Server=tcp:127.0.0.1,{refused};Database=AlveraRefused;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
        psi.Environment["ClientBuildPath"] = _clientDir;
        psi.Environment.Remove("ASPNETCORE_URLS");
        var watch = Stopwatch.StartNew();
        var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            var bound = TimeSpan.FromSeconds((StartupModeProbe.MaxRetries + 1) * StartupModeProbe.Timeout.TotalSeconds + 1 + 2 + 4 + 30);   // + process start-up
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            HttpResponseMessage? status = null;
            while (watch.Elapsed < bound && status is null)
            {
                Assert.False(process.HasExited, $"the process exited with {(process.HasExited ? process.ExitCode : 0)}");
                try { status = await client.GetAsync("/api/setup/status"); }
                catch (HttpRequestException) { await Task.Delay(500); }
                catch (TaskCanceledException) { }
            }
            watch.Stop();
            Console.WriteLine($"S21 refusing server: answered after {watch.Elapsed.TotalSeconds:F1} s (bound {bound.TotalSeconds} s)");
            Assert.NotNull(status);
            Assert.Equal((HttpStatusCode.ServiceUnavailable, "{\"mode\":\"installation_incomplete\"}"), (status!.StatusCode, await status.Content.ReadAsStringAsync()));
        }
        finally { Stop(process); }
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
