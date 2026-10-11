using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>A request to charge the practice's fee for one completed plan procedure. <c>CorrelationId</c> is the caller's id for the operation (the completion), carried onto the charge and its audit entry.</summary>
public sealed record ProcedureChargeRequest(Guid PatientId, Guid PlanItemId, Guid CorrelationId);

/// <summary>
/// The charge that exists for a completed procedure. <c>Created</c> is false when it already existed (a repeat of the same request, or a completion that was replayed); <c>Reversed</c> says a reversal has since undone it.
/// There is only ever one charge per plan procedure, so a reversed one is still the procedure's charge and is never silently made again.
/// </summary>
public sealed record ProcedureChargeResult(Guid PatientId, Guid PlanItemId, Guid ChargeEntryId, int EntryNumber, decimal Amount, string Currency, string ProcedureCode, bool Created, bool Reversed, Guid? CorrelationId);

/// <summary>
/// ALV-007-C01: the contract <c>ALV-008-C01</c> (atomic procedure completion) calls to make the financial effect of completing a procedure, so completion needs no billing logic of its own.
///
/// Calling it:
/// - Run it on the same <c>AlveraDbContext</c> as the completion and inside a transaction the caller opened (<c>Database.BeginTransactionAsync</c>); it refuses to run outside one, because a charge that could outlive a
///   failed completion (or the reverse) would break the story's atomicity. It saves what the caller has staged in that transaction (so the completed state exists when the charge's rules are checked), adds the charge
///   and its audit entry, and saves again; the caller commits or rolls everything back together.
/// - It is idempotent: the charge's save key is derived from the plan procedure, so there is exactly one charge per billable completed procedure however many times a completion is replayed; a repeat returns the
///   existing charge with <c>Created = false</c>.
/// - Its answers are <see cref="BillingException"/>s with stable codes: <c>plan_item_not_found</c> (404; another patient's looks the same), <c>item_withdrawn</c> and <c>not_completed</c> (409), and <c>save_failed</c> (503,
///   including a failed audit write, which stores no charge). Two callers charging the same procedure at once end with one charge: the loser gets a <see cref="ConcurrencyConflictException"/>, rolls back and
///   retries to receive the existing charge.
/// - The amount is always the fee the plan copied from the catalog when it proposed the procedure; the request cannot carry one.
/// </summary>
public interface IProcedureCharging
{
    Task<ProcedureChargeResult> ChargeCompletedProcedureAsync(ProcedureChargeRequest request, Guid actor, CancellationToken ct);
}

public partial class BillingService : IProcedureCharging
{
    /// <summary>The save key that makes "one charge per procedure" true across every caller and every replay.</summary>
    public static string ProcedureChargeKey(Guid planItemId) => $"procedure-charge-{planItemId:N}";

    public async Task<ProcedureChargeResult> ChargeCompletedProcedureAsync(ProcedureChargeRequest request, Guid actor, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Charging a completed procedure must run inside the transaction that completes it: open one on the same context first, so the completion and its charge commit or roll back together.");
        await RequirePatientAsync(request.PatientId, ct);
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);                       // the completion the caller staged is saved (inside its transaction) so it is there when the charge's rules are checked

        var billable = await LoadBillableAsync(request.PatientId, request.PlanItemId, ct);
        var existing = await db.BillingEntries.AsNoTracking().FirstOrDefaultAsync(e => e.PlanItemId == billable.Item.Id, ct);
        if (existing is not null) return await ResultAsync(existing, created: false, ct);

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.PatientId == request.PatientId, ct);
        var charge = await StageChargeAsync(account, request.PatientId, billable, ProcedureChargeKey(billable.Item.Id), request.CorrelationId, actor, ct);
        await SaveAsync(charge.AccountId, ct);
        return await ResultAsync(charge, created: true, ct);
    }

    private async Task<ProcedureChargeResult> ResultAsync(BillingEntry charge, bool created, CancellationToken ct) =>
        new(charge.PatientId, charge.PlanItemId!.Value, charge.Id, charge.EntryNumber, charge.Amount, AppMoney.Currency, charge.ProcedureCode!, created,
            await db.BillingEntries.AsNoTracking().AnyAsync(e => e.ReversesEntryId == charge.Id, ct), charge.CorrelationId);
}
