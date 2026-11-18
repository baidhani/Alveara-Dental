using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Recall;

/// <summary>A queued create job: the item and the waiting episode it is for (ALV-N023: <see cref="LinkSequence"/> and <see cref="Ended"/> default to 0/false for jobs queued before the upgrade).</summary>
public sealed record UnscheduledJobPayload(Guid ItemId, int Sequence, int LinkSequence = 0, bool Ended = false);
public sealed record UnscheduledCloseJobPayload(Guid TaskId);

/// <summary>
/// Where one accepted treatment stands now: whether it still waits to be booked and, if not, why (so a task can be closed with the right words). ALV-N023 (briefing revision 2, section 4.4):
/// a waiting period is identified by the item's latest state row (<see cref="Sequence"/>), its latest appointment-link row (<see cref="LinkSequence"/>, 0 if never linked) and whether that link's
/// appointment has ended (cancelled or no-show; <see cref="Ended"/>). All three come from rows that are never changed, so a new waiting period always has a new identity.
/// </summary>
public sealed record ItemJudgement(bool Waiting, int Sequence, Guid PatientId, DateTimeOffset RecordedAtUtc, bool Settled, string Why, int LinkSequence = 0, bool Ended = false, DateTimeOffset? BookedForUtc = null);

public static class UnscheduledTreatmentRules
{
    /// <summary>A task for accepted treatment is due this many days after the acceptance was recorded.</summary>
    public const int DueDaysAfterAcceptance = 7;

    /// <summary>
    /// ALV-N023 (owner decision DR-1): an item is WAITING when it is accepted, not completed, neither it nor its plan is withdrawn, and it has no live appointment link (its latest link row is
    /// Linked to an appointment whose status is Scheduled). A target date (the old "Scheduled" label) never settles it. Null when the item does not exist.
    /// </summary>
    public static async Task<ItemJudgement?> JudgeAsync(AlveraDbContext db, Guid itemId, CancellationToken ct)
    {
        var item = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => new { i.PatientId, i.WithdrawnAtUtc, PlanStatus = db.TreatmentPlans.Where(p => p.Id == i.PlanId).Select(p => p.Status).FirstOrDefault() }).FirstOrDefaultAsync(ct);
        if (item is null) return null;
        var state = await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.ItemId == itemId).OrderByDescending(s => s.Sequence).FirstOrDefaultAsync(ct);
        if (state is null) return new ItemJudgement(false, 0, item.PatientId, default, false, "pending");
        var link = (await LinksAsync(db, [itemId], ct)).GetValueOrDefault(itemId);
        var (l, ended) = (link?.Sequence ?? 0, link?.Ended ?? false);
        if (item.WithdrawnAtUtc is not null || item.PlanStatus != TreatmentPlanStatuses.Proposed) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "withdrawn", l, ended);
        if (state.Acceptance == ItemAcceptance.Declined) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "declined", l, ended);
        if (state.Acceptance != ItemAcceptance.Accepted) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "pending", l, ended);
        if (state.Completion == ItemCompletion.Completed) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, true, "completed", l, ended);
        if (link is { Live: true }) return new ItemJudgement(false, state.Sequence, item.PatientId, state.RecordedAtUtc, true, "booked", l, ended, link.StartUtc);
        return new ItemJudgement(true, state.Sequence, item.PatientId, state.RecordedAtUtc, false, "waiting", l, ended);
    }

    /// <summary>The latest link row of each item: its sequence, whether it is a live booking (Linked, appointment Scheduled) and whether its appointment has ended (Linked, Cancelled/NoShow).</summary>
    public static async Task<Dictionary<Guid, LinkState>> LinksAsync(AlveraDbContext db, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];
        var latest = (await db.TreatmentPlanItemAppointmentLinks.AsNoTracking().Where(l => itemIds.Contains(l.ItemId)).Select(l => new { l.ItemId, l.Sequence, l.Action, l.AppointmentId }).ToListAsync(ct))
            .GroupBy(l => l.ItemId).Select(g => g.OrderByDescending(l => l.Sequence).First()).ToList();
        var ids = latest.Select(l => l.AppointmentId).Distinct().ToList();
        var appointments = await db.Appointments.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.Status, a.StartUtc }).ToDictionaryAsync(a => a.Id, ct);
        return latest.ToDictionary(l => l.ItemId, l =>
        {
            var linked = l.Action == AppointmentLinkActions.Linked;
            var a = appointments[l.AppointmentId];
            return new LinkState(l.Sequence, linked && a.Status == AppointmentStatuses.Scheduled, linked && a.Status != AppointmentStatuses.Scheduled, a.StartUtc);
        });
    }

    /// <summary>
    /// ALV-N023: whether a task's key is the waiting episode's. The versioned key matches exactly; an old-format key (<c>unscheduled:{item}:{seq}</c>, written before ALV-N023) is the same
    /// episode only as <c>s{seq}, l0, open</c>: old tasks were made only for an Accepted+Unscheduled state row that was then the latest, and every later change appended a higher row, so an
    /// old task can never stand for a later waiting period (briefing revision 2, section 4.4).
    /// </summary>
    public static bool SameEpisode(string taskKey, Guid itemId, int sequence, int linkSequence, bool ended) =>
        taskKey == FollowUpRules.UnscheduledEpisodeKey(itemId, sequence, linkSequence, ended)
        || (linkSequence == 0 && !ended && taskKey == FollowUpRules.UnscheduledSourceKey(itemId, sequence));

    public static string ClosureNote(ItemJudgement? j, string bookedDay) => j?.Why switch
    {
        "booked" => $"A linked appointment is booked for {bookedDay}.",
        "completed" => "The treatment has been completed.",
        "declined" => "The patient declined the treatment.",
        "waiting" => j.Ended ? "Continued in a new follow-up: the linked appointment was cancelled or marked no-show."
            : j.LinkSequence > 0 ? "Continued in a new follow-up: the appointment link was removed." : "Continued in a new follow-up: the treatment's state changed.",
        _ => "The treatment is no longer proposed.",
    };
}

