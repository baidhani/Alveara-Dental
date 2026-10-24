using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.FollowUps;

/// <param name="Sent">Reminders delivered to the responsible person.</param>
/// <param name="Fallback">Reminders delivered to the office managers because the responsible person could not receive them.</param>
/// <param name="Undeliverable">Due tasks nobody could be told about this pass (no responsible person able to receive and no office manager); they are tried again next pass.</param>
/// <param name="Deferred">Due tasks a channel asked to try again later.</param>
public sealed record ReminderPassResult(int Sent, int Fallback, int Undeliverable, int Deferred)
{
    public int Total => Sent + Fallback;
}

/// <summary>
/// STORY-009, acceptance criterion 1: when a follow-up task is due, the responsible party is reminded. One pass finds every open task that is due and has no recorded delivery for a channel and its current due time,
/// oldest due first, and tells the responsible person through that channel. Because the rule is "due and not yet reminded" rather than "due in this window", a missed run, a restart, a clock jump or an outage only
/// delays a reminder; the next pass sends every reminder that is owed (the missed-reminder path). A task whose due time is changed is a new reminder (the delivery is unique per task, due time and channel);
/// a completed or cancelled task is never reminded. If the responsible person is disabled or can no longer see follow-ups, every office manager who can is told instead and the delivery is recorded as a fallback
/// (an unowned reminder must not vanish). The notification, the delivery record, the <c>Reminded</c> event and the audit entry of a batch are saved together, so a failure leaves none of them and the next pass
/// repeats the batch; the unique keys make a repeat, or a second server running the same pass, harmless.
/// </summary>
public class FollowUpReminderService(AlveraDbContext db, IPracticeClock clock, IEffectivePermissions permissions, IEnumerable<IReminderChannel> channels)
{
    public const int BatchSize = 50;

    /// <summary>One batch per channel. Returns what it did; 0 reminders means the service is caught up.</summary>
    public async Task<ReminderPassResult> ProcessOnceAsync(CancellationToken ct)
    {
        int sent = 0, fallback = 0, undeliverable = 0, deferred = 0;
        List<Guid>? managers = null;
        foreach (var channel in channels)
        {
            var now = clock.UtcNow;
            var name = channel.Channel;
            var due = await db.FollowUpTasks.AsNoTracking()
                .Where(t => t.Status == FollowUpStatuses.Open && t.DueAtUtc <= now && !db.FollowUpReminderDeliveries.Any(d => d.TaskId == t.Id && d.DueAtUtc == t.DueAtUtc && d.Channel == name))
                .OrderBy(t => t.DueAtUtc).ThenBy(t => t.Id).Take(BatchSize).ToListAsync(ct);
            foreach (var task in due)
            {
                var primary = task.AssigneeUserId is { } owner && await CanReceiveAsync(owner, ct);
                List<Guid> recipients = primary ? new() { task.AssigneeUserId!.Value } : (managers ??= await OfficeManagersAsync(ct));   // an unassigned task, like one whose owner cannot be told, is the office managers'
                if (recipients.Count == 0) { undeliverable++; continue; }

                var key = FollowUpRules.ReminderSourceKey(task.Id, task.DueAtUtc);
                var delivered = true;
                foreach (var recipient in recipients)
                {
                    try { delivered &= await channel.SendAsync(new ReminderRequest(task.Id, task.PatientId, task.DueAtUtc, recipient, key), ct) == ReminderChannelResult.Delivered; }
                    catch (Exception ex) when (ex is not OperationCanceledException) { delivered = false; }   // one broken channel or recipient never stops the others; the task is tried again next pass
                }
                if (!delivered) { deferred++; continue; }

                db.FollowUpReminderDeliveries.Add(new FollowUpReminderDelivery
                {
                    Id = Guid.NewGuid(), TaskId = task.Id, DueAtUtc = task.DueAtUtc, Channel = name, RecipientUserId = recipients[0],
                    Outcome = primary ? FollowUpDeliveryOutcomes.Delivered : task.AssigneeUserId is null ? FollowUpDeliveryOutcomes.Unassigned : FollowUpDeliveryOutcomes.Fallback, CreatedAtUtc = now,
                });
                db.FollowUpTaskEvents.Add(new FollowUpTaskEvent
                {
                    Id = Guid.NewGuid(), TaskId = task.Id, Kind = FollowUpEventKinds.Reminded, ActorUserId = null, OccurredAtUtc = now,
                    Detail = primary ? $"{name} to {recipients[0]}" : task.AssigneeUserId is null ? $"{name} to {recipients.Count} office manager(s) because the task has no owner" : $"{name} to {recipients.Count} office manager(s) because {task.AssigneeUserId} could not be reminded",
                });
                AuditService.Record(db, AuditEventTypes.FollowUpReminderSent, "FollowUpTask", task.Id, null,
                    $"Reminder for follow-up {task.Id} (patient {task.PatientId}) sent by {name} to {string.Join(", ", recipients)}{(primary ? "" : task.AssigneeUserId is null ? " (unassigned task)" : " as a fallback")}.");
                if (primary) sent++; else fallback++;
            }
            if (!await TrySaveAsync(ct)) { sent = fallback = 0; }   // a concurrent pass got there first: nothing of this batch was stored, the next pass finds what is still owed
        }
        return new ReminderPassResult(sent, fallback, undeliverable, deferred);
    }

    /// <summary>Saves the staged batch. A unique-key violation means another pass delivered the same reminder meanwhile: the whole batch is discarded (it was one save) and false is returned.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<bool> CanReceiveAsync(Guid userId, CancellationToken ct)
    {
        var account = await db.UserAccounts.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Role, u.IsDisabled }).FirstOrDefaultAsync(ct);
        return account is { IsDisabled: false } && await permissions.HasAsync(account.Role, Permission.ViewFollowUps, ct);
    }

    /// <summary>The enabled office managers, when their role can see follow-ups, in a fixed order.</summary>
    private async Task<List<Guid>> OfficeManagersAsync(CancellationToken ct) =>
        await permissions.HasAsync(Role.OfficeManager, Permission.ViewFollowUps, ct)
            ? await db.UserAccounts.AsNoTracking().Where(u => u.Role == Role.OfficeManager && !u.IsDisabled).OrderBy(u => u.Id).Select(u => u.Id).ToListAsync(ct)
            : [];
}
