using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Treatment;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 at the HTTP boundary: who may read revisions and who may revise, answer, schedule, complete, code a plan or resolve a diagnosis's old reference; the CSRF and row-version contract; the stable
/// refusal shape that names every field to correct; the partial-acceptance, withdrawn-plan, already-resolved and stale-version conflicts; the estimate label that says what the figure is not; and the whole
/// workflow end to end. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class TreatmentPlanCompletionApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _annEncounter, _boEncounter;
    private int _codes;

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
    private static string Item(JsonElement plan, int i) => plan.GetProperty("items")[i].GetProperty("id").GetString()!;
    private static string[] Fields(JsonElement e) => e.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray();
    private static string P(JsonElement plan, string tail = "") => $"/api/treatment-plans/{Id(plan)}{tail}";

    private async Task<string> DiagnosisAsync(Session dentist, Guid? patient = null, string? reference = null)
    {
        var p = patient ?? _ann;
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{p}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = p == _bo ? _boEncounter : _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = reference });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return Id(r.Body);
    }

    private async Task<string> ProcedureAsync(Session admin, decimal fee = 100m, string description = "Periodontal maintenance")
    {
        var r = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description, category = "Periodontic", scope = "WholeMouth", fee });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body.GetProperty("summary").GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> PlanAsync(Session dentist, Session admin, int procedures = 2, Guid? patient = null, string title = "Gum health plan")
    {
        var diagnosis = await DiagnosisAsync(dentist, patient);
        var items = new List<object>();
        for (var i = 0; i < procedures; i++) items.Add(new { diagnosisId = diagnosis, procedureId = await ProcedureAsync(admin, 100m - 30m * i, $"Procedure {i + 1}"), toothKey = (string?)null, surface = (string?)null });
        var r = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{patient ?? _ann}/treatment-plans", new { idempotencyKey = Guid.NewGuid().ToString("N"), title, items });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private static object Revise(JsonElement plan, string? reason = "Phased for healing", string? rationale = "Deep pockets", object? phases = null, object? placements = null) =>
        new { reason, clinicalRationale = rationale, patientExplanation = "We clean below the gums first.", phases, placements, rowVersion = Version(plan) };

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_new_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod, string)[]
        {
            (HttpMethod.Get, $"/api/treatment-plans/{id}/revisions"), (HttpMethod.Get, $"/api/treatment-plans/{id}/revisions/1"), (HttpMethod.Get, $"/api/treatment-plans/{id}/revisions/compare?from=1&to=2"),
            (HttpMethod.Post, $"/api/treatment-plans/{id}/revise"), (HttpMethod.Post, $"/api/treatment-plans/{id}/decision"), (HttpMethod.Post, $"/api/treatment-plans/{id}/items/{id}/decision"),
            (HttpMethod.Post, $"/api/treatment-plans/{id}/items/{id}/schedule"), (HttpMethod.Post, $"/api/treatment-plans/{id}/items/{id}/complete"), (HttpMethod.Post, $"/api/treatment-plans/{id}/reference-code"),
            (HttpMethod.Post, $"/api/patients/{id}/treatment-plans/reconcile-references"), (HttpMethod.Post, $"/api/diagnoses/{id}/resolve-plan-reference"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_revisions_and_comparisons(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan));
        var by = await _api.SessionAsync(role);
        var reads = new[] { await by.SendAsync(HttpMethod.Get, P(plan, "/revisions")), await by.SendAsync(HttpMethod.Get, P(plan, "/revisions/2")), await by.SendAsync(HttpMethod.Get, P(plan, "/revisions/compare?from=1&to=2")) };
        Assert.All(reads, r => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_dentists_and_administrators_can_revise_answer_schedule_complete_code_and_resolve(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var plan = await PlanAsync(dentist, admin);
        var diagnosis = await DiagnosisAsync(dentist, reference: "old note");
        var accepted = await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/decision"), new { decision = "Accepted", rowVersion = Version(plan) });
        Assert.Equal(HttpStatusCode.OK, accepted.Status);

        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan))).Status,
            (await by.SendAsync(HttpMethod.Post, P(plan, "/decision"), new { decision = "Accepted", rowVersion = Version(plan) })).Status,
            (await by.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 1)}/decision"), new { decision = "Accepted", rowVersion = Version(plan) })).Status,
            (await by.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/schedule"), new { scheduled = true })).Status,
            (await by.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/complete"))).Status,
            (await by.SendAsync(HttpMethod.Post, P(plan, "/reference-code"), new { code = "PLAN-1", rowVersion = Version(plan) })).Status,
            (await by.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans/reconcile-references", new { apply = true })).Status,
            (await by.SendAsync(HttpMethod.Post, $"/api/diagnoses/{diagnosis}/resolve-plan-reference", new { planId = Id(plan) })).Status,
        };
        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            var now = (await dentist.SendAsync(HttpMethod.Get, P(plan))).Body;                                   // nothing was changed
            Assert.Equal((1, "Pending", null), (now.GetProperty("currentRevision").GetInt32(), now.GetProperty("items")[1].GetProperty("acceptance").GetString(), now.GetProperty("referenceCode").GetString()));
            Assert.Equal("Accepted", now.GetProperty("items")[0].GetProperty("acceptance").GetString());
            Assert.Equal("Unscheduled", now.GetProperty("items")[0].GetProperty("scheduling").GetString());
        }
    }

    [Fact]
    public async Task A_write_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        var diagnosis = await DiagnosisAsync(dentist, reference: "old note");
        var statuses = new[]
        {
            (await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan), csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, P(plan, "/decision"), new { decision = "Accepted", rowVersion = Version(plan) }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/decision"), new { decision = "Accepted", rowVersion = Version(plan) }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/schedule"), new { scheduled = true }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/complete"), csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, P(plan, "/reference-code"), new { code = "PLAN-1", rowVersion = Version(plan) }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans/reconcile-references", new { apply = true }, csrf: false)).Status,
            (await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{diagnosis}/resolve-plan-reference", new { planId = Id(plan) }, csrf: false)).Status,
        };
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.BadRequest, s));
        var now = (await dentist.SendAsync(HttpMethod.Get, P(plan))).Body;
        Assert.Equal((1, "Pending"), (now.GetProperty("currentRevision").GetInt32(), now.GetProperty("acceptanceStatus").GetString()));
        Assert.Equal(0, await _s.CountAsync(db => db.TreatmentPlanItemStates));
        Assert.Equal(0, await _s.CountAsync(db => db.DiagnosisPlanResolutions));
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_plan_is_revised_into_phases_answered_scheduled_completed_and_compared_with_every_step_logged_and_the_estimate_labelled()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var plan = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        Assert.Equal(("Pending", 1), (plan.GetProperty("acceptanceStatus").GetString(), plan.GetProperty("currentRevision").GetInt32()));

        var revised = await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan,
            phases: new[] { new { number = 1, name = "First visit", plannedVisit = "Today", targetDate = (string?)null }, new { number = 2, name = "After healing", plannedVisit = "Two weeks later", targetDate = (string?)"2030-06-01" } },
            placements: new[] { new { itemId = Item(plan, 0), phaseNumber = (int?)1 }, new { itemId = Item(plan, 1), phaseNumber = (int?)2 } }));
        Assert.Equal(HttpStatusCode.OK, revised.Status);
        Assert.Equal((2, "Deep pockets", 2), (revised.Body.GetProperty("currentRevision").GetInt32(), revised.Body.GetProperty("clinicalRationale").GetString(), revised.Body.GetProperty("phases").GetArrayLength()));
        Assert.Equal([1, 2], revised.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("phaseNumber").GetInt32()).ToArray());

        var plan2 = revised.Body;
        var accepted = await dentist.SendAsync(HttpMethod.Post, P(plan2, $"/items/{Item(plan2, 0)}/decision"), new { decision = "Accepted", decidedByName = "Ann Alder (patient)", rowVersion = Version(plan2) });
        var declined = await dentist.SendAsync(HttpMethod.Post, P(plan2, $"/items/{Item(plan2, 1)}/decision"), new { decision = "Declined", reason = "Cannot afford it now", rowVersion = Version(plan2) });
        Assert.Equal(("Accepted", "Ann Alder (patient)", 2), (accepted.Body.GetProperty("items")[0].GetProperty("acceptance").GetString(), accepted.Body.GetProperty("items")[0].GetProperty("decidedByName").GetString(), accepted.Body.GetProperty("items")[0].GetProperty("decisionRevision").GetInt32()));
        Assert.Equal(("Partial", 100m, 170m), (declined.Body.GetProperty("acceptanceStatus").GetString(), declined.Body.GetProperty("acceptedEstimateTotal").GetDecimal(), declined.Body.GetProperty("estimateTotal").GetDecimal()));

        var conflict = await dentist.SendAsync(HttpMethod.Post, P(plan2, "/decision"), new { decision = "Accepted", rowVersion = Version(plan2) });
        Assert.Equal((HttpStatusCode.Conflict, "partial_acceptance_conflict"), (conflict.Status, Error(conflict.Body)));

        var scheduled = await dentist.SendAsync(HttpMethod.Post, P(plan2, $"/items/{Item(plan2, 0)}/schedule"), new { scheduled = true, plannedDate = "2030-05-20" });
        Assert.Equal(("Scheduled", "2030-05-20"), (scheduled.Body.GetProperty("items")[0].GetProperty("scheduling").GetString(), scheduled.Body.GetProperty("items")[0].GetProperty("plannedDate").GetString()));
        var done = await dentist.SendAsync(HttpMethod.Post, P(plan2, $"/items/{Item(plan2, 0)}/complete"));
        Assert.Equal("Completed", done.Body.GetProperty("items")[0].GetProperty("completion").GetString());
        var late = await dentist.SendAsync(HttpMethod.Post, P(plan2, $"/items/{Item(plan2, 0)}/decision"), new { decision = "Declined", reason = "x", rowVersion = Version(plan2) });
        Assert.Equal((HttpStatusCode.Conflict, "item_completed"), (late.Status, Error(late.Body)));

        var compare = (await hygienist.SendAsync(HttpMethod.Get, P(plan, "/revisions/compare?from=1&to=2"))).Body;
        Assert.Equal(["After healing", "First visit"], compare.GetProperty("phasesAdded").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).Order().ToArray());
        Assert.Equal(JsonValueKind.Null, compare.GetProperty("clinicalRationale").GetProperty("from").ValueKind);
        Assert.Equal("Deep pockets", compare.GetProperty("clinicalRationale").GetProperty("to").GetString());
        Assert.Equal((2, 0m), (compare.GetProperty("itemsMoved").GetArrayLength(), compare.GetProperty("estimateDelta").GetDecimal()));     // both procedures were placed in a phase; the money did not change
        var revisions = (await hygienist.SendAsync(HttpMethod.Get, P(plan, "/revisions"))).Body;
        Assert.Equal(["Created", "Revised"], revisions.EnumerateArray().Select(r => r.GetProperty("changeType").GetString()!).ToArray());
        Assert.Equal("Phased for healing", revisions[1].GetProperty("reason").GetString());

        var history = (await hygienist.SendAsync(HttpMethod.Get, P(plan, "/history"))).Body.EnumerateArray().Select(h => h.GetProperty("changeType").GetString()!).ToArray();
        Assert.Equal(["Created", "ItemAdded", "ItemAdded", "Revised", "ItemAccepted", "ItemDeclined", "ItemScheduled", "ItemCompleted"], history);
        Assert.All((await hygienist.SendAsync(HttpMethod.Get, P(plan, "/history"))).Body.EnumerateArray(), h => Assert.False(string.IsNullOrWhiteSpace(h.GetProperty("actorName").GetString())));
        var logged = await _s.CountAsync(db => db.AuditLogEntries.Where(a => a.EntityType == "TreatmentPlan" && a.EventType != "TreatmentPlanCreated" && a.EventType != "TreatmentPlanItemAdded"));
        Assert.Equal(5, logged);                                                                                      // revise, accept, decline, schedule, complete (the create and its two procedures were logged when the plan was made)
    }

    [Fact]
    public async Task The_estimate_is_labelled_as_the_practices_figure_everywhere_it_is_shown_and_never_claims_insurance_cover()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await PlanAsync(dentist, await _api.SessionAsync("Admin"));
        await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan));
        var label = plan.GetProperty("estimateLabel").GetString()!;
        Assert.Equal(TreatmentPlanEstimate.Label, label);
        Assert.Contains("Practice fee estimate", label);
        Assert.Contains("not an insurance estimate", label);
        Assert.Contains("not a guaranteed patient cost", label);
        foreach (var banned in new[] { "covered", "adjudicat", "approved", "insurer pays", "you will pay" }) Assert.DoesNotContain(banned, label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(label, (await dentist.SendAsync(HttpMethod.Get, P(plan, "/revisions/compare?from=1&to=2"))).Body.GetProperty("estimateLabel").GetString());
        Assert.Equal(label, (await dentist.SendAsync(HttpMethod.Get, P(plan, "/revisions/2"))).Body.GetProperty("estimateLabel").GetString());
        Assert.Equal(label, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/treatment-plans")).Body.GetProperty("plans")[0].GetProperty("estimateLabel").GetString());
    }

    // ---------- refusals ----------

    [Fact]
    public async Task A_wrong_revision_or_answer_is_refused_with_a_message_for_every_field_and_saves_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await PlanAsync(dentist, await _api.SessionAsync("Admin"));

        var bad = await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan, reason: " ", phases: new[] { new { number = 0, name = " ", plannedVisit = (string?)null, targetDate = (string?)null } }));
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (bad.Status, Error(bad.Body)));
        Assert.Equal(new[] { "phases[0].name", "phases[0].number", "reason" }, Fields(bad.Body));
        var noReason = await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/decision"), new { decision = "Declined", rowVersion = Version(plan) });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.Status);
        Assert.Equal(new[] { "reason" }, Fields(noReason.Body));
        var wrongWord = await dentist.SendAsync(HttpMethod.Post, P(plan, "/decision"), new { decision = "Maybe", rowVersion = Version(plan) });
        Assert.Equal(new[] { "decision" }, Fields(wrongWord.Body));
        var oldDate = await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/schedule"), new { scheduled = true, plannedDate = "1990-01-01" });
        Assert.Equal(HttpStatusCode.Conflict, oldDate.Status);                                                       // not accepted yet: refused before the date is even judged
        Assert.Equal("item_not_accepted", Error(oldDate.Body));
        var badCode = await dentist.SendAsync(HttpMethod.Post, P(plan, "/reference-code"), new { code = "  ", rowVersion = Version(plan) });
        Assert.Equal(new[] { "referenceCode" }, Fields(badCode.Body));

        Assert.Equal((1, 0), ((await dentist.SendAsync(HttpMethod.Get, P(plan))).Body.GetProperty("currentRevision").GetInt32(), await _s.CountAsync(db => db.TreatmentPlanItemStates)));
    }

    [Fact]
    public async Task A_stale_copy_is_a_409_conflict_a_withdrawn_plan_cannot_change_and_unknown_things_are_404()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var plan = await PlanAsync(dentist, admin);
        var revised = await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan));

        var staleRevise = await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), Revise(plan, rationale: "Other"));
        var staleDecision = await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{Item(plan, 0)}/decision"), new { decision = "Accepted", rowVersion = Version(plan) });
        Assert.Equal([("concurrency_conflict", HttpStatusCode.Conflict), ("concurrency_conflict", HttpStatusCode.Conflict)], new[] { (Error(staleRevise.Body), staleRevise.Status), (Error(staleDecision.Body), staleDecision.Status) });

        var withdrawn = await dentist.SendAsync(HttpMethod.Post, P(plan, "/withdraw"), new { reason = "Entered in error", rowVersion = Version(revised.Body) });
        var w = withdrawn.Body;
        var attempts = new[]
        {
            await dentist.SendAsync(HttpMethod.Post, P(w, "/revise"), Revise(w)), await dentist.SendAsync(HttpMethod.Post, P(w, "/decision"), new { decision = "Accepted", rowVersion = Version(w) }),
            await dentist.SendAsync(HttpMethod.Post, P(w, $"/items/{Item(w, 0)}/schedule"), new { scheduled = true }),
        };
        Assert.All(attempts, a => Assert.Equal((HttpStatusCode.Conflict, "plan_withdrawn"), (a.Status, Error(a.Body))));

        var unknown = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{unknown}/revisions")).Status);
        Assert.Equal("revision_not_found", Error((await dentist.SendAsync(HttpMethod.Get, P(plan, "/revisions/99"))).Body));
        Assert.Equal("revision_not_found", Error((await dentist.SendAsync(HttpMethod.Get, P(plan, "/revisions/compare?from=1&to=99"))).Body));
        Assert.Equal("item_not_found", Error((await dentist.SendAsync(HttpMethod.Post, P(plan, $"/items/{unknown}/complete"))).Body));
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{unknown}/treatment-plans/reconcile-references", new { apply = false })).Status);
    }

    // ---------- reconciliation ----------

    [Fact]
    public async Task An_old_diagnosis_reference_is_previewed_resolved_to_the_same_patients_plan_and_kept_and_another_patients_plan_is_refused()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var plan = await PlanAsync(dentist, admin);
        var bosPlan = await PlanAsync(dentist, admin, 1, _bo, "Bo's plan");
        var diagnosis = await DiagnosisAsync(dentist, reference: "plan-7");
        var unmatched = await DiagnosisAsync(dentist, reference: "no such plan");

        var coded = await dentist.SendAsync(HttpMethod.Post, P(plan, "/reference-code"), new { code = "PLAN-7", rowVersion = Version(plan) });
        Assert.Equal("PLAN-7", coded.Body.GetProperty("referenceCode").GetString());

        var preview = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans/reconcile-references", new { apply = false });
        Assert.Equal((false, 1, 1), (preview.Body.GetProperty("applied").GetBoolean(), preview.Body.GetProperty("matched").GetInt32(), preview.Body.GetProperty("unresolved").GetInt32()));
        Assert.Equal(0, await _s.CountAsync(db => db.DiagnosisPlanResolutions));

        var applied = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans/reconcile-references", new { apply = true });
        Assert.Equal((true, 1), (applied.Body.GetProperty("applied").GetBoolean(), applied.Body.GetProperty("matched").GetInt32()));
        var again = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans/reconcile-references", new { apply = true });
        Assert.Equal((0, 1), (again.Body.GetProperty("matched").GetInt32(), again.Body.GetProperty("alreadyResolved").GetInt32()));

        var diagnoses = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/diagnoses")).Body.GetProperty("diagnoses").EnumerateArray().ToDictionary(d => Id(d));
        Assert.Equal(("Resolved", "plan-7", Id(plan), "Gum health plan"), (diagnoses[diagnosis].GetProperty("treatmentPlanReferenceState").GetString(), diagnoses[diagnosis].GetProperty("treatmentPlanReference").GetString(),
            diagnoses[diagnosis].GetProperty("resolvedPlanId").GetString(), diagnoses[diagnosis].GetProperty("resolvedPlanTitle").GetString()));
        Assert.Equal(("Unresolved", "no such plan"), (diagnoses[unmatched].GetProperty("treatmentPlanReferenceState").GetString(), diagnoses[unmatched].GetProperty("treatmentPlanReference").GetString()));

        var cross = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{unmatched}/resolve-plan-reference", new { planId = Id(bosPlan) });
        var unknown = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{unmatched}/resolve-plan-reference", new { planId = Guid.NewGuid() });
        Assert.Equal((HttpStatusCode.NotFound, "plan_not_found", cross.Body.GetProperty("message").GetString()), (cross.Status, Error(cross.Body), unknown.Body.GetProperty("message").GetString()));
        var chosen = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{unmatched}/resolve-plan-reference", new { planId = Id(plan) });
        Assert.Equal(("Resolved", "Chosen"), (chosen.Body.GetProperty("outcome").GetString(), chosen.Body.GetProperty("method").GetString()));
        var repeat = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{unmatched}/resolve-plan-reference", new { planId = Id(plan) });
        Assert.Equal("AlreadyResolved", repeat.Body.GetProperty("outcome").GetString());

        var current = (await dentist.SendAsync(HttpMethod.Get, $"/api/diagnoses/{diagnosis}")).Body;
        var correction = await dentist.SendAsync(HttpMethod.Post, $"/api/diagnoses/{diagnosis}/correct", new { rowVersion = Version(current), label = "Chronic periodontitis", treatmentPlanReference = "PLAN-8", reason = "Wrong plan" });
        Assert.Equal((HttpStatusCode.Conflict, "treatment_plan_reference_resolved"), (correction.Status, Error(correction.Body)));
    }
}
