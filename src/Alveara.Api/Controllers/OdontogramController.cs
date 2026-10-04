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

/// <summary>
/// STORY-006: the interactive odontogram. Reading the chart and a finding's history needs <see cref="Permission.ViewClinicalDocumentation"/>; recording, moving and withdrawing a
/// finding needs <see cref="Permission.ManageClinicalNotes"/> plus a CSRF token, and moving or withdrawing needs the row version the caller read. A refusal returns a stable
/// <c>error</c> code, a message safe to show and, where several things need attention, <c>fieldErrors</c>; a stale edit is the shared 409 concurrency conflict. The tooth is always
/// the FDI key; how it is numbered on screen is a presentation matter and never travels to the server.
/// </summary>
[ApiController]
[Authorize]
public class OdontogramController(OdontogramService odontogram) : ControllerBase
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
}
