using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.ExternalCases;
using Alveara.Api.Architecture.FollowUps;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C02 through the real pipeline (real API, real SQL Server, cookie sessions): who may read cases, maintain the party list and manage cases (billing and anonymous callers see nothing; a write needs a CSRF token); a
/// party is made once; a referral and a lab case are made once per Idempotency-Key, edited and moved with the version the screen showed (stale or simultaneous changes are 409 and lose nothing); the list filters by
/// kind, status, overdue, patient and party and is ordered and paged with counts; a patient's timeline holds only their cases; documents are linked, not copied, and a broken link is flagged; and an overdue case's chase
/// task reaches the work queue with the link back to the case.
/// </summary>
public class ExternalCaseApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;
    private Guid _bo;
    private int _keys;
    private Session _admin = null!, _dentist = null!, _hygienist = null!, _assistant = null!, _frontDesk = null!, _billing = null!, _manager = null!;

    private static DateOnly Today => DateOnly.FromDateTime(PatientTestSupport.Clock.ToPracticeLocal(DateTimeOffset.UtcNow).Date);

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
        _assistant = await _api.SessionAsync("Assistant");
        _frontDesk = await _api.SessionAsync("FrontDesk");
        _billing = await _api.SessionAsync("Billing");
        _manager = await _api.SessionAsync("OfficeManager");
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private string Key() => $"ec-{Interlocked.Increment(ref _keys)}-{Guid.NewGuid():N}";
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    private async Task<JsonElement> PartyAsync(string kind, string? name = null)
    {
        var r = await _manager.SendAsync(HttpMethod.Post, "/api/external-parties", new { kind, name = name ?? $"Party {Guid.NewGuid():N}", contactDetails = "555-010-0100" });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private async Task<JsonElement> ReferralAsync(Guid? patient = null, DateOnly? due = null, Guid? party = null, Session? by = null, string? key = null, string reason = "Evaluation of an impacted wisdom tooth")
    {
        var partyId = party ?? Guid.Parse(Str(await PartyAsync("ReferralProvider"), "id"));
        var r = await (by ?? _dentist).SendAsync(HttpMethod.Post, $"/api/patients/{patient ?? _ann}/referrals", new { partyId, reason, dueOn = due, encounterId = (Guid?)null, notes = (string?)null }, key ?? Key());
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body.GetProperty("case");
    }

    private async Task<JsonElement> LabAsync(Guid? patient = null, DateOnly? due = null, string title = "Crown, zirconia")
    {
        var party = Guid.Parse(Str(await PartyAsync("LabVendor"), "id"));
        var r = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{patient ?? _ann}/lab-cases", new { partyId = party, title, dueOn = due, toothKey = "36", treatmentPlanItemId = (Guid?)null, material = "Zirconia", shade = "A2", notes = (string?)null }, Key());
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body.GetProperty("case");
    }

    private async Task<JsonElement> MoveAsync(Session by, JsonElement c, string to, string? note = null, DateOnly? date = null)
    {
        var path = Str(c, "kind") == "Lab" ? "lab-cases" : "referrals";
        var r = await by.SendAsync(HttpMethod.Post, $"/api/{path}/{Str(c, "id")}/status", new { toStatus = to, note, date, rowVersion = Str(c, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body.GetProperty("case");
    }

    private static List<string> Summaries(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => Str(i.GetProperty("case"), "summary")).ToList();

    private async Task<PatientDocument> DocumentAsync(Guid patient)
    {
        var d = new PatientDocument
        {
            Id = Guid.NewGuid(), PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png",
            ContentType = "image/png", SizeBytes = 2048, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = _dentist.UserId, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('a', 64),
        };
        await using var db = _fixture.CreateContext();
        db.Add(d);
        await db.SaveChangesAsync();
        return d;
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task The_roles_that_see_follow_ups_read_cases_billing_and_anonymous_callers_do_not()
    {
        var c = await ReferralAsync();
        var id = Str(c, "id");
        foreach (var s in new[] { _admin, _dentist, _hygienist, _assistant, _frontDesk, _manager })
        {
            Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, "/api/external-cases")).Status);
            Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, $"/api/referrals/{id}")).Status);
            Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, $"/api/referrals/{id}/history")).Status);
            Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, "/api/external-parties")).Status);
            Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/external-cases/timeline")).Status);
        }
        foreach (var url in new[] { "/api/external-cases", $"/api/referrals/{id}", $"/api/referrals/{id}/history", $"/api/referrals/{id}/documents", "/api/external-parties", $"/api/patients/{_ann}/external-cases/timeline" })
        {
            var denied = await _billing.SendAsync(HttpMethod.Get, url);
            Assert.Equal((HttpStatusCode.Forbidden, "permission_denied"), (denied.Status, Error(denied.Body)));
            using var anonymous = _api.Factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Only_the_roles_that_manage_cases_create_and_move_them_and_only_the_office_manager_and_admin_maintain_the_party_list()
    {
        var provider = Guid.Parse(Str(await PartyAsync("ReferralProvider"), "id"));
        var body = new { partyId = provider, reason = "Second opinion", dueOn = (DateOnly?)null, encounterId = (Guid?)null, notes = (string?)null };
        foreach (var s in new[] { _hygienist, _assistant, _billing })
            Assert.Equal(HttpStatusCode.Forbidden, (await s.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/referrals", body, Key())).Status);
        foreach (var s in new[] { _dentist, _frontDesk, _manager, _admin })
            Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/referrals", body, Key())).Status);

        var c = await ReferralAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await _hygienist.SendAsync(HttpMethod.Post, $"/api/referrals/{Str(c, "id")}/status", new { toStatus = "Sent", note = (string?)null, date = (DateOnly?)null, rowVersion = Str(c, "rowVersion") })).Status);

        var party = new { kind = "LabVendor", name = $"Lab {Guid.NewGuid():N}", contactDetails = (string?)null };
        foreach (var s in new[] { _dentist, _frontDesk, _hygienist, _billing })
            Assert.Equal(HttpStatusCode.Forbidden, (await s.SendAsync(HttpMethod.Post, "/api/external-parties", party)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/external-parties", party)).Status);
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_changes_nothing()
    {
        var provider = Guid.Parse(Str(await PartyAsync("ReferralProvider"), "id"));
        Assert.NotEqual(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/referrals", new { partyId = provider, reason = "x" }, Key(), csrf: false)).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await _manager.SendAsync(HttpMethod.Post, "/api/external-parties", new { kind = "LabVendor", name = "No token lab" }, csrf: false)).Status);
        var c = await ReferralAsync();
        Assert.NotEqual(HttpStatusCode.OK, (await _dentist.SendAsync(HttpMethod.Post, $"/api/referrals/{Str(c, "id")}/status", new { toStatus = "Cancelled", note = "x", rowVersion = Str(c, "rowVersion") }, csrf: false)).Status);
        await using var db = _fixture.CreateContext();
        Assert.Equal((1, 2), (await db.ReferralCases.CountAsync(), await db.ExternalParties.CountAsync()));            // the provider and the one the real referral went to
        Assert.Equal("Draft", (await db.ReferralCases.AsNoTracking().SingleAsync()).Status);
    }

    // ---------- parties ----------

    [Fact]
    public async Task A_party_is_made_once_edited_with_its_version_and_made_inactive_and_active_again()
    {
        var p = await PartyAsync("LabVendor", "Smile Dental Lab");
        var again = await _manager.SendAsync(HttpMethod.Post, "/api/external-parties", new { kind = "LabVendor", name = "smile dental lab", contactDetails = "555-010-0100" });
        Assert.Equal(Str(p, "id"), Str(again.Body, "id"));
        var conflict = await _manager.SendAsync(HttpMethod.Post, "/api/external-parties", new { kind = "LabVendor", name = "Smile Dental Lab", contactDetails = "different" });
        Assert.Equal((HttpStatusCode.Conflict, "party_exists"), (conflict.Status, Error(conflict.Body)));
        var bad = await _manager.SendAsync(HttpMethod.Post, "/api/external-parties", new { kind = "Dentist", name = "X" });
        Assert.Equal((HttpStatusCode.BadRequest, "kind_invalid"), (bad.Status, Error(bad.Body)));

        var edited = await _manager.SendAsync(HttpMethod.Put, $"/api/external-parties/{Str(p, "id")}", new { name = "Smile Dental Laboratory", contactDetails = "new", rowVersion = Str(p, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, edited.Status);
        var stale = await _manager.SendAsync(HttpMethod.Put, $"/api/external-parties/{Str(p, "id")}", new { name = "Again", contactDetails = "x", rowVersion = Str(p, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "party_changed"), (stale.Status, Error(stale.Body)));

        var off = await _manager.SendAsync(HttpMethod.Post, $"/api/external-parties/{Str(p, "id")}/active", new { active = false, rowVersion = Str(edited.Body, "rowVersion") });
        Assert.False(off.Body.GetProperty("isActive").GetBoolean());
        Assert.DoesNotContain((await _dentist.SendAsync(HttpMethod.Get, "/api/external-parties?kind=LabVendor")).Body.EnumerateArray(), x => Str(x, "id") == Str(p, "id"));
        Assert.Contains((await _dentist.SendAsync(HttpMethod.Get, "/api/external-parties?kind=LabVendor&includeInactive=true")).Body.EnumerateArray(), x => Str(x, "id") == Str(p, "id"));
        var inactive = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/lab-cases", new { partyId = Guid.Parse(Str(p, "id")), title = "Crown" }, Key());
        Assert.Equal((HttpStatusCode.Conflict, "party_inactive"), (inactive.Status, Error(inactive.Body)));
    }

    // ---------- the case lifecycle ----------

    [Fact]
    public async Task A_referral_is_made_once_per_key_and_goes_through_its_statuses_with_a_history_that_names_who()
    {
        var provider = Guid.Parse(Str(await PartyAsync("ReferralProvider"), "id"));
        var key = Key();
        var body = new { partyId = provider, reason = "Evaluation", dueOn = Today.AddDays(7), encounterId = (Guid?)null, notes = "Bring the film" };
        var first = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/referrals", body, key);
        var again = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/referrals", body, key);
        Assert.Equal((false, true), (first.Body.GetProperty("replayed").GetBoolean(), again.Body.GetProperty("replayed").GetBoolean()));
        Assert.Equal(Str(first.Body.GetProperty("case"), "id"), Str(again.Body.GetProperty("case"), "id"));
        var noKey = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/referrals", body);
        Assert.Equal((HttpStatusCode.BadRequest, "idempotency_key_required"), (noKey.Status, Error(noKey.Body)));
        var reused = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/referrals", body with { reason = "Different" }, key);
        Assert.Equal((HttpStatusCode.Conflict, "key_reused"), (reused.Status, Error(reused.Body)));

        var c = first.Body.GetProperty("case");
        c = await MoveAsync(_frontDesk, c, "Sent", date: Today.AddDays(-1));
        c = await MoveAsync(_dentist, c, "Received", date: Today);
        c = await MoveAsync(_dentist, c, "Completed", "Report reviewed");
        Assert.Equal(("Completed", "Report reviewed"), (Str(c, "status"), Str(c, "closureNote")));
        var history = (await _hygienist.SendAsync(HttpMethod.Get, $"/api/referrals/{Str(c, "id")}/history")).Body.EnumerateArray().ToList();
        Assert.Equal(new[] { "Created", "StatusChanged", "StatusChanged", "StatusChanged" }, history.Select(h => Str(h, "kind")).ToArray());
        Assert.Equal(_frontDesk.UserId.ToString(), history[1].GetProperty("actorUserId").GetGuid().ToString());
        var refused = await _dentist.SendAsync(HttpMethod.Post, $"/api/referrals/{Str(c, "id")}/status", new { toStatus = "Sent", rowVersion = Str(c, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "status_invalid"), (refused.Status, Error(refused.Body)));
    }

    [Fact]
    public async Task A_lab_case_links_a_tooth_goes_out_and_back_and_may_be_sent_back_for_a_remake_with_a_note()
    {
        var c = await LabAsync(due: Today.AddDays(10));
        Assert.Equal(("36", "Zirconia", "A2"), (Str(c, "toothKey"), Str(c, "material"), Str(c, "shade")));
        c = await MoveAsync(_dentist, c, "Sent");
        c = await MoveAsync(_dentist, c, "InProgress");
        c = await MoveAsync(_dentist, c, "Received");
        var needNote = await _dentist.SendAsync(HttpMethod.Post, $"/api/lab-cases/{Str(c, "id")}/status", new { toStatus = "Sent", rowVersion = Str(c, "rowVersion") });
        Assert.Equal((HttpStatusCode.BadRequest, "note_required"), (needNote.Status, Error(needNote.Body)));
        c = await MoveAsync(_dentist, c, "Sent", "Margin open");
        Assert.Equal(JsonValueKind.Null, c.GetProperty("receivedOn").ValueKind);
        var referralRoute = await _dentist.SendAsync(HttpMethod.Get, $"/api/referrals/{Str(c, "id")}");
        Assert.Equal((HttpStatusCode.NotFound, "case_not_found"), (referralRoute.Status, Error(referralRoute.Body)));            // a lab case is not a referral
        var ref1 = await ReferralAsync();
        var labRoute = await _dentist.SendAsync(HttpMethod.Get, $"/api/lab-cases/{Str(ref1, "id")}");
        Assert.Equal((HttpStatusCode.NotFound, "case_not_found"), (labRoute.Status, Error(labRoute.Body)));                         // and a referral is not a lab case
    }

    [Fact]
    public async Task A_stale_edit_and_two_people_moving_one_case_at_once_cannot_both_win_and_lose_nothing()
    {
        var c = await ReferralAsync(due: Today.AddDays(3));
        var edit = new { partyId = Guid.Parse(Str(c, "partyId")), reason = "Updated", dueOn = Today.AddDays(4), encounterId = (Guid?)null, notes = (string?)null, rowVersion = Str(c, "rowVersion") };
        var ok = await _dentist.SendAsync(HttpMethod.Put, $"/api/referrals/{Str(c, "id")}", edit);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        var stale = await _frontDesk.SendAsync(HttpMethod.Put, $"/api/referrals/{Str(c, "id")}", edit with { reason = "Stale" });
        Assert.Equal((HttpStatusCode.Conflict, "case_changed"), (stale.Status, Error(stale.Body)));
        Assert.Equal("Updated", Str((await _dentist.SendAsync(HttpMethod.Get, $"/api/referrals/{Str(c, "id")}")).Body, "summary"));

        var d = ok.Body.GetProperty("case");
        var results = await Task.WhenAll(new[] { ("Sent", (string?)null), ("Cancelled", "no longer needed") }.Select(m => _dentist.SendAsync(HttpMethod.Post, $"/api/referrals/{Str(d, "id")}/status", new { toStatus = m.Item1, note = m.Item2, rowVersion = Str(d, "rowVersion") })));
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.Conflict && Error(r.Body) is "case_changed" or "status_invalid"));
        await using var db = _fixture.CreateContext();
        Assert.Equal(3, await db.ExternalCaseEvents.CountAsync());                                                                   // created, the edit, and exactly one move
    }

    // ---------- lists ----------

    [Fact]
    public async Task The_list_filters_by_kind_status_overdue_patient_and_party_with_counts_and_a_sensible_order()
    {
        var late = await ReferralAsync(due: Today.AddDays(-3), reason: "Late referral");
        late = await MoveAsync(_dentist, late, "Sent", date: Today.AddDays(-9));
        await ReferralAsync(due: Today.AddDays(5), reason: "Soon referral");
        await ReferralAsync(reason: "Undated referral");
        var lab = await LabAsync(patient: _bo, due: Today.AddDays(-1), title: "Bo's crown");
        lab = await MoveAsync(_dentist, lab, "Sent", date: Today.AddDays(-4));
        var done = await ReferralAsync(reason: "Cancelled referral");
        await MoveAsync(_dentist, done, "Cancelled", "not needed");

        var open = (await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases")).Body;
        Assert.Equal(new[] { "Late referral", "Bo's crown", "Soon referral", "Undated referral" }, Summaries(open));                    // dated first (soonest due first), undated last
        Assert.Equal((4, 2, 1, 4), (open.GetProperty("total").GetInt32(), open.GetProperty("counts").GetProperty("overdue").GetInt32(), open.GetProperty("counts").GetProperty("closed").GetInt32(), open.GetProperty("counts").GetProperty("open").GetInt32()));
        Assert.Equal(new[] { "Late referral", "Bo's crown" }, Summaries((await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases?overdue=true")).Body));
        Assert.Equal(new[] { "Bo's crown" }, Summaries((await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases?kind=Lab")).Body));
        Assert.Equal(new[] { "Bo's crown" }, Summaries((await _frontDesk.SendAsync(HttpMethod.Get, $"/api/external-cases?patientId={_bo}")).Body));
        Assert.Equal(new[] { "Cancelled referral" }, Summaries((await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases?status=closed")).Body));
        Assert.Equal(new[] { "Cancelled referral" }, Summaries((await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases?status=Cancelled")).Body));
        Assert.Equal(5, (await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases?status=all")).Body.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "Late referral" }, Summaries((await _frontDesk.SendAsync(HttpMethod.Get, $"/api/external-cases?partyId={Str(late, "partyId")}")).Body));
        var paged = (await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases?page=2&pageSize=3")).Body;
        Assert.Equal((4, 1, 3), (paged.GetProperty("total").GetInt32(), paged.GetProperty("items").GetArrayLength(), paged.GetProperty("pageSize").GetInt32()));
        Assert.Equal(100, (await _frontDesk.SendAsync(HttpMethod.Get, "/api/external-cases?pageSize=5000")).Body.GetProperty("pageSize").GetInt32());     // bounded
        Assert.Equal("Bo Kim", Str(open.GetProperty("items")[1], "patientName"));
        Assert.True(open.GetProperty("items")[0].GetProperty("case").GetProperty("overdue").GetBoolean());
        Assert.Equal(("Sent", "Sent"), (Str(late, "status"), Str(lab, "status")));
    }

    [Fact]
    public async Task A_bad_filter_is_refused_with_a_stable_code()
    {
        foreach (var (query, code) in new[] { ("kind=Dentist", "kind_invalid"), ("status=Done", "status_invalid") })
        {
            var r = await _dentist.SendAsync(HttpMethod.Get, "/api/external-cases?" + query);
            Assert.Equal((HttpStatusCode.BadRequest, code), (r.Status, Error(r.Body)));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _dentist.SendAsync(HttpMethod.Get, "/api/external-parties?kind=Dentist")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _dentist.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/external-cases/timeline")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _dentist.SendAsync(HttpMethod.Get, $"/api/referrals/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task A_patients_timeline_holds_what_happened_to_their_cases_newest_first_and_nobody_elses()
    {
        var a = await ReferralAsync(_ann, reason: "Ann's referral");
        await MoveAsync(_dentist, a, "Sent");
        await LabAsync(_ann, title: "Ann's crown");
        await ReferralAsync(_bo, reason: "Bo's referral");
        var timeline = (await _hygienist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/external-cases/timeline")).Body.EnumerateArray().ToList();
        Assert.Equal(3, timeline.Count);                                                                                              // created, sent, and the lab case created
        Assert.DoesNotContain(timeline, t => Str(t, "caseSummary") == "Bo's referral");
        var times = timeline.Select(t => t.GetProperty("occurredAtUtc").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(x => x), times);
        Assert.Equal(("Created", "Lab", "Ann's crown"), (Str(timeline[0], "eventKind"), Str(timeline[0], "caseKind"), Str(timeline[0], "caseSummary")));          // the lab case was made last
        Assert.Equal(("StatusChanged", "Draft", "Sent"), (Str(timeline[1], "eventKind"), Str(timeline[1], "fromStatus"), Str(timeline[1], "toStatus")));
    }

    // ---------- documents ----------

    [Fact]
    public async Task A_document_is_linked_not_copied_shown_in_the_case_and_flagged_when_it_goes_bad()
    {
        var c = await ReferralAsync();
        var doc = await DocumentAsync(_ann); var other = await DocumentAsync(_bo);
        var url = $"/api/referrals/{Str(c, "id")}/documents";
        var linked = await _dentist.SendAsync(HttpMethod.Put, $"{url}/{doc.Id}");
        Assert.Equal((HttpStatusCode.OK, true, false), (linked.Status, linked.Body.GetProperty("linked").GetBoolean(), linked.Body.GetProperty("replayed").GetBoolean()));
        Assert.True((await _dentist.SendAsync(HttpMethod.Put, $"{url}/{doc.Id}")).Body.GetProperty("replayed").GetBoolean());
        var foreign = await _dentist.SendAsync(HttpMethod.Put, $"{url}/{other.Id}");
        Assert.Equal((HttpStatusCode.BadRequest, "document_invalid"), (foreign.Status, Error(foreign.Body)));
        Assert.Equal(HttpStatusCode.Forbidden, (await _hygienist.SendAsync(HttpMethod.Put, $"{url}/{doc.Id}")).Status);              // may read cases, may not change them
        var shown = (await _hygienist.SendAsync(HttpMethod.Get, url)).Body.EnumerateArray().Single();
        Assert.Equal((doc.Id.ToString(), "pano.png", false), (Str(shown, "documentId"), Str(shown, "fileName"), shown.GetProperty("broken").GetBoolean()));
        await using (var db = _fixture.CreateContext())
        {
            db.DocumentIntegrity.Add(new DocumentIntegrity { DocumentId = doc.Id, Status = IntegrityStatuses.Missing, CheckedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.Documents.CountAsync());                                                                         // nothing was copied
        }
        var broken = (await _dentist.SendAsync(HttpMethod.Get, url)).Body.EnumerateArray().Single();
        Assert.Equal((true, "The document's file is missing from storage."), (broken.GetProperty("broken").GetBoolean(), Str(broken, "brokenReason")));
        var unlinked = await _dentist.SendAsync(HttpMethod.Delete, $"{url}/{doc.Id}");
        Assert.False(unlinked.Body.GetProperty("linked").GetBoolean());
        Assert.Empty((await _dentist.SendAsync(HttpMethod.Get, url)).Body.EnumerateArray());
    }

    // ---------- the work queue ----------

    [Fact]
    public async Task An_overdue_case_reaches_the_work_queue_as_an_unassigned_task_that_names_its_case()
    {
        var late = await ReferralAsync(due: Today.AddDays(-2));
        late = await MoveAsync(_dentist, late, "Sent", date: Today.AddDays(-8));
        var lab = await LabAsync(due: Today.AddDays(-1));
        lab = await MoveAsync(_dentist, lab, "Sent", date: Today.AddDays(-6));
        await ReferralAsync(due: Today.AddDays(9));                                                                                  // not due: no task

        await using (var db = _fixture.CreateContext())
        {
            var clock = PatientTestSupport.Clock;
            await new ExternalCaseTaskScheduler(db, new BackgroundJobQueue(db), clock).EnqueueAsync(default);
        }
        await using (var db = _fixture.CreateContext())
        {
            var runnerClock = new TestClock(DateTimeOffset.UtcNow);
            await new BackgroundJobRunner(db, [new ExternalCaseTaskJobHandler(runnerClock)]).ProcessOnceAsync();
        }

        var queue = (await _frontDesk.SendAsync(HttpMethod.Get, "/api/work-queue?kind=ReferralFollowUp")).Body;
        var item = queue.GetProperty("items").EnumerateArray().Single();
        var task = item.GetProperty("task");
        Assert.Equal((Str(late, "id"), "ReferralFollowUp", JsonValueKind.Null, "Overdue"), (task.GetProperty("sourceReferralCaseId").GetGuid().ToString(), Str(task, "kind"), task.GetProperty("assigneeUserId").ValueKind, Str(item, "urgency")));
        var labQueue = (await _frontDesk.SendAsync(HttpMethod.Get, "/api/work-queue?kind=LabCaseDue")).Body.GetProperty("items").EnumerateArray().Single().GetProperty("task");
        Assert.Equal(Str(lab, "id"), labQueue.GetProperty("sourceLabCaseId").GetGuid().ToString());
        Assert.Equal(2, (await _frontDesk.SendAsync(HttpMethod.Get, "/api/work-queue?filter=unassigned")).Body.GetProperty("total").GetInt32());
    }
}
