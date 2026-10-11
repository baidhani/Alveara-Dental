using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 R02 (review finding ALV-015-C01-R01-01) at the HTTP boundary, through the production wiring: the procedure catalog counts the treatment-plan procedures that refer to a procedure, so its usage
/// answer and its inactivation warning are true. Every persisted plan procedure counts (withdrawn and completed ones too, because the reference is historical); inactivating a procedure a plan uses needs the
/// caller to acknowledge it; the plan keeps showing the procedure and the fee it copied; and the inactive procedure cannot be added to new work. Only a number per source leaves the usage answer.
/// </summary>
public class TreatmentPlanProcedureUsageApiTests : IAsyncLifetime
{
    private const string Source = "Treatment plans";
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

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;

    private async Task<(string Id, string RowVersion)> ProcedureAsync(Session admin, decimal fee = 100m)
    {
        var r = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description = "Periodontal maintenance", category = "Periodontic", scope = "WholeMouth", fee });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        var summary = r.Body.GetProperty("summary");
        return (summary.GetProperty("id").GetString()!, summary.GetProperty("rowVersion").GetString()!);
    }

    private async Task<JsonElement> PlanWithAsync(Session dentist, string procedure, Guid? patient = null)
    {
        var p = patient ?? _ann;
        var dx = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{p}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = p == _bo ? _boEncounter : _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, dx.Status);
        var plan = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{p}/treatment-plans",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), title = "Gum health plan", items = new[] { new { diagnosisId = Id(dx.Body), procedureId = procedure, toothKey = (string?)null, surface = (string?)null } } });
        Assert.Equal(HttpStatusCode.OK, plan.Status);
        return plan.Body;
    }

    private static async Task<(int Total, int FromPlans)> UsageAsync(Session by, string procedure)
    {
        var r = await by.SendAsync(HttpMethod.Get, $"/api/procedures/{procedure}/usage");
        Assert.Equal(HttpStatusCode.OK, r.Status);
        var plans = r.Body.GetProperty("bySource").EnumerateArray().Where(b => b.GetProperty("source").GetString() == Source).Select(b => b.GetProperty("count").GetInt32()).ToArray();
        return (r.Body.GetProperty("count").GetInt32(), plans.Length == 0 ? -1 : plans[0]);   // -1: the source is not listed at all
    }

    [Fact]
    public async Task A_procedure_in_a_treatment_plan_is_counted_by_the_catalogs_usage_answer_and_one_no_plan_uses_is_counted_as_zero()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var used = await ProcedureAsync(admin);
        var unused = await ProcedureAsync(admin);
        Assert.Equal((0, 0), await UsageAsync(admin, used.Id));                                                  // the source is listed even when nothing refers to the procedure
        await PlanWithAsync(dentist, used.Id);

        Assert.Equal((1, 1), await UsageAsync(admin, used.Id));
        Assert.Equal((0, 0), await UsageAsync(admin, unused.Id));
    }

    [Fact]
    public async Task Every_persisted_plan_procedure_counts_including_withdrawn_and_completed_ones_and_those_of_other_patients_plans()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var procedure = await ProcedureAsync(admin);
        var first = await PlanWithAsync(dentist, procedure.Id);
        var itemId = first.GetProperty("items")[0].GetProperty("id").GetString()!;

        var withdrawn = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(first)}/items/{itemId}/withdraw", new { reason = "Entered in error", rowVersion = Version(first) });
        Assert.Equal(HttpStatusCode.OK, withdrawn.Status);
        Assert.Equal((1, 1), await UsageAsync(admin, procedure.Id));                                             // a withdrawn procedure is still a reference

        var second = await PlanWithAsync(dentist, procedure.Id, _bo);
        var secondItem = second.GetProperty("items")[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(second)}/items/{secondItem}/decision", new { decision = "Accepted", rowVersion = Version(second) })).Status);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(second)}/items/{secondItem}/complete")).Status);
        Assert.Equal((2, 2), await UsageAsync(admin, procedure.Id));                                             // a completed one, on another patient's plan, counts too
    }

    [Fact]
    public async Task Inactivating_a_procedure_a_plan_uses_needs_the_use_acknowledged_then_the_plan_still_shows_it_and_it_cannot_be_added_to_new_work()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var procedure = await ProcedureAsync(admin, 120m);
        var plan = await PlanWithAsync(dentist, procedure.Id);

        var refused = await admin.SendAsync(HttpMethod.Post, $"/api/procedures/{procedure.Id}/inactivate", new { reason = "No longer offered", rowVersion = procedure.RowVersion });
        Assert.Equal((HttpStatusCode.Conflict, "usage_confirmation_required"), (refused.Status, Error(refused.Body)));
        Assert.Equal(1, refused.Body.GetProperty("usage").GetProperty("count").GetInt32());
        Assert.Contains(refused.Body.GetProperty("usage").GetProperty("bySource").EnumerateArray(), b => b.GetProperty("source").GetString() == Source && b.GetProperty("count").GetInt32() == 1);
        Assert.Equal("Active", (await admin.SendAsync(HttpMethod.Get, $"/api/procedures/{procedure.Id}")).Body.GetProperty("summary").GetProperty("status").GetString());   // nothing changed

        var done = await admin.SendAsync(HttpMethod.Post, $"/api/procedures/{procedure.Id}/inactivate", new { reason = "No longer offered", acknowledgeUsage = true, rowVersion = procedure.RowVersion });
        Assert.Equal(HttpStatusCode.OK, done.Status);
        Assert.Equal("Inactive", done.Body.GetProperty("summary").GetProperty("status").GetString());

        // the existing plan still shows the procedure and the fee it copied
        var shown = (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Id(plan)}")).Body;
        var item = shown.GetProperty("items")[0];
        Assert.Equal((120m, 1, "Periodontal maintenance"), (item.GetProperty("fee").GetDecimal(), shown.GetProperty("activeItemCount").GetInt32(), item.GetProperty("procedureDescription").GetString()));

        // and it cannot be proposed in new work
        var again = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(shown)}/items",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), diagnosisId = item.GetProperty("diagnosisId").GetString(), procedureId = procedure.Id, rowVersion = Version(shown) });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (again.Status, Error(again.Body)));
        Assert.Contains("inactive", again.Body.GetProperty("fieldErrors").GetProperty("procedureId").GetString());
        Assert.Equal(1, (await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Id(plan)}")).Body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task The_usage_answer_carries_only_a_number_per_source_and_nothing_about_the_patient_or_the_plan()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var procedure = await ProcedureAsync(admin);
        var plan = await PlanWithAsync(dentist, procedure.Id);
        var usage = (await admin.SendAsync(HttpMethod.Get, $"/api/procedures/{procedure.Id}/usage")).Body;
        Assert.Equal(["bySource", "count", "procedureId"], usage.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.All(usage.GetProperty("bySource").EnumerateArray(), b => Assert.Equal(["count", "source"], b.EnumerateObject().Select(p => p.Name).Order().ToArray()));
        var text = usage.GetRawText();
        Assert.DoesNotContain(_ann.ToString(), text);
        Assert.DoesNotContain(Id(plan), text);
        Assert.DoesNotContain("Gum health plan", text);
    }
}
