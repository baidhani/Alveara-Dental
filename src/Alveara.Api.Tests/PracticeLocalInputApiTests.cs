using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Alveara.Api.Tests.SchedulingApiHarness;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A3 (briefing section 5, BR-2): the five routes that take a typed practice-local time. Each refuses a malformed, offset, nonexistent or
/// ambiguous local time and a request carrying both forms, with the stable code and the field named, before anything is read or written; follow-up
/// create/update and vitals store the instant the local time means in the practice zone (America/Chicago). The legacy UTC forms alone stay covered by the
/// routes' existing tests (FollowUpApiTests, CompletionApiTests, CompletionAdoptionApiTests, ClinicalRecordApiTests).
/// </summary>
public class PracticeLocalInputApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;
    private Session _dentist = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _dentist = await _api.SessionAsync("Dentist");   // holds ManageTreatmentPlans, ManageFollowUps and ManageClinicalNotes
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string[] Fields(JsonElement e) => e.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray();

    /// <summary>route name -> (method, url with throwaway ids, local field, legacy UTC field, seconds allowed). The refusals come before any lookup.</summary>
    private (HttpMethod Method, string Url, string Local, string Utc, bool Seconds) Route(string name) => name switch
    {
        "complete" => (HttpMethod.Post, $"/api/treatment-plans/{Guid.NewGuid()}/complete-procedures", "performedAtLocal", "performedAtUtc", true),
        "adopt" => (HttpMethod.Post, $"/api/treatment-plans/{Guid.NewGuid()}/items/{Guid.NewGuid()}/adopt-completion", "performedAtLocal", "performedAtUtc", true),
        "follow-up-create" => (HttpMethod.Post, $"/api/patients/{_ann}/follow-ups", "dueAtLocal", "dueAtUtc", false),
        "follow-up-update" => (HttpMethod.Put, $"/api/follow-ups/{Guid.NewGuid()}", "dueAtLocal", "dueAtUtc", false),
        "vitals" => (HttpMethod.Post, $"/api/encounters/{Guid.NewGuid()}/vitals", "measuredAtLocal", "measuredAtUtc", false),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(string route, Dictionary<string, object?> fields)
    {
        var r = Route(route);
        var body = new Dictionary<string, object?>(fields) { ["idempotencyKey"] = $"k-{Guid.NewGuid():N}", ["rowVersion"] = "AAAAAAAAAAA=", ["title"] = "Call", ["pulseBpm"] = 70 };
        return await _dentist.SendAsync(r.Method, r.Url, body, $"key-{Guid.NewGuid():N}");
    }

    public static TheoryData<string> Routes => new() { "complete", "adopt", "follow-up-create", "follow-up-update", "vitals" };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Each_route_refuses_bad_local_text_dst_gaps_and_overlaps_and_both_forms_with_the_field_named_and_writes_nothing(string route)
    {
        var r = Route(route);
        foreach (var text in new[] { "2030-01-14T09:30Z", "2030-01-14T09:30-06:00", " 2030-01-14T09:30", "2030-01-14T09:30:00.5", "2030-1-4T9:5", "๒๐๓๐-๐๑-๑๔T๐๙:๓๐" })
        {
            var bad = await SendAsync(route, new() { [r.Local] = text });
            Assert.True(bad.Status == HttpStatusCode.BadRequest && Error(bad.Body) == "invalid_local_time", $"{route} '{text}': {(int)bad.Status} {bad.Body}");
            Assert.Equal([r.Local], Fields(bad.Body));
        }
        var seconds = await SendAsync(route, new() { [r.Local] = "2030-01-14T09:30:15" });
        if (r.Seconds) Assert.False(seconds.Status == HttpStatusCode.BadRequest && Error(seconds.Body) == "invalid_local_time", $"{route}: seconds are allowed");   // accepted by the parser; the route then answers for its own reasons (e.g. 404)
        else Assert.Equal((HttpStatusCode.BadRequest, "invalid_local_time"), (seconds.Status, Error(seconds.Body)));          // minutes-only field

        var gap = await SendAsync(route, new() { [r.Local] = "2030-03-10T02:30" });
        Assert.Equal((HttpStatusCode.BadRequest, "local_time_does_not_exist"), (gap.Status, Error(gap.Body)));
        Assert.Equal([r.Local], Fields(gap.Body));

        var overlap = await SendAsync(route, new() { [r.Local] = "2030-11-03T01:30" });
        Assert.Equal((HttpStatusCode.BadRequest, "local_time_ambiguous"), (overlap.Status, Error(overlap.Body)));

        var both = await SendAsync(route, new() { [r.Local] = "2030-01-14T09:30", [r.Utc] = "2030-01-14T15:30:00Z" });
        Assert.Equal((HttpStatusCode.BadRequest, "ambiguous_time"), (both.Status, Error(both.Body)));
        Assert.Equal(new[] { r.Local, r.Utc }.Order().ToArray(), Fields(both.Body));

        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.FollowUpTasks.CountAsync() + await db.EncounterVitals.CountAsync() + await db.CompletionSets.CountAsync());
    }

    [Fact]
    public async Task A_follow_up_due_time_typed_in_practice_time_is_stored_as_that_instant_on_create_and_update()
    {
        var created = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/follow-ups",
            new { title = "Call about the crown", note = (string?)null, dueAtLocal = "2030-01-14T09:30", assigneeUserId = _dentist.UserId, recallIntervalDays = (int?)null }, $"fu-{Guid.NewGuid():N}");
        Assert.True(created.Status == HttpStatusCode.OK, created.Body.ToString());
        var task = created.Body.GetProperty("task");
        Assert.Equal(new DateTimeOffset(2030, 1, 14, 15, 30, 0, TimeSpan.Zero), task.GetProperty("dueAtUtc").GetDateTimeOffset());

        var updated = await _dentist.SendAsync(HttpMethod.Put, $"/api/follow-ups/{task.GetProperty("id").GetString()}",
            new { title = "Call about the crown", note = (string?)null, dueAtLocal = "2030-07-14T09:30", assigneeUserId = _dentist.UserId, recallIntervalDays = (int?)null, rowVersion = task.GetProperty("rowVersion").GetString() });
        Assert.True(updated.Status == HttpStatusCode.OK, updated.Body.ToString());
        Assert.Equal(new DateTimeOffset(2030, 7, 14, 14, 30, 0, TimeSpan.Zero), updated.Body.GetProperty("task").GetProperty("dueAtUtc").GetDateTimeOffset());   // CDT
    }

    [Fact]
    public async Task A_vitals_time_typed_in_practice_time_is_stored_as_that_instant()
    {
        var enc = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/encounters", new { }, $"start-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, enc.Status);
        var chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var oneHourAgo = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow.AddHours(-1), chicago);
        var local = oneHourAgo.ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var expected = new DateTimeOffset(DateTime.SpecifyKind(DateTime.ParseExact(local, "yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Unspecified), oneHourAgo.Offset).ToUniversalTime();

        var r = await _dentist.SendAsync(HttpMethod.Post, $"/api/encounters/{enc.Body.GetProperty("id").GetString()}/vitals",
            new { measuredAtLocal = local, pulseBpm = 70, rowVersion = enc.Body.GetProperty("rowVersion").GetString() }, $"v-{Guid.NewGuid():N}");
        Assert.True(r.Status is HttpStatusCode.Created or HttpStatusCode.OK, r.Body.ToString());
        await using var db = _fixture.CreateContext();
        Assert.Equal(expected, (await db.EncounterVitals.SingleAsync()).MeasuredAtUtc);
    }

    [Fact]
    public async Task A_later_field_error_names_the_local_field_too_when_the_local_form_was_sent_and_is_unchanged_for_a_legacy_request()
    {
        // owner decision (option 3): the unchanged vitals rule "cannot be dated in the future" reports measuredAtUtc; a local request also gets measuredAtLocal
        var enc = await _dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/encounters", new { }, $"start-{Guid.NewGuid():N}");
        var url = $"/api/encounters/{enc.Body.GetProperty("id").GetString()}/vitals";
        var version = enc.Body.GetProperty("rowVersion").GetString();

        var local = await _dentist.SendAsync(HttpMethod.Post, url, new { measuredAtLocal = "2040-01-14T09:30", pulseBpm = 70, rowVersion = version }, $"v-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.BadRequest, local.Status);
        Assert.Equal(["measuredAtLocal", "measuredAtUtc"], Fields(local.Body));
        Assert.Equal(local.Body.GetProperty("fieldErrors").GetProperty("measuredAtUtc").GetString(), local.Body.GetProperty("fieldErrors").GetProperty("measuredAtLocal").GetString());

        var legacy = await _dentist.SendAsync(HttpMethod.Post, url, new { measuredAtUtc = "2040-01-14T15:30:00Z", pulseBpm = 70, rowVersion = version }, $"v-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.BadRequest, legacy.Status);
        Assert.Equal(["measuredAtUtc"], Fields(legacy.Body));   // exactly what an old caller saw before A3
    }

    [Fact]
    public void The_alias_adds_only_the_local_name_of_the_marked_field_and_leaves_other_errors_alone()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var errors = new Dictionary<string, string> { ["performedAtUtc"] = "Too early.", ["providerId"] = "Required." };
        Assert.Same(errors, Alveara.Api.Architecture.Time.PracticeLocalInput.WithLocalAlias(errors, context));   // nothing marked: unchanged
        Alveara.Api.Architecture.Time.PracticeLocalInput.MarkLocalUsed(context, "performedAtLocal", "performedAtUtc");
        var aliased = Alveara.Api.Architecture.Time.PracticeLocalInput.WithLocalAlias(errors, context)!;
        Assert.Equal(["performedAtLocal", "performedAtUtc", "providerId"], aliased.Keys.Order());
        Assert.Equal("Too early.", aliased["performedAtLocal"]);
        Assert.Equal(2, errors.Count);   // the original is not modified
    }
}
