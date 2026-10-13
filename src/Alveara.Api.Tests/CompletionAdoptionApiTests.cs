using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 at the HTTP boundary: who may adopt a procedure that was completed the old way (dentists and administrators, with a CSRF token), the readiness the screen reads first (it says which procedures are
/// legacy completions and what adopting each would do), the whole adoption end to end through the real routes with the reads that show it (the completed procedure, the clinical history), the stable refusal shapes,
/// a replay, a conflicting reuse of the key, a stale screen, and the 503 that stores nothing when the log cannot be written. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class CompletionAdoptionApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _annEncounter;
    private int _codes;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        _annEncounter = e.Id;
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;
    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;
    private static string Item(JsonElement plan, int i) => plan.GetProperty("items")[i].GetProperty("id").GetString()!;
    private static string Url(JsonElement plan, int item) => $"/api/treatment-plans/{Id(plan)}/items/{Item(plan, item)}/adopt-completion";
    private Guid Provider => _s.ProviderA;

    /// <summary>A plan for Ann with two accepted procedures (fee 100 and 70); the first is completed the old, state-only way through the test-only fixture. The returned plan carries the current row version.</summary>
    private async Task<JsonElement> LegacyPlanAsync(Session dentist, Session admin)
    {
        var d = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, d.Status);
        var items = new List<object>();
        for (var i = 0; i < 2; i++)
        {
            var p = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description = $"Procedure {i + 1}", category = "Periodontic", scope = "WholeMouth", fee = 100m - 30m * i });
            Assert.Equal(HttpStatusCode.OK, p.Status);
            items.Add(new { diagnosisId = Id(d.Body), procedureId = p.Body.GetProperty("summary").GetProperty("id").GetString()!, toothKey = (string?)null, surface = (string?)null });
        }
        var plan = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans", new { idempotencyKey = Guid.NewGuid().ToString("N"), title = "Gum health plan", items });
        Assert.Equal(HttpStatusCode.OK, plan.Status);
        var accepted = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan.Body)}/decision", new { decision = "Accepted", rowVersion = Version(plan.Body) });
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        await LegacyCompletionFixture.CompleteItemAsync(_fixture, Id(accepted.Body), Item(accepted.Body, 0), _s.Actor);
        return (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Id(accepted.Body)}")).Body;
    }

    private object Body(JsonElement plan, string key = "adopt-1", string reason = "Completed before procedure records existed", string? version = null, DateTimeOffset? performed = null) => new
    {
        idempotencyKey = key, rowVersion = version ?? Version(plan), encounterId = _annEncounter, providerId = Provider, performedAtUtc = performed ?? DateTimeOffset.UtcNow.AddSeconds(2), reason,
    };

    private async Task<int[]> CountsAsync() => [await _s.CountAsync(db => db.CompletionSets), await _s.CountAsync(db => db.CompletedProcedures), await _s.CountAsync(db => db.BillingEntries),
        await _s.CountAsync(db => db.TreatmentPlanItemStates), await _s.CountAsync(db => db.TreatmentPlanEvents), await _s.CountAsync(db => db.AuditLogEntries)];

    // ---------- authentication, roles and the CSRF contract ----------

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/treatment-plans/{id}/items/{id}/adopt-completion"))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_dentists_and_administrators_can_adopt_a_legacy_completion(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await LegacyPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var by = await _api.SessionAsync(role);
        var before = await CountsAsync();                                                                                                // after signing in: a sign-in writes its own audit entries

        var r = await by.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan));

        if (allowed) Assert.Equal(HttpStatusCode.OK, r.Status);
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.Status);
            Assert.Equal(before, await CountsAsync());
        }
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await LegacyPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var before = await CountsAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan), csrf: false)).Status);
        Assert.Equal(before, await CountsAsync());
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task Readiness_names_the_legacy_completion_and_what_adopting_it_would_do_then_the_adoption_is_recorded_and_read_back()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await LegacyPlanAsync(dentist, await _api.SessionAsync("Admin"));

        var readiness = (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Id(plan)}/completion-readiness")).Body.GetProperty("items");
        Assert.Equal((true, false, "already_completed"), (readiness[0].GetProperty("legacyCompleted").GetBoolean(), readiness[0].GetProperty("ready").GetBoolean(), readiness[0].GetProperty("notReadyReason").GetString()));
        Assert.Equal((100m, false, "NotApplicable"), (readiness[0].GetProperty("fee").GetDecimal(), readiness[0].GetProperty("chargeExists").GetBoolean(), readiness[0].GetProperty("chartEffect").GetString()));
        Assert.False(readiness[1].GetProperty("legacyCompleted").GetBoolean());                                                  // an accepted, open procedure is not a legacy completion

        var done = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan));
        Assert.Equal(HttpStatusCode.OK, done.Status);
        var p = done.Body.GetProperty("procedures")[0];
        Assert.Equal(("Completed before procedure records existed", false), (done.Body.GetProperty("adoptionReason").GetString(), done.Body.GetProperty("replayed").GetBoolean()));
        Assert.Equal((true, "created", 100m), (p.GetProperty("adopted").GetBoolean(), p.GetProperty("charge").GetProperty("outcome").GetString(), p.GetProperty("charge").GetProperty("amount").GetDecimal()));

        var detail = (await dentist.SendAsync(HttpMethod.Get, $"/api/completed-procedures/{p.GetProperty("id").GetString()}")).Body;
        Assert.Equal((true, "Completed before procedure records existed"), (detail.GetProperty("adopted").GetBoolean(), detail.GetProperty("adoptionReason").GetString()));
        var history = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/clinical-history")).Body.GetProperty("entries").EnumerateArray().Single();
        Assert.NotEqual(JsonValueKind.Null, history.GetProperty("providerName").ValueKind);                                       // the old completion now shows who performed it
        Assert.Equal(100m, history.GetProperty("chargeAmount").GetDecimal());

        var after = (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Id(plan)}/completion-readiness")).Body.GetProperty("items");
        Assert.False(after[0].GetProperty("legacyCompleted").GetBoolean());                                                      // adopted: no longer a legacy completion
        Assert.True(after[0].GetProperty("chargeExists").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, "adopt-2", version: Version(done.Body.GetProperty("plan"))))).Status);   // and it cannot be adopted twice
    }

    [Fact]
    public async Task The_same_request_replays_a_changed_one_under_the_key_conflicts_and_a_stale_screen_is_refused()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await LegacyPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var performed = DateTimeOffset.UtcNow.AddSeconds(2);
        var first = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, performed: performed));
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var before = await CountsAsync();

        var again = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, performed: performed));                      // the same request from the old screen
        Assert.Equal((HttpStatusCode.OK, true), (again.Status, again.Body.GetProperty("replayed").GetBoolean()));
        var changed = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, reason: "A different reason", performed: performed));
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_conflict"), (changed.Status, Error(changed.Body)));
        Assert.Equal(before, await CountsAsync());

        var stale = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, "another-key", performed: performed));         // another key from a screen that has not seen the adoption
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Every_wrong_field_is_named_and_unknown_things_are_404_and_nothing_is_recorded()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await LegacyPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var before = await CountsAsync();

        var noVersion = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), new { });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var bare = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), new { rowVersion = Version(plan) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (bare.Status, Error(bare.Body)));
        Assert.Equal(["encounterId", "idempotencyKey", "performedAtUtc", "providerId", "reason"], bare.Body.GetProperty("fieldErrors").EnumerateObject().Select(f => f.Name).Order().ToArray());
        var early = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, performed: DateTimeOffset.UtcNow.AddYears(-5)));
        Assert.Equal(["performedAtUtc"], early.Body.GetProperty("fieldErrors").EnumerateObject().Select(f => f.Name).ToArray());
        var notLegacy = await dentist.SendAsync(HttpMethod.Post, Url(plan, 1), Body(plan));                                           // accepted but not completed
        Assert.Equal((HttpStatusCode.Conflict, "not_a_legacy_completion"), (notLegacy.Status, Error(notLegacy.Body)));
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Guid.NewGuid()}/items/{Item(plan, 0)}/adopt-completion", Body(plan))).Status);
        Assert.Equal("item_not_found", Error((await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/items/{Guid.NewGuid()}/adopt-completion", Body(plan))).Body));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task When_the_log_cannot_be_written_an_adoption_answers_503_without_internals_and_records_nothing_then_the_same_request_works()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await LegacyPlanAsync(dentist, await _api.SessionAsync("Admin"));
        var before = await CountsAsync();
        async Task Sql(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }
        await Sql(@"CREATE TRIGGER [TR_Test_RefuseAdoptionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'CompletionAdopted') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var performed = DateTimeOffset.UtcNow.AddSeconds(2);
        var failed = await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, performed: performed));
        Assert.Equal(((HttpStatusCode)503, "save_failed"), (failed.Status, Error(failed.Body)));
        Assert.DoesNotContain("audit", failed.Body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", failed.Body.GetRawText());
        Assert.Equal(before, await CountsAsync());                                                                                       // the set, the record and the charge were all undone
        Assert.Equal(Version(plan), Version((await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Id(plan)}")).Body));     // and the plan's version did not move

        await Sql("DROP TRIGGER [TR_Test_RefuseAdoptionAudit]");
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan, performed: performed))).Status);   // the same request, now
    }

    // ---------- the reconciliation report ----------

    [Theory]
    [InlineData("Admin", true)] [InlineData("OfficeManager", true)]
    [InlineData("Dentist", false)] [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)]
    public async Task Only_those_who_may_view_the_audit_log_can_read_the_reconciliation_and_anonymous_callers_get_401(string role, bool allowed)
    {
        var r = await (await _api.SessionAsync(role)).SendAsync(HttpMethod.Get, "/api/completion/reconciliation");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Factory.CreateClient().GetAsync("/api/completion/reconciliation")).StatusCode);
    }

    [Fact]
    public async Task The_reconciliation_counts_the_legacy_completion_until_it_is_adopted_and_holds_no_patient_or_plan_detail()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var plan = await LegacyPlanAsync(dentist, admin);

        var before = (await admin.SendAsync(HttpMethod.Get, "/api/completion/reconciliation")).Body;
        Assert.Equal((1, 0, 0, 0, 0, 0, false), (before.GetProperty("legacyCompletedNotAdopted").GetInt32(), before.GetProperty("completedMissingCharge").GetInt32(), before.GetProperty("completedWithUnexpectedCharge").GetInt32(),
            before.GetProperty("orphanedCharges").GetInt32(), before.GetProperty("chartMismatch").GetInt32(), before.GetProperty("duplicateCharges").GetInt32(), before.GetProperty("clean").GetBoolean()));
        Assert.DoesNotContain(Id(plan), before.GetRawText());
        Assert.DoesNotContain(_ann.ToString(), before.GetRawText());

        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, Url(plan, 0), Body(plan))).Status);
        var after = (await admin.SendAsync(HttpMethod.Get, "/api/completion/reconciliation")).Body;
        Assert.Equal((0, true), (after.GetProperty("legacyCompletedNotAdopted").GetInt32(), after.GetProperty("clean").GetBoolean()));
    }
}
