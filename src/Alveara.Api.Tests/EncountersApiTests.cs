using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-005 at the HTTP boundary: who may read and who may write clinical documentation, the CSRF / row-version / idempotency-key contract, and the stable refusal
/// shapes a client depends on. A real API, real SQL Server and a real signed-in session per role. The behaviour behind the endpoints is tested in
/// <see cref="EncounterServiceTests"/>; here the point is that each rule is reachable and enforced through the API.
/// </summary>
public class EncountersApiTests : IAsyncLifetime
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

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;
    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static JsonElement Section(JsonElement encounter, string kind) => encounter.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == kind);

    private static string EncountersOf(Guid patient) => $"/api/patients/{patient}/encounters";
    private static string EncounterUrl(JsonElement e, string tail = "") => $"/api/encounters/{Id(e)}{tail}";

    private async Task<JsonElement> StartAsync(Session by, Guid? patient = null, string? key = null, object? body = null)
    {
        var r = await by.SendAsync(HttpMethod.Post, EncountersOf(patient ?? _ann), body ?? new { }, key);
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body;
    }

    private async Task<JsonElement> AddAsync(Session by, JsonElement e, string kind, string name, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["kind"] = kind, ["name"] = name, ["rowVersion"] = Version(e) };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var r = await by.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), body);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private async Task<JsonElement> CompleteDraftAsync(Session clinician)
    {
        var e = await StartAsync(clinician);
        e = await AddAsync(clinician, e, "MedicalHistory", "Hypertension");
        e = await AddAsync(clinician, e, "DentalHistory", "Root canal 2019");
        e = await AddAsync(clinician, e, "Allergy", "Penicillin", new { reaction = "Hives", severity = "Moderate" });
        e = await AddAsync(clinician, e, "Medication", "Lisinopril", new { dose = "10 mg", frequency = "daily" });
        return e;
    }

    private async Task<JsonElement> FinalizeAsync(Session by, JsonElement e)
    {
        var r = await by.SendAsync(HttpMethod.Post, EncounterUrl(e, "/finalize"), new { rowVersion = Version(e) });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_clinical_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, EncountersOf(_ann)), (HttpMethod.Post, EncountersOf(_ann)), (HttpMethod.Get, $"/api/encounters/{id}"),
            (HttpMethod.Post, $"/api/encounters/{id}/entries"), (HttpMethod.Put, $"/api/encounters/{id}/entries/{id}"), (HttpMethod.Post, $"/api/encounters/{id}/entries/{id}/remove"),
            (HttpMethod.Post, $"/api/encounters/{id}/sections/Allergy/none-reported"), (HttpMethod.Post, $"/api/encounters/{id}/sections/Allergy/clear-review"),
            (HttpMethod.Post, $"/api/encounters/{id}/finalize"), (HttpMethod.Post, $"/api/encounters/{id}/addenda"),
        };
        foreach (var (method, url) in calls)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_clinical_documentation(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var e = await CompleteDraftAsync(dentist);
        var by = await _api.SessionAsync(role);

        var list = await by.SendAsync(HttpMethod.Get, EncountersOf(_ann));
        var one = await by.SendAsync(HttpMethod.Get, EncounterUrl(e));
        if (allowed)
        {
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (list.Status, one.Status));
            Assert.Equal(Id(e), list.Body[0].GetProperty("id").GetString());
            Assert.Equal("Penicillin", Section(one.Body, "Allergy").GetProperty("entries")[0].GetProperty("name").GetString());
        }
        else
        {
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (list.Status, one.Status));
        }
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_document(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var finalized = await FinalizeAsync(dentist, await CompleteDraftAsync(dentist));
        var draft = await StartAsync(dentist, _bo);
        var by = await _api.SessionAsync(role);
        var before = await _s.CountAsync(db => db.EncounterEntries);

        var attempts = new (HttpMethod Method, string Url, object? Body, string? Key)[]
        {
            (HttpMethod.Post, EncountersOf(_ann), new { }, null),
            (HttpMethod.Post, EncounterUrl(draft, "/entries"), new { kind = "Allergy", name = "Latex", rowVersion = Version(draft) }, null),
            (HttpMethod.Post, EncounterUrl(draft, "/sections/Medication/none-reported"), new { rowVersion = Version(draft) }, null),
            (HttpMethod.Post, EncounterUrl(draft, "/finalize"), new { rowVersion = Version(draft) }, null),
            (HttpMethod.Post, EncounterUrl(finalized, "/addenda"), new { text = "Addendum" }, $"add-{role}"),
        };
        var statuses = new List<HttpStatusCode>();
        foreach (var (method, url, body, key) in attempts) statuses.Add((await by.SendAsync(method, url, body, key)).Status);

        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            Assert.Equal(before, await _s.CountAsync(db => db.EncounterEntries));                 // nothing was stored
            Assert.Equal(0, await _s.CountAsync(db => db.EncounterAddenda));
            Assert.Equal(1, (await dentist.SendAsync(HttpMethod.Get, EncounterUrl(draft))).Body.GetProperty("history").GetArrayLength()); // the draft was not touched
        }
    }

    // ---------- the whole workflow over HTTP ----------

    [Fact]
    public async Task A_clinician_documents_finalizes_and_amends_an_encounter_and_the_team_reads_it_with_the_original_preserved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var assistant = await _api.SessionAsync("Assistant");

        var draft = await CompleteDraftAsync(dentist);
        Assert.True(draft.GetProperty("isComplete").GetBoolean());
        var finalized = await FinalizeAsync(hygienist, draft);   // another clinician may finalize the note
        Assert.Equal("Finalized", finalized.GetProperty("status").GetString());

        var changeRefused = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized, "/entries"), new { kind = "Allergy", name = "Latex", rowVersion = Version(finalized) });
        Assert.Equal((HttpStatusCode.Conflict, "encounter_finalized"), (changeRefused.Status, Error(changeRefused.Body)));

        var addendum = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized, "/addenda"), new { text = "Patient also reports a latex sensitivity." }, "addendum-1");
        Assert.Equal(HttpStatusCode.Created, addendum.Status);

        var seen = (await assistant.SendAsync(HttpMethod.Get, EncounterUrl(finalized))).Body;
        Assert.Equal("Finalized", seen.GetProperty("status").GetString());
        Assert.Equal("Penicillin", Section(seen, "Allergy").GetProperty("entries")[0].GetProperty("name").GetString()); // the original, untouched
        Assert.Single(Section(seen, "Allergy").GetProperty("entries").EnumerateArray());
        Assert.Equal("Patient also reports a latex sensitivity.", seen.GetProperty("addenda")[0].GetProperty("text").GetString());
        Assert.Equal(Version(finalized), Version(seen));
    }

    [Fact]
    public async Task Finalizing_an_incomplete_encounter_is_a_409_naming_every_section_that_needs_attention_and_a_none_reported_review_completes_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var e = await AddAsync(dentist, await StartAsync(dentist), "MedicalHistory", "Asthma");

        var refused = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/finalize"), new { rowVersion = Version(e) });
        Assert.Equal((HttpStatusCode.Conflict, "documentation_incomplete"), (refused.Status, Error(refused.Body)));
        var fields = refused.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "Allergy", "DentalHistory", "Medication" }, fields);

        foreach (var kind in new[] { "DentalHistory", "Allergy", "Medication" })
        {
            var marked = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, $"/sections/{kind}/none-reported"), new { rowVersion = Version(e) });
            Assert.Equal(HttpStatusCode.OK, marked.Status);
            e = marked.Body;
        }
        Assert.Equal("NoneReported", Section(e, "Allergy").GetProperty("status").GetString());
        Assert.Equal("Finalized", (await FinalizeAsync(dentist, e)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_entry_can_be_changed_and_removed_over_the_API_and_a_removed_entry_leaves_the_chart()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var e = await AddAsync(dentist, await StartAsync(dentist), "Medication", "Lisinopril", new { dose = "10 mg", frequency = "daily" });
        var entryId = Section(e, "Medication").GetProperty("entries")[0].GetProperty("id").GetString();

        var changed = await dentist.SendAsync(HttpMethod.Put, EncounterUrl(e, $"/entries/{entryId}"), new { name = "Lisinopril", dose = "20 mg", frequency = "daily", rowVersion = Version(e) });
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal("20 mg", Section(changed.Body, "Medication").GetProperty("entries")[0].GetProperty("dose").GetString());

        var removed = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, $"/entries/{entryId}/remove"), new { rowVersion = Version(changed.Body) });
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Empty(Section(removed.Body, "Medication").GetProperty("entries").EnumerateArray());
        Assert.Equal("Empty", Section(removed.Body, "Medication").GetProperty("status").GetString());
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterEntries)); // kept, not deleted
    }

    // ---------- the contract: CSRF, row version, concurrency, idempotency keys, stable error shapes ----------

    [Fact]
    public async Task Every_change_needs_a_csrf_token_and_the_row_version_the_caller_read()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var e = await StartAsync(dentist);
        var entry = new { kind = "Allergy", name = "Penicillin", rowVersion = Version(e) };

        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, EncountersOf(_ann), new { }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), entry, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/finalize"), new { rowVersion = Version(e) }, csrf: false)).Status);

        var noVersion = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), new { kind = "Allergy", name = "Penicillin" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var badVersion = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), new { kind = "Allergy", name = "Penicillin", rowVersion = "not-base64!" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_invalid"), (badVersion.Status, Error(badVersion.Body)));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterEntries));
    }

    [Fact]
    public async Task A_stale_editor_gets_the_shared_409_concurrency_conflict_and_the_other_editors_change_stands()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var e = await StartAsync(dentist);
        await AddAsync(dentist, e, "Allergy", "Penicillin");

        var stale = await hygienist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), new { kind = "Medication", name = "Aspirin", rowVersion = Version(e) });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        Assert.Equal("Encounter", stale.Body.GetProperty("entityType").GetString());

        var current = (await dentist.SendAsync(HttpMethod.Get, EncounterUrl(e))).Body;
        Assert.Equal(new[] { "Penicillin" }, current.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("entries").EnumerateArray()).Select(x => x.GetProperty("name").GetString()!));
    }

    [Fact]
    public async Task Starting_with_the_same_Idempotency_Key_or_for_the_same_appointment_returns_the_same_encounter()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var first = await StartAsync(dentist, key: "start-1");
        var retry = await dentist.SendAsync(HttpMethod.Post, EncountersOf(_ann), new { }, "start-1");
        Assert.Equal((HttpStatusCode.OK, Id(first)), (retry.Status, Id(retry.Body)));

        var appointment = (await _s.ScheduleAsync(_s.Request(_ann, At(9)))).Appointment.Id;
        var linked = await StartAsync(dentist, body: new { appointmentId = appointment });
        var again = await dentist.SendAsync(HttpMethod.Post, EncountersOf(_ann), new { appointmentId = appointment });
        Assert.Equal((HttpStatusCode.OK, Id(linked)), (again.Status, Id(again.Body)));
        Assert.Equal(2, await _s.CountAsync(db => db.Encounters));

        var other = await dentist.SendAsync(HttpMethod.Post, EncountersOf(_bo), new { appointmentId = appointment }); // Ann's appointment, Bo's chart
        Assert.Equal((HttpStatusCode.Conflict, "appointment_patient_mismatch"), (other.Status, Error(other.Body)));
    }

    [Fact]
    public async Task An_addendum_needs_an_Idempotency_Key_and_a_retry_returns_the_first_one()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var finalized = await FinalizeAsync(dentist, await CompleteDraftAsync(dentist));

        var noKey = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized, "/addenda"), new { text = "Note" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (noKey.Status, Error(noKey.Body)));
        Assert.True(noKey.Body.GetProperty("fieldErrors").TryGetProperty("clientKey", out _));

        var first = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized, "/addenda"), new { text = "Note" }, "k1");
        var retry = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized, "/addenda"), new { text = "Note" }, "k1");
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.Status, retry.Status));
        Assert.Equal(1, retry.Body.GetProperty("addenda").GetArrayLength());

        var reused = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized, "/addenda"), new { text = "Different" }, "k1");
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (reused.Status, Error(reused.Body)));
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterAddenda));
    }

    [Fact]
    public async Task Refusals_use_stable_codes_and_name_the_fields_that_need_attention()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var e = await StartAsync(dentist);

        var badSeverity = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), new { kind = "Allergy", name = "Penicillin", severity = "Critical", rowVersion = Version(e) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (badSeverity.Status, Error(badSeverity.Body)));
        Assert.True(badSeverity.Body.GetProperty("fieldErrors").TryGetProperty("severity", out _));

        var wrongField = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), new { kind = "Medication", name = "Aspirin", reaction = "Rash", rowVersion = Version(e) });
        Assert.True(wrongField.Body.GetProperty("fieldErrors").TryGetProperty("reaction", out _));

        var noName = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/entries"), new { kind = "Allergy", name = "  ", rowVersion = Version(e) });
        Assert.True(noName.Body.GetProperty("fieldErrors").TryGetProperty("name", out _));

        var unknownSection = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(e, "/sections/Vitals/none-reported"), new { rowVersion = Version(e) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (unknownSection.Status, Error(unknownSection.Body)));

        var unknown = await dentist.SendAsync(HttpMethod.Get, $"/api/encounters/{Guid.NewGuid()}");
        Assert.Equal((HttpStatusCode.NotFound, "encounter_not_found"), (unknown.Status, Error(unknown.Body)));
        var noPatient = await dentist.SendAsync(HttpMethod.Get, EncountersOf(Guid.NewGuid()));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (noPatient.Status, Error(noPatient.Body)));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterEntries));
    }

    [Fact]
    public async Task A_patients_list_holds_only_that_patients_encounters()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var anns = await StartAsync(dentist, _ann);
        var bos = await StartAsync(dentist, _bo);

        var annList = (await dentist.SendAsync(HttpMethod.Get, EncountersOf(_ann))).Body;
        var boList = (await dentist.SendAsync(HttpMethod.Get, EncountersOf(_bo))).Body;
        Assert.Equal(new[] { Id(anns) }, annList.EnumerateArray().Select(x => x.GetProperty("id").GetString()!));
        Assert.Equal(new[] { Id(bos) }, boList.EnumerateArray().Select(x => x.GetProperty("id").GetString()!));
    }

    // ---------- trust: the work done over HTTP is audited with the user and a time, without clinical content ----------

    [Fact]
    public async Task Every_change_made_through_the_API_is_in_the_audit_log_with_the_user_and_time_and_no_clinical_text()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var finalized = await FinalizeAsync(dentist, await CompleteDraftAsync(dentist));
        await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized, "/addenda"), new { text = "Patient confirms the allergy list." }, "audit-1");

        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(Encounter) && a.TargetUserAccountId == Guid.Parse(Id(finalized))).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(new[] { "EncounterStarted", "EncounterEntryAdded", "EncounterEntryAdded", "EncounterEntryAdded", "EncounterEntryAdded", "EncounterFinalized", "EncounterAddendumAdded" }, audit.Select(a => a.EventType));
        Assert.Single(audit.Select(a => a.PerformedByUserAccountId).Distinct());
        Assert.NotNull(audit[0].PerformedByUserAccountId);
        Assert.All(audit, a => Assert.True(a.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(-5)));
        var text = string.Join(" ", audit.Select(a => a.Details));
        foreach (var secret in new[] { "Penicillin", "Hypertension", "Lisinopril", "Hives", "Patient confirms" }) Assert.DoesNotContain(secret, text);
    }
}
