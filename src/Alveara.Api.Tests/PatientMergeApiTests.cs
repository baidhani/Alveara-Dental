using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-017 through the real pipeline (real API, real SQL Server, cookie sessions): only the administrator may look for duplicates, preview, merge or read the log (every working role is 403, an anonymous caller 401, on
/// every endpoint); a merge needs a CSRF token and an Idempotency-Key, answers a repeat with the merge already made, refuses a record that changed after it was reviewed, and shows up in the log with who and when.
/// </summary>
public class PatientMergeApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;
    private Guid _anne;
    private Session _admin = null!;
    private readonly Dictionary<string, Session> _others = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _anne = await _s.PatientAsync("Anne", "Lee");                                                                  // the same last name and birth date as the first: a possible duplicate
        await using (var db = _fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [DateOfBirth] = (SELECT [DateOfBirth] FROM [Patients] WHERE [Id] = {0}) WHERE [Id] = {1}", _ann, _anne);
        _admin = await _api.SessionAsync("Admin");
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager" }) _others[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    private async Task<int> CountAsync(string sql) { await using var db = _fixture.CreateContext(); return await db.Database.SqlQueryRaw<int>(sql).SingleAsync(); }

    private async Task<JsonElement> PreviewAsync(Guid source, Guid survivor)
    {
        var r = await _admin.SendAsync(HttpMethod.Get, $"/api/patient-merges/preview?sourceId={source}&survivorId={survivor}");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private object Body(JsonElement preview, string reason = "Registered twice", Dictionary<string, string>? choices = null, bool? confirmed = true, bool identity = false) =>
        new { sourcePatientId = Guid.Parse(Str(preview.GetProperty("source"), "id")), survivorPatientId = Guid.Parse(Str(preview.GetProperty("survivor"), "id")), reason,
              sourceRowVersion = Str(preview, "sourceRowVersion"), survivorRowVersion = Str(preview, "survivorRowVersion"), fieldChoices = choices, confirmed, confirmIdentityDifference = identity };

    [Fact]
    public async Task Only_the_administrator_may_use_any_of_the_nine_endpoints_and_an_anonymous_caller_sees_nothing()
    {
        var preview = await PreviewAsync(_ann, _anne);
        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, "/api/patient-merges/duplicates", null),
            (HttpMethod.Get, $"/api/patient-merges/preview?sourceId={_ann}&survivorId={_anne}", null),
            (HttpMethod.Get, "/api/patient-merges/log", null),
            (HttpMethod.Post, "/api/patient-merges", Body(preview)),
            (HttpMethod.Get, "/api/patient-merges/dismissals", null),
            (HttpMethod.Post, "/api/patient-merges/dismissals", new { firstPatientId = _ann, secondPatientId = _anne, reason = "Twins" }),
            (HttpMethod.Post, $"/api/patient-merges/dismissals/{Guid.NewGuid()}/restore", null),
            (HttpMethod.Get, $"/api/patient-merges/{Guid.NewGuid()}/unmerge-preview", null),
            (HttpMethod.Post, $"/api/patient-merges/{Guid.NewGuid()}/unmerge", new { reason = "x", confirmed = true }),
        };
        foreach (var (role, session) in _others)
            foreach (var call in calls)
                Assert.True(HttpStatusCode.Forbidden == (await session.SendAsync(call.Method, call.Url, call.Body, "k-" + Guid.NewGuid().ToString("N"))).Status, $"{role} {call.Method} {call.Url}");
        using var anonymous = _api.Factory.CreateClient();
        foreach (var call in calls)
        {
            var response = await anonymous.SendAsync(new HttpRequestMessage(call.Method, call.Url));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));                         // nothing was merged by any of them
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/duplicates")).Status);
    }

    [Fact]
    public async Task The_duplicate_list_suggests_the_pair_with_its_reasons_and_the_preview_names_what_would_move()
    {
        var found = await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/duplicates");
        var pairs = found.Body.GetProperty("pairs").EnumerateArray().ToList();
        var pair = Assert.Single(pairs);
        Assert.Equal(new[] { _ann, _anne }.Order().ToArray(), new[] { Guid.Parse(Str(pair.GetProperty("first"), "id")), Guid.Parse(Str(pair.GetProperty("second"), "id")) }.Order().ToArray());
        Assert.Contains("Same last name and date of birth", pair.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        var preview = await PreviewAsync(_ann, _anne);
        Assert.True(preview.GetProperty("canMerge").GetBoolean());
        Assert.Equal(56, preview.GetProperty("moves").GetArrayLength());
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));                          // a preview changes nothing
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.SendAsync(HttpMethod.Get, $"/api/patient-merges/preview?sourceId={Guid.NewGuid()}&survivorId={_anne}")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Get, $"/api/patient-merges/preview?sourceId={_ann}&survivorId={_ann}")).Status);
    }

    [Fact]
    public async Task A_merge_needs_a_csrf_token_and_a_key_and_a_repeat_is_the_merge_already_made()
    {
        var preview = await PreviewAsync(_ann, _anne);
        Assert.NotEqual(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview), "k1", csrf: false)).Status);
        var noKey = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview));
        Assert.Equal((HttpStatusCode.BadRequest, "idempotency_key_invalid"), (noKey.Status, Str(noKey.Body, "error")));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));

        var first = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, "Same person, two registrations"), "merge-a");
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.False(first.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal((_admin.UserId.ToString(), "Same person, two registrations"), (Str(first.Body, "mergedByUserId"), Str(first.Body, "reason")));
        Assert.NotEqual(default, DateTimeOffset.Parse(Str(first.Body, "mergedAtUtc")));

        var again = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview), "merge-a");
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(Str(first.Body, "mergeId"), Str(again.Body, "mergeId"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientMerged'"));

        var third = await _s.PatientAsync("Cy", "Kim");
        var other = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", new { sourcePatientId = third, survivorPatientId = _anne, reason = "x", sourceRowVersion = (string?)null, survivorRowVersion = (string?)null }, "merge-a");
        Assert.Equal((HttpStatusCode.Conflict, "key_reused"), (other.Status, Str(other.Body, "error")));
    }

    [Fact]
    public async Task A_merge_needs_explicit_confirmation_and_records_that_look_like_different_people_need_their_own()
    {
        var preview = await PreviewAsync(_ann, _anne);
        Assert.Empty(preview.GetProperty("identityConflicts").EnumerateArray());                                                  // a genuine duplicate
        var unconfirmed = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, confirmed: null), "c1");
        Assert.Equal((HttpStatusCode.BadRequest, "confirmation_required"), (unconfirmed.Status, Str(unconfirmed.Body, "error")));
        var declined = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, confirmed: false), "c2");
        Assert.Equal("confirmation_required", Str(declined.Body, "error"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));

        var stranger = await _s.PatientAsync("Zed", "Moss");                                                                      // a different person altogether
        var other = await PreviewAsync(stranger, _anne);
        Assert.Contains("last name", other.GetProperty("identityConflicts").EnumerateArray().Select(c => c.GetString()));
        var refused = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(other), "c3");
        Assert.Equal((HttpStatusCode.Conflict, "identity_conflict"), (refused.Status, Str(refused.Body, "error")));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(other, identity: true), "c4")).Status);       // only on purpose
    }

    [Fact]
    public async Task A_record_changed_after_it_was_reviewed_is_409_and_a_conflicting_field_needs_an_answer_over_http()
    {
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [Phone] = N'(555) 010-0001' WHERE [Id] = {0}; UPDATE [Patients] SET [Phone] = N'(555) 010-0002' WHERE [Id] = {1}", _ann, _anne);
        var preview = await PreviewAsync(_ann, _anne);
        Assert.Equal(new[] { "Phone" }, preview.GetProperty("conflicts").EnumerateArray().Select(c => Str(c, "field")).ToArray());

        var unanswered = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview), "m1");
        Assert.Equal((HttpStatusCode.BadRequest, "choice_required"), (unanswered.Status, Str(unanswered.Body, "error")));
        Assert.True(unanswered.Body.GetProperty("fieldErrors").TryGetProperty("Phone", out _));

        await using (var db = _fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [City] = N'Dallas' WHERE [Id] = {0}", _anne);       // someone edits the survivor meanwhile
        var stale = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, choices: new() { ["Phone"] = "Source" }), "m2");
        Assert.Equal((HttpStatusCode.Conflict, "patient_changed"), (stale.Status, Str(stale.Body, "error")));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));

        var fresh = await PreviewAsync(_ann, _anne);
        var done = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(fresh, choices: new() { ["Phone"] = "Source", ["City"] = "Survivor" }), "m3");
        Assert.Equal(HttpStatusCode.OK, done.Status);
        Assert.Contains(done.Body.GetProperty("resolutions").EnumerateArray(), r => Str(r, "subject") == "Phone" && Str(r, "choice") == "TookSource" && Str(r, "replacedValue") == "(555) 010-0002");
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Phone] = N'(555) 010-0001' AND [MergedAtUtc] IS NULL"));
    }

    [Fact]
    public async Task A_record_that_was_merged_away_refuses_edits_with_a_clear_409_and_stays_out_of_the_patient_search()
    {
        var preview = await PreviewAsync(_ann, _anne);
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, choices: new()), "edit-1")).Status);
        var frontDesk = _others["FrontDesk"];
        var retired = (await frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}")).Body;
        var edit = await frontDesk.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}", new { firstName = "Ann", lastName = "Lee", dateOfBirth = Str(retired, "dateOfBirth"), phone = "(555) 010-9999", addressLine1 = "1 Main St", city = "Austin", state = "TX", postalCode = "78701", rowVersion = Str(retired, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "patient_merged"), (edit.Status, Str(edit.Body, "error")));
        Assert.NotEqual(JsonValueKind.Null, retired.GetProperty("mergedAtUtc").ValueKind);                                                      // the record says it was merged
        var reactivate = await frontDesk.SendAsync(HttpMethod.Put, $"/api/patients/{_ann}/active", new { isActive = true, rowVersion = Str(retired, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "patient_merged"), (reactivate.Status, Str(reactivate.Body, "error")));                        // and cannot be reactivated either
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = '" + _ann + "' AND [Phone] <> N'(555) 010-9999'"));
        var search = await frontDesk.SendAsync(HttpMethod.Get, "/api/patients?q=Ann");
        Assert.DoesNotContain(_ann.ToString(), search.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_pair_dismissed_as_not_a_duplicate_leaves_the_list_is_listed_with_who_and_why_and_comes_back_when_restored()
    {
        Assert.Single((await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/duplicates")).Body.GetProperty("pairs").EnumerateArray());
        Assert.NotEqual(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges/dismissals", new { firstPatientId = _anne, secondPatientId = _ann, reason = "Twins" }, csrf: false)).Status);
        Assert.Equal((HttpStatusCode.BadRequest, "reason_invalid"), await Both(_admin.SendAsync(HttpMethod.Post, "/api/patient-merges/dismissals", new { firstPatientId = _anne, secondPatientId = _ann, reason = " " })));

        var made = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges/dismissals", new { firstPatientId = _anne, secondPatientId = _ann, reason = "They are twins" });
        Assert.Equal(HttpStatusCode.OK, made.Status);
        Assert.Equal((_admin.UserId.ToString(), "They are twins", false), (Str(made.Body, "dismissedByUserId"), Str(made.Body, "reason"), made.Body.GetProperty("replayed").GetBoolean()));
        Assert.False(string.IsNullOrWhiteSpace(Str(made.Body, "dismissedByName")));
        var scan = (await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/duplicates")).Body;
        Assert.Empty(scan.GetProperty("pairs").EnumerateArray()); Assert.Equal(1, scan.GetProperty("dismissed").GetInt32());
        Assert.Single((await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/dismissals")).Body.EnumerateArray());
        Assert.True((await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges/dismissals", new { firstPatientId = _ann, secondPatientId = _anne, reason = "again" })).Body.GetProperty("replayed").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/dismissals/{Str(made.Body, "id")}/restore")).Status);
        Assert.Single((await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/duplicates")).Body.GetProperty("pairs").EnumerateArray());
        Assert.Empty((await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/dismissals")).Body.EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/dismissals/{Guid.NewGuid()}/restore")).Status);
        Assert.Equal(4, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] IN ('PatientDuplicateDismissed','PatientDuplicateRestored') AND [PerformedByUserAccountId] IS NOT NULL"));
    }

    private static async Task<(HttpStatusCode, string)> Both(Task<(HttpStatusCode Status, JsonElement Body)> call) { var r = await call; return (r.Status, Str(r.Body, "error")); }

    [Fact]
    public async Task A_new_record_for_a_merged_away_patient_is_a_clear_409_from_the_screens_and_the_surviving_record_still_takes_one()
    {
        var preview = await PreviewAsync(_ann, _anne);
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview), "guard-1")).Status);
        var dentist = _others["Dentist"];
        var refused = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/clinical-record/items", new { kind = "Allergy", name = "Penicillin", reaction = "Rash", severity = "Mild" });
        Assert.Equal((HttpStatusCode.Conflict, "patient_inactive"), (refused.Status, Str(refused.Body, "error")));    // a retired record is inactive, and the screens' own services say so first (the database check is the backstop behind them)
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_anne}/clinical-record/items", new { kind = "Allergy", name = "Penicillin", reaction = "Rash", severity = "Mild" })).Status);
    }

    [Fact]
    public async Task A_patients_own_page_can_read_where_it_stands_in_the_merge_lineage_whoever_may_view_patients()
    {
        var third = await _s.PatientAsync("Zed", "Moss");
        var frontDesk = _others["FrontDesk"];
        var before = await frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/merge-history");
        Assert.Equal(HttpStatusCode.OK, before.Status);
        Assert.Equal(JsonValueKind.Null, before.Body.GetProperty("mergedInto").ValueKind); Assert.Empty(before.Body.GetProperty("absorbed").EnumerateArray());

        var preview = await PreviewAsync(_ann, _anne);
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, "Registered twice at the desk"), "hist-1")).Status);

        var retired = (await frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/merge-history")).Body;
        var into = retired.GetProperty("mergedInto");
        Assert.Equal((_anne.ToString(), "Registered twice at the desk"), (Str(into, "patientId"), Str(into, "reason")));
        Assert.False(string.IsNullOrWhiteSpace(Str(into, "mergedByName"))); Assert.False(string.IsNullOrWhiteSpace(Str(into, "patientName")));
        Assert.Empty(retired.GetProperty("absorbed").EnumerateArray());

        var survivor = (await frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{_anne}/merge-history")).Body;
        Assert.Equal(JsonValueKind.Null, survivor.GetProperty("mergedInto").ValueKind);
        Assert.Equal(_ann.ToString(), Str(Assert.Single(survivor.GetProperty("absorbed").EnumerateArray()), "patientId"));

        var other = (await frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{third}/merge-history")).Body;
        Assert.Equal(JsonValueKind.Null, other.GetProperty("mergedInto").ValueKind); Assert.Empty(other.GetProperty("absorbed").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await frontDesk.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/merge-history")).Status);
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/patients/{_ann}/merge-history")).StatusCode);
    }

    [Fact]
    public async Task A_merge_can_be_previewed_and_undone_over_http_with_the_same_confirmation_key_and_version_rules_and_the_log_says_so()
    {
        var preview = await PreviewAsync(_ann, _anne);
        var merged = await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, "Registered twice"), "undo-flow-merge");
        var mergeId = Str(merged.Body, "mergeId");
        var log = (await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/log")).Body.EnumerateArray().Single();
        Assert.True(log.GetProperty("canUndo").GetBoolean()); Assert.Equal(JsonValueKind.Null, log.GetProperty("undoneAtUtc").ValueKind);

        var undoPreview = await _admin.SendAsync(HttpMethod.Get, $"/api/patient-merges/{mergeId}/unmerge-preview");
        Assert.Equal(HttpStatusCode.OK, undoPreview.Status);
        Assert.True(undoPreview.Body.GetProperty("canUndo").GetBoolean()); Assert.Empty(undoPreview.Body.GetProperty("blockers").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.SendAsync(HttpMethod.Get, $"/api/patient-merges/{Guid.NewGuid()}/unmerge-preview")).Status);

        object Request(bool? confirmed = true, string reason = "They are different people") => new { reason, confirmed, sourceRowVersion = Str(undoPreview.Body, "sourceRowVersion"), survivorRowVersion = Str(undoPreview.Body, "survivorRowVersion") };
        Assert.NotEqual(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/{mergeId}/unmerge", Request(), "u0", csrf: false)).Status);
        Assert.Equal((HttpStatusCode.BadRequest, "idempotency_key_invalid"), await Both(_admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/{mergeId}/unmerge", Request())));
        Assert.Equal((HttpStatusCode.BadRequest, "confirmation_required"), await Both(_admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/{mergeId}/unmerge", Request(confirmed: null), "u1")));
        Assert.Equal((HttpStatusCode.BadRequest, "reason_invalid"), await Both(_admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/{mergeId}/unmerge", Request(reason: " "), "u2")));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [MergedAtUtc] IS NOT NULL"));              // nothing was undone

        var done = await _admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/{mergeId}/unmerge", Request(), "u3");
        Assert.Equal(HttpStatusCode.OK, done.Status);
        Assert.Equal((_admin.UserId.ToString(), "They are different people", false), (Str(done.Body, "unmergedByUserId"), Str(done.Body, "reason"), done.Body.GetProperty("replayed").GetBoolean()));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [MergedAtUtc] IS NOT NULL"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientUnmerged' AND [PerformedByUserAccountId] IS NOT NULL"));

        Assert.True((await _admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/{mergeId}/unmerge", Request(), "u3")).Body.GetProperty("replayed").GetBoolean());
        Assert.Equal((HttpStatusCode.Conflict, "already_unmerged"), await Both(_admin.SendAsync(HttpMethod.Post, $"/api/patient-merges/{mergeId}/unmerge", Request(), "u4")));
        var after = (await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/log")).Body.EnumerateArray().Single();
        Assert.False(after.GetProperty("canUndo").GetBoolean()); Assert.NotEqual(JsonValueKind.Null, after.GetProperty("undoneAtUtc").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(Str(after, "undoneByName")));
        var history = (await _others["FrontDesk"].SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/merge-history")).Body;
        Assert.Equal(JsonValueKind.Null, history.GetProperty("mergedInto").ValueKind);                                              // the page no longer says merged
    }

    [Fact]
    public async Task The_log_lists_each_merge_with_who_when_why_and_between_which_records()
    {
        var preview = await PreviewAsync(_ann, _anne);
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/patient-merges", Body(preview, "Front desk registered the same person twice"), "log-1")).Status);
        var log = (await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/log")).Body.EnumerateArray().ToList();
        var entry = Assert.Single(log);
        Assert.Equal((_admin.UserId.ToString(), "Front desk registered the same person twice"), (Str(entry, "mergedByUserId"), Str(entry, "reason")));
        Assert.False(string.IsNullOrWhiteSpace(Str(entry, "mergedByName")));
        Assert.Empty((await _admin.SendAsync(HttpMethod.Get, "/api/patient-merges/duplicates")).Body.GetProperty("pairs").EnumerateArray());      // a merged-away record is not suggested again
        Assert.Equal((_ann.ToString(), _anne.ToString()), (Str(entry.GetProperty("source"), "id"), Str(entry.GetProperty("survivor"), "id")));
        Assert.True(DateTimeOffset.Parse(Str(entry, "mergedAtUtc")) > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientMerged' AND [PerformedByUserAccountId] IS NOT NULL"));
    }
}
