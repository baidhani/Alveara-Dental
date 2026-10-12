using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C02: financial corrections that keep the history whole.
///
/// The documented rules:
/// - <b>Moving what a payment pays.</b> The charges to take money off and the charges to put it on are named once each, with positive amounts in dollars and cents, and must add up to exactly the same total.
///   The rows are put in charge order before they are compared, so the same request in any order is the same request. The old applications are released (newest first) and new ones are recorded in one
///   transaction: either both happen or neither does. A payment that has been reversed cannot be moved, nothing can be taken off a charge for more than this payment applied to it, and nothing can be put on a
///   charge for more than it has left once the releases are counted. Moving to the charge it came from is refused as a no-op.
/// - <b>Correcting a payment or adjustment.</b> The entry is reversed exactly and a replacement is recorded that names it, in one save. A replacement payment starts unapplied (apply it afterwards). A charge cannot be
///   corrected this way (it can only be reversed), a reversal or refund cannot be corrected, and a refunded payment cannot be reversed so it cannot be corrected.
/// - Both write the audit entry in the same save, need the account version the person saw, and a repeat of the same request under the same key changes nothing.
/// </summary>
public partial class BillingService
{
    private readonly record struct Row(int Index, int Charge, decimal Amount);

    /// <summary>The rows of one list, checked and put in charge order: each names a charge once, for more than zero, in dollars and cents.</summary>
    private static List<Row> NormalizeRows(IDictionary<string, string> errors, string field, IReadOnlyList<AllocationInput>? rows)
    {
        var result = new List<Row>();
        for (var i = 0; rows is not null && i < rows.Count; i++)
        {
            var r = rows[i];
            var name = $"{field}[{i}]";
            if (r.ChargeEntryNumber is not int charge) { errors[name] = "Choose one of this account's charges."; continue; }
            if (r.Amount is null || r.Amount <= 0) { errors[name] = "Enter an amount above zero for this charge."; continue; }
            if (Math.Round(r.Amount.Value, 2) != r.Amount) { errors[name] = "Enter the amount in dollars and cents (no more than two decimal places)."; continue; }
            if (r.Amount > BillingLimits.AmountMax) { errors[name] = $"The amount cannot be more than {BillingLimits.AmountMax:F2}."; continue; }
            if (result.Any(x => x.Charge == charge)) { errors[name] = $"Charge {charge} is named more than once; name it once with the total."; continue; }
            result.Add(new Row(i, charge, r.Amount.Value));
        }
        return result.OrderBy(r => r.Charge).ToList();
    }

    private static decimal Total(IEnumerable<Row> rows) => rows.Aggregate(AppMoney.Zero, (sum, r) => sum + AppMoney.FromDecimal(r.Amount)).Amount;

