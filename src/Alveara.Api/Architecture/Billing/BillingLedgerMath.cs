using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>One ledger entry as the math sees it: its number, kind, signed amount and, for a reversal, the number of the entry it undoes.</summary>
public sealed record LedgerEntry(int Number, string Kind, decimal Amount, int? ReversesNumber);

/// <summary>How much of one payment pays one charge, by entry number.</summary>
public sealed record LedgerAllocation(int PaymentNumber, int ChargeNumber, decimal Amount);

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
    public static decimal Applied(IEnumerable<LedgerAllocation> allocations, int paymentNumber) =>
        allocations.Where(a => a.PaymentNumber == paymentNumber).Aggregate(AppMoney.Zero, (sum, a) => sum + AppMoney.FromDecimal(a.Amount)).Amount;

    /// <summary>What a charge has been paid so far: the allocations whose payment has not been reversed.</summary>
    public static decimal Paid(IEnumerable<LedgerEntry> entries, IEnumerable<LedgerAllocation> allocations, int chargeNumber)
    {
        var reversed = Reversed(entries);
        return allocations.Where(a => a.ChargeNumber == chargeNumber && !reversed.Contains(a.PaymentNumber)).Aggregate(AppMoney.Zero, (sum, a) => sum + AppMoney.FromDecimal(a.Amount)).Amount;
    }

    /// <summary>What is left to pay on each charge that has not been reversed and is not yet paid in full, in entry order.</summary>
    public static IReadOnlyList<(int Charge, decimal Open)> OpenCharges(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerAllocation> allocations)
    {
        var reversed = Reversed(entries);
        var open = new List<(int, decimal)>();
        foreach (var charge in entries.Where(e => e.Kind == BillingKinds.Charge && !reversed.Contains(e.Number)).OrderBy(e => e.Number))
        {
            var left = (AppMoney.FromDecimal(charge.Amount) - AppMoney.FromDecimal(Paid(entries, allocations, charge.Number))).Amount;
            if (left > 0) open.Add((charge.Number, left));
        }
        return open;
    }

    /// <summary>What a payment still has that is not applied to any charge: zero once it is reversed.</summary>
    public static decimal Unapplied(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerAllocation> allocations, int paymentNumber)
    {
        var payment = entries.First(e => e.Number == paymentNumber);
        if (Reversed(entries).Contains(paymentNumber)) return 0m;
        return (-AppMoney.FromDecimal(payment.Amount) - AppMoney.FromDecimal(Applied(allocations, paymentNumber))).Amount;
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
    public static IReadOnlyList<string> Verify(IReadOnlyList<LedgerEntry> entries, IReadOnlyList<LedgerAllocation> allocations)
    {
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
            if (Applied(allocations, p.Number) > -p.Amount) findings.Add($"Payment {p.Number} is applied for more than it holds.");
        foreach (var c in ordered.Where(e => e.Kind == BillingKinds.Charge))
        {
            var paid = Paid(ordered, allocations, c.Number);
            if (paid > c.Amount) findings.Add($"Charge {c.Number} is paid for more than it is.");
            if (reversed.Contains(c.Number) && paid > 0) findings.Add($"Charge {c.Number} was reversed but is still being paid.");
        }
        return findings;
    }
}
