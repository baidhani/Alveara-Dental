using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Notifications;

/// <summary>
/// ALV-N015: the administrator's two views. (1) The notification history of everyone, as metadata only: recipient name, kind, priority, status, times and a safe source identifier, including entries a recipient
/// hid, and never a message subject, a body or a patient identifier. (2) Break-glass: the subject and body of ONE chosen message or ONE chosen thread, only with a written reason and an explicit confirmation, and only
/// after the attempt has been recorded and the success has been recorded; if the audit entry cannot be written the content is not returned. No bulk export, no read-all, no silent access, and the audit entries carry
/// ids, the administrator's reason and the outcome, never message content.
/// </summary>
public class NotificationAdminService(AlveraDbContext db)
{
    public const int MaxReasonLength = 500;
    public const int MaxThreadMessages = 100;
    public const string Unread = "Unread";
    public const string Read = "Read";
    public const string Hidden = "Hidden";

    public async Task<NotificationHistoryPage> HistoryAsync(Guid? recipientUserId, string? kind, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        if (kind is not null && !NotificationKinds.All.Contains(kind))
            throw new NotificationException("invalid_kind", "That kind is not known.", 400, [new NotificationProblem("kind", "invalid_kind", "That kind is not known.")]);
        if (status is not null && status is not (Unread or Read or Hidden))
            throw new NotificationException("invalid_status", "Ask for Unread, Read or Hidden.", 400, [new NotificationProblem("status", "invalid_status", "Ask for Unread, Read or Hidden.")]);
        var (p, size) = NotificationService.Paging(page, pageSize);
        var q = db.Notifications.AsNoTracking().AsQueryable();
        if (recipientUserId is { } r) q = q.Where(n => n.RecipientUserId == r);
        if (kind is not null) q = q.Where(n => n.Kind == kind);
        q = status switch
        {
            Unread => q.Where(n => n.ReadAtUtc == null),
            Read => q.Where(n => n.ReadAtUtc != null && n.DeletedAtUtc == null),
            Hidden => q.Where(n => n.DeletedAtUtc != null),
            _ => q,
        };
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAtUtc).ThenBy(n => n.Id).Skip((p - 1) * size).Take(size)
            .Select(n => new { n.Id, n.RecipientUserId, n.Kind, n.Priority, n.CreatedAtUtc, n.ReadAtUtc, n.DeletedAtUtc, n.SourceKey, n.MessageId }).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.Select(x => (Guid?)x.RecipientUserId), ct);
        var items = rows.Select(x => new NotificationHistoryItem(x.Id, ClinicalNames.Name(names, x.RecipientUserId)!, x.Kind, x.Priority,
            x.DeletedAtUtc != null ? Hidden : x.ReadAtUtc != null ? Read : Unread, x.CreatedAtUtc, x.ReadAtUtc, x.DeletedAtUtc, x.SourceKey, x.MessageId)).ToList();
        return new NotificationHistoryPage(items, total, p, size);
    }

    public async Task<BreakGlassResult> BreakGlassAsync(Guid adminId, BreakGlassRequest request, CancellationToken ct)
    {
        var target = request.MessageId ?? request.ThreadId ?? adminId;                                          // what the audit entry is about when the request names nothing
        var reason = request.Reason?.Trim() ?? "";

        // 1. the attempt is recorded first, whatever follows
        await RecordAsync(AuditEventTypes.MessageBreakGlassAttempted, target, adminId, $"Break-glass access to one {(request.ThreadId != null ? "thread" : "message")} was attempted.", reason, ct);

        string? refusal = null;
        if ((request.MessageId is null) == (request.ThreadId is null)) refusal = "target_required";
        else if (reason.Length == 0 || reason.Length > MaxReasonLength || reason.Any(char.IsControl)) refusal = "reason_required";
        else if (!request.Confirmed) refusal = "confirmation_required";
        if (refusal is not null)
        {
            await RecordAsync(AuditEventTypes.MessageBreakGlassRefused, target, adminId, $"Break-glass access was refused: {refusal}.", reason, ct);
            throw refusal switch
            {
                "target_required" => new NotificationException("break_glass_target_required", "Name one message or one thread.", 400, [new NotificationProblem("messageId", "target_required", "Name one message or one thread.")]),
                "reason_required" => new NotificationException("break_glass_reason_required", "Write the reason for opening it (up to 500 characters).", 400, [new NotificationProblem("reason", "reason_required", "Write the reason for opening it.")]),
                _ => new NotificationException("break_glass_confirmation_required", "Confirm that you are opening private messages.", 400, [new NotificationProblem("confirmed", "confirmation_required", "Confirm that you are opening private messages.")]),
            };
        }

        // 2. the content is read, but not released until the success is recorded
        var messages = request.MessageId is { } id
            ? await db.Messages.AsNoTracking().Where(m => m.Id == id).ToListAsync(ct)
            : await db.Messages.AsNoTracking().Where(m => m.ThreadId == request.ThreadId!.Value).OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).Take(MaxThreadMessages).ToListAsync(ct);
        if (messages.Count == 0)
        {
            await RecordAsync(AuditEventTypes.MessageBreakGlassRefused, target, adminId, "Break-glass access was refused: not_found.", reason, ct);
            throw new NotificationException("message_not_found", "That message was not found.", 404);
        }

        await RecordAsync(AuditEventTypes.MessageBreakGlassSucceeded, target, adminId, $"Break-glass access succeeded for {messages.Count} message(s).", reason, ct);

        var ids = messages.Select(m => m.Id).ToList();
        var recipients = await db.MessageRecipients.AsNoTracking().Where(r => ids.Contains(r.MessageId)).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, messages.Select(m => (Guid?)m.SenderUserId).Concat(recipients.Select(r => (Guid?)r.RecipientUserId)), ct);
        var view = messages.Select(m => new BreakGlassMessage(m.Id, ClinicalNames.Name(names, m.SenderUserId)!, m.Subject, m.Body, m.Priority, m.CreatedAtUtc,
            recipients.Where(r => r.MessageId == m.Id).Select(r => ClinicalNames.Name(names, r.RecipientUserId)!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())).ToList();
        return new BreakGlassResult(request.MessageId, request.ThreadId, view);
    }

    /// <summary>Records one audit entry and commits it by itself. If it cannot be written the caller gets a refusal and nothing else happens (in particular, no content is returned).</summary>
    private async Task RecordAsync(string eventType, Guid entityId, Guid adminId, string details, string reason, CancellationToken ct)
    {
        try
        {
            AuditService.Record(db, eventType, nameof(Message), entityId, adminId, details, reason.Length == 0 ? null : reason);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            db.ChangeTracker.Clear();
            throw new NotificationException("audit_unavailable", "The access could not be recorded, so it was not allowed. Try again.", 503);
        }
    }
}
