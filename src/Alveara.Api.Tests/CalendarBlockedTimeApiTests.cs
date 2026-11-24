using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Configuration;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (F-052, migration M5, owner decision 2026-11-15) at the HTTP boundary, real SQL Server: GET api/calendar/blocked-time gives everyone who can see the schedule the blocked time
/// of a range of practice-local days with a controlled display label and NEVER the private reason; a new block must say how the calendar shows it, from the controlled list.
/// </summary>
public class CalendarBlockedTimeApiTests : IAsyncLifetime
{
    private const string Private = "Dr. Rivera - custody hearing, do not share";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Url(string from, string? to = null, Guid? provider = null) =>
        $"/api/calendar/blocked-time?from={from}{(to is null ? "" : $"&to={to}")}{(provider is null ? "" : $"&providerId={provider}")}";

    private string AddUrl(Guid? provider = null) => $"/api/config/providers/{provider ?? _s.ProviderA}/blocked-time";

    private static object Block(string start, string end, string? label, string? reason = null) => new { startLocal = start, endLocal = end, reason, calendarLabel = label };

    /// <summary>A block added the pre-M5 way: no label, only a reason (straight through the service, as a migrated row would be).</summary>
    private async Task LegacyAsync(Guid provider, DateTime start, DateTime end, string reason)
    {
        await using var db = _fixture.CreateContext();
        await TestStaff.Service(db, Clock).AddBlockedTimeAsync(provider, start, end, reason, _s.Actor, default);
    }

    private static string[] Labels(JsonElement list) => list.EnumerateArray().Select(b => b.GetProperty("displayLabel").GetString()!).ToArray();

