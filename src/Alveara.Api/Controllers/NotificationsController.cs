using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;

namespace Alveara.Api.Controllers;

public record BreakGlassBody(Guid? MessageId, Guid? ThreadId, string? Reason, bool Confirmed);

/// <summary>
/// ALV-N015: the notification bell, the message inbox and the administrator's two views (docs/NOTIFICATIONS.md). Reading, marking read and hiding one's own notifications and messages needs only a signed-in
/// session; sending needs <see cref="Permission.SendMessages"/>; the administrator's notification history needs <see cref="Permission.ViewNotificationAuditHistory"/> and the break-glass view needs
/// <see cref="Permission.ViewMessageAuditContent"/>. Every write needs a CSRF token. A refusal returns a stable <c>error</c> code, a message safe to show and, where there are any, <c>problems</c> naming their
/// field: <c>notification_not_found</c> and <c>message_not_found</c> (404, the same answer for another person's id), <c>notification_unread</c> and <c>idempotency_key_reused</c> (409), <c>message_invalid</c>,
/// <c>idempotency_key_required</c>, <c>invalid_status</c>, <c>invalid_box</c>, <c>invalid_kind</c> and the <c>break_glass_*</c> codes (400), <c>patient_link_not_allowed</c> and <c>patient_access_denied</c> (403),
/// <c>rate_limited</c> (429 with Retry-After) and <c>audit_unavailable</c> (503). No response carries a patient name, a patient identifier in a list, a file name or message text beyond the message itself, which only
/// its sender and recipients (and the audited break-glass action) can read.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public class NotificationsController(NotificationService notifications, MessageService messages, NotificationLinkService links, NotificationAdminService admin) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool MayViewPatients => Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role) && PermissionMatrix.RoleHas(role, Permission.ViewPatientRecords);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (NotificationException ex)
        {
            if (ex.RetryAfterSeconds is { } retry) Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, problems = ex.Problems });
        }
    }

    // ---------- the bell ----------

    [HttpGet("notifications/summary")]
    public Task<IActionResult> Summary(CancellationToken ct) => Run(async () => Ok(await notifications.SummaryAsync(Actor, ct)));

    [HttpGet("notifications")]
    public Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Run(async () => Ok(await notifications.ListAsync(Actor, status, page, pageSize, ct)));

    [HttpPost("notifications/{id:guid}/read")]
    [RequireCsrfToken]
    public Task<IActionResult> MarkRead(Guid id, CancellationToken ct) => Run(async () => Ok(await notifications.MarkReadAsync(Actor, id, ct)));

    [HttpPost("notifications/read-all")]
    [RequireCsrfToken]
    public Task<IActionResult> MarkAllRead(CancellationToken ct) => Run(async () => Ok(await notifications.MarkAllReadAsync(Actor, ct)));

    [HttpDelete("notifications/{id:guid}")]
    [RequireCsrfToken]
    public Task<IActionResult> Hide(Guid id, CancellationToken ct) => Run(async () => Ok(await notifications.HideAsync(Actor, id, ct)));

    [HttpDelete("notifications/history")]
    [RequireCsrfToken]
    public Task<IActionResult> ClearHistory(CancellationToken ct) => Run(async () => Ok(await notifications.ClearHistoryAsync(Actor, ct)));

    /// <summary>Resolves the patient link of a notification. Answers with a path only when the caller may view patient records; otherwise the same refusal whatever the reason.</summary>
    [HttpGet("notifications/{id:guid}/patient-link")]
    public Task<IActionResult> NotificationPatientLink(Guid id, CancellationToken ct) => Run(async () => Ok(await links.ForNotificationAsync(Actor, id, MayViewPatients, ct)));

    // ---------- messages ----------

    [HttpPost("messages")]
    [RequirePermission(Permission.SendMessages)]
    [RequireCsrfToken]
    public Task<IActionResult> Send([FromBody] SendMessageRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) => Run(async () =>
    {
        var result = await messages.SendAsync(Actor, request with { IdempotencyKey = idempotencyKey }, MayViewPatients, ct);
        return result.Replayed ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
    });

    [HttpGet("messages")]
    public Task<IActionResult> ListMessages([FromQuery] string? box, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Run(async () => Ok(await messages.ListAsync(Actor, box, page, pageSize, ct)));

    [HttpGet("messages/recipients")]
    [RequirePermission(Permission.SendMessages)]
    public Task<IActionResult> Recipients([FromQuery] string? search, CancellationToken ct) => Run(async () => Ok(await messages.RecipientsAsync(search, ct)));

    [HttpGet("messages/{id:guid}")]
    public Task<IActionResult> GetMessage(Guid id, CancellationToken ct) => Run(async () => Ok(await messages.GetAsync(Actor, id, ct)));

    [HttpGet("messages/threads/{threadId:guid}")]
    public Task<IActionResult> GetThread(Guid threadId, CancellationToken ct) => Run(async () => Ok(await messages.GetThreadAsync(Actor, threadId, ct)));

    [HttpGet("messages/{id:guid}/patient-link")]
    public Task<IActionResult> MessagePatientLink(Guid id, CancellationToken ct) => Run(async () => Ok(await links.ForMessageAsync(Actor, id, MayViewPatients, ct)));

    // ---------- the administrator's views ----------

    [HttpGet("admin/notifications/history")]
    [RequirePermission(Permission.ViewNotificationAuditHistory)]
    public Task<IActionResult> History([FromQuery] Guid? recipientUserId, [FromQuery] string? kind, [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Run(async () => Ok(await admin.HistoryAsync(recipientUserId, kind, status, page, pageSize, ct)));

    [HttpPost("admin/messages/break-glass")]
    [RequirePermission(Permission.ViewMessageAuditContent)]
    [RequireCsrfToken]
    public Task<IActionResult> BreakGlass([FromBody] BreakGlassBody body, CancellationToken ct) =>
        Run(async () => Ok(await admin.BreakGlassAsync(Actor, new BreakGlassRequest(body.MessageId, body.ThreadId, body.Reason, body.Confirmed), ct)));
}
