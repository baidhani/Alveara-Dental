using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Procedures;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-007 against real SQL Server: a completed procedure is billed at the fee it was proposed at and no other amount; a payment lowers the balance and the history keeps every entry; an adjustment
/// corrects with a reason; a wrong amount, a misapplied payment, a bad adjustment, a repeat and two people at once are all refused or quiet exactly as documented; and every entry is audited in the same
/// save, so a failed audit write stores nothing.
/// </summary>
public class BillingServiceTests : BillingTestBase
{
    private async Task<BillingAccountView> ChargedAsync(BillablePlan plan, Guid? patient = null) => await Bill(s => s.RecordChargeAsync(patient ?? Ann, ChargeOf(plan.Completed), S.Actor, default));
    private Task<BillingAccountView> AccountAsync(Guid? patient = null) => Bill(s => s.GetAccountAsync(patient ?? Ann, default));

    // ---------- the charge ----------

    [Fact]
    public async Task A_completed_procedure_is_billed_at_the_fee_it_was_proposed_at_with_who_and_when()
    {
        var plan = await BillablePlanAsync();
        var before = await AccountAsync();
        Assert.Equal((false, 0m, null), (before.HasAccount, before.BalanceOwed, before.RowVersion));

        var account = await ChargedAsync(plan);
        var entry = Assert.Single(account.Entries);
        Assert.Equal((1, "Charge", 100m, 100m, "LOCAL-100", plan.Completed), (entry.EntryNumber, entry.Kind, entry.Amount, entry.BalanceAfter, entry.ProcedureCode, entry.PlanItemId));
        Assert.Equal("Periodontal maintenance", entry.ProcedureDescription);
        Assert.False(string.IsNullOrWhiteSpace(entry.RecordedByName));
        Assert.True(account.HasAccount && account.RowVersion is not null);
        Assert.Equal((100m, "USD"), (account.BalanceOwed, account.Currency));
        Assert.Equal((1, 1, 1), await BillingCountsAsync());
    }

    [Fact]
    public async Task The_charge_is_the_plans_fee_even_after_the_catalog_fee_changes()
    {
        var plan = await BillablePlanAsync();
        await Catalog(s => s.ReviseAsync(plan.FirstProcedure.Summary.Id, new ProcedureInput(ProcedureCodeSystems.Local, "LOCAL-100", "Periodontal maintenance", "Periodontic", ProcedureScopes.WholeMouth, null, 250m, null, null, null, null), "Annual fee review", plan.FirstProcedure.Summary.RowVersion, S.Actor, default));
        var account = await ChargedAsync(plan);
        Assert.Equal(100m, account.Entries.Single().Amount);
    }

    [Fact]
    public async Task Billing_sees_only_the_completed_unbilled_procedures_and_nothing_clinical()
    {
        var plan = await BillablePlanAsync();
        var list = await Bill(s => s.ListBillableAsync(Ann, default));
        var only = Assert.Single(list);                                    // the accepted one is not completed and the withdrawn one is not billable
        Assert.Equal((plan.Completed, "LOCAL-100", "Periodontal maintenance", 100m, "USD"), (only.PlanItemId, only.ProcedureCode, only.Description, only.Fee, only.Currency));

        await ChargedAsync(plan);
        Assert.Empty(await Bill(s => s.ListBillableAsync(Ann, default)));  // once billed it is no longer offered
        Assert.Empty(await Bill(s => s.ListBillableAsync(Bo, default)));
    }

