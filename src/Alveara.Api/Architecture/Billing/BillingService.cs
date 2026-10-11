using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// STORY-007: charges, payments and adjustments on a patient's billing account (writes here; the reads are in <c>BillingService.Queries.cs</c>).
///
/// How the promises are kept:
/// - <b>The charge amount is the procedure's, never the caller's.</b> A charge names a completed plan procedure and takes the fee the plan copied from the catalog when it was proposed; the request
///   has no amount to get wrong. A procedure that is not completed, is withdrawn, belongs to another patient or is already billed is refused.
/// - <b>History is only ever added to.</b> Entries are appended and never edited or deleted (a database trigger refuses both); a mistake is corrected with an adjustment that says why. The balance
///   is the sum of the entries every time it is read, so it cannot drift from the history.
/// - <b>A payment cannot be misapplied.</b> It goes to the account of the patient named in the route, must be more than zero and no more than the balance owed, and uses one of a fixed list of methods.
///   (Allocating a payment to specific charges is ALV-007-C01's.)
/// - <b>Every entry is logged in the same save</b> - the entry, the account's new version and a PHI-free audit entry (who, when, what) - so an entry cannot exist without its log entry; if anything in
///   the save fails (including the log) nothing is stored and the caller is told so (<c>save_failed</c>, 503) without internals.
/// - <b>Repeats are quiet and concurrent changes are refused:</b> the same save key returns the entry the first save made (a different request under the same key is refused), and a payment or
///   adjustment made from an out-of-date copy of the account is a conflict that changes nothing.
/// </summary>
public partial class BillingService(AlveraDbContext db, IPracticeClock clock, ILogger<BillingService>? logger = null)
{
    /// <summary>Bills a completed plan procedure at the fee it was proposed at. Billing the same procedure again with the same key returns the account unchanged.</summary>
    public async Task<BillingAccountView> RecordChargeAsync(Guid patientId, ChargeInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        if (input.PlanItemId is null) errors["planItemId"] = "Choose the completed procedure to bill.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var planItemId = input.PlanItemId!.Value;

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        if (account is not null)
        {
            var same = await db.BillingEntries.AsNoTracking().FirstOrDefaultAsync(e => e.AccountId == account.Id && e.IdempotencyKey == key, ct);
            if (same is not null) return same.Kind == BillingKinds.Charge && same.PlanItemId == planItemId ? await GetAccountAsync(patientId, ct) : throw KeyReused();
        }

        var item = await db.TreatmentPlanItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == planItemId && i.PatientId == patientId, ct)
            ?? throw new BillingException("plan_item_not_found", "That procedure was not found for this patient.", 404);   // another patient's looks exactly like an unknown one
        var planWithdrawn = await db.TreatmentPlans.AsNoTracking().AnyAsync(p => p.Id == item.PlanId && p.Status == TreatmentPlanStatuses.Withdrawn, ct);
        if (item.WithdrawnAtUtc is not null || planWithdrawn) throw new BillingException("item_withdrawn", "That procedure has been withdrawn, so it cannot be billed.", 409);
        if (!await db.TreatmentPlanItemStates.AsNoTracking().AnyAsync(s => s.ItemId == item.Id && s.Completion == ItemCompletion.Completed, ct))
            throw new BillingException("not_completed", "Only a completed procedure can be billed. Mark it completed in the treatment plan first.", 409);
        if (await db.BillingEntries.AsNoTracking().AnyAsync(e => e.PlanItemId == item.Id, ct)) throw new BillingException("already_billed", "That procedure has already been billed.", 409);

