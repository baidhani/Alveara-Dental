using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02: the ledger arithmetic with refunds and releases, without a database. A refund adds to the balance like a charge, names one payment and is capped by what that payment still has; a release
/// is subtracted from an allocation, so a payment's unapplied amount, a charge's open amount and the verification all read net of releases. Verification names every rule a refunded history can break.
/// </summary>
public class BillingRefundMathTests
{
    private static LedgerEntry Charge(int n, decimal amount) => new(n, BillingKinds.Charge, amount, null);
    private static LedgerEntry Payment(int n, decimal amount) => new(n, BillingKinds.Payment, -amount, null);
    private static LedgerEntry Refund(int n, int payment, decimal amount) => new(n, BillingKinds.Refund, amount, null, payment);
    private static LedgerEntry Reversal(int n, LedgerEntry undone) => new(n, BillingKinds.Reversal, -undone.Amount, undone.Number);
    private static LedgerAllocation Alloc(int payment, int charge, decimal amount) => new(payment, charge, amount);
    private static LedgerRelease Release(int payment, int charge, decimal amount, int? refund = null) => new(payment, charge, amount, refund);

    // charges 1,2 = 100 each; payment 3 = 150 (60 to charge 1, 40 to charge 2, 50 unapplied)
    private static (LedgerEntry[] Entries, LedgerAllocation[] Allocations) Basis() =>
        ([Charge(1, 100m), Charge(2, 100m), Payment(3, 150m)], [Alloc(3, 1, 60m), Alloc(3, 2, 40m)]);

    [Fact]
    public void A_refund_adds_to_the_balance_and_reduces_what_the_payment_can_still_give_back()
    {
        var (basis, allocations) = Basis();
        var entries = basis.Append(Refund(4, 3, 50m)).ToArray();
        Assert.Equal(50m, BillingLedgerMath.Balance(basis));
        Assert.Equal(100m, BillingLedgerMath.Balance(entries));
        Assert.Equal(50m, BillingLedgerMath.Refunded(entries, 3));
        Assert.Equal(100m, BillingLedgerMath.Refundable(entries, 3));
        Assert.Equal(0m, BillingLedgerMath.Unapplied(entries, allocations, 3));     // the refund took the unapplied 50
    }

    [Fact]
    public void A_reversed_payment_has_nothing_left_to_refund()
    {
        var (basis, _) = Basis();
        var entries = basis.Append(Reversal(4, basis[2])).ToArray();
        Assert.Equal(0m, BillingLedgerMath.Refundable(entries, 3));
    }

    [Fact]
    public void Releases_are_subtracted_from_what_a_charge_has_been_paid_and_from_what_a_payment_has_applied()
    {
        var (basis, allocations) = Basis();
        LedgerRelease[] releases = [Release(3, 2, 15m)];
        Assert.Equal(25m, BillingLedgerMath.Paid(basis, allocations, 2, releases));
        Assert.Equal(40m, BillingLedgerMath.Paid(basis, allocations, 2));          // without releases the old arithmetic holds
        Assert.Equal(85m, BillingLedgerMath.Applied(allocations, 3, releases));
        Assert.Equal(65m, BillingLedgerMath.Unapplied(basis, allocations, 3, releases));
        Assert.Equal([(1, 40m), (2, 75m)], BillingLedgerMath.OpenCharges(basis, allocations, releases));
    }

    [Fact]
    public void A_refund_of_the_whole_payment_with_its_releases_leaves_an_exact_ledger()
    {
        var (basis, allocations) = Basis();
        var entries = basis.Append(Refund(4, 3, 150m)).ToArray();
        LedgerRelease[] releases = [Release(3, 1, 60m, 4), Release(3, 2, 40m, 4)];
        Assert.Equal(200m, BillingLedgerMath.Balance(entries));
        Assert.Equal(0m, BillingLedgerMath.Unapplied(entries, allocations, 3, releases));
        Assert.Equal([(1, 100m), (2, 100m)], BillingLedgerMath.OpenCharges(entries, allocations, releases));
        Assert.Empty(BillingLedgerMath.Verify(entries, allocations, releases));
    }

