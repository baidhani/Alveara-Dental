using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Controllers;

public record VisitStateBody(string? Target, string? RowVersion);
public record AssignVisitBody(Guid? ProviderId, Guid? OperatoryId, string? RowVersion, string? Reason = null);
public record UndoCheckInBody(string? Reason, string? RowVersion);

/// <summary>
/// ALV-011-C01: the live visit board and the visit's whole state chain. An appointment's id is its visit's id.
///
/// - <c>GET board</c> needs <see cref="Permission.ViewSchedule"/>; the check-in form cue is included only for callers who may see form status.
/// - <c>POST {id}/state</c> moves the visit to any state of the chain (the state machine decides if that is allowed from where it is). WHO may do it depends on the
///   move: confirm, check in and check out are front-office work (<see cref="Permission.UpdateVisitFlow"/>); ready, seat, start treatment and complete are
///   chairside work (<see cref="Permission.UpdateChairsideFlow"/>).
/// - <c>PUT {id}/assignment</c> (ALV-N021 A2, OD-13 to OD-16): reassigns the BOOKED provider and/or room before treatment starts; who may do it and the reason rule are in
///   <see cref="VisitAssignmentService"/>. It no longer writes ALV-011-C01's "Seen by" override.
/// - <c>POST {id}/undo-check-in</c> (OD-11): front desk, reason required.
/// Every change carries the row version the caller read (a stale one is the shared 409 <c>concurrency_conflict</c>) and a CSRF token. STORY-011's own
/// endpoints on <c>api/appointments</c> are untouched and keep their <see cref="Permission.ManageAppointments"/> rule.
/// </summary>
[ApiController]
[Route("api/visits")]
[Authorize]
public class VisitsController(VisitBoardService board, AppointmentFlowService flow, VisitAssignmentService assignment, IEffectivePermissions permissions) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> CanAsync(Permission permission, CancellationToken ct) =>
        Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role) && await permissions.HasAsync(role, permission, ct);

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
            var readiness = await CanAsync(Permission.ViewSignedForms, ct);
            var safety = await CanAsync(Permission.ViewSafetyIndicator, ct);
            return Ok(await board.BoardAsync(day, includeReadiness: readiness, ct, includeSafety: safety));
        }
        catch (LocalTimeConversionException ex)
        {
            return BadRequest(new { error = "invalid_range", message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/state")]
    [RequireCsrfToken]
    public async Task<IActionResult> Transition(Guid id, [FromBody] VisitStateBody body, CancellationToken ct)
    {
        if (!VisitStates.IsKnown(body.Target))
            return BadRequest(new { error = "validation_failed", message = $"The target must be one of: {string.Join(", ", VisitStates.InOrder)}." });
        // ALV-N021 A2: who may make the move is decided by VisitMovePolicy inside the flow service, for this route and the legacy ones alike
        if (!Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role)) return Unauthorized();
        return await Run(async () => Ok(await flow.TransitionAsync(id, body.Target!, body.RowVersion, new VisitActor(Actor, role), ct)));
    }

    [HttpPut("{id:guid}/assignment")]
    [RequireCsrfToken]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignVisitBody body, CancellationToken ct)
    {
        if (body.ProviderId is null || body.OperatoryId is null)
            return BadRequest(new { error = "validation_failed", message = "Both the provider and the operatory are required." });
        if (!Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role)) return Unauthorized();
        return await Run(async () => Ok(await assignment.AssignAsync(id, new AssignVisitRequest(body.ProviderId.Value, body.OperatoryId.Value, body.RowVersion, body.Reason), new VisitActor(Actor, role), ct)));
    }

    [HttpPost("{id:guid}/undo-check-in")]
    [RequireCsrfToken]
    public async Task<IActionResult> UndoCheckIn(Guid id, [FromBody] UndoCheckInBody body, CancellationToken ct)
    {
        if (!Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role)) return Unauthorized();
        return await Run(async () => Ok(await flow.UndoCheckInAsync(id, body.Reason, body.RowVersion, new VisitActor(Actor, role), ct)));
    }
}
