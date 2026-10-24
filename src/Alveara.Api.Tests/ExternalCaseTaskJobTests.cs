using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C02, the follow-up task that chases a referral or lab case, as durable background work (ALV-N002) against a real SQL Server: a case that has gone out and reached its due date gets ONE unassigned system task,
/// with its history and audit entry, however many passes and runs there are; a case not yet due, a draft, a finished case and a case without a due date get none; an overdue case's task is High priority; the task is
/// closed by the system, with the right words, when the case is completed or cancelled, sent back to a draft or given another due date (which makes a new task when that date has come); a case that changed after the
/// job was enqueued gets no task and the job still succeeds; a task a person already closed is left alone; a failure stores nothing and the retry makes the one task; a job interrupted by a restart is recovered.
/// </summary>
public class ExternalCaseTaskJobTests : TreatmentPlanTestBase
{
    private static DateOnly Today => DateOnly.FromDateTime(PatientTestSupport.Clock.ToPracticeLocal(DateTimeOffset.UtcNow).Date);

    private Task<T> Cases<T>(Func<ExternalCaseService, Task<T>> action) => WithDb(db => action(new ExternalCaseService(db, PatientTestSupport.Clock)));

    private async Task<ExternalPartyView> PartyAsync(string kind) =>
        await WithDb(db => new ExternalPartyService(db, PatientTestSupport.Clock).CreateAsync(S.Actor, new CreatePartyRequest(kind, $"Party {Guid.NewGuid():N}", null), default));

    /// <summary>A referral that has gone out and is due on the given day.</summary>
    private async Task<ExternalCaseView> SentReferralAsync(DateOnly? due, Guid? patient = null, string name = "Dr. Rivera Endodontics")
    {
        var party = await WithDb(db => new ExternalPartyService(db, PatientTestSupport.Clock).CreateAsync(S.Actor, new CreatePartyRequest(ExternalPartyKinds.ReferralProvider, $"{name} {Guid.NewGuid():N}"[..40], null), default));
        var c = (await Cases(s => s.CreateReferralAsync(S.Actor, patient ?? Ann, new CreateReferralRequest(party.Id, "Retreatment of tooth 19", due, null, null), Guid.NewGuid().ToString("N"), default))).Case;
        return (await Cases(s => s.ChangeStatusAsync(S.Actor, ExternalCaseKinds.Referral, c.Id, new ChangeStatusRequest("Sent", null, Today.AddDays(-5), c.RowVersion), default))).Case;
    }

    private async Task<ExternalCaseView> SentLabAsync(DateOnly? due)
    {
        var party = await PartyAsync(ExternalPartyKinds.LabVendor);
        var c = (await Cases(s => s.CreateLabAsync(S.Actor, Ann, new CreateLabRequest(party.Id, "Crown, zirconia", due, "36", null, null, null, null), Guid.NewGuid().ToString("N"), default))).Case;
        return (await Cases(s => s.ChangeStatusAsync(S.Actor, ExternalCaseKinds.Lab, c.Id, new ChangeStatusRequest("Sent", null, Today.AddDays(-5), c.RowVersion), default))).Case;
    }

    private Task<ExternalCaseView> MoveAsync(ExternalCaseView c, string to, string? note = null) =>
        Cases(async s => (await s.ChangeStatusAsync(S.Actor, c.Kind, c.Id, new ChangeStatusRequest(to, note, to == "Received" ? Today : null, c.RowVersion), default)).Case);

    private async Task<ExternalCaseTaskPassResult> PassAsync()
    {
        await using var db = Fixture.CreateContext();
        return await new ExternalCaseTaskScheduler(db, new BackgroundJobQueue(db), PatientTestSupport.Clock).EnqueueAsync(default);
    }

    private async Task<int> RunAsync()
    {
        await using var db = Fixture.CreateContext();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        return await new BackgroundJobRunner(db, [new ExternalCaseTaskJobHandler(clock), new ExternalCaseTaskCloseJobHandler(clock)]).ProcessOnceAsync();
    }

    private async Task<List<FollowUpTask>> TasksAsync() { await using var db = Fixture.CreateContext(); return await db.FollowUpTasks.AsNoTracking().Where(t => t.Kind == FollowUpKinds.ReferralFollowUp || t.Kind == FollowUpKinds.LabCaseDue).ToListAsync(); }
    private async Task<int> JobsAsync(string type) { await using var db = Fixture.CreateContext(); return await db.BackgroundJobs.CountAsync(j => j.JobType == type); }

    // ---------- making the task ----------

