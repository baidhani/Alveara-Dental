using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C01: the ledger's arithmetic without a database. The balance is the sum of the preserved entries; a charge is open for what is left after the allocations of payments that have not been reversed; a
/// payment has an unapplied amount until it is fully applied; the default allocation is oldest charge first; and the verification names every rule a history can break. A seeded random walk through the same
/// rules the service enforces proves the identities hold after every step, so the screen's numbers can always be rebuilt from the preserved history.
/// </summary>
public class BillingLedgerMathTests
{
    private static LedgerEntry Charge(int n, decimal amount) => new(n, BillingKinds.Charge, amount, null);
    private static LedgerEntry Payment(int n, decimal amount) => new(n, BillingKinds.Payment, -amount, null);
    private static LedgerEntry Adjustment(int n, decimal amount) => new(n, BillingKinds.Adjustment, amount, null);
    private static LedgerEntry Reversal(int n, LedgerEntry undone) => new(n, BillingKinds.Reversal, -undone.Amount, undone.Number);
    private static LedgerAllocation Alloc(int payment, int charge, decimal amount) => new(payment, charge, amount);

    [Fact]
    public void The_balance_is_the_sum_of_the_entries_and_each_entry_shows_the_balance_after_it()
    {
        var entries = new[] { Charge(1, 100m), Charge(2, 70.50m), Payment(3, 60.25m), Adjustment(4, -10m), Adjustment(5, 2.35m) };
        Assert.Equal(new[] { 100m, 170.50m, 110.25m, 100.25m, 102.60m }, BillingLedgerMath.RunningBalances(entries));
        Assert.Equal(102.60m, BillingLedgerMath.Balance(entries));
        Assert.Equal(0m, BillingLedgerMath.Balance([]));
    }

    [Fact]
    public void Money_arithmetic_never_drifts_across_many_small_entries()
    {
        var entries = Enumerable.Range(1, 1000).Select(i => Charge(i, 0.01m)).ToList();
        Assert.Equal(10m, BillingLedgerMath.Balance(entries));                     // a binary float would give 9.99999999999...
    }

    [Fact]
    public void A_charge_is_open_for_what_live_payments_left_and_a_reversed_payment_releases_its_share()
    {
        var entries = new[] { Charge(1, 100m), Charge(2, 50m), Payment(3, 120m), Payment(4, 30m) };
        var allocations = new[] { Alloc(3, 1, 100m), Alloc(3, 2, 20m), Alloc(4, 2, 30m) };
        Assert.Equal(100m, BillingLedgerMath.Paid(entries, allocations, 1));
        Assert.Equal(50m, BillingLedgerMath.Paid(entries, allocations, 2));
        Assert.Empty(BillingLedgerMath.OpenCharges(entries, allocations));

        var withReversal = entries.Append(Reversal(5, entries[2])).ToArray();     // payment 3 is reversed
        Assert.Equal(0m, BillingLedgerMath.Paid(withReversal, allocations, 1));
        Assert.Equal(30m, BillingLedgerMath.Paid(withReversal, allocations, 2));
        Assert.Equal(new[] { (1, 100m), (2, 20m) }, BillingLedgerMath.OpenCharges(withReversal, allocations).ToArray());
        Assert.Equal(0m, BillingLedgerMath.Unapplied(withReversal, allocations, 3));    // a reversed payment has nothing left to apply
    }

    [Fact]
    public void A_reversed_charge_is_not_open()
    {
        var entries = new[] { Charge(1, 100m), Charge(2, 40m), Reversal(3, Charge(1, 100m)) };
        Assert.Equal(new[] { (2, 40m) }, BillingLedgerMath.OpenCharges(entries, []).ToArray());
    }

    [Fact]
    public void A_payment_has_an_unapplied_amount_until_it_is_applied_in_full()
    {
        var entries = new[] { Charge(1, 100m), Payment(2, 70m) };
        Assert.Equal(70m, BillingLedgerMath.Unapplied(entries, [], 2));
        Assert.Equal(40m, BillingLedgerMath.Unapplied(entries, [Alloc(2, 1, 30m)], 2));
        Assert.Equal(0m, BillingLedgerMath.Unapplied(entries, [Alloc(2, 1, 30m), Alloc(2, 1, 40m)], 2));
    }

    [Theory]
    [InlineData(150, new[] { 1, 2 }, new[] { 100, 50 })]          // fills the oldest first
    [InlineData(60, new[] { 1 }, new[] { 60 })]                   // stops when the money runs out
    [InlineData(500, new[] { 1, 2, 3 }, new[] { 100, 50, 80 })]   // never more than a charge has left; the rest stays unapplied
    [InlineData(0, new int[0], new int[0])]
    public void The_default_allocation_pays_the_oldest_open_charges_first(int payment, int[] charges, int[] amounts)
    {
        var open = new[] { (1, 100m), (2, 50m), (3, 80m) };
        var result = BillingLedgerMath.OldestFirst(open, payment);
        Assert.Equal(charges, result.Select(r => r.Charge).ToArray());
        Assert.Equal(amounts.Select(a => (decimal)a).ToArray(), result.Select(r => r.Amount).ToArray());
    }

    [Fact]
    public void A_consistent_history_has_no_findings()
    {
        var entries = new[] { Charge(1, 100m), Charge(2, 50m), Payment(3, 120m), Adjustment(4, 10m), Reversal(5, Adjustment(4, 10m)) };
        Assert.Empty(BillingLedgerMath.Verify(entries, [Alloc(3, 1, 100m), Alloc(3, 2, 20m)]));
    }