    public async Task<BillingAccountView> MoveAllocationsAsync(Guid patientId, MoveAllocationInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        var reason = BillingRules.CleanText(errors, "reason", input.Reason, required: true, missing: "A move needs a reason, so the history explains it.");
        if (input.PaymentEntryNumber is null) errors["paymentEntryNumber"] = "Choose the payment whose applications are moved.";
        var releases = NormalizeRows(errors, "releases", input.Releases);
        var targets = NormalizeRows(errors, "allocations", input.Allocations);
        if (errors.Count == 0)
        {
            if (releases.Count == 0) errors["releases"] = "Choose the charges to take money off.";
            else if (targets.Count == 0) errors["allocations"] = "Choose the charges to put the money on.";
            else if (Total(releases) != Total(targets)) errors["allocations"] = $"The money taken off ({Total(releases):F2} {AppMoney.Currency}) and the money put on ({Total(targets):F2} {AppMoney.Currency}) must be exactly the same.";
            else if (releases.Any(r => targets.Any(t => t.Charge == r.Charge))) errors["allocations"] = "A charge cannot be both taken off and put on; leave it out of one of the lists.";
        }
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var version = BillingRules.ParseVersion(input.RowVersion);

        var account = await AccountForChangeAsync(patientId, ct);
        var ledger = await LoadLedgerAsync(account.Id, ct);
        var number = input.PaymentEntryNumber!.Value;
        if (ledger.ByNumber(number) is not { Kind: BillingKinds.Payment } payment) throw new BillingException("payment_not_found", "That payment was not found on this account.", 404);
        if (ledger.Entries.Any(e => e.IdempotencyKey == key)) throw KeyReused();

        var priorReleases = ledger.Releases.Where(r => r.IdempotencyKey == key).ToList();
        var priorAllocations = ledger.Allocations.Where(a => a.IdempotencyKey == key).ToList();
        if (priorReleases.Count > 0 || priorAllocations.Count > 0)
        {
            var allocationsById = ledger.Allocations.ToDictionary(a => a.Id);
            static Dictionary<int, decimal> Sum(IEnumerable<(int Charge, decimal Amount)> rows) => rows.GroupBy(r => r.Charge).ToDictionary(g => g.Key, g => AppMoney.FromDecimal(g.Sum(r => r.Amount)).Amount);
            var sameRequest = priorReleases.All(r => r.Cause == ReleaseCauses.Correction && r.Reason == reason && allocationsById[r.AllocationId].PaymentEntryId == payment.Id)
                && priorAllocations.All(a => a.PaymentEntryId == payment.Id)
                && Sum(priorReleases.Select(r => (ledger.NumberOf(allocationsById[r.AllocationId].ChargeEntryId), r.Amount))).OrderBy(p => p.Key).SequenceEqual(Sum(releases.Select(r => (r.Charge, r.Amount))).OrderBy(p => p.Key))
                && Sum(priorAllocations.Select(a => (ledger.NumberOf(a.ChargeEntryId), a.Amount))).OrderBy(p => p.Key).SequenceEqual(Sum(targets.Select(t => (t.Charge, t.Amount))).OrderBy(p => p.Key));
            return sameRequest ? await GetAccountAsync(patientId, ct) : throw KeyReused();
        }

        var math = ledger.Math;
        if (BillingLedgerMath.Reversed(math).Contains(number)) throw new BillingException("payment_reversed", $"Payment {number} has been reversed, so what it paid cannot be moved.", 409);
        foreach (var r in releases)
        {
            var held = ledger.Allocations.Where(a => a.PaymentEntryId == payment.Id && ledger.ByNumber(r.Charge) is { Kind: BillingKinds.Charge } c && a.ChargeEntryId == c.Id)
                .Aggregate(AppMoney.Zero, (sum, a) => sum + AppMoney.FromDecimal(ledger.Remaining(a))).Amount;
            if (held <= 0) errors[$"releases[{r.Index}]"] = $"Payment {number} does not pay charge {r.Charge}, so nothing can be taken off it.";
            else if (r.Amount > held) errors[$"releases[{r.Index}]"] = $"Payment {number} pays only {held:F2} {AppMoney.Currency} of charge {r.Charge}.";
        }
        var simulated = ledger.MathReleases.Concat(releases.Select(r => new LedgerRelease(number, r.Charge, r.Amount))).ToList();
        var open = BillingLedgerMath.OpenCharges(math, ledger.MathAllocations, simulated).ToDictionary(o => o.Charge, o => o.Open);
        foreach (var t in targets)
        {
            if (ledger.ByNumber(t.Charge) is not { Kind: BillingKinds.Charge }) errors[$"allocations[{t.Index}]"] = "Choose one of this account's charges.";
            else if (!open.TryGetValue(t.Charge, out var room)) errors[$"allocations[{t.Index}]"] = $"Charge {t.Charge} has nothing left to pay (it is paid in full or has been reversed).";
            else if (t.Amount > room) errors[$"allocations[{t.Index}]"] = $"Charge {t.Charge} has only {room:F2} {AppMoney.Currency} left to pay.";
        }
        if (errors.Count > 0) throw BillingRules.Invalid(errors);

        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);          // the releases and the new applications are saved together or not at all
        Touch(account, version, now);
        var nextRelease = ledger.NextReleaseNumber;
        foreach (var r in releases) StageReleases(account, ledger, payment, ledger.ByNumber(r.Charge), r.Amount, ReleaseCauses.Correction, null, reason, key!, actor, now, ref nextRelease);
        await SaveAsync(account.Id, ct);                                                      // releases first: the database counts them when it checks the new applications
        AddAllocations(account, ledger, payment, targets.Select(t => (ledger.ByNumber(t.Charge)!, t.Amount)).ToList(), key!, actor, now);
        AuditService.Record(db, "BillingAllocationsMoved", nameof(BillingAccount), account.Id, actor,
            $"{Total(targets):F2} {AppMoney.Currency} of payment {number} moved from {releases.Count} charge{(releases.Count == 1 ? "" : "s")} to {targets.Count}.", reason);
        await SaveAsync(account.Id, ct);
        await transaction.CommitAsync(ct);
        return await GetAccountAsync(patientId, ct);
    }

    public async Task<BillingAccountView> CorrectEntryAsync(Guid patientId, CorrectionInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        if (key is not null && key.Length > BillingLimits.KeyMax - 2) errors["idempotencyKey"] = $"For a correction the save key must be {BillingLimits.KeyMax - 2} characters or fewer.";
        var reason = BillingRules.CleanText(errors, "reason", input.Reason, required: true, missing: "A correction needs a reason, so the history explains it.");
        if (input.EntryNumber is null) errors["entryNumber"] = "Choose the entry to correct.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);

        var account = await AccountForChangeAsync(patientId, ct);
        var ledger = await LoadLedgerAsync(account.Id, ct);
        var target = ledger.ByNumber(input.EntryNumber!.Value) ?? throw new BillingException("entry_not_found", "That entry was not found on this account.", 404);
        if (target.Kind == BillingKinds.Charge) throw new BillingException("charge_not_correctable", "A charge cannot be replaced; it can only be reversed.", 409);
        if (target.Kind is not (BillingKinds.Payment or BillingKinds.Adjustment)) throw new BillingException("entry_not_correctable", $"A {target.Kind.ToLowerInvariant()} cannot be corrected.", 409);

        BillingPaymentMethod? method = null;
        decimal? amount = input.Amount;
        var note = BillingRules.CleanText(errors, "note", input.Note, required: false, missing: "");
        Guid? paidBy = null;
        if (target.Kind == BillingKinds.Payment)
        {
            amount = BillingRules.CleanPositiveAmount(errors, input.Amount, "payment");
            method = BillingRules.CleanMethod(errors, input.Method, await ActiveMethodsAsync(ct));
            paidBy = input.PaidByPatientId is Guid payer && payer != patientId ? payer : null;
            if (paidBy is Guid other && !await db.BillingResponsibilities.AsNoTracking().AnyAsync(r => r.AccountId == account.Id && r.ResponsiblePatientId == other, ct))
                errors["paidBy"] = "Only the patient, or someone recorded as responsible for this account, can be recorded as having paid it.";
        }
        else
        {
            if (amount is null) errors["amount"] = "Enter the replacement adjustment amount (negative to reduce what is owed).";
            else if (amount == 0) errors["amount"] = "An adjustment of zero changes nothing.";
            else if (Math.Round(amount.Value, 2) != amount) errors["amount"] = "Enter the amount in dollars and cents (no more than two decimal places).";
            else if (Math.Abs(amount.Value) > BillingLimits.AmountMax) errors["amount"] = $"An adjustment cannot be more than {BillingLimits.AmountMax:F2}.";
            note ??= reason;
        }
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var version = BillingRules.ParseVersion(input.RowVersion);

        var replacementKey = $"{key}~2";
        var signed = target.Kind == BillingKinds.Payment ? -amount!.Value : amount!.Value;
        var reversal = ledger.Entries.FirstOrDefault(e => e.IdempotencyKey == key);
        var replacement = ledger.Entries.FirstOrDefault(e => e.IdempotencyKey == replacementKey);
        if (reversal is not null || replacement is not null)
            return reversal is { Kind: BillingKinds.Reversal } && reversal.ReversesEntryId == target.Id && reversal.Reason == reason
                   && replacement is not null && replacement.ReplacesEntryId == target.Id && replacement.Kind == target.Kind && replacement.Amount == signed && replacement.Reason == note && replacement.PaymentMethodId == method?.Id
                   && replacement.PaidByPatientId == paidBy
                ? await GetAccountAsync(patientId, ct) : throw KeyReused();
        if (ledger.Allocations.Any(a => a.IdempotencyKey == key || a.IdempotencyKey == replacementKey) || ledger.Releases.Any(r => r.IdempotencyKey == key || r.IdempotencyKey == replacementKey)) throw KeyReused();

        var math = ledger.Math;
        if (BillingLedgerMath.Reversed(math).Contains(target.EntryNumber)) throw new BillingException("already_reversed", $"Entry {target.EntryNumber} has already been reversed.", 409);
        if (target.Kind == BillingKinds.Payment && BillingLedgerMath.Refunded(math, target.EntryNumber) > 0)
            throw new BillingException("payment_refunded", $"Payment {target.EntryNumber} has been refunded, so it cannot be corrected.", 409);

        var afterReversal = (AppMoney.FromDecimal(BillingLedgerMath.Balance(math)) - AppMoney.FromDecimal(target.Amount)).Amount;
        if (afterReversal < 0)
            throw BillingRules.Invalid(new Dictionary<string, string> { ["entryNumber"] = $"Reversing entry {target.EntryNumber} would leave the account owing less than nothing ({afterReversal:F2} {AppMoney.Currency}), so it cannot be corrected." });
        var afterReplacement = (AppMoney.FromDecimal(afterReversal) + AppMoney.FromDecimal(signed)).Amount;
        if (afterReplacement < 0)
            throw BillingRules.Invalid(new Dictionary<string, string> { ["amount"] = target.Kind == BillingKinds.Payment
                ? $"A payment cannot be more than the balance owed ({afterReversal:F2} {AppMoney.Currency}) once the entry it replaces is reversed."
                : $"An adjustment cannot take the balance below zero (balance owed after the reversal {afterReversal:F2} {AppMoney.Currency})." });

        var now = clock.UtcNow;
        Touch(account, version, now);
        var number = await NextNumberAsync(account.Id, ct);
        db.BillingEntries.Add(new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = number, Kind = BillingKinds.Reversal, Amount = -target.Amount, ReversesEntryId = target.Id, Reason = reason,
            IdempotencyKey = key!, RecordedAtUtc = now, RecordedByUserId = actor,
        });
        db.BillingEntries.Add(new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = number + 1, Kind = target.Kind, Amount = signed, ReplacesEntryId = target.Id, Reason = note,
            PaymentMethod = method?.Kind, PaymentMethodId = method?.Id, PaidByPatientId = paidBy, IdempotencyKey = replacementKey, RecordedAtUtc = now, RecordedByUserId = actor,
        });
        AuditService.Record(db, "BillingEntryCorrected", nameof(BillingAccount), account.Id, actor,
            $"{target.Kind} {target.EntryNumber} of {target.Amount:F2} {AppMoney.Currency} reversed and replaced by one of {signed:F2} {AppMoney.Currency}.", reason);
        await SaveAsync(account.Id, ct);
        return await GetAccountAsync(patientId, ct);
    }
}
