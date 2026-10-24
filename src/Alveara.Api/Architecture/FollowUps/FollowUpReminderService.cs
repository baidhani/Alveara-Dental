using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.FollowUps;

/// <param name="Sent">Reminders delivered to the responsible person.</param>
/// <param name="Fallback">Reminders delivered to the office managers because the responsible person could not receive them (or the task has no owner).</param>
/// <param name="Undeliverable">Due tasks nobody could be told about this pass (no responsible person able to receive and no office manager); they are tried again next pass.</param>
/// <param name="Deferred">Due tasks a channel asked to try again later.</param>
public sealed record ReminderPassResult(int Sent, int Fallback, int Undeliverable, int Deferred)
{
    public int Total => Sent + Fallback;
}

/// <param name="Enqueued">Reminder jobs newly made (a job that already exists, in any state, is not counted).</param>
/// <param name="Undeliverable">Owed reminders nobody could be told about; no job is made until someone can be.</param>
/// <param name="Reset">Failed jobs for reminders that are still owed, given a fresh set of attempts.</param>
public sealed record ReminderEnqueueResult(int Enqueued, int Undeliverable, int Reset);

/// <summary>What the handler did while a pass ran inline (see <see cref="FollowUpReminderService.ProcessOnceAsync"/>).</summary>
public sealed class ReminderTally
{
    public int Sent, Fallback, Deferred;
}

public sealed class FollowUpReminderPassException(string message) : Exception(message);

public sealed record ReminderJobPayload(Guid TaskId, DateTimeOffset DueAtUtc, string Channel);

/// <summary>
/// STORY-009, acceptance criterion 1: when a follow-up task is due, the responsible party is reminded. ALV-009-C01 puts the delivery on the durable background-job queue (ALV-N002). <see cref="EnqueueDueAsync"/> finds every open
/// task that is due and has no recorded delivery for a channel and its current due time, oldest first, and enqueues ONE job per task, due time and channel (the key says so, so a repeat, a restart or a second server enqueues it
/// once); <see cref="FollowUpReminderJobHandler"/> does the telling inside the runner's transaction. Because the rule is "due and not yet reminded" rather than "due in this window", a missed run, a restart, a clock jump or an
/// outage only delays a reminder. A task whose due time is changed is a new reminder; a completed or cancelled task is never reminded. If the responsible person is disabled, cannot see follow-ups or there is none, every office manager
/// who can is told instead. A job that fails (an audit outage, a channel that is down) is retried by the runner, three attempts at a time; one that has failed all three is given a fresh three on a later pass (after
/// <see cref="FailedRetryAfter"/>) for as long as the reminder is still owed, so a reminder is never given up on. <see cref="ProcessOnceAsync"/> does the whole thing inline (enqueue, then run just the reminder jobs) for tests and tools.
/// </summary>
public class FollowUpReminderService(AlveraDbContext db, IPracticeClock clock, IEffectivePermissions permissions, IEnumerable<IReminderChannel> channels)
{
    public const string JobType = "followup.reminder";
    public const int PageSize = 200;
    public const int MaxPages = 5;
    public static readonly TimeSpan FailedRetryAfter = TimeSpan.FromMinutes(5);

    public static string JobKey(Guid taskId, DateTimeOffset dueAtUtc, string channel) => $"followup-reminder:{taskId:N}:{dueAtUtc.UtcTicks}:{channel}";

    /// <summary>Makes the reminder jobs that are owed. Cheap and repeatable: it only reads, and enqueues keyed jobs.</summary>
    public async Task<ReminderEnqueueResult> EnqueueDueAsync(CancellationToken ct)
    {
        var queue = new BackgroundJobQueue(db);
        int enqueued = 0, undeliverable = 0, reset = 0;
        List<Guid>? managers = null;
        foreach (var channel in channels)
        {
            var now = clock.UtcNow;
            var name = channel.Channel;
            reset += await ResetFailedAsync(name, ct);
            for (var page = 0; page < MaxPages; page++)
            {
                var due = await db.FollowUpTasks.AsNoTracking()
                    .Where(t => t.Status == FollowUpStatuses.Open && t.DueAtUtc <= now && !db.FollowUpReminderDeliveries.Any(d => d.TaskId == t.Id && d.DueAtUtc == t.DueAtUtc && d.Channel == name))
                    .OrderBy(t => t.DueAtUtc).ThenBy(t => t.Id).Skip(page * PageSize).Take(PageSize).ToListAsync(ct);
                foreach (var task in due)
                {
                    var key = JobKey(task.Id, task.DueAtUtc, name);
                    if (await db.BackgroundJobs.AsNoTracking().AnyAsync(j => j.IdempotencyKey == key, ct)) continue;      // a job for this reminder exists (waiting, running, done or failed: a failed one is reset above)
                    var primary = task.AssigneeUserId is { } owner && await CanReceiveAsync(db, permissions, owner, ct);
                    if (!primary && (managers ??= await OfficeManagersAsync(db, permissions, ct)).Count == 0) { undeliverable++; continue; }   // nobody to tell: no job until there is
                    await queue.EnqueueAsync(key, JobType, JsonSerializer.Serialize(new ReminderJobPayload(task.Id, task.DueAtUtc, name)), ct);
                    enqueued++;
                }
                if (due.Count < PageSize || enqueued >= PageSize) break;
            }
        }
        return new ReminderEnqueueResult(enqueued, undeliverable, reset);
    }