    [Fact]
    public void A_consistent_refunded_history_verifies_clean()
    {
        var (basis, allocations) = Basis();
        var entries = basis.Append(Refund(4, 3, 50m)).Append(Refund(5, 3, 20m)).ToArray();
        LedgerRelease[] releases = [Release(3, 2, 20m, 5)];
        Assert.Empty(BillingLedgerMath.Verify(entries, allocations, releases));
    }

    [Fact]
    public void Verify_names_a_refund_beyond_its_payment_a_refund_of_a_reversed_payment_and_a_refund_naming_no_payment()
    {
        var (basis, allocations) = Basis();
        Assert.Contains(BillingLedgerMath.Verify(basis.Append(Refund(4, 3, 150.01m)).ToArray(), allocations), f => f.Contains("refunded for more than it was"));
        Assert.Contains(BillingLedgerMath.Verify(basis.Append(Reversal(4, basis[2])).Append(Refund(5, 3, 10m)).ToArray(), allocations), f => f.Contains("reversed and also refunded"));
        Assert.Contains(BillingLedgerMath.Verify(basis.Append(Refund(4, 1, 10m)).ToArray(), allocations), f => f.Contains("does not name an earlier payment"));
        Assert.Contains(BillingLedgerMath.Verify(basis.Append(new LedgerEntry(4, BillingKinds.Refund, 10m, null)).ToArray(), allocations), f => f.Contains("does not name an earlier payment"));
    }

    [Fact]
    public void Verify_names_a_payment_applied_for_more_than_it_holds_after_its_refunds()
    {
        var (basis, allocations) = Basis();
        var entries = basis.Append(Refund(4, 3, 60m)).ToArray();                    // 150 - 60 = 90 left, but 100 is still applied
        Assert.Contains(BillingLedgerMath.Verify(entries, allocations), f => f.Contains("Payment 3 is applied for more than it holds"));
        Assert.DoesNotContain(BillingLedgerMath.Verify(entries, allocations, [Release(3, 1, 10m, 4)]), f => f.Contains("applied for more than it holds"));
    }

    [Fact]
    public void Verify_names_a_release_beyond_its_allocation_and_the_releases_of_a_refund_beyond_the_refund()
    {
        var (basis, allocations) = Basis();
        var entries = basis.Append(Refund(4, 3, 50m)).ToArray();
        Assert.Contains(BillingLedgerMath.Verify(entries, allocations, [Release(3, 2, 40.01m, 4)]), f => f.Contains("released from charge 2 for more than was applied"));
        Assert.Contains(BillingLedgerMath.Verify(entries, allocations, [Release(3, 1, 30m, 4), Release(3, 2, 30m, 4)]), f => f.Contains("releases of refund 4 are more than the refund"));
        Assert.Contains(BillingLedgerMath.Verify(entries, allocations, [Release(3, 1, 10m, 2)]), f => f.Contains("releases of refund 2 are more than the refund"));   // names an entry that is not a refund
    }

    [Fact]
    public void A_release_of_a_reversed_payment_changes_nothing_because_its_allocations_are_already_given_up()
    {
        var (basis, allocations) = Basis();
        var entries = basis.Append(Reversal(4, basis[2])).ToArray();
        Assert.Equal(0m, BillingLedgerMath.Paid(entries, allocations, 1, [Release(3, 1, 10m)]));
        Assert.Equal(0m, BillingLedgerMath.Unapplied(entries, allocations, 3, [Release(3, 1, 10m)]));
    }

    [Fact]
    public void Cents_never_drift_across_many_small_refunds_and_releases()
    {
        var entries = new List<LedgerEntry> { Charge(1, 10m), Payment(2, 10m) };
        var allocations = new[] { Alloc(2, 1, 10m) };
        var releases = new List<LedgerRelease>();
        for (var i = 0; i < 1000; i++)
        {
            entries.Add(Refund(3 + i, 2, 0.01m));
            releases.Add(Release(2, 1, 0.01m, 3 + i));
        }
        Assert.Equal(10m, BillingLedgerMath.Refunded(entries, 2));
        Assert.Equal(0m, BillingLedgerMath.Applied(allocations, 2, releases));
        Assert.Equal(10m, BillingLedgerMath.Paid(entries.Take(2).ToList(), allocations, 1));
        Assert.Equal(0m, BillingLedgerMath.Paid(entries, allocations, 1, releases));
        Assert.Empty(BillingLedgerMath.Verify(entries, allocations, releases));
    }
}