    // ---------- who ----------

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var anon = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Url("2030-01-14"))).StatusCode);
    }

    [Theory]
    [InlineData("FrontDesk", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", true)]
    [InlineData("Billing", false)]
    public async Task Everyone_who_can_see_the_schedule_can_read_it_and_nobody_else(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await by.SendAsync(HttpMethod.Get, Url("2030-01-14"))).Status);
    }

    // ---------- what it shows ----------

    [Fact]
    public async Task A_labelled_block_shows_its_label_and_only_the_calendar_fields_never_the_private_reason()
    {
        var admin = await _api.SessionAsync("Admin");
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(HttpMethod.Post, AddUrl(), Block("2030-01-14T12:00", "2030-01-14T13:00", BlockedTimeLabels.Lunch, Private))).Status);

        var desk = await _api.SessionAsync("FrontDesk");
        var list = (await desk.SendAsync(HttpMethod.Get, Url("2030-01-14"))).Body;
        var only = Assert.Single(list.EnumerateArray());
        Assert.Equal(new[] { "displayLabel", "endLocal", "endUtc", "id", "providerId", "providerName", "startLocal", "startUtc" }, only.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(("Lunch", "Dr. Rivera", "2030-01-14T12:00", "2030-01-14T13:00"),
            (only.GetProperty("displayLabel").GetString(), only.GetProperty("providerName").GetString(), only.GetProperty("startLocal").GetString(), only.GetProperty("endLocal").GetString()));
        Assert.DoesNotContain("custody", list.GetRawText());

        var config = (await admin.SendAsync(HttpMethod.Get, AddUrl())).Body;                    // the private reason is still there for the practice configuration
        Assert.Equal(Private, Assert.Single(config.EnumerateArray()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Each_label_shows_its_words()
    {
        var admin = await _api.SessionAsync("Admin");
        var labels = BlockedTimeLabels.All.ToArray();
        for (var i = 0; i < labels.Length; i++)
            Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(HttpMethod.Post, AddUrl(), Block($"2030-01-14T{8 + i:00}:00", $"2030-01-14T{8 + i:00}:30", labels[i]))).Status);
        Assert.Equal(new[] { "Lunch", "Meeting", "Training", "Unavailable", "Other blocked time" }, Labels((await admin.SendAsync(HttpMethod.Get, Url("2030-01-14"))).Body));
    }

    [Fact]
    public async Task A_block_from_before_labels_shows_Lunch_only_for_a_recognised_lunch_reason_and_never_its_reason()
    {
        await LegacyAsync(_s.ProviderA, At(12), At(13), "  staff LUNCH ");
        await LegacyAsync(_s.ProviderA, At(14), At(15), Private);
        await LegacyAsync(_s.ProviderA, At(15), At(16), "Lunch and learn with Acme");   // not exactly a lunch wording: not guessed

        var list = (await (await _api.SessionAsync("FrontDesk")).SendAsync(HttpMethod.Get, Url("2030-01-14"))).Body;
        Assert.Equal(new[] { "Lunch", "Blocked time", "Blocked time" }, Labels(list));
        foreach (var secret in new[] { "custody", "Acme" }) Assert.DoesNotContain(secret, list.GetRawText());
    }

    [Fact]
    public async Task No_patient_data_is_in_the_answer_even_over_a_booked_appointment()
    {
        var ann = await _s.PatientAsync("Annabel", "Quist");
        await _s.ScheduleAsync(_s.Request(ann, At(9)));
        var admin = await _api.SessionAsync("Admin");
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(HttpMethod.Post, AddUrl(), new { startLocal = "2030-01-14T08:00", endLocal = "2030-01-14T12:00", reason = "Course", confirmOverlaps = true, calendarLabel = "training" })).Status);

        var text = (await admin.SendAsync(HttpMethod.Get, Url("2030-01-14"))).Body.GetRawText();
        foreach (var phi in new[] { "Annabel", "Quist", ann.ToString() }) Assert.DoesNotContain(phi, text);
    }

    // ---------- range, provider, time zone ----------

    [Theory]
    [InlineData("")]
    [InlineData("from=14-01-2030")]
    [InlineData("from=2030-01-14&to=2030-01-13")]
    [InlineData("from=2030-01-01&to=2030-02-01")]   // 32 days
    public async Task A_missing_malformed_backwards_or_too_long_range_is_400(string query)
    {
        var r = await (await _api.SessionAsync("FrontDesk")).SendAsync(HttpMethod.Get, $"/api/calendar/blocked-time?{query}");
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_range"), (r.Status, r.Body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task The_range_holds_exactly_the_blocks_that_overlap_its_days_and_a_provider_filter_keeps_only_that_provider()
    {
        var admin = await _api.SessionAsync("Admin");
        await admin.SendAsync(HttpMethod.Post, AddUrl(), Block("2030-01-14T12:00", "2030-01-14T13:00", "lunch"));
        await admin.SendAsync(HttpMethod.Post, AddUrl(_s.ProviderB), Block("2030-01-15T09:00", "2030-01-15T10:00", "meeting"));
        await admin.SendAsync(HttpMethod.Post, AddUrl(), Block("2030-01-16T23:00", "2030-01-17T01:00", "unavailable"));   // over midnight
        await admin.SendAsync(HttpMethod.Post, AddUrl(), Block("2030-02-14T12:00", "2030-02-14T13:00", "lunch"));          // the 31st day of the next query

        var desk = await _api.SessionAsync("FrontDesk");
        Assert.Equal(new[] { "Lunch", "Meeting", "Unavailable" }, Labels((await desk.SendAsync(HttpMethod.Get, Url("2030-01-14", "2030-01-20"))).Body));
        Assert.Equal(new[] { "Unavailable" }, Labels((await desk.SendAsync(HttpMethod.Get, Url("2030-01-17"))).Body));                         // seen from the next day too
        Assert.Equal(new[] { "Meeting" }, Labels((await desk.SendAsync(HttpMethod.Get, Url("2030-01-14", "2030-01-20", _s.ProviderB))).Body));
        Assert.Empty((await desk.SendAsync(HttpMethod.Get, Url("2030-01-18"))).Body.EnumerateArray());
        Assert.Equal(new[] { "Meeting", "Unavailable", "Lunch" }, Labels((await desk.SendAsync(HttpMethod.Get, Url("2030-01-15", "2030-02-14"))).Body));   // 31 days: allowed, and holds the last day's block
    }

    [Fact]
    public async Task On_the_spring_forward_day_the_local_times_are_the_practice_clock_times()
    {
        var admin = await _api.SessionAsync("Admin");
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(HttpMethod.Post, AddUrl(), Block("2030-03-10T01:00", "2030-03-10T04:00", "unavailable"))).Status);   // 02:00-03:00 does not exist
        var only = Assert.Single((await admin.SendAsync(HttpMethod.Get, Url("2030-03-10"))).Body.EnumerateArray());
        Assert.Equal(("2030-03-10T01:00", "2030-03-10T04:00"), (only.GetProperty("startLocal").GetString(), only.GetProperty("endLocal").GetString()));
        Assert.Equal(TimeSpan.FromHours(2), only.GetProperty("endUtc").GetDateTimeOffset() - only.GetProperty("startUtc").GetDateTimeOffset());   // two real hours
    }

    // ---------- a new block must say how the calendar shows it ----------

    [Theory]
    [InlineData(null, "calendar_label_required")]
    [InlineData("   ", "calendar_label_required")]
    [InlineData("Lunch", "invalid_calendar_label")]        // the codes are lower case
    [InlineData("coffee", "invalid_calendar_label")]
    public async Task A_new_block_without_a_controlled_label_is_refused_and_nothing_is_stored(string? label, string code)
    {
        var admin = await _api.SessionAsync("Admin");
        var r = await admin.SendAsync(HttpMethod.Post, AddUrl(), Block("2030-01-14T12:00", "2030-01-14T13:00", label, "Lunch"));
        Assert.Equal((HttpStatusCode.BadRequest, code), (r.Status, r.Body.GetProperty("error").GetString()));
        Assert.Empty((await admin.SendAsync(HttpMethod.Get, AddUrl())).Body.EnumerateArray());
    }

    [Fact]
    public async Task The_configuration_list_shows_the_label_it_was_saved_with()
    {
        var admin = await _api.SessionAsync("Admin");
        await admin.SendAsync(HttpMethod.Post, AddUrl(), Block("2030-01-14T12:00", "2030-01-14T13:00", "meeting", "Team"));
        Assert.Equal("meeting", Assert.Single((await admin.SendAsync(HttpMethod.Get, AddUrl())).Body.EnumerateArray()).GetProperty("calendarLabel").GetString());
    }
}
