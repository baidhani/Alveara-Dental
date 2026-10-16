using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-016-C01 at the HTTP boundary, against a real API, real SQL Server and a real signed-in session per role: only the dentist and the administrator may change anything (with a CSRF token), the clinical team
/// reads, everyone else is refused; the whole route from a draft to a finalized, printed and cancelled prescription; the error shapes the screen relies on (every problem named, the allergy assessment, what changed in
/// the allergy and medication lists, a stale version); and that there is no way to edit or delete a prescription or a draft.
/// </summary>
public class PrescriptionWorkflowApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private string _patient = "";

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = (await _s.PatientAsync()).ToString();
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    private static object DraftBody(string medication = "Amoxicillin", decimal dose = 500m, string? key = null, string? instructions = "Take one capsule with food until finished.", decimal? quantity = 21m,
        string? quantityUnit = "capsule", int? refills = 0, string? note = null, string? rowVersion = null) =>
        new { idempotencyKey = key ?? $"d-{Guid.NewGuid():N}", medication, doseValue = dose, doseUnit = "mg", frequency = "Three times daily", instructions, quantityValue = quantity, quantityUnit, refills, reviewNote = note, rowVersion };

    private async Task AllergyListAsync(Session dentist, string state = "NoneKnown", params (string Name, string? Severity)[] allergies)
    {
        foreach (var (name, severity) in allergies)
            Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/clinical-record/items", new { kind = "Allergy", name, reaction = "Hives", severity })).Status);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Put, $"/api/patients/{_patient}/clinical-record/sections/Allergy/review", new { state })).Status);
    }

    private async Task<JsonElement> NewDraftAsync(Session by, object? body = null)
    {
        var r = await by.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescription-drafts", body ?? DraftBody());
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body.GetProperty("draft");
    }

    private async Task<string> FinalizedAsync(Session dentist)
    {
        var d = await NewDraftAsync(dentist);
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(d, "id")}/finalize", new { rowVersion = Str(d, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return Str(r.Body.GetProperty("record"), "id");
    }

    private async Task<(int Drafts, int Prescriptions, int Events, int Audit)> CountsAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await db.PrescriptionDrafts.CountAsync(), await db.Prescriptions.CountAsync(), await db.PrescriptionEvents.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("Prescription")));
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_route()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        foreach (var (method, url) in new[]
        {
            (HttpMethod.Get, "/api/prescription-workflow/options"), (HttpMethod.Get, $"/api/patients/{_patient}/prescription-drafts"), (HttpMethod.Get, $"/api/patients/{_patient}/prescription-records"),
            (HttpMethod.Get, $"/api/prescription-drafts/{id}"), (HttpMethod.Get, $"/api/prescriptions/{id}"), (HttpMethod.Get, $"/api/prescriptions/{id}/print"),
            (HttpMethod.Post, $"/api/patients/{_patient}/prescription-drafts"), (HttpMethod.Put, $"/api/prescription-drafts/{id}"), (HttpMethod.Post, $"/api/prescription-drafts/{id}/check"),
            (HttpMethod.Post, $"/api/prescription-drafts/{id}/acknowledge-safety"), (HttpMethod.Post, $"/api/prescription-drafts/{id}/finalize"), (HttpMethod.Post, $"/api/prescription-drafts/{id}/discard"),
            (HttpMethod.Post, $"/api/prescriptions/{id}/amend"), (HttpMethod.Post, $"/api/prescriptions/{id}/cancel"), (HttpMethod.Post, $"/api/prescriptions/{id}/discontinue"),
        })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task The_clinical_team_reads_the_workflow_and_everyone_else_is_refused(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var id = await FinalizedAsync(dentist);
        var draft = Str(await NewDraftAsync(dentist), "id");
        var by = await _api.SessionAsync(role);
        var expected = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        foreach (var url in new[] { "/api/prescription-workflow/options", $"/api/patients/{_patient}/prescription-drafts", $"/api/patients/{_patient}/prescription-records", $"/api/prescription-drafts/{draft}", $"/api/prescriptions/{id}", $"/api/prescriptions/{id}/print" })
            Assert.Equal(expected, (await by.SendAsync(HttpMethod.Get, url)).Status);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_dentists_and_administrators_can_change_anything_in_the_workflow(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var prescription = await FinalizedAsync(dentist);
        var draft = await NewDraftAsync(dentist);
        var id = Str(draft, "id");
        var rv = Str(draft, "rowVersion");
        var by = await _api.SessionAsync(role);
        var before = await CountsAsync();

        var attempts = new[]
        {
            (HttpMethod.Post, $"/api/patients/{_patient}/prescription-drafts", (object)DraftBody()), (HttpMethod.Put, $"/api/prescription-drafts/{id}", DraftBody(rowVersion: rv)),
            (HttpMethod.Post, $"/api/prescription-drafts/{id}/check", new { }), (HttpMethod.Post, $"/api/prescription-drafts/{id}/acknowledge-safety", new { rowVersion = rv, seenFingerprint = "x" }),
            (HttpMethod.Post, $"/api/prescription-drafts/{id}/finalize", new { rowVersion = rv }), (HttpMethod.Post, $"/api/prescription-drafts/{id}/discard", new { rowVersion = rv }),
            (HttpMethod.Post, $"/api/prescriptions/{prescription}/amend", new { idempotencyKey = "k" }), (HttpMethod.Post, $"/api/prescriptions/{prescription}/cancel", new { reason = "Wrong." }),
            (HttpMethod.Post, $"/api/prescriptions/{prescription}/discontinue", new { reason = "Stopped." }),
        };
        foreach (var (method, url, body) in attempts)
        {
            var r = await by.SendAsync(method, url, body);
            if (allowed) Assert.NotEqual(HttpStatusCode.Forbidden, r.Status);
            else Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        }
        if (!allowed) Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task A_change_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var p = await FinalizedAsync(dentist);
        var d = await NewDraftAsync(dentist);
        var before = await CountsAsync();
        foreach (var (method, url, body) in new[]
        {
            (HttpMethod.Post, $"/api/patients/{_patient}/prescription-drafts", (object)DraftBody()), (HttpMethod.Put, $"/api/prescription-drafts/{Str(d, "id")}", DraftBody(rowVersion: Str(d, "rowVersion"))),
            (HttpMethod.Post, $"/api/prescription-drafts/{Str(d, "id")}/finalize", new { rowVersion = Str(d, "rowVersion") }), (HttpMethod.Post, $"/api/prescriptions/{p}/cancel", new { reason = "Wrong." }),
            (HttpMethod.Post, $"/api/prescriptions/{p}/amend", new { idempotencyKey = "k" }), (HttpMethod.Post, $"/api/prescription-drafts/{Str(d, "id")}/check", new { }),
            (HttpMethod.Post, $"/api/prescription-drafts/{Str(d, "id")}/acknowledge-safety", new { rowVersion = Str(d, "rowVersion"), seenFingerprint = "x" }),
            (HttpMethod.Post, $"/api/prescription-drafts/{Str(d, "id")}/discard", new { rowVersion = Str(d, "rowVersion") }), (HttpMethod.Post, $"/api/prescriptions/{p}/discontinue", new { reason = "Stopped." }),
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(method, url, body, csrf: false)).Status);
        Assert.Equal(before, await CountsAsync());
    }

    // ---------- the whole route ----------

    [Fact]
    public async Task A_prescription_goes_from_a_draft_to_finalized_printed_and_cancelled_and_every_step_is_logged()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist, "Reviewed", ("Latex", "Mild"));

        var created = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescription-drafts", DraftBody(instructions: null, quantity: null, quantityUnit: null, refills: null, key: "once"));
        Assert.Equal((HttpStatusCode.OK, false), (created.Status, created.Body.GetProperty("replayed").GetBoolean()));
        var draft = created.Body.GetProperty("draft");
        var id = Str(draft, "id");
        Assert.True((await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescription-drafts", DraftBody(instructions: null, quantity: null, quantityUnit: null, refills: null, key: "once"))).Body.GetProperty("replayed").GetBoolean());

        var check = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{id}/check", new { });
        Assert.False(check.Body.GetProperty("canFinalize").GetBoolean());                                            // instructions, quantity and refills are still missing
        Assert.Equal(["instructions", "quantityValue", "quantityUnit", "refills"], check.Body.GetProperty("problems").EnumerateArray().Select(p => Str(p, "field")));
        Assert.Contains("not configured", Str(check.Body, "decisionSupport"));

        var updated = await dentist.SendAsync(HttpMethod.Put, $"/api/prescription-drafts/{id}", DraftBody(refills: 1, rowVersion: Str(draft, "rowVersion")));
        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.True((await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{id}/check", new { })).Body.GetProperty("canFinalize").GetBoolean());
        var detail = await dentist.SendAsync(HttpMethod.Get, $"/api/prescription-drafts/{id}");
        Assert.False(detail.Body.GetProperty("safety").GetProperty("changedSinceAcknowledged").GetBoolean());

        var finalized = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{id}/finalize", new { rowVersion = Str(updated.Body, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, finalized.Status);
        var record = finalized.Body.GetProperty("record");
        var prescription = Str(record, "id");
        Assert.Equal(("Active", true, 1, "Take one capsule with food until finished."), (Str(record, "status"), record.GetProperty("hasDetails").GetBoolean(), record.GetProperty("refills").GetInt32(), Str(record, "instructions")));

        var print = await dentist.SendAsync(HttpMethod.Get, $"/api/prescriptions/{prescription}/print");
        Assert.Equal(HttpStatusCode.OK, print.Status);
        Assert.Equal(("500 mg", "21 capsule", "Active"), (Str(print.Body, "dose"), Str(print.Body, "quantity"), Str(print.Body, "status")));
        Assert.Matches("^[0-9a-f]{64}$", Str(print.Body, "contentHash"));

        var cancelled = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/cancel", new { reason = "Entered for the wrong patient." });
        Assert.Equal(("Cancelled", false), (Str(cancelled.Body.GetProperty("record"), "status"), cancelled.Body.GetProperty("replayed").GetBoolean()));
        Assert.Equal("Cancelled", Str((await dentist.SendAsync(HttpMethod.Get, $"/api/prescriptions/{prescription}")).Body, "status"));
        var records = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/prescription-records")).Body.GetProperty("prescriptions");
        Assert.Equal(1, records.GetArrayLength());

        await using var db = _fixture.CreateContext();
        var events = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith("Prescription")).Select(a => a.EventType).ToListAsync();
        foreach (var expected in new[] { "PrescriptionDraftCreated", "PrescriptionDraftChanged", "PrescriptionRecorded", "PrescriptionPrinted", "PrescriptionCancelled" }) Assert.Contains(expected, events);
    }

    [Fact]
    public async Task An_amendment_replaces_a_prescription_through_the_api_and_the_old_one_stays_as_it_was()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var original = await FinalizedAsync(dentist);
        var amend = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{original}/amend", new { idempotencyKey = "amend-1" });
        Assert.Equal((HttpStatusCode.OK, original), (amend.Status, Str(amend.Body.GetProperty("draft"), "supersedesPrescriptionId")));
        var draft = amend.Body.GetProperty("draft");
        var finalized = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(draft, "id")}/finalize", new { rowVersion = Str(draft, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, finalized.Status);
        var old = (await dentist.SendAsync(HttpMethod.Get, $"/api/prescriptions/{original}")).Body;
        Assert.Equal(("Superseded", Str(finalized.Body.GetProperty("record"), "id")), (Str(old, "status"), Str(old, "supersededByPrescriptionId")));
        Assert.Equal("prescription_not_active", Error((await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{original}/amend", new { idempotencyKey = "again" })).Body));
    }

    // ---------- the refusals the screen relies on ----------

    [Fact]
    public async Task The_refusals_carry_stable_codes_and_the_details_the_screen_needs_and_nothing_is_saved()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist, "Reviewed", ("Penicillin", "Severe"));

        var invalid = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescription-drafts", DraftBody("A", 6000m, quantity: 0m, refills: 99));
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (invalid.Status, Error(invalid.Body)));
        Assert.Equal(["medication", "doseValue", "quantityValue", "refills"], invalid.Body.GetProperty("problems").EnumerateArray().Select(p => Str(p, "field")));
        Assert.Equal((0, 0, 0, 0), await CountsAsync());

        var draft = await NewDraftAsync(dentist, DraftBody("Penicillin V Potassium"));
        var needsReview = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(draft, "id")}/finalize", new { rowVersion = Str(draft, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "allergy_review_required", "RecordedAllergyMatch"), (needsReview.Status, Error(needsReview.Body), Str(needsReview.Body.GetProperty("assessment"), "outcome")));

        var stale = await dentist.SendAsync(HttpMethod.Put, $"/api/prescription-drafts/{Str(draft, "id")}", DraftBody("Penicillin V Potassium", note: "Tolerated before.", rowVersion: Str(draft, "rowVersion")));
        Assert.Equal(HttpStatusCode.OK, stale.Status);
        var conflict = await dentist.SendAsync(HttpMethod.Put, $"/api/prescription-drafts/{Str(draft, "id")}", DraftBody("Penicillin V Potassium", refills: 3, rowVersion: Str(draft, "rowVersion")));   // the version from before
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (conflict.Status, Error(conflict.Body)));
        Assert.Equal("row_version_required", Error((await dentist.SendAsync(HttpMethod.Put, $"/api/prescription-drafts/{Str(draft, "id")}", DraftBody())).Body));

        Assert.Equal((HttpStatusCode.NotFound, "draft_not_found"), ((await dentist.SendAsync(HttpMethod.Get, $"/api/prescription-drafts/{Guid.NewGuid()}")).Status, Error((await dentist.SendAsync(HttpMethod.Get, $"/api/prescription-drafts/{Guid.NewGuid()}")).Body)));
        Assert.Equal("patient_not_found", Error((await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{Guid.NewGuid()}/prescription-drafts", DraftBody())).Body));
    }

    [Fact]
    public async Task A_change_to_the_allergy_list_stops_the_finalization_with_what_changed_and_acknowledging_it_lets_it_go_ahead()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var draft = await NewDraftAsync(dentist);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/clinical-record/items", new { kind = "Allergy", name = "Latex", reaction = "Rash", severity = "Mild" })).Status);

        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(draft, "id")}/finalize", new { rowVersion = Str(draft, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "safety_context_changed"), (refused.Status, Error(refused.Body)));
        var detail = refused.Body.GetProperty("detail");
        Assert.Contains(detail.GetProperty("added").EnumerateArray().Select(a => a.GetString()), a => a!.StartsWith("Allergy: Latex"));

        var seen = Str(detail, "currentFingerprint");
        var wrong = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(draft, "id")}/acknowledge-safety", new { rowVersion = Str(draft, "rowVersion"), seenFingerprint = "nope" });
        Assert.Equal("safety_context_changed", Error(wrong.Body));
        var ack = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(draft, "id")}/acknowledge-safety", new { rowVersion = Str(draft, "rowVersion"), seenFingerprint = seen });
        Assert.Equal(HttpStatusCode.OK, ack.Status);
        Assert.Equal(seen, Str(ack.Body, "safetyFingerprint"));
        // the allergy list needs review now (an allergy was added after it was confirmed), so the prescriber's own words are needed
        var withNote = await dentist.SendAsync(HttpMethod.Put, $"/api/prescription-drafts/{Str(draft, "id")}", DraftBody(note: "Asked the patient today.", rowVersion: Str(ack.Body, "rowVersion")));
        var done = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(draft, "id")}/finalize", new { rowVersion = Str(withNote.Body, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, done.Status);
    }

    [Fact]
    public async Task A_prescription_saved_before_the_workflow_cannot_be_printed_and_says_so()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var legacy = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/prescriptions",
            new { idempotencyKey = "legacy", medication = "Naproxen", doseValue = 250m, doseUnit = "mg", frequency = "Twice daily", reviewNote = (string?)null });
        var id = Str(legacy.Body.GetProperty("prescription"), "id");
        var print = await dentist.SendAsync(HttpMethod.Get, $"/api/prescriptions/{id}/print");
        Assert.Equal((HttpStatusCode.Conflict, "print_incomplete"), (print.Status, Error(print.Body)));
        var record = (await dentist.SendAsync(HttpMethod.Get, $"/api/prescriptions/{id}")).Body;
        Assert.Equal(("Active", false), (Str(record, "status"), record.GetProperty("hasDetails").GetBoolean()));
    }

    [Fact]
    public async Task The_options_say_that_no_drug_knowledge_source_is_configured()
    {
        var by = await _api.SessionAsync("Hygienist");
        var o = (await by.SendAsync(HttpMethod.Get, "/api/prescription-workflow/options")).Body;
        Assert.False(o.GetProperty("decisionSupportConfigured").GetBoolean());
        Assert.Contains("not configured", Str(o, "decisionSupportMessage"));
        Assert.Contains("typing-error ceiling", Str(o, "doseLimitation"));
        Assert.Equal(12, o.GetProperty("refillsMax").GetInt32());
    }

    [Fact]
    public async Task There_is_no_way_to_edit_or_delete_a_prescription_or_to_delete_a_draft()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var prescription = await FinalizedAsync(dentist);
        var draft = Str(await NewDraftAsync(dentist), "id");
        foreach (var (method, url) in new[] { (HttpMethod.Put, $"/api/prescriptions/{prescription}"), (HttpMethod.Delete, $"/api/prescriptions/{prescription}"), (HttpMethod.Patch, $"/api/prescriptions/{prescription}"),
                                              (HttpMethod.Delete, $"/api/prescription-drafts/{draft}"), (HttpMethod.Put, $"/api/patients/{_patient}/prescription-records") })
            Assert.Contains((await dentist.SendAsync(method, url, new { medication = "x" })).Status, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        var counts = await CountsAsync();
        Assert.Equal((2, 1), (counts.Drafts, counts.Prescriptions));                                                 // the finalized draft and the open one; the prescription
    }
}
