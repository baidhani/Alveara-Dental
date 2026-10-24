using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-009, the follow-up tables on their own: rows written straight to the database, around any service. A task event and a reminder delivery are append-only (51209, 51212); a task is never deleted, is final
/// once completed or cancelled and keeps its patient, creator, creation time and recall link (51210); an open task is assigned to an enabled account and a recall follows a task of the same patient (51211);
/// a task creates at most one recall; the closed sets, the bounded text and the time ranges are enforced; a follow-up reminder is a notification of a defined shape; and the migration rolls back and forward
/// while no task exists and refuses to roll back over one (51213).
/// </summary>
public class FollowUpSchemaTests : SafetyTestBase
{
    private const string BeforeFollowUps = "20261023140822_AddAdminAvailabilityGuard";

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    private async Task<Microsoft.Data.SqlClient.SqlException> AddRefusedAsync(params object[] rows) => await Refusal(() => AddAsync(rows));

    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    private FollowUpTask NewTask(Action<FollowUpTask>? change = null)
    {
        var t = new FollowUpTask
        {
            Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = Other, Title = "Call the patient about the crown", DueAtUtc = DateTimeOffset.UtcNow.AddDays(7), Status = FollowUpStatuses.Open,
            CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(t);
        return t;
    }

    private static void Close(FollowUpTask t, string status, Guid by)
    {
        t.Status = status; t.ClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1); t.ClosedByUserId = by;
    }

    private FollowUpTaskEvent NewEvent(Guid taskId, string kind = FollowUpEventKinds.Created, Guid? actor = null, string? detail = null) =>
        new() { Id = Guid.NewGuid(), TaskId = taskId, Kind = kind, ActorUserId = actor ?? S.Actor, OccurredAtUtc = DateTimeOffset.UtcNow, Detail = detail };

