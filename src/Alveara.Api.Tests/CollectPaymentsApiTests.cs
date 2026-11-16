using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (OD-27, briefing section 8 row "CollectPayments"): front desk takes a payment at checkout and applies what it still holds through the real billing routes, and nothing else. Every other billing
/// write (charge, reversal, adjustment, refund, method correction, allocation move, responsibility, statement issue, payment-method configuration) still answers 403 to front desk and leaves the account as it was.
/// The roles that hold ManageBilling keep both routes; the clinical roles get neither. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class CollectPaymentsApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _annEncounter;
    private int _codes;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        _annEncounter = e.Id;
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Version(JsonElement account) => account.GetProperty("rowVersion").GetString()!;
    private static decimal Balance(JsonElement account) => account.GetProperty("balanceOwed").GetDecimal();
    private static int Entries(JsonElement account) => account.GetProperty("entries").GetArrayLength();
    private string Url(string tail = "") => $"/api/patients/{_ann}/billing{tail}";

    private static object Pay(string version, decimal amount, string key, object? allocations = null) =>
        new { amount, method = "Cash", note = (string?)null, rowVersion = version, idempotencyKey = key, allocations, paidByPatientId = (Guid?)null };

    /// <summary>Two procedures (fee 100 and 70) completed for Ann and billed by the billing person: entries 1 and 2, balance 170. Same arrangement as BillingLedgerApiTests.BilledAsync.</summary>
    private async Task<JsonElement> BilledAsync()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var diagnosis = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, diagnosis.Status);
        var items = new List<object>();
        foreach (var fee in new[] { 100m, 70m })
        {
            var procedure = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{300 + ++_codes}", description = $"Procedure {_codes}", category = "Periodontic", scope = "WholeMouth", fee });
            Assert.Equal(HttpStatusCode.OK, procedure.Status);
            items.Add(new { diagnosisId = diagnosis.Body.GetProperty("id").GetString(), procedureId = procedure.Body.GetProperty("summary").GetProperty("id").GetString(), toothKey = (string?)null, surface = (string?)null });
        }
        var plan = (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans", new { idempotencyKey = Guid.NewGuid().ToString("N"), title = "Gum health plan", items })).Body;
        var planId = plan.GetProperty("id").GetString()!;
        var ids = new[] { plan.GetProperty("items")[0].GetProperty("id").GetString()!, plan.GetProperty("items")[1].GetProperty("id").GetString()! };
        var version = plan.GetProperty("rowVersion").GetString();
        foreach (var id in ids)
        {
            var step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{planId}/items/{id}/decision", new { decision = "Accepted", rowVersion = version });
            Assert.Equal(HttpStatusCode.OK, step.Status);
            version = step.Body.GetProperty("rowVersion").GetString();
            await LegacyCompletionFixture.CompleteItemAsync(_fixture, planId, id, _s.Actor);
        }
        var billing = await _api.SessionAsync("Billing");
        await billing.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = ids[0], idempotencyKey = "c-1" });
        var both = await billing.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = ids[1], idempotencyKey = "c-2" });
        Assert.Equal((HttpStatusCode.OK, 170m), (both.Status, Balance(both.Body)));
        return both.Body;
    }

    [Theory]
    [InlineData("FrontDesk", true)]
    [InlineData("Billing", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Admin", true)]
    [InlineData("Dentist", false)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    public async Task Front_desk_and_the_billing_roles_may_take_and_apply_a_payment_the_clinical_roles_may_not(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Url("/payments"), Pay("AAAA", 10m, "p-x"))).Status,                                                   // a stale version: 409 for those allowed to ask, 403 for those who are not
            (await by.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 1, rowVersion = "AAAA", idempotencyKey = "a-x" })).Status,
        };
        Assert.Equal(allowed ? new[] { HttpStatusCode.Conflict, HttpStatusCode.Conflict } : new[] { HttpStatusCode.Forbidden, HttpStatusCode.Forbidden }, statuses);
    }

    [Fact]
    public async Task Front_desk_takes_a_payment_applies_the_rest_and_the_account_still_verifies()
    {
        var account = await BilledAsync();
        var desk = await _api.SessionAsync("FrontDesk");

        var paid = await desk.SendAsync(HttpMethod.Post, Url("/payments"), Pay(Version(account), 100m, "p-1", new[] { new { chargeEntryNumber = 2, amount = 40m } }));
        Assert.Equal((HttpStatusCode.OK, 70m, 60m), (paid.Status, Balance(paid.Body), paid.Body.GetProperty("unappliedTotal").GetDecimal()));
        var again = await desk.SendAsync(HttpMethod.Post, Url("/payments"), Pay(Version(account), 100m, "p-1", new[] { new { chargeEntryNumber = 2, amount = 40m } }));   // a retry with the same key
        Assert.Equal((HttpStatusCode.OK, 70m, 3), (again.Status, Balance(again.Body), Entries(again.Body)));                                                                 // records nothing twice

        var applied = await desk.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 3, rowVersion = Version(paid.Body), idempotencyKey = "a-1" });
        Assert.Equal((HttpStatusCode.OK, 0m), (applied.Status, applied.Body.GetProperty("unappliedTotal").GetDecimal()));

        var check = await desk.SendAsync(HttpMethod.Get, Url("/verification"));
        Assert.Equal((HttpStatusCode.OK, true, 70m), (check.Status, check.Body.GetProperty("consistent").GetBoolean(), check.Body.GetProperty("balanceOwed").GetDecimal()));
    }

    [Fact]
    public async Task Front_desk_is_refused_every_other_billing_change_and_the_account_is_left_as_it_was()
    {
        var account = await BilledAsync();
        var desk = await _api.SessionAsync("FrontDesk");
        var paid = await desk.SendAsync(HttpMethod.Post, Url("/payments"), Pay(Version(account), 50m, "p-1"));      // entry 3, so the refusals below are about a real payment
        Assert.Equal(HttpStatusCode.OK, paid.Status);
        var v = Version(paid.Body);

        var calls = new (string Route, HttpStatusCode Status)[]
        {
            ("charges", (await desk.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = Guid.NewGuid(), idempotencyKey = "c-x" })).Status),
            ("reversals", (await desk.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 3, reason = "x", rowVersion = v, idempotencyKey = "r-x" })).Status),
            ("adjustments", (await desk.SendAsync(HttpMethod.Post, Url("/adjustments"), new { amount = -10m, reason = "Courtesy", rowVersion = v, idempotencyKey = "j-x" })).Status),
            ("refunds", (await desk.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 10m, method = "Cash", reason = "x", rowVersion = v, idempotencyKey = "f-x" })).Status),
            ("corrections (method change)", (await desk.SendAsync(HttpMethod.Post, Url("/corrections"), new { entryNumber = 3, reason = "Wrong method", amount = 50m, method = "Check", rowVersion = v, idempotencyKey = "k-x" })).Status),
            ("allocations/move", (await desk.SendAsync(HttpMethod.Post, Url("/allocations/move"), new { paymentEntryNumber = 3, releases = Array.Empty<object>(), allocations = Array.Empty<object>(), reason = "x", rowVersion = v, idempotencyKey = "m-x" })).Status),
            ("responsibility", (await desk.SendAsync(HttpMethod.Post, Url("/responsibility"), new { responsiblePatientId = _ann, reason = "x", rowVersion = v })).Status),
            ("documents", (await desk.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = "x", idempotencyKey = "d-x" })).Status),
            ("payment-methods", (await desk.SendAsync(HttpMethod.Post, "/api/billing/payment-methods", new { name = "Venmo", kind = "Other" })).Status),
            ("payment-methods/active", (await desk.SendAsync(HttpMethod.Post, $"/api/billing/payment-methods/{Guid.NewGuid()}/active", new { active = false, rowVersion = "AAAA" })).Status),
        };
        Assert.All(calls, c => Assert.True(c.Status == HttpStatusCode.Forbidden, $"{c.Route} answered {(int)c.Status}"));

        var after = (await desk.SendAsync(HttpMethod.Get, Url())).Body;
        Assert.Equal((v, 120m, 3), (Version(after), Balance(after), Entries(after)));
    }

    [Fact]
    public async Task A_front_desk_payment_without_the_csrf_token_records_nothing()
    {
        var account = await BilledAsync();
        var desk = await _api.SessionAsync("FrontDesk");
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, Url("/payments"), Pay(Version(account), 50m, "p-1"), csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 1, rowVersion = Version(account), idempotencyKey = "a-1" }, csrf: false)).Status);
        var after = (await desk.SendAsync(HttpMethod.Get, Url())).Body;
        Assert.Equal((170m, 2), (Balance(after), Entries(after)));
    }
}
