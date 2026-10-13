using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 at the HTTP boundary: who may read the timeline, aging, previews and issued documents (everyone who can see billing) and who may refund, move, correct or issue (billing, the practice manager, the
/// administrator), CSRF on every change, the whole refund / move / correct / statement / receipt workflow through the real routes, the stable codes the screen relies on (including the stale preview and the
/// preview that cannot be read), the CSV, and the 503 that stores nothing when the log cannot be written (for a move that includes the releases saved before the failure). A real API, real SQL Server and a
/// real signed-in session per role.
/// </summary>
public class BillingStatementsApiTests : IAsyncLifetime
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
    private static string Version(JsonElement account) => account.GetProperty("rowVersion").GetString()!;
    private static decimal Balance(JsonElement account) => account.GetProperty("balanceOwed").GetDecimal();
    private string Url(string tail = "") => $"/api/patients/{_ann}/billing{tail}";

    /// <summary>Two completed procedures for Ann (fee 100 each), billed by the billing person: entries 1 and 2, balance 200.</summary>
    private async Task<(Session Billing, JsonElement Account)> BilledAsync()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var admin = await _api.SessionAsync("Admin");
        var diagnosis = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, diagnosis.Status);
        var items = new List<object>();
        for (var i = 0; i < 2; i++)
        {
            var procedure = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description = $"Procedure {_codes}", category = "Periodontic", scope = "WholeMouth", fee = 100m });
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
        var billing = await _api.SessionAsync("Billing");
        await billing.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = ids[0], idempotencyKey = "c-1" });
        var both = await billing.SendAsync(HttpMethod.Post, Url("/charges"), new { planItemId = ids[1], idempotencyKey = "c-2" });
        Assert.Equal((HttpStatusCode.OK, 200m), (both.Status, Balance(both.Body)));
        return (billing, both.Body);
    }

    /// <summary>Payment 3 of 150: 60 on charge 1, 40 on charge 2, 50 unapplied; 50 is owed.</summary>
    private async Task<(Session Billing, JsonElement Account)> PaidAsync()
    {
        var (billing, account) = await BilledAsync();
        var paid = await billing.SendAsync(HttpMethod.Post, Url("/payments"), new { amount = 150m, method = "Cash", note = (string?)null, rowVersion = Version(account), idempotencyKey = "p-1",
            allocations = new[] { new { chargeEntryNumber = 1, amount = 60m }, new { chargeEntryNumber = 2, amount = 40m } } });
        Assert.Equal((HttpStatusCode.OK, 50m), (paid.Status, Balance(paid.Body)));
        return (billing, paid.Body);
    }

    // ---------- authentication, roles and CSRF ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_new_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, Url("/timeline")), (HttpMethod.Get, Url("/aging")), (HttpMethod.Get, Url("/documents")), (HttpMethod.Get, Url("/documents/preview?type=Statement")),
            (HttpMethod.Get, Url($"/documents/{id}")), (HttpMethod.Get, Url($"/documents/{id}/csv")),
            (HttpMethod.Post, Url("/refunds")), (HttpMethod.Post, Url("/allocations/move")), (HttpMethod.Post, Url("/corrections")), (HttpMethod.Post, Url("/documents")),
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
    public async Task Whoever_can_see_billing_can_read_the_timeline_aging_documents_and_previews(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Get, Url("/timeline"))).Status, (await by.SendAsync(HttpMethod.Get, Url("/aging"))).Status, (await by.SendAsync(HttpMethod.Get, Url("/documents"))).Status,
        };
        Assert.All(statuses, s => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, s));
        var preview = (await by.SendAsync(HttpMethod.Get, Url("/documents/preview?type=Statement"))).Status;
        Assert.Equal(allowed ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden, preview);            // nothing is billed yet: allowed roles are told so (409 nothing_owed), the others are refused
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Billing", true)]
    [InlineData("Dentist", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    public async Task Only_billing_the_practice_manager_and_the_administrator_can_refund_move_correct_or_issue(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 1, amount = 1m, method = "Cash", reason = "x", rowVersion = "AAAA", idempotencyKey = "a" })).Status,
            (await by.SendAsync(HttpMethod.Post, Url("/allocations/move"), new { paymentEntryNumber = 1, reason = "x", rowVersion = "AAAA", idempotencyKey = "b" })).Status,
            (await by.SendAsync(HttpMethod.Post, Url("/corrections"), new { entryNumber = 1, reason = "x", amount = 1m, rowVersion = "AAAA", idempotencyKey = "c" })).Status,
            (await by.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = "x", idempotencyKey = "d" })).Status,
        };
        Assert.All(statuses, s => Assert.Equal(allowed, s != HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Every_change_needs_a_csrf_token()
    {
        var (billing, account) = await PaidAsync();
        var v = Version(account);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 1m, method = "Cash", reason = "x", rowVersion = v, idempotencyKey = "a" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/allocations/move"), new { paymentEntryNumber = 3, rowVersion = v, idempotencyKey = "b" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/corrections"), new { entryNumber = 3, reason = "x", amount = 1m, rowVersion = v, idempotencyKey = "c" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await billing.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = "x", idempotencyKey = "d" }, csrf: false)).Status);
        Assert.Equal(3, (await billing.SendAsync(HttpMethod.Get, Url())).Body.GetProperty("entries").GetArrayLength());
    }

    // ---------- the workflow ----------

    [Fact]
    public async Task A_refund_a_move_and_a_correction_through_the_routes_leave_a_history_that_always_verifies()
    {
        var (billing, account) = await PaidAsync();
        var refunded = await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 80m, method = "Cash", reason = "Patient request", rowVersion = Version(account), idempotencyKey = "rf-1" });
        Assert.Equal((HttpStatusCode.OK, 130m), (refunded.Status, Balance(refunded.Body)));
        var refund = refunded.Body.GetProperty("entries")[3];
        Assert.Equal(("Refund", 80m, 3), (refund.GetProperty("kind").GetString(), refund.GetProperty("amount").GetDecimal(), refund.GetProperty("refundsEntryNumber").GetInt32()));
        Assert.Equal(30m, refunded.Body.GetProperty("allocations")[1].GetProperty("releasedAmount").GetDecimal());
        Assert.Equal((70m, 0m), (refunded.Body.GetProperty("standing")[2].GetProperty("refundableAmount").GetDecimal(), refunded.Body.GetProperty("standing")[2].GetProperty("unappliedAmount").GetDecimal()));

        var moved = await billing.SendAsync(HttpMethod.Post, Url("/allocations/move"), new { paymentEntryNumber = 3, releases = new[] { new { chargeEntryNumber = 1, amount = 20m } },
            allocations = new[] { new { chargeEntryNumber = 2, amount = 20m } }, reason = "Wrong procedure", rowVersion = Version(refunded.Body), idempotencyKey = "mv-1" });
        Assert.Equal(HttpStatusCode.OK, moved.Status);
        Assert.Equal((40m, 30m), (moved.Body.GetProperty("standing")[0].GetProperty("paidAmount").GetDecimal(), moved.Body.GetProperty("standing")[1].GetProperty("paidAmount").GetDecimal()));

        var corrected = await billing.SendAsync(HttpMethod.Post, Url("/corrections"), new { entryNumber = 3, reason = "x", amount = 50m, method = "Cash", rowVersion = Version(moved.Body), idempotencyKey = "cr-0" });
        Assert.Equal("payment_refunded", Error(corrected.Body));                                          // a refunded payment is not corrected

        var check = await billing.SendAsync(HttpMethod.Get, Url("/verification"));
        Assert.True(check.Body.GetProperty("consistent").GetBoolean());
        Assert.Equal(0, check.Body.GetProperty("findings").GetArrayLength());

        var timeline = await billing.SendAsync(HttpMethod.Get, Url("/timeline"));
        Assert.Contains("Refund", timeline.Body.EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        Assert.Contains("Release", timeline.Body.EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        var aging = await billing.SendAsync(HttpMethod.Get, Url("/aging"));
        Assert.Equal(2, aging.Body.GetProperty("openCharges").GetArrayLength());
    }

    [Fact]
    public async Task A_payment_is_corrected_through_the_route_with_an_exact_reversal_and_a_linked_unapplied_replacement()
    {
        var (billing, account) = await PaidAsync();
        var corrected = await billing.SendAsync(HttpMethod.Post, Url("/corrections"), new { entryNumber = 3, reason = "Typed wrong amount", amount = 100m, method = "Cash", rowVersion = Version(account), idempotencyKey = "cr-1" });
        Assert.Equal((HttpStatusCode.OK, 100m), (corrected.Status, Balance(corrected.Body)));
        var entries = corrected.Body.GetProperty("entries");
        Assert.Equal(("Reversal", 3), (entries[3].GetProperty("kind").GetString(), entries[3].GetProperty("reversesEntryNumber").GetInt32()));
        Assert.Equal(("Payment", -100m, 3), (entries[4].GetProperty("kind").GetString(), entries[4].GetProperty("amount").GetDecimal(), entries[4].GetProperty("replacesEntryNumber").GetInt32()));
        Assert.Equal(100m, corrected.Body.GetProperty("standing")[4].GetProperty("unappliedAmount").GetDecimal());
        Assert.True((await billing.SendAsync(HttpMethod.Get, Url("/verification"))).Body.GetProperty("consistent").GetBoolean());
    }

    [Fact]
    public async Task A_statement_and_a_receipt_are_previewed_issued_listed_reprinted_and_the_statement_exported()
    {
        var (billing, _) = await PaidAsync();
        var preview = await billing.SendAsync(HttpMethod.Get, Url("/documents/preview?type=Statement"));
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal("50.00", preview.Body.GetProperty("statement").GetProperty("balance").GetString());
        Assert.Equal(JsonValueKind.Null, preview.Body.GetProperty("receipt").ValueKind);
        var issued = await billing.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = preview.Body.GetProperty("previewToken").GetString(), idempotencyKey = "doc-1" });
        Assert.Equal((HttpStatusCode.OK, "Statement", 1), (issued.Status, issued.Body.GetProperty("documentType").GetString(), issued.Body.GetProperty("documentNumber").GetInt32()));
        var id = issued.Body.GetProperty("id").GetString();

        var receipt = await billing.SendAsync(HttpMethod.Get, Url("/documents/preview?type=Receipt&paymentEntryNumber=3"));
        Assert.Equal("150.00", receipt.Body.GetProperty("receipt").GetProperty("amount").GetString());
        var receiptIssued = await billing.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = receipt.Body.GetProperty("previewToken").GetString(), idempotencyKey = "doc-2" });
        Assert.Equal(("Receipt", 1, 3), (receiptIssued.Body.GetProperty("documentType").GetString(), receiptIssued.Body.GetProperty("documentNumber").GetInt32(), receiptIssued.Body.GetProperty("paymentEntryNumber").GetInt32()));

        Assert.Equal(2, (await billing.SendAsync(HttpMethod.Get, Url("/documents"))).Body.GetArrayLength());
        var detail = await billing.SendAsync(HttpMethod.Get, Url($"/documents/{id}"));
        Assert.Equal((HttpStatusCode.OK, issued.Body.GetProperty("contentHash").GetString()), (detail.Status, detail.Body.GetProperty("document").GetProperty("contentHash").GetString()));
        Assert.Equal(0, detail.Body.GetProperty("currentStatus").GetArrayLength());

        var csv = await billing.Client.GetAsync(Url($"/documents/{id}/csv"));
        Assert.Equal((HttpStatusCode.OK, "text/csv"), (csv.StatusCode, csv.Content.Headers.ContentType!.MediaType));
        Assert.StartsWith("Number,Kind,Amount", await csv.Content.ReadAsStringAsync());
        var receiptCsv = await billing.SendAsync(HttpMethod.Get, Url($"/documents/{receiptIssued.Body.GetProperty("id").GetString()}/csv"));
        Assert.Equal((HttpStatusCode.Conflict, "csv_statement_only"), (receiptCsv.Status, Error(receiptCsv.Body)));
        Assert.Equal((HttpStatusCode.NotFound, "document_not_found"), ((await billing.SendAsync(HttpMethod.Get, Url($"/documents/{Guid.NewGuid()}"))).Status, Error((await billing.SendAsync(HttpMethod.Get, Url($"/documents/{Guid.NewGuid()}"))).Body)));
    }

    [Fact]
    public async Task A_stale_preview_and_a_preview_that_cannot_be_read_are_refused_with_their_codes_and_nothing_is_issued()
    {
        var (billing, _) = await PaidAsync();
        var preview = await billing.SendAsync(HttpMethod.Get, Url("/documents/preview?type=Statement"));
        await using (var db = _fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("UPDATE Patients SET LastName = 'Renamed' WHERE Id = {0}", _ann);
        var stale = await billing.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = preview.Body.GetProperty("previewToken").GetString(), idempotencyKey = "doc-1" });
        Assert.Equal((HttpStatusCode.Conflict, "document_preview_stale"), (stale.Status, Error(stale.Body)));
        var garbage = await billing.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = "garbage", idempotencyKey = "doc-2" });
        Assert.Equal((HttpStatusCode.BadRequest, "document_preview_invalid"), (garbage.Status, Error(garbage.Body)));
        var missing = await billing.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = (string?)null, idempotencyKey = "doc-3" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (missing.Status, Error(missing.Body)));
        Assert.Equal(0, (await billing.SendAsync(HttpMethod.Get, Url("/documents"))).Body.GetArrayLength());
    }

    [Fact]
    public async Task The_refusal_codes_the_screen_relies_on_come_back_from_the_routes()
    {
        var (billing, account) = await PaidAsync();
        var over = await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 150.01m, method = "Cash", reason = "x", rowVersion = Version(account), idempotencyKey = "rf-1" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed", "amount"), (over.Status, Error(over.Body), over.Body.GetProperty("fieldErrors").EnumerateObject().Single().Name));
        var notAPayment = await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 1, amount = 1m, method = "Cash", reason = "x", rowVersion = Version(account), idempotencyKey = "rf-2" });
        Assert.Equal((HttpStatusCode.NotFound, "payment_not_found"), (notAPayment.Status, Error(notAPayment.Body)));
        var noVersion = await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 1m, method = "Cash", reason = "x", idempotencyKey = "rf-3" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));

        var one = await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 10m, method = "Cash", reason = "x", rowVersion = Version(account), idempotencyKey = "rf-4" });
        Assert.Equal(HttpStatusCode.OK, one.Status);
        var stale = await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 10m, method = "Cash", reason = "x", rowVersion = Version(account), idempotencyKey = "rf-5" });
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);                                              // an out-of-date copy of the account
        var reverse = await billing.SendAsync(HttpMethod.Post, Url("/reversals"), new { entryNumber = 3, reason = "x", rowVersion = Version(one.Body), idempotencyKey = "r-1" });
        Assert.Equal((HttpStatusCode.Conflict, "payment_refunded"), (reverse.Status, Error(reverse.Body)));
        var unequal = await billing.SendAsync(HttpMethod.Post, Url("/allocations/move"), new { paymentEntryNumber = 3, releases = new[] { new { chargeEntryNumber = 1, amount = 20m } },
            allocations = new[] { new { chargeEntryNumber = 2, amount = 19m } }, reason = "x", rowVersion = Version(one.Body), idempotencyKey = "mv-1" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (unequal.Status, Error(unequal.Body)));
        Assert.True((await billing.SendAsync(HttpMethod.Get, Url("/verification"))).Body.GetProperty("consistent").GetBoolean());
    }

    [Fact]
    public async Task When_the_log_cannot_be_written_a_refund_a_move_a_correction_and_an_issue_answer_503_and_store_nothing_not_even_the_releases_a_move_saved_first()
    {
        var (billing, account) = await PaidAsync();
        var preview = await billing.SendAsync(HttpMethod.Get, Url("/documents/preview?type=Statement"));
        async Task Sql(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }
        async Task<(int Entries, int Allocations, int Releases, int Documents)> Counts()
        {
            await using var db = _fixture.CreateContext();
            return (await db.BillingEntries.CountAsync(), await db.BillingAllocations.CountAsync(), await db.BillingAllocationReleases.CountAsync(), await db.FinancialDocuments.CountAsync());
        }
        var before = await Counts();
        await Sql(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType IN ('BillingAccount')) THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var v = Version(account);
        var results = new[]
        {
            await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 80m, method = "Cash", reason = "x", rowVersion = v, idempotencyKey = "rf-1" }),
            await billing.SendAsync(HttpMethod.Post, Url("/allocations/move"), new { paymentEntryNumber = 3, releases = new[] { new { chargeEntryNumber = 1, amount = 20m } },
                allocations = new[] { new { chargeEntryNumber = 2, amount = 20m } }, reason = "x", rowVersion = v, idempotencyKey = "mv-1" }),
            await billing.SendAsync(HttpMethod.Post, Url("/corrections"), new { entryNumber = 3, reason = "x", amount = 100m, method = "Cash", rowVersion = v, idempotencyKey = "cr-1" }),
            await billing.SendAsync(HttpMethod.Post, Url("/documents"), new { previewToken = preview.Body.GetProperty("previewToken").GetString(), idempotencyKey = "doc-1" }),
        };
        foreach (var r in results)
        {
            Assert.Equal((HttpStatusCode)503, r.Status);
            Assert.Equal("save_failed", Error(r.Body));
            Assert.DoesNotContain("SqlException", r.Body.GetRawText());
        }
        Assert.Equal(before, await Counts());                                                              // nothing at all was stored

        await Sql("DROP TRIGGER [TR_Test_RefuseBillingAudit]");
        Assert.Equal(HttpStatusCode.OK, (await billing.SendAsync(HttpMethod.Post, Url("/refunds"), new { paymentEntryNumber = 3, amount = 80m, method = "Cash", reason = "x", rowVersion = v, idempotencyKey = "rf-1" })).Status);   // the same request now succeeds
    }
}
