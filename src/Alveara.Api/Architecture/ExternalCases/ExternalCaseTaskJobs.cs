using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.ExternalCases;

public sealed record ExternalCaseTaskJobPayload(string Kind, Guid CaseId, string DueOn);
public sealed record ExternalCaseTaskCloseJobPayload(Guid TaskId);
public sealed record ExternalCaseTaskPassResult(int Wanting, int Enqueued, int Closing);

/// <summary>
/// ALV-009-C02: finds the referral and lab cases whose chase task should exist and the chase tasks that should not, and ENQUEUES durable ALV-N002 background jobs for them (the runner does the work, with its retries,
/// restart recovery and effect receipts). A case wants a task from its due date until it is finished (<see cref="ExternalCaseRules.WantsTask"/>): a case not yet due makes no task, so the work queue only shows what can
/// be acted on, and an overdue case shows there with its link back to the case. Job keys are the task's source key (one chase per case and due date) and <c>externalcase-close:{task}</c>, so a repeat, a restart or a second
/// server enqueues nothing new. The scheduler only reads and enqueues.
/// </summary>
public class ExternalCaseTaskScheduler(AlveraDbContext db, IBackgroundJobQueue queue, IPracticeClock clock)
{
    public const string CreateJobType = "externalcase.task";
    public const string CloseJobType = "externalcase.task.close";
    public const int BatchSize = 500;

    public async Task<ExternalCaseTaskPassResult> EnqueueAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).Date);
        var tracked = new[] { ExternalCaseRules.Sent, ExternalCaseRules.InProgress, ExternalCaseRules.Received };

        var referrals = await db.ReferralCases.AsNoTracking().Where(c => tracked.Contains(c.Status) && c.DueOn != null && c.DueOn <= today).OrderBy(c => c.DueOn).Take(BatchSize)
            .Select(c => new { c.Id, DueOn = c.DueOn!.Value }).ToListAsync(ct);
        var labs = await db.LabCases.AsNoTracking().Where(c => tracked.Contains(c.Status) && c.DueOn != null && c.DueOn <= today).OrderBy(c => c.DueOn).Take(BatchSize)
            .Select(c => new { c.Id, DueOn = c.DueOn!.Value }).ToListAsync(ct);
        var referralIds = referrals.Select(r => r.Id).ToList();
        var labIds = labs.Select(l => l.Id).ToList();
        var have = (await db.FollowUpTasks.AsNoTracking().Where(t => (t.SourceReferralCaseId != null && referralIds.Contains(t.SourceReferralCaseId.Value)) || (t.SourceLabCaseId != null && labIds.Contains(t.SourceLabCaseId.Value)))
            .Select(t => t.SourceKey).ToListAsync(ct)).ToHashSet();

        var wanting = referrals.Count + labs.Count;
        var enqueued = 0;
        foreach (var r in referrals)
        {
            var key = FollowUpRules.ReferralSourceKey(r.Id, r.DueOn);
            if (have.Contains(key)) continue;
            await queue.EnqueueAsync(key, CreateJobType, JsonSerializer.Serialize(new ExternalCaseTaskJobPayload(ExternalCaseKinds.Referral, r.Id, r.DueOn.ToString("yyyy-MM-dd"))), ct);
            enqueued++;
        }
        foreach (var l in labs)
        {
            var key = FollowUpRules.LabSourceKey(l.Id, l.DueOn);
            if (have.Contains(key)) continue;
            await queue.EnqueueAsync(key, CreateJobType, JsonSerializer.Serialize(new ExternalCaseTaskJobPayload(ExternalCaseKinds.Lab, l.Id, l.DueOn.ToString("yyyy-MM-dd"))), ct);
            enqueued++;
        }

        // open chase tasks whose case no longer wants them: finished, sent back to draft, or given another due date
        var kinds = new[] { FollowUpKinds.ReferralFollowUp, FollowUpKinds.LabCaseDue };
        var open = await db.FollowUpTasks.AsNoTracking().Where(t => kinds.Contains(t.Kind) && t.Status == FollowUpStatuses.Open).OrderBy(t => t.CreatedAtUtc).Take(BatchSize)
            .Select(t => new { t.Id, t.SourceKey, t.SourceReferralCaseId, t.SourceLabCaseId }).ToListAsync(ct);
        var closing = 0;
        foreach (var t in open)
        {
            if (await ExternalCaseTaskRules.StillWantedAsync(db, t.SourceKey!, t.SourceReferralCaseId, t.SourceLabCaseId, today, ct)) continue;
            await queue.EnqueueAsync($"externalcase-close:{t.Id:N}", CloseJobType, JsonSerializer.Serialize(new ExternalCaseTaskCloseJobPayload(t.Id)), ct);
            closing++;
        }
        return new ExternalCaseTaskPassResult(wanting, enqueued, closing);
    }
}

