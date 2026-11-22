using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Pdf;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

namespace Alveara.Api.Controllers;

public record IssuePlanCopyRequest(string? IdempotencyKey);   // ALV-N026 S7

/// <summary>
/// ALV-N026 S7 (D3): the treatment plan's patient copy, issued and produced on the server. Both routes need <see cref="Permission.ViewClinicalDocumentation"/>, the right that reads plans (readers may
/// issue a copy, as they could print the browser copy before). A refusal returns a stable <c>error</c> code: 404 <c>plan_not_found</c> / <c>plan_copy_issue_not_found</c>, 409 <c>plan_withdrawn</c>,
/// 409 <c>integrity_mismatch</c> (checked before rendering), 503 <c>pdf_engine_unavailable</c> or <c>pdf_engine_untrusted</c>.
/// </summary>
[ApiController]
[Authorize]
public class TreatmentPlanCopyIssuesController(TreatmentPlanCopyIssueService issues, PdfEngine pdf, IPracticeClock clock, AlveraDbContext db) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TreatmentPlanException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }

    /// <summary>Issues the plan's patient copy at its current revision. Once per idempotency key: a retry returns the same issue (200), a new issue is 201.</summary>
    [HttpPost("api/treatment-plans/{planId:guid}/copy-issues")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    [RequireCsrfToken]
    public Task<IActionResult> Issue(Guid planId, [FromBody] IssuePlanCopyRequest request, CancellationToken ct) => Run(async () =>
    {
        var issued = await issues.IssueAsync(planId, request.IdempotencyKey, pdf.BundleId, Actor, ct);
        var body = new { id = issued.Issue.Id, planId, revisionNumber = issued.Issue.RevisionNumber, templateVersion = issued.Issue.TemplateVersion, issuedAtUtc = issued.Issue.IssuedAtUtc, replayed = issued.Replayed };
        return issued.Replayed ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    });

    /// <summary>
    /// An issue as a PDF, rebuilt from the recorded revision, cut-off and envelope (never from the current plan text, states, patient or practice). The print is recorded on the server against the
    /// patient resolved through the plan.
    /// </summary>
    [HttpGet("api/treatment-plan-copy-issues/{issueId:guid}/pdf")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> IssuePdf(Guid issueId, CancellationToken ct) => Run(async () =>
    {
        var (issue, patientId) = await issues.GetAsync(issueId, ct);
        var now = clock.UtcNow;
        var request = TreatmentPlanCopyPdf.For(issue, await issues.ContentAsync(issue, ct), clock, now);
        PdfRenderResult rendered;
        try
        {
            rendered = await pdf.RenderAsync(request.TemplateId, request.TemplateVersion, request.InputJson, ct);
        }
        catch (PdfEngineException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        await new PatientAccessRecorder(db).RecordPrintAsync(patientId, "TreatmentPlanPatientCopy", issue.PlanId, Actor, now, ct);
        AuditService.Record(db, "TreatmentPlanCopyPdfProduced", nameof(TreatmentPlanCopyIssue), issue.Id, Actor,
            $"{request.TemplateId} {request.TemplateVersion} with {rendered.BundleId}.", correlationId: issue.CorrelationId);
        await db.SaveChangesAsync(ct);
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(rendered.Pdf, "application/pdf", request.FileName);
    });
}
