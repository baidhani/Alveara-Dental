using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Reporting;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N006 slice 5: the measurement panel. Everything here needs <see cref="Permission.ViewMeasurementReports"/>. Same conventions as <see cref="ReportsController"/>: reads only, <c>no-store</c>, <c>from</c> and <c>to</c>
/// required, stable error codes, every run audited (counts only). Practice-wide: a provider or location filter is refused (<c>filter_not_supported</c>). <c>asOf</c> pins the moment the follow-up and note metrics are judged at.
/// </summary>
[ApiController]
[Route("api/reports/measurement")]
[Authorize]
[RequirePermission(Permission.ViewMeasurementReports)]
public class MeasurementController(ReportFilterResolver filters, MetricsService metrics) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("metrics")]
    public Task<IActionResult> Metrics([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, [FromQuery] DateTimeOffset? asOf, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await metrics.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct, asOf)));

    private async Task<IActionResult> GuardedAsync(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await action();
        }
        catch (ReportException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
