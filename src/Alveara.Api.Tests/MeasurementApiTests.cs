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
