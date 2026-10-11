using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C01 against real SQL Server: a payment is applied to the charges it names or to the oldest open ones, never for more than a charge has left or the payment holds; what is not applied stays
/// visible and can be applied later; an entry is reversed by a new entry that is exactly its opposite (releasing a payment's allocations, refused for a charge still being paid, never for a reversal, once per
/// entry, never below zero); a payer must be someone responsible; payments use the practice's configured methods; every step is audited and idempotent; and the preserved history can be rebuilt and verified.
/// </summary>
public class BillingLedgerServiceTests : BillingTestBase
{
    private Task<BillingAccountView> AccountAsync(Guid? patient = null) => Bill(s => s.GetAccountAsync(patient ?? Ann, default));
    private Task<BillingAccountView> Charge(Guid planItem, string key) => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(planItem, key), S.Actor, default));
    private Task<BillingAccountView> Pay(BillingAccountView account, decimal amount, string key, IReadOnlyList<AllocationInput>? allocations = null, Guid? paidBy = null, string method = "Cash") =>
        Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(amount, method, null, account.RowVersion, key, allocations, paidBy), S.Actor, default));
    private Task<BillingAccountView> Apply(BillingAccountView account, int payment, string key, IReadOnlyList<AllocationInput>? allocations = null) =>
        Bill(s => s.ApplyPaymentAsync(Ann, new ApplyPaymentInput(payment, allocations, account.RowVersion, key), S.Actor, default));
    private Task<BillingAccountView> Reverse(BillingAccountView account, int entry, string key, string reason = "Entered in error") =>
        Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(entry, reason, account.RowVersion, key), S.Actor, default));
    private static AllocationInput To(int charge, decimal amount) => new(charge, amount);

    /// <summary>Two completed procedures, both billed at 100: entries 1 and 2, balance 200.</summary>
    private async Task<(BillablePlan Plan, BillingAccountView Account)> TwoChargesAsync()
    {
        var plan = await BillablePlanAsync();
        await Svc(s => s.CompleteItemAsync(plan.Plan.Id, plan.Accepted, S.Actor, default));
        await Charge(plan.Completed, "c-1");
        return (plan, await Charge(plan.Accepted, "c-2"));
    }

    // ---------- applying payments ----------

    [Fact]
    public async Task A_payment_that_names_no_charge_is_applied_to_the_oldest_open_charges_first()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 150m, "p-1");
        var payment = paid.Standing![2];
        Assert.Equal((150m, 0m), (payment.AppliedAmount, payment.UnappliedAmount));
        Assert.Equal((100m, 0m, 50m, 50m), (paid.Standing![0].PaidAmount, paid.Standing![0].OpenAmount, paid.Standing![1].PaidAmount, paid.Standing![1].OpenAmount));
        Assert.Equal(new[] { (1, 100m), (2, 50m) }, paid.Allocations!.Select(a => (a.ChargeEntryNumber, a.Amount)).ToArray());
        Assert.All(paid.Allocations!, a => Assert.Equal((3, false), (a.PaymentEntryNumber, a.Released)));
        Assert.Equal((50m, 50m, 0m), (paid.BalanceOwed, paid.OpenChargeTotal, paid.UnappliedTotal));
    }

    [Fact]
    public async Task A_payment_can_name_the_charge_it_pays_and_what_it_does_not_apply_stays_visible_until_applied_later()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 100m, "p-1", [To(2, 40m)]);                                           // pays charge 2 only, for 40 of the 100
        Assert.Equal((40m, 60m), (paid.Standing![2].AppliedAmount, paid.Standing![2].UnappliedAmount));
        Assert.Equal((0m, 40m, 60m), (paid.Standing![0].PaidAmount, paid.Standing![1].PaidAmount, paid.UnappliedTotal));

        var later = await Apply(paid, 3, "a-1");                                                          // the oldest open charge first: charge 1
        Assert.Equal((100m, 0m), (later.Standing![2].AppliedAmount, later.Standing![2].UnappliedAmount));
        Assert.Equal((60m, 40m), (later.Standing![0].PaidAmount, later.Standing![1].PaidAmount));
        Assert.Equal(new[] { 1, 2 }, later.Allocations!.Select(a => a.AllocationNumber).ToArray());       // numbered one after another, the earlier row untouched
        Assert.Equal(100m, later.BalanceOwed);

        var again = await RefusedBilling(() => Apply(later, 3, "a-2"));
        Assert.Equal(("nothing_to_apply", 409), (again.Code, again.StatusCode));
    }

    [Fact]
    public async Task A_payment_larger_than_the_open_charges_leaves_the_rest_unapplied_when_an_adjustment_raised_the_balance()
    {
        var plan = await BillablePlanAsync();
        var charged = await Charge(plan.Completed, "c-1");
        var adjusted = await Bill(s => s.RecordAdjustmentAsync(Ann, new AdjustmentInput(10m, "Late fee", charged.RowVersion, "adj-1"), S.Actor, default));
        var paid = await Pay(adjusted, 110m, "p-1");                                                       // the balance is 110: the charge's 100 is applied, the late fee has nothing to apply to
        Assert.Equal((100m, 10m, 0m), (paid.Standing![2].AppliedAmount, paid.Standing![2].UnappliedAmount, paid.BalanceOwed));
        Assert.Equal(10m, paid.UnappliedTotal);
    }

    [Theory]
    [InlineData("unknownCharge", "allocations[0]")]
    [InlineData("namedTwice", "allocations[1]")]
    [InlineData("moreThanLeft", "allocations[0]")]
    [InlineData("zero", "allocations[0]")]
    [InlineData("threeDecimals", "allocations[0]")]
    [InlineData("noCharge", "allocations[0]")]
    [InlineData("moreThanThePayment", "allocations")]
    public async Task A_wrong_allocation_is_refused_beside_its_entry_and_nothing_is_stored(string which, string field)
    {
        var (_, two) = await TwoChargesAsync();
        var asked = which switch
        {
            "unknownCharge" => new[] { To(9, 10m) },
            "namedTwice" => new[] { To(1, 10m), To(1, 10m) },
            "moreThanLeft" => new[] { To(1, 100.01m) },
            "zero" => new[] { To(1, 0m) },
            "threeDecimals" => new[] { To(1, 10.005m) },
            "noCharge" => new[] { new AllocationInput(null, 10m) },
            _ => new[] { To(1, 100m), To(2, 100m) },
        };
        var e = await RefusedBilling(() => Pay(two, which == "moreThanThePayment" ? 150m : 100m, "p-1", asked));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Contains(field, e.FieldErrors.Keys);
        Assert.Equal((2, 0), ((await AccountAsync()).Entries.Count, (await AccountAsync()).Allocations!.Count));
    }

    [Fact]
    public async Task Applying_refuses_a_reversed_payment_an_unknown_one_a_charge_with_nothing_left_and_a_reused_key()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 100m, "p-1", [To(1, 100m)]);
        var reversed = await Reverse(paid, 3, "r-1");
        Assert.Equal(("payment_reversed", 409), await CodeAsync(() => Apply(reversed, 3, "a-1")));
        Assert.Equal(("payment_not_found", 404), await CodeAsync(() => Apply(reversed, 1, "a-2")));         // entry 1 is a charge
        Assert.Equal(("payment_not_found", 404), await CodeAsync(() => Apply(reversed, 99, "a-3")));
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Apply(reversed, 3, "p-1"))).Code);        // that key made a payment

        var again = await Pay(reversed, 200m, "p-2", [To(1, 100m)]);                                       // paying charge 1 again after the first payment was reversed is allowed
        var done = await Apply(again, 5, "a-4");                                                            // what is left goes to the oldest open charge: charge 2
        Assert.Equal(0m, done.Standing![4].UnappliedAmount);
        Assert.Equal(("nothing_to_apply", 409), await CodeAsync(() => Apply(done, 5, "a-5")));
    }

    private static async Task<(string Code, int Status)> CodeAsync<T>(Func<Task<T>> action)
    {
        var e = await RefusedBilling(action);
        return (e.Code, e.StatusCode);
    }

    [Fact]
    public async Task Repeating_a_payment_with_allocations_or_an_application_adds_nothing()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 100m, "p-1", [To(2, 40m)]);
        var again = await Pay(two, 100m, "p-1", [To(2, 40m)]);                                              // the same key, even from the older account version
        Assert.Equal((paid.Entries.Count, paid.Allocations!.Count), (again.Entries.Count, again.Allocations!.Count));

        var applied = await Apply(paid, 3, "a-1");
        var twice = await Apply(paid, 3, "a-1");                                                            // a double click: the same key quiet, even with the old version
        Assert.Equal(applied.Allocations!.Count, twice.Allocations!.Count);
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Apply(applied, 99, "a-1"))).Code);
    }

    // ---------- reversing entries ----------

    [Fact]
    public async Task Reversing_a_payment_adds_the_opposite_entry_releases_its_allocations_and_changes_nothing_that_was_recorded()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 150m, "p-1");
        var reversed = await Reverse(paid, 3, "r-1", "Paid on the wrong account");

        Assert.Equal(("Reversal", 150m, 200m), (reversed.Entries[3].Kind, reversed.Entries[3].Amount, reversed.BalanceOwed));
        Assert.Equal((3, "Paid on the wrong account"), (reversed.Entries[3].ReversesEntryNumber, reversed.Entries[3].Reason));
        Assert.Equal((true, 4), (reversed.Standing![2].IsReversed, reversed.Standing![2].ReversedByEntryNumber));
        Assert.Equal(paid.Entries[2], reversed.Entries[2]);                                                // the payment as recorded is exactly what it was; only what it now stands for changed
        Assert.All(reversed.Allocations!, a => Assert.True(a.Released));                                   // its allocations are released, not deleted
        Assert.Equal((100m, 100m, 0m, 200m), (reversed.Standing![0].OpenAmount, reversed.Standing![1].OpenAmount, reversed.UnappliedTotal, reversed.OpenChargeTotal));
    }

    [Fact]
    public async Task A_charge_with_payments_applied_to_it_cannot_be_reversed_until_they_are_and_the_procedure_stays_billed()
    {
        var (plan, two) = await TwoChargesAsync();
        var paid = await Pay(two, 100m, "p-1", [To(1, 100m)]);
        var refused = await RefusedBilling(() => Reverse(paid, 1, "r-1"));
        Assert.Equal(("charge_has_payments", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(paid.Entries.Count, (await AccountAsync()).Entries.Count);                            // nothing was added

        var released = await Reverse(paid, 3, "r-2");                                                       // reverse the payment...
        var voided = await Reverse(released, 1, "r-3", "Billed in error");                                  // ...and now the charge
        Assert.Equal((true, 100m), (voided.Standing![0].IsReversed, voided.BalanceOwed));
        Assert.Equal(0m, voided.Standing![0].OpenAmount);

        Assert.Empty(await Bill(s => s.ListBillableAsync(Ann, default)));                                   // the procedure is not offered for billing again
        Assert.Equal("already_billed", (await RefusedBilling(() => Charge(plan.Completed, "c-9"))).Code);
    }

    [Fact]
    public async Task An_adjustment_can_be_reversed_and_a_reversal_that_would_overdraw_the_account_is_refused()
    {
        var plan = await BillablePlanAsync();
        var charged = await Charge(plan.Completed, "c-1");
        var adjusted = await Bill(s => s.RecordAdjustmentAsync(Ann, new AdjustmentInput(10m, "Late fee", charged.RowVersion, "adj-1"), S.Actor, default));
        var paid = await Pay(adjusted, 110m, "p-1");                                                        // balance 0

        var overdraw = await RefusedBilling(() => Reverse(paid, 2, "r-1"));                                 // taking back the late fee would leave -10
        Assert.Equal(("validation_failed", 400), (overdraw.Code, overdraw.StatusCode));
        Assert.Contains("entryNumber", overdraw.FieldErrors.Keys);

        var released = await Reverse(paid, 3, "r-2", "Paid on the wrong account");                         // reverse the payment first
        var undone = await Reverse(released, 2, "r-3", "Late fee charged in error");                        // now the adjustment can be reversed too
        Assert.Equal(100m, undone.BalanceOwed);
    }

    [Fact]
    public async Task A_reversal_cannot_be_reversed_an_entry_is_reversed_once_and_a_missing_entry_or_reason_is_refused()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 50m, "p-1");
        var reversed = await Reverse(paid, 3, "r-1");
        Assert.Equal(("cannot_reverse_a_reversal", 409), await CodeAsync(() => Reverse(reversed, 4, "r-2")));
        Assert.Equal(("already_reversed", 409), await CodeAsync(() => Reverse(reversed, 3, "r-3")));
        Assert.Equal(("entry_not_found", 404), await CodeAsync(() => Reverse(reversed, 99, "r-4")));

        var noReason = await RefusedBilling(() => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(3, "  ", reversed.RowVersion, "r-5"), S.Actor, default)));
        var nothing = await RefusedBilling(() => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(null, null, reversed.RowVersion, null), S.Actor, default)));
        Assert.Contains("reason", noReason.FieldErrors.Keys);
        Assert.Equal(new[] { "entryNumber", "idempotencyKey", "reason" }, nothing.FieldErrors.Keys.Order().ToArray());
        Assert.Equal(4, (await AccountAsync()).Entries.Count);
    }

    [Fact]
    public async Task Repeating_a_reversal_is_quiet_and_a_different_one_under_the_same_key_is_refused()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 50m, "p-1");
        var reversed = await Reverse(paid, 3, "r-1");
        var again = await Reverse(paid, 3, "r-1");                                                           // the same key from the older version
        Assert.Equal(reversed.Entries.Count, again.Entries.Count);
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Reverse(reversed, 1, "r-1"))).Code);
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Reverse(reversed, 3, "r-1", "A different reason"))).Code);
    }

    [Fact]
    public async Task A_stale_account_makes_applying_or_reversing_a_conflict_and_two_people_reversing_at_once_end_with_one_reversal()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 100m, "p-1", [To(2, 40m)]);
        await Pay(paid, 10m, "p-x");                                                                       // someone else records a payment: the copy in hand is now out of date
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Apply(paid, 3, "a-1"));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Reverse(paid, 3, "r-1"));

        var fresh = await AccountAsync();
        var results = await Task.WhenAll(Attempt(() => Reverse(fresh, 4, "desk-1")), Attempt(() => Reverse(fresh, 4, "desk-2")));
        var outcome = string.Join(" | ", results.Select(r => r?.GetType().Name + ": " + r?.Message));
        Assert.True(results.Count(r => r is null) == 1, outcome);
        // the loser either hits the account version at save (a conflict) or reads the ledger after the winner committed and is told the entry is already reversed: both stop a double reversal
        Assert.True(results.Count(r => r is ConcurrencyConflictException or BillingException { Code: "already_reversed" }) == 1, outcome);
        Assert.Equal(1, (await AccountAsync()).Entries.Count(e => e.Kind == BillingKinds.Reversal));

        static async Task<Exception?> Attempt(Func<Task<BillingAccountView>> action)
        {
            try { await action(); return null; }
            catch (Exception e) { return e; }
        }
    }

    // ---------- who paid, and how ----------

    [Fact]
    public async Task A_payment_can_be_recorded_as_made_by_someone_responsible_for_the_account_and_nobody_else()
    {
        var (_, two) = await TwoChargesAsync();
        var stranger = await RefusedBilling(() => Pay(two, 10m, "p-1", paidBy: Bo));
        Assert.Contains("paidBy", stranger.FieldErrors.Keys);

        var accountId = await WithDb(db => db.BillingAccounts.Where(a => a.PatientId == Ann).Select(a => a.Id).SingleAsync());
        await using (var db = Fixture.CreateContext())
        {
            db.BillingResponsibilities.Add(new BillingResponsibility { Id = Guid.NewGuid(), AccountId = accountId, PatientId = Ann, Sequence = 1, ResponsiblePatientId = Bo, EffectiveFrom = new DateOnly(2030, 1, 1), SetAtUtc = DateTimeOffset.UtcNow, SetByUserId = S.Actor });
            await db.SaveChangesAsync();
        }
        var paid = await Pay(await AccountAsync(), 10m, "p-2", paidBy: Bo);
        Assert.False(string.IsNullOrWhiteSpace(paid.Entries[2].PaidByName));
        var self = await Pay(paid, 10m, "p-3", paidBy: Ann);
        Assert.Null(self.Entries[3].PaidByName);                                                             // the patient themselves is not recorded as a separate payer
    }

    [Fact]
    public async Task A_payment_uses_a_configured_active_method_by_name_and_the_ledger_keeps_its_kind()
    {
        var (_, two) = await TwoChargesAsync();
        var venmo = new BillingPaymentMethod { Id = Guid.NewGuid(), Name = "Venmo", Kind = BillingPaymentMethods.Other, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        await using (var db = Fixture.CreateContext()) { db.BillingPaymentMethods.Add(venmo); await db.SaveChangesAsync(); }

        var paid = await Pay(two, 10m, "p-1", method: "venmo");                                              // case does not matter
        Assert.Equal(("Other", "Venmo"), (paid.Entries[2].PaymentMethod, paid.Entries[2].PaymentMethodName));

        var unknown = await RefusedBilling(() => Pay(paid, 10m, "p-2", method: "Barter"));
        Assert.Contains("Venmo", unknown.FieldErrors["method"]);                                              // the message lists what can be chosen
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("UPDATE [BillingPaymentMethods] SET IsActive = 0 WHERE Id = {0}", venmo.Id);
        var inactive = await RefusedBilling(() => Pay(paid, 10m, "p-3", method: "Venmo"));
        Assert.DoesNotContain("Venmo", inactive.FieldErrors["method"]);
        Assert.Equal("Venmo", (await AccountAsync()).Entries[2].PaymentMethodName);                           // an old payment keeps showing its method
    }

    // ---------- audit and the preserved history ----------

    [Fact]
    public async Task Applying_and_reversing_are_audited_with_the_user_and_the_reason_and_a_failed_audit_stores_nothing()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 100m, "p-1", [To(2, 40m)]);
        var applied = await Apply(paid, 3, "a-1");
        await Reverse(applied, 3, "r-1", "Wrong account");

        await using (var db = Fixture.CreateContext())
        {
            var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(BillingAccount)).Select(a => new { a.EventType, a.Details, a.Reason, a.PerformedByUserAccountId }).ToListAsync();
            Assert.Equal(new[] { "BillingChargeRecorded", "BillingChargeRecorded", "BillingEntryReversed", "BillingPaymentApplied", "BillingPaymentRecorded" }, audit.Select(a => a.EventType).Order().ToArray());
            Assert.Equal("Wrong account", audit.Single(a => a.EventType == "BillingEntryReversed").Reason);
            Assert.All(audit, a => Assert.Equal(S.Actor, a.PerformedByUserAccountId));
            Assert.All(audit, a => Assert.DoesNotContain("Ann", a.Details));
        }

        var fresh = await AccountAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var before = await BillingCountsAsync();
        Assert.Equal(("save_failed", 503), await CodeAsync(() => Reverse(fresh, 1, "r-2")));
        Assert.Equal(("save_failed", 503), await CodeAsync(() => Pay(fresh, 10m, "p-2")));
        Assert.Equal(before, await BillingCountsAsync());
        Assert.Equal(fresh.Allocations!.Count, (await AccountAsync()).Allocations!.Count);
    }

    [Fact]
    public async Task The_balance_and_every_derived_number_can_be_rebuilt_and_verified_from_the_preserved_entries()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 150m, "p-1");
        var reversed = await Reverse(paid, 3, "r-1");
        var again = await Pay(reversed, 120m, "p-2", [To(2, 100m)]);
        var adjusted = await Bill(s => s.RecordAdjustmentAsync(Ann, new AdjustmentInput(-20m, "Courtesy", again.RowVersion, "adj-1"), S.Actor, default));

        var verification = await Bill(s => s.VerifyLedgerAsync(Ann, default));
        Assert.Equal((true, adjusted.BalanceOwed, 6), (verification.Consistent, verification.BalanceOwed, verification.EntryCount));
        Assert.Empty(verification.Findings);
        var math = adjusted.Entries.Select(e => new LedgerEntry(e.EntryNumber, e.Kind, e.Amount, e.ReversesEntryNumber)).ToList();
        Assert.Equal(adjusted.Entries.Select(e => e.BalanceAfter).ToArray(), BillingLedgerMath.RunningBalances(math).ToArray());          // each shown balance is the rebuilt one
        Assert.Equal(adjusted.BalanceOwed, adjusted.OpenChargeTotal - adjusted.UnappliedTotal + (-20m));

        var none = await Bill(s => s.VerifyLedgerAsync(Bo, default));                                       // a patient with nothing billed is consistent and owes nothing
        Assert.Equal((true, 0m, 0), (none.Consistent, none.BalanceOwed, none.EntryCount));
    }

    [Fact]
    public async Task Verification_finds_a_history_that_was_tampered_with_behind_the_database_rules()
    {
        var (_, two) = await TwoChargesAsync();
        var paid = await Pay(two, 100m, "p-1", [To(1, 100m)]);
        // with the allocation triggers switched off, as only an administrator at the database could, an over-allocation is written; the ledger rebuilds from what is preserved and names it
        await ExecuteAsync("DISABLE TRIGGER [TR_BillingAllocations_Links] ON [BillingAllocations]");
        try
        {
            var accountId = await WithDb(db => db.BillingAccounts.Where(a => a.PatientId == Ann).Select(a => a.Id).SingleAsync());
            var ids = await WithDb(db => db.BillingEntries.Where(e => e.AccountId == accountId).OrderBy(e => e.EntryNumber).Select(e => e.Id).ToListAsync());
            await using (var db = Fixture.CreateContext())
            {
                db.BillingAllocations.Add(new BillingAllocation { Id = Guid.NewGuid(), AccountId = accountId, PatientId = Ann, AllocationNumber = 2, PaymentEntryId = ids[2], ChargeEntryId = ids[0], Amount = 50m, IdempotencyKey = "tamper", AllocatedAtUtc = DateTimeOffset.UtcNow, AllocatedByUserId = S.Actor });
                await db.SaveChangesAsync();
            }
            var verification = await Bill(s => s.VerifyLedgerAsync(Ann, default));
            Assert.False(verification.Consistent);
            Assert.Contains(verification.Findings, f => f.Contains("Payment 3 is applied for more than it holds"));
            Assert.Contains(verification.Findings, f => f.Contains("Charge 1 is paid for more than it is"));
        }
        finally
        {
            await ExecuteAsync("ENABLE TRIGGER [TR_BillingAllocations_Links] ON [BillingAllocations]");
        }
    }
}