/// <summary>ALV-N023: an item's latest appointment-link row as the follow-up rule reads it.</summary>
public sealed record LinkState(int Sequence, bool Live, bool Ended, DateTimeOffset StartUtc);

public sealed record UnscheduledPassResult(int Waiting, int Enqueued, int Closing);

/// <summary>
/// ALV-009-C01: puts accepted but unbooked treatment on the work queue, as durable jobs (ALV-N002). ALV-N023: one pass finds every plan item whose latest state is Accepted and NotStarted (not withdrawn,
/// plan open) and that has no live appointment link, and enqueues one job per waiting episode (its key names the item, the state row, the link row and whether that link's appointment ended), unless
/// a task for that episode exists (old-format keys count as described in <see cref="UnscheduledTreatmentRules.SameEpisode"/>). It also finds the open tasks whose episode has ended and enqueues a
/// job to close each. Idempotent keys make a repeat, a restart or two servers harmless.
/// </summary>
public class UnscheduledTreatmentScheduler(AlveraDbContext db, IBackgroundJobQueue queue)
{
    public const string CreateJobType = "followup.unscheduled";
    public const string CloseJobType = "followup.unscheduled.close";
    public const int BatchSize = 500;

    public async Task<UnscheduledPassResult> EnqueueAsync(CancellationToken ct)
    {
        var latest = db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.Sequence == db.TreatmentPlanItemStates.Where(x => x.ItemId == s.ItemId).Max(x => x.Sequence));
        var accepted = await (from s in latest
                              where s.Acceptance == ItemAcceptance.Accepted && s.Completion == ItemCompletion.NotStarted
                              join i in db.TreatmentPlanItems on s.ItemId equals i.Id
                              join p in db.TreatmentPlans on i.PlanId equals p.Id
                              where i.WithdrawnAtUtc == null && p.Status == TreatmentPlanStatuses.Proposed
                              orderby s.RecordedAtUtc
                              select new { s.ItemId, s.Sequence }).Take(BatchSize).ToListAsync(ct);
        var links = await UnscheduledTreatmentRules.LinksAsync(db, accepted.Select(a => a.ItemId).ToList(), ct);
        var waiting = accepted.Where(a => links.GetValueOrDefault(a.ItemId) is not { Live: true })
            .Select(a => new { a.ItemId, a.Sequence, L = links.GetValueOrDefault(a.ItemId)?.Sequence ?? 0, Ended = links.GetValueOrDefault(a.ItemId)?.Ended ?? false }).ToList();
        var itemIds = waiting.Select(w => w.ItemId).ToList();
        var have = (await db.FollowUpTasks.AsNoTracking().Where(t => t.SourcePlanItemId != null && itemIds.Contains(t.SourcePlanItemId.Value)).Select(t => new { Item = t.SourcePlanItemId!.Value, t.SourceKey }).ToListAsync(ct))
            .ToLookup(t => t.Item, t => t.SourceKey);
        var enqueued = 0;
        foreach (var w in waiting)
        {
            if (have[w.ItemId].Any(k => UnscheduledTreatmentRules.SameEpisode(k, w.ItemId, w.Sequence, w.L, w.Ended))) continue;
            var key = FollowUpRules.UnscheduledEpisodeKey(w.ItemId, w.Sequence, w.L, w.Ended);
            await queue.EnqueueAsync(key, CreateJobType, JsonSerializer.Serialize(new UnscheduledJobPayload(w.ItemId, w.Sequence, w.L, w.Ended)), ct);
            enqueued++;
        }

