using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Reporting;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N006 slice 3: the clinical reports (treatment-plan status, completed procedures, provider activity). Everything here needs <see cref="Permission.ViewClinicalReports"/>, which neither the operational nor the financial permission
/// includes. Same conventions as <see cref="ReportsController"/>: reads only, <c>no-store</c>, <c>from</c> and <c>to</c> required, stable error codes, every run and drill-down audited (counts and ids only), and a filter a
/// report cannot honestly apply is refused with <c>filter_not_supported</c>. The drill-downs return ids and facts only, never a patient's name or a procedure's note.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
[RequirePermission(Permission.ViewClinicalReports)]
public class ClinicalReportsController(ReportFilterResolver filters, TreatmentPlanStatusReportService plans, CompletedProceduresReportService procedures, ClinicalDrillService drill, ProviderActivityReportService activity, ProviderActivityDrillService activityDrill) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("treatment-plans")]
    public Task<IActionResult> TreatmentPlans([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await plans.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("treatment-plans/items")]
    public Task<IActionResult> PlanItems([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? cell, [FromQuery] int? limit, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await drill.PlanItemsAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(cell, limit, afterId), Actor, ct)));

    [HttpGet("completed-procedures")]
    public Task<IActionResult> CompletedProcedures([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await procedures.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("completed-procedures/procedures")]
    public Task<IActionResult> ProcedureRecords([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] Guid? providerId, [FromQuery] string? codeSystem, [FromQuery] string? code, [FromQuery] string? date, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterPerformedAtUtc, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await drill.ProceduresAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(providerId, codeSystem, code, date, limit, afterPerformedAtUtc, afterId), Actor, ct)));

    [HttpGet("provider-activity")]
    public Task<IActionResult> ProviderActivity([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await activity.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("provider-activity/appointments")]
    public Task<IActionResult> ActivityAppointments([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? measure, [FromQuery] Guid? providerId, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterStart, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await activityDrill.AppointmentsAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(measure, providerId, limit, afterStart, afterId), Actor, ct)));

    [HttpGet("provider-activity/procedures")]
    public Task<IActionResult> ActivityProcedures([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] Guid? providerId, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterPerformedAtUtc, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await drill.ProceduresAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(providerId, null, null, null, limit, afterPerformedAtUtc, afterId), Actor, ct, ReportIds.ProviderActivity)));

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
