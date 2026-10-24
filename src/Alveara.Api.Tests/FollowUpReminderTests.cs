using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-009, acceptance criterion 1: when a follow-up task is due, a reminder reaches the responsible party. The reminder pass is driven directly with a clock the test sets: nothing before the due time, one reminder
/// at it, never a second however many passes run, every reminder owed after a gap (the missed-reminder path), a new reminder when the due time changes, none for a closed task, the office managers when the responsible
/// person cannot be reminded, nothing stored when the audit entry cannot be written (and the same pass succeeds afterwards), two passes at once delivering once, and a second channel that neither blocks nor is blocked by the in-app one.
/// </summary>
public class FollowUpReminderTests : SafetyTestBase
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

    private async Task<FollowUpTask> TaskAsync(Guid assignee, DateTimeOffset due, string status = FollowUpStatuses.Open, Guid? patient = null, string title = "Secret title about the patient")
    {
        var t = new FollowUpTask
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, AssigneeUserId = assignee, Title = title, Note = "Secret note", DueAtUtc = due, Status = status, CreatedByUserId = S.Actor,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
        };
        if (status != FollowUpStatuses.Open) { t.ClosedAtUtc = DateTimeOffset.UtcNow.AddDays(-1); t.ClosedByUserId = S.Actor; }
        await using var db = Fixture.CreateContext();
        db.FollowUpTasks.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    private async Task<ReminderPassResult> PassAsync(DateTimeOffset at, params IReminderChannel[] extra)
    {
        await using var db = Fixture.CreateContext();
        var clock = new TestClock(at);
        return await new FollowUpReminderService(db, clock, PolicyTestSupport.Defaults(), new IReminderChannel[] { new InAppReminderChannel(db, clock) }.Concat(extra)).ProcessOnceAsync(default);
    }

    private async Task<(int Notifications, int Deliveries, int Events, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Notifications.CountAsync(n => n.Kind == NotificationKinds.FollowUpDue), await db.FollowUpReminderDeliveries.CountAsync(),
                await db.FollowUpTaskEvents.CountAsync(e => e.Kind == FollowUpEventKinds.Reminded), await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpReminderSent));
    }

    // ---------- the reminder ----------

    [Fact]
    public async Task Nothing_is_sent_before_the_task_is_due_and_one_reminder_is_sent_when_it_is()
    {
        var hyg = await UserAsync(Role.Hygienist);
        var task = await TaskAsync(hyg, Now.AddHours(1));
        Assert.Equal(new ReminderPassResult(0, 0, 0, 0), await PassAsync(Now));
        Assert.Equal((0, 0, 0, 0), await CountsAsync());

        var result = await PassAsync(Now.AddHours(1).AddSeconds(1));
        Assert.Equal(new ReminderPassResult(1, 0, 0, 0), result);
        Assert.Equal((1, 1, 1, 1), await CountsAsync());

        await using var db = Fixture.CreateContext();
        var n = await db.Notifications.AsNoTracking().SingleAsync(x => x.Kind == NotificationKinds.FollowUpDue);
        Assert.Equal((hyg, Ann), (n.RecipientUserId, n.RelatedPatientId));
        Assert.Equal(FollowUpRules.ReminderSourceKey(task.Id, task.DueAtUtc), n.SourceKey);
        Assert.Null(n.MessageId);
        var delivery = await db.FollowUpReminderDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal((task.Id, task.DueAtUtc, FollowUpChannels.InApp, hyg, FollowUpDeliveryOutcomes.Delivered), (delivery.TaskId, delivery.DueAtUtc, delivery.Channel, delivery.RecipientUserId, delivery.Outcome));
        var reminded = await db.FollowUpTaskEvents.AsNoTracking().SingleAsync(e => e.Kind == FollowUpEventKinds.Reminded);
        Assert.Null(reminded.ActorUserId);                                                                                  // the system's own entry
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.FollowUpReminderSent);
        Assert.Null(audit.PerformedByUserAccountId);
        Assert.Equal(task.Id, audit.TargetUserAccountId);
    }

    [Fact]
    public async Task The_reminder_carries_no_title_and_no_note_anywhere()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await PassAsync(Now);
        await using var db = Fixture.CreateContext();
        var text = string.Join("|", (await db.Notifications.AsNoTracking().ToListAsync()).Select(n => $"{n.Kind}{n.SourceKey}"))
                 + string.Join("|", (await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.Kind == FollowUpEventKinds.Reminded).ToListAsync()).Select(e => e.Detail))
                 + string.Join("|", (await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.FollowUpReminderSent).ToListAsync()).Select(a => a.Details));
        Assert.DoesNotContain("Secret", text);
    }

    [Fact]
    public async Task However_many_passes_run_a_task_is_reminded_once_for_its_due_time()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-1));
        Assert.Equal(1, (await PassAsync(Now)).Sent);
        for (var i = 1; i < 4; i++) Assert.Equal(new ReminderPassResult(0, 0, 0, 0), await PassAsync(Now.AddMinutes(i)));   // later passes find nothing owed
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task After_a_gap_every_reminder_owed_is_sent_oldest_first_in_one_catch_up()
    {
        var hyg = await UserAsync(Role.Hygienist);
        var dentist = await UserAsync(Role.Dentist);
        await TaskAsync(hyg, Now.AddDays(-3));
        await TaskAsync(dentist, Now.AddDays(-2), patient: Bo);
        await TaskAsync(hyg, Now.AddHours(-5));
        await TaskAsync(hyg, Now.AddDays(5));                              // not due yet
        var result = await PassAsync(Now);                                  // the service was down for days
        Assert.Equal(new ReminderPassResult(3, 0, 0, 0), result);
        Assert.Equal((3, 3, 3, 3), await CountsAsync());
        await using var db = Fixture.CreateContext();
        Assert.Equal(new[] { hyg, dentist }.OrderBy(x => x), (await db.Notifications.AsNoTracking().Select(n => n.RecipientUserId).Distinct().ToListAsync()).OrderBy(x => x));
    }

    [Theory]
    [InlineData(FollowUpStatuses.Completed)]
    [InlineData(FollowUpStatuses.Cancelled)]
    public async Task A_closed_task_is_never_reminded(string status)
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddDays(-1), status);
        Assert.Equal(new ReminderPassResult(0, 0, 0, 0), await PassAsync(Now));
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Changing_the_due_time_arms_a_new_reminder_for_the_new_time_and_leaves_the_old_one_recorded()
    {
        var hyg = await UserAsync(Role.Hygienist);
        var task = await TaskAsync(hyg, Now.AddMinutes(-10));
        await PassAsync(Now);
        Assert.Equal((1, 1, 1, 1), await CountsAsync());

        await using (var db = Fixture.CreateContext())
        {
            var t = await db.FollowUpTasks.SingleAsync(x => x.Id == task.Id);
            t.DueAtUtc = Now.AddDays(2);                                       // pushed out
            await db.SaveChangesAsync();
        }
        await PassAsync(Now.AddDays(1));
        Assert.Equal((1, 1, 1, 1), await CountsAsync());                       // not due again yet
        await PassAsync(Now.AddDays(2).AddMinutes(1));
        Assert.Equal((2, 2, 2, 2), await CountsAsync());                       // reminded again for the new time
        await using var check = Fixture.CreateContext();
        Assert.Equal(2, await check.Notifications.Where(n => n.Kind == NotificationKinds.FollowUpDue).Select(n => n.SourceKey).Distinct().CountAsync());
    }

    // ---------- when the responsible person cannot be reminded ----------

    [Fact]
    public async Task A_disabled_assignee_means_the_office_managers_are_told_instead_and_the_delivery_says_so()
    {
        var gone = await UserAsync(Role.Hygienist);
        var task = await TaskAsync(gone, Now.AddMinutes(-5));
        var m1 = await UserAsync(Role.OfficeManager);
        var m2 = await UserAsync(Role.OfficeManager);
        await UserAsync(Role.OfficeManager, disabled: true);                    // a disabled manager is not told
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [UserAccounts] SET [IsDisabled] = 1 WHERE [Id] = {gone}");

        Assert.Equal(new ReminderPassResult(0, 1, 0, 0), await PassAsync(Now));
        await using var check = Fixture.CreateContext();
        var told = await check.Notifications.AsNoTracking().Where(n => n.Kind == NotificationKinds.FollowUpDue).Select(n => n.RecipientUserId).ToListAsync();
        Assert.Equal(new[] { m1, m2 }.OrderBy(x => x), told.OrderBy(x => x));
        var delivery = await check.FollowUpReminderDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(FollowUpDeliveryOutcomes.Fallback, delivery.Outcome);
        Assert.Contains(delivery.RecipientUserId, new[] { m1, m2 });                          // the first of the managers, in the database's order
        Assert.Contains("could not be reminded", (await check.FollowUpTaskEvents.AsNoTracking().SingleAsync(e => e.Kind == FollowUpEventKinds.Reminded)).Detail);
        Assert.Equal(1, await check.FollowUpTasks.CountAsync(t => t.Id == task.Id));
    }

    [Fact]
    public async Task An_assignee_whose_role_can_no_longer_see_follow_ups_is_treated_the_same_way()
    {
        var billing = await UserAsync(Role.Billing);                           // billing does not hold ViewFollowUps
        var manager = await UserAsync(Role.OfficeManager);
        await TaskAsync(billing, Now.AddMinutes(-5));
        Assert.Equal(new ReminderPassResult(0, 1, 0, 0), await PassAsync(Now));
        await using var db = Fixture.CreateContext();
        Assert.Equal(manager, (await db.Notifications.AsNoTracking().SingleAsync(n => n.Kind == NotificationKinds.FollowUpDue)).RecipientUserId);
    }

    [Fact]
    public async Task When_nobody_can_be_told_nothing_is_stored_and_the_reminder_is_sent_once_someone_can_be()
    {
        var gone = await UserAsync(Role.Hygienist);
        await TaskAsync(gone, Now.AddMinutes(-5));
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [UserAccounts] SET [IsDisabled] = 1 WHERE [Id] = {gone}");

        Assert.Equal(new ReminderPassResult(0, 0, 1, 0), await PassAsync(Now));
        Assert.Equal((0, 0, 0, 0), await CountsAsync());                         // not lost, not recorded: the next pass tries again
        await UserAsync(Role.OfficeManager);
        Assert.Equal(new ReminderPassResult(0, 1, 0, 0), await PassAsync(Now.AddMinutes(1)));
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    // ---------- failure ----------

    [Fact]
    public async Task When_the_audit_entry_cannot_be_written_nothing_is_stored_and_the_same_pass_succeeds_afterwards()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseReminderAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.FollowUpReminderSent}') THROW 59995, 'audit down', 1;");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => PassAsync(Now));
        }
        finally
        {
            await using var db = Fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestRefuseReminderAudit");
        }
        Assert.Equal((0, 0, 0, 0), await CountsAsync());                         // no notification, no delivery, no event: one save
        Assert.Equal(new ReminderPassResult(1, 0, 0, 0), await PassAsync(Now.AddMinutes(1)));
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Two_passes_at_the_same_moment_deliver_once_and_neither_fails()
    {
        var hyg = await UserAsync(Role.Hygienist);
        for (var i = 0; i < 5; i++) await TaskAsync(hyg, Now.AddMinutes(-5 - i));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PassAsync(Now)));
        Assert.All(results, r => Assert.True(r.Total <= 5));
        for (var i = 0; i < 3; i++) await PassAsync(Now);                         // whatever a losing pass discarded is picked up by the next
        Assert.Equal((5, 5, 5, 5), await CountsAsync());
    }

    // ---------- the second channel (email later) ----------

    private sealed class RecordingChannel(string name, ReminderChannelResult result, bool throws = false) : IReminderChannel
    {
        public string Channel => name;
        public List<ReminderRequest> Seen { get; } = [];
        public Task<ReminderChannelResult> SendAsync(ReminderRequest request, CancellationToken ct)
        {
            Seen.Add(request);
            if (throws) throw new InvalidOperationException("provider down");
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task A_broken_second_channel_never_stops_the_in_app_reminder_and_is_tried_again_on_the_next_pass()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        var result = await PassAsync(Now, new RecordingChannel("Other", ReminderChannelResult.RetryLater));
        Assert.Equal(1, result.Sent);                                            // the in-app reminder went out
        Assert.Equal(1, result.Deferred);                                        // the other channel asked to try again
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
        var again = await PassAsync(Now.AddMinutes(1), new RecordingChannel("Other", ReminderChannelResult.RetryLater));
        Assert.Equal((0, 1), (again.Sent, again.Deferred));                      // in-app is not repeated; the other channel is asked again
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_channel_is_never_asked_again_for_a_reminder_it_has_already_delivered()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        var other = new RecordingChannel("InApp2", ReminderChannelResult.Delivered);
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("ALTER TABLE [FollowUpReminderDeliveries] DROP CONSTRAINT [CK_FollowUpReminderDeliveries_Channel]");
        for (var i = 0; i < 3; i++) await PassAsync(Now.AddMinutes(i), other);
        Assert.Single(other.Seen);                                                  // delivered on the first pass, so not asked on the next two (the unique key would only catch it afterwards)
        await using var check = Fixture.CreateContext();
        Assert.Equal(new[] { "InApp", "InApp2" }, await check.FollowUpReminderDeliveries.Select(d => d.Channel).OrderBy(c => c).ToArrayAsync());
    }

    [Fact]
    public async Task A_second_channel_that_throws_is_contained_and_the_in_app_reminder_still_goes_out()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        var failing = new RecordingChannel("Other", ReminderChannelResult.Delivered, throws: true);
        var result = await PassAsync(Now, failing);
        Assert.Equal((1, 1), (result.Sent, result.Deferred));
        Assert.Single(failing.Seen);
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
        await PassAsync(Now.AddMinutes(1), failing);
        Assert.Equal(2, failing.Seen.Count);                                      // asked again; the in-app reminder was not repeated
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    // ---------- the link and the center ----------

    [Fact]
    public async Task A_reminder_opens_the_patients_follow_ups_for_someone_who_may_view_patients_and_reveals_nothing_otherwise()
    {
        var hyg = await UserAsync(Role.Hygienist);
        await TaskAsync(hyg, Now.AddMinutes(-5));
        await PassAsync(Now);
        await using var db = Fixture.CreateContext();
        var id = (await db.Notifications.AsNoTracking().SingleAsync(n => n.Kind == NotificationKinds.FollowUpDue)).Id;
        var target = await new NotificationLinkService(db).ForNotificationAsync(hyg, id, mayViewPatients: true, default);
        Assert.Equal($"/patients/{Ann}/follow-ups", target.Path);
        await Assert.ThrowsAsync<NotificationException>(() => new NotificationLinkService(db).ForNotificationAsync(hyg, id, mayViewPatients: false, default));
        var someoneElse = await UserAsync(Role.Dentist);
        await Assert.ThrowsAsync<NotificationException>(() => new NotificationLinkService(db).ForNotificationAsync(someoneElse, id, mayViewPatients: true, default));   // someone else's
    }
}