    private async Task<Guid> DisabledUserAsync()
    {
        var id = Guid.NewGuid();
        await AddAsync(new UserAccount { Id = id, Username = $"off-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow, IsDisabled = true });
        return id;
    }

    // ---------- the tables and their rules ----------

    [Fact]
    public async Task A_fresh_database_has_the_follow_up_tables_empty_and_the_reminder_kind_is_known()
    {
        Assert.Equal((0, 0, 0), (await CountAsync<FollowUpTask>(), await CountAsync<FollowUpTaskEvent>(), await CountAsync<FollowUpReminderDelivery>()));
        Assert.Contains(NotificationKinds.FollowUpDue, NotificationKinds.All);
        await AddAsync(NewTask());
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public async Task A_task_needs_a_real_title(string title)
    {
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.Title = title))).Number);
    }

    [Fact]
    public async Task A_title_cannot_hold_control_characters_or_exceed_its_length()
    {
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.Title = "bad\u0001title"))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.Title = "nul\0title"))).Number);
        Assert.NotEqual(0, (await AddRefusedAsync(NewTask(t => t.Title = new string('x', FollowUpRules.TitleMaxLength + 1)))).Number);
        await AddAsync(NewTask(t => t.Title = new string('x', FollowUpRules.TitleMaxLength)));
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task A_note_is_optional_but_when_present_is_real_text_within_its_bound()
    {
        await AddAsync(NewTask(t => t.Note = null), NewTask(t => t.Note = "Line one\nline two\ttabbed"));
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.Note = "   "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.Note = "bell\u0007"))).Number);
        Assert.NotEqual(0, (await AddRefusedAsync(NewTask(t => t.Note = new string('n', FollowUpRules.NoteMaxLength + 1)))).Number);
        Assert.Equal(2, await CountAsync<FollowUpTask>());
    }

    [Theory]
    [InlineData("open")]
    [InlineData("Done")]
    [InlineData("")]
    public async Task The_status_is_one_of_the_three_exact_names(string status)
    {
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.Status = status))).Number);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(3651, false)]
    public async Task A_recall_interval_is_one_to_3650_days_or_absent(int days, bool allowed)
    {
        if (allowed) { await AddAsync(NewTask(t => t.RecallIntervalDays = days)); Assert.Equal(1, await CountAsync<FollowUpTask>()); }
        else Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.RecallIntervalDays = days))).Number);
        await AddAsync(NewTask(t => t.RecallIntervalDays = null));
    }

    [Fact]
    public async Task Times_must_be_real_times()
    {
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.DueAtUtc = new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero)))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.DueAtUtc = new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero)))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => { Close(t, FollowUpStatuses.Completed, S.Actor); t.ClosedAtUtc = t.CreatedAtUtc.AddSeconds(-1); }))).Number);   // closed before it was created
    }

    [Fact]
    public async Task An_open_task_has_no_closure_and_a_closed_task_has_who_and_when()
    {
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.ClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1)))).Number);                                  // open but closed at
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.ClosureNote = "done early"))).Number);                                                          // open but carries a closure note
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.Status = FollowUpStatuses.Completed))).Number);                                                 // completed with nobody and no time
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => { t.Status = FollowUpStatuses.Cancelled; t.ClosedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1); }))).Number);   // cancelled with no person
        await AddAsync(NewTask(t => Close(t, FollowUpStatuses.Completed, Other)), NewTask(t => { Close(t, FollowUpStatuses.Cancelled, S.Actor); t.ClosureNote = "Patient moved away"; }));
        Assert.Equal(2, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task A_task_names_a_real_patient_and_real_people()
    {
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.PatientId = Guid.NewGuid()))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.AssigneeUserId = Guid.NewGuid()))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.CreatedByUserId = Guid.NewGuid()))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewTask(t => t.RecallOfTaskId = Guid.NewGuid()))).Number);
    }

    // ---------- the triggers ----------

    [Fact]
    public async Task A_task_is_never_deleted()
    {
        var task = NewTask();
        await AddAsync(task);
        Assert.Equal(51210, (await SqlRefusalAsync($"DELETE FROM [FollowUpTasks]")).Number);
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task A_completed_or_cancelled_task_is_final()
    {
        var done = NewTask(t => Close(t, FollowUpStatuses.Completed, Other));
        var cancelled = NewTask(t => Close(t, FollowUpStatuses.Cancelled, Other));
        await AddAsync(done, cancelled);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [Title] = N'Rewritten' WHERE [Id] = {done.Id}")).Number);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [DueAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {cancelled.Id}")).Number);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [ClosureNote] = N'later' WHERE [Id] = {done.Id}")).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal("Call the patient about the crown", (await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == done.Id)).Title);
    }

    [Fact]
    public async Task An_open_task_can_be_edited_and_then_closed_but_keeps_its_patient_creator_creation_time_and_recall_link()
    {
        var task = NewTask();
        await AddAsync(task);
        await using (var db = Fixture.CreateContext())
        {
            var t = await db.FollowUpTasks.SingleAsync(x => x.Id == task.Id);
            t.Title = "Call about the crown and the cleaning";
            t.DueAtUtc = t.DueAtUtc.AddDays(2);
            await db.SaveChangesAsync();
        }
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [PatientId] = {Bo} WHERE [Id] = {task.Id}")).Number);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [CreatedByUserId] = {Other} WHERE [Id] = {task.Id}")).Number);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [CreatedAtUtc] = DATEADD(DAY, -1, [CreatedAtUtc]) WHERE [Id] = {task.Id}")).Number);
        await using (var db = Fixture.CreateContext())
        {
            var t = await db.FollowUpTasks.SingleAsync(x => x.Id == task.Id);
            Close(t, FollowUpStatuses.Completed, Other);
            await db.SaveChangesAsync();
        }
        await using var check = Fixture.CreateContext();
        Assert.Equal(FollowUpStatuses.Completed, (await check.FollowUpTasks.AsNoTracking().SingleAsync(x => x.Id == task.Id)).Status);
    }

    [Fact]
    public async Task An_open_task_cannot_be_assigned_to_a_disabled_account_but_an_existing_task_can_still_be_edited_and_closed_after_its_assignee_is_disabled()
    {
        var off = await DisabledUserAsync();
        Assert.Equal(51211, (await AddRefusedAsync(NewTask(t => t.AssigneeUserId = off))).Number);

        var task = NewTask();
        await AddAsync(task);
        Assert.Equal(51211, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [AssigneeUserId] = {off} WHERE [Id] = {task.Id}")).Number);       // reassigning to a disabled account
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [UserAccounts] SET [IsDisabled] = 1 WHERE [Id] = {Other}");
        await using (var db = Fixture.CreateContext())
        {
            var t = await db.FollowUpTasks.SingleAsync(x => x.Id == task.Id);
            t.DueAtUtc = t.DueAtUtc.AddDays(1);                                                                                                       // an edit that leaves the assignee alone
            await db.SaveChangesAsync();
            Close(t, FollowUpStatuses.Cancelled, S.Actor);                                                                                            // and closing it
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_task_creates_at_most_one_recall_and_a_recall_follows_a_task_of_the_same_patient()
    {
        var first = NewTask(t => { t.RecallIntervalDays = 180; Close(t, FollowUpStatuses.Completed, Other); });
        await AddAsync(first);
        await AddAsync(NewTask(t => t.RecallOfTaskId = first.Id));
        Assert.True((await AddRefusedAsync(NewTask(t => t.RecallOfTaskId = first.Id))).Number is 2601 or 2627);                                       // a second recall of the same task
        Assert.Equal(2, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task A_recall_for_another_patient_is_refused_even_when_the_task_has_no_recall_yet()
    {
        var first = NewTask(t => Close(t, FollowUpStatuses.Completed, Other));
        await AddAsync(first);
        Assert.Equal(51211, (await AddRefusedAsync(NewTask(t => { t.RecallOfTaskId = first.Id; t.PatientId = Bo; }))).Number);
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }

    [Fact]
    public async Task A_task_is_never_its_own_recall_and_the_recall_link_never_changes()
    {
        var task = NewTask();
        await AddAsync(task);
        Assert.Equal(547, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [RecallOfTaskId] = [Id] WHERE [Id] = {task.Id}")).Number);
        var other = NewTask();
        await AddAsync(other);
        Assert.Equal(51210, (await SqlRefusalAsync($"UPDATE [FollowUpTasks] SET [RecallOfTaskId] = {task.Id} WHERE [Id] = {other.Id}")).Number);
    }

    [Fact]
    public async Task Task_events_are_append_only_and_only_the_systems_reminder_has_no_person()
    {
        var task = NewTask();
        await AddAsync(task);
        await AddAsync(NewEvent(task.Id, FollowUpEventKinds.Created), new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = FollowUpEventKinds.Reminded, ActorUserId = null, OccurredAtUtc = DateTimeOffset.UtcNow });
        Assert.Equal(51209, (await SqlRefusalAsync($"UPDATE [FollowUpTaskEvents] SET [Detail] = N'rewritten'")).Number);
        Assert.Equal(51209, (await SqlRefusalAsync($"DELETE FROM [FollowUpTaskEvents]")).Number);
        Assert.Equal(547, (await AddRefusedAsync(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = FollowUpEventKinds.Completed, ActorUserId = null, OccurredAtUtc = DateTimeOffset.UtcNow })).Number);
        Assert.Equal(2, await CountAsync<FollowUpTaskEvent>());
    }

    [Theory]
    [InlineData("created")]
    [InlineData("Deleted")]
    [InlineData("")]
    public async Task An_event_kind_is_one_of_the_defined_names(string kind)
    {
        var task = NewTask();
        await AddAsync(task);
        Assert.Equal(547, (await AddRefusedAsync(NewEvent(task.Id, kind))).Number);
    }

    [Fact]
    public async Task An_event_names_its_task_has_a_real_time_and_a_short_plain_detail()
    {
        var task = NewTask();
        await AddAsync(task);
        Assert.Equal(547, (await AddRefusedAsync(NewEvent(Guid.NewGuid()))).Number);
        Assert.Equal(547, (await AddRefusedAsync(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = FollowUpEventKinds.Edited, ActorUserId = S.Actor, OccurredAtUtc = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero) })).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewEvent(task.Id, FollowUpEventKinds.Edited, detail: "   "))).Number);
        Assert.Equal(547, (await AddRefusedAsync(NewEvent(task.Id, FollowUpEventKinds.Edited, detail: "bell\u0007"))).Number);
        Assert.NotEqual(0, (await AddRefusedAsync(NewEvent(task.Id, FollowUpEventKinds.Edited, detail: new string('d', FollowUpRules.DetailMaxLength + 1)))).Number);
        await AddAsync(NewEvent(task.Id, FollowUpEventKinds.DueChanged, detail: "Due 2026-11-03"));
        Assert.Equal(1, await CountAsync<FollowUpTaskEvent>());
    }

    // ---------- reminder deliveries ----------

    private FollowUpReminderDelivery NewDelivery(FollowUpTask task, string channel = FollowUpChannels.InApp, string outcome = FollowUpDeliveryOutcomes.Delivered) =>
        new() { Id = Guid.NewGuid(), TaskId = task.Id, DueAtUtc = task.DueAtUtc, Channel = channel, RecipientUserId = Other, Outcome = outcome, CreatedAtUtc = DateTimeOffset.UtcNow };

    [Fact]
    public async Task One_reminder_per_task_due_time_and_channel_and_it_is_never_changed()
    {
        var task = NewTask();
        await AddAsync(task);
        await AddAsync(NewDelivery(task));
        Assert.True((await AddRefusedAsync(NewDelivery(task))).Number is 2601 or 2627);
        Assert.Equal(51212, (await SqlRefusalAsync($"UPDATE [FollowUpReminderDeliveries] SET [Outcome] = N'Fallback'")).Number);
        Assert.Equal(51212, (await SqlRefusalAsync($"DELETE FROM [FollowUpReminderDeliveries]")).Number);
        // a new due time is a new reminder
        var moved = NewDelivery(task); moved.DueAtUtc = task.DueAtUtc.AddDays(1);
        await AddAsync(moved);
        Assert.Equal(2, await CountAsync<FollowUpReminderDelivery>());
    }

    [Theory]
    [InlineData("Email", "Delivered")]
    [InlineData("inapp", "Delivered")]
    [InlineData("InApp", "Sent")]
    [InlineData("InApp", "")]
    public async Task A_delivery_names_a_defined_channel_and_outcome(string channel, string outcome)
    {
        var task = NewTask();
        await AddAsync(task);
        Assert.Equal(547, (await AddRefusedAsync(NewDelivery(task, channel, outcome))).Number);
    }

    // ---------- the reminder notification ----------

    private Notification Reminder(FollowUpTask task, Action<Notification>? change = null)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = Other, Kind = NotificationKinds.FollowUpDue, Priority = NotificationPriorities.Normal, RelatedPatientId = task.PatientId,
            SourceKey = FollowUpRules.ReminderSourceKey(task.Id, task.DueAtUtc), CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(n);
        return n;
    }

    [Fact]
    public async Task A_follow_up_reminder_names_its_patient_and_task_through_its_key_and_has_no_message_and_no_document()
    {
        var task = NewTask();
        await AddAsync(task);
        await AddAsync(Reminder(task));
        Assert.Equal(547, (await AddRefusedAsync(Reminder(task, n => { n.RelatedPatientId = null; n.RecipientUserId = S.Actor; }))).Number);   // another person, so the unique key does not answer first
        Assert.Equal(547, (await AddRefusedAsync(Reminder(task, n => n.SourceKey = null))).Number);
        Assert.Equal(547, (await AddRefusedAsync(Reminder(task, n => n.SourceKey = "followup due with spaces"))).Number);
        Assert.Equal(1, await CountAsync<Notification>());
    }

    [Fact]
    public async Task One_reminder_notification_per_task_due_time_and_person_however_often_it_is_attempted()
    {
        var task = NewTask();
        await AddAsync(task);
        await AddAsync(Reminder(task));
        Assert.True((await AddRefusedAsync(Reminder(task))).Number is 2601 or 2627);
        await AddAsync(Reminder(task, n => n.RecipientUserId = S.Actor));       // another person (the office-manager fallback) is a different notification
        Assert.Equal(2, await CountAsync<Notification>());
    }

    [Fact]
    public void The_reminder_key_names_the_task_and_its_due_time_and_uses_only_allowed_characters()
    {
        var id = Guid.NewGuid();
        var due = new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero);
        var key = FollowUpRules.ReminderSourceKey(id, due);
        Assert.Equal($"followup.due:{id:N}:{due.UtcTicks}", key);
        Assert.Matches("^[A-Za-z0-9:._-]+$", key);
        Assert.NotEqual(key, FollowUpRules.ReminderSourceKey(id, due.AddMinutes(1)));
        Assert.True(key.Length <= 100);                                                                                                                // the notification's source key column
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_task_exists_and_refuses_to_roll_back_over_one()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeFollowUps);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('FollowUpTasks','FollowUpTaskEvents','FollowUpReminderDeliveries')").SingleAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(3, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('FollowUpTasks','FollowUpTaskEvents','FollowUpReminderDeliveries')").SingleAsync());
            Assert.Equal(4, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_FollowUpTaskEvents_Immutable','TR_FollowUpReminderDeliveries_Immutable','TR_FollowUpTasks_Guard','TR_FollowUpTasks_Links')").SingleAsync());
        }

        await AddAsync(NewTask());
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeFollowUps); });
        Assert.Equal(51213, e.Number);
        Assert.Equal(1, await CountAsync<FollowUpTask>());
    }
}
