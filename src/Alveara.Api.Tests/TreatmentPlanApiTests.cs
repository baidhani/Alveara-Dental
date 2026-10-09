using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-015 at the HTTP boundary: who may read and who may change a treatment plan, the CSRF and row-version contract, the stable refusal shape that names every entry to correct
/// (including <c>items[0].diagnosisId</c>), and the workflow end to end with the catalog fee copied onto each item. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class TreatmentPlanApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _annEncounter, _boEncounter;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
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
    private string ListUrl(Guid? patient = null) => $"/api/patients/{patient ?? _ann}/treatment-plans";
    private static string PlanUrl(JsonElement plan, string tail = "") => $"/api/treatment-plans/{Id(plan)}{tail}";

    /// <summary>A current diagnosis recorded through the diagnosis API by a dentist.</summary>
    private async Task<string> DiagnosisAsync(Session dentist, Guid? patient = null)
    {
        var p = patient ?? _ann;
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{p}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = p == _bo ? _boEncounter : _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return Id(r.Body);
    }

    /// <summary>A whole-mouth local catalog procedure created by an administrator.</summary>
    private async Task<string> ProcedureAsync(Session admin, string code = "LOCAL-100", decimal fee = 50m)
    {
        var r = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code, description = "Periodontal maintenance", category = "Periodontic", scope = "WholeMouth", fee });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body.GetProperty("summary").GetProperty("id").GetString()!;
    }

    private static object Item(string diagnosis, string procedure) => new { diagnosisId = diagnosis, procedureId = procedure, toothKey = (string?)null, surface = (string?)null };

    private Task<(HttpStatusCode Status, JsonElement Body)> CreatePlan(Session by, object[] items, string key = "plan-1", string title = "Periodontal plan", Guid? patient = null) =>
        by.SendAsync(HttpMethod.Post, ListUrl(patient), new { idempotencyKey = key, title, items });

    private async Task<(JsonElement Plan, string Diagnosis, string Procedure)> SimplePlanAsync(Session dentist, Session admin, string key = "plan-1")
    {
        var diagnosis = await DiagnosisAsync(dentist);
        var procedure = await ProcedureAsync(admin);
        var r = await CreatePlan(dentist, [Item(diagnosis, procedure)], key);
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return (r.Body, diagnosis, procedure);
    }

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod, string)[]
        {
            (HttpMethod.Get, ListUrl()), (HttpMethod.Post, ListUrl()), (HttpMethod.Get, $"/api/treatment-plans/{id}"), (HttpMethod.Get, $"/api/treatment-plans/{id}/history"),
            (HttpMethod.Post, $"/api/treatment-plans/{id}/items"), (HttpMethod.Post, $"/api/treatment-plans/{id}/items/{id}/withdraw"),
            (HttpMethod.Post, $"/api/treatment-plans/{id}/rename"), (HttpMethod.Post, $"/api/treatment-plans/{id}/withdraw"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_plans_and_their_history(string role, bool allowed)
    {
        var (plan, _, _) = await SimplePlanAsync(await _api.SessionAsync("Dentist"), await _api.SessionAsync("Admin"));
        var by = await _api.SessionAsync(role);
        var reads = new[] { await by.SendAsync(HttpMethod.Get, ListUrl()), await by.SendAsync(HttpMethod.Get, PlanUrl(plan)), await by.SendAsync(HttpMethod.Get, PlanUrl(plan, "/history")) };
        Assert.All(reads, r => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_dentists_and_administrators_can_create_add_withdraw_and_rename(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var (plan, diagnosis, procedure) = await SimplePlanAsync(dentist, admin);
        var itemId = plan.GetProperty("items")[0].GetProperty("id").GetString()!;
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await CreatePlan(by, [Item(diagnosis, procedure)], "by-role")).Status,
            (await by.SendAsync(HttpMethod.Post, PlanUrl(plan, "/items"), new { idempotencyKey = "extra", diagnosisId = diagnosis, procedureId = procedure, rowVersion = Version(plan) })).Status,
            (await by.SendAsync(HttpMethod.Post, PlanUrl(plan, $"/items/{itemId}/withdraw"), new { reason = "why", rowVersion = Version(plan) })).Status,
            (await by.SendAsync(HttpMethod.Post, PlanUrl(plan, "/rename"), new { title = "Renamed", rowVersion = Version(plan) })).Status,
            (await by.SendAsync(HttpMethod.Post, PlanUrl(plan, "/withdraw"), new { reason = "why", rowVersion = Version(plan) })).Status,
        };
        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            var now = (await dentist.SendAsync(HttpMethod.Get, PlanUrl(plan))).Body;      // nothing was changed
            Assert.Equal(("Periodontal plan", "Proposed", 1), (now.GetProperty("title").GetString(), now.GetProperty("status").GetString(), now.GetProperty("items").GetArrayLength()));
            Assert.Equal(1, await _s.CountAsync(db => db.TreatmentPlans));
        }
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var (plan, diagnosis, procedure) = await SimplePlanAsync(dentist, admin);
        var itemId = plan.GetProperty("items")[0].GetProperty("id").GetString()!;
        var statuses = new[]
        {
            (await dentist.SendAsync(HttpMethod.Post, ListUrl(), new { idempotencyKey = "no-csrf", title = "x", items = new[] { Item(diagnosis, procedure) } }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/items"), new { idempotencyKey = "no-csrf", diagnosisId = diagnosis, procedureId = procedure, rowVersion = Version(plan) }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, $"/items/{itemId}/withdraw"), new { reason = "y", rowVersion = Version(plan) }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/rename"), new { title = "x", rowVersion = Version(plan) }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/withdraw"), new { reason = "y", rowVersion = Version(plan) }, csrf: false)).Status,
        };
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.BadRequest, s));
        var now = (await dentist.SendAsync(HttpMethod.Get, PlanUrl(plan))).Body;
        Assert.Equal((1, "Proposed", "Periodontal plan"), (await _s.CountAsync(db => db.TreatmentPlans), now.GetProperty("status").GetString(), now.GetProperty("title").GetString()));
    }

    // ---------- the workflow: acceptance 1 and 3 ----------

    [Fact]
    public async Task A_plan_links_its_diagnosis_copies_the_catalog_fee_and_every_step_is_in_its_history_with_who_and_when()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var admin = await _api.SessionAsync("Admin");
        var diagnosis = await DiagnosisAsync(dentist);
        var first = await ProcedureAsync(admin, "LOCAL-100", 50m);
        var second = await ProcedureAsync(admin, "LOCAL-200", 70.25m);

        var created = await CreatePlan(dentist, [Item(diagnosis, first)]);
        Assert.Equal(HttpStatusCode.OK, created.Status);
        var item = created.Body.GetProperty("items")[0];
        Assert.Equal((diagnosis, first, 50m, "Proposed", 1, 50m), (item.GetProperty("diagnosisId").GetString(), item.GetProperty("procedureId").GetString(), item.GetProperty("fee").GetDecimal(),
            created.Body.GetProperty("status").GetString(), created.Body.GetProperty("activeItemCount").GetInt32(), created.Body.GetProperty("estimateTotal").GetDecimal()));
        Assert.Equal("Chronic periodontitis", item.GetProperty("diagnosisLabel").GetString());
        Assert.False(string.IsNullOrWhiteSpace(created.Body.GetProperty("estimateLabel").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(created.Body.GetProperty("createdByName").GetString()));

        var added = await dentist.SendAsync(HttpMethod.Post, PlanUrl(created.Body, "/items"), new { idempotencyKey = "second", diagnosisId = diagnosis, procedureId = second, rowVersion = Version(created.Body) });
        Assert.Equal((HttpStatusCode.OK, 120.25m), (added.Status, added.Body.GetProperty("estimateTotal").GetDecimal()));

        var renamed = await dentist.SendAsync(HttpMethod.Post, PlanUrl(added.Body, "/rename"), new { title = "Gum health plan", rowVersion = Version(added.Body) });
        Assert.Equal("Gum health plan", renamed.Body.GetProperty("title").GetString());

        var firstItemId = renamed.Body.GetProperty("items")[0].GetProperty("id").GetString()!;
        var itemWithdrawn = await dentist.SendAsync(HttpMethod.Post, PlanUrl(renamed.Body, $"/items/{firstItemId}/withdraw"), new { reason = "Patient declined this one", rowVersion = Version(renamed.Body) });
        Assert.Equal((70.25m, 1), (itemWithdrawn.Body.GetProperty("estimateTotal").GetDecimal(), itemWithdrawn.Body.GetProperty("activeItemCount").GetInt32()));

        var list = (await hygienist.SendAsync(HttpMethod.Get, ListUrl())).Body.GetProperty("plans");
        Assert.Equal(Id(created.Body), Id(list[0]));

        var withdrawn = await dentist.SendAsync(HttpMethod.Post, PlanUrl(itemWithdrawn.Body, "/withdraw"), new { reason = "Entered on the wrong patient", rowVersion = Version(itemWithdrawn.Body) });
        Assert.Equal(("Withdrawn", "Entered on the wrong patient"), (withdrawn.Body.GetProperty("status").GetString(), withdrawn.Body.GetProperty("withdrawnReason").GetString()));
        Assert.Equal(0, (await hygienist.SendAsync(HttpMethod.Get, ListUrl())).Body.GetProperty("plans").GetArrayLength());                                        // withdrawn plans are hidden by default
        Assert.Equal(1, (await hygienist.SendAsync(HttpMethod.Get, ListUrl() + "?includeWithdrawn=true")).Body.GetProperty("plans").GetArrayLength());

        var history = (await hygienist.SendAsync(HttpMethod.Get, PlanUrl(created.Body, "/history"))).Body;
        var events = history.ValueKind == JsonValueKind.Array ? history : history.GetProperty("events");
        Assert.Equal(6, events.GetArrayLength());                                                                                                                   // created, its first item, second item, rename, item withdrawn, plan withdrawn
        Assert.All(events.EnumerateArray(), e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("actorName").GetString())));
        Assert.Equal(6, await _s.CountAsync(db => db.AuditLogEntries.Where(a => a.EntityType == "TreatmentPlan")));                                                  // acceptance 3: each change is logged with user and time
    }

    [Fact]
    public async Task Retrying_a_create_with_the_same_key_returns_the_same_plan_and_saves_nothing_new()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var diagnosis = await DiagnosisAsync(dentist);
        var procedure = await ProcedureAsync(admin);
        var one = await CreatePlan(dentist, [Item(diagnosis, procedure)], "same-key");
        var two = await CreatePlan(dentist, [Item(diagnosis, procedure)], "same-key");
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK, Id(one.Body)), (one.Status, two.Status, Id(two.Body)));
        Assert.Equal((1, 1), (await _s.CountAsync(db => db.TreatmentPlans), await _s.CountAsync(db => db.TreatmentPlanItems)));
    }

    // ---------- acceptance 2: an error prompts for correction ----------

    [Fact]
    public async Task A_plan_with_mistakes_is_refused_with_a_message_for_every_field_to_correct_and_saves_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var procedure = await ProcedureAsync(admin);
        var r = await CreatePlan(dentist, [new { diagnosisId = (string?)null, procedureId = procedure, toothKey = (string?)null, surface = (string?)null }], title: " ");
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        var fields = r.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("items[0].diagnosisId", fields);
        Assert.Contains("title", fields);
        Assert.All(r.Body.GetProperty("fieldErrors").EnumerateObject(), p => Assert.False(string.IsNullOrWhiteSpace(p.Value.GetString())));
        Assert.Equal(0, await _s.CountAsync(db => db.TreatmentPlans));
    }

    [Fact]
    public async Task A_plan_with_no_procedures_or_another_patients_diagnosis_is_refused()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var procedure = await ProcedureAsync(admin);
        var bosDiagnosis = await DiagnosisAsync(dentist, _bo);
        var none = await CreatePlan(dentist, []);
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (none.Status, Error(none.Body)));
        var wrongPatient = await CreatePlan(dentist, [Item(bosDiagnosis, procedure)]);                                  // Ann's plan, Bo's diagnosis
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (wrongPatient.Status, Error(wrongPatient.Body)));
        Assert.Contains("items[0].diagnosisId", wrongPatient.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name));
        Assert.Equal(0, await _s.CountAsync(db => db.TreatmentPlans));
    }

    [Fact]
    public async Task A_withdrawal_without_a_reason_is_refused_and_a_missing_or_malformed_version_is_a_400()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (plan, _, _) = await SimplePlanAsync(dentist, await _api.SessionAsync("Admin"));
        var noReason = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/withdraw"), new { reason = " ", rowVersion = Version(plan) });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, Error(noReason.Body)));
        Assert.Contains("reason", noReason.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name));
        var noVersion = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/rename"), new { title = "Renamed" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var badVersion = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/rename"), new { title = "Renamed", rowVersion = "not-a-version" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_invalid"), (badVersion.Status, Error(badVersion.Body)));
        Assert.Equal("Proposed", (await dentist.SendAsync(HttpMethod.Get, PlanUrl(plan))).Body.GetProperty("status").GetString());
    }

    // ---------- conflicts and missing things ----------

    [Fact]
    public async Task A_stale_change_is_the_409_conflict_and_the_first_change_stands()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var (plan, _, _) = await SimplePlanAsync(dentist, admin);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/rename"), new { title = "First change", rowVersion = Version(plan) })).Status);
        var stale = await admin.SendAsync(HttpMethod.Post, PlanUrl(plan, "/rename"), new { title = "Second change", rowVersion = Version(plan) });        // the administrator still holds the first version
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        Assert.Equal("First change", (await dentist.SendAsync(HttpMethod.Get, PlanUrl(plan))).Body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_withdrawn_plan_cannot_be_changed_and_a_missing_plan_item_or_patient_is_a_404()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var (plan, diagnosis, procedure) = await SimplePlanAsync(dentist, admin);
        var itemId = plan.GetProperty("items")[0].GetProperty("id").GetString()!;

        var missingItem = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, $"/items/{Guid.NewGuid()}/withdraw"), new { reason = "why", rowVersion = Version(plan) });
        Assert.Equal((HttpStatusCode.NotFound, "item_not_found"), (missingItem.Status, Error(missingItem.Body)));
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Guid.NewGuid()}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Guid.NewGuid()}/history")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, ListUrl(Guid.NewGuid()))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CreatePlan(dentist, [Item(diagnosis, procedure)], "ghost", patient: Guid.NewGuid())).Status);

        var w = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/withdraw"), new { reason = "Entered in error", rowVersion = Version(plan) });
        Assert.Equal(HttpStatusCode.OK, w.Status);
        var attempts = new[]
        {
            await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/rename"), new { title = "x", rowVersion = Version(w.Body) }),
            await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/items"), new { idempotencyKey = "late", diagnosisId = diagnosis, procedureId = procedure, rowVersion = Version(w.Body) }),
            await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, $"/items/{itemId}/withdraw"), new { reason = "why", rowVersion = Version(w.Body) }),
        };
        Assert.All(attempts, a => Assert.Equal((HttpStatusCode.Conflict, "plan_withdrawn"), (a.Status, Error(a.Body))));
        var again = await dentist.SendAsync(HttpMethod.Post, PlanUrl(plan, "/withdraw"), new { reason = "again", rowVersion = Version(w.Body) });          // withdrawing twice is a safe replay: the first reason stands, nothing new is saved
        Assert.Equal((HttpStatusCode.OK, "Entered in error"), (again.Status, again.Body.GetProperty("withdrawnReason").GetString()));
    }

    [Fact]
    public async Task A_patients_plan_list_does_not_show_another_patients_plans()
    {
        var dentist = await _api.SessionAsync("Dentist");
        await SimplePlanAsync(dentist, await _api.SessionAsync("Admin"));
        var bosPlans = (await dentist.SendAsync(HttpMethod.Get, ListUrl(_bo))).Body.GetProperty("plans");
        Assert.Equal(0, bosPlans.GetArrayLength());
    }
}
