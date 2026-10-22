using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C01: allocating payments to charges, reversing entries, and checking that the preserved history adds up. All of it is derived from the entries and allocations through <see cref="BillingLedgerMath"/>;
/// nothing here edits or removes anything already recorded.
///
/// The documented rules:
/// - A payment is applied to the charges it names, or to the oldest open charges first when it names none. It is never applied to a charge for more than that charge has left, and never for more than it holds.
///   What is not applied (a payment larger than the open charges, because an adjustment raised the balance, or one that named only some) stays "unapplied" and can be applied later.
/// - Reversing a payment releases its allocations (they stop counting); reversing a charge is refused while payments are applied to it; a reversal can be reversed by nobody; an entry is reversed once; and a reversal
///   cannot leave the account owing less than nothing.
/// </summary>
public partial class BillingService
{
    /// <summary>An account's entries and allocations as saved, with the lookups the rules need.</summary>
    private sealed class Ledger(List<BillingEntry> entries, List<BillingAllocation> allocations, List<BillingAllocationRelease> releases)
    {
        public List<BillingEntry> Entries { get; } = entries.OrderBy(e => e.EntryNumber).ToList();
        public List<BillingAllocation> Allocations { get; } = allocations;
        public List<BillingAllocationRelease> Releases { get; } = releases;
        private readonly Dictionary<Guid, int> _numberOf = entries.ToDictionary(e => e.Id, e => e.EntryNumber);

        public IReadOnlyList<LedgerEntry> Math => Entries.Select(e => new LedgerEntry(e.EntryNumber, e.Kind, e.Amount, e.ReversesEntryId is Guid id ? _numberOf[id] : null, e.RefundsEntryId is Guid refunded ? _numberOf[refunded] : null)).ToList();
        public IReadOnlyList<LedgerAllocation> MathAllocations => Allocations.Select(a => new LedgerAllocation(_numberOf[a.PaymentEntryId], _numberOf[a.ChargeEntryId], a.Amount)).ToList();
        public IReadOnlyList<LedgerRelease> MathReleases
        {
            get
            {
                var byId = Allocations.ToDictionary(a => a.Id);
                return Releases.Select(r => new LedgerRelease(_numberOf[byId[r.AllocationId].PaymentEntryId], _numberOf[byId[r.AllocationId].ChargeEntryId], r.Amount, r.RefundEntryId is Guid f ? _numberOf[f] : null)).ToList();
            }
        }
        /// <summary>What an allocation still holds after the releases made of it.</summary>
        public decimal Remaining(BillingAllocation allocation) =>
            (AppMoney.FromDecimal(allocation.Amount) - Releases.Where(r => r.AllocationId == allocation.Id).Aggregate(AppMoney.Zero, (sum, r) => sum + AppMoney.FromDecimal(r.Amount))).Amount;
        public int NextReleaseNumber => (Releases.Select(r => (int?)r.ReleaseNumber).Max() ?? 0) + 1;
        public BillingEntry? ByNumber(int number) => Entries.FirstOrDefault(e => e.EntryNumber == number);
        public int NumberOf(Guid entryId) => _numberOf[entryId];
        public int NextAllocationNumber => (Allocations.Select(a => (int?)a.AllocationNumber).Max() ?? 0) + 1;
    }

    /// <summary>How many times a ledger read is repeated when someone records on the account while it is being read, before the reader is told to reload (<see cref="ConcurrencyConflictException"/>).</summary>
    private const int LedgerReadAttempts = 4;

