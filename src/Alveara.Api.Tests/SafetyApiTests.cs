using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Safety;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N011 at the HTTP boundary: who may read the safety context, who may acknowledge, who may change or resolve, the CSRF / row-version contract, the stable refusal shapes, and the
/// MINIMAL indicator on the live visit board - authorized roles get two booleans and nothing else, everyone else gets nothing, and no response of the shared board carries a
/// diagnosis, allergy or medication. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class SafetyApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private string SafetyOf(Guid? patient = null) => $"/api/patients/{patient ?? _ann}/safety";
    private static JsonElement Entry(JsonElement context, string title) => context.GetProperty("entries").EnumerateArray().Concat(context.GetProperty("resolved").EnumerateArray()).Single(e => e.GetProperty("title").GetString() == title);

    private async Task<JsonElement> CreateAlertAsync(Session by, string title = "Prosthetic heart valve", object? extra = null, Guid? patient = null)
    {
        var body = new Dictionary<string, object?> { ["category"] = "Condition", ["title"] = title, ["severity"] = "High", ["sourceNote"] = "Reported by the patient at intake" };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var r = await by.SendAsync(HttpMethod.Post, SafetyOf(patient) + "/alerts", body);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private async Task<JsonElement> RequestClearanceAsync(Session by, string reason = "Cardiac clearance before extraction")
    {
        var r = await by.SendAsync(HttpMethod.Post, SafetyOf() + "/clearances", new { kind = "Medical", reason, requestedFrom = "Dr. Singh" });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private static JsonElement ClearanceOf(JsonElement context) => context.GetProperty("clearances")[0];

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_safety_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, SafetyOf()), (HttpMethod.Get, SafetyOf() + "/summary"), (HttpMethod.Post, SafetyOf() + "/alerts"), (HttpMethod.Post, SafetyOf() + "/clearances"),
            (HttpMethod.Put, $"/api/safety/alerts/{id}"), (HttpMethod.Post, $"/api/safety/alerts/{id}/resolve"), (HttpMethod.Post, $"/api/safety/alerts/{id}/reopen"),
            (HttpMethod.Post, $"/api/safety/alerts/{id}/acknowledge"), (HttpMethod.Get, $"/api/safety/alerts/{id}/history"),
            (HttpMethod.Post, $"/api/safety/clearances/{id}/receive"), (HttpMethod.Post, $"/api/safety/clearances/{id}/document"), (HttpMethod.Post, $"/api/safety/clearances/{id}/resolve"),
            (HttpMethod.Post, $"/api/safety/clearances/{id}/cancel"), (HttpMethod.Get, $"/api/safety/clearances/{id}/history"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_the_safety_context_summary_and_histories(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var context = await CreateAlertAsync(dentist);
        var alertId = Entry(context, "Prosthetic heart valve").GetProperty("id").GetString();
        var clearance = ClearanceOf(await RequestClearanceAsync(dentist)).GetProperty("id").GetString();
        var by = await _api.SessionAsync(role);

        var responses = new[]
        {
            await by.SendAsync(HttpMethod.Get, SafetyOf()), await by.SendAsync(HttpMethod.Get, SafetyOf() + "/summary"),
            await by.SendAsync(HttpMethod.Get, $"/api/safety/alerts/{alertId}/history"), await by.SendAsync(HttpMethod.Get, $"/api/safety/clearances/{clearance}/history"),
        };
        if (allowed) Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        else Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)]        // may SEE and acknowledge, may not change
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_create_change_resolve_and_run_clearances(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var context = await CreateAlertAsync(dentist);
        var alert = Entry(context, "Prosthetic heart valve");
        var clearance = ClearanceOf(await RequestClearanceAsync(dentist));
        var by = await _api.SessionAsync(role);

        var attempts = new (HttpMethod Method, string Url, object Body)[]
        {
            (HttpMethod.Post, SafetyOf() + "/alerts", new { category = "Custom", title = $"Mine-{role}", severity = "Low", sourceNote = "Test" }),
            (HttpMethod.Put, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}", new { title = "Changed", severity = "Low", sourceNote = "x", rowVersion = alert.GetProperty("rowVersion").GetString() }),
            (HttpMethod.Post, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}/resolve", new { reason = "Because", rowVersion = alert.GetProperty("rowVersion").GetString() }),
            (HttpMethod.Post, SafetyOf() + "/clearances", new { kind = "Dental", reason = $"By {role}" }),
            (HttpMethod.Post, $"/api/safety/clearances/{clearance.GetProperty("id").GetString()}/receive", new { rowVersion = clearance.GetProperty("rowVersion").GetString() }),
        };
        var statuses = new List<HttpStatusCode>();
        foreach (var (method, url, body) in attempts) statuses.Add((await by.SendAsync(method, url, body)).Status);

        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            Assert.Equal(1, await _s.CountAsync(db => db.SafetyAlerts.Where(a => a.Status == SafetyAlertStatuses.Active)));   // nothing was changed
            Assert.Equal(1, await _s.CountAsync(db => db.Clearances));
            Assert.Equal(1, (await dentist.SendAsync(HttpMethod.Get, $"/api/safety/clearances/{clearance.GetProperty("id").GetString()}/history")).Body.GetProperty("versions").GetArrayLength());
        }
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Whoever_may_read_the_safety_context_may_acknowledge_an_alert_and_nobody_else_can(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var alert = Entry(await CreateAlertAsync(dentist), "Prosthetic heart valve");
        var by = await _api.SessionAsync(role);
        var r = await by.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}/acknowledge", new { revision = alert.GetProperty("revision").GetInt32() });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status);
        Assert.Equal(allowed ? 1 : 0, await _s.CountAsync(db => db.SafetyAlertAcknowledgements));
        if (allowed) Assert.Equal("Active", Entry(r.Body, "Prosthetic heart valve").GetProperty("status").GetString()); // acknowledged, still active
    }

    // ---------- the workflow over HTTP ----------

    [Fact]
    public async Task A_clinician_states_an_alert_the_team_sees_it_acknowledges_it_and_it_is_resolved_only_by_an_explicit_resolution_with_a_reason()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var assistant = await _api.SessionAsync("Assistant");

        var created = await CreateAlertAsync(dentist, "Anticoagulant therapy", new { category = "Anticoagulant", severity = "Critical", detail = "Warfarin, INR 2.5" });
        var alert = Entry(created, "Anticoagulant therapy");
        Assert.Equal(("Alert", "Anticoagulant", "Critical", false), (alert.GetProperty("origin").GetString(), alert.GetProperty("category").GetString(), alert.GetProperty("severity").GetString(), alert.GetProperty("acknowledgedByMe").GetBoolean()));
        Assert.Equal("Critical", created.GetProperty("summary").GetProperty("highestSeverity").GetString());

        var seen = await assistant.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}/acknowledge", new { revision = alert.GetProperty("revision").GetInt32() });
        Assert.True(Entry(seen.Body, "Anticoagulant therapy").GetProperty("acknowledgedByMe").GetBoolean());
        Assert.Equal("Active", Entry(seen.Body, "Anticoagulant therapy").GetProperty("status").GetString());
        var mine = (await hygienist.SendAsync(HttpMethod.Get, SafetyOf())).Body;                                   // the hygienist has not seen it
        Assert.False(Entry(mine, "Anticoagulant therapy").GetProperty("acknowledgedByMe").GetBoolean());
        Assert.Equal(1, mine.GetProperty("summary").GetProperty("unacknowledgedAlertCount").GetInt32());

        var noReason = await hygienist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}/resolve", new { rowVersion = alert.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, Error(noReason.Body)));
        var resolved = await hygienist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}/resolve", new { reason = "Therapy stopped by the cardiologist", rowVersion = alert.GetProperty("rowVersion").GetString() });
        var done = Entry(resolved.Body, "Anticoagulant therapy");
        Assert.Equal(("Resolved", "Therapy stopped by the cardiologist"), (done.GetProperty("status").GetString(), done.GetProperty("resolutionReason").GetString()));
        Assert.Equal(0, resolved.Body.GetProperty("entries").GetArrayLength());

        var history = (await assistant.SendAsync(HttpMethod.Get, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}/history")).Body;
        Assert.Equal(new[] { "Created", "Resolved" }, history.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("changeType").GetString()!));
    }

    [Fact]
    public async Task The_clearance_workflow_runs_over_the_api_and_a_clearance_still_waiting_cannot_be_resolved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var asked = ClearanceOf(await RequestClearanceAsync(dentist));
        var id = asked.GetProperty("id").GetString();
        var early = await dentist.SendAsync(HttpMethod.Post, $"/api/safety/clearances/{id}/resolve", new { reason = "Assumed", rowVersion = asked.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.Conflict, "clearance_not_received"), (early.Status, Error(early.Body)));

        var received = ClearanceOf((await dentist.SendAsync(HttpMethod.Post, $"/api/safety/clearances/{id}/receive", new { note = "Phoned through", rowVersion = asked.GetProperty("rowVersion").GetString() })).Body);
        Assert.Equal(("Received", true), (received.GetProperty("status").GetString(), received.GetProperty("documentPending").GetBoolean()));   // no document yet, and it says so
        var attached = ClearanceOf((await dentist.SendAsync(HttpMethod.Post, $"/api/safety/clearances/{id}/document", new { documentReference = "letter.pdf", rowVersion = received.GetProperty("rowVersion").GetString() })).Body);
        Assert.False(attached.GetProperty("documentPending").GetBoolean());
        var done = (await dentist.SendAsync(HttpMethod.Post, $"/api/safety/clearances/{id}/resolve", new { reason = "Cleared", rowVersion = attached.GetProperty("rowVersion").GetString() })).Body;
        Assert.Equal("Resolved", ClearanceOf(done).GetProperty("status").GetString());
        Assert.Equal(0, done.GetProperty("summary").GetProperty("openClearanceCount").GetInt32());
    }

    // ---------- the contract ----------

    [Fact]
    public async Task Every_change_needs_a_csrf_token_and_a_stale_or_missing_version_is_refused_with_stable_codes()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var alert = Entry(await CreateAlertAsync(dentist), "Prosthetic heart valve");
        var id = alert.GetProperty("id").GetString();
        var version = alert.GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, SafetyOf() + "/alerts", new { category = "Custom", title = "x", severity = "Low", sourceNote = "y" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{id}/resolve", new { reason = "x", rowVersion = version }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{id}/acknowledge", new { revision = 1 }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, SafetyOf() + "/clearances", new { kind = "Medical", reason = "x" }, csrf: false)).Status);

        var noVersion = await dentist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{id}/resolve", new { reason = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var bad = await dentist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{id}/resolve", new { reason = "x", rowVersion = "not-base64!" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_invalid"), (bad.Status, Error(bad.Body)));

        await dentist.SendAsync(HttpMethod.Put, $"/api/safety/alerts/{id}", new { title = "Prosthetic heart valve", detail = "Edited first", severity = "Critical", sourceNote = "Reported", rowVersion = version });
        var stale = await dentist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{id}/resolve", new { reason = "x", rowVersion = version });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        Assert.Equal("SafetyAlert", stale.Body.GetProperty("entityType").GetString());
        var seen = await dentist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{id}/acknowledge", new { revision = alert.GetProperty("revision").GetInt32() });
        Assert.Equal((HttpStatusCode.Conflict, "alert_changed"), (seen.Status, Error(seen.Body)));        // acknowledging the old revision is refused too
        Assert.Equal(0, await _s.CountAsync(db => db.SafetyAlertAcknowledgements));
    }

    [Fact]
    public async Task Refusals_have_stable_codes_and_field_errors_a_client_can_show()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var noSource = await dentist.SendAsync(HttpMethod.Post, SafetyOf() + "/alerts", new { category = "Condition", title = "Asthma", severity = "Low" });
        Assert.Equal((HttpStatusCode.BadRequest, "source_required"), (noSource.Status, Error(noSource.Body)));
        Assert.True(noSource.Body.GetProperty("fieldErrors").TryGetProperty("sourceNote", out _));
        var invalid = await dentist.SendAsync(HttpMethod.Post, SafetyOf() + "/alerts", new { category = "Allergy", title = "", severity = "Urgent", sourceNote = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (invalid.Status, Error(invalid.Body)));
        Assert.Equal(new[] { "category", "severity", "title" }, invalid.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray());

        var missing = await dentist.SendAsync(HttpMethod.Get, SafetyOf(Guid.NewGuid()));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (missing.Status, Error(missing.Body)));
        Assert.Equal("alert_not_found", Error((await dentist.SendAsync(HttpMethod.Get, $"/api/safety/alerts/{Guid.NewGuid()}/history")).Body));
        Assert.Equal("clearance_not_found", Error((await dentist.SendAsync(HttpMethod.Get, $"/api/safety/clearances/{Guid.NewGuid()}/history")).Body));
        var noReason = await dentist.SendAsync(HttpMethod.Post, SafetyOf() + "/clearances", new { kind = "Medical", reason = "" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (noReason.Status, Error(noReason.Body)));
        var badSource = await dentist.SendAsync(HttpMethod.Post, SafetyOf() + "/alerts", new { category = "Condition", title = "Asthma", severity = "Low", sourceNote = "x", sourceItemId = Guid.NewGuid() });
        Assert.Equal((HttpStatusCode.NotFound, "source_not_found"), (badSource.Status, Error(badSource.Body)));
    }

    [Fact]
    public async Task One_patients_safety_context_never_shows_another_patients_alerts()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await CreateAlertAsync(dentist, "Prosthetic heart valve");
        var bos = (await dentist.SendAsync(HttpMethod.Get, SafetyOf(_bo))).Body;
        Assert.Equal(0, bos.GetProperty("entries").GetArrayLength());
        Assert.Equal(_bo.ToString(), bos.GetProperty("patientId").GetString());
    }

    [Fact]
    public async Task The_summary_is_counts_only_and_matches_the_context()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await CreateAlertAsync(dentist, "Prosthetic heart valve");
        await RequestClearanceAsync(dentist);
        var summary = (await dentist.SendAsync(HttpMethod.Get, SafetyOf() + "/summary")).Body;
        Assert.Equal((1, 1, "High"), (summary.GetProperty("activeAlertCount").GetInt32(), summary.GetProperty("openClearanceCount").GetInt32(), summary.GetProperty("highestSeverity").GetString()));
        Assert.DoesNotContain("heart", summary.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    // ---------- the live visit board: minimal indicator, authorization and minimization ----------

    private async Task<JsonElement> BoardAsync(Session by) => (await by.SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14")).Body;

    private async Task BookAsync(params Guid[] patients)
    {
        var admin = await _api.SessionAsync("Admin");
        var hour = 8;
        foreach (var p in patients)
        {
            var r = await admin.SendAsync(HttpMethod.Post, "/api/appointments", new
            {
                patientId = p, providerId = _s.ProviderA, operatoryId = _s.Op1, appointmentTypeId = _s.Short30, startLocal = $"2030-01-14T{hour++:00}:00", durationMinutes = (int?)null,
            }, $"book-{Guid.NewGuid():N}");
            Assert.Equal(HttpStatusCode.Created, r.Status);
        }
    }

    [Fact]
    public async Task The_board_shows_authorized_roles_only_two_booleans_and_nothing_else_about_a_patients_safety()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await RecordAllergyAsync(_bo, "Penicillin", "Severe");
        await CreateAlertAsync(dentist, "HIV positive", new { detail = "On treatment since 2015", sourceNote = "Disclosed to the dentist" });
        await RequestClearanceAsync(dentist, "Viral load review before surgery");
        var cy = await _s.PatientAsync("Cy", "Poe");
        await BookAsync(_ann, _bo, cy);

        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "Admin" })
        {
            var board = await BoardAsync(await _api.SessionAsync(role));
            var cards = board.GetProperty("visits").EnumerateArray().ToList();
            var ann = cards.Single(c => c.GetProperty("appointment").GetProperty("patientId").GetString() == _ann.ToString());
            var safety = ann.GetProperty("safety");
            Assert.Equal(new[] { "alert", "clearance" }, safety.EnumerateObject().Select(p => p.Name).Order().ToArray());   // exactly two fields: nothing else can leak
            Assert.Equal((true, true), (safety.GetProperty("alert").GetBoolean(), safety.GetProperty("clearance").GetBoolean()));
            var bo = cards.Single(c => c.GetProperty("appointment").GetProperty("patientId").GetString() == _bo.ToString());
            Assert.Equal((true, false), (bo.GetProperty("safety").GetProperty("alert").GetBoolean(), bo.GetProperty("safety").GetProperty("clearance").GetBoolean())); // a severe allergy raises the alert flag
            var nothing = cards.Single(c => c.GetProperty("appointment").GetProperty("patientId").GetString() == cy.ToString());
            Assert.True(!nothing.TryGetProperty("safety", out var s2) || s2.ValueKind == JsonValueKind.Null);                 // nothing to show: nothing invented
            foreach (var secret in new[] { "HIV", "Penicillin", "since 2015", "Viral load", "Disclosed", "Severe", "Critical", "Cardiac" })
                Assert.DoesNotContain(secret, board.GetRawText(), StringComparison.OrdinalIgnoreCase);                          // no diagnosis, allergy or medication on the shared board
        }
    }

    [Theory]
    [InlineData("FrontDesk")]
    [InlineData("OfficeManager")]
    public async Task Roles_that_run_the_board_but_do_not_hold_the_indicator_permission_get_no_safety_information_at_all(string role)
    {
        var dentist = await _api.SessionAsync("Dentist");
        await CreateAlertAsync(dentist);
        await RequestClearanceAsync(dentist);
        await BookAsync(_ann);
        var r = await (await _api.SessionAsync(role)).SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14");
        Assert.Equal(HttpStatusCode.OK, r.Status);                                                                         // they still see the board
        var card = r.Body.GetProperty("visits")[0];
        Assert.True(!card.TryGetProperty("safety", out var s) || s.ValueKind == JsonValueKind.Null);
        Assert.DoesNotContain("alert", card.GetRawText().Replace("alerts", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Billing_cannot_read_the_board_at_all()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await _api.SessionAsync("Billing")).SendAsync(HttpMethod.Get, "/api/visits/board?date=2030-01-14")).Status);
    }

    [Fact]
    public async Task The_board_indicator_follows_the_data_it_clears_when_the_alert_is_resolved_and_the_clearance_resolved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var alert = Entry(await CreateAlertAsync(dentist), "Prosthetic heart valve");
        var asked = ClearanceOf(await RequestClearanceAsync(dentist));
        await BookAsync(_ann);
        Assert.True((await BoardAsync(dentist)).GetProperty("visits")[0].GetProperty("safety").GetProperty("alert").GetBoolean());

        await dentist.SendAsync(HttpMethod.Post, $"/api/safety/alerts/{alert.GetProperty("id").GetString()}/resolve", new { reason = "Entered in error", rowVersion = alert.GetProperty("rowVersion").GetString() });
        var received = ClearanceOf((await dentist.SendAsync(HttpMethod.Post, $"/api/safety/clearances/{asked.GetProperty("id").GetString()}/receive", new { rowVersion = asked.GetProperty("rowVersion").GetString() })).Body);
        var afterReceive = (await BoardAsync(dentist)).GetProperty("visits")[0].GetProperty("safety");
        Assert.Equal((false, true), (afterReceive.GetProperty("alert").GetBoolean(), afterReceive.GetProperty("clearance").GetBoolean()));   // received is not resolved: still open
        await dentist.SendAsync(HttpMethod.Post, $"/api/safety/clearances/{asked.GetProperty("id").GetString()}/resolve", new { reason = "Cleared", rowVersion = received.GetProperty("rowVersion").GetString() });
        var card = (await BoardAsync(dentist)).GetProperty("visits")[0];
        Assert.True(!card.TryGetProperty("safety", out var s) || s.ValueKind == JsonValueKind.Null);
    }

    private async Task RecordAllergyAsync(Guid patient, string name, string severity)
    {
        await using var db = _fixture.CreateContext();
        await new ClinicalRecordService(db, Clock).AddItemAsync(patient, EncounterEntryKinds.Allergy, new EntryFields(name, null, "Hives", severity, null, null), null, null, _s.Actor, default);
    }
}
