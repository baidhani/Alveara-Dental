using System.Net;
using System.Reflection;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Identity;
using static Alveara.Api.Tests.SchedulingApiHarness;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 3a over the real API: the four clinical routes are refused to an anonymous caller (401) and to every role that does not hold ViewClinicalReports (403), and answered for the administrator, the office manager
/// and the dentist; the three report permissions are separate from one another; a report is <c>no-store</c> with its envelope; a filter a report cannot honestly apply gets <c>filter_not_supported</c>; and the permission is
/// registered, grantable and held by the right roles.
/// </summary>
public class ClinicalReportsApiTests : IAsyncLifetime
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
        foreach (var role in new[] { "Admin", "OfficeManager", "Dentist", "Billing", "FrontDesk", "Hygienist", "Assistant" }) _sessions[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string[] Urls => [$"/api/reports/treatment-plans?{Range}", $"/api/reports/completed-procedures?{Range}", $"/api/reports/treatment-plans/items?{Range}", $"/api/reports/completed-procedures/procedures?{Range}"];

    [Fact]
    public async Task Every_route_is_refused_to_anonymous_callers_and_to_roles_without_the_permission()
    {
        using var anonymous = _api.Factory.CreateClient();
        foreach (var url in Urls) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        foreach (var role in new[] { "Billing", "FrontDesk", "Hygienist", "Assistant" })
            foreach (var url in Urls) Assert.Equal(HttpStatusCode.Forbidden, (await _sessions[role].Client.GetAsync(url)).StatusCode);
        foreach (var role in new[] { "Admin", "OfficeManager", "Dentist" })
            foreach (var url in Urls) Assert.Equal(HttpStatusCode.OK, (await _sessions[role].Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task The_three_report_permissions_are_separate_from_one_another()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _sessions["Dentist"].Client.GetAsync($"/api/reports/production?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sessions["Dentist"].Client.GetAsync($"/api/reports/schedule?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sessions["Billing"].Client.GetAsync($"/api/reports/completed-procedures?{Range}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _sessions["FrontDesk"].Client.GetAsync($"/api/reports/treatment-plans?{Range}")).StatusCode);
    }

    [Fact]
    public async Task A_clinical_report_is_no_store_carries_its_envelope_and_says_when_it_has_no_data()
    {
        var response = await _sessions["Dentist"].Client.GetAsync($"/api/reports/treatment-plans?{Range}");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("treatment-plans", body.GetProperty("reportId").GetString());
        Assert.Equal(1, body.GetProperty("definitionVersion").GetInt32());
        Assert.True(body.GetProperty("noData").GetBoolean());
        Assert.True(body.GetProperty("data").GetProperty("reconciled").GetBoolean());
        Assert.Equal(7, body.GetProperty("data").GetProperty("cells").GetArrayLength());

        var procedures = JsonDocument.Parse(await (await _sessions["OfficeManager"].Client.GetAsync($"/api/reports/completed-procedures?{Range}")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("completed-procedures", procedures.GetProperty("reportId").GetString());
        Assert.Equal(3, procedures.GetProperty("data").GetProperty("days").GetArrayLength());
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

        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"/api/reports/treatment-plans?{Range}&providerIds={_s.ProviderA}"));
        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"/api/reports/treatment-plans?{Range}&locationId={_s.LocationId}"));
        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"/api/reports/completed-procedures?{Range}&locationId={_s.LocationId}"));
        Assert.Equal((HttpStatusCode.OK, null), await CallAsync($"/api/reports/completed-procedures?{Range}&providerIds={_s.ProviderA}"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_cell"), await CallAsync($"/api/reports/treatment-plans/items?{Range}&cell=Done"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_code"), await CallAsync($"/api/reports/completed-procedures/procedures?{Range}&code=D0120"));
        Assert.Equal((HttpStatusCode.BadRequest, "dates_required"), await CallAsync("/api/reports/treatment-plans"));
        Assert.Equal((HttpStatusCode.BadRequest, "range_too_large"), await CallAsync("/api/reports/completed-procedures?from=2028-01-01&to=2029-01-01"));
    }

    [Fact]
    public void The_permission_is_held_by_the_administrator_the_office_manager_and_the_dentist_and_can_be_granted()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
            Assert.Equal(role is Role.Admin or Role.OfficeManager or Role.Dentist, PermissionMatrix.RoleHas(role, Permission.ViewClinicalReports));
        Assert.DoesNotContain(Permission.ViewClinicalReports, PermissionMatrix.PermissionsFor(Role.Unassigned));
        Assert.True(PermissionProtection.IsGrantable(Permission.ViewClinicalReports));
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ViewClinicalReports)));
        Assert.Equal(3, new[] { Permission.ViewOperationalReports, Permission.ViewFinancialReports, Permission.ViewClinicalReports }.Distinct().Count());
        var all = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Single(all, AuditEventTypes.ReportViewed);
    }
}
