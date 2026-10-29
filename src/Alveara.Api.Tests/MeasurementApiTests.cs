using System.Net;
using System.Reflection;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Identity;
using static Alveara.Api.Tests.SchedulingApiHarness;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 5a over the real API: the measurement route is refused to an anonymous caller (401) and to every role that does not hold ViewMeasurementReports (403), and answered for the administrator and the office
/// manager; the answer is <c>no-store</c> and carries the metrics, the things that cannot be measured with their reasons and the sentence that a difference is not a cause; a provider or location filter gets
/// <c>filter_not_supported</c>; and the permission is registered, grantable and separate from the other report permissions.
/// </summary>
public class MeasurementApiTests : IAsyncLifetime
{
    private const string Range = "from=2030-01-14&to=2030-01-16";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private SchedulingTestSupport _s = null!;
    private readonly Dictionary<string, Session> _sessions = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture);
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        foreach (var role in new[] { "Admin", "OfficeManager", "FrontDesk", "Billing", "Dentist", "Hygienist", "Assistant" }) _sessions[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    [Fact]
    public async Task The_route_is_refused_to_anonymous_callers_and_to_roles_without_the_permission()
    {
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/reports/measurement/metrics?{Range}")).StatusCode);
        foreach (var role in new[] { "FrontDesk", "Billing", "Dentist", "Hygienist", "Assistant" })
            Assert.Equal(HttpStatusCode.Forbidden, (await _sessions[role].Client.GetAsync($"/api/reports/measurement/metrics?{Range}")).StatusCode);
        foreach (var role in new[] { "Admin", "OfficeManager" })
            Assert.Equal(HttpStatusCode.OK, (await _sessions[role].Client.GetAsync($"/api/reports/measurement/metrics?{Range}")).StatusCode);
    }

    [Fact]
    public async Task The_answer_is_no_store_and_carries_the_metrics_the_unmeasurable_things_and_the_no_causation_note()
    {
        var response = await _sessions["OfficeManager"].Client.GetAsync($"/api/reports/measurement/metrics?{Range}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var data = body.GetProperty("data");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("measurement", body.GetProperty("reportId").GetString());
        Assert.Equal(7, data.GetProperty("metrics").GetArrayLength());
        Assert.All(data.GetProperty("metrics").EnumerateArray(), metric => Assert.Equal(1, metric.GetProperty("definitionVersion").GetInt32()));
        Assert.Equal(3, data.GetProperty("notMeasurable").GetArrayLength());
        Assert.Contains("does not show what caused", data.GetProperty("note").GetString());
        var firstRate = data.GetProperty("metrics")[0].GetProperty("rate");
        Assert.Equal(JsonValueKind.Null, firstRate.ValueKind);        // nothing in this range: the rate is unavailable, not zero
    }

    [Fact]
    public async Task A_filter_the_report_cannot_apply_is_refused_and_a_bad_request_has_a_stable_code()
    {
        var client = _sessions["Admin"].Client;
        async Task<(HttpStatusCode Status, string? Error)> CallAsync(string query)
        {
            var response = await client.GetAsync($"/api/reports/measurement/metrics?{query}");
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length > 0 && JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null);
        }

        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"{Range}&providerIds={_s.ProviderA}"));
        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"{Range}&locationId={_s.LocationId}"));
        Assert.Equal((HttpStatusCode.BadRequest, "dates_required"), await CallAsync(""));
        Assert.Equal((HttpStatusCode.BadRequest, "range_too_large"), await CallAsync("from=2028-01-01&to=2029-01-01"));
        Assert.Equal((HttpStatusCode.OK, null), await CallAsync($"{Range}&asOf=2030-02-01T00:00:00Z"));
    }

    [Fact]
    public async Task Setting_a_baseline_needs_its_own_permission_a_key_and_a_token_and_a_repeat_returns_the_same_baseline()
    {
        const string url = "/api/reports/measurement/baselines";
        var body = new { name = "March 2025", from = "2025-03-01", to = "2025-03-31" };
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        foreach (var role in new[] { "FrontDesk", "Billing", "Dentist", "Hygienist", "Assistant" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await _sessions[role].SendAsync(HttpMethod.Post, url, body, "k-" + role)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await _sessions[role].Client.GetAsync(url)).StatusCode);
        }

        var manager = _sessions["OfficeManager"];
        Assert.Equal("idempotency_key_required", (await manager.SendAsync(HttpMethod.Post, url, body)).Body.GetProperty("error").GetString());
        Assert.NotEqual(HttpStatusCode.Created, (await manager.SendAsync(HttpMethod.Post, url, body, "no-token", csrf: false)).Status);
        Assert.Equal("period_not_complete", (await manager.SendAsync(HttpMethod.Post, url, new { name = "Future", from = "2099-01-01", to = "2099-01-31" }, "future")).Body.GetProperty("error").GetString());
        Assert.Equal("name_required", (await manager.SendAsync(HttpMethod.Post, url, new { name = " ", from = "2025-03-01", to = "2025-03-31" }, "blank")).Body.GetProperty("error").GetString());

        var created = await manager.SendAsync(HttpMethod.Post, url, body, "api-key-1");
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var id = created.Body.GetProperty("id").GetGuid();
        Assert.Equal(7, created.Body.GetProperty("metrics").GetArrayLength());
        var repeat = await manager.SendAsync(HttpMethod.Post, url, body, "api-key-1");
        Assert.Equal((HttpStatusCode.OK, id), (repeat.Status, repeat.Body.GetProperty("id").GetGuid()));
        var reused = await manager.SendAsync(HttpMethod.Post, url, new { name = "March 2025", from = "2025-03-01", to = "2025-03-30" }, "api-key-1");
        Assert.Equal((HttpStatusCode.Conflict, "key_reused"), (reused.Status, reused.Body.GetProperty("error").GetString()));

        var list = await manager.SendAsync(HttpMethod.Get, url, csrf: false);
        Assert.Equal(1, list.Body.GetArrayLength());
        Assert.Equal(id, (await manager.SendAsync(HttpMethod.Get, $"{url}/{id}", csrf: false)).Body.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.SendAsync(HttpMethod.Get, $"{url}/{Guid.NewGuid()}", csrf: false)).Status);
    }

    [Fact]
    public async Task The_comparison_names_its_baseline_and_refuses_an_unknown_one_and_a_provider_filter()
    {
        var manager = _sessions["OfficeManager"];
        Assert.Equal(HttpStatusCode.Created, (await manager.SendAsync(HttpMethod.Post, "/api/reports/measurement/baselines", new { name = "March 2025", from = "2025-03-01", to = "2025-03-31" }, "cmp-1")).Status);

        var result = await manager.SendAsync(HttpMethod.Get, "/api/reports/measurement/comparison?from=2025-04-01&to=2025-04-30", csrf: false);
        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("measurement-comparison", result.Body.GetProperty("reportId").GetString());
        Assert.Equal("March 2025", result.Body.GetProperty("data").GetProperty("baseline").GetProperty("name").GetString());
        Assert.Equal(30, result.Body.GetProperty("data").GetProperty("minimumSample").GetInt32());
        Assert.All(result.Body.GetProperty("data").GetProperty("comparisons").EnumerateArray(), c =>
        {
            Assert.NotEqual("comparable", c.GetProperty("status").GetString());        // nothing in this practice reaches a sample of 30
            Assert.Equal(JsonValueKind.Null, c.GetProperty("movement").ValueKind);
            Assert.Equal(JsonValueKind.Null, c.GetProperty("relativeChange").ValueKind);
        });
        Assert.Equal("baseline_not_found", (await manager.SendAsync(HttpMethod.Get, $"/api/reports/measurement/comparison?from=2025-04-01&to=2025-04-30&baselineId={Guid.NewGuid()}", csrf: false)).Body.GetProperty("error").GetString());
        Assert.Equal("filter_not_supported", (await manager.SendAsync(HttpMethod.Get, $"/api/reports/measurement/comparison?from=2025-04-01&to=2025-04-30&locationId={_s.LocationId}", csrf: false)).Body.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _sessions["Dentist"].Client.GetAsync("/api/reports/measurement/comparison?from=2025-04-01&to=2025-04-30")).StatusCode);
    }

    [Fact]
    public void Setting_a_baseline_is_held_by_the_administrator_and_the_office_manager_and_can_be_granted()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
            Assert.Equal(role is Role.Admin or Role.OfficeManager, PermissionMatrix.RoleHas(role, Permission.ManageMeasurementBaselines));
        Assert.True(PermissionProtection.IsGrantable(Permission.ManageMeasurementBaselines));
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ManageMeasurementBaselines)));
        Assert.NotEqual(Permission.ViewMeasurementReports, Permission.ManageMeasurementBaselines);
    }

    [Fact]
    public void The_permission_is_held_by_the_administrator_and_the_office_manager_and_can_be_granted()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
            Assert.Equal(role is Role.Admin or Role.OfficeManager, PermissionMatrix.RoleHas(role, Permission.ViewMeasurementReports));
        Assert.DoesNotContain(Permission.ViewMeasurementReports, PermissionMatrix.PermissionsFor(Role.Unassigned));
        Assert.True(PermissionProtection.IsGrantable(Permission.ViewMeasurementReports));
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ViewMeasurementReports)));
        Assert.Equal(4, new[] { Permission.ViewOperationalReports, Permission.ViewFinancialReports, Permission.ViewClinicalReports, Permission.ViewMeasurementReports }.Distinct().Count());
        var all = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Single(all, AuditEventTypes.ReportViewed);
    }
}
