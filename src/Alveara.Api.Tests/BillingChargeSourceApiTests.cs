using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 R02 (review finding ALV-007-C02-R01-01): the account timeline may link a charge back to the completed care it bills, and the timeline is read with ViewBilling, so the destination of that link must
/// be readable with ViewBilling too. Real API, real SQL Server, a real signed-in session per role. A charge's source is a narrow billing projection (the procedure, the tooth and surface, the charge, when the
/// completion was recorded, the performed time and whether it was amended); it never carries the provider, the encounter, the chart effect, the diagnosis or any clinical wording, and the roles that cannot read
/// the clinical record are still refused it.
/// </summary>
public class BillingChargeSourceApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _annEncounter;
    private int _codes;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        _annEncounter = e.Id;
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;
    private string Source(int entry, Guid? patient = null) => $"/api/patients/{patient ?? _ann}/billing/entries/{entry}/source";

    /// <summary>Two accepted procedures for Ann; the first is completed the way the product completes (a recorded completion, so the charge has a source record), the second the old state-only way (a charge with no record).</summary>
    private async Task<(Session Billing, Session Dentist)> ChargedAsync()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var d = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = "Private clinical note", treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, d.Status);
        var items = new List<object>();
        for (var i = 0; i < 2; i++)
        {
            var p = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description = $"Procedure {i + 1}", category = "Periodontic", scope = "WholeMouth", fee = 100m });
            Assert.Equal(HttpStatusCode.OK, p.Status);
            items.Add(new { diagnosisId = Id(d.Body), procedureId = p.Body.GetProperty("summary").GetProperty("id").GetString()!, toothKey = (string?)null, surface = (string?)null });
        }
        var plan = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans", new { idempotencyKey = Guid.NewGuid().ToString("N"), title = "Gum health plan", items });
        Assert.Equal(HttpStatusCode.OK, plan.Status);
        var accepted = plan.Body;
        foreach (var item in accepted.GetProperty("items").EnumerateArray())
        {
            var step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(accepted)}/items/{Id(item)}/decision", new { decision = "Accepted", rowVersion = accepted.GetProperty("rowVersion").GetString() });
            Assert.Equal(HttpStatusCode.OK, step.Status);
            accepted = step.Body;
        }
        var first = accepted.GetProperty("items")[0];
        var second = accepted.GetProperty("items")[1];
        var done = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(accepted)}/complete-procedures",
            new { idempotencyKey = "set-1", rowVersion = accepted.GetProperty("rowVersion").GetString(), encounterId = _annEncounter, providerId = _s.ProviderA, performedAtUtc = DateTimeOffset.UtcNow.AddSeconds(2), itemIds = new[] { Id(first) } });
        Assert.Equal(HttpStatusCode.OK, done.Status);
        await LegacyCompletionFixture.CompleteItemAsync(_fixture, Id(accepted), Id(second), _s.Actor);                                      // the legacy, state-only way (the route is retired; see LegacyCompletionFixture)

        var billing = await _api.SessionAsync("Billing");
        Assert.Equal(HttpStatusCode.OK, (await billing.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/billing/charges", new { planItemId = Id(second), idempotencyKey = "c-2" })).Status);   // the first charge was made by the completion itself
        return (billing, dentist);
    }

    [Fact]
    public async Task A_billing_only_reader_can_follow_a_charges_source_and_still_cannot_read_the_clinical_record()
    {
        var (billing, _) = await ChargedAsync();

        var timeline = (await billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/billing/timeline")).Body.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "Charge").ToList();
        Assert.Equal(2, timeline.Count);
        var linked = timeline[0];
        Assert.NotEqual(JsonValueKind.Null, linked.GetProperty("completedProcedureId").ValueKind);                    // the timeline names the completed procedure behind the charge...

        var source = await billing.SendAsync(HttpMethod.Get, Source(linked.GetProperty("number").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, source.Status);                                                                // ...and a billing reader can open that source
        var body = source.Body;
        Assert.Equal((true, 100m, false), (body.GetProperty("completionRecorded").GetBoolean(), body.GetProperty("chargeAmount").GetDecimal(), body.GetProperty("chargeReversed").GetBoolean()));
        Assert.Equal(linked.GetProperty("completedProcedureId").GetString(), body.GetProperty("completedProcedureId").GetString());
        Assert.StartsWith("LOCAL-", body.GetProperty("procedureCode").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("performedAtUtc").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("completedAtUtc").ValueKind);
        Assert.False(body.GetProperty("performedTimeAmended").GetBoolean());

        // the projection carries nothing clinical: no provider, encounter, chart outcome, diagnosis, note or plan wording
        var names = body.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.DoesNotContain(names, n => n.Contains("provider") || n.Contains("encounter") || n.Contains("chart") || n.Contains("diagnos") || n.Contains("note") || n.Contains("rationale") || n.Contains("finding"));
        Assert.DoesNotContain("Private clinical note", body.GetRawText());

        // the same role is still refused the clinical record itself
        Assert.Equal(HttpStatusCode.Forbidden, (await billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/completed-procedures")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await billing.SendAsync(HttpMethod.Get, $"/api/completed-procedures/{linked.GetProperty("completedProcedureId").GetString()}")).Status);
    }

    [Fact]
    public async Task A_charge_completed_the_old_way_has_a_source_that_says_no_completion_record_exists()
    {
        var (billing, _) = await ChargedAsync();
        var source = await billing.SendAsync(HttpMethod.Get, Source(2));
        Assert.Equal(HttpStatusCode.OK, source.Status);
        Assert.Equal((false, JsonValueKind.Null, JsonValueKind.Null), (source.Body.GetProperty("completionRecorded").GetBoolean(), source.Body.GetProperty("completedProcedureId").ValueKind, source.Body.GetProperty("performedAtUtc").ValueKind));
        Assert.Equal(100m, source.Body.GetProperty("chargeAmount").GetDecimal());
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Billing", true)]
    [InlineData("Dentist", true)]
    [InlineData("FrontDesk", true)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    public async Task Whoever_can_see_billing_can_read_a_charges_source_and_nobody_else_can(string role, bool allowed)
    {
        await ChargedAsync();
        var by = await _api.SessionAsync(role);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, (await by.SendAsync(HttpMethod.Get, Source(1))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Factory.CreateClient().GetAsync(Source(1))).StatusCode);
    }

    [Fact]
    public async Task A_source_is_only_for_a_charge_of_that_patient_and_never_another_patients_or_another_kind_of_entry()
    {
        var (billing, _) = await ChargedAsync();
        await using (var db = _fixture.CreateContext()) Assert.Equal(2, await db.BillingEntries.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await billing.SendAsync(HttpMethod.Get, Source(1, _bo))).Status);      // Bo has no such entry; Ann's entry does not leak through Bo's route
        Assert.Equal(HttpStatusCode.NotFound, (await billing.SendAsync(HttpMethod.Get, Source(99))).Status);
        var account = await billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/billing");
        var paid = await billing.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/billing/payments",
            new { amount = 10m, method = "Cash", note = (string?)null, rowVersion = account.Body.GetProperty("rowVersion").GetString(), idempotencyKey = "p-1" });
        Assert.Equal(HttpStatusCode.OK, paid.Status);
        var refusal = await billing.SendAsync(HttpMethod.Get, Source(3));                                               // entry 3 is a payment
        Assert.Equal((HttpStatusCode.NotFound, "source_not_found"), (refusal.Status, refusal.Body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task A_reversed_charge_says_so_and_a_later_amendment_of_the_performed_time_is_shown_as_amended()
    {
        var (billing, dentist) = await ChargedAsync();
        string procedure;
        await using (var db = _fixture.CreateContext()) procedure = (await db.CompletedProcedures.AsNoTracking().SingleAsync()).Id.ToString();
        var amended = await dentist.SendAsync(HttpMethod.Post, $"/api/completed-procedures/{procedure}/amend",
            new { idempotencyKey = "amend-1", expectedAmendmentNumber = 0, performedAtUtc = DateTimeOffset.UtcNow, reason = "Recorded at the wrong time" });
        Assert.Equal(HttpStatusCode.OK, amended.Status);
        var source = await billing.SendAsync(HttpMethod.Get, Source(1));
        Assert.True(source.Body.GetProperty("performedTimeAmended").GetBoolean());

        var account = await billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/billing");
        var reversed = await billing.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/billing/reversals",
            new { entryNumber = 1, reason = "Billed in error", rowVersion = account.Body.GetProperty("rowVersion").GetString(), idempotencyKey = "r-1" });
        Assert.Equal(HttpStatusCode.OK, reversed.Status);
        Assert.True((await billing.SendAsync(HttpMethod.Get, Source(1))).Body.GetProperty("chargeReversed").GetBoolean());
    }
}
