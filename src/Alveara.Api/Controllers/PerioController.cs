using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Periodontal;

namespace Alveara.Api.Controllers;

/// <summary>One probed site as the client sends it. Every field is required: a value left out is reported, never defaulted (a missing depth is not "0 mm", and a missing bleeding answer is not "no").</summary>
public record PerioReadingRequest(string? ToothKey, string? Site, int? ProbingDepthMm, int? RecessionMm, bool? Bleeding);
public record RecordPerioChartRequest(string? IdempotencyKey, List<PerioReadingRequest?>? Readings);

/// <summary>
/// STORY-012: periodontal charting. Reading a patient's charts needs <see cref="Permission.ViewClinicalDocumentation"/>; saving a chart needs <see cref="Permission.ManageClinicalNotes"/> plus a
/// CSRF token - the same permissions as the odontogram and the clinical notes it sits beside. A refusal returns a stable <c>error</c> code, a message safe to show and, for
/// <c>validation_failed</c>, <c>problems</c>: every entry that needs correcting, each with its tooth, site, field, code and message. The tooth is always the FDI key. Saving twice with
/// the same <c>idempotencyKey</c> and readings returns the same chart (200 both times); the same key with different readings is a 409.
/// </summary>
[ApiController]
[Authorize]
public class PerioController(PerioService perio) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PerioException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, problems = ex.Problems });
        }
    }

    [HttpGet("api/patients/{patientId:guid}/periodontal/charts")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Charts(Guid patientId, [FromQuery] int? take, CancellationToken ct) => Run(async () => Ok(await perio.HistoryAsync(patientId, take, ct)));

    [HttpPost("api/patients/{patientId:guid}/periodontal/charts")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Record(Guid patientId, [FromBody] RecordPerioChartRequest request, CancellationToken ct) =>
        Run(async () => Ok(await perio.RecordAsync(patientId, request.IdempotencyKey, ToInputs(request.Readings), Actor, ct)));

    /// <summary>
    /// Turns the request into readings, reporting every missing value as a problem together with the rule problems of what was sent, so one refusal lists everything to fix.
    /// </summary>
    private static List<PerioReadingInput>? ToInputs(List<PerioReadingRequest?>? requests)
    {
        if (requests is null) return null;
        var missing = new List<PerioProblem>();
        var inputs = new List<PerioReadingInput>();
        foreach (var r in requests)
        {
            if (r is null) { missing.Add(new(null, null, "readings", "invalid", "A reading is missing.")); continue; }
            var before = missing.Count;
            if (r.ProbingDepthMm is null) missing.Add(new(r.ToothKey, r.Site, "probingDepthMm", "required", $"Enter the probing depth for tooth {r.ToothKey} site {r.Site}."));
            if (r.RecessionMm is null) missing.Add(new(r.ToothKey, r.Site, "recessionMm", "required", $"Enter the recession for tooth {r.ToothKey} site {r.Site}, or 0 if there is none."));
            if (r.Bleeding is null) missing.Add(new(r.ToothKey, r.Site, "bleeding", "required", $"Say whether tooth {r.ToothKey} site {r.Site} bled on probing (yes or no)."));
            if (missing.Count == before) inputs.Add(new PerioReadingInput(r.ToothKey!, r.Site!, r.ProbingDepthMm!.Value, r.RecessionMm!.Value, r.Bleeding!.Value));
        }
        if (missing.Count == 0) return inputs;
        var all = missing.Concat(inputs.Count == 0 ? [] : PerioRules.Validate(inputs)).ToList();
        throw new PerioException("validation_failed", "The chart has entries that need correcting. Nothing was saved.", 400, all);
    }
}
