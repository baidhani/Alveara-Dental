using System.Net;
using System.Text.Json;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-012-C01 at the HTTP boundary: who may read and who may write, the CSRF and row-version contract, the stable refusal shape that names every entry to correct, and the workflow end to end - start a
/// draft, save a tooth at a time, finalize, link, compare. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class PerioSessionApiTests : IAsyncLifetime
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
    private string Charts(Guid? p = null) => $"/api/patients/{p ?? _ann}/periodontal";
    private static object Site(string tooth = "16", string site = "B", int? pd = 3, int? rec = 1, bool? bleeding = false, bool? pus = null, bool? plaque = null) => new { toothKey = tooth, site, probingDepthMm = pd, recessionMm = rec, bleeding, suppuration = pus, plaque };
    private static object Tooth(string tooth, int? mobility = null, int? furcation = null, bool? excluded = null) => new { toothKey = tooth, mobility, furcation, excluded };
    private static string Version(JsonElement session) => session.GetProperty("rowVersion").GetString()!;

    private async Task<JsonElement> StartAsync(Session by, Guid? patient = null)
    {
        var r = await by.SendAsync(HttpMethod.Post, Charts(patient) + "/sessions");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> SaveAsync(Session by, JsonElement session, object? readings = null, object? teeth = null, object? clearSites = null, object? clearTeeth = null, string? version = null) =>
        by.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{session.GetProperty("id").GetString()}/entries", new { rowVersion = version ?? Version(session), readings, teeth, clearSites, clearTeeth });

    private async Task<JsonElement> FinalizedChartAsync(Session by, Guid? patient = null, params object[] readings)
    {
        var s = await StartAsync(by, patient);
        var saved = await SaveAsync(by, s, readings.Length == 0 ? new[] { Site() } : readings);
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        var done = await by.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{s.GetProperty("id").GetString()}/finalize", new { rowVersion = Version(saved.Body) });
        Assert.Equal(HttpStatusCode.OK, done.Status);
        await Task.Delay(20);
        return done.Body;
    }

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod, string)[]
        {
            (HttpMethod.Get, Charts() + "/session"), (HttpMethod.Post, Charts() + "/sessions"), (HttpMethod.Get, $"/api/periodontal/sessions/{id}"), (HttpMethod.Post, $"/api/periodontal/sessions/{id}/entries"),
            (HttpMethod.Post, $"/api/periodontal/sessions/{id}/finalize"), (HttpMethod.Post, $"/api/periodontal/sessions/{id}/abandon"), (HttpMethod.Get, $"/api/periodontal/charts/{id}"),
            (HttpMethod.Post, $"/api/periodontal/charts/{id}/links"), (HttpMethod.Get, Charts() + "/comparison"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_sessions_charts_and_comparisons(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        await FinalizedChartAsync(dentist);
        var chart = await FinalizedChartAsync(dentist, null, Site("16", "B", 5));
        var open = await StartAsync(dentist);
        var by = await _api.SessionAsync(role);
        var reads = new[]
        {
            await by.SendAsync(HttpMethod.Get, Charts() + "/session"), await by.SendAsync(HttpMethod.Get, $"/api/periodontal/sessions/{open.GetProperty("id").GetString()}"),
            await by.SendAsync(HttpMethod.Get, $"/api/periodontal/charts/{chart.GetProperty("id").GetString()}"), await by.SendAsync(HttpMethod.Get, Charts() + $"/comparison?currentExamId={chart.GetProperty("id").GetString()}"),
        };
        Assert.All(reads, r => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_start_save_finalize_abandon_and_link(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var open = await StartAsync(dentist);
        var chart = await FinalizedChartAsync(dentist, _bo);
        var by = await _api.SessionAsync(role);
        var id = open.GetProperty("id").GetString();
        var (readingsBefore, linksBefore) = (await _s.CountAsync(db => db.PerioSessionReadings), await _s.CountAsync(db => db.PerioExamLinks));
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Charts(_bo) + "/sessions")).Status,
            (await SaveAsync(by, open, new[] { Site() })).Status,
            (await by.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{id}/finalize", new { rowVersion = Version(open) })).Status,
            (await by.SendAsync(HttpMethod.Post, $"/api/periodontal/charts/{chart.GetProperty("id").GetString()}/links", new { linkType = "Diagnosis", reference = "dx-1" })).Status,
        };
        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, st => Assert.Equal(HttpStatusCode.Forbidden, st));
            Assert.Equal((readingsBefore, linksBefore), (await _s.CountAsync(db => db.PerioSessionReadings), await _s.CountAsync(db => db.PerioExamLinks)));   // nothing was saved, nothing was closed, nothing was linked
            Assert.Equal(PerioStatus.Draft, (await dentist.SendAsync(HttpMethod.Get, $"/api/periodontal/sessions/{id}")).Body.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var open = await StartAsync(dentist);
        var id = open.GetProperty("id").GetString();
        foreach (var (url, body) in new (string, object)[] { ($"/api/periodontal/sessions/{id}/entries", new { rowVersion = Version(open), readings = new[] { Site() } }), ($"/api/periodontal/sessions/{id}/finalize", new { rowVersion = Version(open) }), ($"/api/periodontal/sessions/{id}/abandon", new { rowVersion = Version(open) }) })
            Assert.NotEqual(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, url, body, csrf: false)).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, Charts(_bo) + "/sessions", csrf: false)).Status);
        Assert.Equal((0, 1), (await _s.CountAsync(db => db.PerioSessionReadings), await _s.CountAsync(db => db.PerioSessions)));
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_draft_is_started_shared_filled_a_tooth_at_a_time_finalized_linked_and_read_back_as_a_chart()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        Assert.Equal(JsonValueKind.Null, (await hygienist.SendAsync(HttpMethod.Get, Charts() + "/session")).Body.GetProperty("session").ValueKind);

        var s0 = await StartAsync(dentist);
        Assert.Equal(s0.GetProperty("id").GetString(), (await StartAsync(hygienist)).GetProperty("id").GetString());             // the same draft, whoever asks
        Assert.Equal("18", s0.GetProperty("next").GetProperty("toothKey").GetString());

        var saved = await SaveAsync(hygienist, s0, new[] { Site("16", "DB", 3, 0, false, false, true), Site("16", "B", 5, 2, true) }, new[] { Tooth("16", 1, 2) });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        var b = saved.Body.GetProperty("readings").EnumerateArray().Single(r => r.GetProperty("site").GetString() == "B");
        Assert.Equal((5, 2, 7, true), (b.GetProperty("probingDepthMm").GetInt32(), b.GetProperty("recessionMm").GetInt32(), b.GetProperty("attachmentLossMm").GetInt32(), b.GetProperty("bleeding").GetBoolean()));
        Assert.Equal(1, saved.Body.GetProperty("teeth").GetArrayLength());
        Assert.NotEqual(Version(s0), Version(saved.Body));
        var current = (await hygienist.SendAsync(HttpMethod.Get, Charts() + "/session")).Body.GetProperty("session");
        Assert.Equal(2, current.GetProperty("readings").GetArrayLength());

        var done = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{s0.GetProperty("id").GetString()}/finalize", new { rowVersion = Version(saved.Body) });
        Assert.Equal(HttpStatusCode.OK, done.Status);
        var examId = done.Body.GetProperty("id").GetString();
        Assert.Equal(2, done.Body.GetProperty("readingCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await hygienist.SendAsync(HttpMethod.Get, Charts() + "/session")).Body.GetProperty("session").ValueKind);   // no draft is open any more

        var linked = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/charts/{examId}/links", new { linkType = "Diagnosis", reference = "dx-9" });
        Assert.Equal(HttpStatusCode.OK, linked.Status);
        var chart = (await hygienist.SendAsync(HttpMethod.Get, $"/api/periodontal/charts/{examId}")).Body;
        Assert.Equal(("Diagnosis", "dx-9"), (chart.GetProperty("links")[0].GetProperty("linkType").GetString(), chart.GetProperty("links")[0].GetProperty("reference").GetString()));
        Assert.Equal((1, 2), (chart.GetProperty("teeth")[0].GetProperty("mobility").GetInt32(), chart.GetProperty("teeth")[0].GetProperty("furcation").GetInt32()));
        var history = (await hygienist.SendAsync(HttpMethod.Get, Charts() + "/charts")).Body.GetProperty("exams");
        Assert.Equal(examId, history[0].GetProperty("id").GetString());                                                          // the STORY-012 history shows it
    }

    [Fact]
    public async Task Finalizing_twice_is_200_both_times_with_the_same_chart_and_a_closed_session_refuses_more_entries()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var s = await StartAsync(dentist);
        var saved = await SaveAsync(dentist, s, new[] { Site() });
        var id = s.GetProperty("id").GetString();
        var first = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{id}/finalize", new { rowVersion = Version(saved.Body) });
        var again = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{id}/finalize", new { rowVersion = Version(saved.Body) });
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.Status, again.Status));
        Assert.Equal(first.Body.GetProperty("id").GetString(), again.Body.GetProperty("id").GetString());
        var more = await SaveAsync(dentist, saved.Body, new[] { Site("17") });
        Assert.Equal((HttpStatusCode.Conflict, "session_closed"), (more.Status, Error(more.Body)));
        Assert.Equal(1, await _s.CountAsync(db => db.PerioExams));
    }

    // ---------- the contract: refusals ----------

    [Fact]
    public async Task A_wrong_entry_is_a_400_listing_every_problem_and_the_draft_is_exactly_as_it_was()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var s = await StartAsync(dentist);
        var good = await SaveAsync(dentist, s, new[] { Site("18", "B", 3, 0, false) });
        var r = await SaveAsync(dentist, good.Body, new[] { Site("17", "B", 99, 1), Site("19", "B"), Site("17", "Q") }, new[] { Tooth("11", null, 2), Tooth("17", 9) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        Assert.Equal(new[] { "out_of_range", "unknown_tooth", "unknown_site", "furcation_not_applicable", "out_of_range" }, r.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("code").GetString()!));
        var after = (await dentist.SendAsync(HttpMethod.Get, $"/api/periodontal/sessions/{s.GetProperty("id").GetString()}")).Body;
        Assert.Equal(Version(good.Body), Version(after));                                                                         // not even the version moved
        Assert.Equal("18", Assert.Single(after.GetProperty("readings").EnumerateArray()).GetProperty("toothKey").GetString());     // the valid tooth is still there
        Assert.Equal(0, await _s.CountAsync(db => db.PerioSessionTeeth));
    }

    [Fact]
    public async Task A_value_left_out_is_reported_never_defaulted_and_missing_items_are_a_400_with_nothing_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var s = await StartAsync(dentist);
        var r = await SaveAsync(dentist, s, new[] { Site("16", "B", pd: null, rec: null, bleeding: null) });
        Assert.Equal(new[] { "probingDepthMm", "recessionMm", "bleeding" }, r.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("field").GetString()!));
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(dentist, s, new object?[] { null })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(dentist, s, teeth: new object?[] { null })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(dentist, s, clearSites: new[] { new { toothKey = "16", site = (string?)null } })).Status);
        Assert.Equal(0, await _s.CountAsync(db => db.PerioSessionReadings));
    }

    [Fact]
    public async Task A_missing_row_version_is_a_400_and_a_stale_one_is_the_409_conflict_with_the_first_writers_entries_standing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var s = await StartAsync(dentist);
        var none = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{s.GetProperty("id").GetString()}/entries", new { readings = new[] { Site() } });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (none.Status, Error(none.Body)));
        await SaveAsync(dentist, s, new[] { Site("16", "B", 3) });
        var stale = await SaveAsync(hygienist, s, new[] { Site("17", "B", 9) });                                                // the hygienist still holds the first version
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        var now = (await dentist.SendAsync(HttpMethod.Get, $"/api/periodontal/sessions/{s.GetProperty("id").GetString()}")).Body;
        Assert.Equal("16", Assert.Single(now.GetProperty("readings").EnumerateArray()).GetProperty("toothKey").GetString());
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(hygienist, now, new[] { Site("17", "B", 9) })).Status);                  // after reloading she can go on
    }

    [Fact]
    public async Task Two_people_saving_the_same_version_at_the_same_moment_one_gets_200_and_the_other_409()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var s = await StartAsync(dentist);
        var results = await Task.WhenAll(SaveAsync(dentist, s, new[] { Site("16", "B", 3) }), SaveAsync(hygienist, s, new[] { Site("17", "B", 4) }));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, results.Select(r => r.Status).OrderBy(x => x).ToArray());
        Assert.Equal(1, await _s.CountAsync(db => db.PerioSessionReadings));
    }

    [Fact]
    public async Task A_tooth_the_odontogram_records_as_missing_is_listed_skipped_and_refused_unless_marked_not_charted()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var missing = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/odontogram/findings", new { toothKey = "18", condition = "Missing", state = "Existing" });
        Assert.Equal(HttpStatusCode.OK, missing.Status);
        var s = await StartAsync(dentist);
        Assert.Equal(new[] { "18" }, s.GetProperty("absentTeeth").EnumerateArray().Select(x => x.GetString()!));
        Assert.Equal("17", s.GetProperty("next").GetProperty("toothKey").GetString());
        var refused = await SaveAsync(dentist, s, new[] { Site("18", "B") });
        Assert.Equal("tooth_absent", refused.Body.GetProperty("problems")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(dentist, s, teeth: new[] { Tooth("18", excluded: true) })).Status);
    }

    [Fact]
    public async Task Finalizing_an_empty_draft_is_refused_and_abandoning_closes_it_so_a_new_one_can_start()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var s = await StartAsync(dentist);
        var id = s.GetProperty("id").GetString();
        var empty = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{id}/finalize", new { rowVersion = Version(s) });
        Assert.Equal((HttpStatusCode.BadRequest, "required"), (empty.Status, empty.Body.GetProperty("problems")[0].GetProperty("code").GetString()));
        var gone = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/sessions/{id}/abandon", new { rowVersion = Version(s) });
        Assert.Equal((HttpStatusCode.OK, "Abandoned"), (gone.Status, gone.Body.GetProperty("status").GetString()));
        Assert.NotEqual(id, (await StartAsync(dentist)).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/periodontal/sessions/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task A_link_with_an_unknown_type_is_a_400_for_an_unknown_chart_a_404_and_the_same_link_again_is_quiet()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var chart = await FinalizedChartAsync(dentist);
        var id = chart.GetProperty("id").GetString();
        var bad = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/charts/{id}/links", new { linkType = "Procedure", reference = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (bad.Status, Error(bad.Body)));
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/charts/{Guid.NewGuid()}/links", new { linkType = "Diagnosis", reference = "x" })).Status);
        await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/charts/{id}/links", new { linkType = "Encounter", reference = "enc-1" });
        var again = await dentist.SendAsync(HttpMethod.Post, $"/api/periodontal/charts/{id}/links", new { linkType = "Encounter", reference = "enc-1" });
        Assert.Equal((HttpStatusCode.OK, 1), (again.Status, again.Body.GetProperty("links").GetArrayLength()));
    }

    // ---------- comparison ----------

    [Fact]
    public async Task A_draft_is_compared_with_the_latest_finalized_chart_and_a_saved_chart_with_the_one_before_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await FinalizedChartAsync(dentist, null, Site("16", "B", 8, 0, true));
        var middle = await FinalizedChartAsync(dentist, null, Site("16", "B", 5, 1, true), Site("16", "MB", 4));
        var draft = await StartAsync(dentist);
        await SaveAsync(dentist, draft, new[] { Site("16", "B", 3, 1, false), Site("17", "B", 4, 0, true) });
        var live = await dentist.SendAsync(HttpMethod.Get, Charts() + $"/comparison?currentSessionId={draft.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, live.Status);
        var c = live.Body;
        Assert.Equal((1, 1, 0, 1, 1), (c.GetProperty("matchedSites").GetInt32(), c.GetProperty("improved").GetInt32(), c.GetProperty("worsened").GetInt32(), c.GetProperty("onlyPrevious").GetInt32(), c.GetProperty("onlyCurrent").GetInt32()));
        var b16 = c.GetProperty("sites").EnumerateArray().Single(x => x.GetProperty("toothKey").GetString() == "16" && x.GetProperty("site").GetString() == "B");
        Assert.Equal((5, 3, -2, "Improved"), (b16.GetProperty("previousDepthMm").GetInt32(), b16.GetProperty("currentDepthMm").GetInt32(), b16.GetProperty("depthChangeMm").GetInt32(), b16.GetProperty("trend").GetString()));

        var saved = await dentist.SendAsync(HttpMethod.Get, Charts() + $"/comparison?currentExamId={middle.GetProperty("id").GetString()}");
        var older = saved.Body.GetProperty("sites").EnumerateArray().Single(x => x.GetProperty("toothKey").GetString() == "16" && x.GetProperty("site").GetString() == "B");
        Assert.Equal((8, 5, -3), (older.GetProperty("previousDepthMm").GetInt32(), older.GetProperty("currentDepthMm").GetInt32(), older.GetProperty("depthChangeMm").GetInt32()));
        Assert.Equal("deep", older.GetProperty("currentCues").EnumerateArray().Select(x => x.GetString()).First(x => x == "deep"));
    }

    [Fact]
    public async Task Comparison_refuses_no_earlier_chart_a_chart_of_another_patient_and_a_request_that_names_neither_or_both()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var only = await FinalizedChartAsync(dentist);
        var bos = await FinalizedChartAsync(dentist, _bo);
        var id = only.GetProperty("id").GetString();
        var none = await dentist.SendAsync(HttpMethod.Get, Charts() + $"/comparison?currentExamId={id}");
        Assert.Equal((HttpStatusCode.NotFound, "no_previous_chart"), (none.Status, Error(none.Body)));
        Assert.Equal("exam_not_found", Error((await dentist.SendAsync(HttpMethod.Get, Charts() + $"/comparison?currentExamId={bos.GetProperty("id").GetString()}")).Body));
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Get, Charts() + "/comparison")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Get, Charts() + $"/comparison?currentExamId={id}&currentSessionId={Guid.NewGuid()}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/periodontal/comparison?currentExamId={id}")).Status);
    }

    // ---------- STORY-012's own endpoint keeps working and gains the optional measures ----------

    [Fact]
    public async Task The_story_012_save_still_works_unchanged_and_accepts_the_optional_measures_and_its_history_shows_teeth_and_links()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var old = await dentist.SendAsync(HttpMethod.Post, Charts() + "/charts", new { idempotencyKey = "v1", readings = new[] { new { toothKey = "16", site = "B", probingDepthMm = 3, recessionMm = 1, bleeding = true } } });
        Assert.Equal(HttpStatusCode.OK, old.Status);
        var withMore = await dentist.SendAsync(HttpMethod.Post, Charts() + "/charts", new { idempotencyKey = "v2", readings = new[] { Site("16", "B", 3, 1, true, true, false) } });
        Assert.Equal(HttpStatusCode.OK, withMore.Status);
        var reading = withMore.Body.GetProperty("readings")[0];
        Assert.Equal((true, false), (reading.GetProperty("suppuration").GetBoolean(), reading.GetProperty("plaque").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, old.Body.GetProperty("readings")[0].GetProperty("suppuration").ValueKind);               // not assessed stays not assessed
        var exams = (await dentist.SendAsync(HttpMethod.Get, Charts() + "/charts")).Body.GetProperty("exams");
        Assert.All(exams.EnumerateArray(), e => { Assert.Equal(0, e.GetProperty("teeth").GetArrayLength()); Assert.Equal(0, e.GetProperty("links").GetArrayLength()); });
    }

    private static class PerioStatus { public const string Draft = "Draft"; }
}
