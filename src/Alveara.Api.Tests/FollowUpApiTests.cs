using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-009 through the real pipeline (real API, real SQL Server, cookie sessions): creating, listing, editing, completing and cancelling a patient's follow-up tasks. Authorization by role (billing sees nothing),
/// CSRF, the Idempotency-Key on a create, the row version on every change, bounded and validated input, the history events and the audit entry written with every change, the recall created exactly once on
/// completion, a stale or concurrent change answered 409 with nothing stored, and the rollback of the whole change when the audit entry cannot be written.
/// </summary>
public class FollowUpApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;
    private Guid _bo;
    private int _keys;
    private Session _dentist = null!;
    private Session _hygienist = null!;
    private Session _frontDesk = null!;
    private Session _billing = null!;
    private Session _admin = null!;

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
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private string Key() => $"fu-{Interlocked.Increment(ref _keys)}-{Guid.NewGuid():N}";
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static JsonElement TaskOf(JsonElement body) => body.GetProperty("task");

    private object NewBody(Guid? assignee = null, string title = "Call about the crown", string? note = "Ask how the temporary feels", DateTimeOffset? due = null, int? interval = null) =>
        new { title, note, dueAtUtc = due ?? DateTimeOffset.UtcNow.AddDays(7), assigneeUserId = assignee ?? _hygienist.UserId, recallIntervalDays = interval };

    private Task<(HttpStatusCode Status, JsonElement Body)> CreateAsync(Session s, object body, string? key = null, Guid? patient = null) =>
        s.SendAsync(HttpMethod.Post, $"/api/patients/{patient ?? _ann}/follow-ups", body, key ?? Key());

    private async Task<JsonElement> MakeAsync(Session? by = null, Guid? assignee = null, int? interval = null, DateTimeOffset? due = null, Guid? patient = null, string title = "Call about the crown")
    {
        var r = await CreateAsync(by ?? _dentist, NewBody(assignee, title: title, interval: interval, due: due), patient: patient);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return TaskOf(r.Body);
    }

    private object Edit(JsonElement task, string? title = null, string? note = null, DateTimeOffset? due = null, Guid? assignee = null, int? interval = null, string? version = null) => new
    {
        title = title ?? Str(task, "title"), note = note ?? (task.GetProperty("note").ValueKind == JsonValueKind.Null ? null : Str(task, "note")), dueAtUtc = due ?? task.GetProperty("dueAtUtc").GetDateTimeOffset(),
        assigneeUserId = assignee ?? task.GetProperty("assigneeUserId").GetGuid(), recallIntervalDays = interval ?? (task.GetProperty("recallIntervalDays").ValueKind == JsonValueKind.Null ? (int?)null : task.GetProperty("recallIntervalDays").GetInt32()),
        rowVersion = version ?? Str(task, "rowVersion"),
    };

    private async Task<(int Tasks, int Events, int Audit)> CountsAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await db.FollowUpTasks.CountAsync(), await db.FollowUpTaskEvents.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("FollowUp")));
    }

    // ---------- create and read ----------

    [Fact]
    public async Task A_dentist_creates_a_follow_up_for_a_hygienist_and_it_is_recorded_with_its_history_and_audit_entry()
    {
        var due = DateTimeOffset.UtcNow.AddDays(14);
        var created = await CreateAsync(_dentist, NewBody(due: due, interval: 180));
        Assert.Equal(HttpStatusCode.OK, created.Status);
        Assert.False(created.Body.GetProperty("replayed").GetBoolean());
        var task = TaskOf(created.Body);
        Assert.Equal(("Open", "Call about the crown", _ann, _hygienist.UserId, 180), (Str(task, "status"), Str(task, "title"), task.GetProperty("patientId").GetGuid(), task.GetProperty("assigneeUserId").GetGuid(), task.GetProperty("recallIntervalDays").GetInt32()));
        Assert.False(task.GetProperty("overdue").GetBoolean());

        await using var db = _fixture.CreateContext();
        var id = task.GetProperty("id").GetGuid();
        var row = await db.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal((_dentist.UserId, FollowUpStatuses.Open), (row.CreatedByUserId, row.Status));
        var events = await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == id).ToListAsync();
        Assert.Equal((FollowUpEventKinds.Created, _dentist.UserId), (events.Single().Kind, events.Single().ActorUserId));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.FollowUpCreated);
        Assert.Equal((_dentist.UserId, id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        Assert.DoesNotContain("crown", audit.Details);                                                                       // the audit entry names ids, never the title or note
    }

    [Fact]
    public async Task The_same_key_and_request_replays_quietly_and_the_same_key_with_another_request_is_refused()
    {
        var key = Key();
        var body = NewBody();
        var first = await CreateAsync(_dentist, body, key);
        var second = await CreateAsync(_dentist, body, key);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.True(second.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(TaskOf(first.Body).GetProperty("id").GetGuid(), TaskOf(second.Body).GetProperty("id").GetGuid());
        Assert.Equal((1, 1, 1), await CountsAsync());

        var other = await CreateAsync(_dentist, NewBody(title: "A different task"), key);
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (other.Status, Error(other.Body)));
        Assert.Equal((1, 1, 1), await CountsAsync());

        // another person using the same key text makes their own task
        Assert.Equal(HttpStatusCode.OK, (await CreateAsync(_frontDesk, body, key)).Status);
        Assert.Equal(2, (await CountsAsync()).Tasks);
    }

    [Fact]
    public async Task Concurrent_creates_with_the_same_key_make_exactly_one_task()
    {
        var key = Key();
        var body = NewBody();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateAsync(_dentist, body, key)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Single(results.Select(r => TaskOf(r.Body).GetProperty("id").GetGuid()).Distinct());
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_create_needs_an_idempotency_key_and_nothing_is_stored_without_one()
    {
        var r = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/follow-ups", NewBody());
        Assert.Equal((HttpStatusCode.BadRequest, "idempotency_key_required"), (r.Status, Error(r.Body)));
        var tooLong = await CreateAsync(_dentist, NewBody(), new string('k', 65));
        Assert.Equal("idempotency_key_required", Error(tooLong.Body));
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Open_tasks_are_listed_for_the_patient_and_for_the_responsible_person_due_first_and_closed_ones_on_request()
    {
        var late = await MakeAsync(due: DateTimeOffset.UtcNow.AddDays(30));
        var soon = await MakeAsync(due: DateTimeOffset.UtcNow.AddDays(2));
        var elsewhere = await MakeAsync(patient: _bo);
        var done = await MakeAsync(title: "Already done");
        Assert.Equal(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{done.GetProperty("id").GetGuid()}/complete", new { note = (string?)null, rowVersion = Str(done, "rowVersion") })).Status);

        var open = await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups");
        Assert.Equal(HttpStatusCode.OK, open.Status);
        var ids = open.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(new[] { soon.GetProperty("id").GetGuid(), late.GetProperty("id").GetGuid() }, ids);                       // this patient only, open only, soonest due first
        Assert.DoesNotContain(elsewhere.GetProperty("id").GetGuid(), ids);

        var all = await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups?status=all");
        Assert.Equal(3, all.Body.GetProperty("count").GetInt32());
        var closed = await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups?status=closed");
        Assert.Equal(done.GetProperty("id").GetGuid(), closed.Body.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal("status_invalid", Error((await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups?status=bogus")).Body));

        var mine = await _hygienist.SendAsync(HttpMethod.Get, "/api/follow-ups/mine");
        Assert.Equal(3, mine.Body.GetProperty("count").GetInt32());                                                              // the three open ones across both patients are the hygienist's; the completed one is not listed
        Assert.Equal(0, (await _dentist.SendAsync(HttpMethod.Get, "/api/follow-ups/mine")).Body.GetProperty("count").GetInt32());
        Assert.Equal("patient_not_found", Error((await _frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/follow-ups")).Body));
    }

    [Fact]
    public async Task A_task_in_the_past_is_overdue_and_a_detail_carries_its_history_with_who_and_when()
    {
        var task = await MakeAsync(due: DateTimeOffset.UtcNow.AddDays(-2));
        Assert.True(task.GetProperty("overdue").GetBoolean());
        var id = task.GetProperty("id").GetGuid();
        var updated = await _hygienist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, title: "Call about the crown and the cleaning"));
        Assert.Equal(HttpStatusCode.OK, updated.Status);

        var detail = await _frontDesk.SendAsync(HttpMethod.Get, $"/api/follow-ups/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.Status);
        var events = detail.Body.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(new[] { FollowUpEventKinds.Created, FollowUpEventKinds.Edited }, events.Select(e => Str(e, "kind")).ToArray());
        Assert.Equal(new[] { _dentist.UserId, _hygienist.UserId }, events.Select(e => e.GetProperty("actorUserId").GetGuid()).ToArray());
        Assert.All(events, e => Assert.True(e.GetProperty("occurredAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal("task_not_found", Error((await _frontDesk.SendAsync(HttpMethod.Get, $"/api/follow-ups/{Guid.NewGuid()}")).Body));
    }

    // ---------- validation ----------

    [Theory]
    [InlineData("", "title_invalid")]
    [InlineData("   ", "title_invalid")]
    [InlineData("two\nlines", "title_invalid")]
    [InlineData("bell\u0007", "title_invalid")]
    public async Task A_title_must_be_one_short_line_of_text(string title, string expected)
    {
        var r = await CreateAsync(_dentist, NewBody(title: title));
        Assert.Equal((HttpStatusCode.BadRequest, expected), (r.Status, Error(r.Body)));
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Text_due_dates_intervals_and_assignees_are_validated_at_their_bounds_and_nothing_is_stored_on_a_refusal()
    {
        Assert.Equal("title_invalid", Error((await CreateAsync(_dentist, NewBody(title: new string('t', FollowUpRules.TitleMaxLength + 1)))).Body));
        Assert.Equal(HttpStatusCode.OK, (await CreateAsync(_dentist, NewBody(title: new string('t', FollowUpRules.TitleMaxLength)))).Status);                  // exactly the bound
        Assert.Equal("note_invalid", Error((await CreateAsync(_dentist, new { title = "x", note = new string('n', FollowUpRules.NoteMaxLength + 1), dueAtUtc = DateTimeOffset.UtcNow.AddDays(1), assigneeUserId = _hygienist.UserId })).Body));
        Assert.Equal(HttpStatusCode.OK, (await CreateAsync(_dentist, new { title = "x", note = new string('n', FollowUpRules.NoteMaxLength), dueAtUtc = DateTimeOffset.UtcNow.AddDays(1), assigneeUserId = _hygienist.UserId })).Status);
        Assert.Equal("note_invalid", Error((await CreateAsync(_dentist, new { title = "x", note = "bell\u0007", dueAtUtc = DateTimeOffset.UtcNow.AddDays(1), assigneeUserId = _hygienist.UserId })).Body));

        Assert.Equal("due_required", Error((await CreateAsync(_dentist, new { title = "x", assigneeUserId = _hygienist.UserId })).Body));
        Assert.Equal("due_out_of_range", Error((await CreateAsync(_dentist, NewBody(due: DateTimeOffset.UtcNow.AddDays(-FollowUpRules.MaxPastDays - 2)))).Body));
        Assert.Equal("due_out_of_range", Error((await CreateAsync(_dentist, NewBody(due: DateTimeOffset.UtcNow.AddDays(FollowUpRules.MaxFutureDays + 2)))).Body));
        Assert.Equal(HttpStatusCode.OK, (await CreateAsync(_dentist, NewBody(due: DateTimeOffset.UtcNow.AddDays(-FollowUpRules.MaxPastDays + 2)))).Status);             // back-dating within the window is allowed

        foreach (var bad in new[] { 0, -5, FollowUpRules.MaxRecallIntervalDays + 1 })
            Assert.Equal("interval_invalid", Error((await CreateAsync(_dentist, NewBody(interval: bad))).Body));
        foreach (var good in new[] { 1, FollowUpRules.MaxRecallIntervalDays })
            Assert.Equal(HttpStatusCode.OK, (await CreateAsync(_dentist, NewBody(interval: good))).Status);

        Assert.Equal("assignee_invalid", Error((await CreateAsync(_dentist, new { title = "x", dueAtUtc = DateTimeOffset.UtcNow.AddDays(1) })).Body));                          // nobody named
        Assert.Equal("assignee_invalid", Error((await CreateAsync(_dentist, NewBody(assignee: Guid.NewGuid()))).Body));                                                        // not an account
        Assert.Equal("assignee_invalid", Error((await CreateAsync(_dentist, NewBody(assignee: _billing.UserId))).Body));                                                       // billing cannot see follow-ups
        Assert.Equal("patient_not_found", Error((await CreateAsync(_dentist, NewBody(), patient: Guid.NewGuid())).Body));
        Assert.Equal(5, (await CountsAsync()).Tasks);                                                                                                                          // only the five valid ones: the two bounds of the title and note, the back-dated one and the two interval bounds
    }

    [Fact]
    public async Task A_disabled_account_cannot_be_made_responsible()
    {
        var off = await _api.SessionAsync("Dentist");
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{off.UserId}/enabled", new { enabled = false });
        var r = await CreateAsync(_dentist, NewBody(assignee: off.UserId));
        Assert.Equal((HttpStatusCode.BadRequest, "assignee_invalid"), (r.Status, Error(r.Body)));
    }

    // ---------- authorization ----------

    [Fact]
    public async Task Billing_sees_and_changes_nothing_and_the_refusal_is_the_known_permission_denied_body()
    {
        var task = await MakeAsync();
        var id = task.GetProperty("id").GetGuid();
        var denied = new[]
        {
            await CreateAsync(_billing, NewBody()),
            await _billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/follow-ups"),
            await _billing.SendAsync(HttpMethod.Get, "/api/follow-ups/mine"),
            await _billing.SendAsync(HttpMethod.Get, $"/api/follow-ups/{id}"),
            await _billing.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, title: "Changed")),
            await _billing.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(task, "rowVersion") }),
            await _billing.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/cancel", new { note = "x", rowVersion = Str(task, "rowVersion") }),
        };
        Assert.All(denied, r => Assert.Equal(HttpStatusCode.Forbidden, r.Status));
        Assert.All(denied, r => Assert.Equal("permission_denied", Error(r.Body)));
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_caller_who_only_views_follow_ups_can_read_but_not_write_once_the_manage_permission_is_taken_away()
    {
        var task = await MakeAsync();
        var id = task.GetProperty("id").GetGuid();
        var v = (await _admin.SendAsync(HttpMethod.Get, "/api/permissions/effective")).Body.GetProperty("version").GetInt64();
        var revoke = await _admin.SendAsync(HttpMethod.Put, "/api/permissions/overrides", new { role = "FrontDesk", permission = "ManageFollowUps", effect = "Revoke", reason = "Front desk only reads follow-ups", expectedVersion = v, confirmed = true }, Key());
        Assert.Equal(HttpStatusCode.OK, revoke.Status);
        Assert.Equal(HttpStatusCode.OK, (await _frontDesk.SendAsync(HttpMethod.Get, $"/api/follow-ups/{id}")).Status);                                              // the very next request
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(_frontDesk, NewBody())).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _frontDesk.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(task, "rowVersion") })).Status);
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_or_a_session_is_refused_and_nothing_changes()
    {
        var task = await MakeAsync();
        var id = task.GetProperty("id").GetGuid();
        Assert.NotEqual(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/follow-ups", NewBody(), Key(), csrf: false)).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, title: "Changed"), csrf: false)).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(task, "rowVersion") }, csrf: false)).Status);
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/follow-ups/{id}")).StatusCode);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    // ---------- edit ----------

    [Fact]
    public async Task An_edit_records_what_changed_and_by_whom_and_a_stale_version_is_a_409_that_stores_nothing()
    {
        var task = await MakeAsync();
        var id = task.GetProperty("id").GetGuid();
        var newDue = DateTimeOffset.UtcNow.AddDays(21);
        var edited = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, title: "Call and book the crown fitting", due: newDue, assignee: _frontDesk.UserId));
        Assert.Equal(HttpStatusCode.OK, edited.Status);
        Assert.Equal(_frontDesk.UserId, TaskOf(edited.Body).GetProperty("assigneeUserId").GetGuid());
        await using (var db = _fixture.CreateContext())
        {
            var kinds = (await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == id).ToListAsync()).Select(e => e.Kind).OrderBy(k => k).ToArray();
            Assert.Equal(new[] { FollowUpEventKinds.Created, FollowUpEventKinds.DueChanged, FollowUpEventKinds.Edited, FollowUpEventKinds.Reassigned }.OrderBy(k => k).ToArray(), kinds);
            Assert.Equal(2, await db.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("FollowUp")));
        }

        var before = await CountsAsync();
        var stale = await _hygienist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, title: "From an old screen"));            // the version from before the edit
        Assert.Equal((HttpStatusCode.Conflict, "task_changed"), (stale.Status, Error(stale.Body)));
        Assert.Equal(before, await CountsAsync());
        await using var check = _fixture.CreateContext();
        Assert.Equal("Call and book the crown fitting", (await check.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == id)).Title);
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_is_refused_and_needs_a_version()
    {
        var task = await MakeAsync();
        var id = task.GetProperty("id").GetGuid();
        var same = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task));
        Assert.Equal((HttpStatusCode.Conflict, "no_change"), (same.Status, Error(same.Body)));
        var noVersion = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", new { title = "x", dueAtUtc = DateTimeOffset.UtcNow.AddDays(1), assigneeUserId = _hygienist.UserId });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var junk = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, title: "x", version: "not base64!"));
        Assert.Equal("row_version_required", Error(junk.Body));
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Editing_the_date_of_a_task_whose_assignee_has_since_left_still_works_but_reassigning_to_a_disabled_account_does_not()
    {
        var leaver = await _api.SessionAsync("Hygienist");
        var task = await MakeAsync(assignee: leaver.UserId);
        var id = task.GetProperty("id").GetGuid();
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{leaver.UserId}/enabled", new { enabled = false });
        var moved = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, due: DateTimeOffset.UtcNow.AddDays(30)));
        Assert.Equal(HttpStatusCode.OK, moved.Status);
        var again = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(TaskOf(moved.Body), assignee: leaver.UserId, title: "Renamed"));
        Assert.Equal(HttpStatusCode.OK, again.Status);                                                                                       // unchanged assignee, so not re-checked
        var reassign = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(TaskOf(again.Body), assignee: _billing.UserId));
        Assert.Equal((HttpStatusCode.BadRequest, "assignee_invalid"), (reassign.Status, Error(reassign.Body)));
    }

    // ---------- complete ----------

    [Fact]
    public async Task Completing_a_task_updates_its_history_with_who_and_when_and_closes_it_for_good()
    {
        var task = await MakeAsync();
        var id = task.GetProperty("id").GetGuid();
        var done = await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { note = "Spoke to the patient; all well", rowVersion = Str(task, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, done.Status);
        var closed = TaskOf(done.Body);
        Assert.Equal(("Completed", _hygienist.UserId, "Spoke to the patient; all well"), (Str(closed, "status"), closed.GetProperty("closedByUserId").GetGuid(), Str(closed, "closureNote")));
        Assert.True(closed.GetProperty("closedAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5));

        var detail = (await _frontDesk.SendAsync(HttpMethod.Get, $"/api/follow-ups/{id}")).Body;
        var last = detail.GetProperty("events").EnumerateArray().Last();
        Assert.Equal((FollowUpEventKinds.Completed, _hygienist.UserId), (Str(last, "kind"), last.GetProperty("actorUserId").GetGuid()));
        await using var db = _fixture.CreateContext();
        Assert.Contains(await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.FollowUpCompleted).ToListAsync(), a => a.PerformedByUserAccountId == _hygienist.UserId && a.TargetUserAccountId == id);

        // final: no edit, no cancel, and another person cannot complete it again
        var edit = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(closed, title: "Too late"));
        Assert.Equal((HttpStatusCode.Conflict, "task_closed"), (edit.Status, Error(edit.Body)));
        var cancel = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/cancel", new { note = "x", rowVersion = Str(closed, "rowVersion") });
        Assert.Equal("task_closed", Error(cancel.Body));
        var other = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(closed, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "task_closed"), (other.Status, Error(other.Body)));
    }

    [Fact]
    public async Task Completing_twice_the_same_way_by_the_same_person_is_a_quiet_replay_that_changes_nothing()
    {
        var task = await MakeAsync(interval: 90);
        var id = task.GetProperty("id").GetGuid();
        var body = new { note = (string?)null, rowVersion = Str(task, "rowVersion") };
        var first = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", body);
        var again = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", body);
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(first.Body.GetProperty("recallTaskId").GetGuid(), again.Body.GetProperty("recallTaskId").GetGuid());
        var counts = await CountsAsync();
        Assert.Equal(2, counts.Tasks);                                                                                       // the task and its one recall
    }

    [Fact]
    public async Task Two_people_completing_the_same_task_at_once_one_wins_and_the_other_is_told_so()
    {
        var task = await MakeAsync(interval: 30);
        var id = task.GetProperty("id").GetGuid();
        var body = new { note = (string?)null, rowVersion = Str(task, "rowVersion") };
        var results = await Task.WhenAll(new[] { _dentist, _hygienist, _frontDesk, _dentist, _hygienist, _frontDesk }.Select(s => s.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", body)));
        // each person who got in is either the one that completed it, or a replay of their own, or told it is already closed
        Assert.All(results, r => Assert.True(r.Status == HttpStatusCode.OK || (r.Status == HttpStatusCode.Conflict && Error(r.Body) is "task_closed" or "task_changed"), $"{r.Status}"));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.FollowUpTaskEvents.CountAsync(e => e.TaskId == id && e.Kind == FollowUpEventKinds.Completed));      // completed exactly once
        Assert.Equal(1, await db.FollowUpTasks.CountAsync(t => t.RecallOfTaskId == id));                                                // and one recall
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpCompleted));
    }

    [Fact]
    public async Task A_stale_version_cannot_complete_a_task_that_was_edited_meanwhile()
    {
        var task = await MakeAsync();
        var id = task.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{id}", Edit(task, title: "Edited meanwhile"))).Status);
        var before = await CountsAsync();
        var stale = await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(task, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "task_changed"), (stale.Status, Error(stale.Body)));
        Assert.Equal(before, await CountsAsync());
        var noVersion = await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { note = "x" });
        Assert.Equal("row_version_required", Error(noVersion.Body));
    }

    [Fact]
    public async Task Completing_a_task_with_a_recall_interval_creates_the_next_one_once_with_the_right_due_date_assignee_and_links()
    {
        var task = await MakeAsync(interval: 180);
        var id = task.GetProperty("id").GetGuid();
        var done = await _hygienist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { note = "Cleaning done", rowVersion = Str(task, "rowVersion") });
        var recallId = done.Body.GetProperty("recallTaskId").GetGuid();
        var detail = (await _frontDesk.SendAsync(HttpMethod.Get, $"/api/follow-ups/{recallId}")).Body.GetProperty("task");
        Assert.Equal(("Open", id, 180, _hygienist.UserId), (Str(detail, "status"), detail.GetProperty("recallOfTaskId").GetGuid(), detail.GetProperty("recallIntervalDays").GetInt32(), detail.GetProperty("assigneeUserId").GetGuid()));
        Assert.InRange((detail.GetProperty("dueAtUtc").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalDays, 179.9, 180.1);
        Assert.Equal(recallId, TaskOf((await _frontDesk.SendAsync(HttpMethod.Get, $"/api/follow-ups/{id}")).Body).GetProperty("recallTaskId").GetGuid());

        await using var db = _fixture.CreateContext();
        Assert.Contains(await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == id).ToListAsync(), e => e.Kind == FollowUpEventKinds.RecallCreated);
        Assert.Equal(2, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpCreated));                        // the first task and its recall
    }

    [Fact]
    public async Task A_recall_goes_to_the_person_completing_when_the_original_assignee_can_no_longer_receive_it()
    {
        var leaver = await _api.SessionAsync("Hygienist");
        var task = await MakeAsync(assignee: leaver.UserId, interval: 60);
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{leaver.UserId}/enabled", new { enabled = false });
        var done = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{task.GetProperty("id").GetGuid()}/complete", new { rowVersion = Str(task, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, done.Status);
        var recall = (await _dentist.SendAsync(HttpMethod.Get, $"/api/follow-ups/{done.Body.GetProperty("recallTaskId").GetGuid()}")).Body.GetProperty("task");
        Assert.Equal(_dentist.UserId, recall.GetProperty("assigneeUserId").GetGuid());                                                      // never an unowned recall
    }

    // ---------- cancel ----------

    [Fact]
    public async Task Cancelling_needs_a_reason_records_who_and_when_and_creates_no_recall()
    {
        var task = await MakeAsync(interval: 90);
        var id = task.GetProperty("id").GetGuid();
        var noReason = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/cancel", new { note = "  ", rowVersion = Str(task, "rowVersion") });
        Assert.Equal((HttpStatusCode.BadRequest, "note_required"), (noReason.Status, Error(noReason.Body)));
        var cancelled = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/cancel", new { note = "Patient moved away", rowVersion = Str(task, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, cancelled.Status);
        Assert.Equal(("Cancelled", "Patient moved away"), (Str(TaskOf(cancelled.Body), "status"), Str(TaskOf(cancelled.Body), "closureNote")));
        var again = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/cancel", new { note = "Patient moved away", rowVersion = Str(task, "rowVersion") });
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.FollowUpTasks.CountAsync());                                                                                 // a cancelled task makes no recall
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.FollowUpCancelled));
        var complete = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(task, "rowVersion") });
        Assert.Equal("task_closed", Error(complete.Body));
    }

    // ---------- audit failure ----------

    [Fact]
    public async Task When_the_audit_entry_cannot_be_written_nothing_at_all_is_stored_for_a_create_a_completion_and_its_recall()
    {
        var task = await MakeAsync(interval: 45);
        var id = task.GetProperty("id").GetGuid();
        var before = await CountsAsync();
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_TestRefuseFollowUpAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType LIKE 'FollowUp%') THROW 59996, 'audit down', 1;");
        try
        {
            var create = await CreateAsync(_dentist, NewBody(title: "Cannot be audited"));
            Assert.True((int)create.Status >= 500);
            var complete = await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(task, "rowVersion") });
            Assert.True((int)complete.Status >= 500);
        }
        finally
        {
            await using var db = _fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_TestRefuseFollowUpAudit");
        }
        Assert.Equal(before, await CountsAsync());
        await using var check = _fixture.CreateContext();
        Assert.Equal(FollowUpStatuses.Open, (await check.FollowUpTasks.AsNoTracking().SingleAsync(t => t.Id == id)).Status);              // still open, and no recall was made
        // and once the audit works again the same completion succeeds
        Assert.Equal(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Post, $"/api/follow-ups/{id}/complete", new { rowVersion = Str(task, "rowVersion") })).Status);
    }
}
