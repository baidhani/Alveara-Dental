using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Safety;

namespace Alveara.Api.Controllers;

public record CreateAlertRequest(string? Category, string? Title, string? Detail, string? Severity, string? SourceNote, Guid? SourceItemId);
public record UpdateAlertRequest(string? Title, string? Detail, string? Severity, string? SourceNote, string? Reason, string? RowVersion);
public record AlertReasonRequest(string? Reason, string? RowVersion);
public record AcknowledgeRequest(int? Revision);
public record RequestClearanceBody(string? Kind, string? Reason, string? RequestedFrom);
public record ReceiveClearanceBody(string? Note, string? DocumentReference, string? RowVersion);
public record AttachDocumentBody(string? DocumentReference, string? RowVersion);
public record ClearanceReasonBody(string? Reason, string? RowVersion);

/// <summary>
/// ALV-N011: the patient-safety context, alerts and clearances. Reading the context (and acknowledging an alert, which only records that someone SAW it) needs
/// <see cref="Permission.ViewClinicalDocumentation"/>; creating, changing, resolving or reopening an alert and every clearance step needs <see cref="Permission.ManageClinicalNotes"/>
/// plus a CSRF token and the version the caller read. Resolving or cancelling needs a reason. A refusal returns a stable <c>error</c> code, a message safe to show and, where several
/// things need attention, <c>fieldErrors</c>; a stale edit is the shared 409 concurrency conflict. The shared live board gets only a minimal indicator, from <c>api/visits/board</c>.
/// </summary>
[ApiController]
[Authorize]
public class SafetyController(SafetyContextService context, SafetyAlertService alerts, ClearanceService clearances) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SafetyException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    // ---------- reading ----------

    [HttpGet("api/patients/{patientId:guid}/safety")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid patientId, CancellationToken ct) => Run(async () => Ok(await context.GetAsync(patientId, Actor, ct)));

    /// <summary>Counts and the highest severity only (no names) - what the patient header shows.</summary>
    [HttpGet("api/patients/{patientId:guid}/safety/summary")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Summary(Guid patientId, CancellationToken ct) => Run(async () => Ok(await context.SummaryAsync(patientId, Actor, ct)));

    [HttpGet("api/safety/alerts/{alertId:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> AlertHistory(Guid alertId, CancellationToken ct) => Run(async () => Ok(await alerts.HistoryAsync(alertId, ct)));

    [HttpGet("api/safety/clearances/{clearanceId:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> ClearanceHistory(Guid clearanceId, CancellationToken ct) => Run(async () => Ok(await clearances.HistoryAsync(clearanceId, ct)));

    // ---------- alerts ----------

    [HttpPost("api/patients/{patientId:guid}/safety/alerts")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateAlert(Guid patientId, [FromBody] CreateAlertRequest request, CancellationToken ct) =>
        Run(async () => Ok(await alerts.CreateAsync(patientId, request.Category, request.Title, request.Detail, request.Severity, request.SourceNote, request.SourceItemId, Actor, ct)));

    [HttpPut("api/safety/alerts/{alertId:guid}")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateAlert(Guid alertId, [FromBody] UpdateAlertRequest request, CancellationToken ct) =>
        Run(async () => Ok(await alerts.UpdateAsync(alertId, request.Title, request.Detail, request.Severity, request.SourceNote, request.Reason, request.RowVersion, Actor, ct)));

    [HttpPost("api/safety/alerts/{alertId:guid}/resolve")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> ResolveAlert(Guid alertId, [FromBody] AlertReasonRequest request, CancellationToken ct) =>
        Run(async () => Ok(await alerts.ResolveAsync(alertId, request.Reason, request.RowVersion, Actor, ct)));

    [HttpPost("api/safety/alerts/{alertId:guid}/reopen")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> ReopenAlert(Guid alertId, [FromBody] AlertReasonRequest request, CancellationToken ct) =>
        Run(async () => Ok(await alerts.ReopenAsync(alertId, request.Reason, request.RowVersion, Actor, ct)));

    /// <summary>Records that the caller saw this revision of the alert. It never resolves it.</summary>
    [HttpPost("api/safety/alerts/{alertId:guid}/acknowledge")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    [RequireCsrfToken]
    public Task<IActionResult> Acknowledge(Guid alertId, [FromBody] AcknowledgeRequest request, CancellationToken ct) =>
        Run(async () => Ok(await alerts.AcknowledgeAsync(alertId, request.Revision, Actor, ct)));

    // ---------- clearances ----------

    [HttpPost("api/patients/{patientId:guid}/safety/clearances")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> RequestClearance(Guid patientId, [FromBody] RequestClearanceBody request, CancellationToken ct) =>
        Run(async () => Ok(await clearances.RequestAsync(patientId, request.Kind, request.Reason, request.RequestedFrom, Actor, ct)));

    [HttpPost("api/safety/clearances/{clearanceId:guid}/receive")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> ReceiveClearance(Guid clearanceId, [FromBody] ReceiveClearanceBody request, CancellationToken ct) =>
        Run(async () => Ok(await clearances.ReceiveAsync(clearanceId, request.Note, request.DocumentReference, request.RowVersion, Actor, ct)));

    [HttpPost("api/safety/clearances/{clearanceId:guid}/document")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> AttachDocument(Guid clearanceId, [FromBody] AttachDocumentBody request, CancellationToken ct) =>
        Run(async () => Ok(await clearances.AttachDocumentAsync(clearanceId, request.DocumentReference, request.RowVersion, Actor, ct)));

    [HttpPost("api/safety/clearances/{clearanceId:guid}/resolve")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> ResolveClearance(Guid clearanceId, [FromBody] ClearanceReasonBody request, CancellationToken ct) =>
        Run(async () => Ok(await clearances.ResolveAsync(clearanceId, request.Reason, request.RowVersion, Actor, ct)));

    [HttpPost("api/safety/clearances/{clearanceId:guid}/cancel")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> CancelClearance(Guid clearanceId, [FromBody] ClearanceReasonBody request, CancellationToken ct) =>
        Run(async () => Ok(await clearances.CancelAsync(clearanceId, request.Reason, request.RowVersion, Actor, ct)));
}
