using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using static Alveara.Api.Tests.SchedulingApiHarness;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 7 over the real API: every export route needs the SAME permission as the report it exports (anonymous 401, wrong role 403, and a refused export leaves no trace), answers a CSV that is no-store with a
/// name that says what it holds, carries exactly the numbers the report shows, records the run and the export as two separate audit entries, and refuses a bad request with the report's own stable codes.
/// </summary>
public class ReportExportApiTests : IAsyncLifetime
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

    public static TheoryData<string> Routes => new()
    {
        "schedule", "patient-flow", "follow-ups", "cancellations", "production", "collections", "balances", "treatment-plans", "completed-procedures", "provider-activity",
        "measurement/metrics", "measurement/comparison",
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task An_export_is_allowed_to_exactly_the_roles_that_may_read_the_report_it_exports(string report)
    {
        var url = $"/api/reports/{report}/export?{Range}";
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

        var mayRead = 0;
        foreach (var (_, session) in _sessions)
        {
            var read = (await session.Client.GetAsync($"/api/reports/{report}?{Range}")).StatusCode;
            var export = (await session.Client.GetAsync(url)).StatusCode;
            Assert.Equal(read, export);                                          // the same answer, 200 or 403, for the same person
            if (read == HttpStatusCode.OK) mayRead++; else Assert.Equal(HttpStatusCode.Forbidden, export);
        }
        Assert.InRange(mayRead, 2, 6);                                           // someone may read every report, and not everyone may
    }

    [Fact]
    public async Task An_export_is_a_no_store_csv_named_for_what_it_holds_and_carries_the_numbers_the_report_shows()
    {
        var admin = _sessions["Admin"].Client;
        await _s.ScheduleAsync(_s.Request(await _s.PatientAsync(), new DateTime(2030, 1, 15, 10, 0, 0)));
        var json = JsonDocument.Parse(await admin.GetStringAsync($"/api/reports/schedule?{Range}")).RootElement;
        var response = await admin.GetAsync($"/api/reports/schedule/export?{Range}");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("schedule_2030-01-14_to_2030-01-16.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var totals = json.GetProperty("data").GetProperty("totals");
        Assert.Contains($"Totals - Appointments,{totals.GetProperty("appointments").GetInt32()}\r\n", csv);
        Assert.Contains($"Totals - Cancelled,{totals.GetProperty("cancelled").GetInt32()}\r\n", csv);
        Assert.Contains("Report,schedule\r\n", csv);
        Assert.True(totals.GetProperty("appointments").GetInt32() > 0);          // the comparison above is not between two zeros
    }

    [Fact]
    public async Task A_run_and_an_export_are_two_audit_entries_and_a_refused_export_leaves_neither()
    {
        await _sessions["Billing"].Client.GetAsync($"/api/reports/schedule/export?{Range}");                      // refused: no permission
        await _sessions["Admin"].Client.GetAsync("/api/reports/schedule/export?from=2030-01-16&to=2030-01-14");    // refused: inverted range
        await using (var db = _fixture.CreateContext())
        {
            Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportExported));
            Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
        }

        Assert.Equal(HttpStatusCode.OK, (await _sessions["Admin"].Client.GetAsync($"/api/reports/schedule/export?{Range}")).StatusCode);
        await using var after = _fixture.CreateContext();
        Assert.Equal(1, await after.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
        var exported = await after.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportExported);
        Assert.StartsWith("Report schedule exported as CSV: 3 day(s), ", exported.Details);
        Assert.Equal(_sessions["Admin"].UserId, exported.PerformedByUserAccountId);
    }

    [Fact]
    public async Task A_bad_request_gets_the_reports_own_stable_code_not_a_file()
    {
        var inverted = await _sessions["Admin"].Client.GetAsync("/api/reports/schedule/export?from=2030-01-16&to=2030-01-14");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Equal("application/json", inverted.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrEmpty(JsonDocument.Parse(await inverted.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString()));

        var unsupported = await _sessions["Billing"].Client.GetAsync($"/api/reports/collections/export?{Range}&providerIds={_s.ProviderA}");
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal("filter_not_supported", JsonDocument.Parse(await unsupported.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }
}
