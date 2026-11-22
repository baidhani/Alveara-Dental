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

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = (await _s.PatientAsync()).ToString();
    });
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
            (HttpMethod.Post, $"/api/prescriptions/{id}/issues"), (HttpMethod.Get, $"/api/prescription-issues/{id}/pdf"),                                     // ALV-N026 S6
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
        foreach (var url in new[] { "/api/prescription-workflow/options", $"/api/patients/{_patient}/prescription-drafts", $"/api/patients/{_patient}/prescription-records", $"/api/prescription-drafts/{draft}", $"/api/prescriptions/{id}" })
            Assert.Equal(expected, (await by.SendAsync(HttpMethod.Get, url)).Status);
        // ALV-N026 S6: the readers may issue and open a record copy, as they could print before
        var issued = await by.SendAsync(HttpMethod.Post, $"/api/prescriptions/{id}/issues", new { kind = "RecordCopy", idempotencyKey = $"role-{role}" });
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, issued.Status);
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

        var print = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/issues", new { kind = "Print", idempotencyKey = "route-print" });   // ALV-N026 S6: a p2 issue
        Assert.Equal((HttpStatusCode.Created, "Active", "p2"), (print.Status, Str(print.Body, "status"), Str(print.Body, "templateVersion")));

        var cancelled = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/cancel", new { reason = "Entered for the wrong patient." });
        Assert.Equal(("Cancelled", false), (Str(cancelled.Body.GetProperty("record"), "status"), cancelled.Body.GetProperty("replayed").GetBoolean()));
        Assert.Equal("Cancelled", Str((await dentist.SendAsync(HttpMethod.Get, $"/api/prescriptions/{prescription}")).Body, "status"));
        var records = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/prescription-records")).Body.GetProperty("prescriptions");
        Assert.Equal(1, records.GetArrayLength());

        await using var db = _fixture.CreateContext();
        var events = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith("Prescription")).Select(a => a.EventType).ToListAsync();
        foreach (var expected in new[] { "PrescriptionDraftCreated", "PrescriptionDraftChanged", "PrescriptionRecorded", "PrescriptionIssued", "PrescriptionCancelled" }) Assert.Contains(expected, events);
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
        var print = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{id}/issues", new { kind = "RecordCopy", idempotencyKey = "legacy-copy" });
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

    // ---------- ALV-N026 S6: prescriptions as server-made PDFs (p2) ----------

    /// <summary>The text a person sees in a PDF, read with a real PDF reader (PdfPig, a test-only dependency): never a byte comparison.</summary>
    private static string PdfText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var text = string.Join("\n", document.GetPages().Select(p => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p)));
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");                                     // the words as read, not where a line happens to wrap
    }

    private async Task<string> IssueAsync(Session by, string prescription, string kind, string key, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var r = await by.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/issues", new { kind, idempotencyKey = key });
        Assert.Equal(expected, r.Status);
        return Str(r.Body, "id");
    }

    private async Task<string> IssueTextAsync(Session by, string issue)
    {
        var response = await by.Client.GetAsync($"/api/prescription-issues/{issue}/pdf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        return PdfText(await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>Links the dentist's login to a staff profile with the given prescriber details (as an administrator would record them).</summary>
    private async Task PrescriberDetailsAsync(Session dentist, string? credential, string? license, string? jurisdiction, string? npi)
    {
        await using var db = _fixture.CreateContext();
        var staff = await db.StaffProfiles.SingleOrDefaultAsync(s => s.UserAccountId == dentist.UserId);
        if (staff is null) { staff = new Alveara.Api.Architecture.Identity.StaffProfile { Id = Guid.NewGuid(), DisplayName = $"Dentist {Guid.NewGuid():N}"[..20], UserAccountId = dentist.UserId, CreatedAtUtc = DateTimeOffset.UtcNow }; db.StaffProfiles.Add(staff); }
        (staff.CredentialLabel, staff.LicenseNumber, staff.LicenseJurisdiction, staff.Npi) = (credential, license, jurisdiction, npi);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_print_is_issued_once_per_key_and_its_pdf_shows_the_prescription_as_recorded_with_the_boundary_and_no_dea_field()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        await PrescriberDetailsAsync(dentist, "DDS", "D-12345", "Texas", "1234567893");
        var prescription = await FinalizedAsync(dentist);
        var issue = await IssueAsync(dentist, prescription, "Print", "p-1");
        Assert.Equal(issue, await IssueAsync(dentist, prescription, "Print", "p-1", HttpStatusCode.OK));                                  // a retry returns the same issue
        var reused = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/issues", new { kind = "RecordCopy", idempotencyKey = "p-1" });
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (reused.Status, Error(reused.Body)));

        long mrn;
        await using (var db = _fixture.CreateContext())
        {
            Assert.Equal(1, await db.PrescriptionIssues.CountAsync());
            mrn = (await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(_patient))).Mrn;
        }
        var text = await IssueTextAsync(dentist, issue);
        foreach (var expected in new[] { "Amoxicillin", "500 mg", "Three times daily", "Take one capsule with food until finished.", "21 capsules", $"MRN", mrn.ToString(), "DDS", "D-12345 (Texas)", "1234567893",
                     "Prescriber signature", "not a federal or state compliance profile", "does not determine whether a medication is a controlled substance", "template version p2" })
            Assert.True(text.Contains(expected), $"missing \"{expected}\" in the PDF text: {text}");
        foreach (var never in new[] { "DEA", "Strength", "Dosage form", "DO NOT DISPENSE" }) Assert.DoesNotContain(never, text);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.EventType == "PrescriptionIssued"));
        Assert.Contains("typst-0.15.1+noto-sans-2.015+vcrt-", (await check.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "PrescriptionPdfProduced")).Details);
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.EventType == Alveara.Api.Architecture.Auditing.PatientAccessEvents.MaterialPrinted && a.TargetUserAccountId == Guid.Parse(_patient)));
    }

    [Fact]
    public async Task Absent_prescriber_details_are_left_off_without_empty_labels_and_never_refuse()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var text = await IssueTextAsync(dentist, await IssueAsync(dentist, await FinalizedAsync(dentist), "Print", "bare"));
        foreach (var label in new[] { "Credential", "Licence", "NPI" }) Assert.DoesNotContain(label, text);
        Assert.Contains("Prescriber signature", text);
    }

    [Fact]
    public async Task An_ended_prescription_is_not_printed_to_dispense_and_its_record_copy_carries_the_watermark_date_and_reason()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var prescription = await FinalizedAsync(dentist);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/cancel", new { reason = "Entered for the wrong patient." })).Status);
        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/issues", new { kind = "Print", idempotencyKey = "after-cancel" });
        Assert.Equal((HttpStatusCode.Conflict, "prescription_not_active"), (refused.Status, Error(refused.Body)));

        var text = await IssueTextAsync(dentist, await IssueAsync(dentist, prescription, "RecordCopy", "copy-1"));
        Assert.Contains("CANCELLED — DO NOT DISPENSE", text);
        Assert.Contains("Entered for the wrong patient.", text);
        Assert.Contains("Prescription record copy", text);
    }

    // ---------- ALV-N026 S8 restoration (owner decision 2026-11-22): the allergy-check statement on the p2 PDF ----------

    private const string NoMatchStatement = "Allergy check: At the time of prescribing no recorded allergy matched this medication's name. That is a name comparison, not a clearance.";

    /// <summary>Finalizes a draft with the given body (a review note where the allergy outcome needs one) and returns the prescription id.</summary>
    private async Task<string> FinalizedAsync(Session dentist, object body)
    {
        var d = await NewDraftAsync(dentist, body);
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/prescription-drafts/{Str(d, "id")}/finalize", new { rowVersion = Str(d, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return Str(r.Body.GetProperty("record"), "id");
    }

    [Fact]
    public async Task The_pdf_states_no_recorded_match_as_a_name_comparison_and_a_later_allergy_chart_change_does_not_rewrite_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var prescription = await FinalizedAsync(dentist);
        var issue = await IssueAsync(dentist, prescription, "Print", "allergy-before");
        Assert.Contains(NoMatchStatement, await IssueTextAsync(dentist, issue));
        await using (var db = _fixture.CreateContext())                                                                  // captured into the issue itself
            Assert.Contains("\"Outcome\":\"NoRecordedMatch\"", (await db.PrescriptionIssues.SingleAsync(i => i.Id == Guid.Parse(issue))).EnvelopeJson);

        await AllergyListAsync(dentist, "Reviewed", ("Amoxicillin", "Severe"));                                          // the chart now names the medication
        var reprint = await IssueTextAsync(dentist, issue);
        var later = await IssueTextAsync(dentist, await IssueAsync(dentist, prescription, "RecordCopy", "allergy-after"));
        Assert.All(new[] { reprint, later }, text =>
        {
            Assert.Contains(NoMatchStatement, text);                                                                    // what was true when it was written
            Assert.DoesNotContain("matched a recorded allergy", text);
            Assert.DoesNotContain("Severe", text);
        });
    }

    [Fact]
    public async Task The_pdf_states_a_reviewed_recorded_match_with_the_matching_name_only()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist, "Reviewed", ("Penicillin", "Severe"));
        var text = await IssueTextAsync(dentist, await IssueAsync(dentist, await FinalizedAsync(dentist, DraftBody("Penicillin V Potassium", note: "Tolerated before.")), "Print", "match"));
        Assert.Contains("Allergy check: At the time of prescribing the medication matched a recorded allergy (Penicillin); the prescriber reviewed it.", text);
        Assert.DoesNotContain("not a clearance", text);
        Assert.DoesNotContain("Severe", text);                                                                          // no clinical detail beyond the name (as the old print)
        Assert.DoesNotContain("Hives", text);
        Assert.DoesNotContain("Tolerated before.", text);
    }

    [Fact]
    public async Task The_pdf_states_allergies_not_established_without_a_verdict()
    {
        var dentist = await _api.SessionAsync("Dentist");                                                               // the allergy list was never reviewed
        var text = await IssueTextAsync(dentist, await IssueAsync(dentist, await FinalizedAsync(dentist, DraftBody(note: "Patient unsure; asked to confirm with their doctor.")), "Print", "not-established"));
        Assert.Contains("Allergy check: At the time of prescribing the patient's allergies were not established; the prescriber confirmed this in their own words.", text);
        Assert.DoesNotContain("no recorded allergy matched", text);
        Assert.DoesNotContain("asked to confirm", text);
    }

    /// <summary>
    /// ALV-N026 R02 (review item 1, F-107): the p2 PDF prints the stored date of birth as the same calendar date. The test host's practice zone is America/Chicago (UTC-6), so a date-only value
    /// treated as midnight UTC and shown in practice time would print the day before; a year-end and a day-before-DST date are included so a slip across either boundary is visible too.
    /// </summary>
    [Theory]
    [InlineData(1990, 1, 1, "1989-12-31")] [InlineData(1985, 12, 31, "1985-12-30")] [InlineData(2001, 3, 10, "2001-03-09")]
    public async Task The_pdf_prints_the_stored_date_of_birth_as_the_same_calendar_date(int year, int month, int day, string slipped)
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var born = new DateOnly(year, month, day);
        await using (var db = _fixture.CreateContext())
            await db.Patients.Where(p => p.Id == Guid.Parse(_patient)).ExecuteUpdateAsync(u => u.SetProperty(p => p.DateOfBirth, born));
        var text = await IssueTextAsync(dentist, await IssueAsync(dentist, await FinalizedAsync(dentist), "Print", $"dob-{born:yyyyMMdd}"));
        Assert.Contains($"Date of birth {born:yyyy-MM-dd}", text);
        Assert.DoesNotContain(slipped, text);
    }

    [Fact]
    public async Task A_reprint_shows_what_was_issued_not_later_patient_or_prescriber_edits()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        await PrescriberDetailsAsync(dentist, "DDS", "OLD-LICENCE", "Texas", null);
        var issue = await IssueAsync(dentist, await FinalizedAsync(dentist), "Print", "before-edits");
        await PrescriberDetailsAsync(dentist, "DMD", "NEW-LICENCE", "Ohio", null);
        await using (var db = _fixture.CreateContext())
            await db.Patients.Where(p => p.Id == Guid.Parse(_patient)).ExecuteUpdateAsync(u => u.SetProperty(p => p.City, "Renamedville"));
        var text = await IssueTextAsync(dentist, issue);
        Assert.Contains("OLD-LICENCE (Texas)", text);
        Assert.DoesNotContain("NEW-LICENCE", text);
        Assert.DoesNotContain("Renamedville", text);
    }

    [Fact]
    public async Task A_missing_patient_address_refuses_with_print_incomplete_and_issues_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var prescription = await FinalizedAsync(dentist);
        await using (var db = _fixture.CreateContext())
            await db.Patients.Where(p => p.Id == Guid.Parse(_patient)).ExecuteUpdateAsync(u => u.SetProperty(p => p.AddressLine1, ""));
        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/prescriptions/{prescription}/issues", new { kind = "Print", idempotencyKey = "no-address" });
        Assert.Equal((HttpStatusCode.Conflict, "print_incomplete"), (refused.Status, Error(refused.Body)));
        Assert.Contains("address", Str(refused.Body, "message"));
        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.PrescriptionIssues.CountAsync());
    }

    [Fact]
    public async Task The_retired_browser_print_is_refused_as_unverifiable()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await AllergyListAsync(dentist);
        var r = await dentist.SendAsync(HttpMethod.Get, $"/api/prescriptions/{await FinalizedAsync(dentist)}/print");
        Assert.Equal((HttpStatusCode.Gone, "legacy_print_unverifiable"), (r.Status, Error(r.Body)));
        Assert.Contains("record copy", Str(r.Body, "message"));
    }

    [Theory]
    [InlineData("123456789")] [InlineData("12345678901")] [InlineData("12345678a0")]
    public async Task An_npi_that_is_not_exactly_ten_digits_is_refused(string npi)
    {
        var admin = await _api.SessionAsync("Admin");
        var staff = await admin.SendAsync(HttpMethod.Post, "/api/config/staff", new { displayName = $"Dr {npi}", jobTitle = (string?)null, userAccountId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, staff.Status);
        var r = await admin.SendAsync(HttpMethod.Put, $"/api/config/staff/{Str(staff.Body, "id")}/prescriber", new { npi, rowVersion = Str(staff.Body, "rowVersion") });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
    }

    [Fact]
    public async Task Only_an_administrator_records_prescriber_details_and_the_audit_keeps_no_values()
    {
        var admin = await _api.SessionAsync("Admin");
        var staff = await admin.SendAsync(HttpMethod.Post, "/api/config/staff", new { displayName = "Dr Details", jobTitle = (string?)null, userAccountId = (Guid?)null });
        var id = Str(staff.Body, "id");
        Assert.Equal(HttpStatusCode.Forbidden, (await (await _api.SessionAsync("OfficeManager")).SendAsync(HttpMethod.Put, $"/api/config/staff/{id}/prescriber", new { npi = "1234567893", rowVersion = Str(staff.Body, "rowVersion") })).Status);
        var saved = await admin.SendAsync(HttpMethod.Put, $"/api/config/staff/{id}/prescriber", new { credentialLabel = "DDS", licenseNumber = "LIC-777", licenseJurisdiction = "Texas", npi = "1234567893", rowVersion = Str(staff.Body, "rowVersion") });
        Assert.Equal((HttpStatusCode.OK, "LIC-777", "1234567893"), (saved.Status, Str(saved.Body, "licenseNumber"), Str(saved.Body, "npi")));
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == Guid.Parse(id) && a.Details == "Staff prescriber details updated.").ToListAsync();
        Assert.Single(audit);
        Assert.DoesNotContain(await db.AuditLogEntries.AsNoTracking().Select(a => a.Details).ToListAsync(), d => d != null && (d.Contains("LIC-777") || d.Contains("1234567893")));
    }
}
