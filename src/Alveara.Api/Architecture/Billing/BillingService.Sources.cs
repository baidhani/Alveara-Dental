using Microsoft.EntityFrameworkCore;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C02 R02: the source of a charge, read with billing permission only. The timeline links a charge back to the completed care it bills, and the timeline is read with <c>ViewBilling</c>, so the
/// destination of that link must be too: a narrow projection (see <see cref="ChargeSourceView"/>) of the plan procedure and the completion record, with no provider, encounter, chart effect, diagnosis or clinical
/// wording. It answers only for a charge entry of the patient named in the route; anything else (another patient's entry, a payment, an unknown number) is <c>source_not_found</c> and says nothing about what exists.
/// </summary>
public partial class BillingService
{
    public async Task<ChargeSourceView> GetChargeSourceAsync(Guid patientId, int entryNumber, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var charge = await db.BillingEntries.AsNoTracking().FirstOrDefaultAsync(e => e.PatientId == patientId && e.EntryNumber == entryNumber && e.Kind == BillingKinds.Charge && e.PlanItemId != null, ct)
            ?? throw new BillingException("source_not_found", "That charge has no source to show.", 404);
        var item = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.Id == charge.PlanItemId && i.PatientId == patientId).Select(i => new { i.ToothKey, i.Surface }).FirstOrDefaultAsync(ct);
        var reversed = await db.BillingEntries.AsNoTracking().AnyAsync(e => e.ReversesEntryId == charge.Id, ct);
        var completion = await db.CompletedProcedures.AsNoTracking().FirstOrDefaultAsync(p => p.PlanItemId == charge.PlanItemId && p.PatientId == patientId, ct);
        if (completion is null)
            return new ChargeSourceView(charge.EntryNumber, charge.ProcedureCode ?? "", charge.ProcedureDescription ?? "", item?.ToothKey, item?.Surface, AppMoney.FromDecimal(charge.Amount).Amount, AppMoney.Currency, reversed,
                false, null, null, null, false, charge.CorrelationId);
        var set = await db.CompletionSets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == completion.SetId, ct);
        var latest = (await db.CompletedProcedureAmendments.AsNoTracking().Where(a => a.CompletedProcedureId == completion.Id && a.PerformedAtUtc != null).ToListAsync(ct)).OrderBy(a => a.AmendmentNumber).LastOrDefault();
        return new ChargeSourceView(charge.EntryNumber, charge.ProcedureCode ?? "", charge.ProcedureDescription ?? "", item?.ToothKey, item?.Surface, AppMoney.FromDecimal(charge.Amount).Amount, AppMoney.Currency, reversed,
            true, completion.Id, completion.FinalizedAtUtc, latest?.PerformedAtUtc ?? completion.PerformedAtUtc, latest is not null, set?.CorrelationId ?? charge.CorrelationId);
    }
}
