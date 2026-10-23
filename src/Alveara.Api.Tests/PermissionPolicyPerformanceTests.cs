using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017 database-read behaviour and the predeclared performance budget (docs/design/ADMIN_GRANTABLE_PERMISSIONS_PROPOSAL.md section 3.7). The read counts are checked on every run by counting the commands the real
/// pipeline sends to SQL Server: a warm authorization sends NO policy command of its own (the version rides on the security-stamp query the cookie check already makes for every request), a reload sends the
/// closing version read, the overrides and the delegations (the version that showed the change opened the bracket), and a burst of 50 requests on a changed version reloads once. The timings are an evidence generator: they run, and judge their thresholds, only when <c>ALVERA_PERF_EVIDENCE</c> names an output folder
/// (a quiet machine is required; the worst of three repeats is judged), so a busy full run cannot fail on machine noise and the Stage 2 contract runs them explicitly.
/// </summary>
public class PermissionPolicyPerformanceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly CapturingLoggerProvider _logs = new();
    private SchedulingApiHarness _api = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture)
        {
            ConfigureLogging = b =>
            {
                b.AddProvider(_logs);
                b.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
            },
        };
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private int Commands(string table, string? except = null) =>
        _logs.Entries.Count(e => e.Category.Contains("Database.Command") && e.Message.Contains($"[{table}]") && (except is null || !e.Message.Contains($"[{except}]")));

    /// <summary>Commands that read the policy version on their own (the version otherwise rides on the cookie check query that already runs for every authenticated request).</summary>
    private int VersionCommands() => Commands("PermissionPolicyState", except: "UserAccounts");

    private async Task BumpAsync()
    {
        await using var db = _fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
    }

    // ---------- read behaviour (every run) ----------

    [Fact]
    public async Task A_warm_authorization_sends_no_policy_command_of_its_own_and_never_reads_the_rows()
    {
        var manager = await _api.SessionAsync("OfficeManager");
        await manager.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix");                                           // loads the first snapshot
        for (var i = 0; i < 5; i++)
        {
            _logs.Entries.Clear();
            var response = await manager.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix");
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal(1, Commands("PermissionPolicyState"));                                                          // the version, once, inside the security-stamp query
            Assert.Equal(0, VersionCommands());                                                                          // so no round trip of its own, however many times the request asks the seam
            Assert.Equal(0, Commands("RolePermissionOverrides"));
            Assert.Equal(0, Commands("PermissionDelegations"));
        }
    }

    [Fact]
    public async Task A_reload_sends_the_bracketed_reads_and_nothing_else()
    {
        var manager = await _api.SessionAsync("OfficeManager");
        await manager.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix");
        await BumpAsync();
        _logs.Entries.Clear();
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix")).Status);
        Assert.Equal(1, VersionCommands());                                                                              // the version that showed the change (read with the stamp) opens the bracket; one closing read follows the rows
        Assert.Equal(1, Commands("RolePermissionOverrides"));
        Assert.Equal(1, Commands("PermissionDelegations"));
    }

    [Fact]
    public async Task A_burst_of_fifty_concurrent_requests_on_a_changed_version_reloads_the_policy_once()
    {
        var manager = await _api.SessionAsync("OfficeManager");
        await manager.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix");
        await BumpAsync();
        _logs.Entries.Clear();
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => manager.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(1, Commands("RolePermissionOverrides"));
        Assert.Equal(1, Commands("PermissionDelegations"));
    }

    [Fact]
    public async Task The_administrator_is_decided_from_the_code_rules_and_costs_no_policy_command()
    {
        var admin = await _api.SessionAsync("Admin");
        await admin.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix");                                             // the matrix endpoint itself asks for every role, which needs the policy: this loads the first snapshot
        _logs.Entries.Clear();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Get, "/api/auth/permission-matrix")).Status);
        Assert.Equal(0, VersionCommands());                                                                              // warm: no policy command of its own
        _logs.Entries.Clear();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Get, "/api/backup/history")).Status);
        Assert.Equal(0, VersionCommands());                                                                              // a permission-gated route for the administrator reads no policy at all
    }

    // ---------- timings (evidence generator) ----------

    private static string? EvidenceFolder => Environment.GetEnvironmentVariable("ALVERA_PERF_EVIDENCE") is { Length: > 0 } f ? f : null;

    private static double Percentile(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

    private static object Stats(List<double> samples)
    {
        var s = samples.OrderBy(x => x).ToList();
        return new { n = s.Count, p50 = Math.Round(Percentile(s, 0.50), 3), p95 = Math.Round(Percentile(s, 0.95), 3), p99 = Math.Round(Percentile(s, 0.99), 3) };
    }

    private static async Task<List<double>> TimeRequestsAsync(Session s, string url, int warmup, int n)
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

    /// <summary>Baseline measured on 2026-10-22 against the code before this story (same harness, n = 2000 after 300 warm-up): permission-gated GET /api/auth/permission-matrix p95 1.20 ms.</summary>
    private const double BaselineGatedP95Ms = 1.20;

    [Fact]
    public async Task Evidence_the_warm_path_adds_at_most_one_millisecond_at_p95_and_one_database_round_trip()
    {
        if (EvidenceFolder is not { } folder) return;
        var manager = await _api.SessionAsync("OfficeManager");
        var repeats = new List<object>();
        var worstGated = 0.0;
        for (var r = 0; r < 3; r++)
        {
            var gated = await TimeRequestsAsync(manager, "/api/auth/permission-matrix", 300, 2000);
            var control = await TimeRequestsAsync(manager, "/api/auth/csrf-token", 300, 2000);
            var gatedP95 = Percentile(gated.OrderBy(x => x).ToList(), 0.95);
            worstGated = Math.Max(worstGated, gatedP95);
            repeats.Add(new { repeat = r + 1, gated = Stats(gated), control = Stats(control), addedP95VsBaselineMs = Math.Round(gatedP95 - BaselineGatedP95Ms, 3) });
        }
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "warm-path.json"), JsonSerializer.Serialize(new { baselineGatedP95Ms = BaselineGatedP95Ms, thresholdAddedP95Ms = 1.0, worstGatedP95Ms = Math.Round(worstGated, 3), repeats }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(worstGated - BaselineGatedP95Ms <= 1.0, $"worst gated p95 {worstGated:F3} ms vs baseline {BaselineGatedP95Ms} ms: added more than 1.0 ms");
    }

    private async Task SeedOverridesAsync(int valid, int invalid)
    {
        await using var db = _fixture.CreateContext();
        var by = await db.UserAccounts.Select(u => u.Id).FirstAsync();
        var grantable = Enum.GetValues<Permission>().Where(PermissionProtection.IsGrantable).ToList();
        var roles = new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.FrontDesk, Role.Billing, Role.OfficeManager };
        var made = 0;
        foreach (var role in roles)
            foreach (var p in grantable)
            {
                if (made == valid) break;
                db.RolePermissionOverrides.Add(new RolePermissionOverride { Id = Guid.NewGuid(), Role = role.ToString(), Permission = p.ToString(), Effect = PermissionMatrix.RoleHas(role, p) ? OverrideEffects.Revoke : OverrideEffects.Grant, Reason = "performance dataset", ChangedByUserId = by, ChangedAtUtc = DateTimeOffset.UtcNow });
                made++;
            }
        for (var i = 0; i < invalid; i++)
            db.RolePermissionOverrides.Add(new RolePermissionOverride { Id = Guid.NewGuid(), Role = "Dentist", Permission = $"Stress{i:D4}", Effect = OverrideEffects.Revoke, Reason = "performance dataset", ChangedByUserId = by, ChangedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task<(object Stats, double P95)> TimeReloadsAsync(int n)
    {
        var store = _api.Factory.Services.GetRequiredService<PermissionPolicyStore>();
        await store.GetAsync(default);
        var samples = new List<double>(n);
        for (var i = 0; i < n; i++)
        {
            await BumpAsync();                                                                                           // not timed
            var sw = Stopwatch.StartNew();
            var read = await store.GetAsync(default);
            sw.Stop();
            Assert.False(read.Stale);
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        return (Stats(samples), Percentile(samples.OrderBy(x => x).ToList(), 0.95));
    }

    [Fact]
    public async Task Evidence_a_reload_is_at_most_25_ms_at_p95_for_the_standard_dataset_and_50_ms_with_500_overrides_and_a_write_at_most_100_ms()
    {
        if (EvidenceFolder is not { } folder) return;
        await _api.SessionAsync("Admin");
        await SeedOverridesAsync(valid: 60, invalid: 0);
        var standard = new List<(object, double)>();
        for (var r = 0; r < 3; r++) standard.Add(await TimeReloadsAsync(200));

        await SeedOverridesAsync(valid: 0, invalid: 440);                                                                // 60 + 440 = 500 stored rows, 440 of them invalid (the cost of validating a bad row is part of the budget)
        var stress = new List<(object, double)>();
        for (var r = 0; r < 3; r++) stress.Add(await TimeReloadsAsync(200));

        // the write: removing an invalid row is the one policy write in this story (version bump, delete, history and audit in one transaction)
        var writes = new List<double>();
        await using (var scope = _api.Factory.Services.CreateAsyncScope())
        {
            var admin = await _fixture.CreateContext().UserAccounts.Select(u => u.Id).FirstAsync();
            for (var i = 0; i < 100; i++)
            {
                Guid id; string version;
                await using (var db = _fixture.CreateContext())
                {
                    var row = await db.RolePermissionOverrides.Where(o => o.Permission.StartsWith("Stress")).OrderBy(o => o.Permission).FirstAsync();
                    id = row.Id; version = Convert.ToBase64String(row.RowVersion!);
                }
                await using var inner = _api.Factory.Services.CreateAsyncScope();
                var service = inner.ServiceProvider.GetRequiredService<PolicyAdminService>();
                var sw = Stopwatch.StartNew();
                await service.RemoveRowAsync(admin, id, "performance measurement", version, default);
                sw.Stop();
                writes.Add(sw.Elapsed.TotalMilliseconds);
            }
        }
        var writeP95 = Percentile(writes.OrderBy(x => x).ToList(), 0.95);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "reload-and-write.json"), JsonSerializer.Serialize(new
        {
            thresholds = new { reloadStandardP95Ms = 25, reloadStressP95Ms = 50, writeP95Ms = 100 },
            standardDataset = new { roles = Enum.GetValues<Role>().Length, permissions = Enum.GetValues<Permission>().Length, overrides = 60, delegations = 5, repeats = standard.Select(s => s.Item1).ToArray(), worstP95Ms = Math.Round(standard.Max(s => s.Item2), 3) },
            stressDataset = new { overrides = 500, invalid = 440, repeats = stress.Select(s => s.Item1).ToArray(), worstP95Ms = Math.Round(stress.Max(s => s.Item2), 3) },
            write = Stats(writes),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(standard.Max(s => s.Item2) <= 25, $"standard reload worst p95 {standard.Max(s => s.Item2):F2} ms");
        Assert.True(stress.Max(s => s.Item2) <= 50, $"stress reload worst p95 {stress.Max(s => s.Item2):F2} ms");
        Assert.True(writeP95 <= 100, $"write p95 {writeP95:F2} ms");
    }
}
