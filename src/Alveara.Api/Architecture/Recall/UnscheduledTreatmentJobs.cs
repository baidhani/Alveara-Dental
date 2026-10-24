using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Recall;

public sealed record UnscheduledJobPayload(Guid ItemId, int Sequence);
public sealed record UnscheduledCloseJobPayload(Guid TaskId);

/// <summary>Where one accepted treatment stands now, read from the plan's own state history: whether it still waits to be scheduled, and if not, why (so a task can be closed with the right words).</summary>
public sealed record ItemJudgement(bool Waiting, int Sequence, Guid PatientId, DateTimeOffset RecordedAtUtc, bool Settled, string Why);

public static class UnscheduledTreatmentRules
{
    /// <summary>A task for accepted treatment is due this many days after the acceptance was recorded.</summary>
    public const int DueDaysAfterAcceptance = 7;

    /// <summary>
    /// The current state of a plan item is its latest state row (an item with no row is pending). It is WAITING when it is accepted, not yet scheduled, not yet done, and neither the item nor its plan has been withdrawn.
    /// Null when the item does not exist.
    /// </summary>
    public static async Task<ItemJudgement?> JudgeAsync(AlveraDbContext db, Guid itemId, CancellationToken ct)
    {
        var item = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => new { i.PatientId, i.WithdrawnAtUtc, PlanStatus = db.TreatmentPlans.Where(p => p.Id == i.PlanId).Select(p => p.Status).FirstOrDefault() }).FirstOrDefaultAsync(ct);
        if (item is null) return null;
        var state = await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.ItemId == itemId).OrderByDescending(s => s.Sequence).FirstOrDefaultAsync(ct);
        if (state is null) return new ItemJudgement(false, 0, item.PatientId, default, false, "pending");
        if (item.WithdrawnAtUtc is not null || item.PlanStatus != TreatmentPlanStatuses.Proposed) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "withdrawn");
        if (state.Acceptance == ItemAcceptance.Declined) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "declined");
        if (state.Acceptance != ItemAcceptance.Accepted) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "pending");
        if (state.Completion == ItemCompletion.Completed) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, true, "completed");
        if (state.Scheduling == ItemScheduling.Scheduled) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, true, "scheduled");
        return new ItemJudgement(true, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "waiting");
    }

    public static string ClosureNote(string why) => why switch
    {
        "scheduled" => "The treatment has been scheduled.",
        "completed" => "The treatment has been completed.",
        "declined" => "The patient declined the treatment.",
        _ => "The treatment is no longer proposed.",
    };
}

public sealed record UnscheduledPassResult(int Waiting, int Enqueued, int Closing);

/// <summary>
/// ALV-009-C01: puts accepted but unscheduled treatment on the work queue, as durable jobs (ALV-N002). One pass finds every plan item whose latest state is Accepted, Unscheduled and NotStarted (and not withdrawn) that has no
/// task yet for that state, and enqueues one job per item and acceptance (the key names the item and the state sequence, so scheduling it and un-scheduling it later is a new state and a new task). It also finds the open tasks
/// whose treatment has since been scheduled, completed, declined or withdrawn and enqueues a job to close each. Idempotent keys make a repeat, a restart or two servers harmless.
/// </summary>
public class UnscheduledTreatmentScheduler(AlveraDbContext db, IBackgroundJobQueue queue)
{
    public const string CreateJobType = "followup.unscheduled";
    public const string CloseJobType = "followup.unscheduled.close";
    public const int BatchSize = 500;

