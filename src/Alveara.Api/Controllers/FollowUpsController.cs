using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

/// <summary>
/// STORY-009: follow-up tasks for a patient. Reading needs <see cref="Permission.ViewFollowUps"/>; creating, editing, completing and cancelling need <see cref="Permission.ManageFollowUps"/> and a CSRF token.
/// A create needs an <c>Idempotency-Key</c> (a repeat answers with the task already made); an edit, completion or cancellation carries the <c>rowVersion</c> the person was shown (a stale one is 409). Refusals carry
/// a stable <c>error</c> code (see <see cref="FollowUpException"/>).
/// </summary>
[ApiController]
[Authorize]
public class FollowUpsController(FollowUpService service) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (FollowUpException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }

    [HttpPost("api/patients/{patientId:guid}/follow-ups")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Create(Guid patientId, [FromBody] CreateFollowUpRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Run(async () => await service.CreateAsync(Actor, patientId, request, key, ct));

    [HttpGet("api/patients/{patientId:guid}/follow-ups")]
    [RequirePermission(Permission.ViewFollowUps)]
    public Task<IActionResult> ForPatient(Guid patientId, [FromQuery] string? status, CancellationToken ct) =>
        Run(async () => await service.ForPatientAsync(patientId, status, ct));

    [HttpGet("api/follow-ups/mine")]
    [RequirePermission(Permission.ViewFollowUps)]
    public Task<IActionResult> Mine([FromQuery] string? status, CancellationToken ct) =>
        Run(async () => await service.MineAsync(Actor, status, ct));

    [HttpGet("api/follow-ups/{id:guid}")]
    [RequirePermission(Permission.ViewFollowUps)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Run(async () => await service.GetAsync(id, ct));

    [HttpPut("api/follow-ups/{id:guid}")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdateFollowUpRequest request, CancellationToken ct) =>
        Run(async () => await service.UpdateAsync(Actor, id, request, ct));

    [HttpPost("api/follow-ups/{id:guid}/complete")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Complete(Guid id, [FromBody] CompleteFollowUpRequest request, CancellationToken ct) =>
        Run(async () => await service.CompleteAsync(Actor, id, request, ct));

    [HttpPost("api/follow-ups/{id:guid}/cancel")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Cancel(Guid id, [FromBody] CancelFollowUpRequest request, CancellationToken ct) =>
        Run(async () => await service.CancelAsync(Actor, id, request, ct));
}
