using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Controllers;

public record RecordFindingRequest(string? ToothKey, string? Surface, string? Condition, string? State);
public record ChangeFindingStateRequest(string? State, string? RowVersion);
public record WithdrawFindingRequest(string? Reason, string? RowVersion);
public record LinkFindingRequest(string? LinkType, string? Reference);
public record CreateConditionTypeRequest(string? Code, string? Label, string? Scope, string? AppliesTo, string? ToothEffect);
public record ConditionTypeReasonRequest(string? Reason, string? RowVersion);

/// <summary>
/// STORY-006 and ALV-006-C01: the interactive odontogram and its condition catalogue. Reading the chart and a finding's history needs <see cref="Permission.ViewClinicalDocumentation"/>; recording, moving and withdrawing a
/// finding needs <see cref="Permission.ManageClinicalNotes"/> plus a CSRF token, and moving or withdrawing needs the row version the caller read. A refusal returns a stable
/// <c>error</c> code, a message safe to show and, where several things need attention, <c>fieldErrors</c>; a stale edit is the shared 409 concurrency conflict. The tooth is always
/// the FDI key; how it is numbered on screen is a presentation matter and never travels to the server. The condition catalogue is read with <see cref="Permission.ViewClinicalDocumentation"/>
/// and changed (a condition added, retired or reactivated) with <see cref="Permission.ManageClinicalTemplates"/> - the permission that already governs the practice's clinical configuration.
/// A tooth's timeline is read with the view permission; linking a finding to a diagnosis, plan or procedure is a write like any other.
/// </summary>
[ApiController]
[Authorize]
public class OdontogramController(OdontogramService odontogram, ConditionTypeService conditions) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OdontogramException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    [HttpGet("api/patients/{patientId:guid}/odontogram")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Chart(Guid patientId, CancellationToken ct) => Run(async () => Ok(await odontogram.ChartAsync(patientId, ct)));

    [HttpGet("api/odontogram/findings/{findingId:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> History(Guid findingId, CancellationToken ct) => Run(async () => Ok(await odontogram.HistoryAsync(findingId, ct)));

    [HttpPost("api/patients/{patientId:guid}/odontogram/findings")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Record(Guid patientId, [FromBody] RecordFindingRequest request, CancellationToken ct) =>
        Run(async () => Ok(await odontogram.RecordAsync(patientId, request.ToothKey, request.Surface, request.Condition, request.State, Actor, ct)));

    [HttpPost("api/odontogram/findings/{findingId:guid}/state")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> ChangeState(Guid findingId, [FromBody] ChangeFindingStateRequest request, CancellationToken ct) =>
        Run(async () => Ok(await odontogram.ChangeStateAsync(findingId, request.State, request.RowVersion, Actor, ct)));

    [HttpPost("api/odontogram/findings/{findingId:guid}/withdraw")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Withdraw(Guid findingId, [FromBody] WithdrawFindingRequest request, CancellationToken ct) =>
        Run(async () => Ok(await odontogram.WithdrawAsync(findingId, request.Reason, request.RowVersion, Actor, ct)));

    [HttpGet("api/patients/{patientId:guid}/odontogram/teeth/{toothKey}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> ToothHistory(Guid patientId, string toothKey, CancellationToken ct) => Run(async () => Ok(await odontogram.ToothHistoryAsync(patientId, toothKey, ct)));

    [HttpPost("api/odontogram/findings/{findingId:guid}/links")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Link(Guid findingId, [FromBody] LinkFindingRequest request, CancellationToken ct) =>
        Run(async () => Ok(await odontogram.LinkAsync(findingId, request.LinkType, request.Reference, Actor, ct)));

    // ---------- the condition catalogue ----------

    [HttpGet("api/odontogram/condition-types")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> ConditionTypes(CancellationToken ct) => Run(async () => Ok(await conditions.ListAsync(ct)));

    [HttpGet("api/odontogram/condition-types/{id:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> ConditionTypeHistory(Guid id, CancellationToken ct) => Run(async () => Ok(await conditions.HistoryAsync(id, ct)));

    [HttpPost("api/odontogram/condition-types")]
    [RequirePermission(Permission.ManageClinicalTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateConditionType([FromBody] CreateConditionTypeRequest request, CancellationToken ct) =>
        Run(async () => Ok(await conditions.CreateAsync(request.Code, request.Label, request.Scope, request.AppliesTo, request.ToothEffect, Actor, ct)));

    [HttpPost("api/odontogram/condition-types/{id:guid}/retire")]
    [RequirePermission(Permission.ManageClinicalTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> RetireConditionType(Guid id, [FromBody] ConditionTypeReasonRequest request, CancellationToken ct) =>
        Run(async () => Ok(await conditions.RetireAsync(id, request.Reason, request.RowVersion, Actor, ct)));

    [HttpPost("api/odontogram/condition-types/{id:guid}/reactivate")]
    [RequirePermission(Permission.ManageClinicalTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> ReactivateConditionType(Guid id, [FromBody] ConditionTypeReasonRequest request, CancellationToken ct) =>
        Run(async () => Ok(await conditions.ReactivateAsync(id, request.Reason, request.RowVersion, Actor, ct)));
}
