using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Controllers;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N015 at the HTTP boundary, against a real API, real SQL Server and a real signed-in session per role: sending with an idempotency key, validation, the rate limit, who can read what (a person who is
/// not a participant gets the same 404 as a missing message), marking read and hiding, the patient link resolved by the server, the administrator's metadata history, the audited break-glass action, and the
/// cursor-driven consumer (replay-safe, permission-at-processing-time). The consumer's background loop is switched off by the harness so the tests drive it.
/// </summary>
public class NotificationApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private readonly CapturingLoggerProvider _logs = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture) { ConfigureLogging = b => b.AddProvider(_logs) };
        await _s.ArrangeAsync();
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static string Error(JsonElement e) => Str(e, "error");
    private static string Key() => $"k-{Guid.NewGuid():N}";

    private static object Msg(Session to, string subject = "Room 3", string body = "Please set up room 3 for the 2 pm crown.", string priority = "Normal", Guid? patient = null, Guid? replyTo = null) =>
        new { recipientUserIds = new[] { to.UserId }, subject, body, priority, relatedPatientId = patient, inReplyToMessageId = replyTo };

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(Session by, object body, string? key = "default") =>
        await by.SendAsync(HttpMethod.Post, "/api/messages", body, key == "default" ? Key() : key);

    private async Task<Guid> SentAsync(Session by, Session to, string subject = "Room 3", string body = "Please set up room 3 for the 2 pm crown.", string priority = "Normal", Guid? patient = null, Guid? replyTo = null)
    {
        var r = await SendAsync(by, Msg(to, subject, body, priority, patient, replyTo));
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body.GetProperty("message").GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> SummaryAsync(Session s) => (await s.SendAsync(HttpMethod.Get, "/api/notifications/summary")).Body;

    // ---------- sending ----------

    [Fact]
    public async Task A_message_reaches_its_named_recipient_and_the_sender_sees_when_it_was_read()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var id = await SentAsync(dentist, desk);

        Assert.Equal(1, (await SummaryAsync(desk)).GetProperty("unread").GetInt32());
        var list = await desk.SendAsync(HttpMethod.Get, "/api/notifications?status=unread");
        var item = list.Body.GetProperty("items")[0];
        Assert.Equal(("message.received", "Room 3"), (Str(item, "kind"), Str(item, "subject")));

        var opened = await desk.SendAsync(HttpMethod.Get, $"/api/messages/{id}");
        Assert.Equal(HttpStatusCode.OK, opened.Status);
        Assert.Equal("Please set up room 3 for the 2 pm crown.", Str(opened.Body, "body"));
        Assert.Equal(0, (await SummaryAsync(desk)).GetProperty("unread").GetInt32());

        var sent = await dentist.SendAsync(HttpMethod.Get, $"/api/messages/{id}");
        Assert.NotEqual(JsonValueKind.Null, sent.Body.GetProperty("recipients")[0].GetProperty("readAtUtc").ValueKind);
    }

    [Fact]
    public async Task Sending_twice_with_the_same_key_creates_one_message_and_a_changed_body_with_that_key_is_refused()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var key = Key();
        var first = await SendAsync(dentist, Msg(desk), key);
        var again = await SendAsync(dentist, Msg(desk), key);
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.Status, again.Status));
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(first.Body.GetProperty("message").GetProperty("id").GetGuid(), again.Body.GetProperty("message").GetProperty("id").GetGuid());
        Assert.Equal(1, (await SummaryAsync(desk)).GetProperty("unread").GetInt32());

        var changed = await SendAsync(dentist, Msg(desk, body: "Something else."), key);
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (changed.Status, Error(changed.Body)));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.SenderUserId == dentist.UserId));
    }

    [Fact]
    public async Task A_bad_send_is_refused_with_named_problems_and_saves_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        Assert.Equal("idempotency_key_required", Error((await SendAsync(dentist, Msg(desk), null)).Body));

        var empty = await SendAsync(dentist, Msg(desk, body: "   \r\n"));
        Assert.Equal((HttpStatusCode.BadRequest, "message_invalid"), (empty.Status, Error(empty.Body)));
        Assert.Contains(empty.Body.GetProperty("problems").EnumerateArray(), p => Str(p, "field") == "body");

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(dentist, Msg(desk, subject: new string('s', 121)))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(dentist, Msg(desk, body: new string('b', 2001)))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(dentist, Msg(desk, priority: "Panic"))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(dentist, new { recipientUserIds = Array.Empty<Guid>(), subject = "x", body = "y", priority = "Normal" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(dentist, new { recipientUserIds = new[] { Guid.NewGuid() }, subject = "x", body = "y", priority = "Normal" })).Status);

        // the limits themselves are allowed
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(dentist, Msg(desk, subject: new string('s', 120), body: new string('b', 2000)))).Status);
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.SenderUserId == dentist.UserId));
    }

    [Fact]
    public async Task Sending_needs_a_session_and_a_csrf_token()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var anonymous = _api.Factory.CreateClient();
        var response = await anonymous.PostAsync("/api/messages", System.Net.Http.Json.JsonContent.Create(Msg(desk)));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var noCsrf = await dentist.SendAsync(HttpMethod.Post, "/api/messages", Msg(desk), Key(), csrf: false);
        Assert.NotEqual(HttpStatusCode.Created, noCsrf.Status);
        Assert.Equal(0, (await SummaryAsync(desk)).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task The_sender_is_limited_to_thirty_messages_a_minute()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        for (var i = 0; i < MessageRules.MaxMessagesPerMinute; i++)
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(dentist, Msg(desk, subject: $"m{i}"))).Status);
        var over = await SendAsync(dentist, Msg(desk, subject: "one too many"));
        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (over.Status, Error(over.Body)));
    }

    // ---------- reading, hiding, privacy ----------

    [Fact]
    public async Task Someone_who_is_not_a_participant_gets_the_same_not_found_as_a_missing_message()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var outsider = await _api.SessionAsync("Billing");
        var id = await SentAsync(dentist, desk);
        var theirs = await outsider.SendAsync(HttpMethod.Get, $"/api/messages/{id}");
        var missing = await outsider.SendAsync(HttpMethod.Get, $"/api/messages/{Guid.NewGuid()}");
        Assert.Equal((HttpStatusCode.NotFound, "message_not_found"), (theirs.Status, Error(theirs.Body)));
        Assert.Equal((missing.Status, Error(missing.Body)), (theirs.Status, Error(theirs.Body)));

        var thread = await dentist.SendAsync(HttpMethod.Get, $"/api/messages/{id}");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(HttpMethod.Get, $"/api/messages/threads/{Str(thread.Body, "threadId")}")).Status);
        Assert.Empty((await outsider.SendAsync(HttpMethod.Get, "/api/messages?box=inbox")).Body.GetProperty("items").EnumerateArray());
        Assert.Equal(0, (await SummaryAsync(outsider)).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task An_unread_notification_cannot_be_hidden_and_a_read_one_is_hidden_once_and_never_deleted()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        await SentAsync(dentist, desk);
        var id = (await desk.SendAsync(HttpMethod.Get, "/api/notifications?status=unread")).Body.GetProperty("items")[0].GetProperty("id").GetGuid();

        var early = await desk.SendAsync(HttpMethod.Delete, $"/api/notifications/{id}");
        Assert.Equal((HttpStatusCode.Conflict, "notification_unread"), (early.Status, Error(early.Body)));

        Assert.Equal(1, (await desk.SendAsync(HttpMethod.Post, $"/api/notifications/{id}/read")).Body.GetProperty("changed").GetInt32());
        Assert.Equal(0, (await desk.SendAsync(HttpMethod.Post, $"/api/notifications/{id}/read")).Body.GetProperty("changed").GetInt32());
        Assert.Equal(1, (await desk.SendAsync(HttpMethod.Delete, $"/api/notifications/{id}")).Body.GetProperty("changed").GetInt32());
        Assert.Equal(0, (await desk.SendAsync(HttpMethod.Delete, $"/api/notifications/{id}")).Body.GetProperty("changed").GetInt32());

        await using var db = _fixture.CreateContext();
        var row = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.NotNull(row.DeletedAtUtc);
        Assert.True(row.ReadAtUtc >= row.CreatedAtUtc);
    }

    [Fact]
    public async Task One_person_cannot_read_or_hide_another_persons_notification()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var outsider = await _api.SessionAsync("Billing");
        await SentAsync(dentist, desk);
        var id = (await desk.SendAsync(HttpMethod.Get, "/api/notifications?status=unread")).Body.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(HttpMethod.Post, $"/api/notifications/{id}/read")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.SendAsync(HttpMethod.Delete, $"/api/notifications/{id}")).Status);
        Assert.Equal(1, (await SummaryAsync(desk)).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task Marking_all_read_and_clearing_history_touch_only_the_callers_own_entries()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var billing = await _api.SessionAsync("Billing");
        await SentAsync(dentist, desk, "a");
        await SentAsync(dentist, desk, "b");
        await SentAsync(dentist, billing, "c");
        Assert.Equal(2, (await desk.SendAsync(HttpMethod.Post, "/api/notifications/read-all")).Body.GetProperty("changed").GetInt32());
        Assert.Equal(2, (await desk.SendAsync(HttpMethod.Delete, "/api/notifications/history")).Body.GetProperty("changed").GetInt32());
        Assert.Equal(0, (await desk.SendAsync(HttpMethod.Delete, "/api/notifications/history")).Body.GetProperty("changed").GetInt32());
        Assert.Equal(1, (await SummaryAsync(billing)).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task Only_important_and_urgent_messages_raise_the_banner_counts()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        await SentAsync(dentist, desk, priority: "Normal");
        await SentAsync(dentist, desk, priority: "Important");
        await SentAsync(dentist, desk, priority: "Urgent");
        var s = await SummaryAsync(desk);
        Assert.Equal((3, 1, 1), (s.GetProperty("unread").GetInt32(), s.GetProperty("unreadImportant").GetInt32(), s.GetProperty("unreadUrgent").GetInt32()));
    }

    [Fact]
    public async Task A_reply_must_come_from_a_participant_and_joins_the_thread()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var outsider = await _api.SessionAsync("Billing");
        var first = await SentAsync(dentist, desk);
        var reply = await SentAsync(desk, dentist, "Re: Room 3", "Done.", replyTo: first);
        var thread = Str((await dentist.SendAsync(HttpMethod.Get, $"/api/messages/{first}")).Body, "threadId");
        Assert.Equal(2, (await dentist.SendAsync(HttpMethod.Get, $"/api/messages/threads/{thread}")).Body.GetArrayLength());
        Assert.NotEqual(HttpStatusCode.Created, (await SendAsync(outsider, Msg(dentist, replyTo: first))).Status);
        Assert.NotEqual(Guid.Empty, reply);
    }

    // ---------- the patient link ----------

    [Fact]
    public async Task A_patient_link_is_resolved_by_the_server_and_no_list_carries_the_patient_id()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var patient = await _s.PatientAsync();
        var id = await SentAsync(dentist, desk, patient: patient);

        var inbox = await desk.SendAsync(HttpMethod.Get, "/api/notifications?status=unread");
        var list = await desk.SendAsync(HttpMethod.Get, "/api/messages?box=inbox");
        var summary = await SummaryAsync(desk);
        foreach (var json in new[] { inbox.Body.GetRawText(), list.Body.GetRawText(), summary.GetRawText() }) Assert.DoesNotContain(patient.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.True(inbox.Body.GetProperty("items")[0].GetProperty("hasPatientLink").GetBoolean());

        var link = await desk.SendAsync(HttpMethod.Get, $"/api/messages/{id}/patient-link");
        Assert.Equal($"/patients/{patient}", Str(link.Body, "path"));
        var notificationId = inbox.Body.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await desk.SendAsync(HttpMethod.Get, $"/api/notifications/{notificationId}/patient-link")).Status);
    }

    [Fact]
    public async Task A_missing_link_and_a_foreign_message_reveal_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var outsider = await _api.SessionAsync("Billing");
        var plain = await SentAsync(dentist, desk);
        var denied = await desk.SendAsync(HttpMethod.Get, $"/api/messages/{plain}/patient-link");
        Assert.Equal((HttpStatusCode.Forbidden, "patient_access_denied"), (denied.Status, Error(denied.Body)));
        var foreign = await outsider.SendAsync(HttpMethod.Get, $"/api/messages/{plain}/patient-link");
        Assert.Equal((HttpStatusCode.NotFound, "message_not_found"), (foreign.Status, Error(foreign.Body)));
    }

    // ---------- the administrator's views ----------

    [Fact]
    public async Task The_notification_history_is_metadata_only_and_for_the_administrator()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        await SentAsync(dentist, desk, "Secret subject", "Secret body text.");
        var history = await admin.SendAsync(HttpMethod.Get, $"/api/admin/notifications/history?recipientUserId={desk.UserId}");
        Assert.Equal(HttpStatusCode.OK, history.Status);
        var text = history.Body.GetRawText();
        Assert.DoesNotContain("Secret", text);
        Assert.DoesNotContain("subject", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Unread", Str(history.Body.GetProperty("items")[0], "status"));
        Assert.Equal(HttpStatusCode.Forbidden, (await dentist.SendAsync(HttpMethod.Get, "/api/admin/notifications/history")).Status);
        Assert.Equal("invalid_status", Error((await admin.SendAsync(HttpMethod.Get, "/api/admin/notifications/history?status=Gone")).Body));
    }

    private async Task<List<(string Event, string Details)>> AuditAsync(Guid entity)
    {
        await using var db = _fixture.CreateContext();
        return (await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == entity).OrderBy(a => a.TimestampUtc).Select(a => new { a.EventType, a.Details }).ToListAsync()).Select(a => (a.EventType, a.Details ?? "")).ToList();
    }

    [Fact]
    public async Task Break_glass_needs_a_reason_and_a_confirmation_audits_every_attempt_and_returns_one_message()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var id = await SentAsync(dentist, desk, "Private subject", "Private body.");

        Assert.Equal(HttpStatusCode.Forbidden, (await dentist.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { messageId = id, reason = "x", confirmed = true })).Status);

        var noReason = await admin.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { messageId = id, reason = " ", confirmed = true });
        Assert.Equal("break_glass_reason_required", Error(noReason.Body));
        var noConfirm = await admin.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { messageId = id, reason = "Complaint review", confirmed = false });
        Assert.Equal("break_glass_confirmation_required", Error(noConfirm.Body));
        var noTarget = await admin.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { reason = "Complaint review", confirmed = true });
        Assert.Equal("break_glass_target_required", Error(noTarget.Body));
        Assert.DoesNotContain("Private", noReason.Body.GetRawText() + noConfirm.Body.GetRawText());

        var ok = await admin.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { messageId = id, reason = "Complaint review", confirmed = true });
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal("Private body.", Str(ok.Body.GetProperty("messages")[0], "body"));

        var audit = await AuditAsync(id);
        Assert.Equal((3, 2, 1), (audit.Count(a => a.Event == AuditEventTypes.MessageBreakGlassAttempted), audit.Count(a => a.Event == AuditEventTypes.MessageBreakGlassRefused), audit.Count(a => a.Event == AuditEventTypes.MessageBreakGlassSucceeded)));
        Assert.Contains(audit, a => a.Event == AuditEventTypes.MessageBreakGlassSucceeded);
        Assert.Contains(audit, a => a.Event == AuditEventTypes.MessageBreakGlassRefused);
        Assert.DoesNotContain(audit, a => a.Details.Contains("Private"));
    }

    [Fact]
    public async Task Break_glass_returns_no_content_when_the_success_cannot_be_audited()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var id = await SentAsync(dentist, desk, "Private subject", "Private body.");
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseBreakGlassSuccess ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.MessageBreakGlassSucceeded}') THROW 59999, 'audit down', 1;");
        var r = await admin.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { messageId = id, reason = "Complaint review", confirmed = true });
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "audit_unavailable"), (r.Status, Error(r.Body)));
        Assert.DoesNotContain("Private", r.Body.GetRawText());
        Assert.DoesNotContain(await AuditAsync(id), a => a.Event == AuditEventTypes.MessageBreakGlassSucceeded);
    }

    [Fact]
    public async Task Break_glass_on_an_unknown_message_is_audited_and_returns_not_found()
    {
        var admin = await _api.SessionAsync("Admin");
        var unknown = Guid.NewGuid();
        var r = await admin.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { messageId = unknown, reason = "Check", confirmed = true });
        Assert.Equal((HttpStatusCode.NotFound, "message_not_found"), (r.Status, Error(r.Body)));
        Assert.Contains((await AuditAsync(unknown)), a => a.Event == AuditEventTypes.MessageBreakGlassRefused);
    }

    // ---------- privacy and concurrency ----------

    [Fact]
    public async Task No_audit_entry_and_no_log_line_holds_a_subject_a_body_a_patient_id_or_a_file_name()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var patient = await _s.PatientAsync();
        var id = await SentAsync(dentist, desk, "Zebra-subject-77", "Zebra-body-text-88", "Urgent", patient);
        await desk.SendAsync(HttpMethod.Get, $"/api/messages/{id}");
        await admin.SendAsync(HttpMethod.Post, "/api/admin/messages/break-glass", new { messageId = id, reason = "Complaint review", confirmed = true });
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Enumerable.Repeat((byte)3, 300)).ToArray();
        var first = (await UploadAsync(admin, patient, png)).GetProperty("document").GetProperty("id").GetGuid();
        await UploadAsync(admin, patient, png, confirm: first.ToString());
        await RunConsumerAsync();

        await using var db = _fixture.CreateContext();
        var audits = await db.AuditLogEntries.AsNoTracking().Select(a => a.Details + " | " + a.Reason + " | " + a.EntityType).ToListAsync();
        var stored = string.Join("\n", audits) + string.Join("\n", await db.Notifications.AsNoTracking().Select(n => n.Kind + n.SourceKey).ToListAsync());
        var logged = string.Join("\n", _logs.Entries.Select(e => e.Message));
        foreach (var secret in new[] { "Zebra-subject-77", "Zebra-body-text-88", patient.ToString(), "scan.png" })
        {
            Assert.DoesNotContain(secret, stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, logged, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(audits, a => a.Contains("Urgent message was sent to 1 recipient"));
    }

    [Fact]
    public async Task Reading_and_hiding_the_same_notification_at_once_ends_in_one_consistent_state()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        await SentAsync(dentist, desk);
        var id = (await desk.SendAsync(HttpMethod.Get, "/api/notifications?status=unread")).Body.GetProperty("items")[0].GetProperty("id").GetGuid();
        var calls = Enumerable.Range(0, 6).SelectMany(_ => new[]
        {
            desk.SendAsync(HttpMethod.Post, $"/api/notifications/{id}/read"),
            desk.SendAsync(HttpMethod.Delete, $"/api/notifications/{id}"),
        }).ToArray();
        var results = await Task.WhenAll(calls);
        Assert.All(results, r => Assert.Contains(r.Status, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        await using var db = _fixture.CreateContext();
        var row = await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id);
        Assert.NotNull(row.ReadAtUtc);
        if (row.DeletedAtUtc is { } deleted) Assert.True(deleted >= row.ReadAtUtc);
    }

    [Fact]
    public async Task Sending_the_same_message_at_once_with_one_key_creates_exactly_one()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var desk = await _api.SessionAsync("FrontDesk");
        var key = Key();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SendAsync(dentist, Msg(desk), key)));
        Assert.All(results, r => Assert.Contains(r.Status, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.Created));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.Messages.CountAsync(m => m.SenderUserId == dentist.UserId));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.RecipientUserId == desk.UserId));
    }

    // ---------- the consumer ----------

    private async Task<JsonElement> UploadAsync(Session by, Guid patient, byte[] bytes, string? confirm = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "scan.png");
        form.Add(new StringContent(Alveara.Api.Architecture.Documents.DocumentCategorySeeds.Radiograph.ToString()), "categoryId");
        form.Add(new StringContent("2026-10-01"), "documentDate");
        form.Add(new StringContent("Scan."), "description");
        if (confirm is not null) form.Add(new StringContent(confirm), "confirmDuplicateOf");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/patients/{patient}/documents") { Content = form };
        request.Headers.Add("X-CSRF-Token", by.Csrf);
        request.Headers.Add("Idempotency-Key", Key());
        var response = await by.Client.SendAsync(request);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        return body;
    }

    private async Task<int> RunConsumerAsync()
    {
        await using var db = _fixture.CreateContext();
        return await new NotificationConsumer(db, PatientTestSupport.Clock).ProcessAllAsync(CancellationToken.None);
    }

    private async Task<(Session Admin, Session Manager, Session Dentist, Guid Original)> ADuplicateAsync()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var dentist = await _api.SessionAsync("Dentist");
        var patient = await _s.PatientAsync();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Enumerable.Repeat((byte)9, 300)).ToArray();
        var first = (await UploadAsync(admin, patient, png)).GetProperty("document").GetProperty("id").GetGuid();
        await RunConsumerAsync();                                                                            // the first import is not a duplicate: nothing to tell
        await UploadAsync(admin, patient, png, confirm: first.ToString());
        return (admin, manager, dentist, first);
    }

    [Fact]
    public async Task A_duplicate_import_tells_the_people_who_hold_the_permission_once_however_often_the_consumer_runs()
    {
        var (admin, manager, dentist, _) = await ADuplicateAsync();
        Assert.Equal(0, (await SummaryAsync(manager)).GetProperty("unread").GetInt32());                    // eventually consistent: not before the consumer runs
        await RunConsumerAsync();
        await RunConsumerAsync();

        foreach (var s in new[] { admin, manager })
        {
            var summary = await SummaryAsync(s);
            Assert.Equal((1, 1), (summary.GetProperty("unread").GetInt32(), summary.GetProperty("unreadImportant").GetInt32()));
            Assert.Equal("document.duplicate_added", Str((await s.SendAsync(HttpMethod.Get, "/api/notifications?status=unread")).Body.GetProperty("items")[0], "kind"));
        }
        Assert.Equal(0, (await SummaryAsync(dentist)).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task A_replay_after_a_lost_cursor_creates_no_second_notification()
    {
        var (_, manager, _, _) = await ADuplicateAsync();
        await RunConsumerAsync();
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("DISABLE TRIGGER TR_NotificationCursors_Guard ON NotificationCursors; UPDATE NotificationCursors SET LastEventNumber = 0; ENABLE TRIGGER TR_NotificationCursors_Guard ON NotificationCursors;");
        await RunConsumerAsync();                                                                            // the whole history is read again
        Assert.Equal(1, (await SummaryAsync(manager)).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task A_disabled_account_is_not_notified_and_a_notification_carries_no_file_name_or_patient_id()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        await _admin_disable(admin, manager);
        var patient = await _s.PatientAsync();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Enumerable.Repeat((byte)5, 300)).ToArray();
        var first = (await UploadAsync(admin, patient, png)).GetProperty("document").GetProperty("id").GetGuid();
        await UploadAsync(admin, patient, png, confirm: first.ToString());
        await RunConsumerAsync();

        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.RecipientUserId == manager.UserId));
        var adminList = (await admin.SendAsync(HttpMethod.Get, "/api/notifications?status=unread")).Body.GetRawText();
        Assert.DoesNotContain(patient.ToString(), adminList, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scan.png", adminList, StringComparison.OrdinalIgnoreCase);
    }

    private static Task _admin_disable(Session admin, Session user) => admin.SendAsync(HttpMethod.Put, $"/api/auth/{user.UserId}/enabled", new SetEnabledRequest(false));
}
