using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Controllers;

public record VisitStateBody(string? Target, string? RowVersion);
public record AssignVisitBody(Guid? ProviderId, Guid? OperatoryId, string? RowVersion);

/// <summary>
/// ALV-011-C01: the live visit board and the visit's whole state chain. An appointment's id is its visit's id.
///
/// - <c>GET board</c> needs <see cref="Permission.ViewSchedule"/>; the check-in form cue is included only for callers who may see form status.
/// - <c>POST {id}/state</c> moves the visit to any state of the chain (the state machine decides if that is allowed from where it is). WHO may do it depends on the
///   move: confirm, check in and check out are front-office work (<see cref="Permission.UpdateVisitFlow"/>); ready, seat, start treatment and complete are
///   chairside work (<see cref="Permission.UpdateChairsideFlow"/>).
/// - <c>PUT {id}/assignment</c> sets the provider and operatory the patient is actually with; either kind of staff may do it.
/// Every change carries the row version the caller read (a stale one is the shared 409 <c>concurrency_conflict</c>) and a CSRF token. STORY-011's own
/// endpoints on <c>api/appointments</c> are untouched and keep their <see cref="Permission.ManageAppointments"/> rule.
/// </summary>
[ApiController]
[Route("api/visits")]
[Authorize]
public class VisitsController(VisitBoardService board, AppointmentFlowService flow, VisitAssignmentService assignment) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool Can(Permission permission) =>
        Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role) && PermissionMatrix.RoleHas(role, permission);

    private IActionResult Refusal(SchedulingException ex) =>
        StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, conflictingAppointmentId = ex.ConflictingAppointmentId, reason = ex.Reason });

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SchedulingException ex)
        {
            return Refusal(ex);
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    /// <summary>The board for a practice-local day (<c>date=yyyy-MM-dd</c>, default today), cancelled and no-show included, plus any visit still open from an earlier day when asking for today.</summary>
    [HttpGet("board")]
    [RequirePermission(Permission.ViewSchedule)]
    public async Task<IActionResult> Board([FromQuery] string? date, CancellationToken ct)
    {
        var day = board.Today();
        if (date is not null && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return BadRequest(new { error = "invalid_range", message = "The date must look like 2030-01-14." });
        try
        {
            return Ok(await board.BoardAsync(day, includeReadiness: Can(Permission.ViewSignedForms), ct, includeSafety: Can(Permission.ViewSafetyIndicator)));
        }
        catch (LocalTimeConversionException ex)
        {
            return BadRequest(new { error = "invalid_range", message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/state")]
    [RequireCsrfToken]
    public Task<IActionResult> Transition(Guid id, [FromBody] VisitStateBody body, CancellationToken ct)
    {
        if (!VisitStates.IsKnown(body.Target))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "validation_failed", message = $"The target must be one of: {string.Join(", ", VisitStates.InOrder)}." }));
        var needed = VisitStateMachine.DutyFor(body.Target!) == TransitionDuty.FrontOffice ? Permission.UpdateVisitFlow : Permission.UpdateChairsideFlow;
        if (!Can(needed))
            return Task.FromResult<IActionResult>(StatusCode(StatusCodes.Status403Forbidden, new { error = "permission_denied", required = needed.ToString() }));
        return Run(async () => Ok(await flow.TransitionAsync(id, body.Target!, body.RowVersion, Actor, ct)));
    }

    [HttpPut("{id:guid}/assignment")]
    [RequireAnyPermission(Permission.UpdateVisitFlow, Permission.UpdateChairsideFlow)]
    [RequireCsrfToken]
    public Task<IActionResult> Assign(Guid id, [FromBody] AssignVisitBody body, CancellationToken ct)
    {
        if (body.ProviderId is null || body.OperatoryId is null)
            return Task.FromResult<IActionResult>(BadRequest(new { error = "validation_failed", message = "Both the provider and the operatory are required." }));
        return Run(async () => Ok(await assignment.AssignAsync(id, new AssignVisitRequest(body.ProviderId.Value, body.OperatoryId.Value, body.RowVersion), Actor, ct)));
    }
}
