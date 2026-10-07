using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// N002-R01-03 real-process reproduction: the reviewer found that launching the actual API with a
/// missing/unreachable database crashed the whole host (an unhandled exception from the
/// background service's startup recovery query escaped and stopped the process). A
/// WebApplicationFactory-based test runs in-process and does not reproduce this — it needs a real,
/// separate OS process, exactly like the reviewer's reproduction.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class RealProcessDatabaseOutageTests : IAsyncLifetime
{
    private const int Port = 5193;
    private readonly string _databaseName = $"AlveraMissing_Test_{Guid.NewGuid():N}";
    private Process? _process;
    private readonly HttpClient _client = new() { BaseAddress = new Uri($"http://localhost:{Port}") };

    public async Task InitializeAsync()
    {
        var apiDllPath = FindApiDll();
        var connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True";

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{apiDllPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(apiDllPath),
        };
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://localhost:{Port}";
        startInfo.EnvironmentVariables["ConnectionStrings__Alveara"] = connectionString;

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the API process.");

        // Wait for the port to actually accept connections rather than a fixed sleep.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                var stderr = await _process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"API process exited early during startup wait. Stderr: {stderr}");
            }
            try
            {
                using var probe = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                var res = await probe.GetAsync($"http://localhost:{Port}/api/health");
                if (res.IsSuccessStatusCode) break;
            }
            catch
            {
                // Not up yet — keep waiting.
            }
            await Task.Delay(300);
        }
    }

    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        _process?.Dispose();
        _client.Dispose();

        // Clean up the database if the recovery test created it.
        try
        {
            const string masterConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";
            await using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID('{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END";
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    [Fact]
    public async Task Real_process_survives_startup_with_a_missing_database_and_reports_sustained_degraded_status()
    {
        // The process must still be alive at all (this is exactly what the reviewer found broken:
        // an unhandled exception from the background service's startup recovery query used to
        // crash the whole host here).
        Assert.False(_process!.HasExited, "The API process crashed during startup with a missing database — this is the exact N002-R01-03 defect.");

        var first = await _client.GetFromJsonAsync<StatusBody>("/api/systemstatus");
        Assert.NotNull(first);
        Assert.False(first.database.reachable);

        // Sustained degraded service, not a one-off: wait past at least one more poll interval
        // and confirm the process is still alive and still reports the same truthful state.
        await Task.Delay(TimeSpan.FromSeconds(11));
        Assert.False(_process.HasExited, "The API process crashed after the initial failed poll.");

        var second = await _client.GetFromJsonAsync<StatusBody>("/api/systemstatus");
        Assert.NotNull(second);
        Assert.False(second.database.reachable);
    }

    private static string FindApiDll()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "Alveara.Api", "bin", "Debug", "net10.0", "Alveara.Api.dll");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        throw new FileNotFoundException("Could not locate the built Alveara.Api.dll from the test output path.");
    }

    private sealed record StatusBody(bool localServerReachable, DatabaseStatus database);
    private sealed record DatabaseStatus(bool reachable);
}
