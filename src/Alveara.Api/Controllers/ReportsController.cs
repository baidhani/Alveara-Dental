using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Reporting;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N006: the operational reports. Everything here needs <see cref="Permission.ViewOperationalReports"/>; later slices add the financial and clinical reports as their own routes and permissions. Reads only (GET,
/// so no CSRF token), <c>no-store</c> because a report describes the practice's patients. The filter is <c>from</c> and <c>to</c> (practice-local <c>yyyy-MM-dd</c>, both required), optional repeated
/// <c>providerIds</c> (booked providers) and an optional <c>locationId</c>. A refused request returns the stable <c>error</c> code the screens map to words. Each run and each drill-down is recorded in the audit
/// log (counts and ids only); if that cannot be saved the report is not returned (503 <c>audit_unavailable</c>).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
[RequirePermission(Permission.ViewOperationalReports)]
public class ReportsController(ReportFilterResolver filters, ScheduleReportService schedule, PatientFlowReportService patientFlow, ReportDrillService drill,
    FollowUpReportService followUps, FollowUpDrillService followUpDrill, CancellationReportService cancellations, CancellationDrillService cancellationDrill, ReportExportService export) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("schedule")]
    public Task<IActionResult> Schedule([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await schedule.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("patient-flow")]
    public Task<IActionResult> PatientFlow([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await patientFlow.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("schedule/appointments")]
    public Task<IActionResult> ScheduleAppointments([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? status, [FromQuery] string? date, [FromQuery] Guid? providerId, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterStart, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await drill.AppointmentsAsync(ReportIds.Schedule, await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(status, null, date, providerId, limit, afterStart, afterId), Actor, ct)));

    [HttpGet("patient-flow/appointments")]
    public Task<IActionResult> PatientFlowAppointments([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? status, [FromQuery] string? flowState, [FromQuery] Guid? providerId, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterStart, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await drill.AppointmentsAsync(ReportIds.PatientFlow, await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(status, flowState, null, providerId, limit, afterStart, afterId), Actor, ct)));

    [HttpGet("follow-ups")]
    public Task<IActionResult> FollowUps([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, [FromQuery] DateTimeOffset? asOf, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await followUps.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct, asOf)));

    [HttpGet("follow-ups/tasks")]
    public Task<IActionResult> FollowUpTasks([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? kind, [FromQuery] string? outcome, [FromQuery] DateTimeOffset? asOf, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterDueAtUtc, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await followUpDrill.TasksAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(kind, outcome, asOf, limit, afterDueAtUtc, afterId), Actor, ct)));

    [HttpGet("cancellations")]
    public Task<IActionResult> Cancellations([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await cancellations.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("cancellations/appointments")]
    public Task<IActionResult> CancellationAppointments([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? measure, [FromQuery] Guid? providerId, [FromQuery] Guid? appointmentTypeId, [FromQuery] string? leadTime, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterStart, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await cancellationDrill.AppointmentsAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(measure, providerId, appointmentTypeId, leadTime, limit, afterStart, afterId), Actor, ct)));

    /// <summary>The same report as <c>schedule</c>, handed over as a CSV (recorded as an export).</summary>
    [HttpGet("schedule/export")]
    public Task<IActionResult> ScheduleExport([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => await CsvAsync(schedule.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct), ct));

    /// <summary>The same report as <c>patient-flow</c>, handed over as a CSV (recorded as an export).</summary>
    [HttpGet("patient-flow/export")]
    public Task<IActionResult> PatientFlowExport([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => await CsvAsync(patientFlow.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct), ct));

    /// <summary>The same report as <c>follow-ups</c>, handed over as a CSV (recorded as an export).</summary>
    [HttpGet("follow-ups/export")]
    public Task<IActionResult> FollowUpsExport([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, [FromQuery] DateTimeOffset? asOf, CancellationToken ct) =>
        GuardedAsync(async () => await CsvAsync(followUps.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct, asOf), ct));

    /// <summary>The same report as <c>cancellations</c>, handed over as a CSV (recorded as an export).</summary>
    [HttpGet("cancellations/export")]
    public Task<IActionResult> CancellationsExport([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => await CsvAsync(cancellations.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct), ct));

    private async Task<IActionResult> CsvAsync<T>(Task<ReportResult<T>> run, CancellationToken ct)
    {
        var file = await export.CsvAsync(await run, Actor, ct);
        return File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
    }

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
