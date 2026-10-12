using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>One ledger entry as the math sees it: its number, kind, signed amount and, for a reversal, the number of the entry it undoes.</summary>
public sealed record LedgerEntry(int Number, string Kind, decimal Amount, int? ReversesNumber, int? RefundsNumber = null);

/// <summary>How much of one payment pays one charge, by entry number.</summary>
public sealed record LedgerAllocation(int PaymentNumber, int ChargeNumber, decimal Amount);

/// <summary>ALV-007-C02: part of an allocation given back (by a refund or a correction), by entry number. The negative of an allocation: it never changes the allocation row, it is subtracted from it.</summary>
public sealed record LedgerRelease(int PaymentNumber, int ChargeNumber, decimal Amount, int? RefundNumber = null);

/// <summary>
/// ALV-007-C01: the ledger's arithmetic, on its own so it can be proven without a database and so the service, the screen's numbers and the verification all use the same rules. Everything is derived from the
/// preserved entries and allocations (nothing is stored): the balance owed is the sum of the entries; a charge is open for what is left after the allocations of payments that have not been reversed;
/// a payment has an unapplied amount until it is fully allocated. Money is the ALV-N002 type throughout (decimal, two places, banker's rounding).
/// </summary>
public static class BillingLedgerMath
{
    /// <summary>The balance owed after each entry, in entry order.</summary>
    public static IReadOnlyList<decimal> RunningBalances(IEnumerable<LedgerEntry> entries)
    {
        var running = AppMoney.Zero;
        var result = new List<decimal>();
        foreach (var e in entries.OrderBy(e => e.Number))
        {
            running += AppMoney.FromDecimal(e.Amount);
            result.Add(running.Amount);
        }
        return result;
    }

    public static decimal Balance(IEnumerable<LedgerEntry> entries) => RunningBalances(entries).DefaultIfEmpty(0m).Last();

    /// <summary>The numbers of the entries that a reversal has undone.</summary>
    public static ISet<int> Reversed(IEnumerable<LedgerEntry> entries) => entries.Where(e => e.ReversesNumber is not null).Select(e => e.ReversesNumber!.Value).ToHashSet();

    /// <summary>What each payment has been applied to so far, in total (a reversed payment keeps its rows but its allocations are released, so see <see cref="Unapplied"/>).</summary>
    public static decimal Applied(IEnumerable<LedgerAllocation> allocations, int paymentNumber, IEnumerable<LedgerRelease>? releases = null) =>
        (allocations.Where(a => a.PaymentNumber == paymentNumber).Aggregate(AppMoney.Zero, (sum, a) => sum + AppMoney.FromDecimal(a.Amount))
         - (releases ?? []).Where(r => r.PaymentNumber == paymentNumber).Aggregate(AppMoney.Zero, (sum, r) => sum + AppMoney.FromDecimal(r.Amount))).Amount;

    /// <summary>What has been handed back of a payment so far: the sum of the refund entries that name it.</summary>
    public static decimal Refunded(IEnumerable<LedgerEntry> entries, int paymentNumber) =>
        entries.Where(e => e.Kind == BillingKinds.Refund && e.RefundsNumber == paymentNumber).Aggregate(AppMoney.Zero, (sum, e) => sum + AppMoney.FromDecimal(e.Amount)).Amount;

    /// <summary>What can still be refunded of a payment: what it was less what was refunded; zero once it is reversed.</summary>
    public static decimal Refundable(IReadOnlyList<LedgerEntry> entries, int paymentNumber)
    {
        var payment = entries.First(e => e.Number == paymentNumber);
        if (Reversed(entries).Contains(paymentNumber)) return 0m;
        return (-AppMoney.FromDecimal(payment.Amount) - AppMoney.FromDecimal(Refunded(entries, paymentNumber))).Amount;
    }