public static class ExternalCaseTaskRules
{
    /// <summary>Whether the case behind a chase task still wants exactly this task: it has gone out and is not finished, its due date has come, and the task's key still names that due date.</summary>
    public static async Task<bool> StillWantedAsync(AlveraDbContext db, string sourceKey, Guid? referralCaseId, Guid? labCaseId, DateOnly today, CancellationToken ct)
    {
        if (referralCaseId is { } r)
        {
            var c = await db.ReferralCases.AsNoTracking().Where(x => x.Id == r).Select(x => new { x.Status, x.DueOn }).FirstOrDefaultAsync(ct);
            return c is not null && ExternalCaseRules.WantsTask(c.Status, c.DueOn, today) && FollowUpRules.ReferralSourceKey(r, c.DueOn!.Value) == sourceKey;
        }
        if (labCaseId is { } l)
        {
            var c = await db.LabCases.AsNoTracking().Where(x => x.Id == l).Select(x => new { x.Status, x.DueOn }).FirstOrDefaultAsync(ct);
            return c is not null && ExternalCaseRules.WantsTask(c.Status, c.DueOn, today) && FollowUpRules.LabSourceKey(l, c.DueOn!.Value) == sourceKey;
        }
        return false;
    }
}

/// <summary>
/// ALV-009-C02: makes one chase task, inside the job runner's transaction. It looks again at the case as it is NOW (it may have come back, been completed or cancelled, or been given another due date since the job was
/// enqueued) and does nothing then; a task that already exists for the key is left alone. Otherwise it stages the unassigned system task (priority High when the due date is already past), its history entry and its audit
/// entry through <see cref="SystemTaskWriter"/>; the runner saves them together with the job's receipt. Never saves itself. The task title names the outside party, never the reason or any note.
/// </summary>
public sealed class ExternalCaseTaskJobHandler(IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => ExternalCaseTaskScheduler.CreateJobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ExternalCaseTaskJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("A case task job carries its case and due date."))!;
        var dueOn = DateOnly.ParseExact(payload.DueOn, "yyyy-MM-dd");
        var lab = payload.Kind == ExternalCaseKinds.Lab;
        var key = lab ? FollowUpRules.LabSourceKey(payload.CaseId, dueOn) : FollowUpRules.ReferralSourceKey(payload.CaseId, dueOn);
        if (await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.SourceKey == key, ct)) return;      // already made (an earlier attempt committed it)

        var today = DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).Date);
        if (!await ExternalCaseTaskRules.StillWantedAsync(db, key, lab ? null : payload.CaseId, lab ? payload.CaseId : null, today, ct)) return;
        var info = lab
            ? await db.LabCases.AsNoTracking().Where(c => c.Id == payload.CaseId).Select(c => new { c.PatientId, Party = db.ExternalParties.Where(p => p.Id == c.PartyId).Select(p => p.Name).First() }).FirstAsync(ct)
            : await db.ReferralCases.AsNoTracking().Where(c => c.Id == payload.CaseId).Select(c => new { c.PatientId, Party = db.ExternalParties.Where(p => p.Id == c.PartyId).Select(p => p.Name).First() }).FirstAsync(ct);
        var title = Trim($"{(lab ? "Lab case due" : "Referral to follow up")}: {info.Party}");
        var priority = dueOn < today ? FollowUpPriorities.High : FollowUpPriorities.Normal;
        SystemTaskWriter.Create(db, lab ? FollowUpKinds.LabCaseDue : FollowUpKinds.ReferralFollowUp, key, info.PatientId, title, ExternalCaseRules.TaskDueAt(dueOn, clock), priority, null, clock.UtcNow,
            $"{(lab ? "Lab case" : "Referral")} {payload.CaseId:N} due {dueOn:yyyy-MM-dd}", lab ? null : payload.CaseId, lab ? payload.CaseId : null);
    }

    private static string Trim(string s) => s.Length <= FollowUpRules.TitleMaxLength ? s : s[..(FollowUpRules.TitleMaxLength - 1)] + "…";
}

/// <summary>ALV-009-C02: closes one chase task, as the system and inside the runner's transaction, when its case no longer wants it. Completed when the case was completed, otherwise cancelled with the reason in the note. A task already closed, or one its case still wants, is left alone.</summary>
public sealed class ExternalCaseTaskCloseJobHandler(IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => ExternalCaseTaskScheduler.CloseJobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<ExternalCaseTaskCloseJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("A case task close job carries its task."))!;
        var task = await db.FollowUpTasks.FirstOrDefaultAsync(t => t.Id == payload.TaskId, ct);
        if (task is not { Status: FollowUpStatuses.Open } || (task.Kind != FollowUpKinds.ReferralFollowUp && task.Kind != FollowUpKinds.LabCaseDue)) return;
        var today = DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).Date);
        if (await ExternalCaseTaskRules.StillWantedAsync(db, task.SourceKey!, task.SourceReferralCaseId, task.SourceLabCaseId, today, ct)) return;

        var status = task.SourceReferralCaseId is { } r
            ? await db.ReferralCases.AsNoTracking().Where(c => c.Id == r).Select(c => c.Status).FirstAsync(ct)
            : await db.LabCases.AsNoTracking().Where(c => c.Id == task.SourceLabCaseId!.Value).Select(c => c.Status).FirstAsync(ct);
        var what = task.Kind == FollowUpKinds.LabCaseDue ? "lab case" : "referral";
        var (completed, note) = status switch
        {
            ExternalCaseRules.Completed => (true, $"The {what} was completed."),
            ExternalCaseRules.Cancelled => (false, $"The {what} was cancelled."),
            ExternalCaseRules.Draft => (false, $"The {what} is back to a draft."),
            _ => (false, $"The {what} has a new due date."),
        };
        SystemTaskWriter.Close(db, task, completed, note, clock.UtcNow);
    }
}
