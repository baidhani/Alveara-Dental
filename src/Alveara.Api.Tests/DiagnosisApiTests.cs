using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-013 at the HTTP boundary: who may read and who may record, correct or withdraw a diagnosis, the CSRF and row-version contract, the stable refusal shape that names every entry to correct, the
/// treatment-plan forward reference shown as unresolved and kept through every change, and the workflow end to end. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class DiagnosisApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _annEncounter, _boEncounter;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _annEncounter = await EncounterAsync(_ann);
        _boEncounter = await EncounterAsync(_bo);
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<Guid> EncounterAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private string Path(Guid? p = null) => $"/api/patients/{p ?? _ann}/diagnoses";
    private static string Id(JsonElement d) => d.GetProperty("id").GetString()!;
    private static string Version(JsonElement d) => d.GetProperty("rowVersion").GetString()!;
    private static string? Plan(JsonElement d) => d.GetProperty("treatmentPlanReference").GetString();

    private object Entry(string key, string label = "Chronic periodontitis", string? tooth = null, string? notes = null, string? plan = null, Guid? encounter = null) =>
        new { idempotencyKey = key, encounterId = encounter ?? _annEncounter, label, toothKey = tooth, notes, treatmentPlanReference = plan };

    private async Task<JsonElement> RecordAsync(Session by, string key = "k1", string label = "Chronic periodontitis", string? tooth = null, string? plan = null, Guid? patient = null, Guid? encounter = null)
    {
        var r = await by.SendAsync(HttpMethod.Post, Path(patient), Entry(key, label, tooth, null, plan, encounter ?? (patient == _bo ? _boEncounter : _annEncounter)));
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> Correct(Session by, JsonElement d, object fields, string? version = null) =>
        by.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/correct", Merge(new { rowVersion = version ?? Version(d) }, fields));

    private static Dictionary<string, object?> Merge(params object[] parts)
    {
        var map = new Dictionary<string, object?>();
        foreach (var p in parts) foreach (var prop in p.GetType().GetProperties()) map[prop.Name] = prop.GetValue(p);
        return map;
    }

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod, string)[]
        {
            (HttpMethod.Get, Path()), (HttpMethod.Post, Path()), (HttpMethod.Get, $"/api/diagnoses/{id}"), (HttpMethod.Get, $"/api/diagnoses/{id}/history"),
            (HttpMethod.Post, $"/api/diagnoses/{id}/correct"), (HttpMethod.Post, $"/api/diagnoses/{id}/withdraw"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_diagnoses_and_their_history(string role, bool allowed)
    {
        var d = await RecordAsync(await _api.SessionAsync("Dentist"));
        var by = await _api.SessionAsync(role);
        var reads = new[] { await by.SendAsync(HttpMethod.Get, Path()), await by.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}"), await by.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}/history") };
        Assert.All(reads, r => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_record_correct_and_withdraw(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist, "seed", plan: "plan-7");
        var by = await _api.SessionAsync(role);
        var before = await _s.CountAsync(db => db.Diagnoses);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Path(), Entry("by-role"))).Status,
            (await Correct(by, d, new { label = "Changed", reason = "why" })).Status,
            (await by.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/withdraw", new { rowVersion = Version(d), reason = "why" })).Status,
        };
        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            Assert.Equal(before, await _s.CountAsync(db => db.Diagnoses));                          // nothing was saved
            var now = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}")).Body;
            Assert.Equal(("Chronic periodontitis", "Active", "plan-7"), (now.GetProperty("label").GetString(), now.GetProperty("status").GetString(), Plan(now)));   // nothing was changed
        }
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist);
        Assert.NotEqual(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, Path(), Entry("no-csrf"), csrf: false)).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/correct", new { rowVersion = Version(d), label = "x", reason = "y" }, csrf: false)).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/withdraw", new { rowVersion = Version(d), reason = "y" }, csrf: false)).Status);
        Assert.Equal((1, "Active"), (await _s.CountAsync(db => db.Diagnoses), (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}")).Body.GetProperty("status").GetString()));
    }

    // ---------- the workflow: acceptance 1 and 3 ----------

    [Fact]
    public async Task A_diagnosis_is_recorded_linked_to_the_patient_and_encounter_with_a_plan_reference_then_corrected_withdrawn_and_every_step_is_in_its_history()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var d = await RecordAsync(dentist, "visit-1", "Caries", "16", "plan-2026-07");
        Assert.Equal((_ann.ToString(), _annEncounter.ToString(), "Caries", "16", "plan-2026-07", "Unresolved", "Active"),
            (d.GetProperty("patientId").GetString(), d.GetProperty("encounterId").GetString(), d.GetProperty("label").GetString(), d.GetProperty("toothKey").GetString(), Plan(d), d.GetProperty("treatmentPlanReferenceState").GetString(), d.GetProperty("status").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(d.GetProperty("recordedByName").GetString()));

        var list = (await hygienist.SendAsync(HttpMethod.Get, Path())).Body.GetProperty("diagnoses");
        Assert.Equal(Id(d), Id(list[0]));

        var c = await Correct(hygienist, d, new { label = "Deep caries", toothKey = "17", notes = (string?)null, reason = "Wrong tooth" });
        Assert.Equal(HttpStatusCode.OK, c.Status);
        Assert.Equal(("Deep caries", "17", "plan-2026-07"), (c.Body.GetProperty("label").GetString(), c.Body.GetProperty("toothKey").GetString(), Plan(c.Body)));        // the reference was left out, so it stays

        var w = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/withdraw", new { rowVersion = Version(c.Body), reason = "Entered on the wrong patient" });
        Assert.Equal(("Withdrawn", "Entered on the wrong patient", "plan-2026-07"), (w.Body.GetProperty("status").GetString(), w.Body.GetProperty("withdrawnReason").GetString(), Plan(w.Body)));

        var history = (await hygienist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}/history")).Body.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(new[] { "Recorded", "Corrected", "Withdrawn" }, history.Select(v => v.GetProperty("changeType").GetString()!));
        Assert.All(history, v => { Assert.Equal("plan-2026-07", Plan(v)); Assert.Equal("Unresolved", v.GetProperty("treatmentPlanReferenceState").GetString()); Assert.False(string.IsNullOrWhiteSpace(v.GetProperty("actorName").GetString())); });
        Assert.Equal(0, (await dentist.SendAsync(HttpMethod.Get, Path())).Body.GetProperty("diagnoses").GetArrayLength());                               // withdrawn is not in the working list
        Assert.Equal(1, (await dentist.SendAsync(HttpMethod.Get, Path() + "?includeWithdrawn=true")).Body.GetProperty("diagnoses").GetArrayLength());
    }

    [Fact]
    public async Task The_save_is_logged_with_the_user_and_the_log_holds_nothing_that_was_diagnosed()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist, "k", "Chronic periodontitis", "36", "plan-secret-7");
        await Correct(dentist, d, new { label = "Gingivitis", toothKey = "36", reason = "Rechecked" });
        await using var db = _fixture.CreateContext();
        var entries = db.AuditLogEntries.Where(a => a.EntityType == "Diagnosis").OrderBy(a => a.TimestampUtc).ToList();
        Assert.Equal(new[] { "DiagnosisRecorded", "DiagnosisCorrected" }, entries.Select(a => a.EventType));
        Assert.All(entries, a => { Assert.NotNull(a.PerformedByUserAccountId); Assert.NotEqual(default, a.TimestampUtc); foreach (var secret in new[] { "periodontitis", "Gingivitis", "36", "plan-secret-7" }) Assert.DoesNotContain(secret, a.Details); });
    }

    // ---------- the treatment-plan forward reference ----------

    [Fact]
    public async Task The_reference_is_replaced_only_when_asked_and_removed_only_by_an_explicit_clear()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist, plan: "plan-7");
        var replaced = await Correct(dentist, d, new { label = "Chronic periodontitis", reason = "Wrong plan", treatmentPlanReference = "  plan-8 " });
        Assert.Equal("plan-8", Plan(replaced.Body));
        var both = await Correct(dentist, replaced.Body, new { label = "Chronic periodontitis", reason = "x", treatmentPlanReference = "plan-9", clearTreatmentPlanReference = true });
        Assert.Equal((HttpStatusCode.BadRequest, "treatmentPlanReference:conflict"), (both.Status, $"{both.Body.GetProperty("problems")[0].GetProperty("field").GetString()}:{both.Body.GetProperty("problems")[0].GetProperty("code").GetString()}"));
        var cleared = await Correct(dentist, replaced.Body, new { label = "Chronic periodontitis", reason = "Entered by mistake", clearTreatmentPlanReference = true });
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (cleared.Body.GetProperty("treatmentPlanReference").ValueKind, cleared.Body.GetProperty("treatmentPlanReferenceState").ValueKind));
        var history = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}/history")).Body.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("treatmentPlanReference").GetString()).ToList();
        Assert.Equal(new string?[] { "plan-7", "plan-8", null }, history);                          // every value it ever had is in the history
    }

    [Fact]
    public async Task A_reference_is_never_looked_up_so_one_that_names_nothing_is_accepted_and_shown_unresolved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist, plan: "00000000-0000-0000-0000-000000000001");
        Assert.Equal(("00000000-0000-0000-0000-000000000001", "Unresolved"), (Plan(d), d.GetProperty("treatmentPlanReferenceState").GetString()));
    }

    // ---------- acceptance 2 over HTTP: incorrect data is refused and the person is told what to correct ----------

    [Fact]
    public async Task Incorrect_data_is_a_400_listing_every_problem_and_nothing_is_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var r = await dentist.SendAsync(HttpMethod.Post, Path(), new { idempotencyKey = "k", encounterId = _annEncounter, label = "", toothKey = "19", notes = "bell\a", treatmentPlanReference = "   " });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        var problems = r.Body.GetProperty("problems").EnumerateArray().Select(p => $"{p.GetProperty("field").GetString()}:{p.GetProperty("code").GetString()}").ToList();
        Assert.Equal(new[] { "label:required", "toothKey:unknown_tooth", "notes:invalid_characters", "treatmentPlanReference:blank" }, problems);
        Assert.All(r.Body.GetProperty("problems").EnumerateArray(), p => Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("message").GetString())));
        Assert.Equal(0, await _s.CountAsync(db => db.Diagnoses));
    }

    [Fact]
    public async Task A_missing_encounter_or_key_or_body_is_a_400_with_nothing_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var noEncounter = await dentist.SendAsync(HttpMethod.Post, Path(), new { idempotencyKey = "k", label = "x" });
        Assert.Equal("encounterId", noEncounter.Body.GetProperty("problems")[0].GetProperty("field").GetString());
        var noKey = await dentist.SendAsync(HttpMethod.Post, Path(), new { encounterId = _annEncounter, label = "x" });
        Assert.Equal("idempotencyKey", noKey.Body.GetProperty("problems")[0].GetProperty("field").GetString());
        var wrongType = await dentist.SendAsync(HttpMethod.Post, Path(), new { idempotencyKey = "k", encounterId = "not-a-guid", label = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.Status);
        Assert.Equal(0, await _s.CountAsync(db => db.Diagnoses));
    }

    [Fact]
    public async Task An_encounter_of_another_patient_is_a_404_the_same_as_one_that_does_not_exist_and_nothing_is_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        foreach (var encounter in new[] { _boEncounter, Guid.NewGuid() })
        {
            var r = await dentist.SendAsync(HttpMethod.Post, Path(), Entry($"k-{encounter}", encounter: encounter));
            Assert.Equal((HttpStatusCode.NotFound, "encounter_not_found"), (r.Status, Error(r.Body)));
        }
        var unknownPatient = await dentist.SendAsync(HttpMethod.Post, Path(Guid.NewGuid()), Entry("k"));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (unknownPatient.Status, Error(unknownPatient.Body)));
        Assert.Equal(0, await _s.CountAsync(db => db.Diagnoses));
    }

    [Fact]
    public async Task Recording_twice_with_the_same_key_is_200_both_times_with_one_diagnosis_and_a_different_entry_under_it_is_409()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var first = await RecordAsync(dentist, "visit-1", plan: "plan-7");
        var again = await RecordAsync(dentist, "visit-1", plan: "plan-7");
        Assert.Equal(Id(first), Id(again));
        var other = await dentist.SendAsync(HttpMethod.Post, Path(), Entry("visit-1", "Something else"));
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (other.Status, Error(other.Body)));
        Assert.Equal(1, await _s.CountAsync(db => db.Diagnoses));
    }

    [Fact]
    public async Task A_correction_or_withdrawal_needs_a_reason_and_a_row_version_and_a_withdrawn_diagnosis_cannot_be_corrected()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var d = await RecordAsync(dentist, plan: "plan-7");
        var noReason = await Correct(dentist, d, new { label = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "reason:required"), (noReason.Status, $"{noReason.Body.GetProperty("problems")[0].GetProperty("field").GetString()}:{noReason.Body.GetProperty("problems")[0].GetProperty("code").GetString()}"));
        var noVersion = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/correct", new { label = "x", reason = "y" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var noWithdrawReason = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/withdraw", new { rowVersion = Version(d) });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noWithdrawReason.Status, Error(noWithdrawReason.Body)));

        var w = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(d)}/withdraw", new { rowVersion = Version(d), reason = "Wrong patient" });
        var corrected = await Correct(dentist, w.Body, new { label = "x", reason = "y" });
        Assert.Equal((HttpStatusCode.Conflict, "diagnosis_withdrawn"), (corrected.Status, Error(corrected.Body)));
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task A_stale_correction_is_the_409_conflict_and_the_first_change_stands()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var d = await RecordAsync(dentist);
        Assert.Equal(HttpStatusCode.OK, (await Correct(dentist, d, new { label = "First change", reason = "r" })).Status);
        var stale = await Correct(hygienist, d, new { label = "Second change", reason = "r" });                // the hygienist still holds the first version
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        Assert.Equal("First change", (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{Id(d)}")).Body.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Two_people_correcting_the_same_version_at_the_same_moment_one_gets_200_and_the_other_409()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var d = await RecordAsync(dentist);
        var results = await Task.WhenAll(Correct(dentist, d, new { label = "From the dentist", reason = "r" }), Correct(hygienist, d, new { label = "From the hygienist", reason = "r" }));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, results.Select(r => r.Status).OrderBy(x => x).ToArray());
        Assert.Equal(2, await _s.CountAsync(db => db.DiagnosisVersions));
    }

    // ---------- reading ----------

    [Fact]
    public async Task Diagnoses_are_listed_per_patient_and_can_be_narrowed_to_an_encounter_and_an_unknown_patient_is_404()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var second = await EncounterAsync(_ann);
        await RecordAsync(dentist, "a", "First");
        await RecordAsync(dentist, "b", "Second", encounter: second);
        await RecordAsync(dentist, "c", "Bo's", patient: _bo);
        Assert.Equal(2, (await dentist.SendAsync(HttpMethod.Get, Path())).Body.GetProperty("diagnoses").GetArrayLength());
        var narrowed = (await dentist.SendAsync(HttpMethod.Get, Path() + $"?encounterId={second}")).Body.GetProperty("diagnoses");
        Assert.Equal("Second", Assert.Single(narrowed.EnumerateArray()).GetProperty("label").GetString());
        Assert.Equal(0, (await dentist.SendAsync(HttpMethod.Get, Path() + $"?encounterId={_boEncounter}")).Body.GetProperty("diagnoses").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, Path(Guid.NewGuid()))).Status);
    }
}