    /// <summary>What a charge has been paid so far: the allocations whose payment has not been reversed.</summary>
    public static decimal Paid(IEnumerable<LedgerEntry> entries, IEnumerable<LedgerAllocation> allocations, int chargeNumber, IEnumerable<LedgerRelease>? releases = null)
    {
        var reversed = Reversed(entries);
        var given = allocations.Where(a => a.ChargeNumber == chargeNumber && !reversed.Contains(a.PaymentNumber)).Aggregate(AppMoney.Zero, (sum, a) => sum + AppMoney.FromDecimal(a.Amount));
        var released = (releases ?? []).Where(r => r.ChargeNumber == chargeNumber && !reversed.Contains(r.PaymentNumber)).Aggregate(AppMoney.Zero, (sum, r) => sum + AppMoney.FromDecimal(r.Amount));
        return (given - released).Amount;
    }

    /// <summary>What is left to pay on each charge that has not been reversed and is not yet paid in full, in entry order.</summary>
    public static IReadOnlyList<(int Charge, decimal Open)> OpenCharges(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerAllocation> allocations, IReadOnlyList<LedgerRelease>? releases = null)
    {
        var reversed = Reversed(entries);
        var open = new List<(int, decimal)>();
        foreach (var charge in entries.Where(e => e.Kind == BillingKinds.Charge && !reversed.Contains(e.Number)).OrderBy(e => e.Number))
        {
            var left = (AppMoney.FromDecimal(charge.Amount) - AppMoney.FromDecimal(Paid(entries, allocations, charge.Number, releases))).Amount;
            if (left > 0) open.Add((charge.Number, left));
        }
        return open;
    }

    /// <summary>What a payment still has that is not applied to any charge: zero once it is reversed.</summary>
    public static decimal Unapplied(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerAllocation> allocations, int paymentNumber, IReadOnlyList<LedgerRelease>? releases = null)
    {
        var payment = entries.First(e => e.Number == paymentNumber);
        if (Reversed(entries).Contains(paymentNumber)) return 0m;
        return (-AppMoney.FromDecimal(payment.Amount) - AppMoney.FromDecimal(Refunded(entries, paymentNumber)) - AppMoney.FromDecimal(Applied(allocations, paymentNumber, releases))).Amount;
    }

    /// <summary>The default allocation: apply <paramref name="amount"/> to the oldest open charges first, each for no more than it has left. Whatever is not used stays unapplied.</summary>
    public static IReadOnlyList<(int Charge, decimal Amount)> OldestFirst(IReadOnlyList<(int Charge, decimal Open)> open, decimal amount)
    {
        var left = AppMoney.FromDecimal(amount);
        var result = new List<(int, decimal)>();
        foreach (var (charge, room) in open)
        {
            if (left.Amount <= 0) break;
            var take = left.Amount < room ? left.Amount : room;
            result.Add((charge, take));
            left -= AppMoney.FromDecimal(take);
        }
        return result;
    }

