using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Recall;

public sealed record RecallJobPayload(Guid PatientId, string DueDate);

/// <summary>What one scheduling pass found: patients looked at, jobs enqueued (already-enqueued ones are counted too: enqueuing the same key twice is a no-op).</summary>
public sealed record RecallPassResult(int Considered, int Due, int Enqueued);

/// <summary>
/// ALV-009-C01: turns "this patient is due for a recall" into durable background jobs (ALV-N002). It does nothing while recall is switched off in the settings (the owner turns it on deliberately, so deploying the story never
/// fills the queue with a backlog). It reads each patient's last COMPLETED visit, leaves out patients who opted out, are inactive, already have an open recall task or already have a future appointment, works out who is due with
/// the same pure arithmetic as everywhere else (<see cref="RecallCalculator"/>), and enqueues one job per patient and due date. The job's key is the task's source key, so a second pass, a restart or two servers enqueue it once.
/// Oldest visits first, at most <see cref="BatchSize"/> patients a pass: a big backlog is worked down over several passes rather than in one transaction.
/// </summary>
public class RecallScheduler(AlveraDbContext db, IBackgroundJobQueue queue, IPracticeClock clock)
{
    public const string JobType = "followup.recall";
    public const int BatchSize = 500;

    public async Task<RecallPassResult> EnqueueDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var settings = await db.RecallSettings.AsNoTracking().SingleAsync(ct);
        if (!settings.Enabled) return new RecallPassResult(0, 0, 0);

        var seenSince = now.AddMonths(-settings.MaxVisitAgeMonths - 1);                                // a month of slack: the exact lapse test is the calculator's, on practice dates
        var last = await db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatuses.Scheduled && a.FlowState == PatientFlowStates.Completed && a.StartUtc >= seenSince && a.StartUtc <= now)
            .GroupBy(a => a.PatientId).Select(g => new { PatientId = g.Key, Last = g.Max(a => a.StartUtc) })
            .Where(x => db.Patients.Any(p => p.Id == x.PatientId && p.IsActive)
                     && !db.PatientRecallPlans.Any(r => r.PatientId == x.PatientId && r.OptedOut)
                     && !db.FollowUpTasks.Any(t => t.PatientId == x.PatientId && t.Kind == FollowUpKinds.Recall && t.Status == FollowUpStatuses.Open)
                     && !db.Appointments.Any(a => a.PatientId == x.PatientId && a.Status == AppointmentStatuses.Scheduled && a.StartUtc > now))
            .OrderBy(x => x.Last).Take(BatchSize).ToListAsync(ct);
        var plans = await db.PatientRecallPlans.AsNoTracking().Where(r => last.Select(l => l.PatientId).Contains(r.PatientId)).ToDictionaryAsync(r => r.PatientId, ct);

        int due = 0, enqueued = 0;
        foreach (var candidate in last)
        {
            var input = new RecallInput(candidate.Last, settings.DefaultIntervalDays, plans.GetValueOrDefault(candidate.PatientId)?.IntervalDays, false, settings.LeadDays, settings.MaxVisitAgeMonths, false, false);
            var decision = RecallCalculator.Evaluate(input, now, clock);
            if (decision.Verdict != RecallVerdict.Due || decision.NextDueUtc is not { } next) continue;
            due++;
            var dueDate = DateOnly.FromDateTime(clock.ToPracticeLocal(next).Date);
            var key = FollowUpRules.RecallSourceKey(candidate.PatientId, dueDate);
            await queue.EnqueueAsync(key, JobType, JsonSerializer.Serialize(new RecallJobPayload(candidate.PatientId, dueDate.ToString("yyyy-MM-dd"))), ct);
            enqueued++;
        }
        return new RecallPassResult(last.Count, due, enqueued);
    }
}

/// <summary>
/// ALV-009-C01: makes one recall task, inside the job runner's transaction. It looks again at the world as it is NOW (the patient may have booked, opted out or been given another open recall since the job was
/// enqueued, or the practice may have switched recall off), and does nothing then; a task that already exists for this key is left alone. Otherwise it stages the unassigned Recall task, its history entry and its audit entry
/// through <see cref="SystemTaskWriter"/>; the runner saves them together with the job's receipt. Never saves itself.
/// </summary>
public sealed class RecallTaskJobHandler(IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => RecallScheduler.JobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext db, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<RecallJobPayload>(job.PayloadJson ?? throw new InvalidOperationException("A recall job carries its patient and due date."))!;
        var dueDate = DateOnly.ParseExact(payload.DueDate, "yyyy-MM-dd");
        var key = FollowUpRules.RecallSourceKey(payload.PatientId, dueDate);
        if (await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.SourceKey == key, ct)) return;      // already made (an earlier attempt committed it)

        var now = clock.UtcNow;
        var settings = await db.RecallSettings.AsNoTracking().SingleAsync(ct);
        if (!settings.Enabled) return;
        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == payload.PatientId).Select(p => new { p.IsActive }).FirstOrDefaultAsync(ct);
        if (patient is not { IsActive: true }) return;
        var plan = await db.PatientRecallPlans.AsNoTracking().FirstOrDefaultAsync(r => r.PatientId == payload.PatientId, ct);
        var last = await db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == payload.PatientId && a.Status == AppointmentStatuses.Scheduled && a.FlowState == PatientFlowStates.Completed && a.StartUtc <= now)
            .MaxAsync(a => (DateTimeOffset?)a.StartUtc, ct);
        var open = await db.FollowUpTasks.AsNoTracking().AnyAsync(t => t.PatientId == payload.PatientId && t.Kind == FollowUpKinds.Recall && t.Status == FollowUpStatuses.Open, ct);
        var booked = await db.Appointments.AsNoTracking().AnyAsync(a => a.PatientId == payload.PatientId && a.Status == AppointmentStatuses.Scheduled && a.StartUtc > now, ct);

        var decision = RecallCalculator.Evaluate(new RecallInput(last, settings.DefaultIntervalDays, plan?.IntervalDays, plan?.OptedOut ?? false, settings.LeadDays, settings.MaxVisitAgeMonths, open, booked), now, clock);
        if (decision.Verdict != RecallVerdict.Due || decision.NextDueUtc is not { } nextDue) return;
        // the key names the date the job was enqueued for; if the patient was seen again since, their due date moved and a later pass makes the right task
        if (DateOnly.FromDateTime(clock.ToPracticeLocal(nextDue).Date) != dueDate) return;

        SystemTaskWriter.Create(db, FollowUpKinds.Recall, key, payload.PatientId, "Patient recall: schedule the next visit", nextDue, FollowUpPriorities.Normal, null, now,
            $"Recall due {dueDate:yyyy-MM-dd} ({decision.IntervalDays} days)");
    }
}
