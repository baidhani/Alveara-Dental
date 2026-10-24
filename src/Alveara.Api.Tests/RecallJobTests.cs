using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Recall;
using Alveara.Api.Architecture.Scheduling;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C01, recall as durable background work (ALV-N002) against a real SQL Server: nothing happens while the practice has recall switched off; a patient whose last completed visit puts them inside the lead time gets exactly one
/// unassigned Recall task with its history and audit entry however many times the passes and the job runner run; a patient who opted out, is inactive, has an open recall or a future appointment, has lapsed or was never seen gets none;
/// the job looks at the world again when it runs; a failure (here: the audit write) stores nothing and the retry makes the one task; and a job interrupted by a restart is recovered without making a second.
/// </summary>
public class RecallJobTests : SafetyTestBase
{
    // the visits are in 2027 so that the (real) calendar's availability rules apply; the clock the tests inject says "now" is after them
    private static readonly DateTime FirstVisitLocal = new(2027, 3, 2, 9, 0, 0);

    private async Task<DateTimeOffset> CompletedVisitAsync(Guid patient, DateTime? startLocal = null)
    {
        var scheduled = await S.ScheduleAsync(S.Request(patient, startLocal ?? FirstVisitLocal));
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [Appointments] SET [FlowState] = N'Completed' WHERE [Id] = {scheduled.Appointment.Id}");
        return scheduled.Appointment.StartUtc;
    }

    private async Task EnableRecallAsync(bool enabled = true, int? interval = null, int? lead = null)
    {
        await using var db = Fixture.CreateContext();
        var s = await db.RecallSettings.SingleAsync();
        s.Enabled = enabled;
        if (interval is { } i) s.DefaultIntervalDays = i;
        if (lead is { } l) s.LeadDays = l;
        await db.SaveChangesAsync();
    }

    private async Task<RecallPassResult> SchedulePassAsync(DateTimeOffset now)
    {
        await using var db = Fixture.CreateContext();
        return await new RecallScheduler(db, new BackgroundJobQueue(db), new TestClock(now)).EnqueueDueAsync(now, default);
    }

    private async Task<int> RunJobsAsync(DateTimeOffset now)
    {
        await using var db = Fixture.CreateContext();
        var clock = new TestClock(now);
        return await new BackgroundJobRunner(db, [new RecallTaskJobHandler(clock), new UnscheduledTreatmentJobHandler(clock), new UnscheduledTreatmentCloseJobHandler(clock)]).ProcessOnceAsync();
    }

    private async Task<List<FollowUpTask>> RecallTasksAsync() { await using var db = Fixture.CreateContext(); return await db.FollowUpTasks.AsNoTracking().Where(t => t.Kind == FollowUpKinds.Recall).ToListAsync(); }
    private async Task<int> JobsAsync(string? status = null) { await using var db = Fixture.CreateContext(); return await db.BackgroundJobs.CountAsync(j => j.JobType == RecallScheduler.JobType && (status == null || j.Status.ToString() == status)); }

    // ---------- the switch, the lead time, the one task ----------

