using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-011-C01 at the HTTP boundary: who may read the board and move a visit, the CSRF and row-version contract, and the stable refusal shapes the board
/// depends on. A real API, real SQL Server and a real signed-in session per role. STORY-011's own endpoints on api/appointments are exercised too, to show
/// they are unchanged and still work alongside the new ones.
/// </summary>
public class VisitsApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _cy;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _cy = await _s.PatientAsync("Cy", "Poe");
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<JsonElement> BookAsync(Session by, Guid patient, DateTime start, Guid? provider = null, Guid? operatory = null)
    {
        var body = new
        {
            patientId = patient, providerId = provider ?? _s.ProviderA, operatoryId = operatory ?? _s.Op1, appointmentTypeId = _s.Long60,
            startLocal = start.ToString("yyyy-MM-ddTHH:mm"), durationMinutes = (int?)null,
        };
        var r = await by.SendAsync(HttpMethod.Post, "/api/appointments", body, $"book-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body;
    }

    private static string Id(JsonElement a) => a.GetProperty("id").GetString()!;
    private static string Version(JsonElement a) => a.GetProperty("rowVersion").GetString()!;
    private static string FlowOf(JsonElement a) => a.GetProperty("flowState").GetString()!;
    private static string StateUrl(JsonElement a) => $"/api/visits/{Id(a)}/state";
    private static object Target(string target, JsonElement a) => new { target, rowVersion = Version(a) };

    private Task<(HttpStatusCode Status, JsonElement Body)> MoveAsync(Session by, JsonElement a, string target) =>
        by.SendAsync(HttpMethod.Post, StateUrl(a), Target(target, a));

    /// <summary>Books a visit and walks it to <paramref name="state"/> as the administrator, returning the latest view.</summary>
    private async Task<JsonElement> VisitAtAsync(Session admin, string state, Guid patient, DateTime start, Guid? provider = null, Guid? operatory = null)
    {
        var a = await BookAsync(admin, patient, start, provider, operatory);
        foreach (var step in VisitStates.InOrder.Skip(1))
        {
            if (FlowOf(a) == state) break;
            var r = await MoveAsync(admin, a, step);
            Assert.Equal(HttpStatusCode.OK, r.Status);
            a = r.Body;
            if (step == state) break;
        }
        return a;
    }

    // ---------- authentication and the board ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_visit_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/visits/board")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/visits/{id}/state", new { target = "Confirmed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync($"/api/visits/{id}/assignment", new { })).StatusCode);
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.OK)]
    [InlineData("Hygienist", HttpStatusCode.OK)]
    [InlineData("Assistant", HttpStatusCode.OK)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Only_roles_that_view_the_schedule_can_read_the_board(string role, HttpStatusCode expected)
    {
        var session = await _api.SessionAsync(role);
        var r = await session.SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14");
        Assert.Equal(expected, r.Status);
        if (expected == HttpStatusCode.Forbidden) Assert.Equal("ViewSchedule", r.Body.GetProperty("required").GetString());
    }

    [Fact]
    public async Task The_board_returns_the_days_visits_in_order_with_states_next_moves_assignments_the_cue_and_the_servers_clock()
    {
        var admin = await _api.SessionAsync("Admin");
        var late = await BookAsync(admin, _ann, At(14));
        var early = await BookAsync(admin, _bo, At(9), _s.ProviderB, _s.Op2);
        await BookAsync(admin, _cy, At(9, 0, dayOffset: 1));
        var desk = await _api.SessionAsync("FrontDesk");

        var r = await desk.SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14");

        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("2030-01-14", r.Body.GetProperty("date").GetString());
        Assert.True(DateTimeOffset.TryParse(r.Body.GetProperty("serverNowUtc").GetString(), out var now));
        Assert.InRange(now, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.Equal(VisitStates.InOrder, r.Body.GetProperty("states").EnumerateArray().Select(s => s.GetString()));
        var visits = r.Body.GetProperty("visits").EnumerateArray().ToList();
        Assert.Equal([Id(early), Id(late)], visits.Select(v => Id(v.GetProperty("appointment"))));
        var first = visits[0];
        Assert.Equal(("Scheduled", "Dr. Patel", "Op 2", false), (first.GetProperty("appointment").GetProperty("flowState").GetString(), first.GetProperty("appointment").GetProperty("visitProviderName").GetString(),
            first.GetProperty("appointment").GetProperty("visitOperatoryName").GetString(), first.GetProperty("carriedOver").GetBoolean()));
        Assert.Equal(["Confirmed", "CheckedIn"], first.GetProperty("appointment").GetProperty("nextFlowStates").EnumerateArray().Select(s => s.GetString()));
        var cue = first.GetProperty("readiness");
        Assert.Equal((true, 0), (cue.GetProperty("ready").GetBoolean(), cue.GetProperty("requiredCount").GetInt32())); // nothing required, and it says so
    }

    [Fact]
    public async Task The_board_defaults_to_today_and_a_malformed_date_is_a_400()
    {
        var desk = await _api.SessionAsync("FrontDesk");
        var today = await desk.SendAsync(HttpMethod.Get, "/api/visits/board");
        Assert.Equal(HttpStatusCode.OK, today.Status);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", today.Body.GetProperty("date").GetString()!);
        foreach (var bad in new[] { "14-01-2030", "garbage", "2030-13-01", "2030-1-4" })
        {
            var r = await desk.SendAsync(HttpMethod.Get, $"/api/visits/board?date={bad}");
            Assert.Equal((HttpStatusCode.BadRequest, "invalid_range"), (r.Status, r.Body.GetProperty("error").GetString()));
        }
    }

    [Fact]
    public async Task The_board_shows_what_the_api_has_just_changed_and_drops_the_cue_once_the_patient_is_seated()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));
        var desk = await _api.SessionAsync("FrontDesk");
        var assistant = await _api.SessionAsync("Assistant");
        var ci = await MoveAsync(desk, a, "CheckedIn");
        var ready = await MoveAsync(assistant, ci.Body, "Ready");
        Assert.Equal(HttpStatusCode.OK, ready.Status);

        var waiting = (await desk.SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14")).Body.GetProperty("visits")[0];
        Assert.Equal("Ready", waiting.GetProperty("appointment").GetProperty("flowState").GetString());
        Assert.Equal(JsonValueKind.Object, waiting.GetProperty("readiness").ValueKind);

        await MoveAsync(assistant, ready.Body, "Seated");
        var seated = (await desk.SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14")).Body.GetProperty("visits")[0];
        Assert.Equal(("Seated", JsonValueKind.Null), (seated.GetProperty("appointment").GetProperty("flowState").GetString(), seated.GetProperty("readiness").ValueKind));
    }

    // ---------- who may make which move ----------

    [Theory]
    [InlineData("FrontDesk", "Scheduled", "Confirmed", HttpStatusCode.OK)]
    [InlineData("FrontDesk", "Scheduled", "CheckedIn", HttpStatusCode.OK)]
    [InlineData("FrontDesk", "InTreatment", "CheckedOut", HttpStatusCode.OK)]
    [InlineData("FrontDesk", "CheckedIn", "Ready", HttpStatusCode.Forbidden)]
    [InlineData("FrontDesk", "Ready", "Seated", HttpStatusCode.Forbidden)]
    [InlineData("FrontDesk", "InTreatment", "Completed", HttpStatusCode.Forbidden)]
    [InlineData("Dentist", "CheckedIn", "Ready", HttpStatusCode.OK)]
    [InlineData("Dentist", "Ready", "Seated", HttpStatusCode.OK)]
    [InlineData("Dentist", "Seated", "InTreatment", HttpStatusCode.OK)]
    [InlineData("Dentist", "InTreatment", "Completed", HttpStatusCode.OK)]
    [InlineData("Dentist", "Scheduled", "CheckedIn", HttpStatusCode.Forbidden)]
    [InlineData("Dentist", "InTreatment", "CheckedOut", HttpStatusCode.Forbidden)]
    [InlineData("Hygienist", "CheckedIn", "Ready", HttpStatusCode.OK)]
    [InlineData("Assistant", "Ready", "Seated", HttpStatusCode.OK)]
    [InlineData("OfficeManager", "Scheduled", "Confirmed", HttpStatusCode.OK)]
    [InlineData("OfficeManager", "CheckedIn", "Ready", HttpStatusCode.OK)]
    [InlineData("Admin", "Scheduled", "CheckedIn", HttpStatusCode.OK)]
    [InlineData("Billing", "Scheduled", "CheckedIn", HttpStatusCode.Forbidden)]
    [InlineData("Billing", "CheckedIn", "Ready", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", "Scheduled", "Confirmed", HttpStatusCode.Forbidden)]
    public async Task Front_office_moves_and_chairside_moves_are_each_allowed_only_to_the_team_that_does_them(string role, string from, string target, HttpStatusCode expected)
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await VisitAtAsync(admin, from, _ann, At(9));
        var session = await _api.SessionAsync(role);

        var r = await MoveAsync(session, a, target);

        Assert.Equal(expected, r.Status);
        var stored = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body;
        if (expected == HttpStatusCode.OK) Assert.Equal(target, FlowOf(stored));
        else
        {
            Assert.Equal(from, FlowOf(stored)); // refused: nothing moved
            Assert.Equal(VisitStateMachineDuty(target), r.Body.GetProperty("required").GetString());
        }
    }

    private static string VisitStateMachineDuty(string target) => VisitStateMachine.DutyFor(target) == TransitionDuty.FrontOffice ? "UpdateVisitFlow" : "UpdateChairsideFlow";

    // ---------- the request contract ----------

    [Fact]
    public async Task A_missing_csrf_token_an_unknown_target_and_a_missing_target_are_refused_before_anything_moves()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, StateUrl(a), Target("CheckedIn", a), csrf: false)).Status);
        foreach (var target in new[] { "Arrived", "Cancelled", "NoShow", "completed", "" })
        {
            var r = await admin.SendAsync(HttpMethod.Post, StateUrl(a), new { target, rowVersion = Version(a) });
            Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, r.Body.GetProperty("error").GetString()));
        }
        var missing = await admin.SendAsync(HttpMethod.Post, StateUrl(a), new { rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (missing.Status, missing.Body.GetProperty("error").GetString()));
        Assert.Equal("Scheduled", FlowOf((await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body));
    }

    [Fact]
    public async Task Refusals_have_stable_codes_a_move_the_rules_refuse_a_stale_or_missing_version_an_unknown_visit_and_a_cancelled_one()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));

        var skip = await MoveAsync(admin, a, "Seated");
        Assert.Equal((HttpStatusCode.Conflict, "invalid_flow_transition"), (skip.Status, skip.Body.GetProperty("error").GetString()));

        var noVersion = await admin.SendAsync(HttpMethod.Post, StateUrl(a), new { target = "CheckedIn" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, noVersion.Body.GetProperty("error").GetString()));

        var first = await MoveAsync(admin, a, "Confirmed");
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var stale = await MoveAsync(admin, a, "CheckedIn"); // still carrying the version from before Confirmed
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, stale.Body.GetProperty("error").GetString()));

        var unknown = await admin.SendAsync(HttpMethod.Post, $"/api/visits/{Guid.NewGuid()}/state", new { target = "Confirmed", rowVersion = "AAAA" });
        Assert.Equal((HttpStatusCode.NotFound, "appointment_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));

        var b = await BookAsync(admin, _bo, At(11));
        var cancelled = await admin.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(b)}/cancel", new { reason = "Illness", rowVersion = Version(b) });
        var onCancelled = await MoveAsync(admin, cancelled.Body, "CheckedIn");
        Assert.Equal((HttpStatusCode.Conflict, "appointment_not_scheduled"), (onCancelled.Status, onCancelled.Body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Repeating_a_move_returns_the_same_visit_and_logs_it_once()
    {
        var desk = await _api.SessionAsync("FrontDesk");
        var a = await BookAsync(desk, _ann, At(9));

        var first = await MoveAsync(desk, a, "Confirmed");
        var again = await MoveAsync(desk, a, "Confirmed"); // the old version, but the visit is already confirmed

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, Version(first.Body)), (first.Status, again.Status, Version(again.Body)));
        var history = (await desk.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}/history")).Body.EnumerateArray().Select(e => e.GetProperty("eventType").GetString());
        Assert.Equal(["Scheduled", "Confirmed"], history);
    }

    [Fact]
    public async Task A_room_that_holds_a_patient_refuses_another_with_the_visit_that_holds_it()
    {
        var admin = await _api.SessionAsync("Admin");
        var first = await VisitAtAsync(admin, "Seated", _ann, At(9));
        var second = await VisitAtAsync(admin, "Ready", _bo, At(11));
        var dentist = await _api.SessionAsync("Dentist");

        var r = await MoveAsync(dentist, second, "Seated");

        Assert.Equal((HttpStatusCode.Conflict, "operatory_occupied", Id(first)), (r.Status, r.Body.GetProperty("error").GetString(), r.Body.GetProperty("conflictingAppointmentId").GetString()));
        Assert.Equal("Ready", FlowOf((await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(second)}")).Body));
    }

    [Fact]
    public async Task Two_chairside_users_seating_two_patients_into_one_room_at_once_leave_exactly_one_seated()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await VisitAtAsync(admin, "Ready", _ann, At(9));
        var b = await VisitAtAsync(admin, "Ready", _bo, At(11));
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");

        var results = await Task.WhenAll(MoveAsync(dentist, a, "Seated"), MoveAsync(hygienist, b, "Seated"));

        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        var refused = Assert.Single(results, r => r.Status != HttpStatusCode.OK);
        Assert.Equal((HttpStatusCode.Conflict, "operatory_occupied"), (refused.Status, refused.Body.GetProperty("error").GetString()));
    }

    // ---------- assignment ----------

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.OK)]
    [InlineData("Hygienist", HttpStatusCode.OK)]
    [InlineData("Assistant", HttpStatusCode.OK)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Either_team_can_reassign_where_the_patient_is_seen_and_nobody_else_can(string role, HttpStatusCode expected)
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));
        var session = await _api.SessionAsync(role);

        var r = await session.SendAsync(HttpMethod.Put, $"/api/visits/{Id(a)}/assignment", new { providerId = _s.ProviderB, operatoryId = _s.Op2, rowVersion = Version(a) });

        Assert.Equal(expected, r.Status);
        var stored = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body;
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(("Dr. Patel", "Op 2"), (r.Body.GetProperty("visitProviderName").GetString(), r.Body.GetProperty("visitOperatoryName").GetString()));
            Assert.Equal(("Dr. Rivera", "Op 1"), (stored.GetProperty("providerName").GetString(), stored.GetProperty("operatoryName").GetString())); // the booking did not move
        }
        else
        {
            Assert.Equal("UpdateVisitFlow or UpdateChairsideFlow", r.Body.GetProperty("required").GetString());
            Assert.Equal("Dr. Rivera", stored.GetProperty("visitProviderName").GetString());
        }
    }

    [Fact]
    public async Task Assignment_refusals_are_missing_ids_unknown_people_stale_versions_finished_visits_and_occupied_rooms()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));
        string Url(JsonElement x) => $"/api/visits/{Id(x)}/assignment";

        var missing = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (missing.Status, missing.Body.GetProperty("error").GetString()));
        var noProvider = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = Guid.NewGuid(), operatoryId = _s.Op2, rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.NotFound, "provider_not_found"), (noProvider.Status, noProvider.Body.GetProperty("error").GetString()));
        var noVersion = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op2 });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, noVersion.Body.GetProperty("error").GetString()));
        var noCsrf = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op2, rowVersion = Version(a) }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.Status);

        var done = await VisitAtAsync(admin, "Completed", _bo, At(11), _s.ProviderB, _s.Op2);
        var finished = await admin.SendAsync(HttpMethod.Put, Url(done), new { providerId = _s.ProviderA, operatoryId = _s.Op1, rowVersion = Version(done) });
        Assert.Equal((HttpStatusCode.Conflict, "visit_completed"), (finished.Status, finished.Body.GetProperty("error").GetString()));

        var holder = await VisitAtAsync(admin, "Seated", _cy, At(13), _s.ProviderB, _s.Op2);
        var seated = await VisitAtAsync(admin, "Seated", await _s.PatientAsync("Di", "Lo"), At(15));
        var occupied = await admin.SendAsync(HttpMethod.Put, Url(seated), new { providerId = _s.ProviderA, operatoryId = _s.Op2, rowVersion = Version(seated) });
        Assert.Equal((HttpStatusCode.Conflict, "operatory_occupied", Id(holder)), (occupied.Status, occupied.Body.GetProperty("error").GetString(), occupied.Body.GetProperty("conflictingAppointmentId").GetString()));

        var first = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op2, rowVersion = Version(a) });
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var stale = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderA, operatoryId = _s.Op2, rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, stale.Body.GetProperty("error").GetString()));
    }

    // ---------- STORY-011's endpoints are unchanged and still work alongside ----------

    [Fact]
    public async Task STORY_011s_endpoints_keep_their_rules_and_a_visit_can_continue_on_the_new_ones_after_the_old()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");

        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a)}/check-in", new { rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.Forbidden, "ManageAppointments"), (refused.Status, refused.Body.GetProperty("required").GetString()));

        var ci = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a)}/check-in", new { rowVersion = Version(a) }); // the old endpoint
        Assert.Equal((HttpStatusCode.OK, "CheckedIn"), (ci.Status, FlowOf(ci.Body)));
        var ready = await MoveAsync(dentist, ci.Body, "Ready"); // and on from there with the new one
        Assert.Equal((HttpStatusCode.OK, "Ready"), (ready.Status, FlowOf(ready.Body)));

        var b = await BookAsync(admin, _bo, At(11), _s.ProviderB, _s.Op2);
        var legacyCheckIn = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(b)}/check-in", new { rowVersion = Version(b) });
        var legacyComplete = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(b)}/complete", new { rowVersion = Version(legacyCheckIn.Body) }); // STORY-011's shortcut
        Assert.Equal((HttpStatusCode.OK, "Completed"), (legacyComplete.Status, FlowOf(legacyComplete.Body)));
    }
}
