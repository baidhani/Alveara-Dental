using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-007 at the HTTP boundary: who may read an account (everyone who can see billing) and who may bill, take a payment or adjust (billing, the practice manager, the administrator), the CSRF and
/// account-version contract, the stable refusal shape that names every field to correct, the whole bill-pay-adjust workflow through the real routes, and the answer when the log cannot be written. A real
/// API, real SQL Server and a real signed-in session per role.
/// </summary>
public class BillingApiTests : IAsyncLifetime
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

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Version(JsonElement account) => account.GetProperty("rowVersion").GetString()!;
    private static decimal Balance(JsonElement account) => account.GetProperty("balanceOwed").GetDecimal();
    private static string[] Fields(JsonElement e) => e.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray();
    private string Url(string tail = "", Guid? patient = null) => $"/api/patients/{patient ?? _ann}/billing{tail}";

    /// <summary>A plan for Ann with two procedures (fee 100 and 70): the first accepted and completed by the dentist, the second only accepted. Returns both plan procedure ids.</summary>
    private async Task<(string Completed, string Accepted)> CompletedPlanAsync()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var diagnosis = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, diagnosis.Status);
        var items = new List<object>();
        foreach (var fee in new[] { 100m, 70m })
        {
            var procedure = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description = $"Procedure {_codes}", category = "Periodontic", scope = "WholeMouth", fee });
            Assert.Equal(HttpStatusCode.OK, procedure.Status);
            items.Add(new { diagnosisId = diagnosis.Body.GetProperty("id").GetString(), procedureId = procedure.Body.GetProperty("summary").GetProperty("id").GetString(), toothKey = (string?)null, surface = (string?)null });
        }
        var plan = (await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans", new { idempotencyKey = Guid.NewGuid().ToString("N"), title = "Gum health plan", items })).Body;
        var planId = plan.GetProperty("id").GetString();
        string ItemId(JsonElement p, int i) => p.GetProperty("items")[i].GetProperty("id").GetString()!;
        var first = ItemId(plan, 0);
        var second = ItemId(plan, 1);
        var step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{planId}/items/{first}/decision", new { decision = "Accepted", rowVersion = plan.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, step.Status);
        step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{planId}/items/{second}/decision", new { decision = "Accepted", rowVersion = step.Body.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, step.Status);
        Assert.Equal(HttpStatusCode.OK, (await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{planId}/items/{first}/complete")).Status);
        return (first, second);
    }

    private static object ChargeBody(string planItem, string key = "charge-1") => new { planItemId = planItem, idempotencyKey = key };
    private static object PaymentBody(decimal amount, string version, string key = "pay-1", string method = "Cash") => new { amount, method, note = (string?)null, rowVersion = version, idempotencyKey = key };
    private static object AdjustmentBody(decimal amount, string version, string key = "adj-1", string reason = "Courtesy") => new { amount, reason, rowVersion = version, idempotencyKey = key };

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, Url()), (HttpMethod.Get, Url("/billable-procedures")), (HttpMethod.Post, Url("/charges")), (HttpMethod.Post, Url("/payments")), (HttpMethod.Post, Url("/adjustments")),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Billing", true)]
    [InlineData("Dentist", true)]
    [InlineData("FrontDesk", true)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    public async Task Whoever_can_see_billing_can_read_an_account_and_the_billable_procedures(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var statuses = new[] { (await by.SendAsync(HttpMethod.Get, Url())).Status, (await by.SendAsync(HttpMethod.Get, Url("/billable-procedures"))).Status };
        Assert.All(statuses, s => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, s));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Billing", true)]
    [InlineData("Dentist", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    public async Task Only_billing_the_practice_manager_and_the_administrator_can_bill_take_payment_or_adjust(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(Guid.NewGuid().ToString()))).Status,            // an unknown procedure: 404 for those allowed to ask, 403 for those who are not
            (await by.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(10m, "AAAA"))).Status,
            (await by.SendAsync(HttpMethod.Post, Url("/adjustments"), AdjustmentBody(10m, "AAAA"))).Status,
        };
        Assert.All(statuses, s => Assert.Equal(allowed, s != HttpStatusCode.Forbidden));
        Assert.Equal(allowed ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, statuses[0]);
    }

    [Fact]
    public async Task Billing_cannot_read_treatment_plans_so_the_billable_list_is_the_only_view_it_gets_of_them()
    {
        var (completed, _) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");
        Assert.Equal(HttpStatusCode.Forbidden, (await billing.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/treatment-plans")).Status);

        var list = await billing.SendAsync(HttpMethod.Get, Url("/billable-procedures"));
        var only = Assert.Single(list.Body.EnumerateArray());
        Assert.Equal(completed, only.GetProperty("planItemId").GetString());
        Assert.Equal(new[] { "code", "currency", "description", "fee", "planItemId", "surface", "toothKey" }, only.EnumerateObject().Select(p => p.Name.Replace("procedureCode", "code")).Order().ToArray());   // nothing clinical
    }

    [Fact]
    public async Task Every_change_needs_a_csrf_token()
    {
        var (completed, _) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed), csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(10m, "AAAA"), csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/adjustments"), AdjustmentBody(10m, "AAAA"), csrf: false)).Status);
        Assert.False((await billing.SendAsync(HttpMethod.Get, Url())).Body.GetProperty("hasAccount").GetBoolean());
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_completed_procedure_is_billed_a_payment_lowers_the_balance_an_adjustment_corrects_it_and_the_history_keeps_every_entry()
    {
        var (completed, _) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");

        var empty = (await billing.SendAsync(HttpMethod.Get, Url())).Body;
        Assert.Equal((false, 0m), (empty.GetProperty("hasAccount").GetBoolean(), Balance(empty)));

        var charged = await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed));
        Assert.Equal(HttpStatusCode.OK, charged.Status);
        Assert.Equal(100m, Balance(charged.Body));                                              // the plan's fee: the request had no amount
        Assert.Empty((await billing.SendAsync(HttpMethod.Get, Url("/billable-procedures"))).Body.EnumerateArray());

        var paid = await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(60m, Version(charged.Body)));
        Assert.Equal(HttpStatusCode.OK, paid.Status);
        Assert.Equal(40m, Balance(paid.Body));

        var adjusted = await billing.SendAsync(HttpMethod.Post, Url("/adjustments"), AdjustmentBody(-15m, Version(paid.Body), reason: "Fee entered wrongly"));
        Assert.Equal(25m, Balance(adjusted.Body));

        var entries = (await billing.SendAsync(HttpMethod.Get, Url())).Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(new[] { "Charge", "Payment", "Adjustment" }, entries.Select(e => e.GetProperty("kind").GetString()).ToArray());
        Assert.Equal(new[] { 100m, 40m, 25m }, entries.Select(e => e.GetProperty("balanceAfter").GetDecimal()).ToArray());
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("recordedByName").GetString())));
        Assert.Equal("Fee entered wrongly", entries[2].GetProperty("reason").GetString());

        await using var db = _fixture.CreateContext();                                          // and every action is in the audit log with the user and time
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == "BillingAccount").ToListAsync();
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.True(a.PerformedByUserAccountId is not null && a.TimestampUtc != default));
    }

    [Fact]
    public async Task A_double_click_adds_nothing_twice()
    {
        var (completed, _) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");
        await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed));
        var again = await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed));
        Assert.Equal((HttpStatusCode.OK, 1), (again.Status, again.Body.GetProperty("entries").GetArrayLength()));
        var other = await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed, "another-key"));
        Assert.Equal((HttpStatusCode.Conflict, "already_billed"), (other.Status, Error(other.Body)));
    }

    // ---------- refusals ----------

    [Fact]
    public async Task A_procedure_that_is_not_completed_is_refused_and_another_patients_looks_like_an_unknown_one()
    {
        var (_, accepted) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");
        var notCompleted = await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(accepted));
        Assert.Equal((HttpStatusCode.Conflict, "not_completed"), (notCompleted.Status, Error(notCompleted.Body)));

        var wrongPatient = await billing.SendAsync(HttpMethod.Post, Url("/charges", _bo), ChargeBody(accepted));
        var unknown = await billing.SendAsync(HttpMethod.Post, Url("/charges", _bo), ChargeBody(Guid.NewGuid().ToString()));
        Assert.Equal((HttpStatusCode.NotFound, "plan_item_not_found"), (wrongPatient.Status, Error(wrongPatient.Body)));
        Assert.Equal(unknown.Body.GetProperty("message").GetString(), wrongPatient.Body.GetProperty("message").GetString());
        var noPatient = await billing.SendAsync(HttpMethod.Post, Url("/charges", Guid.NewGuid()), ChargeBody(accepted));
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (noPatient.Status, Error(noPatient.Body)));
        Assert.False((await billing.SendAsync(HttpMethod.Get, Url())).Body.GetProperty("hasAccount").GetBoolean());
    }

    [Fact]
    public async Task A_wrong_entry_is_refused_with_a_message_per_field_in_one_answer()
    {
        var (completed, _) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");
        var charged = (await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed))).Body;

        var noCharge = await billing.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = (string?)null, idempotencyKey = (string?)null });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (noCharge.Status, Error(noCharge.Body)));
        Assert.Equal(new[] { "idempotencyKey", "planItemId" }, Fields(noCharge.Body));

        var badPayment = await billing.SendAsync(HttpMethod.Post, Url("/payments"), new { amount = -5m, method = "Barter", note = (string?)null, rowVersion = Version(charged), idempotencyKey = (string?)null });
        Assert.Equal(new[] { "amount", "idempotencyKey", "method" }, Fields(badPayment.Body));
        var overPayment = await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(100.01m, Version(charged)));
        Assert.Equal(HttpStatusCode.BadRequest, overPayment.Status);
        Assert.Equal(new[] { "amount" }, Fields(overPayment.Body));
        Assert.Contains("100.00", overPayment.Body.GetProperty("fieldErrors").GetProperty("amount").GetString());

        var badAdjustment = await billing.SendAsync(HttpMethod.Post, Url("/adjustments"), new { amount = 0m, reason = (string?)null, rowVersion = Version(charged), idempotencyKey = "adj-x" });
        Assert.Equal(new[] { "amount", "reason" }, Fields(badAdjustment.Body));
        var belowZero = await billing.SendAsync(HttpMethod.Post, Url("/adjustments"), AdjustmentBody(-100.01m, Version(charged)));
        Assert.Equal(new[] { "amount" }, Fields(belowZero.Body));

        var noVersion = await billing.SendAsync(HttpMethod.Post, Url("/payments"), new { amount = 10m, method = "Cash", note = (string?)null, rowVersion = (string?)null, idempotencyKey = "pay-v" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        Assert.Equal(100m, Balance((await billing.SendAsync(HttpMethod.Get, Url())).Body));    // nothing was stored by any of them
    }

    [Fact]
    public async Task A_payment_with_nothing_billed_and_a_payment_from_an_out_of_date_account_are_refused_and_change_nothing()
    {
        var (completed, _) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");
        var nothing = await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(10m, "AAAA"));
        Assert.Equal((HttpStatusCode.Conflict, "nothing_owed"), (nothing.Status, Error(nothing.Body)));

        var charged = (await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed))).Body;
        Assert.Equal(HttpStatusCode.OK, (await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(30m, Version(charged)))).Status);
        var stale = await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(30m, Version(charged), key: "pay-2"));
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(70m, Balance((await billing.SendAsync(HttpMethod.Get, Url())).Body));
    }

    [Fact]
    public async Task When_the_log_cannot_be_written_every_billing_action_answers_503_save_failed_without_internals_and_stores_nothing()
    {
        var (completed, _) = await CompletedPlanAsync();
        var billing = await _api.SessionAsync("Billing");
        var charged = (await billing.SendAsync(HttpMethod.Post, Url("/charges"), ChargeBody(completed))).Body;
        async Task Sql(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }
        await Sql(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var pay = await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(10m, Version(charged)));
        var adjust = await billing.SendAsync(HttpMethod.Post, Url("/adjustments"), AdjustmentBody(-10m, Version(charged)));
        foreach (var r in new[] { pay, adjust })
        {
            Assert.Equal((HttpStatusCode)503, r.Status);
            Assert.Equal("save_failed", Error(r.Body));
            Assert.DoesNotContain("audit", r.Body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SqlException", r.Body.GetRawText());
        }
        Assert.Equal(100m, Balance((await billing.SendAsync(HttpMethod.Get, Url())).Body));

        await Sql("DROP TRIGGER [TR_Test_RefuseBillingAudit]");
        Assert.Equal(90m, Balance((await billing.SendAsync(HttpMethod.Post, Url("/payments"), PaymentBody(10m, Version(charged)))).Body));   // the same request now works
    }
}