    [Fact]
    public void Verification_names_every_rule_a_history_can_break()
    {
        static IReadOnlyList<string> Check(LedgerEntry[] e, params LedgerAllocation[] a) => BillingLedgerMath.Verify(e, a);

        Assert.Contains(Check([Charge(1, 10m), Charge(3, 10m)]), f => f.Contains("not consecutive"));
        Assert.Contains(Check([Charge(1, 100m), new LedgerEntry(2, BillingKinds.Reversal, -99m, 1)]), f => f.Contains("not the opposite"));
        Assert.Contains(Check([Charge(1, 100m), Reversal(2, Charge(1, 100m)), new LedgerEntry(3, BillingKinds.Reversal, 100m, 2)]), f => f.Contains("undoes another reversal"));
        Assert.Contains(Check([Charge(1, 100m), Reversal(2, Charge(1, 100m)), Reversal(3, Charge(1, 100m))]), f => f.Contains("reversed 2 times"));
        Assert.Contains(Check([new LedgerEntry(1, BillingKinds.Reversal, 10m, 5)]), f => f.Contains("does not undo an earlier entry"));
        Assert.Contains(Check([Charge(1, 10m), Payment(2, 25m)]), f => f.Contains("below zero after entry 2"));
        Assert.Contains(Check([Charge(1, 100m), Payment(2, 50m)], Alloc(2, 1, 60m)), f => f.Contains("Payment 2 is applied for more than it holds"));
        Assert.Contains(Check([Charge(1, 100m), Charge(2, 100m), Payment(3, 150m), Payment(4, 50m)], Alloc(3, 1, 100m), Alloc(4, 1, 50m)), f => f.Contains("Charge 1 is paid for more than it is"));
        Assert.Contains(Check([Charge(1, 100m), Payment(2, 50m), Adjustment(3, 5m)], Alloc(2, 3, 5m)), f => f.Contains("not from a payment to a charge"));
        Assert.Contains(Check([Charge(1, 100m), Payment(2, 50m)], Alloc(2, 1, 0m)), f => f.Contains("not above zero"));
        Assert.Contains(Check([Charge(1, 100m), Payment(2, 50m), Reversal(3, Charge(1, 100m))], Alloc(2, 1, 50m)), f => f.Contains("reversed but is still being paid"));
    }

    // ---------- a seeded random walk through the rules the service enforces ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void After_every_step_of_a_random_history_it_verifies_and_the_numbers_reconcile(int seed)
    {
        var random = new Random(seed);
        var entries = new List<LedgerEntry>();
        var allocations = new List<LedgerAllocation>();
        for (var step = 0; step < 150; step++)
        {
            var n = entries.Count + 1;
            var balance = BillingLedgerMath.Balance(entries);
            var reversed = BillingLedgerMath.Reversed(entries);
            switch (random.Next(6))
            {
                case 0:
                case 1:
                    entries.Add(Charge(n, random.Next(1, 20_000) / 100m));
                    break;
                case 2 when balance > 0:                                              // a payment, no more than the balance, applied oldest first
                    var amount = Math.Min(balance, random.Next(1, 20_000) / 100m);
                    entries.Add(Payment(n, amount));
                    foreach (var (charge, part) in BillingLedgerMath.OldestFirst(BillingLedgerMath.OpenCharges(entries.Take(n - 1).ToList(), allocations), amount))
                        allocations.Add(Alloc(n, charge, part));
                    break;
                case 3:
                    var adjustment = random.Next(-5_000, 5_000) / 100m;
                    if (adjustment != 0 && balance + adjustment >= 0) entries.Add(Adjustment(n, adjustment));
                    break;
                case 4:                                                                // a reversal, where the rules allow one
                    var candidates = entries.Where(e => e.Kind != BillingKinds.Reversal && !reversed.Contains(e.Number)
                        && balance - e.Amount >= 0
                        && (e.Kind != BillingKinds.Charge || BillingLedgerMath.Paid(entries, allocations, e.Number) == 0)).ToList();
                    if (candidates.Count > 0) entries.Add(Reversal(n, candidates[random.Next(candidates.Count)]));
                    break;
                case 5 when entries.Any(e => e.Kind == BillingKinds.Payment && !reversed.Contains(e.Number)):   // applying what a payment still has
                    var payment = entries.Where(e => e.Kind == BillingKinds.Payment && !reversed.Contains(e.Number)).OrderBy(_ => random.Next()).First();
                    var left = BillingLedgerMath.Unapplied(entries, allocations, payment.Number);
                    foreach (var (charge, part) in BillingLedgerMath.OldestFirst(BillingLedgerMath.OpenCharges(entries, allocations), left))
                        allocations.Add(Alloc(payment.Number, charge, part));
                    break;
            }

            Assert.Empty(BillingLedgerMath.Verify(entries, allocations));
            // the balance owed is what is open on the charges, less what payments hold unapplied, plus the adjustments that stand
            var reversedNow = BillingLedgerMath.Reversed(entries);
            var open = BillingLedgerMath.OpenCharges(entries, allocations).Sum(o => o.Open);
            var unapplied = entries.Where(e => e.Kind == BillingKinds.Payment).Sum(e => BillingLedgerMath.Unapplied(entries, allocations, e.Number));
            var standingAdjustments = entries.Where(e => e.Kind == BillingKinds.Adjustment && !reversedNow.Contains(e.Number)).Sum(e => e.Amount);
            Assert.Equal(BillingLedgerMath.Balance(entries), open - unapplied + standingAdjustments);
        }
        Assert.True(entries.Count > 30, "the walk should have produced a real history");
    }
}
