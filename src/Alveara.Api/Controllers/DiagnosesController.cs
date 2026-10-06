using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

/// <summary>A diagnosis entry as the client sends it. <c>treatmentPlanReference</c> is optional: left out or null means none.</summary>
public record RecordDiagnosisRequest(string? IdempotencyKey, Guid? EncounterId, string? Label, string? ToothKey, string? Notes, string? TreatmentPlanReference);

/// <summary>
/// A correction: the corrected label, tooth and notes, a reason, the row version read, and what to do with the treatment-plan reference. Leaving <c>treatmentPlanReference</c> out keeps the current one, a value
/// replaces it, and only <c>clearTreatmentPlanReference: true</c> removes it.
/// </summary>
public record CorrectDiagnosisRequest(string? RowVersion, string? Label, string? ToothKey, string? Notes, string? TreatmentPlanReference, bool? ClearTreatmentPlanReference, string? Reason);

public record WithdrawDiagnosisRequest(string? RowVersion, string? Reason);

/// <summary>
/// STORY-013: structured diagnoses. Reading needs <see cref="Permission.ViewClinicalDocumentation"/>; recording, correcting and withdrawing need <see cref="Permission.ManageClinicalNotes"/> plus a CSRF token
/// (the same permissions as the rest of clinical documentation). A refusal returns a stable <c>error</c> code, a message safe to show and, for <c>validation_failed</c>, <c>problems</c>: every entry that needs
/// correcting, each with its field, code and message. A stale correction or withdrawal is the shared 409 concurrency conflict. Recording twice with the same <c>idempotencyKey</c> and entry returns the same diagnosis
/// (200 both times); the same key with a different entry is a 409. The treatment-plan reference is an opaque forward reference and the response says so (<c>treatmentPlanReferenceState</c> is "Unresolved").
/// </summary>
[ApiController]
[Authorize]
public class DiagnosesController(DiagnosisService diagnoses) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DiagnosisException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, problems = ex.Problems });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    [HttpGet("api/patients/{patientId:guid}/diagnoses")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> List(Guid patientId, [FromQuery] Guid? encounterId, [FromQuery] bool includeWithdrawn, CancellationToken ct) =>
        Run(async () => Ok(new { patientId, diagnoses = await diagnoses.ListAsync(patientId, encounterId, includeWithdrawn, ct) }));

    [HttpPost("api/patients/{patientId:guid}/diagnoses")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Record(Guid patientId, [FromBody] RecordDiagnosisRequest request, CancellationToken ct) =>
        Run(async () => Ok(await diagnoses.RecordAsync(patientId, request.IdempotencyKey, new DiagnosisInput(request.EncounterId ?? Guid.Empty, request.Label, request.ToothKey, request.Notes, request.TreatmentPlanReference), Actor, ct)));

    [HttpGet("api/diagnoses/{diagnosisId:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid diagnosisId, CancellationToken ct) => Run(async () => Ok(await diagnoses.GetAsync(diagnosisId, ct)));

    [HttpGet("api/diagnoses/{diagnosisId:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> History(Guid diagnosisId, CancellationToken ct) => Run(async () => Ok(await diagnoses.HistoryAsync(diagnosisId, ct)));

    [HttpPost("api/diagnoses/{diagnosisId:guid}/correct")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Correct(Guid diagnosisId, [FromBody] CorrectDiagnosisRequest request, CancellationToken ct) =>
        Run(async () => Ok(await diagnoses.CorrectAsync(diagnosisId, request.RowVersion,
            new DiagnosisCorrection(request.Label, request.ToothKey, request.Notes, request.TreatmentPlanReference, request.ClearTreatmentPlanReference ?? false), request.Reason, Actor, ct)));

    [HttpPost("api/diagnoses/{diagnosisId:guid}/withdraw")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Withdraw(Guid diagnosisId, [FromBody] WithdrawDiagnosisRequest request, CancellationToken ct) =>
        Run(async () => Ok(await diagnoses.WithdrawAsync(diagnosisId, request.RowVersion, request.Reason, Actor, ct)));
}
