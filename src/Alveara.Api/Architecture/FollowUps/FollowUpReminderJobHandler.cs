using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>A channel asked to try again later (or threw): the job fails, its transaction is rolled back and the runner retries it.</summary>
public sealed class ReminderDeferredException(string message) : Exception(message);

/// <summary>Nobody could be told when the job ran (a race with someone being disabled): the job fails and is retried.</summary>
public sealed class ReminderUndeliverableException(string message) : Exception(message);

/// <summary>
/// ALV-009-C01: delivers ONE reminder (one task, one due time, one channel), inside the ALV-N002 runner's transaction. It looks again first: a task that is no longer open, whose due time has moved, or that already has a delivery
/// for this channel needs nothing (the job succeeds quietly). Otherwise it tells the responsible person through the channel (or, when they cannot be told or there is none, every office manager who can), and stages the delivery record,
/// the <c>Reminded</c> event and the audit entry beside it; the runner saves them with the job's receipt and status, so a failure anywhere leaves nothing and the retry starts clean. A channel that stages into the context
/// (<see cref="IStagingReminderChannel"/>, the in-app one) writes into THIS transaction; any other channel is called as it is and must be safe to call again for the same key. Never saves itself.
/// </summary>
public sealed class FollowUpReminderJobHandler(IPracticeClock clock, IEffectivePermissions permissions, IEnumerable<IReminderChannel> channels, ReminderTally? tally = null) : IBackgroundJobHandler
{
    public string JobType => FollowUpReminderService.JobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ReminderJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("A reminder job carries its task, due time and channel."))!;
        var task = await db.FollowUpTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == payload.TaskId, ct);
        if (task is not { Status: FollowUpStatuses.Open } || task.DueAtUtc != payload.DueAtUtc) return;                                   // completed, cancelled or re-dated since: nothing is owed
        if (await db.FollowUpReminderDeliveries.AsNoTracking().AnyAsync(d => d.TaskId == task.Id && d.DueAtUtc == task.DueAtUtc && d.Channel == payload.Channel, ct)) return;
        var channel = channels.FirstOrDefault(c => c.Channel == payload.Channel) ?? throw new InvalidOperationException($"No reminder channel named {payload.Channel} is registered.");

        var primary = task.AssigneeUserId is { } owner && await FollowUpReminderService.CanReceiveAsync(db, permissions, owner, ct);
        var recipients = primary ? [task.AssigneeUserId!.Value] : await FollowUpReminderService.OfficeManagersAsync(db, permissions, ct);
        if (recipients.Count == 0) throw new ReminderUndeliverableException("Nobody can be told about this follow-up yet.");

        var now = clock.UtcNow;
        var key = FollowUpRules.ReminderSourceKey(task.Id, task.DueAtUtc);
        var delivered = true;
        foreach (var recipient in recipients)
        {
            var request = new ReminderRequest(task.Id, task.PatientId, task.DueAtUtc, recipient, key);
            try
            {
                var result = channel is IStagingReminderChannel staging ? await staging.StageAsync(request, db, ct) : await channel.SendAsync(request, ct);
                delivered &= result == ReminderChannelResult.Delivered;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { delivered = false; }                                    // one broken channel never stops the others: this job fails and is retried
        }
        if (!delivered)
        {
            if (tally is not null) tally.Deferred++;
            throw new ReminderDeferredException("The channel could not deliver the reminder yet.");
        }

        db.FollowUpReminderDeliveries.Add(new FollowUpReminderDelivery
        {
            Id = Guid.NewGuid(), TaskId = task.Id, DueAtUtc = task.DueAtUtc, Channel = payload.Channel, RecipientUserId = recipients[0],
            Outcome = primary ? FollowUpDeliveryOutcomes.Delivered : task.AssigneeUserId is null ? FollowUpDeliveryOutcomes.Unassigned : FollowUpDeliveryOutcomes.Fallback, CreatedAtUtc = now,
        });
        db.FollowUpTaskEvents.Add(new FollowUpTaskEvent
        {
            Id = Guid.NewGuid(), TaskId = task.Id, Kind = FollowUpEventKinds.Reminded, ActorUserId = null, OccurredAtUtc = now,
            Detail = primary ? $"{payload.Channel} to {recipients[0]}"
                : task.AssigneeUserId is null ? $"{payload.Channel} to {recipients.Count} office manager(s) because the task has no owner"
                : $"{payload.Channel} to {recipients.Count} office manager(s) because {task.AssigneeUserId} could not be reminded",
        });
        AuditService.Record(db, AuditEventTypes.FollowUpReminderSent, "FollowUpTask", task.Id, null,
            $"Reminder for follow-up {task.Id} (patient {task.PatientId}) sent by {payload.Channel} to {string.Join(", ", recipients)}{(primary ? "" : task.AssigneeUserId is null ? " (unassigned task)" : " as a fallback")}.");
        if (tally is not null) { if (primary) tally.Sent++; else tally.Fallback++; }
    }
}
