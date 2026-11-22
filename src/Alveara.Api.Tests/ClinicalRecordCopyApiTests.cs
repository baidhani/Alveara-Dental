using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S8 (D4) at the HTTP boundary: a clinical record copy is issued and returned as a PDF in one request, titled "Clinical record copy generated as of ...", with the patient's name and MRN and
/// the readable-record sections. Every new key is a new issue; the same key returns the same issue. Only holders of ExportPatientReadableRecord may make one (the administrator by default).
/// PDF text is read with PdfPig (rendered text, never bytes). A real API, real SQL Server and the real PDF engine.
/// </summary>
public class ClinicalRecordCopyApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string PdfText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var text = string.Join("\n", document.GetPages().Select(p => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p)));
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    private string Url(Guid? patient = null) => $"/api/patients/{patient ?? _ann}/clinical-record-copies";

    /// <summary>Posts with the session's CSRF token and returns the raw response (a PDF, or a JSON refusal).</summary>
    private async Task<HttpResponseMessage> PostAsync(SchedulingApiHarness.Session by, object body, Guid? patient = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Url(patient)) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-CSRF-Token", by.Csrf);
        return await by.Client.SendAsync(message);
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString()!;

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var anon = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync(Url(), new { idempotencyKey = "k" })).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", false)] [InlineData("Hygienist", false)] [InlineData("Assistant", false)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_holders_of_the_readable_record_right_may_make_a_copy(string role, bool allowed)
    {
        var r = await PostAsync(await _api.SessionAsync(role), new { idempotencyKey = $"role-{role}" });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.StatusCode);
        await using var db = _fixture.CreateContext();
        Assert.Equal(allowed ? 1 : 0, await db.ClinicalRecordCopyIssues.CountAsync());
    }

    [Fact]
    public async Task A_copy_is_a_pdf_generated_as_of_now_with_the_name_the_mrn_and_the_sections_and_is_recorded_and_audited()
    {
        var admin = await _api.SessionAsync("Admin");
        var r = await PostAsync(admin, new { idempotencyKey = "c-1" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("false", r.Headers.GetValues("X-Record-Copy-Replayed").Single());
        var id = Guid.Parse(r.Headers.GetValues("X-Record-Copy-Id").Single());
        var text = PdfText(await r.Content.ReadAsByteArrayAsync());

        await using var db = _fixture.CreateContext();
        var patient = await db.Patients.SingleAsync(p => p.Id == _ann);
        var issue = await db.ClinicalRecordCopyIssues.SingleAsync(i => i.Id == id);
        Assert.Contains("Clinical record copy generated as of", text);
        Assert.Contains($"MRN: {patient.Mrn}", text);
        Assert.Contains(patient.LastName, text);
        Assert.Contains("template version v1", text);
        Assert.Equal((_ann, "v1"), (issue.PatientId, issue.TemplateVersion));
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == "ClinicalRecordCopyIssued" && a.CorrelationId == issue.CorrelationId));
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == "ClinicalRecordCopyPdfProduced" && a.CorrelationId == issue.CorrelationId));
        Assert.DoesNotContain(await db.AuditLogEntries.Where(a => a.CorrelationId == issue.CorrelationId).Select(a => a.Details).ToListAsync(), d => d.Contains(patient.LastName));   // PHI-free
    }

    [Fact]
    public async Task A_second_request_is_a_new_issue_and_a_retry_with_the_same_key_returns_the_same_issue()
    {
        var admin = await _api.SessionAsync("Admin");
        var first = await PostAsync(admin, new { idempotencyKey = "a" });
        var retry = await PostAsync(admin, new { idempotencyKey = "a" });
        var second = await PostAsync(admin, new { idempotencyKey = "b" });
        Assert.All(new[] { first, retry, second }, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        string Id(HttpResponseMessage r) => r.Headers.GetValues("X-Record-Copy-Id").Single();
        Assert.Equal(Id(first), Id(retry));
        Assert.Equal("true", retry.Headers.GetValues("X-Record-Copy-Replayed").Single());
        Assert.NotEqual(Id(first), Id(second));
        await using var db = _fixture.CreateContext();
        var issues = await db.ClinicalRecordCopyIssues.Where(i => i.PatientId == _ann).ToListAsync();
        Assert.Equal(2, issues.Count);
        Assert.Contains("Clinical record copy generated as of", PdfText(await second.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task A_missing_key_or_an_unknown_patient_is_refused_and_nothing_is_recorded()
    {
        var admin = await _api.SessionAsync("Admin");
        var noKey = await PostAsync(admin, new { idempotencyKey = " " });
        var unknown = await PostAsync(admin, new { idempotencyKey = "k" }, Guid.NewGuid());
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (noKey.StatusCode, await ErrorAsync(noKey)));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (unknown.StatusCode, await ErrorAsync(unknown)));
        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.ClinicalRecordCopyIssues.CountAsync());
    }
}