    [Fact]
    public async Task A_sent_referral_that_reached_its_due_date_gets_one_unassigned_task_with_its_history_and_audit_entry()
    {
        var c = await SentReferralAsync(Today);
        var pass = await PassAsync();
        Assert.Equal((1, 1, 0), (pass.Wanting, pass.Enqueued, pass.Closing));
        await RunAsync();

        var task = Assert.Single(await TasksAsync());
        Assert.Equal((Ann, FollowUpKinds.ReferralFollowUp, FollowUpStatuses.Open, FollowUpPriorities.Normal, null, null, c.Id), (task.PatientId, task.Kind, task.Status, task.Priority, task.AssigneeUserId, task.CreatedByUserId, task.SourceReferralCaseId));
        Assert.Null(task.SourceLabCaseId);
        Assert.Equal(FollowUpRules.ReferralSourceKey(c.Id, Today), task.SourceKey);
        Assert.Equal(ExternalCaseRules.TaskDueAt(Today, PatientTestSupport.Clock), task.DueAtUtc);
        Assert.StartsWith("Referral to follow up: Dr. Rivera Endodontics", task.Title);
        Assert.DoesNotContain("Retreatment", task.Title);                                                                                            // the reason never reaches the task
        await using var db = Fixture.CreateContext();
        var events = await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == task.Id).ToListAsync();
        Assert.Equal((FollowUpEventKinds.SystemCreated, (Guid?)null), (events.Single().Kind, events.Single().ActorUserId));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.FollowUpCreated && a.TargetUserAccountId == task.Id);
        Assert.Null(audit.PerformedByUserAccountId);
        Assert.Equal(1, await JobsAsync(ExternalCaseTaskScheduler.CreateJobType));
    }

    [Fact]
    public async Task A_lab_case_that_reached_its_due_date_gets_a_lab_task_and_an_overdue_case_gets_a_high_priority_one()
    {
        var late = await SentLabAsync(Today.AddDays(-4));
        await PassAsync(); await RunAsync();
        var task = Assert.Single(await TasksAsync());
        Assert.Equal((FollowUpKinds.LabCaseDue, FollowUpPriorities.High, late.Id, (Guid?)null), (task.Kind, task.Priority, task.SourceLabCaseId, task.SourceReferralCaseId));
        Assert.StartsWith("Lab case due: Party ", task.Title);
        Assert.Equal(ExternalCaseRules.TaskDueAt(Today.AddDays(-4), PatientTestSupport.Clock), task.DueAtUtc);
    }

    [Fact]
    public async Task A_case_not_yet_due_a_draft_a_finished_case_and_a_case_without_a_due_date_get_no_task()
    {
        await SentReferralAsync(Today.AddDays(3));                                                                                                  // not yet due
        await SentReferralAsync(null);                                                                                                              // no due date
        var party = await PartyAsync(ExternalPartyKinds.ReferralProvider);
        await Cases(s => s.CreateReferralAsync(S.Actor, Ann, new CreateReferralRequest(party.Id, "A draft", Today.AddDays(-10), null, null), "draft-key", default));   // a draft, though its date has passed
        var done = await MoveAsync(await SentReferralAsync(Today.AddDays(-2)), "Cancelled", "Not needed");                                          // cancelled
        var completed = await MoveAsync(await MoveAsync(await SentReferralAsync(Today.AddDays(-2)), "Received"), "Completed");                       // completed
        var pass = await PassAsync();
        Assert.Equal((0, 0, 0), (pass.Wanting, pass.Enqueued, pass.Closing));
        Assert.Equal(0, await JobsAsync(ExternalCaseTaskScheduler.CreateJobType));
        Assert.Equal(("Cancelled", "Completed"), (done.Status, completed.Status));
    }

    [Fact]
    public async Task However_many_passes_and_runs_there_are_one_job_and_one_task_per_case_and_due_date()
    {
        await SentReferralAsync(Today); await SentLabAsync(Today.AddDays(-1));
        for (var i = 0; i < 3; i++)
        {
            await PassAsync();
            await RunAsync();
        }
        Assert.Equal(2, (await TasksAsync()).Count);
        Assert.Equal(2, await JobsAsync(ExternalCaseTaskScheduler.CreateJobType));
        var again = await PassAsync();
        Assert.Equal(0, again.Enqueued);                                                                                                            // a case that has its task enqueues nothing
    }

    [Fact]
    public async Task Two_schedulers_at_once_enqueue_one_job_for_the_case()
    {
        await SentReferralAsync(Today);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => { try { await PassAsync(); } catch (DbUpdateException) { /* the unique job key lost the race: that is the point */ } }));
        Assert.Equal(1, await JobsAsync(ExternalCaseTaskScheduler.CreateJobType));
        await RunAsync();
        Assert.Single(await TasksAsync());
    }

    [Fact]
    public async Task A_case_that_came_back_or_was_cancelled_after_the_job_was_enqueued_gets_no_task_and_the_job_still_succeeds()
    {
        var c = await SentReferralAsync(Today);
        await PassAsync();
        await MoveAsync(c, "Cancelled", "Patient went elsewhere");
        Assert.Equal(1, await RunAsync());
        Assert.Empty(await TasksAsync());
        await using var db = Fixture.CreateContext();
        Assert.Equal(BackgroundJobStatus.Succeeded, (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == ExternalCaseTaskScheduler.CreateJobType)).Status);
    }

    [Fact]
    public async Task A_second_job_for_a_task_that_already_exists_succeeds_and_makes_nothing_more()
    {
        var c = await SentReferralAsync(Today);
        await PassAsync(); await RunAsync();
        string payload;
        await using (var db = Fixture.CreateContext())
        {
            payload = (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == ExternalCaseTaskScheduler.CreateJobType)).PayloadJson!;
            await new BackgroundJobQueue(db).EnqueueAsync("replay-of-the-case-task-job", ExternalCaseTaskScheduler.CreateJobType, payload, default);
        }
        await RunAsync();
        Assert.Single(await TasksAsync());
        await using var check = Fixture.CreateContext();
        Assert.Equal(new[] { BackgroundJobStatus.Succeeded, BackgroundJobStatus.Succeeded }, await check.BackgroundJobs.AsNoTracking().Where(j => j.JobType == ExternalCaseTaskScheduler.CreateJobType).Select(j => j.Status).ToArrayAsync());
        Assert.Equal(c.Id, (await TasksAsync()).Single().SourceReferralCaseId);
    }

    // ---------- closing the task ----------

    [Theory]
    [InlineData("Completed", FollowUpStatuses.Completed, "The referral was completed.")]
    [InlineData("Cancelled", FollowUpStatuses.Cancelled, "The referral was cancelled.")]
    public async Task When_the_case_is_finished_the_open_task_is_closed_by_the_system_with_the_right_words(string finish, string expectedStatus, string note)
    {
        var c = await SentReferralAsync(Today);
        await PassAsync(); await RunAsync();
        var task = Assert.Single(await TasksAsync());
        if (finish == "Completed") await MoveAsync(await MoveAsync(c, "Received"), "Completed"); else await MoveAsync(c, "Cancelled", "No longer needed");

        var pass = await PassAsync();
        Assert.Equal(1, pass.Closing);
        await RunAsync();
        await using var db = Fixture.CreateContext();
        var closed = await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal((expectedStatus, true, (Guid?)null, note), (closed.Status, closed.ClosedBySystem, closed.ClosedByUserId, closed.ClosureNote));
        var events = await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == task.Id).OrderBy(e => e.OccurredAtUtc).Select(e => e.Kind).ToListAsync();
        Assert.Equal(new[] { FollowUpEventKinds.SystemCreated, FollowUpEventKinds.SystemClosed }, events);
        await PassAsync(); await RunAsync();                                                                                                        // nothing is closed twice
        Assert.Equal(2, await db.FollowUpTaskEvents.CountAsync(e => e.TaskId == task.Id));
    }

    [Fact]
    public async Task A_lab_remake_back_to_sent_with_a_later_due_date_closes_the_old_task_and_a_new_due_date_that_has_come_makes_a_new_one()
    {
        var lab = await SentLabAsync(Today.AddDays(-3));
        await PassAsync(); await RunAsync();
        var first = Assert.Single(await TasksAsync());
        var received = await MoveAsync(lab, "Received");
        var remake = await MoveAsync(received, "Sent", "Margin open on the mesial");
        var moved = (await Cases(s => s.UpdateLabAsync(S.Actor, lab.Id, new UpdateLabRequest(lab.PartyId, lab.Summary, Today.AddDays(10), lab.ToothKey, null, null, null, null, remake.RowVersion), default))).Case;
        var pass = await PassAsync();
        Assert.Equal((0, 0, 1), (pass.Wanting, pass.Enqueued, pass.Closing));                                                                       // the case no longer wants the old task
        await RunAsync();
        await using (var db = Fixture.CreateContext())
        {
            var closed = await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == first.Id);
            Assert.Equal((FollowUpStatuses.Cancelled, "The lab case has a new due date."), (closed.Status, closed.ClosureNote));
        }
        // the new date arrives: moved earlier again, it wants a new task (its key names the new date)
        await Cases(s => s.UpdateLabAsync(S.Actor, lab.Id, new UpdateLabRequest(lab.PartyId, lab.Summary, Today, lab.ToothKey, null, null, null, null, moved.RowVersion), default));
        await PassAsync(); await RunAsync();
        var tasks = await TasksAsync();
        Assert.Equal(2, tasks.Count);
        Assert.Equal(new[] { FollowUpRules.LabSourceKey(lab.Id, Today), FollowUpRules.LabSourceKey(lab.Id, Today.AddDays(-3)) }.OrderBy(x => x), tasks.Select(t => t.SourceKey!).OrderBy(x => x));
        Assert.Single(tasks, t => t.Status == FollowUpStatuses.Open);
    }

    [Fact]
    public async Task A_task_a_person_already_closed_is_left_alone_when_the_case_is_later_finished()
    {
        var c = await SentReferralAsync(Today);
        await PassAsync(); await RunAsync();
        var task = Assert.Single(await TasksAsync());
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Status] = N'Completed', [ClosedAtUtc] = DATEADD(YEAR, 5, SYSDATETIMEOFFSET()), [ClosedByUserId] = {S.Actor}, [ClosureNote] = N'Called them' WHERE [Id] = {task.Id}");
        await MoveAsync(c, "Cancelled", "Not needed");
        Assert.Equal(0, (await PassAsync()).Closing);                                                                                               // a closed task is not even looked at
        await RunAsync();
        await using var check = Fixture.CreateContext();
        var after = await check.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal((false, S.Actor, "Called them"), (after.ClosedBySystem, after.ClosedByUserId, after.ClosureNote));
    }

    [Fact]
    public async Task A_close_job_for_a_task_its_case_still_wants_leaves_it_open()
    {
        await SentReferralAsync(Today);
        await PassAsync(); await RunAsync();
        var task = Assert.Single(await TasksAsync());
        await using (var db = Fixture.CreateContext())
            await new BackgroundJobQueue(db).EnqueueAsync("stale-case-close-job", ExternalCaseTaskScheduler.CloseJobType, JsonSerializer.Serialize(new ExternalCaseTaskCloseJobPayload(task.Id)), default);
        await RunAsync();
        var after = Assert.Single(await TasksAsync());
        Assert.Equal((FollowUpStatuses.Open, false), (after.Status, after.ClosedBySystem));
    }

    // ---------- failure and restart ----------

    [Fact]
    public async Task When_the_audit_entry_cannot_be_written_nothing_is_stored_and_the_retry_makes_the_one_task()
    {
        await SentLabAsync(Today);
        await PassAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseCaseTaskAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.FollowUpCreated}') THROW 59992, 'audit down', 1;");
        try
        {
            await RunAsync();
            Assert.Empty(await TasksAsync());
            await using var check = Fixture.CreateContext();
            Assert.Equal(0, await check.FollowUpTaskEvents.CountAsync());
            Assert.Equal(BackgroundJobStatus.Pending, (await check.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == ExternalCaseTaskScheduler.CreateJobType)).Status);
        }
        finally
        {
            await using var db = Fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestRefuseCaseTaskAudit");
        }
        await RunAsync();
        Assert.Single(await TasksAsync());
    }

    [Fact]
    public async Task A_job_interrupted_by_a_restart_is_recovered_without_making_a_second_task()
    {
        await SentReferralAsync(Today);
        await PassAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 1, [StartedAtUtc] = DATEADD(MINUTE, -30, SYSDATETIMEOFFSET()), [AttemptCount] = 1 WHERE [JobType] = {ExternalCaseTaskScheduler.CreateJobType}");
        await using (var db = Fixture.CreateContext()) Assert.Equal(1, await new BackgroundJobRunner(db, []).RecoverStuckJobsAsync());
        Assert.Equal(1, await RunAsync());
        Assert.Single(await TasksAsync());
        // the server died AFTER the effect committed but before the job was marked done: recovery finishes the bookkeeping and does not run it again
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 1, [StartedAtUtc] = DATEADD(MINUTE, -30, SYSDATETIMEOFFSET()), [CompletedAtUtc] = NULL WHERE [JobType] = {ExternalCaseTaskScheduler.CreateJobType}");
        await using (var db = Fixture.CreateContext()) await new BackgroundJobRunner(db, []).RecoverStuckJobsAsync();
        await RunAsync();
        Assert.Single(await TasksAsync());
        await using var check = Fixture.CreateContext();
        Assert.Equal(1, await check.FollowUpTaskEvents.CountAsync(e => e.Kind == FollowUpEventKinds.SystemCreated));
    }
}
