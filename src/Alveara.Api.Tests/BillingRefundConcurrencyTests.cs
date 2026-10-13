using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 deterministic concurrency controls. Instead of racing two requests and hoping, a hooked clock lets a competing request complete in the middle of the first one (after it has read the account and
/// checked its rules, before it saves), so the stale-version conflict happens every time: exactly one of two refunds, one of two moves, one of two corrections and one of two issues of the same number is
/// recorded, the loser gets the shared concurrency conflict and records nothing, the ledger verifies clean, and the loser's retry under the same key then returns the winner's result when it is the same
/// request (and is refused as stale or reused when it is not).
/// </summary>
public class BillingRefundConcurrencyTests : BillingTestBase
{
    private Task<T> BillWith<T>(IPracticeClock clock, Func<BillingService, Task<T>> action) => WithDb(db => action(new BillingService(db, clock)));
    private static AllocationInput To(int charge, decimal amount) => new(charge, amount);

    /// <summary>Charges 1 and 2 are 100 each; payment 3 is 150 (60 on charge 1, 40 on charge 2, 50 unapplied).</summary>
    private async Task<BillingAccountView> PaidAsync()
    {
        var plan = await BillablePlanAsync();
        await LegacyComplete(plan.Plan.Id, plan.Accepted);
        await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-1"), S.Actor, default));
        var two = await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Accepted, "c-2"), S.Actor, default));
        return await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(150m, "Cash", null, two.RowVersion, "p-1", [To(1, 60m), To(2, 40m)]), S.Actor, default));
    }

    /// <summary>Runs <paramref name="competing"/> while <paramref name="first"/> is between its checks and its save.</summary>
    private async Task<Exception?> RaceAsync<T>(Func<IPracticeClock, Task<T>> first, Func<Task> competing)
    {
        var clock = new HookedClock(SchedulingTestSupport.Clock, 1, () => competing().GetAwaiter().GetResult());
        try { await first(clock); return null; }
        catch (ConcurrencyConflictException e) { return e; }
    }

    private async Task AssertConsistentAsync() => Assert.Empty((await Bill(s => s.VerifyLedgerAsync(Ann, default))).Findings);

    [Fact]
    public async Task Of_two_refunds_that_would_together_refund_more_than_the_payment_only_the_one_that_finishes_first_is_recorded()
    {
        var paid = await PaidAsync();
        var loser = await RaceAsync(
            clock => BillWith(clock, s => s.RefundPaymentAsync(Ann, new RefundInput(3, 100m, "Cash", "Patient request", paid.RowVersion, "rf-a"), S.Actor, default)),
            () => Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(3, 100m, "Cash", "Patient request", paid.RowVersion, "rf-b"), S.Actor, default)));
        Assert.NotNull(loser);
        var after = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Equal(new[] { 100m }, after.Entries.Where(e => e.Kind == BillingKinds.Refund).Select(e => e.Amount));
        Assert.Equal(("rf-b".Length, 150m), ("rf-b".Length, after.Standing![2].RefundableAmount + 100m));
        Assert.Equal(50m, after.Standing![2].RefundableAmount);                                            // the winner's 100 is all that was refunded
        await AssertConsistentAsync();

        var retry = await RefusedBilling(() => Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(3, 100m, "Cash", "Patient request", after.RowVersion, "rf-a"), S.Actor, default)));
        Assert.Contains("50.00", retry.FieldErrors["amount"]);                                             // the loser's retry sees the new state: only 50 is left to refund
    }

    [Fact]
    public async Task A_double_submitted_refund_is_recorded_once_and_the_retry_returns_the_recorded_one()
    {
        var paid = await PaidAsync();
        var input = new RefundInput(3, 80m, "Cash", "Patient request", paid.RowVersion, "rf-same");
        var loser = await RaceAsync(clock => BillWith(clock, s => s.RefundPaymentAsync(Ann, input, S.Actor, default)), () => Bill(s => s.RefundPaymentAsync(Ann, input, S.Actor, default)));
        Assert.NotNull(loser);
        var retry = await Bill(s => s.RefundPaymentAsync(Ann, input, S.Actor, default));                   // the same request again, with the version it had
        Assert.Single(retry.Entries, e => e.Kind == BillingKinds.Refund);
        Assert.Equal(130m, retry.BalanceOwed);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task Of_two_moves_of_the_same_money_only_one_is_recorded()
    {
        var paid = await PaidAsync();
        var loser = await RaceAsync(
            clock => BillWith(clock, s => s.MoveAllocationsAsync(Ann, new MoveAllocationInput(3, [To(1, 40m)], [To(2, 40m)], "Wrong procedure", paid.RowVersion, "mv-a"), S.Actor, default)),
            () => Bill(s => s.MoveAllocationsAsync(Ann, new MoveAllocationInput(3, [To(1, 40m)], [To(2, 40m)], "Wrong procedure", paid.RowVersion, "mv-b"), S.Actor, default)));
        Assert.NotNull(loser);
        var after = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Equal((20m, 80m), (after.Standing![0].PaidAmount, after.Standing![1].PaidAmount));          // moved once: charge 1 pays 20 now, charge 2 pays 80
        Assert.Equal(3, after.Allocations!.Count);                                                          // the two originals and the one new application
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.BillingAllocationReleases.CountAsync());
        await AssertConsistentAsync();

        var again = await RefusedBilling(() => Bill(s => s.MoveAllocationsAsync(Ann, new MoveAllocationInput(3, [To(1, 40m)], [To(2, 40m)], "Wrong procedure", after.RowVersion, "mv-a"), S.Actor, default)));
        Assert.Equal("validation_failed", again.Code);                                                      // charge 1 now pays only 20 of this payment, so 40 cannot come off it
    }

    [Fact]
    public async Task Of_two_corrections_of_the_same_payment_only_one_is_recorded()
    {
        var paid = await PaidAsync();
        var loser = await RaceAsync(
            clock => BillWith(clock, s => s.CorrectEntryAsync(Ann, new CorrectionInput(3, "Typed wrong", 100m, "Cash", null, null, paid.RowVersion, "cr-a"), S.Actor, default)),
            () => Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(3, "Typed wrong", 90m, "Cash", null, null, paid.RowVersion, "cr-b"), S.Actor, default)));
        Assert.NotNull(loser);
        var after = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Equal(new[] { BillingKinds.Reversal, BillingKinds.Payment }, after.Entries.Skip(3).Select(e => e.Kind));
        Assert.Equal(-90m, after.Entries[^1].Amount);                                                      // the winner's replacement, once
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task Two_statements_issued_at_once_are_numbered_one_and_two_never_the_same_number_twice()
    {
        await PaidAsync();
        var a = await Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null), default));
        var b = await Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null), default));
        var loser = await RaceAsync(
            clock => BillWith(clock, s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(a.PreviewToken, "doc-a"), S.Actor, default)),
            () => Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(b.PreviewToken, "doc-b"), S.Actor, default)));
        Assert.NotNull(loser);                                                                              // both took number 1: the unique number lets one in
        var issued = await Bill(s => s.ListDocumentsAsync(Ann, default));
        Assert.Equal(new[] { 1 }, issued.Select(d => d.DocumentNumber));
        var retry = await Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(a.PreviewToken, "doc-a"), S.Actor, default));
        Assert.Equal(2, retry.DocumentNumber);                                                              // the loser's retry takes the next number
    }
}