    /// <summary>
    /// ALV-007-C01 (correction): the entries, allocations and releases are three queries, and each query sees what is committed at the moment it runs, so a payment recorded by someone else between two of them can show
    /// an allocation whose entry the first query did not return. Every recorded change moves the account's row version in the same transaction, so the three reads form one coherent picture exactly when the version is
    /// the same before and after them; when it moved, the read is repeated (at most <see cref="LedgerReadAttempts"/> times) and then refused as a conflict, the same classified outcome as any other stale read.
    /// Failure modes handled: a concurrent recording (retried, then a conflict). Not handled: a database that is down (the exception goes to the caller unchanged).
    /// </summary>
    private async Task<Ledger> LoadLedgerAsync(Guid accountId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var before = await AccountVersionAsync(accountId, ct);
            var entries = await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == accountId).ToListAsync(ct);
            var allocations = await db.BillingAllocations.AsNoTracking().Where(a => a.AccountId == accountId).ToListAsync(ct);
            var releases = await db.BillingAllocationReleases.AsNoTracking().Where(r => r.AccountId == accountId).ToListAsync(ct);
            var after = await AccountVersionAsync(accountId, ct);
            if ((before is null && after is null) || (before is not null && after is not null && before.AsSpan().SequenceEqual(after))) return new Ledger(entries, allocations, releases);
            logger?.LogWarning("billing_ledger_read_changed error_class={ErrorClass} attempt={Attempt}", nameof(ConcurrencyConflictException), attempt);
            if (attempt >= LedgerReadAttempts) throw new ConcurrencyConflictException(nameof(BillingAccount), accountId);
        }
    }

    private Task<byte[]?> AccountVersionAsync(Guid accountId, CancellationToken ct) =>
        db.BillingAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.RowVersion).FirstOrDefaultAsync(ct)!;

    private Task<List<BillingPaymentMethod>> ActiveMethodsAsync(CancellationToken ct) =>
        db.BillingPaymentMethods.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Kind == m.Name ? 0 : 1).ThenBy(m => m.Name).ToListAsync(ct);

    /// <summary>
    /// The allocations for <paramref name="available"/> money: the charges the caller named (each must be an open charge of this account, named once, for a whole number of cents and no more than it has left, and
    /// all together no more than is available), or the oldest open charges first when none were named. Problems are added to <paramref name="errors"/>, one message per allocation.
    /// </summary>
    private static List<(BillingEntry Charge, decimal Amount)> BuildAllocations(Ledger ledger, decimal available, IReadOnlyList<AllocationInput>? requested, IDictionary<string, string> errors)
    {
        var math = ledger.Math;
        var open = BillingLedgerMath.OpenCharges(math, ledger.MathAllocations, ledger.MathReleases).ToDictionary(o => o.Charge, o => o.Open);
        if (requested is null || requested.Count == 0)
            return BillingLedgerMath.OldestFirst(open.Select(o => (o.Key, o.Value)).OrderBy(o => o.Key).ToList(), available).Select(a => (ledger.ByNumber(a.Charge)!, a.Amount)).ToList();

        var chosen = new List<(BillingEntry Charge, decimal Amount)>();
        var total = AppMoney.Zero;
        for (var i = 0; i < requested.Count; i++)
        {
            var r = requested[i];
            var field = $"allocations[{i}]";
            if (r.ChargeEntryNumber is not int number || ledger.ByNumber(number) is not { Kind: BillingKinds.Charge } charge) { errors[field] = "Choose one of this account's charges to apply the payment to."; continue; }
            if (!open.TryGetValue(number, out var room)) { errors[field] = $"Charge {number} has nothing left to pay (it is paid in full or has been reversed)."; continue; }
            if (chosen.Any(c => c.Charge.EntryNumber == number)) { errors[field] = $"Charge {number} is named more than once; name it once with the total."; continue; }
            var amount = r.Amount;
            if (amount is null || amount <= 0) { errors[field] = "Enter an amount above zero for this charge."; continue; }
            if (Math.Round(amount.Value, 2) != amount) { errors[field] = "Enter the amount in dollars and cents (no more than two decimal places)."; continue; }
            if (amount > room) { errors[field] = $"Charge {number} has only {room:F2} {AppMoney.Currency} left to pay."; continue; }
            chosen.Add((charge, amount.Value));
            total += AppMoney.FromDecimal(amount.Value);
        }
        if (errors.Count == 0 && total.Amount > available) errors["allocations"] = $"The payment holds {available:F2} {AppMoney.Currency}, so it cannot be applied for {total.Amount:F2}.";
        return chosen;
    }

    private void AddAllocations(BillingAccount account, Ledger ledger, BillingEntry payment, IEnumerable<(BillingEntry Charge, decimal Amount)> chosen, string key, Guid actor, DateTimeOffset now)
    {
        var number = ledger.NextAllocationNumber;
        foreach (var (charge, amount) in chosen)
            db.BillingAllocations.Add(new BillingAllocation
            {
                Id = Guid.NewGuid(), AccountId = account.Id, PatientId = account.PatientId, AllocationNumber = number++, PaymentEntryId = payment.Id, ChargeEntryId = charge.Id, Amount = amount,
                IdempotencyKey = key, AllocatedAtUtc = now, AllocatedByUserId = actor,
            });
    }

    /// <summary>Applies what is left of an earlier payment to charges: the ones named, or the oldest open ones first when none are.</summary>
    public async Task<BillingAccountView> ApplyPaymentAsync(Guid patientId, ApplyPaymentInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        if (input.PaymentEntryNumber is null) errors["paymentEntryNumber"] = "Choose the payment to apply.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var version = BillingRules.ParseVersion(input.RowVersion);

        var account = await AccountForChangeAsync(patientId, ct);
        var ledger = await LoadLedgerAsync(account.Id, ct);
        if (ledger.Entries.Any(e => e.IdempotencyKey == key)) throw KeyReused();                                  // that key made an entry; it cannot also be an application
        var number = input.PaymentEntryNumber!.Value;
        var payment = ledger.ByNumber(number);
        var earlier = ledger.Allocations.Where(a => a.IdempotencyKey == key).ToList();
        if (earlier.Count > 0) return payment is { Kind: BillingKinds.Payment } && earlier.All(a => a.PaymentEntryId == payment.Id) ? await GetAccountAsync(patientId, ct) : throw KeyReused();   // the same application again is quiet
        if (payment is not { Kind: BillingKinds.Payment }) throw new BillingException("payment_not_found", "That payment was not found on this account.", 404);

        var math = ledger.Math;
        if (BillingLedgerMath.Reversed(math).Contains(number)) throw new BillingException("payment_reversed", $"Payment {number} has been reversed, so it cannot be applied.", 409);
        var left = BillingLedgerMath.Unapplied(math, ledger.MathAllocations, number, ledger.MathReleases);
        if (left <= 0) throw new BillingException("nothing_to_apply", $"Payment {number} is already applied in full.", 409);

        var chosen = BuildAllocations(ledger, left, input.Allocations, errors);
        if (errors.Count == 0 && chosen.Count == 0) errors["allocations"] = "There is no open charge to apply this payment to.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);

        var now = clock.UtcNow;
        Touch(account, version, now);
        AddAllocations(account, ledger, payment, chosen, key!, actor, now);
        var applied = chosen.Aggregate(AppMoney.Zero, (sum, c) => sum + AppMoney.FromDecimal(c.Amount)).Amount;
        AuditService.Record(db, "BillingPaymentApplied", nameof(BillingAccount), account.Id, actor, $"{applied:F2} {AppMoney.Currency} of payment {number} applied to {chosen.Count} charge{(chosen.Count == 1 ? "" : "s")}.");
        await SaveAsync(account.Id, ct);
        return await GetAccountAsync(patientId, ct);
    }

    /// <summary>
    /// Undoes one earlier entry with a new entry that is exactly its opposite and says why. A payment's allocations are released; a charge that payments are applied to is refused until they are reversed; a reversal
    /// cannot be reversed; an entry is reversed once; and the account may not be left owing less than nothing.
    /// </summary>
    public async Task<BillingAccountView> ReverseEntryAsync(Guid patientId, ReversalInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        var reason = BillingRules.CleanText(errors, "reason", input.Reason, required: true, missing: "A reversal needs a reason, so the history explains it.");
        if (input.EntryNumber is null) errors["entryNumber"] = "Choose the entry to reverse.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var version = BillingRules.ParseVersion(input.RowVersion);

        var account = await AccountForChangeAsync(patientId, ct);
        var ledger = await LoadLedgerAsync(account.Id, ct);
        var target = ledger.ByNumber(input.EntryNumber!.Value) ?? throw new BillingException("entry_not_found", "That entry was not found on this account.", 404);
        var same = ledger.Entries.FirstOrDefault(e => e.IdempotencyKey == key);
        if (same is not null) return same.Kind == BillingKinds.Reversal && same.ReversesEntryId == target.Id && same.Reason == reason ? await GetAccountAsync(patientId, ct) : throw KeyReused();

        var math = ledger.Math;
        if (target.Kind == BillingKinds.Refund) throw new BillingException("cannot_reverse_a_refund", "A refund cannot be reversed; the money has been handed back. Record a new payment if it was returned.", 409);
        if (target.Kind == BillingKinds.Payment && BillingLedgerMath.Refunded(math, target.EntryNumber) > 0)
            throw new BillingException("payment_refunded", $"Payment {target.EntryNumber} has been refunded, so it cannot be reversed.", 409);
        if (target.Kind == BillingKinds.Reversal) throw new BillingException("cannot_reverse_a_reversal", "A reversal cannot itself be reversed; record a new charge, payment or adjustment instead.", 409);
        if (BillingLedgerMath.Reversed(math).Contains(target.EntryNumber)) throw new BillingException("already_reversed", $"Entry {target.EntryNumber} has already been reversed.", 409);
        if (target.Kind == BillingKinds.Charge && BillingLedgerMath.Paid(math, ledger.MathAllocations, target.EntryNumber, ledger.MathReleases) > 0)
            throw new BillingException("charge_has_payments", $"Charge {target.EntryNumber} has payments applied to it. Reverse those payments first, then reverse the charge.", 409);

        var after = (AppMoney.FromDecimal(BillingLedgerMath.Balance(math)) - AppMoney.FromDecimal(target.Amount)).Amount;
        if (after < 0)
            throw BillingRules.Invalid(new Dictionary<string, string> { ["entryNumber"] = $"Reversing entry {target.EntryNumber} would leave the account owing less than nothing ({after:F2} {AppMoney.Currency}). Reverse the payments or adjustments that depend on it first." });

        var now = clock.UtcNow;
        Touch(account, version, now);
        db.BillingEntries.Add(new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = await NextNumberAsync(account.Id, ct), Kind = BillingKinds.Reversal, Amount = -target.Amount, ReversesEntryId = target.Id,
            Reason = reason, IdempotencyKey = key!, RecordedAtUtc = now, RecordedByUserId = actor,
        });
        AuditService.Record(db, "BillingEntryReversed", nameof(BillingAccount), account.Id, actor, $"{target.Kind} {target.EntryNumber} of {target.Amount:F2} {AppMoney.Currency} reversed.", reason);
        await SaveAsync(account.Id, ct);
        return await GetAccountAsync(patientId, ct);
    }

    /// <summary>
    /// Rebuilds the balance and every derived number from the preserved entries and allocations and reports anything that does not obey the ledger's rules, plus any difference between the rebuilt balance and
    /// the database's own sum of the entries. A consistent ledger has an empty list of findings.
    /// </summary>
    public async Task<LedgerVerification> VerifyLedgerAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        if (account is null) return new LedgerVerification(patientId, true, [], 0m, 0, 0);
        var ledger = await LoadLedgerAsync(account.Id, ct);
        var findings = BillingLedgerMath.Verify(ledger.Math, ledger.MathAllocations, ledger.MathReleases).ToList();
        var rebuilt = BillingLedgerMath.Balance(ledger.Math);
        var stored = AppMoney.FromDecimal(await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == account.Id).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m).Amount;
        if (stored != rebuilt) findings.Add($"The rebuilt balance {rebuilt:F2} differs from the sum of the entries {stored:F2}.");
        return new LedgerVerification(patientId, findings.Count == 0, findings, rebuilt, ledger.Entries.Count, ledger.Allocations.Count);
    }
}
