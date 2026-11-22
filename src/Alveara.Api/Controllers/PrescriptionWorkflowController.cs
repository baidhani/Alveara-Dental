using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Prescribing;

namespace Alveara.Api.Controllers;

/// <summary>A draft as the client sends it. Only the entry's fields; <c>rowVersion</c> echoes the version the client read (required for a change), <c>idempotencyKey</c> is for creating.</summary>
public record WorkflowRequest(
    string? IdempotencyKey, string? Medication, decimal? DoseValue, string? DoseUnit, string? Frequency, string? Instructions, decimal? QuantityValue, string? QuantityUnit, int? Refills, string? ReviewNote, string? RowVersion);

public record AcknowledgeSafetyRequest(string? RowVersion, string? SeenFingerprint);
public record DiscardDraftRequest(string? RowVersion, string? Reason);
public record ReasonRequest(string? Reason);
public record AmendRequest(string? IdempotencyKey);
public record IssuePrescriptionRequest(string? Kind, string? IdempotencyKey);   // ALV-N026 S6

/// <summary>
/// ALV-016-C01: the prescription workflow (drafts, finalization, cancellation, discontinuation, amendment and printing). It adds to STORY-016's <see cref="PrescriptionsController"/> without changing it. Reading needs
/// <see cref="Permission.ViewPrescriptions"/>; every change needs <see cref="Permission.ManagePrescriptions"/> (dentist, administrator) plus a CSRF token. A refusal returns a stable <c>error</c> code, a message safe to show
/// and, where there are any, <c>problems</c>, the allergy <c>assessment</c> and a <c>detail</c> (what changed in the allergy and medication lists for <c>safety_context_changed</c>): <c>validation_failed</c> (400),
/// <c>row_version_required</c> and <c>row_version_invalid</c> (400), <c>*_not_found</c> (404), <c>allergy_review_required</c>, <c>safety_context_changed</c>, <c>idempotency_key_reused</c>, <c>draft_closed</c>,
/// <c>prescription_not_active</c>, <c>print_incomplete</c> and <c>concurrency_conflict</c> (409), <c>allergy_check_unavailable</c>, <c>save_failed</c> and <c>print_failed</c> (503). Nothing is saved on any refusal.
/// Preparing a prescription for printing is a read that is logged (who, when; never the contents).
/// </summary>
[ApiController]
[Authorize]
public class PrescriptionWorkflowController(PrescriptionDraftService drafts, PrescriptionFinalizeService finalizer, PrescriptionLifecycleService lifecycle, PrescriptionRecordReader records,
    PrescriptionIssueService issues, Alveara.Api.Architecture.Pdf.PdfEngine pdf, Alveara.Api.Architecture.Time.IPracticeClock clock, Alveara.Api.Data.AlveraDbContext db) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PrescriptionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, problems = ex.Problems, assessment = ex.Assessment, detail = ex.Detail });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    private static WorkflowInput ToInput(WorkflowRequest r) => new(r.Medication, r.DoseValue, r.DoseUnit, r.Frequency, r.Instructions, r.QuantityValue, r.QuantityUnit, r.Refills, r.ReviewNote, r.IdempotencyKey);

    // ---------- reads ----------

    [HttpGet("api/prescription-workflow/options")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public IActionResult Options() => Ok(PrescriptionDraftService.Options());

    [HttpGet("api/patients/{patientId:guid}/prescription-drafts")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public Task<IActionResult> ListDrafts(Guid patientId, [FromQuery] bool includeClosed, CancellationToken ct) =>
        Run(async () => Ok(new { patientId, drafts = await drafts.ListAsync(patientId, includeClosed, ct) }));

    [HttpGet("api/prescription-drafts/{draftId:guid}")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public Task<IActionResult> GetDraft(Guid draftId, CancellationToken ct) => Run(async () => Ok(await drafts.GetAsync(draftId, Actor, ct)));

    [HttpGet("api/patients/{patientId:guid}/prescription-records")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public Task<IActionResult> ListRecords(Guid patientId, CancellationToken ct) => Run(async () => Ok(new { patientId, prescriptions = await records.ListAsync(patientId, ct) }));

    [HttpGet("api/prescriptions/{prescriptionId:guid}")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public Task<IActionResult> GetRecord(Guid prescriptionId, CancellationToken ct) => Run(async () => Ok(await records.GetAsync(prescriptionId, ct)));

    /// <summary>
    /// ALV-N026 S6 (D2): the browser-laid-out p1 print is retired. A p1 printout was never recorded, so it cannot be reproduced as it was: 410 <c>legacy_print_unverifiable</c>. Issue a print or a
    /// record copy (p2) instead.
    /// </summary>
    [HttpGet("api/prescriptions/{prescriptionId:guid}/print")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public IActionResult Print(Guid prescriptionId) => StatusCode(StatusCodes.Status410Gone, new
    {
        error = "legacy_print_unverifiable",
        message = "An earlier printout of this prescription cannot be reproduced as it was. Issue a new record copy, dated today with today's status, instead.",
    });

    /// <summary>
    /// ALV-N026 S6 (D1, D2): issues a prescription as a print (Active only) or a record copy (any status; inactive ones are watermarked). Once per idempotency key: a retry returns the same issue.
    /// Reading staff may issue, as they could print before. 409 <c>print_incomplete</c> (missing patient address or details), <c>prescription_not_active</c>, <c>idempotency_key_reused</c>.
    /// </summary>
    [HttpPost("api/prescriptions/{prescriptionId:guid}/issues")]
    [RequirePermission(Permission.ViewPrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Issue(Guid prescriptionId, [FromBody] IssuePrescriptionRequest request, CancellationToken ct) => Run(async () =>
    {
        var issued = await issues.IssueAsync(prescriptionId, request.Kind, request.IdempotencyKey, pdf.BundleId, Actor, ct);
        var body = new { id = issued.Issue.Id, prescriptionId, kind = issued.Issue.IssueKind, status = issued.Issue.StatusAtIssue, templateVersion = issued.Issue.TemplateVersion, issuedAtUtc = issued.Issue.IssuedAtUtc, replayed = issued.Replayed };
        return issued.Replayed ? Ok(body) : StatusCode(StatusCodes.Status201Created, body);
    });

    /// <summary>
    /// ALV-N026 S6: an issue as a PDF, rebuilt from the immutable prescription, the status recorded at issue and the issue's envelope (never current patient, staff or practice data). The print is
    /// recorded on the server against the patient resolved through the prescription. 503 <c>pdf_engine_unavailable</c> or <c>pdf_engine_untrusted</c> while PDFs cannot be made.
    /// </summary>
    [HttpGet("api/prescription-issues/{issueId:guid}/pdf")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public Task<IActionResult> IssuePdf(Guid issueId, CancellationToken ct) => Run(async () =>
    {
        var (issue, patientId) = await issues.GetAsync(issueId, ct);
        var now = clock.UtcNow;
        var request = await issues.PdfAsync(issue, now, ct);
        Alveara.Api.Architecture.Pdf.PdfRenderResult rendered;
        try
        {
            rendered = await pdf.RenderAsync(request.TemplateId, request.TemplateVersion, request.InputJson, ct);
        }
        catch (Alveara.Api.Architecture.Pdf.PdfEngineException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        await new Alveara.Api.Architecture.Auditing.PatientAccessRecorder(db).RecordPrintAsync(patientId, "Prescription", issue.PrescriptionId, Actor, now, ct);
        Alveara.Api.Architecture.Auditing.AuditService.Record(db, "PrescriptionPdfProduced", nameof(PrescriptionIssue), issue.Id, Actor,
            $"{issue.IssueKind} {request.TemplateId} {request.TemplateVersion} with {rendered.BundleId}.", correlationId: issue.CorrelationId);
        await db.SaveChangesAsync(ct);
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(rendered.Pdf, "application/pdf", request.FileName);
    });

    // ---------- drafts ----------

    [HttpPost("api/patients/{patientId:guid}/prescription-drafts")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateDraft(Guid patientId, [FromBody] WorkflowRequest request, CancellationToken ct) =>
        Run(async () => Ok(await drafts.CreateAsync(patientId, ToInput(request), Actor, ct)));

    [HttpPut("api/prescription-drafts/{draftId:guid}")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateDraft(Guid draftId, [FromBody] WorkflowRequest request, CancellationToken ct) =>
        Run(async () => Ok(await drafts.UpdateAsync(draftId, request.RowVersion, ToInput(request), Actor, ct)));

    /// <summary>What finalizing would meet right now (problems, safety check, limitations) without changing anything.</summary>
    [HttpPost("api/prescription-drafts/{draftId:guid}/check")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> CheckDraft(Guid draftId, CancellationToken ct) => Run(async () => Ok(await drafts.CheckAsync(draftId, Actor, ct)));

    [HttpPost("api/prescription-drafts/{draftId:guid}/acknowledge-safety")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> AcknowledgeSafety(Guid draftId, [FromBody] AcknowledgeSafetyRequest request, CancellationToken ct) =>
        Run(async () => Ok(await drafts.AcknowledgeSafetyAsync(draftId, request.RowVersion, request.SeenFingerprint, Actor, ct)));

    [HttpPost("api/prescription-drafts/{draftId:guid}/finalize")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Finalize(Guid draftId, [FromBody] RowVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await finalizer.FinalizeAsync(draftId, request.RowVersion, Actor, ct)));

    [HttpPost("api/prescription-drafts/{draftId:guid}/discard")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Discard(Guid draftId, [FromBody] DiscardDraftRequest request, CancellationToken ct) =>
        Run(async () => Ok(await drafts.DiscardAsync(draftId, request.RowVersion, request.Reason, Actor, ct)));

    // ---------- finalized prescriptions ----------

    [HttpPost("api/prescriptions/{prescriptionId:guid}/amend")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Amend(Guid prescriptionId, [FromBody] AmendRequest request, CancellationToken ct) =>
        Run(async () => Ok(await drafts.AmendAsync(prescriptionId, request.IdempotencyKey, Actor, ct)));

    [HttpPost("api/prescriptions/{prescriptionId:guid}/cancel")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Cancel(Guid prescriptionId, [FromBody] ReasonRequest request, CancellationToken ct) =>
        Run(async () => Ok(await lifecycle.CancelAsync(prescriptionId, request.Reason, Actor, ct)));

    [HttpPost("api/prescriptions/{prescriptionId:guid}/discontinue")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Discontinue(Guid prescriptionId, [FromBody] ReasonRequest request, CancellationToken ct) =>
        Run(async () => Ok(await lifecycle.DiscontinueAsync(prescriptionId, request.Reason, Actor, ct)));
}
