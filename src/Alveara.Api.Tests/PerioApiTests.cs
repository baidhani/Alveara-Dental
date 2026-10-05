using System.Net;
using System.Text.Json;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-012 at the HTTP boundary: who may read and save periodontal charts, the CSRF and idempotency contract, the stable refusal shape that lists every entry to correct, and the
/// workflow end to end. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class PerioApiTests : IAsyncLifetime
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

    private string ChartsOf(Guid? patient = null) => $"/api/patients/{patient ?? _ann}/periodontal/charts";
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static object Site(string tooth = "16", string site = "B", int? pd = 3, int? rec = 1, bool? bleeding = true) => new { toothKey = tooth, site, probingDepthMm = pd, recessionMm = rec, bleeding };
    private static object Chart(string key, params object[] readings) => new { idempotencyKey = key, readings };

    private Task<(HttpStatusCode Status, JsonElement Body)> Save(Session by, object body, Guid? patient = null) => by.SendAsync(HttpMethod.Post, ChartsOf(patient), body);

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_both_endpoints()
    {
        var anon = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Get, ChartsOf()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Post, ChartsOf()))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_charts(string role, bool allowed)
    {
        await Save(await _api.SessionAsync("Dentist"), Chart("v1", Site()));
        var r = await (await _api.SessionAsync(role)).SendAsync(HttpMethod.Get, ChartsOf());
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_save_a_chart(string role, bool allowed)
    {
        var r = await Save(await _api.SessionAsync(role), Chart("v1", Site()));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status);
        Assert.Equal(allowed ? 1 : 0, await _s.CountAsync(db => db.PerioExams));         // a refused role leaves nothing behind
    }

    [Fact]
    public async Task Saving_without_a_csrf_token_is_refused_and_nothing_is_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var r = await dentist.SendAsync(HttpMethod.Post, ChartsOf(), Chart("v1", Site()), csrf: false);
        Assert.NotEqual(HttpStatusCode.OK, r.Status);
        Assert.Equal(0, await _s.CountAsync(db => db.PerioExams));
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_chart_is_saved_and_read_back_with_who_took_it_and_the_derived_attachment_loss()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var saved = await Save(dentist, Chart("v1", Site("36", "ML", 7, 2, true), Site("36", "B", 3, 0, false)));
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(2, saved.Body.GetProperty("readingCount").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(saved.Body.GetProperty("recordedByName").GetString()));

        var list = (await (await _api.SessionAsync("Hygienist")).SendAsync(HttpMethod.Get, ChartsOf())).Body;
        var exam = list.GetProperty("exams").EnumerateArray().Single();
        Assert.Equal(saved.Body.GetProperty("id").GetString(), exam.GetProperty("id").GetString());
        var ml = exam.GetProperty("readings").EnumerateArray().Single(x => x.GetProperty("site").GetString() == "ML");
        Assert.Equal((7, 2, 9, true), (ml.GetProperty("probingDepthMm").GetInt32(), ml.GetProperty("recessionMm").GetInt32(), ml.GetProperty("attachmentLossMm").GetInt32(), ml.GetProperty("bleeding").GetBoolean()));
    }

    [Fact]
    public async Task Saving_twice_with_the_same_key_is_200_both_times_and_makes_one_chart_while_a_different_chart_under_it_is_409()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var first = await Save(dentist, Chart("v1", Site()));
        var again = await Save(dentist, Chart("v1", Site()));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.Status, again.Status));
        Assert.Equal(first.Body.GetProperty("id").GetString(), again.Body.GetProperty("id").GetString());
        var other = await Save(dentist, Chart("v1", Site(pd: 9)));
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (other.Status, Error(other.Body)));
        Assert.Equal(1, await _s.CountAsync(db => db.PerioExams));
    }

    [Fact]
    public async Task Charts_are_per_patient()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await Save(dentist, Chart("v1", Site()));
        Assert.Equal(0, (await dentist.SendAsync(HttpMethod.Get, ChartsOf(_bo))).Body.GetProperty("exams").GetArrayLength());
    }

    // ---------- acceptance 2 over HTTP: incorrect data is rejected and the person is told what to correct ----------

    [Fact]
    public async Task Incorrect_data_is_a_400_listing_every_entry_to_correct_and_nothing_is_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var r = await Save(dentist, Chart("v1", Site("16", "B", 99, -1), Site("19", "B"), Site("17", "Q"), Site("55", "B"), Site("26", "B")));
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        var problems = r.Body.GetProperty("problems").EnumerateArray().ToList();
        Assert.Equal(new[] { "out_of_range", "out_of_range", "unknown_tooth", "unknown_site", "primary_tooth" }, problems.Select(p => p.GetProperty("code").GetString()!));
        var first = problems[0];
        Assert.Equal(("16", "B", "probingDepthMm"), (first.GetProperty("toothKey").GetString(), first.GetProperty("site").GetString(), first.GetProperty("field").GetString()));
        Assert.Contains("0 to 15 mm", first.GetProperty("message").GetString());
        Assert.Equal(0, await _s.CountAsync(db => db.PerioExams));
        Assert.Equal(0, await _s.CountAsync(db => db.PerioReadings));
    }

    [Fact]
    public async Task A_value_left_out_is_reported_never_defaulted_to_zero_or_no_and_is_listed_with_the_other_problems()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var r = await Save(dentist, Chart("v1", Site("16", "B", pd: null, rec: null, bleeding: null), Site("17", "B", pd: 40)));
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        Assert.Equal(new[] { "probingDepthMm", "recessionMm", "bleeding", "probingDepthMm" }, r.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("field").GetString()!));
        Assert.Equal(0, await _s.CountAsync(db => db.PerioExams));
    }

    [Fact]
    public async Task A_missing_readings_list_or_a_null_reading_or_a_missing_key_is_a_400_with_nothing_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var none = await Save(dentist, new { idempotencyKey = "v1" });
        Assert.Equal((HttpStatusCode.BadRequest, "required"), (none.Status, none.Body.GetProperty("problems")[0].GetProperty("code").GetString()));
        var nullEntry = await Save(dentist, new { idempotencyKey = "v1", readings = new object?[] { null } });
        Assert.Equal(HttpStatusCode.BadRequest, nullEntry.Status);
        var noKey = await Save(dentist, new { readings = new[] { Site() } });
        Assert.Equal("idempotencyKey", noKey.Body.GetProperty("problems")[0].GetProperty("field").GetString());
        Assert.Equal(0, await _s.CountAsync(db => db.PerioExams));
    }

    [Fact]
    public async Task A_body_that_is_not_the_right_shape_is_a_400_and_an_unknown_patient_is_a_404()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var wrongType = await Save(dentist, new { idempotencyKey = "v1", readings = new[] { new { toothKey = "16", site = "B", probingDepthMm = "deep", recessionMm = 1, bleeding = true } } });
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.Status);
        var unknown = await Save(dentist, Chart("v1", Site()), patient: Guid.NewGuid());
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (unknown.Status, Error(unknown.Body)));
        Assert.Equal(0, await _s.CountAsync(db => db.PerioExams));
    }

    [Fact]
    public async Task The_save_is_logged_with_the_user_and_the_log_holds_no_tooth_or_measurement()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await Save(dentist, Chart("v1", Site("47", "DL", 11, 4, true)));
        await using var db = _fixture.CreateContext();
        var entry = Assert.Single(db.AuditLogEntries.Where(a => a.EntityType == "PerioExam").ToList());
        Assert.Equal("PerioExamRecorded", entry.EventType);
        Assert.NotNull(entry.PerformedByUserAccountId);
        Assert.NotEqual(default, entry.TimestampUtc);
        Assert.DoesNotContain("47", entry.Details);
    }
}
