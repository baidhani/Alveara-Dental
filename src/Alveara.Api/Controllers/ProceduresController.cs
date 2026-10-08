using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Procedures;

namespace Alveara.Api.Controllers;

public record CreateProcedureRequest(string? CodeSystem, string? Code, string? Description, string? Category, string? Scope, string? Dentition, decimal? Fee,
    string? SourceName, string? SourceVersion, DateOnly? EffectiveFrom, DateOnly? ValidThrough);

public record ReviseProcedureRequest(string? CodeSystem, string? Code, string? Description, string? Category, string? Scope, string? Dentition, decimal? Fee,
    string? SourceName, string? SourceVersion, DateOnly? EffectiveFrom, DateOnly? ValidThrough, string? Reason, string? RowVersion);

public record InactivateProcedureRequest(string? Reason, bool? AcknowledgeUsage, string? RowVersion);

public record ReactivateProcedureRequest(string? Reason, string? RowVersion);

/// <summary>
/// ALV-N005: the procedure and fee catalog. Reading it (the list, a procedure, its history, the usage warning, and the calls treatment planning, completion and billing make) needs
/// <see cref="Permission.ViewBilling"/>, which dentists, front desk, billing, the practice manager and admins hold; changing it (adding a procedure, revising it or its fee, inactivating,
/// reactivating) needs <see cref="Permission.ManageBilling"/> plus a CSRF token, and every change needs the row version the caller read. A refusal returns a stable <c>error</c> code, a message
/// safe to show and, where several fields need attention, <c>fieldErrors</c>; inactivating a procedure that is still referenced is refused with <c>usage_confirmation_required</c> and the
/// <c>usage</c> counts until the caller confirms. A stale edit is the shared 409 concurrency conflict. Fees are US dollars.
/// </summary>
[ApiController]
[Authorize]
public class ProceduresController(ProcedureCatalogService catalog) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ProcedureCatalogException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors, usage = ex.Usage });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    // ---------- reads ----------

    [HttpGet("api/procedures")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? category, [FromQuery] string? codeSystem, [FromQuery] string? status, [FromQuery] DateOnly? asOf, CancellationToken ct) =>
        Run(async () => Ok(await catalog.ListAsync(search, category, codeSystem, status, asOf, ct)));

    /// <summary>The procedures treatment planning may offer on a date, optionally for a tooth and surface (the call planning makes).</summary>
    [HttpGet("api/procedures/active")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Active([FromQuery] string? toothKey, [FromQuery] string? surface, [FromQuery] string? scope, [FromQuery] string? category, [FromQuery] string? search, [FromQuery] DateOnly? asOf, CancellationToken ct) =>
        Run(async () => Ok(await catalog.ActiveForPlanningAsync(toothKey, surface, scope, category, search, asOf, ct)));

    [HttpGet("api/procedures/{id:guid}")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Get(Guid id, [FromQuery] DateOnly? asOf, CancellationToken ct) => Run(async () => Ok(await catalog.GetAsync(id, asOf, ct)));

    [HttpGet("api/procedures/{id:guid}/history")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> History(Guid id, CancellationToken ct) => Run(async () => Ok(await catalog.HistoryAsync(id, ct)));

    [HttpGet("api/procedures/{id:guid}/usage")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Usage(Guid id, CancellationToken ct) => Run(async () => Ok(await catalog.UsageAsync(id, ct)));

    /// <summary>The snapshot of a procedure on a date: the fee and description a consumer copies to remember what it was made with.</summary>
    [HttpGet("api/procedures/{id:guid}/snapshot")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Snapshot(Guid id, [FromQuery] DateOnly? asOf, CancellationToken ct) => Run(async () => Ok(await catalog.SnapshotAsync(id, asOf, ct)));

    /// <summary>The exact snapshot of one remembered version, however old.</summary>
    [HttpGet("api/procedures/versions/{versionId:guid}")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Version(Guid versionId, CancellationToken ct) => Run(async () => Ok(await catalog.VersionSnapshotAsync(versionId, ct)));

    // ---------- changes ----------

    [HttpPost("api/procedures")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Create([FromBody] CreateProcedureRequest request, CancellationToken ct) =>
        Run(async () => Ok(await catalog.CreateAsync(new ProcedureInput(request.CodeSystem, request.Code, request.Description, request.Category, request.Scope, request.Dentition, request.Fee,
            request.SourceName, request.SourceVersion, request.EffectiveFrom, request.ValidThrough), Actor, ct)));

    [HttpPost("api/procedures/{id:guid}/revise")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Revise(Guid id, [FromBody] ReviseProcedureRequest request, CancellationToken ct) =>
        Run(async () => Ok(await catalog.ReviseAsync(id, new ProcedureInput(request.CodeSystem, request.Code, request.Description, request.Category, request.Scope, request.Dentition, request.Fee,
            request.SourceName, request.SourceVersion, request.EffectiveFrom, request.ValidThrough), request.Reason, request.RowVersion, Actor, ct)));

    [HttpPost("api/procedures/{id:guid}/inactivate")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Inactivate(Guid id, [FromBody] InactivateProcedureRequest request, CancellationToken ct) =>
        Run(async () => Ok(await catalog.InactivateAsync(id, request.Reason, request.AcknowledgeUsage ?? false, request.RowVersion, Actor, ct)));

    [HttpPost("api/procedures/{id:guid}/reactivate")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Reactivate(Guid id, [FromBody] ReactivateProcedureRequest request, CancellationToken ct) =>
        Run(async () => Ok(await catalog.ReactivateAsync(id, request.Reason, request.RowVersion, Actor, ct)));
}