    [Fact]
    public async Task A_second_job_for_a_task_that_already_exists_succeeds_and_makes_nothing_more()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(170);
        await SchedulePassAsync(now);
        await RunJobsAsync(now);
        await using (var close = Fixture.CreateContext())                                                              // the task is closed, so only the key (not the open-recall rule) can stop a second one
            await close.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Status] = N'Completed', [ClosedAtUtc] = DATEADD(YEAR, 5, SYSDATETIMEOFFSET()), [ClosedByUserId] = {S.Actor} WHERE [Kind] = N'Recall'");
        string payload;
        await using (var db = Fixture.CreateContext())
        {
            payload = (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == RecallScheduler.JobType)).PayloadJson!;
            await new BackgroundJobQueue(db).EnqueueAsync("replay-of-the-recall-job", RecallScheduler.JobType, payload, default);        // the same task asked for under another job key
        }
        await RunJobsAsync(now);
        Assert.Single(await RecallTasksAsync());
        await using var check = Fixture.CreateContext();
        Assert.Equal(new[] { BackgroundJobStatus.Succeeded, BackgroundJobStatus.Succeeded }, await check.BackgroundJobs.AsNoTracking().Where(j => j.JobType == RecallScheduler.JobType).Select(j => j.Status).ToArrayAsync());
    }

    [Fact]
    public async Task Nothing_is_enqueued_while_recall_is_switched_off()
    {
        var visit = await CompletedVisitAsync(Ann);
        var result = await SchedulePassAsync(visit.AddDays(400));
        Assert.Equal(new RecallPassResult(0, 0, 0), result);
        Assert.Equal(0, await JobsAsync());
    }

    [Fact]
    public async Task A_patient_inside_the_lead_time_gets_exactly_one_unassigned_recall_task_with_its_history_and_audit_entry()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(170);                                                  // next due in 10 days, lead time 30
        var pass = await SchedulePassAsync(now);
        Assert.Equal((1, 1, 1), (pass.Considered, pass.Due, pass.Enqueued));
        Assert.Equal(1, await RunJobsAsync(now));

        var task = Assert.Single(await RecallTasksAsync());
        Assert.Equal((Ann, FollowUpKinds.Recall, FollowUpStatuses.Open, FollowUpPriorities.Normal, null, null), (task.PatientId, task.Kind, task.Status, task.Priority, task.AssigneeUserId, task.CreatedByUserId));
        Assert.Equal(RecallCalculator.NextDue(visit, 180, Alveara.Api.Tests.SchedulingTestSupport.Clock), task.DueAtUtc);
        Assert.StartsWith($"recall:{Ann:N}:", task.SourceKey);
        await using var db = Fixture.CreateContext();
        var events = await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == task.Id).ToListAsync();
        Assert.Equal((FollowUpEventKinds.SystemCreated, null), (events.Single().Kind, events.Single().ActorUserId));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.FollowUpCreated);
        Assert.Null(audit.PerformedByUserAccountId);
        Assert.DoesNotContain("Ann", audit.Details);
        var job = await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == RecallScheduler.JobType);
        Assert.Equal(BackgroundJobStatus.Succeeded, job.Status);
        Assert.True(await db.BackgroundJobEffectReceipts.AnyAsync(r => r.JobId == job.Id));
    }

    [Fact]
    public async Task The_lead_time_starts_to_the_second_and_a_patient_seen_recently_is_not_yet_due()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var threshold = RecallCalculator.NextDue(visit, 180, Alveara.Api.Tests.SchedulingTestSupport.Clock).AddDays(-30);
        Assert.Equal(0, (await SchedulePassAsync(threshold.AddSeconds(-1))).Due);
        Assert.Equal(1, (await SchedulePassAsync(threshold)).Due);
    }

    [Fact]
    public async Task However_often_the_passes_and_the_runner_run_there_is_one_job_and_one_task()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(175);
        for (var i = 0; i < 3; i++) { await SchedulePassAsync(now); await RunJobsAsync(now); }
        Assert.Single(await RecallTasksAsync());
        Assert.Equal(1, await JobsAsync());
        // the task is worked and closed; the same due date is never made again
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Status] = N'Completed', [ClosedAtUtc] = DATEADD(YEAR, 5, SYSDATETIMEOFFSET()), [ClosedByUserId] = {S.Actor} WHERE [Kind] = N'Recall'");
        await SchedulePassAsync(now); await RunJobsAsync(now);
        Assert.Single(await RecallTasksAsync());
        Assert.Equal(1, await JobsAsync());
    }

    // ---------- who is not recalled ----------

    [Fact]
    public async Task A_patient_who_opted_out_is_inactive_has_lapsed_has_an_open_recall_or_a_booking_or_was_never_seen_gets_no_recall()
    {
        await EnableRecallAsync();
        var optedOut = await S.PatientAsync("Opt", "Out");
        var inactive = await S.PatientAsync("Ina", "Ctive");
        var lapsed = await S.PatientAsync("Lap", "Sed");
        var booked = await S.PatientAsync("Boo", "Ked");
        var never = await S.PatientAsync("Nev", "Er");
        var visit = await CompletedVisitAsync(optedOut, FirstVisitLocal);
        await CompletedVisitAsync(inactive, FirstVisitLocal.AddHours(1));
        await CompletedVisitAsync(booked, FirstVisitLocal.AddHours(2));
        await CompletedVisitAsync(Ann, FirstVisitLocal.AddHours(3));
        await using (var db = Fixture.CreateContext())
        {
            db.PatientRecallPlans.Add(new PatientRecallPlan { PatientId = optedOut, OptedOut = true, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"UPDATE [Patients] SET [IsActive] = 0 WHERE [Id] = {inactive}");
        }
        var now = visit.AddDays(190);
        await S.ScheduleAsync(S.Request(booked, new DateTime(2027, 9, 14, 9, 0, 0)));                                   // booked after 'now' (a future appointment)
        var result = await SchedulePassAsync(now);
        Assert.Equal(1, result.Enqueued);                                                                               // only Ann is left
        await RunJobsAsync(now);
        Assert.Equal(Ann, Assert.Single(await RecallTasksAsync()).PatientId);

        // Ann now has an open recall: another pass adds nothing; and a patient seen so long ago that they have lapsed is left to the front desk
        Assert.Equal(0, (await SchedulePassAsync(now)).Enqueued);
        await CompletedVisitAsync(lapsed, FirstVisitLocal.AddHours(4));
        Assert.Equal(0, (await SchedulePassAsync(visit.AddDays(365 * 3))).Enqueued);
        Assert.Equal(1, (await RecallTasksAsync()).Count);
        Assert.NotEqual(Guid.Empty, never);
    }

    [Fact]
    public async Task A_patients_own_interval_decides_when_they_come_due()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        await using (var db = Fixture.CreateContext())
        {
            db.PatientRecallPlans.Add(new PatientRecallPlan { PatientId = Ann, IntervalDays = 60, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor });
            await db.SaveChangesAsync();
        }
        var now = visit.AddDays(40);                                                                                    // 60 - 30: due now; the default 180 would not be
        Assert.Equal(1, (await SchedulePassAsync(now)).Enqueued);
        await RunJobsAsync(now);
        var task = Assert.Single(await RecallTasksAsync());
        Assert.Equal(RecallCalculator.NextDue(visit, 60, Alveara.Api.Tests.SchedulingTestSupport.Clock), task.DueAtUtc);
    }

    // ---------- the job looks again ----------

    [Fact]
    public async Task A_patient_who_books_after_the_job_was_enqueued_gets_no_task_and_the_job_still_succeeds()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(175);
        Assert.Equal(1, (await SchedulePassAsync(now)).Enqueued);
        await S.ScheduleAsync(S.Request(Ann, new DateTime(2027, 9, 14, 9, 0, 0)));                                      // the front desk books them first
        await RunJobsAsync(now);
        Assert.Empty(await RecallTasksAsync());
        Assert.Equal(1, await JobsAsync("Succeeded"));
    }

    [Fact]
    public async Task Switching_recall_off_after_the_job_was_enqueued_stops_the_task()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(175);
        await SchedulePassAsync(now);
        await EnableRecallAsync(false);
        await RunJobsAsync(now);
        Assert.Empty(await RecallTasksAsync());
    }

    [Fact]
    public async Task A_patient_seen_again_after_the_job_was_enqueued_gets_no_task_for_the_old_due_date()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(175);
        await SchedulePassAsync(now);
        await CompletedVisitAsync(Ann, new DateTime(2027, 8, 24, 9, 0, 0));                                             // a newer completed visit moves the due date
        await RunJobsAsync(now.AddDays(5));
        Assert.Empty(await RecallTasksAsync());
    }

    // ---------- failure and restart ----------

    [Fact]
    public async Task When_the_audit_entry_cannot_be_written_nothing_is_stored_and_the_retry_makes_the_one_task()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(175);
        await SchedulePassAsync(now);
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseRecallAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.FollowUpCreated}') THROW 59994, 'audit down', 1;");
        try
        {
            await RunJobsAsync(now);
            Assert.Empty(await RecallTasksAsync());                                                                      // no task, no event, no audit entry
            await using var check = Fixture.CreateContext();
            Assert.Equal(0, await check.FollowUpTaskEvents.CountAsync());
            var job = await check.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == RecallScheduler.JobType);
            Assert.Equal((BackgroundJobStatus.Pending, 1), (job.Status, job.AttemptCount));                              // ready to be tried again
            Assert.False(string.IsNullOrEmpty(job.LastError));
            Assert.DoesNotContain("Ann", job.LastError);
        }
        finally
        {
            await using var db = Fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestRefuseRecallAudit");
        }
        await RunJobsAsync(now);
        Assert.Single(await RecallTasksAsync());
        Assert.Equal(1, await JobsAsync("Succeeded"));
    }

    [Fact]
    public async Task A_job_interrupted_by_a_restart_is_recovered_without_making_a_second_task()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(175);
        await SchedulePassAsync(now);

        // the server died with the job claimed but not run
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 1, [StartedAtUtc] = DATEADD(MINUTE, -30, SYSDATETIMEOFFSET()), [AttemptCount] = 1 WHERE [JobType] = {RecallScheduler.JobType}");
        await using (var db = Fixture.CreateContext()) Assert.Equal(1, await new BackgroundJobRunner(db, []).RecoverStuckJobsAsync());
        Assert.Equal(1, await RunJobsAsync(now));
        Assert.Single(await RecallTasksAsync());

        // and the server died AFTER the effect committed but before the job was marked done: recovery finishes the bookkeeping and does not run it again
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 1, [StartedAtUtc] = DATEADD(MINUTE, -30, SYSDATETIMEOFFSET()), [CompletedAtUtc] = NULL WHERE [JobType] = {RecallScheduler.JobType}");
        await using (var db = Fixture.CreateContext()) await new BackgroundJobRunner(db, []).RecoverStuckJobsAsync();
        await RunJobsAsync(now);
        Assert.Single(await RecallTasksAsync());
        await using var check = Fixture.CreateContext();
        Assert.Equal(1, await check.FollowUpTaskEvents.CountAsync());
        Assert.Equal(BackgroundJobStatus.Succeeded, (await check.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == RecallScheduler.JobType)).Status);
    }

    [Fact]
    public async Task Two_schedulers_at_once_enqueue_one_job_for_the_patient()
    {
        await EnableRecallAsync();
        var visit = await CompletedVisitAsync(Ann);
        var now = visit.AddDays(175);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SchedulePassAsync(now)));
        Assert.Equal(1, await JobsAsync());
        await RunJobsAsync(now);
        Assert.Single(await RecallTasksAsync());
    }
}
