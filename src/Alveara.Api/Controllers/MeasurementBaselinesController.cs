using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Reporting;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N006 slice 5b: setting a baseline. Needs <see cref="Permission.ManageMeasurementBaselines"/> (reading baselines and comparing needs <see cref="Permission.ViewMeasurementReports"/>, in <see cref="MeasurementController"/>). A POST with a CSRF
/// token and an <c>Idempotency-Key</c> header: 201 for a new baseline, 200 with the same baseline for a repeat of the same request, 409 <c>key_reused</c> for the same key with a different request. The body is the name and the
/// first and last day of a period that is already over.
/// </summary>
[ApiController]
[Route("api/reports/measurement/baselines")]
[Authorize]
[RequirePermission(Permission.ManageMeasurementBaselines)]
public class MeasurementBaselinesController(MeasurementBaselineService baselines) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    [RequireCsrfToken]
    public async Task<IActionResult> Set([FromBody] SetBaselineInput? input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await baselines.SetAsync(Request.Headers["Idempotency-Key"].FirstOrDefault(), input ?? new SetBaselineInput(null, null, null), Actor, ct);
            return result.Created ? Created($"/api/reports/measurement/baselines/{result.Baseline.Id}", result.Baseline) : Ok(result.Baseline);
        }
        catch (ReportException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