    /// <summary>
    /// Enqueues what is owed and runs just the reminder jobs, here and now, and says what happened. It raises <see cref="FollowUpReminderPassException"/> when a job failed for a reason other than a channel asking to try again
    /// (an audit outage, say); the failed job stays queued for the next pass.
    /// </summary>
    public async Task<ReminderPassResult> ProcessOnceAsync(CancellationToken ct)
    {
        var tally = new ReminderTally();
        var enqueue = await EnqueueDueAsync(ct);
        var started = DateTimeOffset.UtcNow;
        await new BackgroundJobRunner(db, [new FollowUpReminderJobHandler(clock, permissions, channels, tally)]).ProcessOnceAsync([JobType], ct);
        var failed = await db.BackgroundJobs.AsNoTracking().CountAsync(j => j.JobType == JobType && j.StartedAtUtc >= started && j.Status != BackgroundJobStatus.Succeeded && j.Status != BackgroundJobStatus.InProgress, ct);
        if (failed > tally.Deferred) throw new FollowUpReminderPassException($"{failed - tally.Deferred} reminder job(s) failed and will be tried again.");
        return new ReminderPassResult(tally.Sent, tally.Fallback, enqueue.Undeliverable, tally.Deferred);
    }

    /// <summary>A reminder job that failed all its attempts is retried for as long as the reminder is still owed: after the cool-down it gets a fresh set of attempts.</summary>
    private async Task<int> ResetFailedAsync(string channel, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - FailedRetryAfter;
        var failed = await db.BackgroundJobs.AsNoTracking().Where(j => j.JobType == JobType && j.Status == BackgroundJobStatus.Failed && (j.StartedAtUtc == null || j.StartedAtUtc < cutoff)).Take(PageSize).ToListAsync(ct);
        var reset = 0;
        foreach (var job in failed)
        {
            var payload = job.PayloadJson is null ? null : JsonSerializer.Deserialize<ReminderJobPayload>(job.PayloadJson);
            if (payload is null || payload.Channel != channel) continue;
            var stillOwed = await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.Id == payload.TaskId && t.Status == FollowUpStatuses.Open && t.DueAtUtc == payload.DueAtUtc, ct)
                && !await db.FollowUpReminderDeliveries.AsNoTracking().AnyAsync(d => d.TaskId == payload.TaskId && d.DueAtUtc == payload.DueAtUtc && d.Channel == channel, ct);
            if (!stillOwed) continue;
            reset += await db.BackgroundJobs.Where(j => j.Id == job.Id && j.Status == BackgroundJobStatus.Failed)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BackgroundJobStatus.Pending).SetProperty(j => j.AttemptCount, 0).SetProperty(j => j.LastError, (string?)null).SetProperty(j => j.StartedAtUtc, (DateTimeOffset?)null), ct);
        }
        return reset;
    }

    // ---------- shared with the job handler ----------

    internal static async Task<bool> CanReceiveAsync(AlveraDbContext db, IEffectivePermissions permissions, Guid userId, CancellationToken ct)
    {
        var account = await db.UserAccounts.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Role, u.IsDisabled }).FirstOrDefaultAsync(ct);
        return account is { IsDisabled: false } && await permissions.HasAsync(account.Role, Permission.ViewFollowUps, ct);
    }

    /// <summary>The enabled office managers, when their role can see follow-ups, in a fixed order.</summary>
    internal static async Task<List<Guid>> OfficeManagersAsync(AlveraDbContext db, IEffectivePermissions permissions, CancellationToken ct) =>
        await permissions.HasAsync(Role.OfficeManager, Permission.ViewFollowUps, ct)
            ? await db.UserAccounts.AsNoTracking().Where(u => u.Role == Role.OfficeManager && !u.IsDisabled).OrderBy(u => u.Id).Select(u => u.Id).ToListAsync(ct)
            : [];
}
