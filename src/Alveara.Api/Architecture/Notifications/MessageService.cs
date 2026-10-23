using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Notifications;

/// <summary>
/// ALV-N015: operational messages between named colleagues. A message is plain text, immutable once sent, and visible to its sender and its named recipients only: anyone else gets the same not-found as for a
/// message that does not exist. Sending saves the message, its recipient rows, one notification per recipient and the audit entry as one transaction, or nothing. The same key and request is a quiet replay; the same
/// key with anything different is refused. There is a per-sender rate limit. The audit entry names the sender, the recipients, the priority and the time and never the subject or the body. A message is operational
/// communication only; clinical instructions belong in the chart.
/// </summary>
public class MessageService(AlveraDbContext db, IPracticeClock clock)
{
    public const int MaxRecipients = 25;
    public const string Inbox = "inbox";
    public const string Sent = "sent";
    public const int RecipientSearchLimit = 25;

    // ---------- send ----------

    public async Task<SendMessageResult> SendAsync(Guid senderId, SendMessageRequest request, bool senderMayLinkPatients, CancellationToken ct)
    {
        var key = (request.IdempotencyKey ?? "").Trim();
        if (key.Length == 0 || key.Length > 64 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
            throw new NotificationException("idempotency_key_required", "Send a save key (the Idempotency-Key header) of up to 64 letters, digits, dashes or underscores.", 400);

        var problems = new List<NotificationProblem>();
        var priority = request.Priority ?? NotificationPriorities.Normal;
        if (!NotificationPriorities.All.Contains(priority)) problems.Add(new("priority", "priority_invalid", "Choose Normal, Important or Urgent."));

        var subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim();
        if (subject is not null)
        {
            if (subject.Length > MessageRules.SubjectMaxLength) problems.Add(new("subject", "subject_too_long", $"The subject can be up to {MessageRules.SubjectMaxLength} characters."));
            else if (subject.Any(char.IsControl)) problems.Add(new("subject", "subject_invalid", "The subject cannot contain control characters."));
        }

        var body = request.Body ?? "";
        if (string.IsNullOrWhiteSpace(body)) problems.Add(new("body", "body_required", "Write the message."));
        else if (body.Length > MessageRules.BodyMaxLength) problems.Add(new("body", "body_too_long", $"The message can be up to {MessageRules.BodyMaxLength} characters."));
        else if (body.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r'))) problems.Add(new("body", "body_invalid", "The message is plain text and cannot contain control characters."));

        var recipients = (request.RecipientUserIds ?? []).Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToList();
        if (recipients.Count == 0) problems.Add(new("recipientUserIds", "recipient_required", "Choose at least one person to send it to."));
        else if (recipients.Count > MaxRecipients) problems.Add(new("recipientUserIds", "too_many_recipients", $"A message can go to up to {MaxRecipients} people."));
        if (problems.Count > 0) throw new NotificationException("message_invalid", "The message cannot be sent as written.", 400, problems);

        var hash = RequestHash(priority, subject, body, recipients, request.RelatedPatientId, request.InReplyToMessageId);
        if (await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.SenderUserId == senderId && m.IdempotencyKey == key, ct) is { } earlier)
            return await ReplayAsync(earlier, hash, senderId, ct);

        if (request.RelatedPatientId is { } patientId)
        {
            if (!senderMayLinkPatients) throw new NotificationException("patient_link_not_allowed", "You cannot link a patient to a message.", 403);
            if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new NotificationException("patient_not_found", "That patient was not found.", 404);
        }

        Guid threadId;
        if (request.InReplyToMessageId is { } parentId)
        {
            var parent = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == parentId, ct);
            if (parent is null || !await IsParticipantAsync(senderId, parent, ct)) throw new NotificationException("message_not_found", "That message was not found.", 404);
            threadId = parent.ThreadId;
        }
        else threadId = Guid.Empty;

        var now = clock.UtcNow;
        if (await db.Messages.AsNoTracking().CountAsync(m => m.SenderUserId == senderId && m.CreatedAtUtc > now.AddMinutes(-1), ct) >= MessageRules.MaxMessagesPerMinute)
            throw new NotificationException("rate_limited", "You are sending messages too quickly. Wait a moment and try again.", 429, retryAfterSeconds: 30);

        var accounts = await db.UserAccounts.AsNoTracking().Where(u => recipients.Contains(u.Id)).Select(u => new { u.Id, u.IsDisabled }).ToListAsync(ct);
        if (accounts.Count != recipients.Count || accounts.Any(a => a.IsDisabled))
            throw new NotificationException("message_invalid", "The message cannot be sent as written.", 400, [new("recipientUserIds", "recipient_unavailable", "One or more people cannot receive messages.")]);

        var id = Guid.NewGuid();
        var message = new Message
        {
            Id = id, SenderUserId = senderId, Subject = subject, Body = body, Priority = priority, RelatedPatientId = request.RelatedPatientId, InReplyToMessageId = request.InReplyToMessageId,
            ThreadId = threadId == Guid.Empty ? id : threadId, CreatedAtUtc = now, IdempotencyKey = key, RequestHash = hash,
        };
        db.Messages.Add(message);
        foreach (var r in recipients)
        {
            db.MessageRecipients.Add(new MessageRecipient { MessageId = id, RecipientUserId = r });
            db.Notifications.Add(new Notification { Id = Guid.NewGuid(), RecipientUserId = r, Kind = NotificationKinds.MessageReceived, Priority = priority, MessageId = id, RelatedPatientId = request.RelatedPatientId, CreatedAtUtc = now });
        }
        AuditService.Record(db, AuditEventTypes.MessageSent, nameof(Message), id, senderId, $"A {priority} message was sent to {recipients.Count} recipient(s): {string.Join(", ", recipients)}.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql && sql.Message.Contains("IX_Messages_SenderUserId_IdempotencyKey", StringComparison.Ordinal))
        {
            db.ChangeTracker.Clear();
            var winner = await db.Messages.AsNoTracking().FirstAsync(m => m.SenderUserId == senderId && m.IdempotencyKey == key, ct);
            return await ReplayAsync(winner, hash, senderId, ct);                                              // two sends with one key at once: one message, the other is its replay
        }
        return new SendMessageResult(await ViewAsync(message, senderId, ct), false);
    }

    private async Task<SendMessageResult> ReplayAsync(Message existing, string hash, Guid callerId, CancellationToken ct)
    {
        if (existing.RequestHash != hash) throw new NotificationException("idempotency_key_reused", "That save key was already used for a different message. Start the message again.", 409);
        return new SendMessageResult(await ViewAsync(existing, callerId, ct), true);
    }

    private static string RequestHash(string priority, string? subject, string body, IReadOnlyList<Guid> recipients, Guid? patient, Guid? inReplyTo)
    {
        var canonical = string.Join('\u001f', priority, subject ?? "", body, string.Join(',', recipients), patient?.ToString() ?? "", inReplyTo?.ToString() ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    // ---------- read ----------

    /// <summary>One message, for its sender or one of its recipients only. A recipient opening it marks their own notification read (once; the first read is audited, with ids only).</summary>
    public async Task<MessageView> GetAsync(Guid userId, Guid messageId, CancellationToken ct)
    {
        var message = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null || !await IsParticipantAsync(userId, message, ct)) throw NotFound();
        await MarkReadAsync(userId, message, ct);
        return await ViewAsync(message, userId, ct);
    }

    /// <summary>A thread as far as the caller took part in it (a message they sent or received), oldest first. Opening it reads the caller's unread entries in it.</summary>
    public async Task<IReadOnlyList<MessageView>> GetThreadAsync(Guid userId, Guid threadId, CancellationToken ct)
    {
        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ThreadId == threadId && (m.SenderUserId == userId || db.MessageRecipients.Any(r => r.MessageId == m.Id && r.RecipientUserId == userId)))
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).ToListAsync(ct);
        if (messages.Count == 0) throw NotFound();
        var views = new List<MessageView>();
        foreach (var m in messages)
        {
            await MarkReadAsync(userId, m, ct);
            views.Add(await ViewAsync(m, userId, ct));
        }
        return views;
    }

