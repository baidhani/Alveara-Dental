using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-011-C01 at the HTTP boundary: who may read the board and move a visit, the CSRF and row-version contract, and the stable refusal shapes the board
/// depends on. A real API, real SQL Server and a real signed-in session per role. STORY-011's own endpoints on api/appointments are exercised too, to show
/// they are unchanged and still work alongside the new ones.
/// ALV-N021 A2: the API runs on the test day's clock (the check-in day rule); moves are made by the role that does them (treatment by the booked provider's linked account); the legacy routes run
/// the same policy; completion needs a resolved checkout, which these tests record through the checkout API (its HTTP contract is tested here too).
/// </summary>
public class VisitsApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _cy;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture) { ConfigureServices = services => services.AddSingleton<IPracticeClock>(VisitDayClock) };
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _cy = await _s.PatientAsync("Cy", "Poe");
    });
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

    private readonly Dictionary<Guid, Session> _providerSessions = [];

    /// <summary>ALV-N021 A2: a Dentist session whose account is linked to <paramref name="provider"/> (start and end treatment belong to the booked provider).</summary>
    private async Task<Session> ProviderSessionAsync(Guid provider)
    {
        if (_providerSessions.TryGetValue(provider, out var cached)) return cached;
        var session = await _api.SessionAsync("Dentist");
        await using var db = _fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("UPDATE s SET [UserAccountId] = {0} FROM [StaffProfiles] s JOIN [ProviderProfiles] p ON p.[StaffProfileId] = s.[Id] WHERE p.[Id] = {1}", session.UserId, provider);
        return _providerSessions[provider] = session;
    }

    /// <summary>ALV-N021 A2: records "patient left" and "follow-up declined" through the checkout API, so a visit with nothing owed may be completed.</summary>
    private async Task ResolveCheckoutAsync(Session by, JsonElement a)
    {
        foreach (var (item, outcome) in new[] { ("departure", "patient_left"), ("follow_up", "patient_declined") })
            Assert.Equal(HttpStatusCode.OK, (await by.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a)}/checkout/{item}", new { outcome }, $"co-{Guid.NewGuid():N}")).Status);
    }

    /// <summary>Books a visit and walks it to <paramref name="state"/>: the administrator makes the front-office and chairside moves, the booked provider starts and ends treatment, and the
    /// checkout is resolved as soon as the visit is ready for checkout.</summary>
    private async Task<JsonElement> VisitAtAsync(Session admin, string state, Guid patient, DateTime start, Guid? provider = null, Guid? operatory = null)
    {
        var a = await BookAsync(admin, patient, start, provider, operatory);
        foreach (var step in VisitStates.InOrder.Skip(1))
        {
            if (FlowOf(a) == state) break;
            var by = step is "InTreatment" or "CheckedOut" ? await ProviderSessionAsync(provider ?? _s.ProviderA) : admin;
            var r = await MoveAsync(by, a, step);
            Assert.Equal(HttpStatusCode.OK, r.Status);
            a = r.Body;
            if (step == "CheckedOut") await ResolveCheckoutAsync(admin, a);
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/visits/{id}/undo-check-in", new { reason = "x" })).StatusCode);   // ALV-N021 A2
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
        Assert.InRange(now, VisitDayClock.UtcNow.AddMinutes(-5), VisitDayClock.UtcNow);   // the server's own clock (the test day's, running)
        Assert.Equal(VisitStates.InOrder, r.Body.GetProperty("states").EnumerateArray().Select(s => s.GetString()));
        var visits = r.Body.GetProperty("visits").EnumerateArray().ToList();
        Assert.Equal([Id(early), Id(late)], visits.Select(v => Id(v.GetProperty("appointment"))));
        var first = visits[0];
        Assert.Equal(("Scheduled", "Dr. Patel", "Op 2", false), (first.GetProperty("appointment").GetProperty("flowState").GetString(), first.GetProperty("appointment").GetProperty("visitProviderName").GetString(),
            first.GetProperty("appointment").GetProperty("visitOperatoryName").GetString(), first.GetProperty("carriedOver").GetBoolean()));
        Assert.Equal(["Confirmed"], first.GetProperty("appointment").GetProperty("nextFlowStates").EnumerateArray().Select(s => s.GetString()));   // ALV-N021 A2: confirmation required
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
        var ci = await MoveAsync(desk, (await MoveAsync(desk, a, "Confirmed")).Body, "CheckedIn");
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

    // ALV-N021 A2 (briefing 5.4): "BookedDentist" is a Dentist whose account is linked to the appointment's booked provider; a plain "Dentist" is not.
    [Theory]
    [InlineData("FrontDesk", "Scheduled", "Confirmed", HttpStatusCode.OK)]
    [InlineData("FrontDesk", "Confirmed", "CheckedIn", HttpStatusCode.OK)]
    [InlineData("FrontDesk", "CheckedIn", "Ready", HttpStatusCode.Forbidden)]
    [InlineData("FrontDesk", "Ready", "Seated", HttpStatusCode.Forbidden)]
    [InlineData("FrontDesk", "InTreatment", "CheckedOut", HttpStatusCode.Forbidden)]
    [InlineData("FrontDesk", "CheckedOut", "Completed", HttpStatusCode.OK)]
    [InlineData("Dentist", "CheckedIn", "Ready", HttpStatusCode.OK)]
    [InlineData("Dentist", "Ready", "Seated", HttpStatusCode.OK)]
    [InlineData("Dentist", "Seated", "InTreatment", HttpStatusCode.Forbidden)]
    [InlineData("Dentist", "Confirmed", "CheckedIn", HttpStatusCode.Forbidden)]
    [InlineData("Dentist", "CheckedOut", "Completed", HttpStatusCode.Forbidden)]
    [InlineData("BookedDentist", "Seated", "InTreatment", HttpStatusCode.OK)]
    [InlineData("BookedDentist", "InTreatment", "CheckedOut", HttpStatusCode.OK)]
    [InlineData("Hygienist", "CheckedIn", "Ready", HttpStatusCode.OK)]
    [InlineData("Assistant", "Ready", "Seated", HttpStatusCode.OK)]
    [InlineData("Assistant", "Seated", "InTreatment", HttpStatusCode.Forbidden)]
    [InlineData("OfficeManager", "Scheduled", "Confirmed", HttpStatusCode.OK)]
    [InlineData("OfficeManager", "CheckedIn", "Ready", HttpStatusCode.Forbidden)]   // OD-07: no chairside default for the office manager
    [InlineData("Admin", "Confirmed", "CheckedIn", HttpStatusCode.OK)]
    [InlineData("Admin", "Seated", "InTreatment", HttpStatusCode.Forbidden)]        // OD-08: an administrator not linked to the booked provider
    [InlineData("Billing", "Confirmed", "CheckedIn", HttpStatusCode.Forbidden)]
    [InlineData("Billing", "CheckedIn", "Ready", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", "Scheduled", "Confirmed", HttpStatusCode.Forbidden)]
    public async Task Every_move_is_allowed_only_to_the_role_and_person_that_makes_it(string role, string from, string target, HttpStatusCode expected)
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await VisitAtAsync(admin, from, _ann, At(9));
        var session = role == "BookedDentist" ? await ProviderSessionAsync(_s.ProviderA) : await _api.SessionAsync(role);

        var r = await MoveAsync(session, a, target);

        Assert.Equal(expected, r.Status);
        var stored = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body;
        if (expected == HttpStatusCode.OK) Assert.Equal(target, FlowOf(stored));
        else
        {
            Assert.Equal(from, FlowOf(stored)); // refused: nothing moved
            Assert.Equal("visit_move_forbidden", r.Body.GetProperty("error").GetString());
        }
    }

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
        Assert.Equal((HttpStatusCode.Conflict, "illegal_transition"), (skip.Status, skip.Body.GetProperty("error").GetString()));

        var noVersion = await admin.SendAsync(HttpMethod.Post, StateUrl(a), new { target = "CheckedIn" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, noVersion.Body.GetProperty("error").GetString()));

        var first = await MoveAsync(admin, a, "Confirmed");
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var stale = await MoveAsync(admin, a, "CheckedIn"); // still carrying the version from before Confirmed
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, stale.Body.GetProperty("error").GetString()));

        var unknown = await admin.SendAsync(HttpMethod.Post, $"/api/visits/{Guid.NewGuid()}/state", new { target = "Confirmed", rowVersion = "AAAA" });
        Assert.Equal((HttpStatusCode.NotFound, "appointment_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));

        var b = await BookAsync(admin, _bo, At(11));
        var cancelled = await admin.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(b)}/cancel", new { category = "patient", note = "Illness", rowVersion = Version(b) });
        var onCancelled = await MoveAsync(admin, cancelled.Body, "CheckedIn");
        Assert.Equal((HttpStatusCode.Conflict, "terminal_state"), (onCancelled.Status, onCancelled.Body.GetProperty("error").GetString()));
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

    // ---------- ALV-N021 A2: reassignment (OD-13 to OD-16) ----------

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData("OfficeManager", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData("Hygienist", HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData("Assistant", HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData("Billing", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task A_provider_change_needs_the_reassignment_permission_and_a_room_change_also_allows_chairside_staff(string role, HttpStatusCode provider, HttpStatusCode room)
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));
        var b = await BookAsync(admin, _bo, At(11));
        var session = await _api.SessionAsync(role);

        var p = await session.SendAsync(HttpMethod.Put, $"/api/visits/{Id(a)}/assignment", new { providerId = _s.ProviderB, operatoryId = _s.Op1, rowVersion = Version(a), reason = "Dr. Rivera is ill" });
        var r = await session.SendAsync(HttpMethod.Put, $"/api/visits/{Id(b)}/assignment", new { providerId = _s.ProviderA, operatoryId = _s.Op2, rowVersion = Version(b) });

        Assert.Equal((provider, room), (p.Status, r.Status));
        var storedA = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body;
        Assert.Equal(provider == HttpStatusCode.OK ? "Dr. Patel" : "Dr. Rivera", storedA.GetProperty("providerName").GetString());   // the BOOKING moves (no separate "Seen by")
        var storedB = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(b)}")).Body;
        Assert.Equal(room == HttpStatusCode.OK ? "Op 2" : "Op 1", storedB.GetProperty("operatoryName").GetString());
        if (provider == HttpStatusCode.Forbidden)
            Assert.Equal(("visit_move_forbidden", "reassign_not_permitted"), (p.Body.GetProperty("error").GetString(), p.Body.GetProperty("reason").GetString()));
        if (room == HttpStatusCode.Forbidden)
            Assert.Equal(("visit_move_forbidden", "room_change_not_permitted"), (r.Body.GetProperty("error").GetString(), r.Body.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Reassignment_refusals_are_missing_ids_a_missing_reason_unknown_people_stale_versions_started_visits_and_occupied_rooms()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await BookAsync(admin, _ann, At(9));
        string Url(JsonElement x) => $"/api/visits/{Id(x)}/assignment";
        const string why = "Dr. Rivera is ill";

        var missing = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, rowVersion = Version(a), reason = why });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (missing.Status, missing.Body.GetProperty("error").GetString()));
        var noReason = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op1, rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, noReason.Body.GetProperty("error").GetString()));
        var noProvider = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = Guid.NewGuid(), operatoryId = _s.Op2, rowVersion = Version(a), reason = why });
        Assert.Equal((HttpStatusCode.NotFound, "provider_not_found"), (noProvider.Status, noProvider.Body.GetProperty("error").GetString()));
        var noVersion = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op2, reason = why });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, noVersion.Body.GetProperty("error").GetString()));
        var noCsrf = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op2, rowVersion = Version(a), reason = why }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.Status);

        var started = await VisitAtAsync(admin, "Completed", _bo, At(11), _s.ProviderB, _s.Op2);   // finished, so Op 2 is free for the seated patients below (In treatment is covered in ProviderReassignmentTests)
        var tooLate = await admin.SendAsync(HttpMethod.Put, Url(started), new { providerId = _s.ProviderA, operatoryId = _s.Op2, rowVersion = Version(started), reason = why });
        Assert.Equal((HttpStatusCode.Conflict, "visit_already_started"), (tooLate.Status, tooLate.Body.GetProperty("error").GetString()));

        var holder = await VisitAtAsync(admin, "Seated", _cy, At(13), _s.ProviderB, _s.Op2);
        var seated = await VisitAtAsync(admin, "Seated", await _s.PatientAsync("Di", "Lo"), At(15));
        var occupied = await admin.SendAsync(HttpMethod.Put, Url(seated), new { providerId = _s.ProviderA, operatoryId = _s.Op2, rowVersion = Version(seated) });
        Assert.Equal((HttpStatusCode.Conflict, "operatory_occupied", Id(holder)), (occupied.Status, occupied.Body.GetProperty("error").GetString(), occupied.Body.GetProperty("conflictingAppointmentId").GetString()));

        var first = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op2, rowVersion = Version(a), reason = why });
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var retry = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderB, operatoryId = _s.Op2, rowVersion = Version(a), reason = why });
        Assert.Equal((HttpStatusCode.OK, Version(first.Body)), (retry.Status, Version(retry.Body)));   // a retry changes nothing
        var stale = await admin.SendAsync(HttpMethod.Put, Url(a), new { providerId = _s.ProviderA, operatoryId = _s.Op2, rowVersion = Version(a), reason = why });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, stale.Body.GetProperty("error").GetString()));
        await using var db = _fixture.CreateContext();
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.Kind == Alveara.Api.Architecture.Notifications.NotificationKinds.AppointmentProviderReassigned));
    }

    // ---------- ALV-N021 A2: undo check-in (OD-11) ----------

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.Forbidden)]
    [InlineData("Hygienist", HttpStatusCode.Forbidden)]
    [InlineData("Assistant", HttpStatusCode.Forbidden)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Only_the_front_office_can_undo_a_check_in_over_the_api(string role, HttpStatusCode expected)
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await VisitAtAsync(admin, "CheckedIn", _ann, At(9));
        var session = await _api.SessionAsync(role);

        var r = await session.SendAsync(HttpMethod.Post, $"/api/visits/{Id(a)}/undo-check-in", new { reason = "Wrong patient", rowVersion = Version(a) });

        Assert.Equal(expected, r.Status);
        var stored = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body;
        Assert.Equal(expected == HttpStatusCode.OK ? "Confirmed" : "CheckedIn", FlowOf(stored));
        if (expected == HttpStatusCode.Forbidden)
            Assert.Equal(("visit_move_forbidden", "front_office_only"), (r.Body.GetProperty("error").GetString(), r.Body.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Undo_check_in_refusals_are_a_missing_reason_a_missing_csrf_token_and_a_seated_patient()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await VisitAtAsync(admin, "CheckedIn", _ann, At(9));
        string Url(JsonElement x) => $"/api/visits/{Id(x)}/undo-check-in";

        var noReason = await admin.SendAsync(HttpMethod.Post, Url(a), new { rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, noReason.Body.GetProperty("error").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, Url(a), new { reason = "Wrong patient", rowVersion = Version(a) }, csrf: false)).Status);

        var seated = await VisitAtAsync(admin, "Seated", _bo, At(11));
        var tooLate = await admin.SendAsync(HttpMethod.Post, Url(seated), new { reason = "Wrong patient", rowVersion = Version(seated) });
        Assert.Equal((HttpStatusCode.Conflict, "undo_check_in_too_late"), (tooLate.Status, tooLate.Body.GetProperty("error").GetString()));

        var undone = await admin.SendAsync(HttpMethod.Post, Url(a), new { reason = "Wrong patient", rowVersion = Version(a) });
        Assert.Equal((HttpStatusCode.OK, "Confirmed"), (undone.Status, FlowOf(undone.Body)));
        var history = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}/history")).Body.EnumerateArray().Last();
        Assert.Equal(("CheckInUndone", "Wrong patient"), (history.GetProperty("eventType").GetString(), history.GetProperty("reason").GetString()));
    }

    // ---------- ALV-N021 A2: blocked time over appointments (OD-17, OD-18) ----------

    [Fact]
    public async Task Blocked_time_over_an_appointment_needs_confirmation_flags_it_refuses_its_check_in_and_the_front_desk_can_keep_it()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = (await MoveAsync(admin, await BookAsync(admin, _ann, At(9)), "Confirmed")).Body;
        var url = $"/api/config/providers/{_s.ProviderA}/blocked-time";

        var unconfirmed = await admin.SendAsync(HttpMethod.Post, url, new { startLocal = "2030-01-14T08:00", endLocal = "2030-01-14T12:00", reason = "Course" });
        Assert.Equal((HttpStatusCode.Conflict, "block_overlaps_appointments"), (unconfirmed.Status, unconfirmed.Body.GetProperty("error").GetString()));
        Assert.Equal(Id(a), unconfirmed.Body.GetProperty("overlaps")[0].GetProperty("appointmentId").GetString());
        var noReason = await admin.SendAsync(HttpMethod.Post, url, new { startLocal = "2030-01-14T08:00", endLocal = "2030-01-14T12:00", confirmOverlaps = true });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, noReason.Body.GetProperty("error").GetString()));
        var saved = await admin.SendAsync(HttpMethod.Post, url, new { startLocal = "2030-01-14T08:00", endLocal = "2030-01-14T12:00", reason = "Course", confirmOverlaps = true });
        Assert.Equal(HttpStatusCode.Created, saved.Status);

        var flagged = (await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body;
        Assert.True(flagged.GetProperty("blockFlagged").GetBoolean());
        var checkIn = await MoveAsync(admin, flagged, "CheckedIn");
        Assert.Equal((HttpStatusCode.Conflict, "block_flag_unresolved"), (checkIn.Status, checkIn.Body.GetProperty("error").GetString()));

        var keepUrl = $"/api/appointments/{Id(a)}/keep-despite-block";
        var dentist = await _api.SessionAsync("Dentist");
        var refused = await dentist.SendAsync(HttpMethod.Post, keepUrl, new { reason = "Seen anyway", rowVersion = Version(flagged) });
        Assert.Equal((HttpStatusCode.Forbidden, "visit_move_forbidden"), (refused.Status, refused.Body.GetProperty("error").GetString()));
        var desk = await _api.SessionAsync("FrontDesk");
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, keepUrl, new { reason = "Seen anyway", rowVersion = Version(flagged) }, csrf: false)).Status);
        var kept = await desk.SendAsync(HttpMethod.Post, keepUrl, new { reason = "Seen anyway", rowVersion = Version(flagged) });
        Assert.Equal((HttpStatusCode.OK, false), (kept.Status, kept.Body.GetProperty("blockFlagged").GetBoolean()));
        Assert.Equal((HttpStatusCode.OK, "CheckedIn"), ((await MoveAsync(desk, kept.Body, "CheckedIn")).Status, FlowOf((await admin.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(a)}")).Body)));

        var overActive = await admin.SendAsync(HttpMethod.Post, url, new { startLocal = "2030-01-14T09:00", endLocal = "2030-01-14T09:30", reason = "Meeting", confirmOverlaps = true });
        Assert.Equal((HttpStatusCode.Conflict, "block_overlaps_active_visit"), (overActive.Status, overActive.Body.GetProperty("error").GetString()));
    }

    // ---------- ALV-N021 A2: available actions (briefing 5.5) ----------

    [Fact]
    public async Task The_actions_route_answers_for_the_signed_in_user_and_each_board_card_carries_the_same_list()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = (await MoveAsync(admin, await BookAsync(admin, _ann, At(9)), "Confirmed")).Body;
        var url = $"/api/appointments/{Id(a)}/actions";
        static string Codes(System.Text.Json.JsonElement list) =>
            string.Join(' ', list.GetProperty("actions").EnumerateArray().Select(x => x.GetProperty("code").GetString()).Order(StringComparer.Ordinal));

        var desk = await _api.SessionAsync("FrontDesk");
        var deskList = await desk.SendAsync(HttpMethod.Get, url);
        Assert.Equal((HttpStatusCode.OK, "cancel check_in no_show reschedule"), (deskList.Status, Codes(deskList.Body)));
        Assert.Equal("Waiting for the front desk to check the patient in.", deskList.Body.GetProperty("nextActorText").GetString());
        var assistant = await _api.SessionAsync("Assistant");
        Assert.Equal("change_room", Codes((await assistant.SendAsync(HttpMethod.Get, url)).Body));

        var board = (await desk.SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14")).Body;
        var card = board.GetProperty("visits").EnumerateArray().Single(v => v.GetProperty("appointment").GetProperty("id").GetString() == Id(a));
        Assert.Equal(deskList.Body.GetRawText(), card.GetProperty("actions").GetRawText());

        Assert.Equal(HttpStatusCode.NotFound, (await desk.SendAsync(HttpMethod.Get, $"/api/appointments/{Guid.NewGuid()}/actions")).Status);
    }

    // ---------- STORY-011's endpoints run the same policy ----------

    [Fact]
    public async Task STORY_011s_endpoints_run_the_same_policy_and_path_and_a_visit_can_continue_on_the_new_ones_after_the_old()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await MoveAsync(admin, await BookAsync(admin, _ann, At(9)), "Confirmed");
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var booked = await ProviderSessionAsync(_s.ProviderA);

        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a.Body)}/check-in", new { rowVersion = Version(a.Body) });
        Assert.Equal((HttpStatusCode.Forbidden, "visit_move_forbidden", "front_office_only"), (refused.Status, refused.Body.GetProperty("error").GetString(), refused.Body.GetProperty("reason").GetString()));

        var ci = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a.Body)}/check-in", new { rowVersion = Version(a.Body) }); // the old endpoint
        Assert.Equal((HttpStatusCode.OK, "CheckedIn"), (ci.Status, FlowOf(ci.Body)));
        var shortcut = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a.Body)}/complete", new { rowVersion = Version(ci.Body) });   // STORY-011's shortcut is gone
        Assert.Equal((HttpStatusCode.Conflict, "illegal_transition"), (shortcut.Status, shortcut.Body.GetProperty("error").GetString()));
        var seated = await MoveAsync(dentist, (await MoveAsync(dentist, ci.Body, "Ready")).Body, "Seated"); // and on from there with the new one
        var deskStart = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a.Body)}/start-treatment", new { rowVersion = Version(seated.Body) });
        Assert.Equal(HttpStatusCode.Forbidden, deskStart.Status);
        var started = await booked.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a.Body)}/start-treatment", new { rowVersion = Version(seated.Body) });
        Assert.Equal((HttpStatusCode.OK, "InTreatment"), (started.Status, FlowOf(started.Body)));
        var ended = await MoveAsync(booked, started.Body, "CheckedOut");

        var gated = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a.Body)}/complete", new { rowVersion = Version(ended.Body) });
        Assert.Equal((HttpStatusCode.Conflict, "checkout_incomplete", "departure,follow_up"), (gated.Status, gated.Body.GetProperty("error").GetString(), gated.Body.GetProperty("reason").GetString()));
        await ResolveCheckoutAsync(desk, ended.Body);
        var done = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Id(a.Body)}/complete", new { rowVersion = Version(ended.Body) });
        Assert.Equal((HttpStatusCode.OK, "Completed"), (done.Status, FlowOf(done.Body)));
    }

    // ---------- the checkout API (A2 step 2) ----------

    [Fact]
    public async Task The_checkout_api_reads_with_the_schedule_permission_and_records_with_a_key_a_csrf_token_and_the_right_duty()
    {
        var admin = await _api.SessionAsync("Admin");
        var a = await VisitAtAsync(admin, "CheckedOut", _ann, At(9));
        var url = $"/api/appointments/{Id(a)}/checkout";
        var desk = await _api.SessionAsync("FrontDesk");
        var billing = await _api.SessionAsync("Billing");
        var assistant = await _api.SessionAsync("Assistant");

        var read = await desk.SendAsync(HttpMethod.Get, url);
        Assert.Equal((HttpStatusCode.OK, true), (read.Status, read.Body.GetProperty("ready").GetBoolean()));   // resolved by the walk
        Assert.Equal((HttpStatusCode.Forbidden, "ViewSchedule"), ((await billing.SendAsync(HttpMethod.Get, url)).Status, (await billing.SendAsync(HttpMethod.Get, url)).Body.GetProperty("required").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Factory.CreateClient().GetAsync(url)).StatusCode);

        var noKey = await desk.SendAsync(HttpMethod.Post, $"{url}/follow_up", new { outcome = "deferred", reason = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "idempotency_key_required"), (noKey.Status, noKey.Body.GetProperty("error").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, $"{url}/follow_up", new { outcome = "deferred", reason = "x" }, "k-1", csrf: false)).Status);
        var forbidden = await assistant.SendAsync(HttpMethod.Post, $"{url}/departure", new { outcome = "patient_left" }, "k-2");
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), (forbidden.Status, forbidden.Body.GetProperty("error").GetString()));
        var unknownItem = await desk.SendAsync(HttpMethod.Post, $"{url}/treatment_ended", new { outcome = "patient_left" }, "k-3");
        Assert.Equal((HttpStatusCode.NotFound, "checkout_item_unknown"), (unknownItem.Status, unknownItem.Body.GetProperty("error").GetString()));
        var corrected = await desk.SendAsync(HttpMethod.Post, $"{url}/follow_up", new { outcome = "deferred", reason = "Waiting for results" }, "k-4");
        Assert.Equal(HttpStatusCode.OK, corrected.Status);
        Assert.Equal("deferred", corrected.Body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("code").GetString() == "follow_up").GetProperty("outcome").GetProperty("code").GetString());
    }
}
