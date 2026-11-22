using System.Globalization;
using System.Text.Json;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-N026 S8 (D4): one clinical record copy issued as a PDF. It is directly patient-owned (it has no parent record), so it carries <see cref="PatientId"/>, is in <c>PatientOwnedData</c> and joins the
/// established merge machinery: a merge moves it with the patient (the append-only trigger stands aside for that one change), a merge-time change of any other column is refused (51234), and a merged-away
/// record takes no new copy (51235). Append-only otherwise (51309). No PDF and no identity envelope are stored (owner decision 2026-11-21): a copy is made only "as of now" and never regenerated.
/// </summary>
public class ClinicalRecordCopyIssue
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTimeOffset GeneratedAsOfUtc { get; set; }
    public Guid IssuedByUserId { get; set; }
    /// <summary>"v1" for every copy made here.</summary>
    public required string TemplateVersion { get; set; }
    public string? RenderBundleId { get; set; }
    /// <summary>Optional diagnostic; never an acceptance gate.</summary>
    public string? ContentHash { get; set; }
    /// <summary>Unique per patient: a retried request returns the first issue.</summary>
    public required string IdempotencyKey { get; set; }
    public Guid CorrelationId { get; set; }
}

/// <summary>An issued copy with the content to print and whether the issue already existed for that key.</summary>
public sealed record IssuedRecordCopy(ClinicalRecordCopyIssue Issue, ReadableRecord Record, long Mrn, bool Replayed);

/// <summary>
/// ALV-N026 S8: issues a clinical record copy from the readable-record content (<see cref="ReadableRecordLoader"/>, brief assumption A3). Every request with a new key is a new issue "generated as of" now;
/// there is no route that rebuilds an earlier copy. A retry with the same key returns the same issue (its "generated as of" time is kept; the content is read again, which on a retry seconds later is the same).
/// </summary>
public class ClinicalRecordCopyService(AlveraDbContext db, ReadableRecordLoader loader, IPracticeClock clock, ILogger<ClinicalRecordCopyService> logger)
{
    public const string TemplateVersion = "v1";

    public async Task<IssuedRecordCopy> IssueAsync(Guid patientId, string? idempotencyKey, string? renderBundleId, Guid actor, CancellationToken ct)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 100)
            throw new PatientException("validation_failed", "An idempotency key of 1 to 100 characters is required.", 400);
        await PatientExportSubject.RequireAsync(db, patientId, ct);                                  // 404 for an unknown record, 409 patient_merged for a merged-away one
        var mrn = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => p.Mrn).SingleAsync(ct);

        var existing = await db.ClinicalRecordCopyIssues.AsNoTracking().SingleOrDefaultAsync(i => i.PatientId == patientId && i.IdempotencyKey == key, ct);
        if (existing is not null) return new IssuedRecordCopy(existing, await loader.LoadAsync(patientId, ct), mrn, true);

        var issue = new ClinicalRecordCopyIssue
        {
            Id = Guid.NewGuid(), PatientId = patientId, GeneratedAsOfUtc = clock.UtcNow, IssuedByUserId = actor, TemplateVersion = TemplateVersion, RenderBundleId = renderBundleId,
            IdempotencyKey = key, CorrelationId = Guid.NewGuid(),
        };
        var record = await loader.LoadAsync(patientId, ct);
        db.ClinicalRecordCopyIssues.Add(issue);
        AuditService.Record(db, "ClinicalRecordCopyIssued", nameof(ClinicalRecordCopyIssue), issue.Id, actor, $"Clinical record copy issued (template {TemplateVersion}).", correlationId: issue.CorrelationId);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            // a concurrent retry with the same key won: return its issue; any other failure is not swallowed
            var winner = await db.ClinicalRecordCopyIssues.AsNoTracking().SingleOrDefaultAsync(i => i.PatientId == patientId && i.IdempotencyKey == key, ct);
            if (winner is null)
            {
                logger.LogWarning("record_copy_issue_not_saved error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, issue.CorrelationId);
                throw new PatientException("print_failed", "The clinical record copy could not be issued. Nothing was printed. Try again.", 503);
            }
            return new IssuedRecordCopy(winner, record, mrn, true);
        }
        return new IssuedRecordCopy(issue, record, mrn, false);
    }
}

/// <summary>ALV-N026 S8: the input for the <c>clinical-record-copy/v1</c> template: the readable record's summary and sections as tables, titled with the practice-local "generated as of" time and the MRN.</summary>
public static class ClinicalRecordCopyPdf
{
    public const string TemplateId = "clinical-record-copy";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public sealed record Request(string TemplateId, string TemplateVersion, string InputJson, string FileName);

    public static string Title(ClinicalRecordCopyIssue issue, IPracticeClock clock) =>
        $"Clinical record copy generated as of {clock.ToPracticeLocal(issue.GeneratedAsOfUtc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} ({clock.PracticeTimeZone.Id})";

    public static Request For(IssuedRecordCopy copy, IPracticeClock clock, DateTimeOffset producedAtUtc)
    {
        var input = new
        {
            title = Title(copy.Issue, clock),
            templateVersion = copy.Issue.TemplateVersion,
            issueId = copy.Issue.Id.ToString("N")[..8],
            produced = clock.ToPracticeLocal(producedAtUtc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            patientName = copy.Record.PatientName,
            mrn = copy.Mrn.ToString(CultureInfo.InvariantCulture),
            summary = copy.Record.Summary.Select(f => new { label = f.Label, value = f.Value }).ToList(),
            sections = copy.Record.Sections.Select(s => new { title = s.Title, headers = s.Headers, rows = s.Rows, note = s.Note }).ToList(),
        };
        return new Request(TemplateId, copy.Issue.TemplateVersion, JsonSerializer.Serialize(input, Json), $"Clinical-record-copy-{copy.Issue.Id.ToString("N")[..8]}.pdf");
    }
}
