using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>A logger provider that keeps what the API logs, so a test can assert a structured event and that it carries nothing it should not.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, Entries);
    public void Dispose() { }

    private sealed class Capture(string category, ConcurrentQueue<(string, LogLevel, string)> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => sink.Enqueue((category, logLevel, formatter(state, exception)));
    }
}

/// <summary>
/// ALV-008-C01 at the HTTP boundary: who may complete a set, read the readiness, read and correct completed procedures; the CSRF and version contract; the stable refusal shapes (with the procedures named when
/// the refusal is about particular procedures); that no charge amount can be supplied; the whole workflow end to end; and that the two legacy completion routes are unchanged and now log that they were used.
/// A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class CompletionApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly CapturingLoggerProvider _logs = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _annEncounter, _boEncounter;
    private int _codes;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture) { ConfigureLogging = b => b.AddProvider(_logs) };
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _annEncounter = await EncounterAsync(_ann);
        _boEncounter = await EncounterAsync(_bo);
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<Guid> EncounterAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;
    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;
    private static string[] Fields(JsonElement e) => e.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray();
    private static string PlanUrl(JsonElement plan, string tail = "") => $"/api/treatment-plans/{Id(plan)}{tail}";
    private Guid Provider => _s.ProviderA;

    /// <summary>A plan for Ann with <paramref name="count"/> procedures (fee 100, 70, 40...), all accepted. The returned plan carries the current row version.</summary>
    private async Task<JsonElement> AcceptedPlanAsync(Session dentist, Session admin, int count = 2)
    {
        var d = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, d.Status);
        var items = new List<object>();
        for (var i = 0; i < count; i++)
        {
            var p = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description = $"Procedure {i + 1}", category = "Periodontic", scope = "WholeMouth", fee = 100m - 30m * i });
            Assert.Equal(HttpStatusCode.OK, p.Status);
            items.Add(new { diagnosisId = Id(d.Body), procedureId = p.Body.GetProperty("summary").GetProperty("id").GetString()!, toothKey = (string?)null, surface = (string?)null });
        }
        var plan = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans", new { idempotencyKey = Guid.NewGuid().ToString("N"), title = "Gum health plan", items });
        Assert.Equal(HttpStatusCode.OK, plan.Status);
        var accepted = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan.Body, "/decision"), new { decision = "Accepted", rowVersion = Version(plan.Body) });
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        return accepted.Body;
    }

    private object Body(JsonElement plan, int[] items, string key = "set-1", DateTimeOffset? performed = null, string? version = null, object? extra = null) => new
    {
        idempotencyKey = key, rowVersion = version ?? Version(plan), encounterId = _annEncounter, providerId = Provider, performedAtUtc = performed ?? DateTimeOffset.UtcNow.AddSeconds(2),
        itemIds = items.Select(i => plan.GetProperty("items")[i].GetProperty("id").GetString()!).ToArray(), extra,
    };

    // ---------- authentication, roles and the CSRF contract ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_new_route()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod, string)[]
        {
            (HttpMethod.Post, $"/api/treatment-plans/{id}/complete-procedures"), (HttpMethod.Get, $"/api/treatment-plans/{id}/completion-readiness"),
            (HttpMethod.Get, $"/api/patients/{id}/completed-procedures"), (HttpMethod.Get, $"/api/completed-procedures/{id}"), (HttpMethod.Post, $"/api/completed-procedures/{id}/amend"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_dentists_and_administrators_can_read_the_readiness_complete_and_correct(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var done = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(plan, [0], "seed"));
        Assert.Equal(HttpStatusCode.OK, done.Status);
        var procedureId = done.Body.GetProperty("procedures")[0].GetProperty("id").GetString();
        var current = done.Body.GetProperty("plan");

        var by = await _api.SessionAsync(role);
        var readiness = await by.SendAsync(HttpMethod.Get, PlanUrl(plan, "/completion-readiness"));
        var complete = await by.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(current, [1], "second", version: Version(current)));
        var amend = await by.SendAsync(HttpMethod.Post, $"/api/completed-procedures/{procedureId}/amend", new { idempotencyKey = "a1", expectedAmendmentNumber = 0, providerId = _s.ProviderB, reason = "Wrong provider" });

        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, new[] { readiness.Status, complete.Status, amend.Status });
        else Assert.All(new[] { readiness.Status, complete.Status, amend.Status }, st => Assert.Equal(HttpStatusCode.Forbidden, st));
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_completed_procedures(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var done = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(plan, [0]));
        var by = await _api.SessionAsync(role);
        var list = await by.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/completed-procedures");
        var one = await by.SendAsync(HttpMethod.Get, $"/api/completed-procedures/{done.Body.GetProperty("procedures")[0].GetProperty("id").GetString()}");
        Assert.All(new[] { list.Status, one.Status }, st => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, st));
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_records_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var noToken = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(plan, [0]), csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.Status);
        var ok = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(plan, [0]));
        var amend = await dentist.SendAsync(HttpMethod.Post, $"/api/completed-procedures/{ok.Body.GetProperty("procedures")[0].GetProperty("id").GetString()}/amend",
            new { idempotencyKey = "a1", expectedAmendmentNumber = 0, providerId = _s.ProviderB, reason = "Wrong provider" }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, amend.Status);
        Assert.Equal(1, await _s.CountAsync(db => db.CompletedProcedures));
        Assert.Equal(0, await _s.CountAsync(db => db.CompletedProcedureAmendments));
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_set_is_read_for_readiness_completed_charged_replayed_listed_corrected_and_the_correction_replayed()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"), 3);

        var ready = await dentist.SendAsync(HttpMethod.Get, PlanUrl(plan, "/completion-readiness"));
        Assert.Equal(HttpStatusCode.OK, ready.Status);
        Assert.Equal(Version(plan), Version(ready.Body));
        Assert.All(ready.Body.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("ready").GetBoolean()));
        Assert.Equal([100m, 70m, 40m], ready.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("fee").GetDecimal()).ToArray());

        var performed = DateTimeOffset.UtcNow.AddSeconds(2);
        var body = Body(plan, [0, 2], "set-1", performed, extra: new { amount = 1 });
        var done = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), body);
        Assert.Equal(HttpStatusCode.OK, done.Status);
        Assert.False(done.Body.GetProperty("replayed").GetBoolean());
        var procedures = done.Body.GetProperty("procedures").EnumerateArray().ToList();
        Assert.Equal([1, 3], procedures.Select(p => p.GetProperty("itemNumber").GetInt32()).ToArray());
        Assert.Equal([100m, 40m], procedures.Select(p => p.GetProperty("charge").GetProperty("amount").GetDecimal()).ToArray());             // the plan's fees: a caller-supplied amount means nothing
        Assert.All(procedures, p => Assert.Equal("created", p.GetProperty("charge").GetProperty("outcome").GetString()));
        Assert.NotEqual(Version(plan), Version(done.Body.GetProperty("plan")));

        var replay = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), body);                                   // the same request, from the out-of-date screen
        Assert.Equal(HttpStatusCode.OK, replay.Status);
        Assert.True(replay.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(2, await _s.CountAsync(db => db.BillingEntries));

        var list = await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/completed-procedures");
        Assert.Equal(2, list.Body.GetProperty("procedures").GetArrayLength());
        var id = procedures[0].GetProperty("id").GetString();
        var amend = new { idempotencyKey = "a1", expectedAmendmentNumber = 0, providerId = _s.ProviderB, reason = "Recorded under the wrong provider" };
        var fixedUp = await dentist.SendAsync(HttpMethod.Post, $"/api/completed-procedures/{id}/amend", amend);
        Assert.Equal(HttpStatusCode.OK, fixedUp.Status);
        Assert.Equal((1, false), (fixedUp.Body.GetProperty("amendmentNumber").GetInt32(), fixedUp.Body.GetProperty("replayed").GetBoolean()));
        Assert.Equal(_s.ProviderB.ToString(), fixedUp.Body.GetProperty("procedure").GetProperty("providerProfileId").GetString());
        var again = await dentist.SendAsync(HttpMethod.Post, $"/api/completed-procedures/{id}/amend", amend);
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        var one = await dentist.SendAsync(HttpMethod.Get, $"/api/completed-procedures/{id}");
        Assert.Equal((1, 1), (one.Body.GetProperty("latestAmendmentNumber").GetInt32(), one.Body.GetProperty("amendments").GetArrayLength()));
        Assert.Equal(1, await _s.CountAsync(db => db.CompletedProcedureAmendments));
    }

    [Fact]
    public async Task The_refusals_have_stable_codes_and_name_what_to_correct()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"));

        var missingKey = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), new { rowVersion = Version(plan), itemIds = Array.Empty<Guid>() });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (missingKey.Status, Error(missingKey.Body)));
        Assert.Equal(["encounterId", "idempotencyKey", "itemIds", "performedAtUtc", "providerId"], Fields(missingKey.Body));

        var noVersion = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), new { idempotencyKey = "k", itemIds = Array.Empty<Guid>() });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));

        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Guid.NewGuid()}/complete-procedures", Body(plan, [0]))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/completed-procedures")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/completed-procedures/{Guid.NewGuid()}")).Status);
        var amendMissing = await dentist.SendAsync(HttpMethod.Post, $"/api/completed-procedures/{Guid.NewGuid()}/amend", new { idempotencyKey = "a", expectedAmendmentNumber = 0, providerId = _s.ProviderB, reason = "x" });
        Assert.Equal((HttpStatusCode.NotFound, "completed_procedure_not_found"), (amendMissing.Status, Error(amendMissing.Body)));
        Assert.Equal(0, await _s.CountAsync(db => db.CompletionSets));
    }

    [Fact]
    public async Task A_stale_screen_is_a_conflict_a_changed_key_is_a_conflict_and_a_completed_procedure_is_listed_in_the_refusal()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"), 3);
        var first = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(plan, [0]));
        Assert.Equal(HttpStatusCode.OK, first.Status);

        var stale = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(plan, [1], "set-2"));            // the version from before the first set
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);

        var conflict = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(plan, [1], "set-1"));          // set-1 again, asking for something else
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_conflict"), (conflict.Status, Error(conflict.Body)));

        var current = first.Body.GetProperty("plan");
        var again = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(current, [0, 1], "set-3", version: Version(current)));
        Assert.Equal((HttpStatusCode.Conflict, "items_not_completable"), (again.Status, Error(again.Body)));
        var listed = Assert.Single(again.Body.GetProperty("items").EnumerateArray());
        Assert.Equal((1, "already_completed"), (listed.GetProperty("itemNumber").GetInt32(), listed.GetProperty("reason").GetString()));
        Assert.Equal(["items.1"], Fields(again.Body));
        Assert.Equal(1, await _s.CountAsync(db => db.CompletedProcedures));
    }

    [Fact]
    public async Task The_clinical_history_route_adds_the_detail_for_a_workflow_completion_and_leaves_it_null_for_an_old_one()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"), 2);
        var old = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, $"/items/{plan.GetProperty("items")[0].GetProperty("id").GetString()}/complete"), new { });
        Assert.Equal(HttpStatusCode.OK, old.Status);
        var done = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(old.Body, [1], version: Version(old.Body)));
        Assert.Equal(HttpStatusCode.OK, done.Status);

        var history = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/clinical-history")).Body.GetProperty("entries").EnumerateArray().ToList();

        var legacy = history.Single(h => h.GetProperty("itemNumber").GetInt32() == 1);
        var authoritative = history.Single(h => h.GetProperty("itemNumber").GetInt32() == 2);
        Assert.All(new[] { "completedProcedureId", "providerName", "performedAtUtc", "encounterId", "chargeEntryNumber", "chargeAmount", "chartOutcome" }, f => Assert.Equal(JsonValueKind.Null, legacy.GetProperty(f).ValueKind));
        Assert.Equal((JsonValueKind.String, 1, 70m, "NotApplicable"), (authoritative.GetProperty("providerName").ValueKind, authoritative.GetProperty("chargeEntryNumber").GetInt32(), authoritative.GetProperty("chargeAmount").GetDecimal(), authoritative.GetProperty("chartOutcome").GetString()));
        Assert.True(authoritative.TryGetProperty("completedByName", out _));                                                            // the STORY-008 fields are still there
    }

    // ---------- the legacy routes ----------

    [Fact]
    public async Task The_legacy_completion_routes_are_unchanged_and_only_log_that_they_were_used()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"), 3);

        var one = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, $"/items/{plan.GetProperty("items")[0].GetProperty("id").GetString()}/complete"), new { });
        Assert.Equal(HttpStatusCode.OK, one.Status);
        var all = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-accepted"), new { rowVersion = Version(one.Body) });
        Assert.Equal(HttpStatusCode.OK, all.Status);
        var refused = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-accepted"), new { });
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);

        Assert.Equal(["Completed", "Completed", "Completed"], all.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("completion").GetString()!).ToArray());
        Assert.Equal((0, 0, 0), (await _s.CountAsync(db => db.CompletedProcedures), await _s.CountAsync(db => db.CompletionSets), await _s.CountAsync(db => db.BillingEntries)));    // they record nothing new

        var events = _logs.Entries.Where(e => e.Message.StartsWith("legacy_completion_used")).Select(e => e.Message).ToList();
        Assert.Equal(3, events.Count);
        Assert.Contains(events, m => m.Contains("route=items/complete") && m.Contains("outcome=success"));
        Assert.Contains(events, m => m.Contains("route=complete-accepted") && m.Contains("outcome=success"));
        Assert.Contains(events, m => m.Contains("route=complete-accepted") && m.Contains("outcome=failure") && m.Contains("status=400"));
        Assert.All(events, m => { Assert.Contains("trace_id=", m); Assert.DoesNotContain(Id(plan), m); });                                   // no plan, patient or clinical detail

        var product = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/complete-procedures"), Body(all.Body, [0], "set-1", version: Version(all.Body)));
        Assert.Equal(HttpStatusCode.Conflict, product.Status);                                                                                 // already completed the old way: the new workflow refuses it
        Assert.Equal(3, _logs.Entries.Count(e => e.Message.StartsWith("legacy_completion_used")));                                          // and does not count as legacy use
    }
}
