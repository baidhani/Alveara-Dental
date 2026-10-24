using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Recall;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C01, accepted but unscheduled treatment on the work queue, as durable background work (ALV-N002) against a real SQL Server: an item whose latest state is Accepted, Unscheduled and NotStarted gets exactly one task for
/// that state however many passes and runs there are; an item that is pending, declined, scheduled, completed or withdrawn (or whose plan is withdrawn) gets none; the job looks again when it runs; when the treatment is later scheduled,
/// completed, declined or withdrawn the open task is closed by the system with the right words and the history and audit entries say so; a new acceptance after un-scheduling is a new task; a task a person already closed is left alone;
/// and a failure stores nothing while the retry makes the one task.
/// </summary>
public class UnscheduledTreatmentJobTests : TreatmentPlanTestBase
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task<(TreatmentPlan Plan, TreatmentPlanItem Item)> PlanItemAsync(Guid patient, string planStatus = TreatmentPlanStatuses.Proposed, bool withdrawnItem = false)
    {
        var plan = new TreatmentPlan { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = $"plan-{Guid.NewGuid():N}", Title = "Plan", Status = planStatus, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        if (planStatus == TreatmentPlanStatuses.Withdrawn) (plan.WithdrawnAtUtc, plan.WithdrawnByUserId, plan.WithdrawnReason) = (DateTimeOffset.UtcNow, S.Actor, "Withdrawn");
        var procedure = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var item = new TreatmentPlanItem
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = patient, ItemNumber = 1, IdempotencyKey = "item-1", DiagnosisId = await DiagnosisAsync(patient), ProcedureId = procedure.Summary.Id,
            ProcedureVersionId = procedure.Versions[0].VersionId, Fee = procedure.Versions[0].Fee, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor,
        };
        if (withdrawnItem) (item.WithdrawnAtUtc, item.WithdrawnByUserId, item.WithdrawnReason) = (DateTimeOffset.UtcNow, S.Actor, "Declined");
        await AddAsync(plan, item);
        return (plan, item);
    }

    private async Task StateAsync(TreatmentPlanItem item, int sequence, string acceptance = ItemAcceptance.Accepted, string scheduling = ItemScheduling.Unscheduled, string completion = ItemCompletion.NotStarted, DateTimeOffset? at = null)
    {
        await AddAsync(new TreatmentPlanItemState
        {
            Id = Guid.NewGuid(), PlanId = item.PlanId, PatientId = item.PatientId, ItemId = item.Id, Sequence = sequence, Acceptance = acceptance, Scheduling = scheduling, Completion = completion,
            PlannedDate = scheduling == ItemScheduling.Scheduled ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) : null, Reason = acceptance == ItemAcceptance.Declined ? "Too costly" : null,
            RevisionNumber = 1, RecordedByUserId = S.Actor, RecordedAtUtc = at ?? Now.AddDays(-1),
        });
    }

    private async Task<UnscheduledPassResult> PassAsync()
    {
        await using var db = Fixture.CreateContext();
        return await new UnscheduledTreatmentScheduler(db, new BackgroundJobQueue(db)).EnqueueAsync(default);
    }

    private async Task RunAsync()
    {
        await using var db = Fixture.CreateContext();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await new BackgroundJobRunner(db, [new RecallTaskJobHandler(clock), new UnscheduledTreatmentJobHandler(clock), new UnscheduledTreatmentCloseJobHandler(clock)]).ProcessOnceAsync();
    }

    private async Task<List<FollowUpTask>> TasksAsync() { await using var db = Fixture.CreateContext(); return await db.FollowUpTasks.AsNoTracking().Where(t => t.Kind == FollowUpKinds.UnscheduledTreatment).OrderBy(t => t.CreatedAtUtc).ToListAsync(); }

    // ---------- making the task ----------

    [Fact]
    public async Task Accepted_treatment_nobody_has_scheduled_gets_one_unassigned_task_with_its_history_and_audit_entry()
    {
        var (_, item) = await PlanItemAsync(Ann);
        var acceptedAt = Now.AddDays(-2);
        await StateAsync(item, 1, at: acceptedAt);
        var pass = await PassAsync();
        Assert.Equal((1, 1, 0), (pass.Waiting, pass.Enqueued, pass.Closing));
        await RunAsync();

        var task = Assert.Single(await TasksAsync());
        Assert.Equal((Ann, FollowUpKinds.UnscheduledTreatment, FollowUpStatuses.Open, FollowUpPriorities.Normal, null, null, item.Id), (task.PatientId, task.Kind, task.Status, task.Priority, task.AssigneeUserId, task.CreatedByUserId, task.SourcePlanItemId));
        Assert.Equal(FollowUpRules.UnscheduledSourceKey(item.Id, 1), task.SourceKey);
        Assert.Equal(acceptedAt.AddDays(UnscheduledTreatmentRules.DueDaysAfterAcceptance), task.DueAtUtc);
        await using var db = Fixture.CreateContext();
        var ev = await db.FollowUpTaskEvents.AsNoTracking().SingleAsync(e => e.TaskId == task.Id);
        Assert.Equal((FollowUpEventKinds.SystemCreated, null), (ev.Kind, ev.ActorUserId));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.FollowUpCreated);
        Assert.Null(audit.PerformedByUserAccountId);
        Assert.Equal(BackgroundJobStatus.Succeeded, (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == UnscheduledTreatmentScheduler.CreateJobType)).Status);
    }

    [Fact]
    public async Task However_many_passes_and_runs_there_are_one_job_and_one_task_per_accepted_state()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        for (var i = 0; i < 3; i++) { await PassAsync(); await RunAsync(); }
        Assert.Single(await TasksAsync());
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.BackgroundJobs.CountAsync(j => j.JobType == UnscheduledTreatmentScheduler.CreateJobType));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Declined")]
    [InlineData("Scheduled")]
    [InlineData("Completed")]
    [InlineData("WithdrawnItem")]
    [InlineData("WithdrawnPlan")]
    [InlineData("NoState")]
    public async Task Treatment_that_is_not_waiting_gets_no_task(string situation)
    {
        var (plan, item) = await PlanItemAsync(Ann);
        switch (situation)
        {
            case "Pending": await StateAsync(item, 1, acceptance: ItemAcceptance.Pending); break;
            case "Declined": await StateAsync(item, 1, acceptance: ItemAcceptance.Declined); break;
            case "Scheduled": await StateAsync(item, 1, scheduling: ItemScheduling.Scheduled); break;
            case "Completed": await StateAsync(item, 1, scheduling: ItemScheduling.Scheduled, completion: ItemCompletion.Completed); break;
            case "NoState": break;
            default: await StateAsync(item, 1); break;
        }
        // the database refuses to put a state on a withdrawn item or plan, so these are withdrawn after they were accepted
        await using (var db = Fixture.CreateContext())
        {
            if (situation == "WithdrawnItem") await db.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlanItems] SET [WithdrawnAtUtc] = SYSDATETIMEOFFSET(), [WithdrawnByUserId] = {S.Actor}, [WithdrawnReason] = N'Not needed' WHERE [Id] = {item.Id}");
            if (situation == "WithdrawnPlan") await db.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlans] SET [Status] = N'Withdrawn', [WithdrawnAtUtc] = SYSDATETIMEOFFSET(), [WithdrawnByUserId] = {S.Actor}, [WithdrawnReason] = N'Replaced' WHERE [Id] = {plan.Id}");
        }
        var pass = await PassAsync();
        Assert.Equal(0, pass.Enqueued);
        await RunAsync();
        Assert.Empty(await TasksAsync());
    }

    [Fact]
    public async Task Only_the_latest_state_counts_so_an_item_scheduled_after_acceptance_is_not_waiting()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await StateAsync(item, 2, scheduling: ItemScheduling.Scheduled);
        Assert.Equal(0, (await PassAsync()).Enqueued);
    }

    // ---------- the job looks again ----------

    [Fact]
    public async Task Treatment_scheduled_after_the_job_was_enqueued_gets_no_task_and_the_job_still_succeeds()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        Assert.Equal(1, (await PassAsync()).Enqueued);
        await StateAsync(item, 2, scheduling: ItemScheduling.Scheduled);                                              // the front desk books it first
        await RunAsync();
        Assert.Empty(await TasksAsync());
        await using var db = Fixture.CreateContext();
        Assert.Equal(BackgroundJobStatus.Succeeded, (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == UnscheduledTreatmentScheduler.CreateJobType)).Status);
    }

    // ---------- closing it when it is settled ----------

    [Theory]
    [InlineData("scheduled", true, "The treatment has been scheduled.")]
    [InlineData("completed", true, "The treatment has been completed.")]
    [InlineData("declined", false, "The patient declined the treatment.")]
    public async Task When_the_treatment_is_settled_the_open_task_is_closed_by_the_system_with_the_right_words(string how, bool completed, string note)
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await PassAsync(); await RunAsync();
        var task = Assert.Single(await TasksAsync());

        switch (how)
        {
            case "scheduled": await StateAsync(item, 2, scheduling: ItemScheduling.Scheduled); break;
            case "completed": await StateAsync(item, 2, scheduling: ItemScheduling.Scheduled, completion: ItemCompletion.Completed); break;
            default: await StateAsync(item, 2, acceptance: ItemAcceptance.Declined); break;
        }
        var pass = await PassAsync();
        Assert.Equal(1, pass.Closing);
        await RunAsync();

        await using var db = Fixture.CreateContext();
        var closed = await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal((completed ? FollowUpStatuses.Completed : FollowUpStatuses.Cancelled, true, null, note), (closed.Status, closed.ClosedBySystem, closed.ClosedByUserId, closed.ClosureNote));
        var events = await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == task.Id).OrderBy(e => e.OccurredAtUtc).Select(e => e.Kind).ToListAsync();
        Assert.Equal(new[] { FollowUpEventKinds.SystemCreated, FollowUpEventKinds.SystemClosed }, events);
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == (completed ? AuditEventTypes.FollowUpCompleted : AuditEventTypes.FollowUpCancelled));
        Assert.Null(audit.PerformedByUserAccountId);
        // and nothing is closed twice
        await PassAsync(); await RunAsync();
        Assert.Equal(2, await db.FollowUpTaskEvents.CountAsync(e => e.TaskId == task.Id));
    }

    [Fact]
    public async Task A_second_job_for_a_task_that_already_exists_succeeds_and_makes_nothing_more()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await PassAsync(); await RunAsync();
        await using (var db = Fixture.CreateContext())
        {
            var payload = (await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == UnscheduledTreatmentScheduler.CreateJobType)).PayloadJson!;
            await new BackgroundJobQueue(db).EnqueueAsync("replay-of-the-create-job", UnscheduledTreatmentScheduler.CreateJobType, payload, default);   // the same task asked for under another job key
        }
        await RunAsync();
        Assert.Single(await TasksAsync());
        await using var check = Fixture.CreateContext();
        Assert.Equal(new[] { BackgroundJobStatus.Succeeded, BackgroundJobStatus.Succeeded }, await check.BackgroundJobs.AsNoTracking().Where(j => j.JobType == UnscheduledTreatmentScheduler.CreateJobType).Select(j => j.Status).ToArrayAsync());
    }

    [Fact]
    public async Task A_close_job_for_a_task_whose_treatment_still_waits_leaves_it_open()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await PassAsync(); await RunAsync();
        var task = Assert.Single(await TasksAsync());
        await using (var db = Fixture.CreateContext())
            await new BackgroundJobQueue(db).EnqueueAsync("stale-close-job", UnscheduledTreatmentScheduler.CloseJobType, System.Text.Json.JsonSerializer.Serialize(new UnscheduledCloseJobPayload(task.Id)), default);
        await RunAsync();                                                                                                // the treatment was never settled: a late or mistaken close job must not close the task
        var after = Assert.Single(await TasksAsync());
        Assert.Equal((FollowUpStatuses.Open, false), (after.Status, after.ClosedBySystem));
    }

    [Fact]
    public async Task A_task_a_person_already_closed_is_left_alone_when_the_treatment_is_later_scheduled()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await PassAsync(); await RunAsync();
        var task = Assert.Single(await TasksAsync());
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Status] = N'Cancelled', [ClosedAtUtc] = SYSDATETIMEOFFSET(), [ClosedByUserId] = {S.Actor}, [ClosureNote] = N'Handled by phone' WHERE [Id] = {task.Id}");
        await StateAsync(item, 2, scheduling: ItemScheduling.Scheduled);
        Assert.Equal(0, (await PassAsync()).Closing);                                                                // a closed task is not even looked at
        await RunAsync();
        await using var check = Fixture.CreateContext();
        var after = await check.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal((false, S.Actor, "Handled by phone"), (after.ClosedBySystem, after.ClosedByUserId, after.ClosureNote));
    }

    [Fact]
    public async Task Accepting_again_after_un_scheduling_is_a_new_state_and_a_new_task_and_the_old_one_stays_closed()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await PassAsync(); await RunAsync();
        await StateAsync(item, 2, scheduling: ItemScheduling.Scheduled);
        await PassAsync(); await RunAsync();
        await StateAsync(item, 3);                                                                                    // the appointment fell through: unscheduled again
        await PassAsync(); await RunAsync();
        var tasks = await TasksAsync();
        Assert.Equal(2, tasks.Count);
        Assert.Equal(new[] { FollowUpRules.UnscheduledSourceKey(item.Id, 1), FollowUpRules.UnscheduledSourceKey(item.Id, 3) }, tasks.Select(t => t.SourceKey).ToArray());
        Assert.Equal(new[] { FollowUpStatuses.Completed, FollowUpStatuses.Open }, tasks.Select(t => t.Status).ToArray());
    }

    [Fact]
    public async Task A_withdrawn_plan_closes_its_open_task_as_cancelled()
    {
        var (plan, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await PassAsync(); await RunAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlans] SET [Status] = N'Withdrawn', [WithdrawnAtUtc] = SYSDATETIMEOFFSET(), [WithdrawnByUserId] = {S.Actor}, [WithdrawnReason] = N'Replaced' WHERE [Id] = {plan.Id}");
        Assert.Equal(1, (await PassAsync()).Closing);
        await RunAsync();
        var closed = Assert.Single(await TasksAsync());
        Assert.Equal((FollowUpStatuses.Cancelled, "The treatment is no longer proposed."), (closed.Status, closed.ClosureNote));
    }

    // ---------- failure ----------

    [Fact]
    public async Task When_the_audit_entry_cannot_be_written_nothing_is_stored_and_the_retry_makes_the_one_task()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await PassAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseTreatmentAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.FollowUpCreated}') THROW 59993, 'audit down', 1;");
        try
        {
            await RunAsync();
            Assert.Empty(await TasksAsync());
            await using var check = Fixture.CreateContext();
            Assert.Equal(0, await check.FollowUpTaskEvents.CountAsync());
            Assert.Equal(BackgroundJobStatus.Pending, (await check.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == UnscheduledTreatmentScheduler.CreateJobType)).Status);
        }
        finally
        {
            await using var db = Fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestRefuseTreatmentAudit");
        }
        await RunAsync();
        Assert.Single(await TasksAsync());
    }

    [Fact]
    public async Task Two_schedulers_at_once_enqueue_one_job_for_the_item()
    {
        var (_, item) = await PlanItemAsync(Ann);
        await StateAsync(item, 1);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PassAsync()));
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.BackgroundJobs.CountAsync(j => j.JobType == UnscheduledTreatmentScheduler.CreateJobType));
        await RunAsync();
        Assert.Single(await TasksAsync());
    }
}
