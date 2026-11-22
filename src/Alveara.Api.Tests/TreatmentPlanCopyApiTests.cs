using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Treatment;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S7 (D3) at the HTTP boundary: a treatment plan's patient copy is issued on the server once per key and produced as a PDF rebuilt only from the recorded revision, the item-state cut-off,
/// the diagnosis labels as of the issue and the envelope. Later renames, revisions, answers, withdrawals and diagnosis corrections do not change a reprint; a source that no longer matches refuses with
/// 409 <c>integrity_mismatch</c> before rendering. PDF text is read with PdfPig (rendered text, never bytes). A real API, real SQL Server, the real PDF engine and a signed-in session per role.
/// </summary>
public class TreatmentPlanCopyApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _annEncounter;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        _annEncounter = e.Id;
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;
    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;

    private static string PdfText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var text = string.Join("\n", document.GetPages().Select(p => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p)));
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    private async Task<JsonElement> DiagnosisAsync(Session dentist, string label)
    {
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _annEncounter, label, toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private async Task<string> ProcedureAsync(Session admin, string code, string description, decimal fee)
    {
        var r = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code, description, category = "Periodontic", scope = "WholeMouth", fee });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body.GetProperty("summary").GetProperty("id").GetString()!;
    }

    /// <summary>A two-procedure plan: deep cleaning ($100) and polish ($20), both for one diagnosis.</summary>
    private async Task<(JsonElement Plan, JsonElement Diagnosis)> PlanAsync(Session dentist, Session admin, string key = "plan-1")
    {
        var diagnosis = await DiagnosisAsync(dentist, "Chronic periodontitis");
        var items = new[]
        {
            new { diagnosisId = Id(diagnosis), procedureId = await ProcedureAsync(admin, $"DC-{key}", "Deep cleaning", 100m), toothKey = (string?)null, surface = (string?)null },
            new { diagnosisId = Id(diagnosis), procedureId = await ProcedureAsync(admin, $"PO-{key}", "Polish", 20m), toothKey = (string?)null, surface = (string?)null },
        };
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans", new { idempotencyKey = key, title = "Gum health plan", items });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return (r.Body, diagnosis);
    }

    private static async Task<string> IssueAsync(Session by, string plan, string key, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var r = await by.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{plan}/copy-issues", new { idempotencyKey = key });
        Assert.Equal(expected, r.Status);
        return Id(r.Body);
    }

    private static async Task<string> TextAsync(Session by, string issue)
    {
        var response = await by.Client.GetAsync($"/api/treatment-plan-copy-issues/{issue}/pdf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        return PdfText(await response.Content.ReadAsByteArrayAsync());
    }

    private static async Task<(HttpStatusCode Status, string? Error)> RefusedPdfAsync(Session by, string issue)
    {
        var response = await by.Client.GetAsync($"/api/treatment-plan-copy-issues/{issue}/pdf");
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body.StartsWith('{') ? JsonDocument.Parse(body).RootElement.GetProperty("error").GetString() : null);
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_both_routes()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"/api/treatment-plans/{id}/copy-issues", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/treatment-plan-copy-issues/{id}/pdf")).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Whoever_may_read_plans_may_issue_and_open_a_copy_and_no_one_else(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (plan, _) = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        var issue = await IssueAsync(dentist, Id(plan), "by-dentist");
        var by = await _api.SessionAsync(role);
        var issued = await by.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/copy-issues", new { idempotencyKey = $"role-{role}" });
        var pdf = await by.Client.GetAsync($"/api/treatment-plan-copy-issues/{issue}/pdf");
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, issued.Status);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, pdf.StatusCode);
    }

    [Fact]
    public async Task Issuing_refuses_a_missing_key_an_unknown_plan_and_a_withdrawn_plan_and_an_unknown_issue_is_404()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (plan, _) = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        var noKey = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/copy-issues", new { idempotencyKey = " " });
        var unknown = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Guid.NewGuid()}/copy-issues", new { idempotencyKey = "k" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (noKey.Status, Error(noKey.Body)));
        Assert.Equal((HttpStatusCode.NotFound, "plan_not_found"), (unknown.Status, Error(unknown.Body)));
        Assert.Equal((HttpStatusCode.NotFound, "plan_copy_issue_not_found"), await RefusedPdfAsync(dentist, Guid.NewGuid().ToString()));

        var issue = await IssueAsync(dentist, Id(plan), "before-withdrawal");
        var withdrawn = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/withdraw", new { reason = "Entered in error", rowVersion = Version(plan) });
        Assert.Equal(HttpStatusCode.OK, withdrawn.Status);
        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/copy-issues", new { idempotencyKey = "after-withdrawal" });
        Assert.Equal((HttpStatusCode.Conflict, "plan_withdrawn"), (refused.Status, Error(refused.Body)));
        Assert.Contains("Gum health plan", await TextAsync(dentist, issue));                                       // an earlier issue is history and still reprints
    }

    // ---------- issue and content ----------

    [Fact]
    public async Task A_copy_is_issued_once_per_key_and_its_pdf_shows_the_patient_words_the_mrn_and_the_estimate_and_records_the_print()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (plan, _) = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        var issue = await IssueAsync(dentist, Id(plan), "c-1");
        Assert.Equal(issue, await IssueAsync(dentist, Id(plan), "c-1", HttpStatusCode.OK));                       // a retry returns the same issue

        var text = await TextAsync(dentist, issue);
        long mrn;
        await using (var db = _fixture.CreateContext()) mrn = (await db.Patients.SingleAsync(p => p.Id == _ann)).Mrn;
        foreach (var expected in new[]
                 {
                     "Your treatment plan: Gum health plan", "Prepared for Ann Lee on", $"MRN: {mrn}", "Your dentist has proposed the care below.", "Deep cleaning", "Polish", "For: Chronic periodontitis",
                     "Estimated fee: $100.00 · Your answer: not yet given", "Estimated total: $120.00", TreatmentPlanCopyPdf.EstimateNote, "Plan version 1", "template version v2",
                 })
            Assert.Contains(expected, text);
        Assert.DoesNotContain("Plan reference", text);
        Assert.DoesNotContain("accepted so far", text);

        await using var check = _fixture.CreateContext();
        var stored = await check.TreatmentPlanCopyIssues.SingleAsync(i => i.Id == Guid.Parse(issue));
        Assert.Equal((1, "v2", (string?)null), (stored.RevisionNumber, stored.TemplateVersion, stored.ReferenceCodeAtIssue));
        Assert.Equal(1, await check.TreatmentPlanCopyIssues.CountAsync(i => i.PlanId == Guid.Parse(Id(plan))));
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.EventType == "TreatmentPlanCopyIssued" && a.CorrelationId == stored.CorrelationId));
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.EventType == "TreatmentPlanCopyPdfProduced" && a.CorrelationId == stored.CorrelationId));
    }

    [Fact]
    public async Task A_reprint_is_unchanged_by_a_later_rename_revision_answer_withdrawal_and_diagnosis_correction_while_a_new_issue_shows_them()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (plan, diagnosis) = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        var first = await IssueAsync(dentist, Id(plan), "before");
        var items = plan.GetProperty("items").EnumerateArray().ToList();
        var deep = items.Single(i => i.GetProperty("procedureDescription").GetString() == "Deep cleaning");
        var polish = items.Single(i => i.GetProperty("procedureDescription").GetString() == "Polish");

        var step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/rename", new { reason = "Clearer title", title = "Renamed plan", rowVersion = Version(plan) });
        Assert.Equal(HttpStatusCode.OK, step.Status);
        step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/revise", new { reason = "Explained", patientExplanation = "NEW WORDS for the patient.", rowVersion = Version(step.Body) });
        Assert.Equal(HttpStatusCode.OK, step.Status);
        step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/items/{Id(deep)}/decision", new { decision = "Accepted", rowVersion = Version(step.Body) });
        Assert.Equal(HttpStatusCode.OK, step.Status);
        step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/items/{Id(polish)}/withdraw", new { reason = "Not needed", rowVersion = Version(step.Body) });
        Assert.Equal(HttpStatusCode.OK, step.Status);
        var corrected = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{Id(diagnosis)}/correct", new { rowVersion = Version(diagnosis), label = "Gingivitis CORRECTED", reason = "Wrong label" });
        Assert.Equal(HttpStatusCode.OK, corrected.Status);

        var reprint = await TextAsync(dentist, first);
        Assert.Contains("Your treatment plan: Gum health plan", reprint);
        Assert.Contains("Your dentist has proposed the care below.", reprint);
        Assert.Contains("Estimated fee: $100.00 · Your answer: not yet given", reprint);
        Assert.Contains("Polish", reprint);
        Assert.Contains("For: Chronic periodontitis", reprint);
        Assert.Contains("Estimated total: $120.00", reprint);
        Assert.Contains("Plan version 1", reprint);
        foreach (var later in new[] { "Renamed plan", "NEW WORDS", "CORRECTED", "accepted" }) Assert.DoesNotContain(later, reprint);

        var now = await TextAsync(dentist, await IssueAsync(dentist, Id(plan), "after"));
        Assert.Contains("Your treatment plan: Renamed plan", now);
        Assert.Contains("NEW WORDS for the patient.", now);
        Assert.Contains("For: Gingivitis CORRECTED", now);
        Assert.Contains("Estimated fee: $100.00 · Your answer: accepted", now);
        Assert.DoesNotContain("Polish", now);
        Assert.Contains("Estimated total: $100.00", now);
        Assert.Contains("Of that, the part you have accepted so far: $100.00", now);
    }

    // ---------- the reference code and integrity ----------

    [Fact]
    public async Task A_reference_code_set_after_the_issue_leaves_the_reprint_without_it_and_a_code_at_issue_is_printed()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (plan, _) = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        var without = await IssueAsync(dentist, Id(plan), "no-code");
        var coded = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/reference-code", new { code = "PLAN-7", rowVersion = Version(plan) });
        Assert.Equal(HttpStatusCode.OK, coded.Status);
        Assert.DoesNotContain("Plan reference", await TextAsync(dentist, without));                                   // null at issue stays null
        Assert.Contains("Plan version 1 · Plan reference: PLAN-7", await TextAsync(dentist, await IssueAsync(dentist, Id(plan), "with-code")));
    }

    /// <summary>Inserts an issue row directly (the table is append-only, so inserting is allowed) with the given recorded values, to prove the rebuild refuses rather than guessing.</summary>
    private async Task<string> RecordedIssueAsync(string plan, string? referenceCode, string cutoffJson, int revision = 1)
    {
        await using var db = _fixture.CreateContext();
        var actor = await db.UserAccounts.Select(u => u.Id).FirstAsync();
        var issue = new TreatmentPlanCopyIssue
        {
            Id = Guid.NewGuid(), PlanId = Guid.Parse(plan), RevisionNumber = revision, ItemStateCutoffJson = cutoffJson, ReferenceCodeAtIssue = referenceCode, IssuedAtUtc = DateTimeOffset.UtcNow,
            IssuedByUserId = actor, TemplateVersion = "v2", EnvelopeJson = JsonSerializer.Serialize(new PlanCopyEnvelope(new PlanCopyPractice(null, null, null), "Ann Lee", 1)),
            IdempotencyKey = Guid.NewGuid().ToString("N"), CorrelationId = Guid.NewGuid(),
        };
        db.TreatmentPlanCopyIssues.Add(issue);
        await db.SaveChangesAsync();
        return issue.Id.ToString();
    }

    [Fact]
    public async Task A_copy_whose_recorded_sources_no_longer_match_is_refused_with_integrity_mismatch_before_rendering()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (plan, _) = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        var coded = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/reference-code", new { code = "PLAN-8", rowVersion = Version(plan) });
        Assert.Equal(HttpStatusCode.OK, coded.Status);
        var items = plan.GetProperty("items").EnumerateArray().Select(i => Guid.Parse(Id(i))).ToList();
        var good = JsonSerializer.Serialize(items.ToDictionary(i => i, _ => 0));
        int printsBefore;
        await using (var db = _fixture.CreateContext()) printsBefore = await db.AuditLogEntries.CountAsync(a => a.EventType == "TreatmentPlanCopyPdfProduced");

        Assert.Contains("Plan reference: PLAN-8", await TextAsync(dentist, await RecordedIssueAsync(Id(plan), "PLAN-8", good)));                  // the control: the same row with true values prints
        foreach (var bad in new[]
                 {
                     await RecordedIssueAsync(Id(plan), "OTHER-CODE", good),                                                                         // the code recorded differs from the plan's
                     await RecordedIssueAsync(Id(plan), "PLAN-8", JsonSerializer.Serialize(items.ToDictionary(i => i, _ => 99))),                    // a state that does not exist
                     await RecordedIssueAsync(Id(plan), "PLAN-8", JsonSerializer.Serialize(items.Take(1).ToDictionary(i => i, _ => 0))),             // a procedure missing from the cut-off
                     await RecordedIssueAsync(Id(plan), "PLAN-8", good, revision: 5),                                                                // a revision that does not exist
                     await RecordedIssueAsync(Id(plan), "PLAN-8", "not json"),                                                                       // an unreadable cut-off
                 })
            Assert.Equal((HttpStatusCode.Conflict, "integrity_mismatch"), await RefusedPdfAsync(dentist, bad));
        await using var after = _fixture.CreateContext();
        Assert.Equal(printsBefore + 1, await after.AuditLogEntries.CountAsync(a => a.EventType == "TreatmentPlanCopyPdfProduced"));             // only the control was produced
    }
}
