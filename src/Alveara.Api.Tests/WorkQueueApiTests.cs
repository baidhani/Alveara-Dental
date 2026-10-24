using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C01 through the real pipeline (real API, real SQL Server, cookie sessions): the work queue (most urgent first; the filters overdue, due soon, unassigned, mine and all; the narrowing by priority, kind and owner; the
/// counts; paging; bad filter values refused with stable codes; billing sees nothing), the quick actions (assign, reassign, unassign, claim, set priority, link an appointment of the same patient) with their history and audit
/// entries, stale and simultaneous changes answered 409 with nothing lost, closed tasks final, doing what is already done a quiet replay; and the recall settings and a patient's own recall arrangement with their limits and permissions.
/// </summary>
public class WorkQueueApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;
    private Guid _bo;
    private int _keys;
    private Session _admin = null!, _dentist = null!, _hygienist = null!, _frontDesk = null!, _billing = null!, _manager = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _admin = await _api.SessionAsync("Admin");
        _dentist = await _api.SessionAsync("Dentist");
        _hygienist = await _api.SessionAsync("Hygienist");
        _frontDesk = await _api.SessionAsync("FrontDesk");
        _billing = await _api.SessionAsync("Billing");
        _manager = await _api.SessionAsync("OfficeManager");
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private string Key() => $"wq-{Interlocked.Increment(ref _keys)}-{Guid.NewGuid():N}";
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static JsonElement TaskOf(JsonElement body) => body.GetProperty("task");

    private async Task<JsonElement> ManualAsync(Session by, Guid assignee, string title = "Call about the crown", Guid? patient = null, DateTimeOffset? due = null)
    {
        var r = await by.SendAsync(HttpMethod.Post, $"/api/patients/{patient ?? _ann}/follow-ups", new { title, note = (string?)null, dueAtUtc = due ?? DateTimeOffset.UtcNow.AddDays(20), assigneeUserId = assignee, recallIntervalDays = (int?)null }, Key());
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return TaskOf(r.Body);
    }

    /// <summary>A task the system made (no owner, no creator), written straight to the database as the recall job would.</summary>
    private async Task<FollowUpTask> SystemTaskAsync(string kind = FollowUpKinds.Recall, Guid? patient = null, DateTimeOffset? due = null, string priority = FollowUpPriorities.Normal, string title = "Patient recall: schedule the next visit")
    {
        var t = new FollowUpTask
        {
            Id = Guid.NewGuid(), PatientId = patient ?? _ann, AssigneeUserId = null, Title = title, DueAtUtc = due ?? DateTimeOffset.UtcNow.AddDays(3), Status = FollowUpStatuses.Open, Priority = priority, Kind = kind,
            SourceKey = $"recall:{Guid.NewGuid():N}:x", CreatedByUserId = null, CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
        };
        await using var db = _fixture.CreateContext();
        db.FollowUpTasks.Add(t);
        await db.SaveChangesAsync();
        return t;
    }


    private async Task<string> VersionOfAsync(Guid id) => Str((await _dentist.SendAsync(HttpMethod.Get, $"/api/follow-ups/{id}")).Body.GetProperty("task"), "rowVersion");

    private Task<(HttpStatusCode Status, JsonElement Body)> QueueAsync(Session s, string query = "") => s.SendAsync(HttpMethod.Get, "/api/work-queue" + query);

    private static List<string> Titles(JsonElement body) => body.GetProperty("items").EnumerateArray().Select(i => Str(i.GetProperty("task"), "title")).ToList();

    private async Task<(int Events, int Audit)> CountsAsync(Guid task, string kind, string audit)
    {
        await using var db = _fixture.CreateContext();
        return (await db.FollowUpTaskEvents.CountAsync(e => e.TaskId == task && e.Kind == kind), await db.AuditLogEntries.CountAsync(a => a.EventType == audit && a.TargetUserAccountId == task));
    }

    // ---------- reading the queue ----------

    [Fact]
    public async Task Every_role_that_can_see_follow_ups_reads_the_queue_and_billing_and_anonymous_callers_do_not()
    {
        await ManualAsync(_dentist, _hygienist.UserId);
        foreach (var s in new[] { _admin, _dentist, _hygienist, _frontDesk, _manager })
            Assert.Equal(HttpStatusCode.OK, (await QueueAsync(s)).Status);
        var denied = await QueueAsync(_billing);
        Assert.Equal((HttpStatusCode.Forbidden, "permission_denied"), (denied.Status, Error(denied.Body)));
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/work-queue")).StatusCode);
    }

    [Fact]
    public async Task The_queue_lists_open_work_most_urgent_first_then_soonest_due_with_the_patient_and_the_urgency_in_words()
    {
        var soon = DateTimeOffset.UtcNow.AddDays(2);
        await ManualAsync(_dentist, _hygienist.UserId, "Normal later", due: DateTimeOffset.UtcNow.AddDays(30));
        await SystemTaskAsync(priority: FollowUpPriorities.High, title: "High soon", due: soon);
        await SystemTaskAsync(priority: FollowUpPriorities.Urgent, title: "Urgent late", due: DateTimeOffset.UtcNow.AddDays(40), patient: _bo);
        await SystemTaskAsync(priority: FollowUpPriorities.High, title: "High overdue", due: DateTimeOffset.UtcNow.AddDays(-3));
        await SystemTaskAsync(priority: FollowUpPriorities.Low, title: "Low", due: DateTimeOffset.UtcNow.AddDays(-9));
        var queue = await QueueAsync(_frontDesk);
        Assert.Equal(new[] { "Urgent late", "High overdue", "High soon", "Normal later", "Low" }, Titles(queue.Body));
        var items = queue.Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { "Later", "Overdue", "DueSoon", "Later", "Overdue" }, items.Select(i => Str(i, "urgency")).ToArray());
        Assert.Equal("Bo Kim", Str(items[0], "patientName"));
        Assert.Equal(5, queue.Body.GetProperty("total").GetInt32());
        Assert.Equal(7, queue.Body.GetProperty("dueSoonDays").GetInt32());
    }

    [Fact]
    public async Task The_counts_say_how_many_open_tasks_each_filter_would_show_whatever_filter_is_on()
    {
        await ManualAsync(_dentist, _hygienist.UserId, "Mine", due: DateTimeOffset.UtcNow.AddDays(30));
        await ManualAsync(_dentist, _frontDesk.UserId, "Overdue theirs", due: DateTimeOffset.UtcNow.AddDays(-2));
        await SystemTaskAsync(title: "Unassigned soon", due: DateTimeOffset.UtcNow.AddDays(3));
        await SystemTaskAsync(title: "Unassigned overdue", due: DateTimeOffset.UtcNow.AddDays(-5));
        var closed = await ManualAsync(_dentist, _hygienist.UserId, "Closed one");
        await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{closed.GetProperty("id").GetGuid()}/complete", new { note = (string?)null, rowVersion = Str(closed, "rowVersion") });

        var asHygienist = await QueueAsync(_hygienist, "?filter=unassigned");
        var c = asHygienist.Body.GetProperty("counts");
        Assert.Equal((2, 1, 2, 1, 4), (c.GetProperty("overdue").GetInt32(), c.GetProperty("dueSoon").GetInt32(), c.GetProperty("unassigned").GetInt32(), c.GetProperty("mine").GetInt32(), c.GetProperty("all").GetInt32()));
        Assert.Equal(2, asHygienist.Body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Each_filter_shows_exactly_its_own_open_tasks()
    {
        await ManualAsync(_dentist, _hygienist.UserId, "Mine later", due: DateTimeOffset.UtcNow.AddDays(30));
        await ManualAsync(_dentist, _hygienist.UserId, "Mine overdue", due: DateTimeOffset.UtcNow.AddDays(-2));
        await ManualAsync(_dentist, _frontDesk.UserId, "Desk soon", due: DateTimeOffset.UtcNow.AddDays(4));
        await SystemTaskAsync(title: "Nobody soon", due: DateTimeOffset.UtcNow.AddDays(6));
        await SystemTaskAsync(title: "Nobody later", due: DateTimeOffset.UtcNow.AddDays(60));
        Assert.Equal(new[] { "Mine overdue" }, Titles((await QueueAsync(_hygienist, "?filter=overdue")).Body));
        Assert.Equal(new[] { "Desk soon", "Nobody soon" }, Titles((await QueueAsync(_hygienist, "?filter=duesoon")).Body).OrderBy(x => x));
        Assert.Equal(new[] { "Nobody soon", "Nobody later" }.OrderBy(x => x), Titles((await QueueAsync(_hygienist, "?filter=unassigned")).Body).OrderBy(x => x));
        Assert.Equal(new[] { "Mine overdue", "Mine later" }.OrderBy(x => x), Titles((await QueueAsync(_hygienist, "?filter=mine")).Body).OrderBy(x => x));
        Assert.Equal(5, (await QueueAsync(_hygienist, "?filter=all")).Body.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "Nobody soon" }, Titles((await QueueAsync(_hygienist, "?filter=duesoon&kind=Recall")).Body));
        Assert.Equal(new[] { "Desk soon" }, Titles((await QueueAsync(_hygienist, $"?filter=duesoon&owner={_frontDesk.UserId}")).Body));
        Assert.Equal(2, (await QueueAsync(_hygienist, "?owner=none")).Body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task The_due_soon_window_follows_the_practice_setting_and_the_other_narrowings_work()
    {
        await SystemTaskAsync(title: "In 10 days", due: DateTimeOffset.UtcNow.AddDays(10), priority: FollowUpPriorities.Urgent);
        await SystemTaskAsync(title: "In 3 days", due: DateTimeOffset.UtcNow.AddDays(3));
        Assert.Equal(new[] { "In 3 days" }, Titles((await QueueAsync(_manager, "?filter=duesoon")).Body));
        var settings = (await _manager.SendAsync(HttpMethod.Get, "/api/recall/settings")).Body;
        var put = await _manager.SendAsync(HttpMethod.Put, "/api/recall/settings", new { enabled = false, defaultIntervalDays = 180, leadDays = 30, maxVisitAgeMonths = 24, dueSoonDays = 14, rowVersion = Str(settings, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, put.Status);
        Assert.Equal(new[] { "In 10 days", "In 3 days" }, Titles((await QueueAsync(_manager, "?filter=duesoon")).Body));          // 14 days now reaches the later one, most urgent first
        Assert.Equal(new[] { "In 10 days" }, Titles((await QueueAsync(_manager, "?priority=Urgent")).Body));
        Assert.Equal(2, (await QueueAsync(_manager, "?kind=Recall")).Body.GetProperty("total").GetInt32());
        Assert.Equal(0, (await QueueAsync(_manager, "?kind=Manual")).Body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Closed_work_is_hidden_by_default_and_listed_on_request_and_paging_is_bounded()
    {
        var done = await ManualAsync(_dentist, _hygienist.UserId, "Done one");
        await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{done.GetProperty("id").GetGuid()}/complete", new { note = "Spoke to the patient", rowVersion = Str(done, "rowVersion") });
        for (var i = 0; i < 4; i++) await SystemTaskAsync(title: $"Open {i}", due: DateTimeOffset.UtcNow.AddDays(i + 1));
        Assert.DoesNotContain("Done one", Titles((await QueueAsync(_frontDesk)).Body));
        var closed = await QueueAsync(_frontDesk, "?status=closed");
        Assert.Equal(new[] { "Done one" }, Titles(closed.Body));
        Assert.Equal("Closed", Str(closed.Body.GetProperty("items")[0], "urgency"));
        Assert.Equal(5, (await QueueAsync(_frontDesk, "?status=all")).Body.GetProperty("total").GetInt32());

        var page1 = await QueueAsync(_frontDesk, "?pageSize=3");
        var page2 = await QueueAsync(_frontDesk, "?pageSize=3&page=2");
        Assert.Equal((3, 1, 4), (page1.Body.GetProperty("items").GetArrayLength(), page2.Body.GetProperty("items").GetArrayLength(), page1.Body.GetProperty("total").GetInt32()));
        Assert.Empty(Titles(page1.Body).Intersect(Titles(page2.Body)));
        Assert.Equal(QueueFilters.MaxPageSize, (await QueueAsync(_frontDesk, "?pageSize=5000")).Body.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, (await QueueAsync(_frontDesk, "?pageSize=0&page=-3")).Body.GetProperty("page").GetInt32());
    }

    [Theory]
    [InlineData("?filter=whenever", "filter_invalid")]
    [InlineData("?status=gone", "status_invalid")]
    [InlineData("?priority=Critical", "priority_invalid")]
    [InlineData("?priority=low", "priority_invalid")]
    [InlineData("?kind=Referral", "kind_invalid")]
    [InlineData("?owner=somebody", "owner_invalid")]
    public async Task A_filter_value_that_means_nothing_is_refused_with_a_stable_code(string query, string expected)
    {
        var r = await QueueAsync(_frontDesk, query);
        Assert.Equal((HttpStatusCode.BadRequest, expected), (r.Status, Error(r.Body)));
    }

    // ---------- assign, reassign, unassign, claim ----------

    [Fact]
    public async Task A_person_assigns_an_unassigned_task_and_it_is_recorded_as_an_assignment_in_the_history_and_the_audit_log()
    {
        var task = await SystemTaskAsync();
        var version = await VersionOfAsync(task.Id);
        var r = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _hygienist.UserId, rowVersion = version });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.False(r.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(_hygienist.UserId, TaskOf(r.Body).GetProperty("assigneeUserId").GetGuid());
        Assert.Equal((1, 1), await CountsAsync(task.Id, FollowUpEventKinds.Assigned, AuditEventTypes.FollowUpAssigned));
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.FollowUpAssigned);
        Assert.Equal(_frontDesk.UserId, audit.PerformedByUserAccountId);
        Assert.Equal(_frontDesk.UserId, (await db.FollowUpTaskEvents.AsNoTracking().SingleAsync(e => e.Kind == FollowUpEventKinds.Assigned)).ActorUserId);
    }

    [Fact]
    public async Task Reassigning_to_someone_else_is_a_reassignment_and_the_same_owner_again_is_a_quiet_replay()
    {
        var task = await SystemTaskAsync();
        await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _hygienist.UserId, rowVersion = await VersionOfAsync(task.Id) });
        var again = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _hygienist.UserId, rowVersion = (string?)null });
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());                                                  // no version needed to say the same thing twice
        var moved = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _dentist.UserId, rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal(_dentist.UserId, TaskOf(moved.Body).GetProperty("assigneeUserId").GetGuid());
        Assert.Equal(1, (await CountsAsync(task.Id, FollowUpEventKinds.Reassigned, AuditEventTypes.FollowUpAssigned)).Events);
        await using var db = _fixture.CreateContext();
        Assert.Equal(2, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpAssigned));    // two real changes, not three
    }

    [Fact]
    public async Task A_task_the_system_made_can_go_back_to_the_unassigned_queue_but_a_task_a_person_made_needs_an_owner()
    {
        var system = await SystemTaskAsync();
        await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{system.Id}/assign", new { assigneeUserId = _hygienist.UserId, rowVersion = await VersionOfAsync(system.Id) });
        var back = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{system.Id}/assign", new { assigneeUserId = (Guid?)null, rowVersion = await VersionOfAsync(system.Id) });
        Assert.Equal(HttpStatusCode.OK, back.Status);
        Assert.Equal(JsonValueKind.Null, TaskOf(back.Body).GetProperty("assigneeUserId").ValueKind);

        var manual = await ManualAsync(_dentist, _hygienist.UserId);
        var refused = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{manual.GetProperty("id").GetGuid()}/assign", new { assigneeUserId = (Guid?)null, rowVersion = Str(manual, "rowVersion") });
        Assert.Equal((HttpStatusCode.BadRequest, "assignee_invalid"), (refused.Status, Error(refused.Body)));
    }

    [Fact]
    public async Task A_task_cannot_be_given_to_billing_an_unknown_person_or_a_disabled_account()
    {
        var off = await _api.SessionAsync("Hygienist");
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{off.UserId}/enabled", new { enabled = false });
        var task = await SystemTaskAsync();
        var v = await VersionOfAsync(task.Id);
        foreach (var who in new[] { _billing.UserId, Guid.NewGuid(), off.UserId })
        {
            var r = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = who, rowVersion = v });
            Assert.Equal((HttpStatusCode.BadRequest, "assignee_invalid"), (r.Status, Error(r.Body)));
        }
        Assert.Equal((0, 0), await CountsAsync(task.Id, FollowUpEventKinds.Assigned, AuditEventTypes.FollowUpAssigned));
    }

    [Fact]
    public async Task A_person_claims_a_task_for_themselves_and_claiming_it_again_changes_nothing()
    {
        var task = await SystemTaskAsync();
        var claimed = await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/claim", new { rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal(HttpStatusCode.OK, claimed.Status);
        Assert.Equal(_hygienist.UserId, TaskOf(claimed.Body).GetProperty("assigneeUserId").GetGuid());
        var again = await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/claim", new { rowVersion = (string?)null });
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal((1, 1), await CountsAsync(task.Id, FollowUpEventKinds.Assigned, AuditEventTypes.FollowUpAssigned));
        Assert.Equal(new[] { "Patient recall: schedule the next visit" }, Titles((await QueueAsync(_hygienist, "?filter=mine")).Body));
    }

    [Fact]
    public async Task Only_one_of_two_people_claiming_the_same_unassigned_task_at_once_gets_it()
    {
        var task = await SystemTaskAsync();
        var version = await VersionOfAsync(task.Id);
        var results = await Task.WhenAll(new[] { _hygienist, _dentist, _frontDesk, _manager }.Select(s => s.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/claim", new { rowVersion = version })));
        var winners = results.Where(r => r.Status == HttpStatusCode.OK).ToList();
        Assert.Single(winners);
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Equal((HttpStatusCode.Conflict, "task_changed"), (r.Status, Error(r.Body))));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.FollowUpTaskEvents.CountAsync(e => e.TaskId == task.Id && e.Kind == FollowUpEventKinds.Assigned));
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpAssigned));
        Assert.Equal(TaskOf(winners[0].Body).GetProperty("assigneeUserId").GetGuid(), (await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).AssigneeUserId);
    }

    [Fact]
    public async Task A_stale_or_missing_version_closed_tasks_and_unknown_tasks_are_refused_and_store_nothing()
    {
        var task = await SystemTaskAsync();
        var stale = await VersionOfAsync(task.Id);
        await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _hygienist.UserId, rowVersion = stale });
        var behind = await _manager.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _dentist.UserId, rowVersion = stale });
        Assert.Equal((HttpStatusCode.Conflict, "task_changed"), (behind.Status, Error(behind.Body)));
        var missing = await _manager.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _dentist.UserId });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (missing.Status, Error(missing.Body)));
        Assert.Equal("row_version_required", Error((await _manager.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _dentist.UserId, rowVersion = "not base64!" })).Body));

        await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/complete", new { note = (string?)null, rowVersion = await VersionOfAsync(task.Id) });
        var closed = await _manager.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _dentist.UserId, rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal((HttpStatusCode.Conflict, "task_closed"), (closed.Status, Error(closed.Body)));
        Assert.Equal("task_not_found", Error((await _manager.SendAsync(HttpMethod.Post, $"/api/follow-ups/{Guid.NewGuid()}/assign", new { assigneeUserId = _dentist.UserId, rowVersion = stale })).Body));
        Assert.Equal((1, 1), await CountsAsync(task.Id, FollowUpEventKinds.Assigned, AuditEventTypes.FollowUpAssigned));
    }

    [Fact]
    public async Task Reassigning_and_completing_at_once_one_wins_nothing_is_lost_and_the_history_agrees()
    {
        var task = await SystemTaskAsync();
        await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/claim", new { rowVersion = await VersionOfAsync(task.Id) });
        var version = await VersionOfAsync(task.Id);
        var assign = _manager.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/assign", new { assigneeUserId = _dentist.UserId, rowVersion = version });
        var complete = _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/complete", new { note = (string?)null, rowVersion = version });
        var results = await Task.WhenAll(assign, complete);
        Assert.Contains(HttpStatusCode.OK, results.Select(r => r.Status));
        Assert.True(results.Count(r => r.Status == HttpStatusCode.OK) is 1 or 2);                                      // assign first then complete (needs the new version, so 409), or complete first then assign refused: never both
        await using var db = _fixture.CreateContext();
        var final = await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        var events = await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == task.Id).Select(e => e.Kind).ToListAsync();
        if (final.Status == FollowUpStatuses.Completed) Assert.Equal(1, events.Count(k => k == FollowUpEventKinds.Completed));
        else Assert.Contains(FollowUpEventKinds.Reassigned, events);
        Assert.Equal(final.Status == FollowUpStatuses.Completed ? 1 : 0, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpCompleted));
        Assert.True(results.All(r => r.Status is HttpStatusCode.OK or HttpStatusCode.Conflict));
    }

    // ---------- priority and the appointment link ----------

    [Fact]
    public async Task Setting_the_priority_is_recorded_and_the_same_priority_again_is_a_quiet_replay()
    {
        var task = await SystemTaskAsync();
        var r = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/priority", new { priority = "Urgent", rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal("Urgent", Str(TaskOf(r.Body), "priority"));
        Assert.Equal((1, 1), await CountsAsync(task.Id, FollowUpEventKinds.PriorityChanged, AuditEventTypes.FollowUpPriorityChanged));
        var again = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/priority", new { priority = "Urgent", rowVersion = (string?)null });
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal((1, 1), await CountsAsync(task.Id, FollowUpEventKinds.PriorityChanged, AuditEventTypes.FollowUpPriorityChanged));
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("Critical")]
    [InlineData("")]
    public async Task A_priority_outside_the_four_names_is_refused(string priority)
    {
        var task = await SystemTaskAsync();
        var r = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/priority", new { priority, rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal((HttpStatusCode.BadRequest, "priority_invalid"), (r.Status, Error(r.Body)));
    }

    [Fact]
    public async Task A_stale_priority_change_is_a_409_and_stores_nothing()
    {
        var task = await SystemTaskAsync();
        var stale = await VersionOfAsync(task.Id);
        await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/priority", new { priority = "High", rowVersion = stale });
        var behind = await _manager.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/priority", new { priority = "Low", rowVersion = stale });
        Assert.Equal((HttpStatusCode.Conflict, "task_changed"), (behind.Status, Error(behind.Body)));
        await using var db = _fixture.CreateContext();
        Assert.Equal("High", (await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).Priority);
    }

    [Fact]
    public async Task An_appointment_of_the_same_patient_can_be_linked_and_unlinked_and_the_queue_shows_its_time()
    {
        var task = await SystemTaskAsync(patient: _ann);
        var appointment = await _s.ScheduleAsync(_s.Request(_ann, new DateTime(2027, 3, 2, 9, 0, 0)));
        var linked = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/appointment", new { appointmentId = appointment.Appointment.Id, rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal(HttpStatusCode.OK, linked.Status);
        Assert.Equal(appointment.Appointment.Id, TaskOf(linked.Body).GetProperty("appointmentId").GetGuid());
        var item = (await QueueAsync(_frontDesk)).Body.GetProperty("items")[0];
        Assert.Equal(appointment.Appointment.StartUtc, item.GetProperty("appointmentStartUtc").GetDateTimeOffset());
        Assert.True((await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/appointment", new { appointmentId = appointment.Appointment.Id, rowVersion = (string?)null })).Body.GetProperty("replayed").GetBoolean());

        var unlinked = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/appointment", new { appointmentId = (Guid?)null, rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal(JsonValueKind.Null, TaskOf(unlinked.Body).GetProperty("appointmentId").ValueKind);
        await using var db = _fixture.CreateContext();
        Assert.Equal(2, await db.FollowUpTaskEvents.CountAsync(e => e.TaskId == task.Id && e.Kind == FollowUpEventKinds.Edited));
    }

    [Fact]
    public async Task Another_patients_appointment_or_one_that_does_not_exist_cannot_be_linked()
    {
        var task = await SystemTaskAsync(patient: _ann);
        var bos = await _s.ScheduleAsync(_s.Request(_bo, new DateTime(2027, 3, 2, 10, 0, 0)));
        foreach (var id in new[] { bos.Appointment.Id, Guid.NewGuid() })
        {
            var r = await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/appointment", new { appointmentId = id, rowVersion = await VersionOfAsync(task.Id) });
            Assert.Equal((HttpStatusCode.BadRequest, "appointment_invalid"), (r.Status, Error(r.Body)));
        }
    }

    // ---------- permissions on the actions ----------

    [Fact]
    public async Task Billing_cannot_act_on_the_queue_and_an_action_without_a_csrf_token_changes_nothing()
    {
        var task = await SystemTaskAsync();
        var v = await VersionOfAsync(task.Id);
        foreach (var (path, body) in new (string, object)[] { ("assign", new { assigneeUserId = _hygienist.UserId, rowVersion = v }), ("claim", new { rowVersion = v }), ("priority", new { priority = "High", rowVersion = v }), ("appointment", new { appointmentId = (Guid?)null, rowVersion = v }) })
        {
            var denied = await _billing.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/{path}", body);
            Assert.Equal((HttpStatusCode.Forbidden, "permission_denied"), (denied.Status, Error(denied.Body)));
            Assert.NotEqual(HttpStatusCode.OK, (await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/{path}", body, csrf: false)).Status);
        }
        await using var db = _fixture.CreateContext();
        var after = await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal((null, FollowUpPriorities.Normal, null), (after.AssigneeUserId, after.Priority, after.AppointmentId));
    }

    [Fact]
    public async Task A_person_may_complete_a_system_made_task_and_its_history_says_who_and_the_owner_of_a_system_task_can_stay_unassigned_through_an_edit()
    {
        var task = await SystemTaskAsync();
        var edit = await _frontDesk.SendAsync(HttpMethod.Put, $"/api/follow-ups/{task.Id}", new { title = "Recall: call Ann", note = "Prefers mornings", dueAtUtc = task.DueAtUtc, assigneeUserId = (Guid?)null, recallIntervalDays = (int?)null, rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal(HttpStatusCode.OK, edit.Status);
        Assert.Equal(JsonValueKind.Null, TaskOf(edit.Body).GetProperty("assigneeUserId").ValueKind);
        var done = await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.Id}/complete", new { note = "Booked for March", rowVersion = await VersionOfAsync(task.Id) });
        Assert.Equal(HttpStatusCode.OK, done.Status);
        Assert.Equal((_hygienist.UserId, false), (TaskOf(done.Body).GetProperty("closedByUserId").GetGuid(), TaskOf(done.Body).GetProperty("closedBySystem").GetBoolean()));
        var history = (await _frontDesk.SendAsync(HttpMethod.Get, $"/api/follow-ups/{task.Id}")).Body.GetProperty("events").EnumerateArray().Select(e => Str(e, "kind")).ToArray();
        Assert.Equal(new[] { FollowUpEventKinds.Edited, FollowUpEventKinds.Completed }, history);   // the system's own creation entry is not in this test's hand-made task
    }

    // ---------- a patient's timeline ----------

    [Fact]
    public async Task A_patients_timeline_lists_what_happened_to_all_their_follow_ups_newest_first_and_nobody_elses()
    {
        var mine = await ManualAsync(_dentist, _hygienist.UserId, "Call about the crown", due: DateTimeOffset.UtcNow.AddDays(10));
        var id = mine.GetProperty("id").GetGuid();
        var system = await SystemTaskAsync(title: "Patient recall: schedule the next visit");
        await using (var db = _fixture.CreateContext())
        {
            db.FollowUpTaskEvents.Add(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = system.Id, Kind = FollowUpEventKinds.SystemCreated, ActorUserId = null, OccurredAtUtc = DateTimeOffset.UtcNow.AddHours(-2), Detail = "Recall due 2027-01-01 (180 days)" });
            await db.SaveChangesAsync();
        }
        await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{system.Id}/claim", new { rowVersion = await VersionOfAsync(system.Id) });
        await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/priority", new { priority = "High", rowVersion = Str(mine, "rowVersion") });
        await ManualAsync(_dentist, _hygienist.UserId, "Someone else's patient", patient: _bo);

        var r = await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups/timeline");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        var entries = r.Body.EnumerateArray().ToList();
        Assert.DoesNotContain("Someone else's patient", entries.Select(e => Str(e, "taskTitle")));
        Assert.Equal(new[] { "PriorityChanged", "Assigned", "Created", "SystemCreated" }, entries.Select(e => Str(e, "eventKind")).ToArray());      // newest first
        Assert.Equal(entries.OrderByDescending(e => e.GetProperty("occurredAtUtc").GetDateTimeOffset()).Select(e => Str(e, "eventKind")), entries.Select(e => Str(e, "eventKind")));
        var systemEntry = entries.Last();
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (systemEntry.GetProperty("actorUserId").ValueKind, systemEntry.GetProperty("actorName").ValueKind));   // the system's own entry has no person
        Assert.Equal(JsonValueKind.Null, systemEntry.GetProperty("detail").ValueKind);                                                    // only a due time is ever shown as detail
        Assert.Contains(entries, e => Str(e, "eventKind") == "Created" && e.GetProperty("detail").GetString()!.StartsWith("Due "));
        Assert.DoesNotContain(entries, e => e.GetProperty("detail").ValueKind == JsonValueKind.String && !e.GetProperty("detail").GetString()!.StartsWith("Due "));
    }

    [Fact]
    public async Task The_timeline_is_refused_to_billing_and_says_when_the_patient_does_not_exist()
    {
        var denied = await _billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups/timeline");
        Assert.Equal((HttpStatusCode.Forbidden, "permission_denied"), (denied.Status, Error(denied.Body)));
        var missing = await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/follow-ups/timeline");
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (missing.Status, Error(missing.Body)));
        Assert.Equal(0, (await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups/timeline")).Body.GetArrayLength());
    }

    // ---------- the recall settings ----------

    [Fact]
    public async Task The_office_manager_reads_and_changes_the_recall_settings_with_an_audit_entry_and_the_dentist_may_only_read_them()
    {
        var read = await _dentist.SendAsync(HttpMethod.Get, "/api/recall/settings");
        Assert.Equal((HttpStatusCode.OK, false, 180, 30, 24, 7), (read.Status, read.Body.GetProperty("enabled").GetBoolean(), read.Body.GetProperty("defaultIntervalDays").GetInt32(), read.Body.GetProperty("leadDays").GetInt32(), read.Body.GetProperty("maxVisitAgeMonths").GetInt32(), read.Body.GetProperty("dueSoonDays").GetInt32()));
        var version = Str(read.Body, "rowVersion");
        var denied = await _dentist.SendAsync(HttpMethod.Put, "/api/recall/settings", new { enabled = true, defaultIntervalDays = 120, leadDays = 21, maxVisitAgeMonths = 18, dueSoonDays = 5, rowVersion = version });
        Assert.Equal((HttpStatusCode.Forbidden, "permission_denied"), (denied.Status, Error(denied.Body)));
        Assert.Equal(HttpStatusCode.Forbidden, (await _billing.SendAsync(HttpMethod.Get, "/api/recall/settings")).Status);

        var changed = await _manager.SendAsync(HttpMethod.Put, "/api/recall/settings", new { enabled = true, defaultIntervalDays = 120, leadDays = 21, maxVisitAgeMonths = 18, dueSoonDays = 5, rowVersion = version });
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal((true, 120, 21, 18, 5, _manager.UserId), (changed.Body.GetProperty("enabled").GetBoolean(), changed.Body.GetProperty("defaultIntervalDays").GetInt32(), changed.Body.GetProperty("leadDays").GetInt32(), changed.Body.GetProperty("maxVisitAgeMonths").GetInt32(), changed.Body.GetProperty("dueSoonDays").GetInt32(), changed.Body.GetProperty("updatedByUserId").GetGuid()));
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.RecallSettingsChanged);
        Assert.Equal(_manager.UserId, audit.PerformedByUserAccountId);
        Assert.Contains("interval 120", audit.Details);

        // saying the same thing again is a quiet replay (no second audit entry), and a stale version is refused
        var same = await _manager.SendAsync(HttpMethod.Put, "/api/recall/settings", new { enabled = true, defaultIntervalDays = 120, leadDays = 21, maxVisitAgeMonths = 18, dueSoonDays = 5, rowVersion = version });
        Assert.Equal(HttpStatusCode.OK, same.Status);
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.RecallSettingsChanged));
        var stale = await _manager.SendAsync(HttpMethod.Put, "/api/recall/settings", new { enabled = true, defaultIntervalDays = 100, leadDays = 21, maxVisitAgeMonths = 18, dueSoonDays = 5, rowVersion = version });
        Assert.Equal((HttpStatusCode.Conflict, "settings_changed"), (stale.Status, Error(stale.Body)));
    }

    [Theory]
    [InlineData("defaultIntervalDays", 0)]
    [InlineData("defaultIntervalDays", 3651)]
    [InlineData("leadDays", -1)]
    [InlineData("leadDays", 366)]
    [InlineData("maxVisitAgeMonths", 0)]
    [InlineData("maxVisitAgeMonths", 121)]
    [InlineData("dueSoonDays", 0)]
    [InlineData("dueSoonDays", 31)]
    public async Task A_setting_outside_its_limits_is_refused_and_the_limits_themselves_are_accepted(string name, int bad)
    {
        var version = Str((await _manager.SendAsync(HttpMethod.Get, "/api/recall/settings")).Body, "rowVersion");
        object Body(int value) => new Dictionary<string, object?>
        {
            ["enabled"] = false, ["defaultIntervalDays"] = 180, ["leadDays"] = 30, ["maxVisitAgeMonths"] = 24, ["dueSoonDays"] = 7, ["rowVersion"] = version, [name] = value,
        };
        var refused = await _manager.SendAsync(HttpMethod.Put, "/api/recall/settings", Body(bad));
        Assert.Equal((HttpStatusCode.BadRequest, "settings_invalid"), (refused.Status, Error(refused.Body)));
        var missing = await _manager.SendAsync(HttpMethod.Put, "/api/recall/settings", new { enabled = false, defaultIntervalDays = 180, leadDays = 30, maxVisitAgeMonths = 24, dueSoonDays = 7 });
        Assert.Equal("row_version_required", Error(missing.Body));
        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.RecallSettingsChanged));
    }

    // ---------- a patient's own recall ----------

    [Fact]
    public async Task A_patient_never_seen_shows_why_they_are_not_due_and_follows_the_practice_default()
    {
        var r = await _dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/recall");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal((false, 180, "NoVisit"), (r.Body.GetProperty("hasPlan").GetBoolean(), r.Body.GetProperty("effectiveIntervalDays").GetInt32(), Str(r.Body, "verdict")));
        Assert.Equal("patient_not_found", Error((await _dentist.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/recall")).Body));
        Assert.Equal(HttpStatusCode.Forbidden, (await _billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/recall")).Status);
    }

    [Fact]
    public async Task A_patients_own_interval_can_be_set_changed_and_reset_with_versions_replays_and_audit_entries()
    {
        var set = await _hygienist.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = 90, optedOut = false, reason = "Gum disease: every three months", rowVersion = (string?)null });
        Assert.Equal(HttpStatusCode.OK, set.Status);
        Assert.Equal((true, 90, 90), (set.Body.GetProperty("hasPlan").GetBoolean(), set.Body.GetProperty("ownIntervalDays").GetInt32(), set.Body.GetProperty("effectiveIntervalDays").GetInt32()));
        var version = Str(set.Body, "rowVersion");

        var same = await _hygienist.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = 90, optedOut = false, reason = "Gum disease: every three months", rowVersion = (string?)null });
        Assert.Equal(HttpStatusCode.OK, same.Status);                                                                    // the same thing again needs no version
        var noVersion = await _hygienist.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = 60, optedOut = false, reason = (string?)null });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var changed = await _hygienist.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = 60, optedOut = false, reason = (string?)null, rowVersion = version });
        Assert.Equal(60, changed.Body.GetProperty("ownIntervalDays").GetInt32());
        var stale = await _hygienist.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = 30, optedOut = false, reason = (string?)null, rowVersion = version });
        Assert.Equal((HttpStatusCode.Conflict, "plan_changed"), (stale.Status, Error(stale.Body)));

        var reset = await _hygienist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/recall/reset", new { rowVersion = Str(changed.Body, "rowVersion") });
        Assert.Equal((false, 180), (reset.Body.GetProperty("hasPlan").GetBoolean(), reset.Body.GetProperty("effectiveIntervalDays").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await _hygienist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/recall/reset", new { rowVersion = (string?)null })).Status);   // resetting the default is a quiet replay

        await using var db = _fixture.CreateContext();
        Assert.Equal(2, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.RecallPlanChanged));
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.RecallPlanReset));
        Assert.Equal(0, await db.PatientRecallPlans.CountAsync());
    }

    [Fact]
    public async Task A_patient_can_be_opted_out_and_the_plan_must_mean_something_and_stay_inside_its_limits()
    {
        var out1 = await _frontDesk.SendAsync(HttpMethod.Put, $"/api/patients/{_bo}/recall", new { intervalDays = (int?)null, optedOut = true, reason = "Moved to another practice" });
        Assert.Equal((true, "OptedOut"), (out1.Body.GetProperty("optedOut").GetBoolean(), Str(out1.Body, "verdict")));
        foreach (var (days, optedOut, reason, code) in new (int?, bool, string?, string)[] { (null, false, null, "plan_empty"), (0, false, null, "interval_invalid"), (3651, false, null, "interval_invalid"), (90, false, new string('r', 201), "reason_invalid"), (90, false, "bell\u0007", "reason_invalid") })
        {
            var r = await _frontDesk.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = days, optedOut, reason, rowVersion = (string?)null });
            Assert.Equal((HttpStatusCode.BadRequest, code), (r.Status, Error(r.Body)));
        }
        foreach (var good in new[] { 1, 3650 }) Assert.Equal(HttpStatusCode.OK, (await _frontDesk.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = good, optedOut = false, reason = (string?)null, rowVersion = good == 1 ? null : await RecallVersionAsync(_ann) })).Status);
        Assert.Equal("patient_not_found", Error((await _frontDesk.SendAsync(HttpMethod.Put, $"/api/patients/{Guid.NewGuid()}/recall", new { intervalDays = 90, optedOut = false })).Body));
    }

    private async Task<string?> RecallVersionAsync(Guid patient) => (await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{patient}/recall")).Body.GetProperty("rowVersion") is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    [Fact]
    public async Task Two_people_setting_a_patients_first_plan_at_once_one_wins_and_the_other_is_told()
    {
        var results = await Task.WhenAll(new[] { _frontDesk, _hygienist, _dentist, _manager }.Select((s, i) => s.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/recall", new { intervalDays = 60 + i, optedOut = false, reason = (string?)null })));
        Assert.Contains(HttpStatusCode.OK, results.Select(r => r.Status));
        // a loser either collided with the winner's insert (plan_changed) or arrived after it and, having no version to send for a plan it had not seen, was asked for one (row_version_required): both mean "reload"
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.True(Error(r.Body) is "plan_changed" or "row_version_required", Error(r.Body)));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.PatientRecallPlans.CountAsync());
    }
}