    public async Task<UnscheduledPassResult> EnqueueAsync(CancellationToken ct)
    {
        var latest = db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.Sequence == db.TreatmentPlanItemStates.Where(x => x.ItemId == s.ItemId).Max(x => x.Sequence));
        var waiting = await (from s in latest
                             where s.Acceptance == ItemAcceptance.Accepted && s.Scheduling == ItemScheduling.Unscheduled && s.Completion == ItemCompletion.NotStarted
                             join i in db.TreatmentPlanItems on s.ItemId equals i.Id
                             join p in db.TreatmentPlans on i.PlanId equals p.Id
                             where i.WithdrawnAtUtc == null && p.Status == TreatmentPlanStatuses.Proposed
                             orderby s.RecordedAtUtc
                             select new { s.ItemId, s.Sequence }).Take(BatchSize).ToListAsync(ct);
        var itemIds = waiting.Select(w => w.ItemId).ToList();
        var have = (await db.FollowUpTasks.AsNoTracking().Where(t => t.SourcePlanItemId != null && itemIds.Contains(t.SourcePlanItemId.Value)).Select(t => t.SourceKey).ToListAsync(ct)).ToHashSet();
        var enqueued = 0;
        foreach (var w in waiting)
        {
            var key = FollowUpRules.UnscheduledSourceKey(w.ItemId, w.Sequence);
            if (have.Contains(key)) continue;
            await queue.EnqueueAsync(key, CreateJobType, JsonSerializer.Serialize(new UnscheduledJobPayload(w.ItemId, w.Sequence)), ct);
            enqueued++;
        }

        var open = await db.FollowUpTasks.AsNoTracking().Where(t => t.Kind == FollowUpKinds.UnscheduledTreatment && t.Status == FollowUpStatuses.Open).OrderBy(t => t.CreatedAtUtc).Take(BatchSize).Select(t => new { t.Id, t.SourcePlanItemId, t.SourceKey }).ToListAsync(ct);
        var closing = 0;
        foreach (var t in open)
        {
            var j = await UnscheduledTreatmentRules.JudgeAsync(db, t.SourcePlanItemId!.Value, ct);
            if (j is { Waiting: true } && FollowUpRules.UnscheduledSourceKey(t.SourcePlanItemId.Value, j.Sequence) == t.SourceKey) continue;
            await queue.EnqueueAsync($"unscheduled-close:{t.Id:N}", CloseJobType, JsonSerializer.Serialize(new UnscheduledCloseJobPayload(t.Id)), ct);
            closing++;
        }
        return new UnscheduledPassResult(waiting.Count, enqueued, closing);
    }
}

/// <summary>ALV-009-C01: makes one task for accepted treatment nobody has scheduled, inside the runner's transaction. It looks again first: if the treatment has been scheduled, completed, declined or withdrawn, or has moved to a newer state, it does nothing.</summary>
public sealed class UnscheduledTreatmentJobHandler(IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => UnscheduledTreatmentScheduler.CreateJobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<UnscheduledJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("An unscheduled-treatment job carries its plan item and state."))!;
        var key = FollowUpRules.UnscheduledSourceKey(payload.ItemId, payload.Sequence);
        if (await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.SourceKey == key, ct)) return;
        var judgement = await UnscheduledTreatmentRules.JudgeAsync(db, payload.ItemId, ct);
        if (judgement is not { Waiting: true } || judgement.Sequence != payload.Sequence) return;
        var now = clock.UtcNow;
        SystemTaskWriter.Create(db, FollowUpKinds.UnscheduledTreatment, key, judgement.PatientId, "Accepted treatment is not scheduled", judgement.RecordedAtUtc.AddDays(UnscheduledTreatmentRules.DueDaysAfterAcceptance),
            FollowUpPriorities.Normal, payload.ItemId, now, $"Accepted treatment (state {payload.Sequence}) not yet scheduled");
    }
}

/// <summary>ALV-009-C01: closes one task for accepted treatment when what it was about is settled, as the system, inside the runner's transaction. A task already closed, or whose treatment still waits, is left alone.</summary>
public sealed class UnscheduledTreatmentCloseJobHandler(IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => UnscheduledTreatmentScheduler.CloseJobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<UnscheduledCloseJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("A close job carries its task."))!;
        var task = await db.FollowUpTasks.FirstOrDefaultAsync(t => t.Id == payload.TaskId, ct);
        if (task is not { Status: FollowUpStatuses.Open, Kind: FollowUpKinds.UnscheduledTreatment, SourcePlanItemId: { } itemId }) return;
        var judgement = await UnscheduledTreatmentRules.JudgeAsync(db, itemId, ct);
        if (judgement is { Waiting: true } && FollowUpRules.UnscheduledSourceKey(itemId, judgement.Sequence) == task.SourceKey) return;
        var why = judgement?.Why ?? "withdrawn";
        SystemTaskWriter.Close(db, task, judgement?.Settled ?? false, UnscheduledTreatmentRules.ClosureNote(why), clock.UtcNow);
    }
}
