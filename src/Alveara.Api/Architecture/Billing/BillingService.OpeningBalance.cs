using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-018-C02 slice 5b: an opening balance migrated from another system, the FIRST entry on a patient's ledger. The rules that keep it from ever changing what is there:
/// - it is an Adjustment (more than zero, in dollars and cents, within the ceiling), appended like every other entry: nothing is edited or overwritten, and the balance stays the sum of the entries;
/// - it is only ever the first entry: an account that already holds any entry refuses it (409 <c>account_not_empty</c>), so a balance cannot be loaded twice or added to a history;
/// - the save key makes it replayable: the same key and amount returns the entry the first call made, a different amount under the same key is refused (<c>idempotency_key_reused</c>);
/// - the correlation id names the import batch, so the ledger itself says where the entry came from; the audit entry carries the amount and currency only, never a name.
/// Two writers racing to open the same account lose cleanly: the second gets a concurrency conflict (the caller retries and then sees <c>account_not_empty</c>).
/// </summary>
public partial class BillingService
{
    public const string OpeningBalanceReason = "Opening balance migrated from an import.";

    /// <summary>Records the opening balance and returns the id of its ledger entry.</summary>
    public async Task<Guid> RecordOpeningBalanceAsync(Guid patientId, decimal? amount, string? idempotencyKey, Guid correlationId, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, idempotencyKey);
        var clean = BillingRules.CleanPositiveAmount(errors, amount, "opening balance");
        if (errors.Count > 0) throw BillingRules.Invalid(errors);

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        if (account is not null)
        {
            var same = await db.BillingEntries.AsNoTracking().FirstOrDefaultAsync(e => e.AccountId == account.Id && e.IdempotencyKey == key, ct);
            if (same is not null) return same.Kind == BillingKinds.Adjustment && same.Amount == clean && same.Reason == OpeningBalanceReason ? same.Id : throw KeyReused();
            if (await db.BillingEntries.AsNoTracking().AnyAsync(e => e.AccountId == account.Id, ct))
                throw new BillingException("account_not_empty", "This patient's account already has entries, so an opening balance cannot be added.", 409);
        }

        var now = clock.UtcNow;
        account = Open(account, patientId, actor, now);
        var entry = new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = 1, Kind = BillingKinds.Adjustment, Amount = clean!.Value, Reason = OpeningBalanceReason,
            CorrelationId = correlationId, IdempotencyKey = key!, RecordedAtUtc = now, RecordedByUserId = actor,
        };
        db.BillingEntries.Add(entry);
        AuditService.Record(db, "BillingOpeningBalanceRecorded", nameof(BillingAccount), account.Id, actor, $"Opening balance of {clean:F2} {AppMoney.Currency} recorded from an import.", correlationId: correlationId);
        await SaveAsync(account.Id, ct);
        return entry.Id;
    }
}