    [Fact]
    public async Task A_procedure_that_cannot_be_billed_is_refused_with_the_reason_and_nothing_is_stored()
    {
        var plan = await BillablePlanAsync();
        var bo = await BillablePlanAsync(Bo);

        var notCompleted = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, ChargeOf(plan.Accepted), S.Actor, default)));
        var withdrawn = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, ChargeOf(plan.Withdrawn), S.Actor, default)));
        var anothers = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, ChargeOf(bo.Completed), S.Actor, default)));
        var unknown = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, ChargeOf(Guid.NewGuid()), S.Actor, default)));
        Assert.Equal(new[] { ("not_completed", 409), ("item_withdrawn", 409), ("plan_item_not_found", 404), ("plan_item_not_found", 404) },
            new[] { notCompleted, withdrawn, anothers, unknown }.Select(e => (e.Code, e.StatusCode)).ToArray());
        Assert.Equal(unknown.Message, anothers.Message);                   // another patient's procedure looks exactly like an unknown one
        Assert.Equal((0, 0, 0), await BillingCountsAsync());
    }

    [Fact]
    public async Task A_missing_procedure_or_key_is_named_in_one_answer_and_nothing_is_stored()
    {
        var e = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(null, null), S.Actor, default)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(new[] { "idempotencyKey", "planItemId" }, e.FieldErrors.Keys.Order().ToArray());
        Assert.Equal("patient_not_found", (await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Guid.NewGuid(), ChargeOf(Guid.NewGuid()), S.Actor, default)))).Code);
        Assert.Equal((0, 0, 0), await BillingCountsAsync());
    }

    [Fact]
    public async Task Billing_the_same_procedure_twice_never_double_charges()
    {
        var plan = await BillablePlanAsync();
        await ChargedAsync(plan);
        var again = await ChargedAsync(plan);                              // the same key: a retry or a double click returns what the first save made
        Assert.Equal((1, 100m), (again.Entries.Count, again.BalanceOwed));
        Assert.Equal((1, 1, 1), await BillingCountsAsync());

        var other = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "another-key"), S.Actor, default)));
        Assert.Equal(("already_billed", 409), (other.Code, other.StatusCode));
        var reused = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Accepted, "charge-1"), S.Actor, default)));
        Assert.Equal(("idempotency_key_reused", 409), (reused.Code, reused.StatusCode));
        Assert.Equal((1, 1, 1), await BillingCountsAsync());
    }

    // ---------- the payment ----------

    [Fact]
    public async Task A_payment_lowers_the_balance_and_the_history_keeps_every_entry_as_it_was()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var paid = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(60m, charged.RowVersion!, note: "Visa ending 4242 not stored"), S.Actor, default));

        Assert.Equal(40m, paid.BalanceOwed);
        Assert.Equal(new[] { "Charge", "Payment" }, paid.Entries.Select(e => e.Kind).ToArray());
        Assert.Equal(new[] { 100m, -60m }, paid.Entries.Select(e => e.Amount).ToArray());
        Assert.Equal(new[] { 100m, 40m }, paid.Entries.Select(e => e.BalanceAfter).ToArray());
        Assert.Equal(charged.Entries[0], paid.Entries[0] with { });        // the earlier entry is exactly what it was
        Assert.Equal(("Cash", "Visa ending 4242 not stored"), (paid.Entries[1].PaymentMethod, paid.Entries[1].Reason));
        Assert.NotEqual(charged.RowVersion, paid.RowVersion);              // the account moved on, so anyone holding the old copy is told
        Assert.Equal((1, 2, 2), await BillingCountsAsync());

        var rest = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(40m, paid.RowVersion!, key: "pay-2", method: "check"), S.Actor, default));
        Assert.Equal((0m, "Check"), (rest.BalanceOwed, rest.Entries[2].PaymentMethod));
    }

    [Theory]
    [InlineData("zero", "amount")]
    [InlineData("negative", "amount")]
    [InlineData("threeDecimals", "amount")]
    [InlineData("tooBig", "amount")]
    [InlineData("moreThanOwed", "amount")]
    [InlineData("noMethod", "method")]
    [InlineData("badMethod", "method")]
    [InlineData("longNote", "note")]
    public async Task A_wrong_payment_is_refused_beside_its_field_and_nothing_is_stored(string which, string field)
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var input = which switch
        {
            "zero" => PaymentOf(0m, charged.RowVersion!),
            "negative" => PaymentOf(-5m, charged.RowVersion!),
            "threeDecimals" => PaymentOf(10.005m, charged.RowVersion!),
            "tooBig" => PaymentOf(1_000_000.01m, charged.RowVersion!),
            "moreThanOwed" => PaymentOf(100.01m, charged.RowVersion!),
            "noMethod" => PaymentOf(10m, charged.RowVersion!) with { Method = null },
            "badMethod" => PaymentOf(10m, charged.RowVersion!, method: "Barter"),
            _ => PaymentOf(10m, charged.RowVersion!, note: new string('x', 501)),
        };
        var e = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Ann, input, S.Actor, default)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Contains(field, e.FieldErrors.Keys);
        if (which == "moreThanOwed") Assert.Contains("100.00", e.FieldErrors["amount"]);   // it says what is owed
        Assert.Equal((1, 1, 1), await BillingCountsAsync());
        Assert.Equal(100m, (await AccountAsync()).BalanceOwed);
    }

    [Fact]
    public async Task A_payment_cannot_reach_another_patients_account_or_an_account_with_nothing_on_it()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var nothing = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Bo, PaymentOf(10m, charged.RowVersion!), S.Actor, default)));   // Bo owes nothing; Ann's version is no use
        Assert.Equal(("nothing_owed", 409), (nothing.Code, nothing.StatusCode));
        Assert.Equal(100m, (await AccountAsync()).BalanceOwed);

        var bo = await BillablePlanAsync(Bo);
        var boCharged = await Bill(s => s.RecordChargeAsync(Bo, ChargeOf(bo.Completed, "bo-charge"), S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Bill(s => s.RecordPaymentAsync(Bo, PaymentOf(10m, charged.RowVersion!), S.Actor, default)));   // Ann's copy does not fit Bo's account
        Assert.Equal((100m, 100m), ((await AccountAsync()).BalanceOwed, boCharged.BalanceOwed));
    }

    [Fact]
    public async Task Repeating_a_payment_is_quiet_and_a_different_one_under_the_same_key_is_refused()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var paid = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(30m, charged.RowVersion!), S.Actor, default));
        var again = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(30m, charged.RowVersion!), S.Actor, default));   // a double click: the same key, even with the older version
        Assert.Equal((70m, paid.RowVersion), (again.BalanceOwed, again.RowVersion));
        Assert.Equal((1, 2, 2), await BillingCountsAsync());

        var reused = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(31m, paid.RowVersion!), S.Actor, default)));
        Assert.Equal("idempotency_key_reused", reused.Code);
        Assert.Equal((1, 2, 2), await BillingCountsAsync());
    }

    [Fact]
    public async Task A_payment_made_from_an_out_of_date_copy_of_the_account_is_a_conflict_that_changes_nothing()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(30m, charged.RowVersion!), S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(30m, charged.RowVersion!, key: "pay-2"), S.Actor, default)));
        Assert.Equal((70m, 2), ((await AccountAsync()).BalanceOwed, (await BillingCountsAsync()).Entries));

        var missing = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(10m, charged.RowVersion!) with { RowVersion = null }, S.Actor, default)));
        var garbled = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(10m, "not-base64!"), S.Actor, default)));
        Assert.Equal(("row_version_required", "row_version_invalid"), (missing.Code, garbled.Code));
    }

    [Fact]
    public async Task Two_people_paying_the_same_balance_at_once_end_with_one_payment_and_one_conflict()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var results = await Task.WhenAll(
            Attempt(PaymentOf(60m, charged.RowVersion!, key: "desk-1")),
            Attempt(PaymentOf(60m, charged.RowVersion!, key: "desk-2")));
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal(1, results.Count(r => r is ConcurrencyConflictException));
        Assert.Equal((40m, 2), ((await AccountAsync()).BalanceOwed, (await BillingCountsAsync()).Entries));   // never 100 - 120

        async Task<Exception?> Attempt(PaymentInput input)
        {
            try { await Bill(s => s.RecordPaymentAsync(Ann, input, S.Actor, default)); return null; }
            catch (Exception e) { return e; }
        }
    }

    // ---------- the adjustment ----------

    [Fact]
    public async Task An_adjustment_corrects_the_balance_with_a_reason_and_is_kept_in_the_history()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var down = await Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(-25m, charged.RowVersion!, reason: "Fee entered wrongly"), S.Actor, default));
        var up = await Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(10m, down.RowVersion!, key: "adj-2", reason: "Late fee"), S.Actor, default));
        Assert.Equal(85m, up.BalanceOwed);
        Assert.Equal(new[] { 100m, 75m, 85m }, up.Entries.Select(e => e.BalanceAfter).ToArray());
        Assert.Equal(new[] { "Fee entered wrongly", "Late fee" }, up.Entries.Skip(1).Select(e => e.Reason).ToArray());
        Assert.Equal((1, 3, 3), await BillingCountsAsync());
    }

    [Theory]
    [InlineData("zero", "amount")]
    [InlineData("belowZero", "amount")]
    [InlineData("threeDecimals", "amount")]
    [InlineData("tooBig", "amount")]
    [InlineData("noReason", "reason")]
    [InlineData("blankReason", "reason")]
    public async Task A_wrong_adjustment_is_refused_beside_its_field_and_nothing_is_stored(string which, string field)
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var input = which switch
        {
            "zero" => AdjustmentOf(0m, charged.RowVersion!),
            "belowZero" => AdjustmentOf(-100.01m, charged.RowVersion!),
            "threeDecimals" => AdjustmentOf(5.001m, charged.RowVersion!),
            "tooBig" => AdjustmentOf(1_000_000.01m, charged.RowVersion!),
            "noReason" => AdjustmentOf(5m, charged.RowVersion!) with { Reason = null },
            _ => AdjustmentOf(5m, charged.RowVersion!, reason: "   "),
        };
        var e = await RefusedBilling(() => Bill(s => s.RecordAdjustmentAsync(Ann, input, S.Actor, default)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Contains(field, e.FieldErrors.Keys);
        Assert.Equal((1, 1, 1), await BillingCountsAsync());
    }

    [Fact]
    public async Task An_adjustment_on_an_account_with_nothing_on_it_is_refused_and_a_stale_one_is_a_conflict()
    {
        var nothing = await RefusedBilling(() => Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(5m, "AAAA"), S.Actor, default)));
        Assert.Equal("nothing_owed", nothing.Code);

        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        await Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(5m, charged.RowVersion!), S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(5m, charged.RowVersion!, key: "adj-2"), S.Actor, default)));
        Assert.Equal((105m, 2), ((await AccountAsync()).BalanceOwed, (await BillingCountsAsync()).Entries));
    }

    // ---------- audit ----------

    [Fact]
    public async Task Every_billing_action_is_audited_with_the_user_and_time_and_no_patient_or_clinical_wording()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargedAsync(plan);
        var paid = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(40m, charged.RowVersion!), S.Actor, default));
        await Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(-10m, paid.RowVersion!), S.Actor, default));

        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(BillingAccount)).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(new[] { "BillingChargeRecorded", "BillingPaymentRecorded", "BillingAdjustmentRecorded" }, audit.Select(a => a.EventType).ToArray());
        Assert.All(audit, a => Assert.Equal((S.Actor, true), (a.PerformedByUserAccountId, a.TimestampUtc != default)));
        Assert.Contains("100.00", audit[0].Details);
        Assert.Contains("40.00", audit[1].Details);
        Assert.All(audit, a => Assert.DoesNotContain("Ann", a.Details));
        Assert.All(audit, a => Assert.DoesNotContain("Periodontal", a.Details));
    }

    [Fact]
    public async Task A_failed_audit_write_stores_nothing_says_so_and_the_same_entry_works_once_the_log_is_back()
    {
        var plan = await BillablePlanAsync();
        await Sql(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var e = await RefusedBilling(() => ChargedAsync(plan));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message, StringComparison.OrdinalIgnoreCase);   // no internals reach the person
        Assert.Equal((0, 0, 0), await BillingCountsAsync());

        await Sql("DROP TRIGGER [TR_Test_RefuseBillingAudit]");
        var charged = await ChargedAsync(plan);
        Assert.Equal((100m, 1), (charged.BalanceOwed, charged.Entries.Count));
        Assert.Equal((1, 1, 1), await BillingCountsAsync());

        await Sql(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var pay = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(10m, charged.RowVersion!), S.Actor, default)));
        var adjust = await RefusedBilling(() => Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(-10m, charged.RowVersion!), S.Actor, default)));
        Assert.All(new[] { pay, adjust }, x => Assert.Equal(("save_failed", 503), (x.Code, x.StatusCode)));
        Assert.Equal((1, 1, 1), await BillingCountsAsync());
        Assert.Equal(100m, (await AccountAsync()).BalanceOwed);
        await Sql("DROP TRIGGER [TR_Test_RefuseBillingAudit]");
        Assert.Equal(90m, (await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(10m, charged.RowVersion!), S.Actor, default))).BalanceOwed);   // the same version still fits: nothing was recorded
    }

    private async Task Sql(string sql)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