    public async Task<MessagePage> ListAsync(Guid userId, string? box, int? page, int? pageSize, CancellationToken ct)
    {
        if (box is not (Inbox or Sent))
            throw new NotificationException("invalid_box", "Ask for the inbox or the sent list.", 400, [new NotificationProblem("box", "invalid_box", "Ask for the inbox or the sent list.")]);
        var (p, size) = NotificationService.Paging(page, pageSize);
        var q = box == Inbox
            ? db.Messages.AsNoTracking().Where(m => db.MessageRecipients.Any(r => r.MessageId == m.Id && r.RecipientUserId == userId))
            : db.Messages.AsNoTracking().Where(m => m.SenderUserId == userId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(m => m.CreatedAtUtc).ThenBy(m => m.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.MessageRecipients.AsNoTracking().Where(r => ids.Contains(r.MessageId)).GroupBy(r => r.MessageId).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var readCounts = await db.Notifications.AsNoTracking().Where(n => n.MessageId != null && ids.Contains(n.MessageId.Value) && n.ReadAtUtc != null).GroupBy(n => n.MessageId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var mine = await db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId && n.MessageId != null && ids.Contains(n.MessageId.Value)).Select(n => new { Id = n.MessageId!.Value, n.ReadAtUtc }).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.Select(r => (Guid?)r.SenderUserId), ct);
        var items = rows.Select(r => new MessageListItem(r.Id, r.ThreadId, ClinicalNames.Name(names, r.SenderUserId)!, r.SenderUserId == userId, r.Subject, r.Priority, r.CreatedAtUtc,
            counts.FirstOrDefault(c => c.Id == r.Id)?.Count ?? 0, readCounts.FirstOrDefault(c => c.Id == r.Id)?.Count ?? 0, mine.Any(n => n.Id == r.Id && n.ReadAtUtc == null))).ToList();
        return new MessagePage(items, total, p, size);
    }

    /// <summary>The people a message can be addressed to: enabled accounts, by name and role only, so a person can be chosen. Nothing else about them is returned.</summary>
    public async Task<IReadOnlyList<MessageRecipientChoice>> RecipientsAsync(string? search, CancellationToken ct)
    {
        var accounts = await db.UserAccounts.AsNoTracking().Where(u => !u.IsDisabled && u.Role != Role.Unassigned).Select(u => new { u.Id, u.Username, u.Role }).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, accounts.Select(a => (Guid?)a.Id), ct);
        var term = (search ?? "").Trim();
        return accounts.Select(a => new MessageRecipientChoice(a.Id, names.TryGetValue(a.Id, out var n) ? n : a.Username, a.Role.ToString()))
            .Where(c => term.Length == 0 || c.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(RecipientSearchLimit).ToList();
    }

    // ---------- internals ----------

    private static NotificationException NotFound() => new("message_not_found", "That message was not found.", 404);

    private async Task<bool> IsParticipantAsync(Guid userId, Message message, CancellationToken ct) =>
        message.SenderUserId == userId || await db.MessageRecipients.AsNoTracking().AnyAsync(r => r.MessageId == message.Id && r.RecipientUserId == userId, ct);

    /// <summary>Sets the caller's own read time for this message once and audits that first read (ids only). Reading again changes and records nothing.</summary>
    private async Task MarkReadAsync(Guid userId, Message message, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Notifications.Where(n => n.MessageId == message.Id && n.RecipientUserId == userId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, n => n.CreatedAtUtc > now ? n.CreatedAtUtc : now), ct);
        if (changed > 0)
        {
            AuditService.Record(db, AuditEventTypes.MessageRead, nameof(Message), message.Id, userId, "A message was opened by a recipient.");
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task<MessageView> ViewAsync(Message m, Guid callerId, CancellationToken ct)
    {
        var recipientIds = await db.MessageRecipients.AsNoTracking().Where(r => r.MessageId == m.Id).Select(r => r.RecipientUserId).ToListAsync(ct);
        var reads = await db.Notifications.AsNoTracking().Where(n => n.MessageId == m.Id).Select(n => new { n.RecipientUserId, n.ReadAtUtc }).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, recipientIds.Select(i => (Guid?)i).Append(m.SenderUserId), ct);
        var isSender = m.SenderUserId == callerId;
        var recipients = recipientIds.Select(id => new MessageRecipientView(id, ClinicalNames.Name(names, id)!, isSender ? reads.FirstOrDefault(r => r.RecipientUserId == id)?.ReadAtUtc : null))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new MessageView(m.Id, m.ThreadId, m.InReplyToMessageId, ClinicalNames.Name(names, m.SenderUserId)!, isSender, m.Subject, m.Body, m.Priority, m.CreatedAtUtc, m.RelatedPatientId != null, recipients,
            reads.FirstOrDefault(r => r.RecipientUserId == callerId)?.ReadAtUtc);
    }
}
