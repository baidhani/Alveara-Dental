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
public class PrescriptionWorkflowController(PrescriptionDraftService drafts, PrescriptionFinalizeService finalizer, PrescriptionLifecycleService lifecycle, PrescriptionRecordReader records) : ControllerBase
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

    /// <summary>The prescription laid out to print. The print is logged with the user and the time; if the log cannot be written nothing is returned.</summary>
    [HttpGet("api/prescriptions/{prescriptionId:guid}/print")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public Task<IActionResult> Print(Guid prescriptionId, CancellationToken ct) => Run(async () => Ok(await lifecycle.PrintAsync(prescriptionId, Actor, ct)));

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
