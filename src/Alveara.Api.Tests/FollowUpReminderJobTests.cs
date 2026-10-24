using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C01, the follow-up reminders on the durable background-job queue (ALV-N002) against a real SQL Server: the scan only enqueues one job per task, due time and channel; the handler delivers inside the runner's
/// transaction, so a restart (a job left claimed, or one whose effect had committed) never loses a reminder or sends it twice; a retry or a second delivery of the same job changes nothing; a reminder job that fails all its
/// attempts is given fresh ones after a cool-down for as long as the reminder is owed; a job for a task that was completed or re-dated does nothing; an unassigned task is the office managers'; nobody to tell makes no job;
/// the inline pass runs only reminder jobs; and a backlog larger than a page is worked through without the first page starving the rest.
/// </summary>
public class FollowUpReminderJobTests : SafetyTestBase
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<Guid> UserAsync(Role role, bool disabled = false)
    {
        var id = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = id, Username = $"u-{Guid.NewGuid():N}", Role = role, IsDisabled = disabled, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<FollowUpTask> TaskAsync(Guid? assignee, DateTimeOffset due, string status = FollowUpStatuses.Open)
    {
        var t = new FollowUpTask { Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = assignee, Title = "Secret title", DueAtUtc = due, Status = status, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-30) };
        await using var db = Fixture.CreateContext();
        db.FollowUpTasks.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    private FollowUpReminderService Service(AlveraDbContext db) => new(db, new TestClock(Now), PolicyTestSupport.Defaults(), [new InAppReminderChannel(db, new TestClock(Now))]);
    private FollowUpReminderJobHandler Handler(AlveraDbContext db) => new(new TestClock(Now), PolicyTestSupport.Defaults(), [new InAppReminderChannel(db, new TestClock(Now))]);

    private async Task<ReminderEnqueueResult> EnqueueAsync() { await using var db = Fixture.CreateContext(); return await Service(db).EnqueueDueAsync(default); }

    private async Task<int> RunAsync()
    {
        await using var db = Fixture.CreateContext();
        return await new BackgroundJobRunner(db, [Handler(db)]).ProcessOnceAsync([FollowUpReminderService.JobType]);
    }

    private async Task<(int Jobs, int Notifications, int Deliveries, int Events, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.BackgroundJobs.CountAsync(j => j.JobType == FollowUpReminderService.JobType), await db.Notifications.CountAsync(n => n.Kind == NotificationKinds.FollowUpDue), await db.FollowUpReminderDeliveries.CountAsync(),
                await db.FollowUpTaskEvents.CountAsync(e => e.Kind == FollowUpEventKinds.Reminded), await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpReminderSent));
    }

    private async Task<BackgroundJob> JobAsync() { await using var db = Fixture.CreateContext(); return await db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == FollowUpReminderService.JobType); }

    // ---------- the scan only enqueues ----------

    [Fact]
    public async Task The_scan_makes_one_job_per_task_due_time_and_channel_and_delivers_nothing_itself()
    {
        var hyg = await UserAsync(Role.Hygienist);
        var task = await TaskAsync(hyg, Now.AddMinutes(-5));
        Assert.Equal(new ReminderEnqueueResult(1, 0, 0), await EnqueueAsync());
        Assert.Equal((1, 0, 0, 0, 0), await CountsAsync());                                             // a job, and nothing told yet
        for (var i = 0; i < 3; i++) Assert.Equal(0, (await EnqueueAsync()).Enqueued);                  // a repeat, a restart or a second server enqueues nothing new
        var job = await JobAsync();
        Assert.Equal(FollowUpReminderService.JobKey(task.Id, task.DueAtUtc, FollowUpChannels.InApp), job.IdempotencyKey);
        Assert.Equal(BackgroundJobStatus.Pending, job.Status);
        Assert.StartsWith("followup-reminder:", job.IdempotencyKey);
        Assert.True(job.IdempotencyKey.Length < 200);
    }

    [Fact]
    public async Task The_job_runner_then_delivers_it_once_inside_its_transaction()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await EnqueueAsync();
        Assert.Equal(1, await RunAsync());
        Assert.Equal((1, 1, 1, 1, 1), await CountsAsync());
        var job = await JobAsync();
        Assert.Equal(BackgroundJobStatus.Succeeded, job.Status);
        await using var db = Fixture.CreateContext();
        Assert.True(await db.BackgroundJobEffectReceipts.AnyAsync(r => r.JobId == job.Id));
        Assert.Equal(0, await RunAsync());                                                               // nothing pending any more
    }

    // ---------- restart and retry ----------

    [Fact]
    public async Task A_job_left_claimed_by_a_dead_server_is_recovered_and_the_reminder_is_sent_once()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await EnqueueAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 1, [StartedAtUtc] = DATEADD(MINUTE, -30, SYSDATETIMEOFFSET()), [AttemptCount] = 1 WHERE [JobType] = {FollowUpReminderService.JobType}");
        await using (var db = Fixture.CreateContext()) Assert.Equal(1, await new BackgroundJobRunner(db, []).RecoverStuckJobsAsync());
        await RunAsync();
        Assert.Equal((1, 1, 1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_job_whose_effect_had_committed_before_the_server_died_is_finished_without_a_second_reminder()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await EnqueueAsync();
        await RunAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 1, [StartedAtUtc] = DATEADD(MINUTE, -30, SYSDATETIMEOFFSET()), [CompletedAtUtc] = NULL WHERE [JobType] = {FollowUpReminderService.JobType}");
        await using (var db = Fixture.CreateContext()) await new BackgroundJobRunner(db, []).RecoverStuckJobsAsync();
        await RunAsync();
        Assert.Equal((1, 1, 1, 1, 1), await CountsAsync());
        Assert.Equal(BackgroundJobStatus.Succeeded, (await JobAsync()).Status);
    }

    [Fact]
    public async Task Running_the_same_job_again_even_by_force_changes_nothing()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await EnqueueAsync();
        await RunAsync();
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM [BackgroundJobEffectReceipts]");
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 0, [AttemptCount] = 0 WHERE [JobType] = {FollowUpReminderService.JobType}");
        }
        await RunAsync();                                                                                // the handler itself finds the delivery and does nothing
        Assert.Equal((1, 1, 1, 1, 1), await CountsAsync());
    }

    // ---------- failure ----------

    [Fact]
    public async Task A_job_that_fails_all_its_attempts_is_given_fresh_ones_after_the_cool_down_for_as_long_as_the_reminder_is_owed()
    {
        var hyg = await UserAsync(Role.Hygienist);
        var task = await TaskAsync(hyg, Now.AddMinutes(-5));
        await EnqueueAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseReminderJobAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.FollowUpReminderSent}') THROW 59992, 'audit down', 1;");
        try
        {
            for (var i = 0; i < 4; i++) await RunAsync();                                                // three attempts, then it is Failed
            var failed = await JobAsync();
            Assert.Equal((BackgroundJobStatus.Failed, 3), (failed.Status, failed.AttemptCount));
            Assert.Equal((1, 0, 0, 0, 0), await CountsAsync());                                          // nothing stored by any attempt
            Assert.Equal(0, (await EnqueueAsync()).Reset);                                               // still cooling down
        }
        finally
        {
            await using var db = Fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestRefuseReminderJobAudit");
        }
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [StartedAtUtc] = DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()) WHERE [JobType] = {FollowUpReminderService.JobType}");
        Assert.Equal(1, (await EnqueueAsync()).Reset);                                                   // after the cool-down: a fresh set of attempts
        Assert.Equal((BackgroundJobStatus.Pending, 0), ((await JobAsync()).Status, (await JobAsync()).AttemptCount));
        await RunAsync();
        Assert.Equal((1, 1, 1, 1, 1), await CountsAsync());
        Assert.Equal(task.Id, (await DeliveryAsync()).TaskId);
    }

    private async Task<FollowUpReminderDelivery> DeliveryAsync() { await using var db = Fixture.CreateContext(); return await db.FollowUpReminderDeliveries.AsNoTracking().SingleAsync(); }

    [Fact]
    public async Task A_failed_job_is_not_reset_once_the_reminder_is_no_longer_owed()
    {
        var hyg = await UserAsync(Role.Hygienist);
        var task = await TaskAsync(hyg, Now.AddMinutes(-5));
        await EnqueueAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [BackgroundJobs] SET [Status] = 3, [AttemptCount] = 3, [StartedAtUtc] = DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()), [LastError] = N'X' WHERE [JobType] = {FollowUpReminderService.JobType}");
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Status] = N'Cancelled', [ClosedAtUtc] = SYSDATETIMEOFFSET(), [ClosedByUserId] = {S.Actor} WHERE [Id] = {task.Id}");
        Assert.Equal(0, (await EnqueueAsync()).Reset);
        Assert.Equal(BackgroundJobStatus.Failed, (await JobAsync()).Status);
    }

    // ---------- the job looks again ----------

    [Fact]
    public async Task A_task_completed_or_re_dated_after_the_job_was_enqueued_gets_no_reminder_and_the_job_still_succeeds()
    {
        var hyg = await UserAsync(Role.Hygienist);
        var completed = await TaskAsync(hyg, Now.AddMinutes(-5));
        await EnqueueAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [Status] = N'Completed', [ClosedAtUtc] = SYSDATETIMEOFFSET(), [ClosedByUserId] = {S.Actor} WHERE [Id] = {completed.Id}");
        await RunAsync();
        Assert.Equal((1, 0, 0, 0, 0), await CountsAsync());
        Assert.Equal(BackgroundJobStatus.Succeeded, (await JobAsync()).Status);

        var moved = await TaskAsync(hyg, Now.AddMinutes(-4));
        await EnqueueAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlAsync($"UPDATE [FollowUpTasks] SET [DueAtUtc] = DATEADD(DAY, 5, [DueAtUtc]) WHERE [Id] = {moved.Id}");
        await RunAsync();
        Assert.Equal((0, 0), ((await CountsAsync()).Notifications, (await CountsAsync()).Deliveries));
    }

    // ---------- who is told ----------

    [Fact]
    public async Task An_unassigned_task_is_the_office_managers_and_the_delivery_says_so()
    {
        var m1 = await UserAsync(Role.OfficeManager);
        var m2 = await UserAsync(Role.OfficeManager);
        await UserAsync(Role.OfficeManager, disabled: true);
        await TaskAsync(null, Now.AddMinutes(-5));
        await EnqueueAsync(); await RunAsync();
        await using var db = Fixture.CreateContext();
        var told = await db.Notifications.AsNoTracking().Where(n => n.Kind == NotificationKinds.FollowUpDue).Select(n => n.RecipientUserId).ToListAsync();
        Assert.Equal(new[] { m1, m2 }.OrderBy(x => x), told.OrderBy(x => x));
        Assert.Equal(FollowUpDeliveryOutcomes.Unassigned, (await db.FollowUpReminderDeliveries.AsNoTracking().SingleAsync()).Outcome);
        Assert.Contains("has no owner", (await db.FollowUpTaskEvents.AsNoTracking().SingleAsync(e => e.Kind == FollowUpEventKinds.Reminded)).Detail);
    }

    [Fact]
    public async Task When_nobody_can_be_told_no_job_is_made_until_someone_can_be()
    {
        await TaskAsync(null, Now.AddMinutes(-5));
        Assert.Equal(new ReminderEnqueueResult(0, 1, 0), await EnqueueAsync());
        Assert.Equal((0, 0, 0, 0, 0), await CountsAsync());
        await UserAsync(Role.OfficeManager);
        Assert.Equal(1, (await EnqueueAsync()).Enqueued);
        await RunAsync();
        Assert.Equal((1, 1, 1, 1, 1), await CountsAsync());
    }

    // ---------- the runner, the registration and the backlog ----------

    [Fact]
    public async Task The_inline_pass_runs_only_reminder_jobs_and_leaves_every_other_job_pending()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await using (var db = Fixture.CreateContext())
            await new BackgroundJobQueue(db).EnqueueAsync("other:1", "some.other.job", null);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(new ReminderPassResult(1, 0, 0, 0), await Service(db).ProcessOnceAsync(default));
        await using var check = Fixture.CreateContext();
        var other = await check.BackgroundJobs.AsNoTracking().SingleAsync(j => j.JobType == "some.other.job");
        Assert.Equal((BackgroundJobStatus.Pending, 0), (other.Status, other.AttemptCount));              // untouched: not claimed, not failed for want of a handler
    }

    [Fact]
    public async Task The_application_registers_the_reminder_handler_and_the_in_app_channel_for_the_job_runner()
    {
        using var api = new SchedulingApiHarness(Fixture);
        await api.SessionAsync("Admin");
        using var scope = api.Factory.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IBackgroundJobHandler>().Select(h => h.JobType).ToList();
        Assert.Contains(FollowUpReminderService.JobType, handlers);
        Assert.Contains(Alveara.Api.Architecture.Recall.RecallScheduler.JobType, handlers);
        Assert.Equal(handlers.Count, handlers.Distinct().Count());                                       // no job type has two handlers
        Assert.Contains(FollowUpChannels.InApp, scope.ServiceProvider.GetServices<IReminderChannel>().Select(c => c.Channel));
    }

    [Fact]
    public async Task A_backlog_bigger_than_a_page_is_worked_through_without_the_first_page_starving_the_rest()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await using (var db = Fixture.CreateContext())
        {
            for (var i = 0; i < FollowUpReminderService.PageSize + 30; i++)
                db.FollowUpTasks.Add(new FollowUpTask { Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = hyg, Title = $"Task {i}", DueAtUtc = Now.AddMinutes(-1000 + i), Status = FollowUpStatuses.Open, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-30) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(FollowUpReminderService.PageSize, (await EnqueueAsync()).Enqueued);
        Assert.Equal(30, (await EnqueueAsync()).Enqueued);                                               // the second pass skips the 200 that already have jobs and reaches the rest
        Assert.Equal(0, (await EnqueueAsync()).Enqueued);
        Assert.Equal(FollowUpReminderService.PageSize + 30, (await CountsAsync()).Jobs);
    }
}
