using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Notifications;

/// <summary>
/// ALV-N015: a person's own bell. Everything here is about the caller's own entries: another person's id is the same not-found as an id that does not exist. Marking read, hiding one read entry and
/// clearing the read history change only the caller's view (the entry is never physically deleted and a message is never touched). Each change is a single conditional statement, so a repeat or a concurrent
/// repeat is quiet and a stale request cannot undo a newer one. Nothing here reads or returns a patient name, a patient id, a file name or message text beyond the subject of a message the caller received.
/// </summary>
public class NotificationService(AlveraDbContext db, IPracticeClock clock)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const string Unread = "unread";
    public const string History = "history";

    private IQueryable<Notification> Own(Guid userId) => db.Notifications.AsNoTracking().Where(n => n.RecipientUserId == userId && n.DeletedAtUtc == null);

    /// <summary>The bell and the banner: three counts and a time, read from an index, never from the list.</summary>
    public async Task<NotificationSummary> SummaryAsync(Guid userId, CancellationToken ct)
    {
        var unread = Own(userId).Where(n => n.ReadAtUtc == null);
        var counts = await unread.GroupBy(n => n.Priority).Select(g => new { Priority = g.Key, Count = g.Count(), Newest = g.Max(n => n.CreatedAtUtc) }).ToListAsync(ct);
        var important = counts.FirstOrDefault(c => c.Priority == NotificationPriorities.Important);
        var urgent = counts.FirstOrDefault(c => c.Priority == NotificationPriorities.Urgent);
        DateTimeOffset? newest = new[] { important?.Newest, urgent?.Newest }.Where(t => t.HasValue).DefaultIfEmpty().Max();
        return new NotificationSummary(counts.Sum(c => c.Count), important?.Count ?? 0, urgent?.Count ?? 0, newest);
    }

    public async Task<NotificationPage> ListAsync(Guid userId, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        if (status is not (Unread or History))
            throw new NotificationException("invalid_status", "Ask for unread or history.", 400, [new NotificationProblem("status", "invalid_status", "Ask for unread or history.")]);
        var (p, size) = Paging(page, pageSize);
        var q = Own(userId);
        q = status == Unread ? q.Where(n => n.ReadAtUtc == null) : q.Where(n => n.ReadAtUtc != null);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAtUtc).ThenBy(n => n.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);

        var messageIds = rows.Where(r => r.MessageId != null).Select(r => r.MessageId!.Value).Distinct().ToList();
        var messages = messageIds.Count == 0 ? [] : await db.Messages.AsNoTracking().Where(m => messageIds.Contains(m.Id)).Select(m => new { m.Id, m.SenderUserId, m.Subject }).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, messages.Select(m => (Guid?)m.SenderUserId), ct);
        var byMessage = messages.ToDictionary(m => m.Id);
        var items = rows.Select(r =>
        {
            var m = r.MessageId is { } id && byMessage.TryGetValue(id, out var found) ? found : null;
            return new NotificationView(r.Id, r.Kind, r.Priority, r.CreatedAtUtc, r.ReadAtUtc, r.MessageId, m is null ? null : ClinicalNames.Name(names, m.SenderUserId), m?.Subject, r.RelatedPatientId != null);
        }).ToList();
        return new NotificationPage(items, total, p, size);
    }

    /// <summary>Marks one of the caller's entries read. Reading it again changes nothing; the read time is set once.</summary>
    public async Task<NotificationResult> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var changed = await db.Notifications.Where(n => n.Id == notificationId && n.RecipientUserId == userId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, n => n.CreatedAtUtc > now ? n.CreatedAtUtc : now), ct);
        if (changed == 0) await RequireOwnAsync(userId, notificationId, ct);
        return new NotificationResult(changed);
    }

    /// <summary>Marks every unread entry of the caller read, once each.</summary>
    public async Task<NotificationResult> MarkAllReadAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var changed = await db.Notifications.Where(n => n.RecipientUserId == userId && n.DeletedAtUtc == null && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, n => n.CreatedAtUtc > now ? n.CreatedAtUtc : now), ct);
        return new NotificationResult(changed);
    }

    /// <summary>Hides one read entry from the caller's own list. An unread entry cannot be hidden. Hiding it again changes nothing. The entry and the message stay stored.</summary>
    public async Task<NotificationResult> HideAsync(Guid userId, Guid notificationId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var changed = await db.Notifications.Where(n => n.Id == notificationId && n.RecipientUserId == userId && n.ReadAtUtc != null && n.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedAtUtc, n => n.ReadAtUtc > now ? n.ReadAtUtc : now), ct);
        if (changed > 0) return new NotificationResult(changed);

        var own = await RequireOwnAsync(userId, notificationId, ct);
        if (own.ReadAtUtc == null)
            throw new NotificationException("notification_unread", "Read this notification before removing it from your list.", 409);
        return new NotificationResult(0);                                                                    // already hidden: quiet
    }

    /// <summary>Hides every read entry of the caller's own list. Only the count is audited. Nothing else is touched.</summary>
    public async Task<NotificationResult> ClearHistoryAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Notifications.Where(n => n.RecipientUserId == userId && n.ReadAtUtc != null && n.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedAtUtc, n => n.ReadAtUtc > now ? n.ReadAtUtc : now), ct);
        if (changed > 0)
        {
            AuditService.Record(db, AuditEventTypes.NotificationsCleared, nameof(Notification), userId, userId, $"{changed} read notification(s) removed from the person's own list.");
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new NotificationResult(changed);
    }

    private async Task<Notification> RequireOwnAsync(Guid userId, Guid notificationId, CancellationToken ct) =>
        await db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId, ct)
        ?? throw new NotificationException("notification_not_found", "That notification was not found.", 404);

    internal static (int Page, int Size) Paging(int? page, int? pageSize) => (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
