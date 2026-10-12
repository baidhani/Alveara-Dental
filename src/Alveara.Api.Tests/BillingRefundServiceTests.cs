using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 against real SQL Server: refunds (unapplied money first, then the newest applications; never more than the payment; never of a reversed payment; a refunded payment cannot be reversed; the same
/// key changes nothing), moving what a payment pays (equal totals, no duplicate or no-op rows, never more than was applied or than a charge has left, one transaction) and correcting a payment or adjustment
/// (an exact reversal plus a linked replacement that starts unapplied). After each change the preserved history verifies clean.
/// </summary>
public class BillingRefundServiceTests : BillingTestBase
{
    private Task<BillingAccountView> Charge(Guid planItem, string key) => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(planItem, key), S.Actor, default));
    private Task<BillingAccountView> Pay(BillingAccountView account, decimal amount, string key, IReadOnlyList<AllocationInput>? allocations = null) =>
        Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(amount, "Cash", null, account.RowVersion, key, allocations), S.Actor, default));
    private Task<BillingAccountView> Refund(BillingAccountView account, int payment, decimal amount, string key, string? reason = "Patient request", string method = "Cash") =>
        Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(payment, amount, method, reason, account.RowVersion, key), S.Actor, default));
    private Task<BillingAccountView> Reverse(BillingAccountView account, int entry, string key) =>
        Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(entry, "Entered in error", account.RowVersion, key), S.Actor, default));
    private Task<BillingAccountView> Move(BillingAccountView account, int payment, AllocationInput[] from, AllocationInput[] to, string key, string? reason = "Wrong procedure") =>
        Bill(s => s.MoveAllocationsAsync(Ann, new MoveAllocationInput(payment, from, to, reason, account.RowVersion, key), S.Actor, default));
    private Task<BillingAccountView> Correct(BillingAccountView account, int entry, decimal? amount, string key, string? reason = "Typed wrong amount", string? method = "Cash") =>
        Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(entry, reason, amount, method, null, null, account.RowVersion, key), S.Actor, default));
    private static AllocationInput To(int charge, decimal amount) => new(charge, amount);

    /// <summary>Charges 1 and 2 are 100 each; payment 3 is 150 with 60 on charge 1 and 40 on charge 2, so 50 is unapplied and 50 is owed.</summary>
    private async Task<BillingAccountView> PaidAsync()
    {
        var plan = await BillablePlanAsync();
        await Svc(s => s.CompleteItemAsync(plan.Plan.Id, plan.Accepted, S.Actor, default));
        await Charge(plan.Completed, "c-1");
        var two = await Charge(plan.Accepted, "c-2");
        return await Pay(two, 150m, "p-1", [To(1, 60m), To(2, 40m)]);
    }

    private async Task AssertConsistentAsync() => Assert.Empty((await Bill(s => s.VerifyLedgerAsync(Ann, default))).Findings);

    // ---------- refunds ----------

    [Fact]
    public async Task A_refund_takes_the_unapplied_money_first_and_touches_no_application()
    {
        var paid = await PaidAsync();
        var refunded = await Refund(paid, 3, 30m, "rf-1");
        var entry = refunded.Entries[^1];
        Assert.Equal((BillingKinds.Refund, 30m, 3), (entry.Kind, entry.Amount, entry.RefundsEntryNumber));
        Assert.Equal(80m, refunded.BalanceOwed);                                                    // 50 owed + 30 handed back
        Assert.Equal((20m, 100m), (refunded.Standing![2].UnappliedAmount, refunded.Standing![2].AppliedAmount));
        Assert.Equal((30m, 120m), (refunded.Standing![2].RefundedAmount, refunded.Standing![2].RefundableAmount));
        Assert.All(refunded.Allocations!, a => Assert.Equal(0m, a.ReleasedAmount));
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A_refund_beyond_the_unapplied_money_releases_the_newest_applications_first()
    {
        var paid = await PaidAsync();
        var refunded = await Refund(paid, 3, 80m, "rf-1");                                          // 50 unapplied, then 30 from the newest application (40 on charge 2)
        Assert.Equal(new[] { 0m, 30m }, refunded.Allocations!.Select(a => a.ReleasedAmount!.Value).ToArray());
        Assert.Equal((40m, 90m), (refunded.Standing![0].OpenAmount, refunded.Standing![1].OpenAmount));   // charge 1 still 60 paid, charge 2 now 10 paid
        Assert.Equal((0m, 70m), (refunded.Standing![2].UnappliedAmount, refunded.Standing![2].AppliedAmount));
        Assert.Equal(130m, refunded.BalanceOwed);
        await AssertConsistentAsync();

        var rest = await Refund(refunded, 3, 70m, "rf-2");                                          // the rest: the newest again (10), then the older (60)
        Assert.Equal(new[] { 60m, 40m }, rest.Allocations!.Select(a => a.ReleasedAmount!.Value).ToArray());
        Assert.Equal((200m, 0m), (rest.BalanceOwed, rest.Standing![2].RefundableAmount));
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A_refund_cannot_exceed_what_the_payment_still_has_and_names_a_payment_a_reason_and_a_method()
    {
        var paid = await PaidAsync();
        var over = await RefusedBilling(() => Refund(paid, 3, 150.01m, "rf-1"));
        Assert.Contains("150.00", over.FieldErrors["amount"]);
        var some = await Refund(paid, 3, 100m, "rf-2");
        Assert.Contains("50.00", (await RefusedBilling(() => Refund(some, 3, 50.01m, "rf-3"))).FieldErrors["amount"]);
        var all = await Refund(some, 3, 50m, "rf-4");
        Assert.Contains("refunded in full", (await RefusedBilling(() => Refund(all, 3, 0.01m, "rf-5"))).FieldErrors["amount"]);

        Assert.Equal("validation_failed", (await RefusedBilling(() => Refund(paid, 3, 10m, "rf-6", reason: null))).Code);
        Assert.Contains("method", (await RefusedBilling(() => Refund(paid, 3, 10m, "rf-7", method: "Bitcoin"))).FieldErrors.Keys);
        Assert.Contains("amount", (await RefusedBilling(() => Refund(paid, 3, 0m, "rf-8"))).FieldErrors.Keys);
        Assert.Equal("payment_not_found", (await RefusedBilling(() => Refund(paid, 1, 10m, "rf-9"))).Code);               // entry 1 is a charge
        Assert.Equal("payment_not_found", (await RefusedBilling(() => Refund(paid, 99, 10m, "rf-10"))).Code);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A_refund_without_a_reason_is_refused_naming_the_reason_and_records_nothing()
    {
        var paid = await PaidAsync();                                                                   // a fresh account version, so only the missing reason can refuse it
        var e = await RefusedBilling(() => Refund(paid, 3, 10m, "rf-1", reason: null));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(["reason"], e.FieldErrors.Keys);
        Assert.Equal("validation_failed", (await RefusedBilling(() => Refund(paid, 3, 10m, "rf-2", reason: "   "))).Code);
        Assert.Equal(3, (await Bill(s => s.GetAccountAsync(Ann, default))).Entries.Count);
    }

    [Fact]
    public async Task Correcting_an_adjustment_that_the_account_could_not_afford_to_reverse_is_refused_before_anything_is_saved()
    {
        var paid = await PaidAsync();                                                                   // balance 50
        var adjusted = await Bill(s => s.RecordAdjustmentAsync(Ann, new AdjustmentInput(10m, "Late fee", paid.RowVersion, "adj-1"), S.Actor, default));   // 60
        var low = await Pay(adjusted, 55m, "p-2");                                                      // balance 5: reversing the +10 would leave -5
        var e = await RefusedBilling(() => Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(4, "Wrong fee", 3m, null, null, null, low.RowVersion, "cr-1"), S.Actor, default)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Contains("entryNumber", e.FieldErrors.Keys);
        Assert.Contains("less than nothing", e.FieldErrors["entryNumber"]);
        Assert.Equal(5, (await Bill(s => s.GetAccountAsync(Ann, default))).Entries.Count);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A_reversed_payment_cannot_be_refunded_a_refunded_payment_cannot_be_reversed_and_a_refund_cannot_be_reversed()
    {
        var paid = await PaidAsync();
        var refunded = await Refund(paid, 3, 10m, "rf-1");
        Assert.Equal("payment_refunded", (await RefusedBilling(() => Reverse(refunded, 3, "r-1"))).Code);
        Assert.Equal("cannot_reverse_a_refund", (await RefusedBilling(() => Reverse(refunded, 4, "r-2"))).Code);

        var other = await PaidAsyncSecondPayment(refunded);
        var reversed = await Reverse(other.Account, other.Payment, "r-3");
        Assert.Equal("payment_reversed", (await RefusedBilling(() => Refund(reversed, other.Payment, 1m, "rf-2"))).Code);
        await AssertConsistentAsync();
    }

    private async Task<(BillingAccountView Account, int Payment)> PaidAsyncSecondPayment(BillingAccountView account)
    {
        var paid = await Pay(account, 20m, "p-2");
        return (paid, paid.Entries[^1].EntryNumber);
    }

    [Fact]
    public async Task The_same_refund_again_changes_nothing_and_the_same_key_for_something_else_is_refused()
    {
        var paid = await PaidAsync();
        var first = await Refund(paid, 3, 80m, "rf-1");
        var again = await Refund(paid, 3, 80m, "rf-1");                                              // the double click: same key, same request, an older row version
        Assert.Equal((first.Entries.Count, first.BalanceOwed), (again.Entries.Count, again.BalanceOwed));
        Assert.Equal(first.Allocations!.Select(a => a.ReleasedAmount), again.Allocations!.Select(a => a.ReleasedAmount));
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Refund(first, 3, 70m, "rf-1"))).Code);
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Refund(first, 3, 80m, "rf-1", reason: "Different"))).Code);
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Refund(first, 3, 1m, "p-1"))).Code);                  // a payment's key
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A_refund_from_an_out_of_date_account_is_a_conflict_that_changes_nothing()
    {
        var paid = await PaidAsync();
        await Refund(paid, 3, 10m, "rf-1");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Refund(paid, 3, 10m, "rf-2"));
        Assert.Equal(1, (await Bill(s => s.GetAccountAsync(Ann, default))).Entries.Count(e => e.Kind == BillingKinds.Refund));
    }

    // ---------- moving ----------

    [Fact]
    public async Task Moving_what_a_payment_pays_releases_the_old_application_and_records_the_new_one()
    {
        var paid = await PaidAsync();
        var moved = await Move(paid, 3, [To(1, 20m)], [To(2, 20m)], "mv-1");
        Assert.Equal((40m, 60m), (moved.Standing![0].PaidAmount, moved.Standing![1].PaidAmount));
        Assert.Equal(20m, moved.Allocations![0].ReleasedAmount);
        Assert.Equal((3, 3), (moved.Allocations!.Count, moved.Allocations![2].AllocationNumber));
        Assert.Equal((paid.BalanceOwed, 50m), (moved.BalanceOwed, moved.Standing![2].UnappliedAmount));       // the balance and the unapplied amount do not change
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task The_same_move_again_changes_nothing_in_any_row_order_and_a_different_one_under_the_key_is_refused()
    {
        var paid = await PaidAsync();
        var first = await Move(paid, 3, [To(1, 20m)], [To(2, 20m)], "mv-1");
        var again = await Move(paid, 3, [To(1, 20m)], [To(2, 20m)], "mv-1");
        Assert.Equal(first.Allocations!.Count, again.Allocations!.Count);
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Move(first, 3, [To(1, 10m)], [To(2, 10m)], "mv-1"))).Code);
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Move(first, 3, [To(1, 20m)], [To(2, 20m)], "mv-1", reason: "Other"))).Code);
        await AssertConsistentAsync();
    }

    [Theory]
    [InlineData("unequal", "allocations")]
    [InlineData("sameCharge", "allocations")]
    [InlineData("duplicateRelease", "releases[1]")]
    [InlineData("duplicateTarget", "allocations[1]")]
    [InlineData("zero", "releases[0]")]
    [InlineData("fraction", "allocations[0]")]
    [InlineData("noReleases", "releases")]
    [InlineData("noTargets", "allocations")]
    [InlineData("moreThanApplied", "releases[0]")]
    [InlineData("notPaidByThePayment", "releases[0]")]
    [InlineData("notACharge", "allocations[0]")]
    public async Task A_move_that_breaks_a_rule_is_refused_naming_the_row_and_changes_nothing(string which, string field)
    {
        var paid = await PaidAsync();
        AllocationInput[] from = [To(1, 20m)], to = [To(2, 20m)];
        switch (which)
        {
            case "unequal": to = [To(2, 19.99m)]; break;
            case "sameCharge": from = [To(1, 20m)]; to = [To(1, 20m)]; break;
            case "duplicateRelease": from = [To(1, 10m), To(1, 10m)]; break;
            case "duplicateTarget": to = [To(2, 10m), To(2, 10m)]; break;
            case "zero": from = [To(1, 0m)]; break;
            case "fraction": to = [To(2, 20.005m)]; break;
            case "noReleases": from = []; break;
            case "noTargets": to = []; break;
            case "moreThanApplied": from = [To(1, 60.01m)]; to = [To(2, 60.01m)]; break;
            case "notPaidByThePayment": from = [To(4, 5m)]; to = [To(2, 5m)]; break;
            case "notACharge": to = [To(3, 20m)]; break;
        }
        var before = paid.Allocations!.Count;
        var e = await RefusedBilling(() => Move(paid, 3, from, to, "mv-1"));
        Assert.Equal("validation_failed", e.Code);
        Assert.Contains(field, e.FieldErrors.Keys);
        Assert.Equal(before, (await Bill(s => s.GetAccountAsync(Ann, default))).Allocations!.Count);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A_reversed_payment_cannot_be_moved_and_a_move_needs_a_reason_and_a_payment()
    {
        var paid = await PaidAsync();
        Assert.Equal("validation_failed", (await RefusedBilling(() => Move(paid, 3, [To(1, 1m)], [To(2, 1m)], "mv-1", reason: null))).Code);
        Assert.Equal("payment_not_found", (await RefusedBilling(() => Move(paid, 1, [To(1, 1m)], [To(2, 1m)], "mv-2"))).Code);
        var reversed = await Reverse(paid, 3, "r-1");
        Assert.Equal("payment_reversed", (await RefusedBilling(() => Move(reversed, 3, [To(1, 1m)], [To(2, 1m)], "mv-3"))).Code);
    }

    // ---------- corrections ----------

    [Fact]
    public async Task Correcting_a_payment_reverses_it_exactly_and_records_a_linked_replacement_that_starts_unapplied()
    {
        var paid = await PaidAsync();
        var corrected = await Correct(paid, 3, 100m, "cr-1");
        var (reversal, replacement) = (corrected.Entries[^2], corrected.Entries[^1]);
        Assert.Equal((BillingKinds.Reversal, 150m, 3), (reversal.Kind, reversal.Amount, reversal.ReversesEntryNumber));
        Assert.Equal((BillingKinds.Payment, -100m, 3), (replacement.Kind, replacement.Amount, replacement.ReplacesEntryNumber));
        Assert.Equal((100m, 0m), (corrected.Standing![4].UnappliedAmount, corrected.Standing![4].AppliedAmount));
        Assert.Equal(100m, corrected.BalanceOwed);                                                  // 200 charged, 100 paid
        Assert.Equal((100m, 100m), (corrected.Standing![0].OpenAmount, corrected.Standing![1].OpenAmount));   // the old payment's applications are given up
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task Correcting_an_adjustment_replaces_it_and_a_charge_a_refund_a_refunded_payment_and_a_reversed_entry_cannot_be_corrected()
    {
        var paid = await PaidAsync();
        var adjusted = await Bill(s => s.RecordAdjustmentAsync(Ann, new AdjustmentInput(10m, "Late fee", paid.RowVersion, "adj-1"), S.Actor, default));
        var corrected = await Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(4, "Wrong fee", 25m, null, "Late fee corrected", null, adjusted.RowVersion, "cr-1"), S.Actor, default));
        Assert.Equal((BillingKinds.Adjustment, 25m, 4), (corrected.Entries[^1].Kind, corrected.Entries[^1].Amount, corrected.Entries[^1].ReplacesEntryNumber));
        Assert.Equal(75m, corrected.BalanceOwed);                                                   // 50 + 25

        Assert.Equal("charge_not_correctable", (await RefusedBilling(() => Correct(corrected, 1, 10m, "cr-2"))).Code);
        Assert.Equal("already_reversed", (await RefusedBilling(() => Correct(corrected, 4, 10m, "cr-3"))).Code);
        Assert.Equal("entry_not_correctable", (await RefusedBilling(() => Correct(corrected, 5, 10m, "cr-4"))).Code);          // the reversal
        var refunded = await Refund(corrected, 3, 10m, "rf-1");
        Assert.Equal("payment_refunded", (await RefusedBilling(() => Correct(refunded, 3, 100m, "cr-5"))).Code);
        Assert.Equal("entry_not_correctable", (await RefusedBilling(() => Correct(refunded, refunded.Entries[^1].EntryNumber, 5m, "cr-6"))).Code);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A_correction_cannot_leave_the_account_owing_less_than_nothing_and_a_repeat_changes_nothing()
    {
        var paid = await PaidAsync();
        var tooMuch = await RefusedBilling(() => Correct(paid, 3, 200.01m, "cr-1"));                // once the old 150 is reversed the account owes 200
        Assert.Contains("200.00", tooMuch.FieldErrors["amount"]);
        Assert.Equal("validation_failed", (await RefusedBilling(() => Correct(paid, 3, 100m, "cr-2", reason: null))).Code);
        Assert.Contains("method", (await RefusedBilling(() => Correct(paid, 3, 100m, "cr-3", method: "Bitcoin"))).FieldErrors.Keys);

        var first = await Correct(paid, 3, 100m, "cr-4");
        var again = await Correct(paid, 3, 100m, "cr-4");
        Assert.Equal((first.Entries.Count, first.BalanceOwed), (again.Entries.Count, again.BalanceOwed));
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Correct(first, 3, 90m, "cr-4"))).Code);
        await AssertConsistentAsync();
    }
}
