using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-016 at the HTTP boundary, against a real API, real SQL Server and a real signed-in session per role: only the dentist and the administrator may check and save a prescription (with a CSRF token), the
/// clinical team reads, everyone else is refused; the error shapes the screen relies on (every problem named, the allergy assessment, the review that is required); a replay; a conflicting reuse of the key; the
/// 503 that saves nothing when the log cannot be written; and what an unauthenticated caller can reach (nothing).
/// </summary>
public class PrescriptionApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;
    private string _patient = "";

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _patient = _ann.ToString();
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static object Body(string medication = "Ibuprofen", decimal dose = 400m, string unit = "mg", string frequency = "Three times daily", string? note = null, string? key = null) =>
        new { idempotencyKey = key ?? $"rx-{Guid.NewGuid():N}", medication, doseValue = dose, doseUnit = unit, frequency, reviewNote = note };

    private async Task<int> RowsAsync() { await using var db = _fixture.CreateContext(); return await db.Prescriptions.CountAsync(); }
    private async Task<int> AuditAsync() { await using var db = _fixture.CreateContext(); return await db.AuditLogEntries.CountAsync(a => a.EventType == "PrescriptionRecorded"); }

    private async Task AllergyListAsync(Session dentist, string state = "NoneKnown", params (string Name, string? Severity)[] allergies)
    {
        foreach (var (name, severity) in allergies)
            Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/clinical-record/items", new { kind = "Allergy", name, reaction = "Hives", severity })).Status);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Put, $"/api/patients/{_patient}/clinical-record/sections/Allergy/review", new { state })).Status);
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_route()
    {
        var anon = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/patients/{_patient}/prescriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/prescriptions/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions/check"))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_dentists_and_administrators_can_check_and_save_a_prescription(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var by = await _api.SessionAsync(role);
        var before = (await RowsAsync(), await AuditAsync());

        var check = await by.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions/check", Body());
        var save = await by.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body());

        if (allowed) Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (check.Status, save.Status));
        else
        {
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (check.Status, save.Status));
            Assert.Equal(before, (await RowsAsync(), await AuditAsync()));
        }
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task The_clinical_team_reads_prescriptions_and_the_options_and_everyone_else_is_refused(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var expected = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await by.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/prescriptions")).Status);
        Assert.Equal(expected, (await by.SendAsync(HttpMethod.Get, "/api/prescriptions/options")).Status);
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_saves_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body(), csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions/check", Body(), csrf: false)).Status);
        Assert.Equal((0, 0), (await RowsAsync(), await AuditAsync()));
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_correct_prescription_is_saved_listed_and_logged_and_a_retry_replays_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist, "Reviewed", ("Latex", "Mild"));
        var body = Body("Ibuprofen 400", 400m, key: "once");

        var saved = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", body);
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        var p = saved.Body.GetProperty("prescription");
        Assert.Equal((false, "Ibuprofen 400", "mg", "Three times daily", "NoRecordedMatch", "Established"),
            (saved.Body.GetProperty("replayed").GetBoolean(), p.GetProperty("medication").GetString(), p.GetProperty("doseUnit").GetString(), p.GetProperty("frequency").GetString(),
             p.GetProperty("allergyOutcome").GetString(), p.GetProperty("allergyListState").GetString()));

        var again = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", body);
        Assert.Equal((HttpStatusCode.OK, true, p.GetProperty("id").GetString()), (again.Status, again.Body.GetProperty("replayed").GetBoolean(), again.Body.GetProperty("prescription").GetProperty("id").GetString()));
        Assert.Equal((1, 1), (await RowsAsync(), await AuditAsync()));

        var list = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/prescriptions")).Body.GetProperty("prescriptions");
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(p.GetProperty("id").GetString(), list[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_incorrect_entry_is_refused_with_every_problem_named_and_nothing_is_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body("A", 99999m, "stone", "whenever"));
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        Assert.Equal(["medication", "doseUnit", "frequency"], r.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("field").GetString()));
        var slip = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body("Ibuprofen", 5001m, "mg"));
        Assert.Equal("above_typing_ceiling", slip.Body.GetProperty("problems")[0].GetProperty("code").GetString());
        Assert.Equal((0, 0), (await RowsAsync(), await AuditAsync()));
    }

    [Fact]
    public async Task A_matching_allergy_returns_the_assessment_and_the_review_it_needs_and_the_entry_saves_once_the_prescriber_reviews_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist, "Reviewed", ("Penicillin", "Severe"));
        var key = "pen-1";

        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body("Penicillin V Potassium", 500m, key: key));

        Assert.Equal((HttpStatusCode.Conflict, "allergy_review_required"), (refused.Status, Error(refused.Body)));
        var assessment = refused.Body.GetProperty("assessment");
        Assert.Equal(("RecordedAllergyMatch", "Penicillin"), (assessment.GetProperty("outcome").GetString(), assessment.GetProperty("matches")[0].GetProperty("name").GetString()));
        Assert.Contains("does not know drug families", assessment.GetProperty("limitation").GetString());
        Assert.Equal("reviewNote", refused.Body.GetProperty("problems")[0].GetProperty("field").GetString());
        Assert.Equal((0, 0), (await RowsAsync(), await AuditAsync()));

        var saved = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body("Penicillin V Potassium", 500m, note: "Childhood rash; tolerated since.", key: key));
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(("RecordedAllergyMatch", "Penicillin"), (saved.Body.GetProperty("prescription").GetProperty("allergyOutcome").GetString(), saved.Body.GetProperty("prescription").GetProperty("allergyMatches").GetString()));
        Assert.Equal((1, 1), (await RowsAsync(), await AuditAsync()));
    }

    [Fact]
    public async Task The_same_key_with_a_different_prescription_is_a_409_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body(key: "k"))).Status);
        var other = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", Body("Naproxen", key: "k"));
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (other.Status, Error(other.Body)));
        Assert.Equal((1, 1), (await RowsAsync(), await AuditAsync()));
    }

    [Fact]
    public async Task An_unknown_patient_is_a_404_for_every_prescription_route()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{id}/prescriptions")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{id}/prescriptions", Body())).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{id}/prescriptions/check", Body())).Status);
    }

    [Fact]
    public async Task The_check_route_reports_the_problems_the_allergy_result_and_the_limitations_and_saves_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist, "Reviewed", ("Penicillin", "Severe"));
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions/check", Body("Penicillin", 6000m, "mg"));
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("doseValue", r.Body.GetProperty("problems")[0].GetProperty("field").GetString());
        Assert.Equal("RecordedAllergyMatch", r.Body.GetProperty("assessment").GetProperty("outcome").GetString());
        Assert.True(r.Body.GetProperty("reviewRequired").GetBoolean());
        Assert.Contains("not checked against any clinical dose range", r.Body.GetProperty("doseLimitation").GetString());
        Assert.Contains("not a clearance", r.Body.GetProperty("allergyLimitation").GetString());
        Assert.Equal((0, 0), (await RowsAsync(), await AuditAsync()));
    }

    [Fact]
    public async Task The_options_list_the_units_frequencies_ceilings_and_both_limitations()
    {
        var o = (await (await _api.SessionAsync("Hygienist")).SendAsync(HttpMethod.Get, "/api/prescriptions/options")).Body;
        Assert.Equal(9, o.GetProperty("doseUnits").GetArrayLength());
        Assert.Equal(11, o.GetProperty("frequencies").GetArrayLength());
        Assert.Equal(5000m, o.GetProperty("typingCeilings").GetProperty("mg").GetDecimal());
        Assert.Contains("no drug-knowledge source", o.GetProperty("doseLimitation").GetString());
    }

    [Fact]
    public async Task When_the_log_cannot_be_written_the_answer_is_503_without_internals_and_nothing_is_saved_then_the_same_request_works()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        async Task Sql(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }
        await Sql(@"CREATE TRIGGER [TR_Test_RefusePrescriptionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'PrescriptionRecorded') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var body = Body(key: "retry");

        var failed = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", body);

        Assert.Equal(((HttpStatusCode)503, "save_failed"), (failed.Status, Error(failed.Body)));
        Assert.DoesNotContain("audit", failed.Body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", failed.Body.GetRawText());
        Assert.Equal((0, 0), (await RowsAsync(), await AuditAsync()));

        await Sql("DROP TRIGGER [TR_Test_RefusePrescriptionAudit]");
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions", body)).Status);
        Assert.Equal((1, 1), (await RowsAsync(), await AuditAsync()));
    }
}