/// <summary>The same reminder through the real API: a person creates a task that is already due, the pass runs, and the responsible person finds the reminder in their notification center without any task text.</summary>
public class FollowUpReminderApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    [Fact]
    public async Task A_due_task_shows_up_as_a_reminder_in_the_responsible_persons_notification_center_and_nowhere_else()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var frontDesk = await _api.SessionAsync("FrontDesk");
        var created = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/follow-ups",
            new { title = "Check the temporary crown", note = "Private note", dueAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30), assigneeUserId = hygienist.UserId }, $"rem-{Guid.NewGuid():N}");
        Assert.True(created.Status == HttpStatusCode.OK, created.Body.ToString());

        using (var scope = _api.Factory.Services.CreateScope())
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<FollowUpReminderService>().ProcessOnceAsync(default)).Sent);

        var mine = await hygienist.Client.GetAsync("/api/notifications?status=unread");
        var text = await mine.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Contains("followup.due", text);
        Assert.DoesNotContain("Check the temporary crown", text);
        Assert.DoesNotContain("Private note", text);
        Assert.DoesNotContain("followup.due", await (await frontDesk.Client.GetAsync("/api/notifications?status=unread")).Content.ReadAsStringAsync());
        Assert.DoesNotContain("followup.due", await (await dentist.Client.GetAsync("/api/notifications?status=unread")).Content.ReadAsStringAsync());     // the creator is not the responsible party
    }
}
