using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Architecture.Pdf;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Controllers;

public record IssueRecordCopyRequest(string? IdempotencyKey);   // ALV-N026 S8

/// <summary>
/// ALV-N026 S8 (D4): a clinical record copy, issued and returned as a PDF in the same request (it is made only "as of now"; there is no route that rebuilds an earlier copy). It needs
/// <see cref="Permission.ExportPatientReadableRecord"/>, the right that already gives the readable record (brief 3.4). A refusal returns a stable <c>error</c>: 400 <c>validation_failed</c>,
/// 404 <c>patient_not_found</c>, 409 <c>patient_merged</c>, 503 <c>pdf_engine_unavailable</c> or <c>pdf_engine_untrusted</c>. The <c>X-Record-Copy-Id</c> header names the issue, and
/// <c>X-Record-Copy-Replayed</c> says whether the key had been used before.
/// </summary>
[ApiController]
[Authorize]
public class ClinicalRecordCopiesController(ClinicalRecordCopyService copies, PdfEngine pdf, IPracticeClock clock, AlveraDbContext db) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("api/patients/{patientId:guid}/clinical-record-copies")]
    [RequirePermission(Permission.ExportPatientReadableRecord)]
    [RequireCsrfToken]
    public async Task<IActionResult> Issue(Guid patientId, [FromBody] IssueRecordCopyRequest request, CancellationToken ct)
    {
        try
        {
            var issued = await copies.IssueAsync(patientId, request.IdempotencyKey, pdf.BundleId, Actor, ct);
            var now = clock.UtcNow;
            var input = ClinicalRecordCopyPdf.For(issued, clock, now);
            PdfRenderResult rendered;
            try
            {
                rendered = await pdf.RenderAsync(input.TemplateId, input.TemplateVersion, input.InputJson, ct);
            }
            catch (PdfEngineException ex)
            {
                return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
            }
            await new PatientAccessRecorder(db).RecordPrintAsync(patientId, "ReadableRecord", issued.Issue.Id, Actor, now, ct);
            AuditService.Record(db, "ClinicalRecordCopyPdfProduced", nameof(ClinicalRecordCopyIssue), issued.Issue.Id, Actor,
                $"{input.TemplateId} {input.TemplateVersion} with {rendered.BundleId}.", correlationId: issued.Issue.CorrelationId);
            await db.SaveChangesAsync(ct);
            Response.Headers["Cache-Control"] = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["X-Record-Copy-Id"] = issued.Issue.Id.ToString();
            Response.Headers["X-Record-Copy-Replayed"] = issued.Replayed ? "true" : "false";
            return File(rendered.Pdf, "application/pdf", input.FileName);
        }
        catch (PatientException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
