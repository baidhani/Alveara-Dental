using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// The financial effect of a completion. A procedure is billable when the fee its plan stored is more than zero (the ledger refuses a zero charge, so a no-charge procedure completes with <c>charge: none</c> and no
/// ledger entry). A billable procedure is charged only through the billing contract, inside this transaction on this context, with the set's correlation id and no amount from the caller; the contract makes
/// one charge per procedure, so a charge billing already holds for it is reused, never duplicated.
/// </summary>
public partial class CompletedProcedureService
{
    public static bool IsBillable(TreatmentPlanItem item) => item.Fee > 0m;

    /// <summary>Charges each billable procedure in item order. Any failure ends the whole transaction: the caller rolls everything back.</summary>
    private async Task<int> ChargeAsync(CompletionSet set, IReadOnlyList<TreatmentPlanItem> items, Guid actor, CancellationToken ct)
    {
        var created = 0;
        foreach (var item in items.OrderBy(i => i.ItemNumber).Where(IsBillable))
        {
            try
            {
                var charge = await charging.ChargeCompletedProcedureAsync(new ProcedureChargeRequest(set.PatientId, item.Id, set.CorrelationId), actor, ct);
                if (charge.Created) created++;
            }
            catch (BillingException ex) when (ex.Code == "save_failed")
            {
                throw new TreatmentPlanException("save_failed", "The completion could not be saved, so nothing was completed. Try again; if it keeps failing, contact support.", 503, inner: ex);
            }
            catch (BillingException ex)
            {
                throw new TreatmentPlanException("charge_failed", $"The charge for procedure {item.ItemNumber} could not be created, so nothing was completed.", 409, inner: ex);
            }
        }
        return created;
    }

    /// <summary>What each procedure's completion did to its charge, read from the ledger: a charge that carries this set's correlation id was created by it; any other was already there; a zero fee has none.</summary>
    private async Task<IReadOnlyDictionary<Guid, ChargeEffect>> ChargeEffectsAsync(CompletionSet set, IReadOnlyList<PlanItemView> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var entries = await db.BillingEntries.AsNoTracking().Where(e => e.PlanItemId != null && ids.Contains(e.PlanItemId.Value)).ToDictionaryAsync(e => e.PlanItemId!.Value, ct);
        return items.ToDictionary(i => i.Id, i =>
            i.Fee <= 0m || !entries.TryGetValue(i.Id, out var entry)
                ? new ChargeEffect(ChargeOutcomes.None, null, null)
                : new ChargeEffect(entry.CorrelationId == set.CorrelationId ? ChargeOutcomes.Created : ChargeOutcomes.Existing, entry.EntryNumber, entry.Amount));
    }
}
