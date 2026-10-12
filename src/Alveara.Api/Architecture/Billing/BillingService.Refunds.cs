using Alveara.Api.Architecture.Auditing;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C02: refunds. A refund is a new ledger entry (it adds to the balance owed) that names the payment it hands back, plus append-only release rows that take the refunded money off the payment's applications.
///
/// The documented rules:
/// - A refund is for more than zero and no more than the payment still has (the payment less earlier refunds); a reversed payment cannot be refunded and a refunded payment cannot be reversed.
/// - It needs a reason and one of the active payment methods as the way it is returned. It goes back to whoever the payment records as having paid it; there is no way to name anyone else, and a refund never
///   creates credit (the balance only goes up).
/// - The money handed back comes first from what the payment has not applied to any charge, then from its applications, newest first. Each application taken from is released by the amount taken, so the charge
///   it paid is open again. The database refuses a refund beyond its payment and a release beyond its application; the order (unapplied first, newest first) is this service's rule and is checked by
///   <see cref="BillingLedgerMath.Verify"/>.
/// - Entry, releases, the account version and the audit entry are saved together or not at all; the same save key again changes nothing.
/// </summary>
public partial class BillingService
{
    public async Task<BillingAccountView> RefundPaymentAsync(Guid patientId, RefundInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        var amount = BillingRules.CleanPositiveAmount(errors, input.Amount, "refund");
        var method = BillingRules.CleanMethod(errors, input.Method, await ActiveMethodsAsync(ct));
        var reason = BillingRules.CleanText(errors, "reason", input.Reason, required: true, missing: "A refund needs a reason, so the history explains it.");
        if (input.PaymentEntryNumber is null) errors["paymentEntryNumber"] = "Choose the payment to refund.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var version = BillingRules.ParseVersion(input.RowVersion);

        var account = await AccountForChangeAsync(patientId, ct);
        var ledger = await LoadLedgerAsync(account.Id, ct);
        var number = input.PaymentEntryNumber!.Value;
        var payment = ledger.ByNumber(number);
        var same = ledger.Entries.FirstOrDefault(e => e.IdempotencyKey == key);
        if (same is not null)
            return payment is { Kind: BillingKinds.Payment } && same.Kind == BillingKinds.Refund && same.RefundsEntryId == payment.Id && same.Amount == amount && same.PaymentMethodId == method!.Id && same.Reason == reason
                ? await GetAccountAsync(patientId, ct) : throw KeyReused();
        if (ledger.Allocations.Any(a => a.IdempotencyKey == key) || ledger.Releases.Any(r => r.IdempotencyKey == key)) throw KeyReused();
        if (payment is not { Kind: BillingKinds.Payment }) throw new BillingException("payment_not_found", "That payment was not found on this account.", 404);

        var math = ledger.Math;
        if (BillingLedgerMath.Reversed(math).Contains(number)) throw new BillingException("payment_reversed", $"Payment {number} has been reversed, so it cannot be refunded.", 409);
        var refundable = BillingLedgerMath.Refundable(math, number);
        if (amount > refundable)
            throw BillingRules.Invalid(new Dictionary<string, string> { ["amount"] = refundable <= 0 ? $"Payment {number} has already been refunded in full." : $"Payment {number} can be refunded for {refundable:F2} {AppMoney.Currency} at most." });

        var unapplied = BillingLedgerMath.Unapplied(math, ledger.MathAllocations, number, ledger.MathReleases);
        var fromApplications = (AppMoney.FromDecimal(amount!.Value) - AppMoney.FromDecimal(Math.Min(amount.Value, unapplied))).Amount;

        var now = clock.UtcNow;
        Touch(account, version, now);
        var refund = new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = await NextNumberAsync(account.Id, ct), Kind = BillingKinds.Refund, Amount = amount.Value, PaymentMethod = method!.Kind,
            PaymentMethodId = method.Id, RefundsEntryId = payment.Id, Reason = reason, IdempotencyKey = key!, RecordedAtUtc = now, RecordedByUserId = actor,
        };
        db.BillingEntries.Add(refund);
        var nextRelease = ledger.NextReleaseNumber;
        var released = StageReleases(account, ledger, payment, null, fromApplications, ReleaseCauses.Refund, refund.Id, null, key!, actor, now, ref nextRelease);
        AuditService.Record(db, "BillingPaymentRefunded", nameof(BillingAccount), account.Id, actor,
            $"Refund of {amount:F2} {AppMoney.Currency} recorded for payment {number} ({method.Name}) to the original payer; {released:F2} released from {(released == 0 ? "no charge" : "its applications")}.", reason);
        await SaveAsync(account.Id, ct);
        return await GetAccountAsync(patientId, ct);
    }

    /// <summary>
    /// Stages releases totalling <paramref name="amount"/> from a payment's applications (those to one charge when <paramref name="charge"/> is given), newest application first, each for no more than it still holds. Returns what was staged. The caller has already
    /// checked that the payment's applications hold at least that much.
    /// </summary>
    private decimal StageReleases(BillingAccount account, Ledger ledger, BillingEntry payment, BillingEntry? charge, decimal amount, string cause, Guid? refundId, string? reason, string key, Guid actor, DateTimeOffset now, ref int number)
    {
        var left = AppMoney.FromDecimal(amount);
        foreach (var allocation in ledger.Allocations.Where(a => a.PaymentEntryId == payment.Id && (charge is null || a.ChargeEntryId == charge.Id)).OrderByDescending(a => a.AllocationNumber).ThenByDescending(a => a.AllocatedAtUtc))
        {
            if (left.Amount <= 0) break;
            var room = ledger.Remaining(allocation);
            if (room <= 0) continue;
            var take = Math.Min(room, left.Amount);
            AddRelease(account, allocation, number++, take, cause, refundId, reason, key, actor, now);
            left -= AppMoney.FromDecimal(take);
        }
        return amount - left.Amount;
    }

    private void AddRelease(BillingAccount account, BillingAllocation allocation, int releaseNumber, decimal amount, string cause, Guid? refundId, string? reason, string key, Guid actor, DateTimeOffset now) =>
        db.BillingAllocationReleases.Add(new BillingAllocationRelease
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = account.PatientId, ReleaseNumber = releaseNumber, AllocationId = allocation.Id, Amount = amount, Cause = cause, RefundEntryId = refundId, Reason = reason,
            IdempotencyKey = key, ReleasedAtUtc = now, ReleasedByUserId = actor,
        });
}