        var procedure = await (from v in db.ProcedureVersions.AsNoTracking() join d in db.ProcedureDefinitions.AsNoTracking() on v.ProcedureId equals d.Id
                               where v.Id == item.ProcedureVersionId select new { d.Code, v.Description }).FirstAsync(ct);
        var now = clock.UtcNow;
        account = Open(account, patientId, actor, now);
        var amount = AppMoney.FromDecimal(item.Fee).Amount;
        db.BillingEntries.Add(new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = await NextNumberAsync(account.Id, ct), Kind = BillingKinds.Charge, Amount = amount, PlanItemId = item.Id,
            ProcedureCode = procedure.Code, ProcedureDescription = procedure.Description, IdempotencyKey = key!, RecordedAtUtc = now, RecordedByUserId = actor,
        });
        AuditService.Record(db, "BillingChargeRecorded", nameof(BillingAccount), account.Id, actor, $"Charge of {amount:F2} {AppMoney.Currency} recorded for completed procedure {procedure.Code}.");
        await SaveAsync(account.Id, ct);
        return await GetAccountAsync(patientId, ct);
    }

    /// <summary>Records money received against the balance owed. It cannot be more than the balance (no credit balances in this story) and must carry the account version the person saw.</summary>
    public async Task<BillingAccountView> RecordPaymentAsync(Guid patientId, PaymentInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        var amount = BillingRules.CleanPositiveAmount(errors, input.Amount, "payment");
        var method = BillingRules.CleanMethod(errors, input.Method);
        var note = BillingRules.CleanText(errors, "note", input.Note, required: false, missing: "");
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var version = BillingRules.ParseVersion(input.RowVersion);

        var account = await AccountForChangeAsync(patientId, ct);
        var same = await db.BillingEntries.AsNoTracking().FirstOrDefaultAsync(e => e.AccountId == account.Id && e.IdempotencyKey == key, ct);
        if (same is not null)
            return same.Kind == BillingKinds.Payment && same.Amount == -amount && same.PaymentMethod == method && same.Reason == note ? await GetAccountAsync(patientId, ct) : throw KeyReused();

        var balance = await BalanceAsync(account.Id, ct);
        if (amount > balance)
            throw BillingRules.Invalid(new Dictionary<string, string> { ["amount"] = $"A payment cannot be more than the balance owed ({balance:F2} {AppMoney.Currency}). Check the amount and the patient." });

        var now = clock.UtcNow;
        Touch(account, version, now);
        db.BillingEntries.Add(new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = await NextNumberAsync(account.Id, ct), Kind = BillingKinds.Payment, Amount = -amount!.Value, PaymentMethod = method,
            Reason = note, IdempotencyKey = key!, RecordedAtUtc = now, RecordedByUserId = actor,
        });
        AuditService.Record(db, "BillingPaymentRecorded", nameof(BillingAccount), account.Id, actor, $"Payment of {amount:F2} {AppMoney.Currency} recorded ({method}).");
        await SaveAsync(account.Id, ct);
        return await GetAccountAsync(patientId, ct);
    }

    /// <summary>Corrects the balance with a signed amount and the reason for it. It cannot take the balance below zero; reversing a specific entry is ALV-007-C01's.</summary>
    public async Task<BillingAccountView> RecordAdjustmentAsync(Guid patientId, AdjustmentInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        var reason = BillingRules.CleanText(errors, "reason", input.Reason, required: true, missing: "An adjustment needs a reason, so the history explains it.");
        var amount = input.Amount;
        if (amount is null) errors["amount"] = "Enter the adjustment amount (negative to reduce what is owed).";
        else if (amount == 0) errors["amount"] = "An adjustment of zero changes nothing.";
        else if (Math.Round(amount.Value, 2) != amount) errors["amount"] = "Enter the amount in dollars and cents (no more than two decimal places).";
        else if (Math.Abs(amount.Value) > BillingLimits.AmountMax) errors["amount"] = $"An adjustment cannot be more than {BillingLimits.AmountMax:F2}.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var version = BillingRules.ParseVersion(input.RowVersion);

        var account = await AccountForChangeAsync(patientId, ct);
        var same = await db.BillingEntries.AsNoTracking().FirstOrDefaultAsync(e => e.AccountId == account.Id && e.IdempotencyKey == key, ct);
        if (same is not null)
            return same.Kind == BillingKinds.Adjustment && same.Amount == amount && same.Reason == reason ? await GetAccountAsync(patientId, ct) : throw KeyReused();

        var balance = await BalanceAsync(account.Id, ct);
        if (balance + amount < 0)
            throw BillingRules.Invalid(new Dictionary<string, string> { ["amount"] = $"An adjustment cannot take the balance below zero (balance owed {balance:F2} {AppMoney.Currency})." });

        var now = clock.UtcNow;
        Touch(account, version, now);
        db.BillingEntries.Add(new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, EntryNumber = await NextNumberAsync(account.Id, ct), Kind = BillingKinds.Adjustment, Amount = amount!.Value, Reason = reason,
            IdempotencyKey = key!, RecordedAtUtc = now, RecordedByUserId = actor,
        });
        AuditService.Record(db, "BillingAdjustmentRecorded", nameof(BillingAccount), account.Id, actor, $"Adjustment of {amount:+0.00;-0.00} {AppMoney.Currency} recorded.", reason);
        await SaveAsync(account.Id, ct);
        return await GetAccountAsync(patientId, ct);
    }

    // ---------- shared ----------

    private async Task RequirePatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new BillingException("patient_not_found", "That patient was not found.", 404);
    }

    private static BillingException KeyReused() =>
        new("idempotency_key_reused", "That save key was already used for a different entry. Reload the account and try again.", 409);

    /// <summary>The patient's account for a payment or adjustment, which needs something already on it; the account is loaded for the patient named in the route and no other.</summary>
    private async Task<BillingAccount> AccountForChangeAsync(Guid patientId, CancellationToken ct) =>
        await db.BillingAccounts.FirstOrDefaultAsync(a => a.PatientId == patientId, ct)
            ?? throw new BillingException("nothing_owed", "Nothing has been billed to this patient yet, so there is nothing to pay or adjust.", 409);

    private BillingAccount Open(BillingAccount? existing, Guid patientId, Guid actor, DateTimeOffset now)
    {
        if (existing is not null)
        {
            existing.UpdatedAtUtc = now;
            db.Entry(existing).Property(a => a.UpdatedAtUtc).IsModified = true;   // the row must change on every entry, even in the same instant, so its version moves
            return existing;
        }
        var account = new BillingAccount { Id = Guid.NewGuid(), PatientId = patientId, CreatedAtUtc = now, CreatedByUserId = actor };
        db.BillingAccounts.Add(account);
        return account;
    }

    /// <summary>Checks the account against the version the person saw: the save is refused as a conflict if anyone recorded anything since.</summary>
    private void Touch(BillingAccount account, byte[] version, DateTimeOffset now)
    {
        db.Entry(account).Property(a => a.RowVersion).OriginalValue = version;
        account.UpdatedAtUtc = now;
        db.Entry(account).Property(a => a.UpdatedAtUtc).IsModified = true;
    }

    private async Task<int> NextNumberAsync(Guid accountId, CancellationToken ct) =>
        (await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == accountId).Select(e => (int?)e.EntryNumber).MaxAsync(ct) ?? 0) + 1;

    private async Task<decimal> BalanceAsync(Guid accountId, CancellationToken ct) =>
        AppMoney.FromDecimal(await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == accountId).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m).Amount;

    private async Task SaveAsync(Guid accountId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(BillingAccount), accountId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException(nameof(BillingAccount), accountId); // two writers opened the account, claimed the same entry number, key or procedure
        }
        catch (DbUpdateException ex)
        {
            // the whole save was one transaction, so nothing was stored; say so without leaking internals. Error class and SQL number only in the log: never the patient, the amount or the SQL text
            db.ChangeTracker.Clear();
            logger?.LogError("billing_save_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
            throw new BillingException("save_failed", "The entry could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503, inner: ex);
        }
    }
}
