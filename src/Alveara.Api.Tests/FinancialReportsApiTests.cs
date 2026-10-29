using System.Net;
using System.Reflection;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Identity;
using static Alveara.Api.Tests.SchedulingApiHarness;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 2a over the real API: the four financial routes are refused to an anonymous caller (401) and to every role that does not hold ViewFinancialReports (403), and answered for the administrator, the office
/// manager and billing; the operational and the financial permissions are separate (billing cannot read the schedule reports, the front desk cannot read the financial ones); a report is <c>no-store</c> with its envelope; a
/// filter the report cannot honestly apply gets <c>filter_not_supported</c>; and the permission is registered, grantable and its audit event is not shared.
/// </summary>
public class FinancialReportsApiTests : IAsyncLifetime
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
        foreach (var role in new[] { "Admin", "OfficeManager", "Billing", "FrontDesk", "Dentist", "Hygienist", "Assistant" }) _sessions[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string[] Urls => [$"/api/reports/production?{Range}", $"/api/reports/collections?{Range}", $"/api/reports/production/entries?{Range}", $"/api/reports/collections/entries?{Range}"];

    [Fact]
    public async Task Every_route_is_refused_to_anonymous_callers_and_to_roles_without_the_permission()
    {
        using var anonymous = _api.Factory.CreateClient();
        foreach (var url in Urls) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        foreach (var role in new[] { "FrontDesk", "Dentist", "Hygienist", "Assistant" })
            foreach (var url in Urls) Assert.Equal(HttpStatusCode.Forbidden, (await _sessions[role].Client.GetAsync(url)).StatusCode);
        foreach (var role in new[] { "Admin", "OfficeManager", "Billing" })
            foreach (var url in Urls) Assert.Equal(HttpStatusCode.OK, (await _sessions[role].Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task The_operational_and_the_financial_permissions_are_separate()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _sessions["Billing"].Client.GetAsync($"/api/reports/schedule?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sessions["FrontDesk"].Client.GetAsync($"/api/reports/production?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _sessions["FrontDesk"].Client.GetAsync($"/api/reports/schedule?{Range}")).StatusCode);
    }

    [Fact]
    public async Task A_financial_report_is_no_store_carries_its_envelope_and_says_when_it_has_no_data()
    {
        var response = await _sessions["Billing"].Client.GetAsync($"/api/reports/production?{Range}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("production", body.GetProperty("reportId").GetString());
        Assert.Equal(1, body.GetProperty("definitionVersion").GetInt32());
        Assert.True(body.GetProperty("noData").GetBoolean());
        Assert.True(body.GetProperty("data").GetProperty("ledger").GetProperty("reconciled").GetBoolean());
        Assert.Equal(10, body.GetProperty("data").GetProperty("ledger").GetProperty("lines").GetArrayLength());

        var collections = JsonDocument.Parse(await (await _sessions["OfficeManager"].Client.GetAsync($"/api/reports/collections?{Range}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("collections", collections.GetProperty("reportId").GetString());
        Assert.Equal(0, collections.GetProperty("data").GetProperty("netCollections").GetDecimal());
    }

    [Fact]
    public async Task A_filter_the_report_cannot_honestly_apply_is_refused_and_other_errors_have_stable_codes()
    {
        var client = _sessions["Admin"].Client;
        async Task<(HttpStatusCode Status, string? Error)> CallAsync(string url)
        {
            var response = await client.GetAsync(url);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length > 0 && JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null);
        }

        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"/api/reports/production?{Range}&locationId={_s.LocationId}"));
        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"/api/reports/collections?{Range}&locationId={_s.LocationId}"));
        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"/api/reports/collections?{Range}&providerIds={_s.ProviderA}"));
        Assert.Equal((HttpStatusCode.OK, null), await CallAsync($"/api/reports/production?{Range}&providerIds={_s.ProviderA}"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_line"), await CallAsync($"/api/reports/production/entries?{Range}&line=Payments"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_limit"), await CallAsync($"/api/reports/collections/entries?{Range}&limit=501"));
        Assert.Equal((HttpStatusCode.BadRequest, "dates_required"), await CallAsync("/api/reports/collections"));
        Assert.Equal((HttpStatusCode.BadRequest, "range_too_large"), await CallAsync("/api/reports/production?from=2028-01-01&to=2029-01-01"));
    }

    [Fact]
    public void The_permission_is_held_by_the_administrator_the_office_manager_and_billing_and_can_be_granted()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
            Assert.Equal(role is Role.Admin or Role.OfficeManager or Role.Billing, PermissionMatrix.RoleHas(role, Permission.ViewFinancialReports));
        Assert.DoesNotContain(Permission.ViewFinancialReports, PermissionMatrix.PermissionsFor(Role.Unassigned));
        Assert.True(PermissionProtection.IsGrantable(Permission.ViewFinancialReports));
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ViewFinancialReports)));
        Assert.NotEqual(Permission.ViewOperationalReports, Permission.ViewFinancialReports);
        var all = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Single(all, AuditEventTypes.ReportViewed);
    }
}
