using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Prescribing;

namespace Alveara.Api.Controllers;

/// <summary>
/// A prescription entry as the client sends it. <c>doseValue</c> is a number; <c>reviewNote</c> is the prescriber's own words and is required only when the allergy check found a match or could not say anything.
/// </summary>
public record SavePrescriptionRequest(string? IdempotencyKey, string? Medication, decimal? DoseValue, string? DoseUnit, string? Frequency, string? ReviewNote);

/// <summary>
/// STORY-016: prescriptions. Writing (checking an entry and saving it) needs <see cref="Permission.ManagePrescriptions"/> (dentist, administrator) plus a CSRF token for the writes; reading needs
/// <see cref="Permission.ViewPrescriptions"/> (dentist, hygienist, assistant, administrator). A refusal returns a stable <c>error</c> code, a message safe to show and, where there are any, <c>problems</c> (each with
/// its field, code and message) and the <c>assessment</c> the allergy check made: <c>validation_failed</c> (400), <c>allergy_review_required</c> (409: the prescriber's own review note is needed),
/// <c>idempotency_key_reused</c> (409), <c>allergy_check_unavailable</c> and <c>save_failed</c> (503, nothing saved). Saving twice with the same <c>idempotencyKey</c> and entry returns the same prescription
/// (<c>replayed</c> true); a prescription is never changed or deleted.
/// </summary>
[ApiController]
[Authorize]
public class PrescriptionsController(PrescriptionService prescriptions) : ControllerBase
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
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, problems = ex.Problems, assessment = ex.Assessment });
        }
    }

    private static PrescriptionInput ToInput(SavePrescriptionRequest r) => new(r.Medication, r.DoseValue, r.DoseUnit, r.Frequency, r.ReviewNote, r.IdempotencyKey);

    /// <summary>The closed lists, the typing-error ceilings and the two limitation sentences the screen shows.</summary>
    [HttpGet("api/prescriptions/options")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public IActionResult Options() => Ok(PrescriptionService.Options());

    [HttpGet("api/patients/{patientId:guid}/prescriptions")]
    [RequirePermission(Permission.ViewPrescriptions)]
    public Task<IActionResult> List(Guid patientId, CancellationToken ct) => Run(async () => Ok(new { patientId, prescriptions = await prescriptions.ListAsync(patientId, ct) }));

    /// <summary>Judges an entry without saving it: every problem, the allergy assessment, and whether the prescriber's review is required.</summary>
    [HttpPost("api/patients/{patientId:guid}/prescriptions/check")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Check(Guid patientId, [FromBody] SavePrescriptionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await prescriptions.PreviewAsync(patientId, ToInput(request), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/prescriptions")]
    [RequirePermission(Permission.ManagePrescriptions)]
    [RequireCsrfToken]
    public Task<IActionResult> Save(Guid patientId, [FromBody] SavePrescriptionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await prescriptions.RecordAsync(patientId, ToInput(request), Actor, ct)));
}
