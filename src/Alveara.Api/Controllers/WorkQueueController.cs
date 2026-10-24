using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Recall;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-009-C01: the work queue, its quick actions and the recall settings. Reading the queue, the recall settings and a patient's recall arrangement needs <see cref="Permission.ViewFollowUps"/>; acting on a task and changing a
/// patient's own recall interval need <see cref="Permission.ManageFollowUps"/>; the practice's recall settings need <see cref="Permission.ManageRecallSettings"/>. Writes need a CSRF token and carry the version the screen showed (stale:
/// 409, nothing stored). Refusals carry a stable <c>error</c> code (see <see cref="FollowUpException"/> and <see cref="RecallException"/>).
/// </summary>
[ApiController]
[Authorize]
public class WorkQueueController(FollowUpService followUps, RecallAdminService recall) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (FollowUpException ex) { return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message }); }
        catch (RecallException ex) { return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message }); }
    }

    // ---------- the queue and its actions ----------

    [HttpGet("api/work-queue")]
    [RequirePermission(Permission.ViewFollowUps)]
    public Task<IActionResult> Queue([FromQuery] string? filter, [FromQuery] string? priority, [FromQuery] string? kind, [FromQuery] string? owner, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Run(async () => await followUps.QueueAsync(Actor, new QueueQuery(filter, priority, kind, owner, status, page, pageSize), ct));

    /// <summary>Everything that happened to a patient's follow-ups, newest first.</summary>
    [HttpGet("api/patients/{patientId:guid}/follow-ups/timeline")]
    [RequirePermission(Permission.ViewFollowUps)]
    public Task<IActionResult> Timeline(Guid patientId, CancellationToken ct) => Run(async () => await followUps.TimelineAsync(patientId, ct));

    [HttpPost("api/follow-ups/{id:guid}/assign")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Assign(Guid id, [FromBody] AssignFollowUpRequest request, CancellationToken ct) => Run(async () => await followUps.AssignAsync(Actor, id, request, ct));

    [HttpPost("api/follow-ups/{id:guid}/claim")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Claim(Guid id, [FromBody] ClaimFollowUpRequest request, CancellationToken ct) => Run(async () => await followUps.ClaimAsync(Actor, id, request, ct));

    [HttpPost("api/follow-ups/{id:guid}/priority")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Priority(Guid id, [FromBody] SetPriorityRequest request, CancellationToken ct) => Run(async () => await followUps.SetPriorityAsync(Actor, id, request, ct));

    [HttpPost("api/follow-ups/{id:guid}/appointment")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> Appointment(Guid id, [FromBody] LinkAppointmentRequest request, CancellationToken ct) => Run(async () => await followUps.LinkAppointmentAsync(Actor, id, request, ct));

    // ---------- recall settings and a patient's own recall ----------

    [HttpGet("api/recall/settings")]
    [RequirePermission(Permission.ViewFollowUps)]
    public Task<IActionResult> Settings(CancellationToken ct) => Run(async () => await recall.GetSettingsAsync(ct));

    [HttpPut("api/recall/settings")]
    [RequirePermission(Permission.ManageRecallSettings)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateSettings([FromBody] UpdateRecallSettingsRequest request, CancellationToken ct) => Run(async () => await recall.UpdateSettingsAsync(Actor, request, ct));

    [HttpGet("api/patients/{patientId:guid}/recall")]
    [RequirePermission(Permission.ViewFollowUps)]
    public Task<IActionResult> PatientRecall(Guid patientId, CancellationToken ct) => Run(async () => await recall.GetPatientAsync(patientId, ct));

    [HttpPut("api/patients/{patientId:guid}/recall")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> SetPatientRecall(Guid patientId, [FromBody] SetPatientRecallRequest request, CancellationToken ct) => Run(async () => await recall.SetPatientAsync(Actor, patientId, request, ct));

    [HttpPost("api/patients/{patientId:guid}/recall/reset")]
    [RequirePermission(Permission.ManageFollowUps)]
    [RequireCsrfToken]
    public Task<IActionResult> ResetPatientRecall(Guid patientId, [FromBody] ResetPatientRecallRequest request, CancellationToken ct) => Run(async () => await recall.ResetPatientAsync(Actor, patientId, request, ct));
}