        var open = await db.FollowUpTasks.AsNoTracking().Where(t => t.Kind == FollowUpKinds.UnscheduledTreatment && t.Status == FollowUpStatuses.Open).OrderBy(t => t.CreatedAtUtc).Take(BatchSize).Select(t => new { t.Id, t.SourcePlanItemId, t.SourceKey }).ToListAsync(ct);
        var closing = 0;
        foreach (var t in open)
        {
            var j = await UnscheduledTreatmentRules.JudgeAsync(db, t.SourcePlanItemId!.Value, ct);
            if (j is { Waiting: true } && UnscheduledTreatmentRules.SameEpisode(t.SourceKey!, t.SourcePlanItemId.Value, j.Sequence, j.LinkSequence, j.Ended)) continue;
            await queue.EnqueueAsync($"unscheduled-close:{t.Id:N}", CloseJobType, JsonSerializer.Serialize(new UnscheduledCloseJobPayload(t.Id)), ct);
            closing++;
        }
        return new UnscheduledPassResult(waiting.Count, enqueued, closing);
    }
}

/// <summary>ALV-009-C01: makes one task for accepted treatment nobody has booked, inside the runner's transaction. It looks again first: if the episode has ended (booked, completed, declined, withdrawn, or a newer episode began) nothing is made.</summary>
public sealed class UnscheduledTreatmentJobHandler(IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => UnscheduledTreatmentScheduler.CreateJobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<UnscheduledJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("An unscheduled-treatment job carries its plan item and state."))!;
        var keys = await db.FollowUpTasks.AsNoTracking().Where(t => t.SourcePlanItemId == payload.ItemId).Select(t => t.SourceKey).ToListAsync(ct);
        if (keys.Any(k => k is not null && UnscheduledTreatmentRules.SameEpisode(k, payload.ItemId, payload.Sequence, payload.LinkSequence, payload.Ended))) return;
        var judgement = await UnscheduledTreatmentRules.JudgeAsync(db, payload.ItemId, ct);
        if (judgement is not { Waiting: true } || judgement.Sequence != payload.Sequence || judgement.LinkSequence != payload.LinkSequence || judgement.Ended != payload.Ended) return;
        var now = clock.UtcNow;
        SystemTaskWriter.Create(db, FollowUpKinds.UnscheduledTreatment, FollowUpRules.UnscheduledEpisodeKey(payload.ItemId, payload.Sequence, payload.LinkSequence, payload.Ended), judgement.PatientId,
            "Accepted treatment is not booked", judgement.RecordedAtUtc.AddDays(UnscheduledTreatmentRules.DueDaysAfterAcceptance), FollowUpPriorities.Normal, payload.ItemId, now,
            payload.Ended ? $"Accepted treatment (state {payload.Sequence}): its linked appointment was cancelled or marked no-show"
                : payload.LinkSequence > 0 ? $"Accepted treatment (state {payload.Sequence}): its appointment link was removed" : $"Accepted treatment (state {payload.Sequence}) not yet booked");
    }
}

/// <summary>ALV-009-C01: closes one task for accepted treatment when its waiting episode has ended, as the system, inside the runner's transaction. A task already closed, or whose episode still waits, is left alone.</summary>
public sealed class UnscheduledTreatmentCloseJobHandler(IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => UnscheduledTreatmentScheduler.CloseJobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<UnscheduledCloseJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("A close job carries its task."))!;
        var task = await db.FollowUpTasks.FirstOrDefaultAsync(t => t.Id == payload.TaskId, ct);
        if (task is not { Status: FollowUpStatuses.Open, Kind: FollowUpKinds.UnscheduledTreatment, SourcePlanItemId: { } itemId }) return;
        var judgement = await UnscheduledTreatmentRules.JudgeAsync(db, itemId, ct);
        if (judgement is { Waiting: true } && UnscheduledTreatmentRules.SameEpisode(task.SourceKey!, itemId, judgement.Sequence, judgement.LinkSequence, judgement.Ended)) return;
        var day = judgement?.BookedForUtc is { } at ? clock.ToPracticeLocal(at).ToString("yyyy-MM-dd") : "";
        SystemTaskWriter.Close(db, task, judgement?.Settled ?? false, UnscheduledTreatmentRules.ClosureNote(judgement, day), clock.UtcNow);
    }
}
