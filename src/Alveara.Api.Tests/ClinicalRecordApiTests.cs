using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-005-C01 at the HTTP boundary: the longitudinal record, notes, vitals, templates and signing - who may read and write each, the CSRF / row-version / idempotency-key
/// contract and the stable refusal shapes a client depends on. A real API, real SQL Server and a real signed-in session per role. The behaviour behind the endpoints is
/// tested in <see cref="ClinicalRecordServiceTests"/> and <see cref="EncounterNotesServiceTests"/>.
/// </summary>
public class ClinicalRecordApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private string RecordOf(Guid? patient = null) => $"/api/patients/{patient ?? _ann}/clinical-record";
    private static JsonElement Section(JsonElement record, string kind) => record.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == kind);
    private static JsonElement Item(JsonElement record, string kind, string name) => Section(record, kind).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == name);

    private async Task<JsonElement> AddItemAsync(Session by, string kind, string name, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["kind"] = kind, ["name"] = name };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var r = await by.SendAsync(HttpMethod.Post, RecordOf() + "/items", body);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private async Task<JsonElement> StartEncounterAsync(Session by)
    {
        var r = await by.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/encounters", new { }, $"start-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body;
    }

    private static string EncounterUrl(JsonElement e, string tail = "") => $"/api/encounters/{e.GetProperty("id").GetString()}{tail}";

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_new_clinical_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, RecordOf()), (HttpMethod.Get, $"/api/clinical-record/items/{id}/history"), (HttpMethod.Post, RecordOf() + "/items"), (HttpMethod.Put, $"/api/clinical-record/items/{id}"),
            (HttpMethod.Post, $"/api/clinical-record/items/{id}/status"), (HttpMethod.Post, $"/api/clinical-record/items/{id}/remove"), (HttpMethod.Put, RecordOf() + "/sections/Allergy/review"),
            (HttpMethod.Put, $"/api/encounters/{id}/notes/Plan"), (HttpMethod.Post, $"/api/encounters/{id}/template"), (HttpMethod.Post, $"/api/encounters/{id}/vitals"),
            (HttpMethod.Post, $"/api/encounters/{id}/vitals/{id}/void"), (HttpMethod.Post, $"/api/encounters/{id}/sign"), (HttpMethod.Post, $"/api/encounters/{id}/unsign"),
            (HttpMethod.Get, "/api/clinical/templates"), (HttpMethod.Get, $"/api/clinical/templates/{id}"), (HttpMethod.Post, "/api/clinical/templates"),
            (HttpMethod.Put, $"/api/clinical/templates/{id}"), (HttpMethod.Post, $"/api/clinical/templates/{id}/active"),
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
    public async Task Only_the_clinical_team_and_the_administrator_can_read_the_record_its_history_and_the_templates(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var record = await AddItemAsync(dentist, "Allergy", "Penicillin");
        var itemId = Item(record, "Allergy", "Penicillin").GetProperty("id").GetString();
        var by = await _api.SessionAsync(role);

        var responses = new[]
        {
            await by.SendAsync(HttpMethod.Get, RecordOf()), await by.SendAsync(HttpMethod.Get, $"/api/clinical-record/items/{itemId}/history"), await by.SendAsync(HttpMethod.Get, "/api/clinical/templates"),
        };
        if (allowed) Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        else Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_change_the_record_write_notes_record_vitals_and_sign(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var item = Item(await AddItemAsync(dentist, "Medication", "Lisinopril"), "Medication", "Lisinopril");
        var enc = await StartEncounterAsync(dentist);
        var by = await _api.SessionAsync(role);
        var before = await _s.CountAsync(db => db.ClinicalRecordItems);

        var attempts = new (HttpMethod Method, string Url, object? Body, string? Key)[]
        {
            (HttpMethod.Post, RecordOf() + "/items", new { kind = "Allergy", name = $"Latex-{role}" }, null),
            (HttpMethod.Post, $"/api/clinical-record/items/{item.GetProperty("id").GetString()}/status", new { status = "Inactive", rowVersion = item.GetProperty("rowVersion").GetString() }, null),
            (HttpMethod.Put, RecordOf() + "/sections/Allergy/review", new { state = "NoneKnown" }, null),
            (HttpMethod.Put, EncounterUrl(enc, "/notes/Plan"), new { body = "Plan", rowVersion = Version(enc) }, null),
            (HttpMethod.Post, EncounterUrl(enc, "/vitals"), new { pulseBpm = 70, rowVersion = Version(enc) }, $"v-{role}"),
            (HttpMethod.Post, EncounterUrl(enc, "/sign"), new { rowVersion = Version(enc) }, null),
        };
        var statuses = new List<HttpStatusCode>();
        foreach (var (method, url, body, key) in attempts) statuses.Add((await by.SendAsync(method, url, body, key)).Status);

        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            Assert.Equal(before, await _s.CountAsync(db => db.ClinicalRecordItems));
            Assert.Equal(0, await _s.CountAsync(db => db.ClinicalSectionReviews));
            Assert.Equal(0, await _s.CountAsync(db => db.EncounterNotes));
            Assert.Equal(0, await _s.CountAsync(db => db.EncounterVitals));
            Assert.Equal(1, (await dentist.SendAsync(HttpMethod.Get, EncounterUrl(enc))).Body.GetProperty("history").GetArrayLength()); // the draft was not touched
        }
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_the_dentist_and_the_administrator_can_configure_note_templates(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var body = new { name = $"SOAP-{role}", sections = new[] { new { section = "Plan", required = true, starterText = "Plan:" } } };
        var created = await by.SendAsync(HttpMethod.Post, "/api/clinical/templates", body);
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, created.Status);
        Assert.Equal(allowed ? 1 : 0, await _s.CountAsync(db => db.NoteTemplates));
        if (!allowed) Assert.Equal("ManageClinicalTemplates", created.Body.GetProperty("required").GetString());
    }

    [Fact]
    public async Task Taken_out_of_use_templates_are_listed_only_for_those_who_configure_templates()
    {
        var admin = await _api.SessionAsync("Admin");
        var hygienist = await _api.SessionAsync("Hygienist");
        var made = await admin.SendAsync(HttpMethod.Post, "/api/clinical/templates", new { name = "Old", sections = new[] { new { section = "Plan", required = false } } });
        await admin.SendAsync(HttpMethod.Post, $"/api/clinical/templates/{made.Body.GetProperty("id").GetString()}/active", new { active = false, rowVersion = Version(made.Body) });

        Assert.Equal(0, (await hygienist.SendAsync(HttpMethod.Get, "/api/clinical/templates?includeInactive=true")).Body.GetArrayLength()); // not theirs to see
        Assert.Equal(1, (await admin.SendAsync(HttpMethod.Get, "/api/clinical/templates?includeInactive=true")).Body.GetArrayLength());
        Assert.Equal(0, (await admin.SendAsync(HttpMethod.Get, "/api/clinical/templates")).Body.GetArrayLength());
    }

    // ---------- the workflow over HTTP ----------

    [Fact]
    public async Task A_clinician_builds_the_record_documents_with_a_template_signs_finalizes_and_amends_and_the_team_reads_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var assistant = await _api.SessionAsync("Assistant");

        // the longitudinal record
        var record = await AddItemAsync(dentist, "Allergy", "Penicillin", new { reaction = "Hives", severity = "Moderate" });
        var penicillin = Item(record, "Allergy", "Penicillin");
        var resolved = await hygienist.SendAsync(HttpMethod.Post, $"/api/clinical-record/items/{penicillin.GetProperty("id").GetString()}/status",
            new { status = "Resolved", rowVersion = penicillin.GetProperty("rowVersion").GetString(), reason = "Tolerated a challenge dose" });
        Assert.Equal(HttpStatusCode.OK, resolved.Status);
        Assert.Equal("Resolved", Item(resolved.Body, "Allergy", "Penicillin").GetProperty("status").GetString());
        var noneKnown = await dentist.SendAsync(HttpMethod.Put, RecordOf() + "/sections/Medication/review", new { state = "NoneKnown" });
        Assert.Equal("NoneKnown", Section(noneKnown.Body, "Medication").GetProperty("status").GetString());
        Assert.Equal("NotReviewed", Section(noneKnown.Body, "DentalHistory").GetProperty("status").GetString());
        var history = await assistant.SendAsync(HttpMethod.Get, $"/api/clinical-record/items/{penicillin.GetProperty("id").GetString()}/history");
        Assert.Equal(new[] { "Added", "StatusChanged" }, history.Body.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("changeType").GetString()!));

        // a template, a note, vitals, signing, finalizing
        var template = await dentist.SendAsync(HttpMethod.Post, "/api/clinical/templates", new
        {
            name = "SOAP note", description = "Standard visit",
            sections = new[] { new { section = "Subjective", required = true, starterText = "Chief complaint:" }, new { section = "Plan", required = true, starterText = (string?)null } },
        });
        Assert.Equal(HttpStatusCode.Created, template.Status);
        var enc = await StartEncounterAsync(dentist);
        foreach (var kind in new[] { "MedicalHistory", "DentalHistory", "Allergy", "Medication" })
        {
            var marked = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, $"/sections/{kind}/none-reported"), new { rowVersion = Version(enc) });
            Assert.Equal(HttpStatusCode.OK, marked.Status);
            enc = marked.Body;
        }
        var applied = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/template"), new { templateId = template.Body.GetProperty("id").GetString(), rowVersion = Version(enc) });
        Assert.Equal("SOAP note", applied.Body.GetProperty("templateName").GetString());
        Assert.False(applied.Body.GetProperty("readyToSign").GetBoolean());
        enc = applied.Body;

        var early = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/sign"), new { rowVersion = Version(enc) });
        Assert.Equal((HttpStatusCode.Conflict, "documentation_incomplete"), (early.Status, Error(early.Body)));
        Assert.Equal(new[] { "note:Plan", "note:Subjective" }, early.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray());

        foreach (var (section, text) in new[] { ("Subjective", "Chief complaint: pain, lower left"), ("Plan", "Endodontic treatment, 36") })
        {
            var saved = await dentist.SendAsync(HttpMethod.Put, EncounterUrl(enc, $"/notes/{section}"), new { body = text, rowVersion = Version(enc) });
            Assert.Equal(HttpStatusCode.OK, saved.Status);
            enc = saved.Body;
        }
        var vitals = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/vitals"), new { systolicMmHg = 122, diastolicMmHg = 78, pulseBpm = 68, rowVersion = Version(enc) }, "vitals-1");
        Assert.Equal(HttpStatusCode.Created, vitals.Status);
        enc = vitals.Body;
        Assert.Equal(122, enc.GetProperty("vitals")[0].GetProperty("systolicMmHg").GetInt32());

        var signed = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/sign"), new { rowVersion = Version(enc) });
        Assert.Equal(HttpStatusCode.OK, signed.Status);
        Assert.True(signed.Body.GetProperty("isSigned").GetBoolean());
        var locked = await dentist.SendAsync(HttpMethod.Put, EncounterUrl(signed.Body, "/notes/Plan"), new { body = "Changed", rowVersion = Version(signed.Body) });
        Assert.Equal((HttpStatusCode.Conflict, "encounter_signed"), (locked.Status, Error(locked.Body)));

        var finalized = await hygienist.SendAsync(HttpMethod.Post, EncounterUrl(signed.Body, "/finalize"), new { rowVersion = Version(signed.Body) });
        Assert.Equal("Finalized", finalized.Body.GetProperty("status").GetString());
        var overwritten = await dentist.SendAsync(HttpMethod.Put, EncounterUrl(finalized.Body, "/notes/Plan"), new { body = "Overwritten", rowVersion = Version(finalized.Body) });
        Assert.Equal((HttpStatusCode.Conflict, "encounter_finalized"), (overwritten.Status, Error(overwritten.Body)));

        var amended = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(finalized.Body, "/addenda"), new { text = "Plan changed: refer out.", section = "Plan" }, "amend-1");
        Assert.Equal(HttpStatusCode.Created, amended.Status);
        var seen = (await assistant.SendAsync(HttpMethod.Get, EncounterUrl(finalized.Body))).Body;
        Assert.Equal("Endodontic treatment, 36", seen.GetProperty("notes").EnumerateArray().Single(n => n.GetProperty("section").GetString() == "Plan").GetProperty("body").GetString());
        Assert.Equal("Plan", seen.GetProperty("addenda")[0].GetProperty("section").GetString());
        Assert.Equal(Version(finalized.Body), Version(seen));
    }

    // ---------- the contract: CSRF, row version, concurrency, idempotency keys, stable error shapes ----------

    [Fact]
    public async Task Every_change_needs_a_csrf_token_and_item_changes_need_the_row_version_the_caller_read()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var item = Item(await AddItemAsync(dentist, "Medication", "Lisinopril"), "Medication", "Lisinopril");
        var id = item.GetProperty("id").GetString();
        var enc = await StartEncounterAsync(dentist);

        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, RecordOf() + "/items", new { kind = "Allergy", name = "Latex" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/clinical-record/items/{id}/status", new { status = "Inactive", rowVersion = item.GetProperty("rowVersion").GetString() }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Put, RecordOf() + "/sections/Allergy/review", new { state = "NoneKnown" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Put, EncounterUrl(enc, "/notes/Plan"), new { body = "x", rowVersion = Version(enc) }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/vitals"), new { pulseBpm = 70, rowVersion = Version(enc) }, "k", csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, "/api/clinical/templates", new { name = "x", sections = new[] { new { section = "Plan", required = false } } }, csrf: false)).Status);

        var noVersion = await dentist.SendAsync(HttpMethod.Post, $"/api/clinical-record/items/{id}/status", new { status = "Inactive" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var badVersion = await dentist.SendAsync(HttpMethod.Put, EncounterUrl(enc, "/notes/Plan"), new { body = "x", rowVersion = "not-base64!" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_invalid"), (badVersion.Status, Error(badVersion.Body)));
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterNotes));
        Assert.Equal(0, await _s.CountAsync(db => db.NoteTemplates));
    }

    [Fact]
    public async Task A_stale_editor_gets_the_shared_409_concurrency_conflict_for_items_notes_and_templates()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var item = Item(await AddItemAsync(dentist, "Medication", "Lisinopril"), "Medication", "Lisinopril");
        var itemId = item.GetProperty("id").GetString();
        await dentist.SendAsync(HttpMethod.Post, $"/api/clinical-record/items/{itemId}/status", new { status = "Discontinued", rowVersion = item.GetProperty("rowVersion").GetString() });
        var staleItem = await hygienist.SendAsync(HttpMethod.Post, $"/api/clinical-record/items/{itemId}/status", new { status = "Inactive", rowVersion = item.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (staleItem.Status, Error(staleItem.Body)));
        Assert.Equal("ClinicalRecordItem", staleItem.Body.GetProperty("entityType").GetString());

        var enc = await StartEncounterAsync(dentist);
        await dentist.SendAsync(HttpMethod.Put, EncounterUrl(enc, "/notes/Plan"), new { body = "First tab", rowVersion = Version(enc) });
        var staleNote = await hygienist.SendAsync(HttpMethod.Put, EncounterUrl(enc, "/notes/Plan"), new { body = "Stale tab", rowVersion = Version(enc) });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (staleNote.Status, Error(staleNote.Body)));
        Assert.Equal("First tab", (await dentist.SendAsync(HttpMethod.Get, EncounterUrl(enc))).Body.GetProperty("notes")[0].GetProperty("body").GetString());

        var made = await dentist.SendAsync(HttpMethod.Post, "/api/clinical/templates", new { name = "SOAP", sections = new[] { new { section = "Plan", required = false } } });
        await dentist.SendAsync(HttpMethod.Put, $"/api/clinical/templates/{made.Body.GetProperty("id").GetString()}", new { name = "SOAP 2", sections = new[] { new { section = "Plan", required = true } }, rowVersion = Version(made.Body) });
        var staleTemplate = await dentist.SendAsync(HttpMethod.Put, $"/api/clinical/templates/{made.Body.GetProperty("id").GetString()}", new { name = "SOAP 3", sections = new[] { new { section = "Plan", required = true } }, rowVersion = Version(made.Body) });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (staleTemplate.Status, Error(staleTemplate.Body)));
    }

    [Fact]
    public async Task Recording_vitals_needs_an_idempotency_key_and_the_same_key_returns_the_first_reading_with_200()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var enc = await StartEncounterAsync(dentist);
        var body = new { systolicMmHg = 120, diastolicMmHg = 80, rowVersion = Version(enc) };

        var noKey = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/vitals"), body);
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (noKey.Status, Error(noKey.Body)));
        Assert.True(noKey.Body.GetProperty("fieldErrors").TryGetProperty("clientKey", out _));

        var first = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/vitals"), body, "same-key");
        var again = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/vitals"), body, "same-key");
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.Status, again.Status));
        Assert.Equal(1, again.Body.GetProperty("vitals").GetArrayLength());

        var different = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/vitals"), new { systolicMmHg = 150, diastolicMmHg = 90, rowVersion = Version(enc) }, "same-key");
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (different.Status, Error(different.Body)));
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterVitals));
    }

    [Fact]
    public async Task Refusals_have_stable_codes_and_field_errors_a_client_can_show()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var enc = await StartEncounterAsync(dentist);

        var implausible = await dentist.SendAsync(HttpMethod.Post, EncounterUrl(enc, "/vitals"), new { systolicMmHg = 500, diastolicMmHg = 80, rowVersion = Version(enc) }, "v1");
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (implausible.Status, Error(implausible.Body)));
        Assert.True(implausible.Body.GetProperty("fieldErrors").TryGetProperty("systolicMmHg", out _));

        var badSection = await dentist.SendAsync(HttpMethod.Put, EncounterUrl(enc, "/notes/Soap"), new { body = "x", rowVersion = Version(enc) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (badSection.Status, Error(badSection.Body)));

        var badState = await dentist.SendAsync(HttpMethod.Put, RecordOf() + "/sections/Allergy/review", new { state = "Fine" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (badState.Status, Error(badState.Body)));
        var emptyConfirm = await dentist.SendAsync(HttpMethod.Put, RecordOf() + "/sections/Allergy/review", new { state = "Reviewed" });
        Assert.Equal((HttpStatusCode.Conflict, "section_empty"), (emptyConfirm.Status, Error(emptyConfirm.Body)));

        var missingPatient = await dentist.SendAsync(HttpMethod.Get, RecordOf(Guid.NewGuid()));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (missingPatient.Status, Error(missingPatient.Body)));
        var missingItem = await dentist.SendAsync(HttpMethod.Get, $"/api/clinical-record/items/{Guid.NewGuid()}/history");
        Assert.Equal((HttpStatusCode.NotFound, "item_not_found"), (missingItem.Status, Error(missingItem.Body)));
        var missingTemplate = await dentist.SendAsync(HttpMethod.Get, $"/api/clinical/templates/{Guid.NewGuid()}");
        Assert.Equal((HttpStatusCode.NotFound, "template_not_found"), (missingTemplate.Status, Error(missingTemplate.Body)));

        var emptyTemplate = await dentist.SendAsync(HttpMethod.Post, "/api/clinical/templates", new { name = "", sections = Array.Empty<object>() });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (emptyTemplate.Status, Error(emptyTemplate.Body)));
        Assert.True(emptyTemplate.Body.GetProperty("fieldErrors").TryGetProperty("name", out _) && emptyTemplate.Body.GetProperty("fieldErrors").TryGetProperty("sections", out _));
    }

    [Fact]
    public async Task Another_patients_record_is_never_read_through_this_patients_route()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var bo = await _s.PatientAsync("Bo", "Kim");
        await AddItemAsync(dentist, "Allergy", "Penicillin");
        var theirs = (await dentist.SendAsync(HttpMethod.Get, RecordOf(bo))).Body;
        Assert.All(theirs.GetProperty("sections").EnumerateArray(), s => Assert.Equal(0, s.GetProperty("items").GetArrayLength()));
        Assert.Equal(bo.ToString(), theirs.GetProperty("patientId").GetString());
    }

    [Fact]
    public async Task An_audit_entry_names_the_signed_in_user_for_a_change_made_over_the_API()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AddItemAsync(dentist, "Allergy", "Penicillin");
        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == nameof(ClinicalRecordItem));
        Assert.Equal("ClinicalRecordItemAdded", entry.EventType);
        Assert.NotNull(entry.PerformedByUserAccountId);
        Assert.DoesNotContain("Penicillin", entry.Details);
    }
}
