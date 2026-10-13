using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C01 at the HTTP boundary: who may read the ledger's checks, a guarantor's summary and the payment methods (everyone who can see billing) and who may apply a payment, reverse an entry, record who is
/// responsible or configure a payment method (billing, the practice manager, the administrator), the CSRF and account-version contract, field-by-field refusals, the stable codes the screen relies on, the whole
/// allocate-reverse-verify workflow through the real routes, and the answer when the log cannot be written. A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class BillingLedgerApiTests : IAsyncLifetime
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
    private const string Methods = "/api/billing/payment-methods";

    /// <summary>A plan for Ann with two procedures (fee 100 and 70), both accepted and completed by the dentist; returns their plan procedure ids.</summary>
    private async Task<(string First, string Second)> TwoCompletedAsync()
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
        var ids = new[] { plan.GetProperty("items")[0].GetProperty("id").GetString()!, plan.GetProperty("items")[1].GetProperty("id").GetString()! };
        var version = plan.GetProperty("rowVersion").GetString();
        foreach (var id in ids)
        {
            var step = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{planId}/items/{id}/decision", new { decision = "Accepted", rowVersion = version });
            Assert.Equal(HttpStatusCode.OK, step.Status);
            version = step.Body.GetProperty("rowVersion").GetString();
            await LegacyCompletionFixture.CompleteItemAsync(_fixture, planId, id, _s.Actor);   // completed the old, state-only way (the route is retired; see LegacyCompletionFixture)
        }
        return (ids[0], ids[1]);
    }

    /// <summary>Both procedures billed by the billing person: entries 1 (100) and 2 (70), balance 170. Returns the session and the account as it stands.</summary>
    private async Task<(Session Billing, JsonElement Account)> BilledAsync()
    {
        var (first, second) = await TwoCompletedAsync();
        var billing = await _api.SessionAsync("Billing");
        await billing.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = first, idempotencyKey = "c-1" });
        var both = await billing.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = second, idempotencyKey = "c-2" });
        Assert.Equal((HttpStatusCode.OK, 170m), (both.Status, Balance(both.Body)));
        return (billing, both.Body);
    }

    private static object Pay(JsonElement account, decimal amount, string key, object? allocations = null, Guid? paidBy = null, string method = "Cash") =>
        new { amount, method, note = (string?)null, rowVersion = Version(account), idempotencyKey = key, allocations, paidByPatientId = paidBy };

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_new_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, Url("/verification")), (HttpMethod.Get, Url("/responsibility")), (HttpMethod.Get, Url("/guarantor-summary")), (HttpMethod.Get, Methods),
            (HttpMethod.Post, Url("/payments/apply")), (HttpMethod.Post, Url("/reversals")), (HttpMethod.Post, Url("/responsibility")), (HttpMethod.Post, Methods), (HttpMethod.Post, $"{Methods}/{id}/active"),
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
    public async Task Whoever_can_see_billing_can_read_the_verification_responsibility_guarantor_summary_and_payment_methods(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Get, Url("/verification"))).Status, (await by.SendAsync(HttpMethod.Get, Url("/responsibility"))).Status,
            (await by.SendAsync(HttpMethod.Get, Url("/guarantor-summary"))).Status, (await by.SendAsync(HttpMethod.Get, Methods)).Status,
        };
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
    public async Task Only_billing_the_practice_manager_and_the_administrator_can_apply_reverse_record_responsibility_or_configure_methods(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 1, rowVersion = "AAAA", idempotencyKey = "a" })).Status,
            (await by.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 1, reason = "x", rowVersion = "AAAA", idempotencyKey = "r" })).Status,
            (await by.SendAsync(HttpMethod.Post, Url("/responsibility"), new { responsiblePatientId = Guid.NewGuid(), reason = "x" })).Status,
            (await by.SendAsync(HttpMethod.Post, Methods, new { name = "  ", kind = "Other" })).Status,
            (await by.SendAsync(HttpMethod.Post, $"{Methods}/{Guid.NewGuid()}/active", new { active = true, rowVersion = "AAAA" })).Status,
        };
        Assert.All(statuses, s => Assert.Equal(allowed, s != HttpStatusCode.Forbidden));
        if (allowed) Assert.Equal(new[] { HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.NotFound }, statuses);
    }

    [Fact]
    public async Task Every_new_change_needs_a_csrf_token()
    {
        var (billing, account) = await BilledAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 1, rowVersion = Version(account), idempotencyKey = "a" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 1, reason = "x", rowVersion = Version(account), idempotencyKey = "r" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/responsibility"), new { responsiblePatientId = _bo }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Methods, new { name = "Venmo", kind = "Other" }, csrf: false)).Status);
        Assert.Equal(4, (await billing.SendAsync(HttpMethod.Get, Methods)).Body.GetArrayLength());
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_payment_is_applied_to_the_charges_it_names_applied_later_reversed_and_the_history_always_verifies()
    {
        var (billing, account) = await BilledAsync();
        var paid = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(account, 100m, "p-1", new[] { new { chargeEntryNumber = 2, amount = 40m } }));
        Assert.Equal((HttpStatusCode.OK, 70m), (paid.Status, Balance(paid.Body)));
        var standing = paid.Body.GetProperty("standing");
        Assert.Equal((40m, 60m), (standing[2].GetProperty("appliedAmount").GetDecimal(), standing[2].GetProperty("unappliedAmount").GetDecimal()));
        Assert.Equal((0m, 40m), (standing[0].GetProperty("paidAmount").GetDecimal(), standing[1].GetProperty("paidAmount").GetDecimal()));
        Assert.Equal(60m, paid.Body.GetProperty("unappliedTotal").GetDecimal());

        var applied = await billing.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 3, rowVersion = Version(paid.Body), idempotencyKey = "a-1" });
        Assert.Equal(HttpStatusCode.OK, applied.Status);
        Assert.Equal((0m, 60m), (applied.Body.GetProperty("unappliedTotal").GetDecimal(), applied.Body.GetProperty("standing")[0].GetProperty("paidAmount").GetDecimal()));   // the rest went to the oldest charge
        Assert.Equal(2, applied.Body.GetProperty("allocations").GetArrayLength());

        var reversed = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 3, reason = "Paid on the wrong account", rowVersion = Version(applied.Body), idempotencyKey = "r-1" });
        Assert.Equal((HttpStatusCode.OK, 170m), (reversed.Status, Balance(reversed.Body)));
        var entries = reversed.Body.GetProperty("entries");
        Assert.Equal(("Reversal", 100m, 3), (entries[3].GetProperty("kind").GetString(), entries[3].GetProperty("amount").GetDecimal(), entries[3].GetProperty("reversesEntryNumber").GetInt32()));
        Assert.True(reversed.Body.GetProperty("standing")[2].GetProperty("isReversed").GetBoolean());
        Assert.All(reversed.Body.GetProperty("allocations").EnumerateArray(), a => Assert.True(a.GetProperty("released").GetBoolean()));      // released, still preserved

        var check = await billing.SendAsync(HttpMethod.Get, Url("/verification"));
        Assert.Equal((HttpStatusCode.OK, true, 170m, 4), (check.Status, check.Body.GetProperty("consistent").GetBoolean(), check.Body.GetProperty("balanceOwed").GetDecimal(), check.Body.GetProperty("entryCount").GetInt32()));
        Assert.Equal(0, check.Body.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public async Task Who_is_responsible_for_the_account_is_recorded_a_guarantor_sees_the_account_and_can_be_recorded_as_the_payer()
    {
        var (billing, account) = await BilledAsync();
        var stranger = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(account, 10m, "p-0", paidBy: _bo));
        Assert.Equal(HttpStatusCode.BadRequest, stranger.Status);
        Assert.Contains("paidBy", Fields(stranger.Body));

        var before = await billing.SendAsync(HttpMethod.Get, Url("/responsibility"));
        Assert.Equal(("Self", true), (before.Body.GetProperty("source").GetString(), before.Body.GetProperty("isSelf").GetBoolean()));
        var recorded = await billing.SendAsync(HttpMethod.Post, Url("/responsibility"), new { responsiblePatientId = _bo, reason = "Bo pays for Ann", rowVersion = Version(account) });
        Assert.Equal((HttpStatusCode.OK, "Recorded", _bo.ToString()), (recorded.Status, recorded.Body.GetProperty("source").GetString(), recorded.Body.GetProperty("responsiblePatientId").GetString()));
        Assert.Equal(1, recorded.Body.GetProperty("history").GetArrayLength());

        var summary = await billing.SendAsync(HttpMethod.Get, Url("/guarantor-summary", _bo));
        Assert.Equal(170m, summary.Body.GetProperty("totalOwed").GetDecimal());
        Assert.Equal(_ann.ToString(), summary.Body.GetProperty("accounts")[0].GetProperty("patientId").GetString());

        var current = (await billing.SendAsync(HttpMethod.Get, Url())).Body;
        var paid = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(current, 10m, "p-1", paidBy: _bo));
        Assert.Equal((HttpStatusCode.OK, false), (paid.Status, string.IsNullOrWhiteSpace(paid.Body.GetProperty("entries")[2].GetProperty("paidByName").GetString())));
    }

    [Fact]
    public async Task Payment_methods_are_configured_by_billing_and_a_payment_can_use_a_configured_one_by_name()
    {
        var (billing, account) = await BilledAsync();
        var list = await billing.SendAsync(HttpMethod.Get, Methods);
        Assert.Equal(new[] { "Card", "Cash", "Check", "Other" }, list.Body.EnumerateArray().Select(m => m.GetProperty("name").GetString()!).ToArray());

        var venmo = await billing.SendAsync(HttpMethod.Post, Methods, new { name = "Venmo", kind = "Other" });
        Assert.Equal((HttpStatusCode.OK, "Other", false), (venmo.Status, venmo.Body.GetProperty("kind").GetString(), venmo.Body.GetProperty("isBuiltIn").GetBoolean()));
        var again = await billing.SendAsync(HttpMethod.Post, Methods, new { name = "venmo", kind = "Other" });
        Assert.Equal(venmo.Body.GetProperty("id").GetString(), again.Body.GetProperty("id").GetString());
        var clash = await billing.SendAsync(HttpMethod.Post, Methods, new { name = "Venmo", kind = "Card" });
        Assert.Equal((HttpStatusCode.Conflict, "method_exists"), (clash.Status, Error(clash.Body)));
        var bad = await billing.SendAsync(HttpMethod.Post, Methods, new { name = "", kind = "Barter" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(new[] { "kind", "name" }, Fields(bad.Body));

        var paid = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(account, 20m, "p-1", method: "Venmo"));
        Assert.Equal(("Other", "Venmo"), (paid.Body.GetProperty("entries")[2].GetProperty("paymentMethod").GetString(), paid.Body.GetProperty("entries")[2].GetProperty("paymentMethodName").GetString()));

        var id = venmo.Body.GetProperty("id").GetString();
        var off = await billing.SendAsync(HttpMethod.Post, $"{Methods}/{id}/active", new { active = false, rowVersion = venmo.Body.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.OK, false), (off.Status, off.Body.GetProperty("isActive").GetBoolean()));
        Assert.Equal(4, (await billing.SendAsync(HttpMethod.Get, Methods)).Body.GetArrayLength());
        Assert.Equal(5, (await billing.SendAsync(HttpMethod.Get, $"{Methods}?includeInactive=true")).Body.GetArrayLength());
        var refused = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(paid.Body, 10m, "p-2", method: "Venmo"));
        Assert.Equal((HttpStatusCode.BadRequest, "method"), (refused.Status, Fields(refused.Body).Single()));

        var cash = list.Body.EnumerateArray().First(m => m.GetProperty("name").GetString() == "Cash");
        var builtIn = await billing.SendAsync(HttpMethod.Post, $"{Methods}/{cash.GetProperty("id").GetString()}/active", new { active = false, rowVersion = cash.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.Conflict, "built_in_method"), (builtIn.Status, Error(builtIn.Body)));
        var missing = await billing.SendAsync(HttpMethod.Post, $"{Methods}/{id}/active", new { rowVersion = "AAAA" });
        Assert.Equal((HttpStatusCode.BadRequest, "active"), (missing.Status, Fields(missing.Body).Single()));
    }

    // ---------- refusals ----------

    [Fact]
    public async Task A_wrong_allocation_or_reversal_is_refused_with_a_message_per_field_and_a_stable_code_and_changes_nothing()
    {
        var (billing, account) = await BilledAsync();
        var wrong = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(account, 100m, "p-1", new object[] { new { chargeEntryNumber = 9, amount = 10m }, new { chargeEntryNumber = 1, amount = 100.01m } }));
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (wrong.Status, Error(wrong.Body)));
        Assert.Equal(new[] { "allocations[0]", "allocations[1]" }, Fields(wrong.Body));
        Assert.Equal(2, (await billing.SendAsync(HttpMethod.Get, Url())).Body.GetProperty("entries").GetArrayLength());

        var paid = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(account, 100m, "p-2", new[] { new { chargeEntryNumber = 1, amount = 100m } }));
        var chargePaid = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 1, reason = "Billed in error", rowVersion = Version(paid.Body), idempotencyKey = "r-1" });
        Assert.Equal((HttpStatusCode.Conflict, "charge_has_payments"), (chargePaid.Status, Error(chargePaid.Body)));
        var undone = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 3, reason = "Wrong account", rowVersion = Version(paid.Body), idempotencyKey = "r-2" });
        Assert.Equal(HttpStatusCode.OK, undone.Status);
        var twice = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 3, reason = "Again", rowVersion = Version(undone.Body), idempotencyKey = "r-3" });
        var ofReversal = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 4, reason = "Again", rowVersion = Version(undone.Body), idempotencyKey = "r-4" });
        var missing = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 99, reason = "Again", rowVersion = Version(undone.Body), idempotencyKey = "r-5" });
        var noReason = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 1, reason = "  ", rowVersion = Version(undone.Body), idempotencyKey = "r-6" });
        Assert.Equal(new[] { "already_reversed", "cannot_reverse_a_reversal", "entry_not_found" }, new[] { Error(twice.Body), Error(ofReversal.Body), Error(missing.Body) });
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.BadRequest, "reason"), (missing.Status, noReason.Status, Fields(noReason.Body).Single()));

        var nothing = await billing.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 3, rowVersion = Version(undone.Body), idempotencyKey = "a-1" });
        var notAPayment = await billing.SendAsync(HttpMethod.Post, Url("/payments/apply"), new { paymentEntryNumber = 1, rowVersion = Version(undone.Body), idempotencyKey = "a-2" });
        Assert.Equal(("payment_reversed", "payment_not_found"), (Error(nothing.Body), Error(notAPayment.Body)));
        var stale = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 2, reason = "Late", rowVersion = Version(account), idempotencyKey = "r-7" });
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);                                                       // an out-of-date copy of the account
        Assert.True((await billing.SendAsync(HttpMethod.Get, Url("/verification"))).Body.GetProperty("consistent").GetBoolean());
    }

    [Fact]
    public async Task When_the_log_cannot_be_written_a_reversal_and_a_responsibility_answer_503_save_failed_without_internals_and_store_nothing()
    {
        var (billing, account) = await BilledAsync();
        var paid = await billing.SendAsync(HttpMethod.Post, Url("/payments"), Pay(account, 50m, "p-1"));
        async Task Sql(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }
        await Sql(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType IN ('BillingAccount', 'BillingPaymentMethod')) THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var reverse = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 3, reason = "Wrong account", rowVersion = Version(paid.Body), idempotencyKey = "r-1" });
        var responsibility = await billing.SendAsync(HttpMethod.Post, Url("/responsibility"), new { responsiblePatientId = _bo, rowVersion = Version(paid.Body) });
        var method = await billing.SendAsync(HttpMethod.Post, Methods, new { name = "Venmo", kind = "Other" });
        foreach (var r in new[] { reverse, responsibility, method })
        {
            Assert.Equal((HttpStatusCode)503, r.Status);
            Assert.Equal("save_failed", Error(r.Body));
            Assert.DoesNotContain("audit", r.Body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SqlException", r.Body.GetRawText());
        }
        Assert.Equal(3, (await billing.SendAsync(HttpMethod.Get, Url())).Body.GetProperty("entries").GetArrayLength());
        Assert.Equal(4, (await billing.SendAsync(HttpMethod.Get, $"{Methods}?includeInactive=true")).Body.GetArrayLength());

        await Sql("DROP TRIGGER [TR_Test_RefuseBillingAudit]");
        Assert.Equal(HttpStatusCode.OK, (await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 3, reason = "Wrong account", rowVersion = Version(paid.Body), idempotencyKey = "r-1" })).Status);   // the same request now works
    }

    // ---------- the contract for ALV-008-C01 ----------

    [Fact]
    public async Task The_contract_for_atomic_completion_is_registered_and_is_the_billing_service()
    {
        using var scope = _api.Factory.Services.CreateScope();
        Assert.IsType<BillingService>(scope.ServiceProvider.GetRequiredService<IProcedureCharging>());
    }
}