    /// <summary>
    /// Checks that the entries and allocations obey every rule of the ledger, as a person or a script reading the preserved history would: entries numbered one after another; each reversal exactly the opposite of
    /// an earlier entry that is not itself a reversal, and each entry reversed once; the balance never below zero; each allocation from a payment to a charge, no payment applied for more than it holds and no
    /// charge paid for more than it is, and no charge that was reversed still being paid. An empty list means the ledger is consistent.
    /// </summary>
    public static IReadOnlyList<string> Verify(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerAllocation> allocations, IReadOnlyList<LedgerRelease>? releases = null)
    {
        releases ??= [];
        var findings = new List<string>();
        var ordered = entries.OrderBy(e => e.Number).ToList();
        for (var i = 0; i < ordered.Count; i++)
            if (ordered[i].Number != i + 1) { findings.Add($"Entry numbers are not consecutive at entry {ordered[i].Number}."); break; }

        var byNumber = ordered.ToDictionary(e => e.Number);
        var reversalCount = new Dictionary<int, int>();
        foreach (var r in ordered.Where(e => e.Kind == BillingKinds.Reversal))
        {
            if (r.ReversesNumber is not int target || !byNumber.TryGetValue(target, out var undone) || target >= r.Number) { findings.Add($"Reversal {r.Number} does not undo an earlier entry."); continue; }
            if (undone.Kind == BillingKinds.Reversal) findings.Add($"Reversal {r.Number} undoes another reversal.");
            if (AppMoney.FromDecimal(r.Amount) != -AppMoney.FromDecimal(undone.Amount)) findings.Add($"Reversal {r.Number} is not the opposite of entry {target}.");
            reversalCount[target] = reversalCount.GetValueOrDefault(target) + 1;
        }
        foreach (var (target, count) in reversalCount.Where(p => p.Value > 1)) findings.Add($"Entry {target} is reversed {count} times.");

        var running = RunningBalances(ordered);
        for (var i = 0; i < running.Count; i++)
            if (running[i] < 0) { findings.Add($"The balance is below zero after entry {ordered[i].Number}."); break; }

        foreach (var a in allocations)
        {
            if (!byNumber.TryGetValue(a.PaymentNumber, out var p) || p.Kind != BillingKinds.Payment || !byNumber.TryGetValue(a.ChargeNumber, out var c) || c.Kind != BillingKinds.Charge)
                findings.Add($"An allocation of payment {a.PaymentNumber} to charge {a.ChargeNumber} is not from a payment to a charge.");
            if (a.Amount <= 0) findings.Add($"An allocation of payment {a.PaymentNumber} to charge {a.ChargeNumber} is not above zero.");
        }
        var reversed = Reversed(ordered);
        foreach (var p in ordered.Where(e => e.Kind == BillingKinds.Payment))
        {
            if (Applied(allocations, p.Number, releases) > -p.Amount - Refunded(ordered, p.Number)) findings.Add($"Payment {p.Number} is applied for more than it holds.");
            if (Refunded(ordered, p.Number) > -p.Amount) findings.Add($"Payment {p.Number} is refunded for more than it was.");
            if (reversed.Contains(p.Number) && Refunded(ordered, p.Number) > 0) findings.Add($"Payment {p.Number} was reversed and also refunded.");
        }
        foreach (var r in ordered.Where(e => e.Kind == BillingKinds.Refund))
            if (r.RefundsNumber is not int paid || !byNumber.TryGetValue(paid, out var pay) || pay.Kind != BillingKinds.Payment || paid >= r.Number)
                findings.Add($"Refund {r.Number} does not name an earlier payment.");
        foreach (var g in releases.GroupBy(x => (x.PaymentNumber, x.ChargeNumber)))
        {
            var given = allocations.Where(a => (a.PaymentNumber, a.ChargeNumber) == g.Key).Aggregate(AppMoney.Zero, (sum, a) => sum + AppMoney.FromDecimal(a.Amount));
            var back = g.Aggregate(AppMoney.Zero, (sum, x) => sum + AppMoney.FromDecimal(x.Amount));
            if (back.Amount > given.Amount) findings.Add($"Payment {g.Key.PaymentNumber} is released from charge {g.Key.ChargeNumber} for more than was applied.");
        }
        foreach (var g in releases.Where(x => x.RefundNumber is not null).GroupBy(x => x.RefundNumber!.Value))
            if (!byNumber.TryGetValue(g.Key, out var refund) || refund.Kind != BillingKinds.Refund || g.Aggregate(AppMoney.Zero, (sum, x) => sum + AppMoney.FromDecimal(x.Amount)).Amount > refund.Amount)
                findings.Add($"The releases of refund {g.Key} are more than the refund.");
        foreach (var c in ordered.Where(e => e.Kind == BillingKinds.Charge))
        {
            var paid = Paid(ordered, allocations, c.Number, releases);
            if (paid > c.Amount) findings.Add($"Charge {c.Number} is paid for more than it is.");
            if (reversed.Contains(c.Number) && paid > 0) findings.Add($"Charge {c.Number} was reversed but is still being paid.");
        }
        return findings;
    }
}
