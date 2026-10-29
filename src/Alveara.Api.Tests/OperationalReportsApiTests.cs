using System.Net;
using System.Reflection;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Identity;
using static Alveara.Api.Tests.SchedulingApiHarness;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 1 over the real API: every operational report route is refused to an anonymous caller (401) and to the roles that do not hold ViewOperationalReports (403), and answered for the administrator, the office
/// manager and the front desk; a report comes back <c>no-store</c> with its envelope (report id, definition version, the filter as understood, the practice time zone) and neither it nor its supporting records carry a patient
/// name; a bad request gets a stable code (a malformed id is a plain 400); and the drill-down total equals the number in the report.
/// </summary>
public class OperationalReportsApiTests : IAsyncLifetime
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
        var s = new SchedulingTestSupport(_fixture);
        _s = s;
        await s.ArrangeAsync();
        await s.ScheduleAsync(s.Request(await s.PatientAsync("Zed", "Qwerty"), SchedulingTestSupport.At(9)));
        foreach (var role in new[] { "Admin", "OfficeManager", "FrontDesk", "Dentist", "Hygienist", "Assistant", "Billing" }) _sessions[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string[] Urls => [$"/api/reports/schedule?{Range}", $"/api/reports/patient-flow?{Range}", $"/api/reports/schedule/appointments?{Range}", $"/api/reports/patient-flow/appointments?{Range}", $"/api/reports/follow-ups?{Range}", $"/api/reports/follow-ups/tasks?{Range}", $"/api/reports/cancellations?{Range}", $"/api/reports/cancellations/appointments?{Range}&measure=Booked"];

    [Fact]
    public async Task Every_route_is_refused_to_anonymous_callers_and_to_roles_without_the_permission()
    {
        using var anonymous = _api.Factory.CreateClient();
        foreach (var url in Urls) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "Billing" })
            foreach (var url in Urls) Assert.Equal(HttpStatusCode.Forbidden, (await _sessions[role].Client.GetAsync(url)).StatusCode);
        foreach (var role in new[] { "Admin", "OfficeManager", "FrontDesk" })
            foreach (var url in Urls) Assert.Equal(HttpStatusCode.OK, (await _sessions[role].Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task A_report_is_no_store_carries_its_envelope_and_names_no_patient()
    {
        var response = await _sessions["OfficeManager"].Client.GetAsync($"/api/reports/schedule?{Range}");
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("schedule", body.GetProperty("reportId").GetString());
        Assert.Equal(1, body.GetProperty("definitionVersion").GetInt32());
        Assert.Equal("America/Chicago", body.GetProperty("filter").GetProperty("timeZone").GetString());
        Assert.Equal(3, body.GetProperty("filter").GetProperty("days").GetInt32());
        Assert.Equal(1, body.GetProperty("data").GetProperty("totals").GetProperty("appointments").GetInt32());
        Assert.False(body.GetProperty("noData").GetBoolean());
        Assert.DoesNotContain("Zed", text);
        Assert.DoesNotContain("Qwerty", text);

        var drill = await _sessions["FrontDesk"].Client.GetAsync($"/api/reports/patient-flow/appointments?{Range}&flowState=Scheduled");
        var drillText = await drill.Content.ReadAsStringAsync();
        var page = JsonDocument.Parse(drillText).RootElement;
        Assert.Equal("no-store", drill.Headers.CacheControl?.ToString());
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("rows").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("next").ValueKind);
        Assert.DoesNotContain("Zed", drillText);
        Assert.DoesNotContain("Qwerty", drillText);
    }

    [Fact]
    public async Task A_bad_request_gets_a_stable_code_and_a_malformed_id_is_a_plain_400()
    {
        var client = _sessions["Admin"].Client;
        async Task<(HttpStatusCode Status, string? Error)> CallAsync(string query, string route = "schedule")
        {
            var response = await client.GetAsync($"/api/reports/{route}?{query}");
            var text = await response.Content.ReadAsStringAsync();
            var error = text.Length > 0 && JsonDocument.Parse(text).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (response.StatusCode, error);
        }

        Assert.Equal((HttpStatusCode.BadRequest, "dates_required"), await CallAsync(""));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_date"), await CallAsync("from=14-01-2030&to=2030-01-16"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_range"), await CallAsync("from=2030-01-16&to=2030-01-14"));
        Assert.Equal((HttpStatusCode.BadRequest, "range_too_large"), await CallAsync("from=2028-01-01&to=2029-01-01"));
        Assert.Equal((HttpStatusCode.BadRequest, "unknown_provider"), await CallAsync($"{Range}&providerIds={Guid.NewGuid()}"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_status"), await CallAsync($"{Range}&status=Done", "schedule/appointments"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_limit"), await CallAsync($"{Range}&limit=0", "patient-flow/appointments"));
        Assert.Equal((HttpStatusCode.BadRequest, "filter_not_supported"), await CallAsync($"{Range}&locationId={_s.LocationId}", "follow-ups"));
        Assert.Equal((HttpStatusCode.OK, null), await CallAsync($"{Range}&locationId={_s.LocationId}", "cancellations"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_measure"), await CallAsync(Range, "cancellations/appointments"));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_outcome"), await CallAsync($"{Range}&outcome=Lost", "follow-ups/tasks"));
        Assert.Equal(HttpStatusCode.BadRequest, (await CallAsync($"{Range}&providerIds=not-an-id")).Status);
    }

    [Fact]
    public void The_permission_is_held_by_the_administrator_the_office_manager_and_the_front_desk_and_can_be_granted_to_another_role()
    {
        foreach (var role in Enum.GetValues<Role>().Where(r => r != Role.Unassigned))
            Assert.Equal(role is Role.Admin or Role.OfficeManager or Role.FrontDesk, PermissionMatrix.RoleHas(role, Permission.ViewOperationalReports));
        Assert.DoesNotContain(Permission.ViewOperationalReports, PermissionMatrix.PermissionsFor(Role.Unassigned));
        Assert.True(PermissionProtection.IsGrantable(Permission.ViewOperationalReports));
        Assert.True(PermissionRegistry.IsActive(nameof(Permission.ViewOperationalReports)));

        var all = typeof(AuditEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Single(all, AuditEventTypes.ReportViewed);        // no other event carries the same name
    }
}
