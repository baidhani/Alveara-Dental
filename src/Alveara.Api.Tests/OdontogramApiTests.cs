using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-006 at the HTTP boundary: who may read the chart and who may record, move or withdraw a finding, the CSRF / row-version contract, the stable refusal shapes, and the
/// workflow end to end. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class OdontogramApiTests : IAsyncLifetime
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
    private string ChartOf(Guid? patient = null) => $"/api/patients/{patient ?? _ann}/odontogram";
    private static JsonElement Finding(JsonElement chart, string tooth, string? surface = null) =>
        chart.GetProperty("findings").EnumerateArray().Single(f => f.GetProperty("toothKey").GetString() == tooth && (surface is null || f.GetProperty("surface").GetString() == surface));

    private async Task<JsonElement> RecordAsync(Session by, string tooth = "16", string? surface = "O", string condition = "Caries", string state = "Diagnosed", Guid? patient = null)
    {
        var r = await by.SendAsync(HttpMethod.Post, ChartOf(patient) + "/findings", new { toothKey = tooth, surface, condition, state });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_odontogram_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, ChartOf()), (HttpMethod.Post, ChartOf() + "/findings"), (HttpMethod.Get, $"/api/odontogram/findings/{id}/history"),
            (HttpMethod.Post, $"/api/odontogram/findings/{id}/state"), (HttpMethod.Post, $"/api/odontogram/findings/{id}/withdraw"),
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
    public async Task Only_the_clinical_team_and_the_administrator_can_read_the_chart_and_a_findings_history(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var id = Finding(await RecordAsync(dentist), "16").GetProperty("id").GetString();
        var by = await _api.SessionAsync(role);
        var responses = new[] { await by.SendAsync(HttpMethod.Get, ChartOf()), await by.SendAsync(HttpMethod.Get, $"/api/odontogram/findings/{id}/history") };
        Assert.All(responses, r => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)]        // may SEE the chart, may not change it
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_record_move_and_withdraw_findings(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var f = Finding(await RecordAsync(dentist), "16");
        var id = f.GetProperty("id").GetString();
        var version = f.GetProperty("rowVersion").GetString();
        var by = await _api.SessionAsync(role);

        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { toothKey = "26", surface = "M", condition = "Caries", state = "Diagnosed" })).Status,
            (await by.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Planned", rowVersion = version })).Status,
            (await by.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/withdraw", new { reason = "Because", rowVersion = version })).Status,
        };
        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            var chart = (await dentist.SendAsync(HttpMethod.Get, ChartOf())).Body;
            Assert.Equal(1, chart.GetProperty("findings").GetArrayLength());                 // nothing was changed
            Assert.Equal("Diagnosed", Finding(chart, "16").GetProperty("state").GetString());
            Assert.Equal(1, await _s.CountAsync(db => db.ToothFindingVersions));
        }
    }

    // ---------- the workflow over HTTP ----------

    [Fact]
    public async Task A_condition_is_recorded_planned_completed_and_the_chart_follows_each_step_with_who_did_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");

        var recorded = Finding(await RecordAsync(dentist, "16", "O", "Caries", "Diagnosed"), "16");
        Assert.Equal(("O", "Caries", "Diagnosed", "Active"), (recorded.GetProperty("surface").GetString(), recorded.GetProperty("condition").GetString(), recorded.GetProperty("state").GetString(), recorded.GetProperty("status").GetString()));

        var id = recorded.GetProperty("id").GetString();
        var planned = Finding((await hygienist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Planned", rowVersion = recorded.GetProperty("rowVersion").GetString() })).Body, "16");
        Assert.Equal("Planned", planned.GetProperty("state").GetString());
        var done = Finding((await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Completed", rowVersion = planned.GetProperty("rowVersion").GetString() })).Body, "16");
        Assert.Equal("Completed", done.GetProperty("state").GetString());
        Assert.Equal("Completed", Finding((await hygienist.SendAsync(HttpMethod.Get, ChartOf())).Body, "16").GetProperty("state").GetString());

        var history = (await hygienist.SendAsync(HttpMethod.Get, $"/api/odontogram/findings/{id}/history")).Body.GetProperty("versions");
        Assert.Equal(new[] { "Recorded", "StateChanged", "StateChanged" }, history.EnumerateArray().Select(v => v.GetProperty("changeType").GetString()!));
        Assert.Equal(new[] { "Diagnosed", "Planned", "Completed" }, history.EnumerateArray().Select(v => v.GetProperty("state").GetString()!));
        Assert.All(history.EnumerateArray(), v => Assert.False(string.IsNullOrWhiteSpace(v.GetProperty("actorName").GetString())));
    }

    [Fact]
    public async Task A_wrong_entry_is_withdrawn_with_a_reason_and_leaves_the_chart_but_not_the_history()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var f = Finding(await RecordAsync(dentist), "16");
        var id = f.GetProperty("id").GetString();
        var version = f.GetProperty("rowVersion").GetString();

        var noReason = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/withdraw", new { rowVersion = version });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, Error(noReason.Body)));
        var withdrawn = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/withdraw", new { reason = "Wrong tooth", rowVersion = version });
        Assert.Equal(0, withdrawn.Body.GetProperty("findings").GetArrayLength());
        var history = (await dentist.SendAsync(HttpMethod.Get, $"/api/odontogram/findings/{id}/history")).Body.GetProperty("versions");
        Assert.Equal(("Withdrawn", "Wrong tooth"), (history[1].GetProperty("changeType").GetString(), history[1].GetProperty("reason").GetString()));
    }

    // ---------- the contract ----------

    [Fact]
    public async Task Every_change_needs_a_csrf_token_and_a_stale_or_missing_version_is_refused_with_stable_codes()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var f = Finding(await RecordAsync(dentist), "16");
        var id = f.GetProperty("id").GetString();
        var version = f.GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { toothKey = "26", surface = "M", condition = "Caries", state = "Diagnosed" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Planned", rowVersion = version }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/withdraw", new { reason = "x", rowVersion = version }, csrf: false)).Status);

        var noVersion = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Planned" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var bad = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Planned", rowVersion = "not-base64!" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_invalid"), (bad.Status, Error(bad.Body)));

        await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Planned", rowVersion = version });
        var stale = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/state", new { state = "Completed", rowVersion = version });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        Assert.Equal("ToothFinding", stale.Body.GetProperty("entityType").GetString());
        var staleWithdraw = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/withdraw", new { reason = "x", rowVersion = version });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (staleWithdraw.Status, Error(staleWithdraw.Body)));
        Assert.Equal("Planned", Finding((await dentist.SendAsync(HttpMethod.Get, ChartOf())).Body, "16").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Refusals_have_stable_codes_and_field_errors_a_client_can_show()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var wrongTooth = await dentist.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { toothKey = "19", surface = "O", condition = "Caries", state = "Diagnosed" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (wrongTooth.Status, Error(wrongTooth.Body)));
        Assert.True(wrongTooth.Body.GetProperty("fieldErrors").TryGetProperty("toothKey", out _));
        var wrongSurface = await dentist.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { toothKey = "11", surface = "O", condition = "Caries", state = "Diagnosed" });
        Assert.True(wrongSurface.Body.GetProperty("fieldErrors").TryGetProperty("surface", out _));
        var all = await dentist.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { toothKey = "99", surface = "O", condition = "Cavity", state = "Soon" });
        Assert.Equal(new[] { "condition", "state", "toothKey" }, all.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray());

        var missing = await dentist.SendAsync(HttpMethod.Get, ChartOf(Guid.NewGuid()));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (missing.Status, Error(missing.Body)));
        Assert.Equal("patient_not_found", Error((await dentist.SendAsync(HttpMethod.Post, ChartOf(Guid.NewGuid()) + "/findings", new { toothKey = "16", surface = "O", condition = "Caries", state = "Diagnosed" })).Body));
        Assert.Equal("finding_not_found", Error((await dentist.SendAsync(HttpMethod.Get, $"/api/odontogram/findings/{Guid.NewGuid()}/history")).Body));
        Assert.Equal("finding_not_found", Error((await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{Guid.NewGuid()}/state", new { state = "Planned", rowVersion = "AAAA" })).Body));

        var f = Finding(await RecordAsync(dentist, "16", "O", "Caries", "Completed"), "16");
        var illegal = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{f.GetProperty("id").GetString()}/state", new { state = "Planned", rowVersion = f.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.Conflict, "invalid_transition"), (illegal.Status, Error(illegal.Body)));
        var twin = await dentist.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { toothKey = "16", surface = "O", condition = "Caries", state = "Diagnosed" });
        Assert.Equal((HttpStatusCode.Conflict, "finding_exists"), (twin.Status, Error(twin.Body)));
    }

    [Fact]
    public async Task A_missing_or_malformed_body_is_a_400_and_stores_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var empty = await dentist.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        var request = new HttpRequestMessage(HttpMethod.Post, ChartOf() + "/findings") { Content = new StringContent("{ not json", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-Token", dentist.Csrf);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.Client.SendAsync(request)).StatusCode);
        Assert.Equal(0, await _s.CountAsync(db => db.ToothFindings));
    }

    [Fact]
    public async Task One_patients_chart_never_shows_another_patients_findings_and_the_response_carries_no_patient_details()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var chart = await RecordAsync(dentist, "16");
        var bos = (await dentist.SendAsync(HttpMethod.Get, ChartOf(_bo))).Body;
        Assert.Equal(0, bos.GetProperty("findings").GetArrayLength());
        Assert.Equal(_bo.ToString(), bos.GetProperty("patientId").GetString());
        Assert.DoesNotContain("Kim", chart.GetRawText());
    }

    [Fact]
    public async Task The_tooth_is_always_the_fdi_key_so_a_universal_number_is_refused_not_translated()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var r = await dentist.SendAsync(HttpMethod.Post, ChartOf() + "/findings", new { toothKey = "3", surface = "O", condition = "Caries", state = "Diagnosed" });
        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal(0, await _s.CountAsync(db => db.ToothFindings));
    }
}
