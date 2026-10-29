using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Reporting;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N006 slice 2: the financial reports (production and charges, collections and payments, balances). Everything here needs <see cref="Permission.ViewFinancialReports"/>, which the operational reports' permission does not
/// include. Same conventions as <see cref="ReportsController"/>: reads only, <c>no-store</c>, <c>from</c> and <c>to</c> required, stable error codes, every run and drill-down audited (counts and ids only). A filter a
/// report cannot honestly apply is refused with <c>filter_not_supported</c> rather than ignored.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
[RequirePermission(Permission.ViewFinancialReports)]
public class FinancialReportsController(ReportFilterResolver filters, ProductionReportService production, CollectionsReportService collections, FinancialDrillService drill, BalancesReportService balances, BalancesDrillService balanceDrill) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("production")]
    public Task<IActionResult> Production([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await production.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("collections")]
    public Task<IActionResult> Collections([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await collections.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("balances")]
    public Task<IActionResult> Balances([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await balances.RunAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), Actor, ct)));

    [HttpGet("balances/accounts")]
    public Task<IActionResult> BalanceAccounts([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? side, [FromQuery] int? limit, [FromQuery] decimal? afterBalance, [FromQuery] Guid? afterAccountId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await balanceDrill.AccountsAsync(await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(side, limit, afterBalance, afterAccountId), Actor, ct)));

    [HttpGet("production/entries")]
    public Task<IActionResult> ProductionEntries([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? line, [FromQuery] Guid? providerId, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterRecordedAtUtc, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await drill.EntriesAsync(ReportIds.Production, await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(line, null, providerId, limit, afterRecordedAtUtc, afterId), Actor, ct)));

    [HttpGet("collections/entries")]
    public Task<IActionResult> CollectionsEntries([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid[]? providerIds, [FromQuery] Guid? locationId,
        [FromQuery] string? line, [FromQuery] string? method, [FromQuery] int? limit, [FromQuery] DateTimeOffset? afterRecordedAtUtc, [FromQuery] Guid? afterId, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await drill.EntriesAsync(ReportIds.Collections, await filters.ResolveAsync(new(from, to, providerIds, locationId), ct), new(line, method, null, limit, afterRecordedAtUtc, afterId), Actor, ct)));

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
