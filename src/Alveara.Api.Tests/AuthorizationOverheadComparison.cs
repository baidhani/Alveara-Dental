using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017 evidence generator (runs only when <c>ALVERA_AB_OUT</c> names a folder): times the same permission-gated routes, with the same method, on whatever build it is compiled into. Run once on the commit
/// before the story and once on the story's commit, on the same machine, so the cost the effective-permission seam adds is measured as a difference between two builds and not against a remembered number. It
/// needs only the harness, so the identical file compiles on both commits.
/// </summary>
public class AuthorizationOverheadComparison : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static double Percentile(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

    private static async Task<List<double>> TimeAsync(Session s, string url, int warmup, int n)
    {
        for (var i = 0; i < warmup; i++) await s.SendAsync(HttpMethod.Get, url);
        var samples = new List<double>(n);
        for (var i = 0; i < n; i++)
        {
            var sw = Stopwatch.StartNew();
            var r = await s.SendAsync(HttpMethod.Get, url);
            sw.Stop();
            Assert.Equal(HttpStatusCode.OK, r.Status);
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        return samples;
    }

    [Fact]
    public async Task Evidence_time_the_same_gated_routes_on_this_build()
    {
        if (Environment.GetEnvironmentVariable("ALVERA_AB_OUT") is not { Length: > 0 } folder) return;
        await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var routes = new[] { "/api/auth/csrf-token", "/api/document-categories", "/api/auth/permission-matrix", "/api/auth/permissions" };   // an ungated route, two gated routes with different handlers, and the route that lists the caller's own permissions
        var result = new Dictionary<string, object>();
        foreach (var url in routes)
        {
            var repeats = new List<object>();
            for (var r = 0; r < 3; r++)
            {
                var s = (await TimeAsync(manager, url, 300, 2000)).OrderBy(x => x).ToList();
                repeats.Add(new { repeat = r + 1, p50 = Math.Round(Percentile(s, 0.5), 3), p95 = Math.Round(Percentile(s, 0.95), 3), p99 = Math.Round(Percentile(s, 0.99), 3) });
            }
            result[url] = repeats;
        }
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "ab.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
