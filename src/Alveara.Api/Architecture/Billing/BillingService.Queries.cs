using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Treatment;

using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

public partial class BillingService
{
    /// <summary>The patient's account: the entries in the order they were made, each with the balance owed after it, and the balance now. A patient with nothing billed yet has a zero balance and no row version.</summary>
    public async Task<BillingAccountView> GetAccountAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        if (account is null) return new BillingAccountView(patientId, false, 0m, AppMoney.Currency, null, []);

        var entries = await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == account.Id).OrderBy(e => e.EntryNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, entries.Select(e => (Guid?)e.RecordedByUserId), ct);
        var running = AppMoney.Zero;
        var views = new List<BillingEntryView>();
        foreach (var e in entries)
        {
            running += AppMoney.FromDecimal(e.Amount);
            views.Add(new BillingEntryView(e.EntryNumber, e.Kind, e.Amount, running.Amount, e.PlanItemId, e.ProcedureCode, e.ProcedureDescription, e.PaymentMethod, e.Reason, e.RecordedAtUtc,
                ClinicalNames.Name(names, e.RecordedByUserId)));
        }
        return new BillingAccountView(patientId, true, running.Amount, AppMoney.Currency, Convert.ToBase64String(account.RowVersion), views);
    }

    /// <summary>
    /// The patient's plan procedures that are completed, not withdrawn and not yet billed, with only what billing needs: the code, the description, the tooth and surface and the fee the plan copied.
    /// Billing roles cannot read treatment plans (STORY-015), so this is the one view of them they get, and it carries nothing clinical.
    /// </summary>
    public async Task<IReadOnlyList<BillableProcedureView>> ListBillableAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var rows = await (from i in db.TreatmentPlanItems.AsNoTracking()
                          join p in db.TreatmentPlans.AsNoTracking() on i.PlanId equals p.Id
                          join v in db.ProcedureVersions.AsNoTracking() on i.ProcedureVersionId equals v.Id
                          join d in db.ProcedureDefinitions.AsNoTracking() on i.ProcedureId equals d.Id
                          where i.PatientId == patientId && i.WithdrawnAtUtc == null && p.Status != TreatmentPlanStatuses.Withdrawn
                                && db.TreatmentPlanItemStates.Any(s => s.ItemId == i.Id && s.Completion == ItemCompletion.Completed)
                                && !db.BillingEntries.Any(e => e.PlanItemId == i.Id)
                          orderby i.CreatedAtUtc, i.ItemNumber
                          select new { i.Id, d.Code, v.Description, i.ToothKey, i.Surface, i.Fee }).ToListAsync(ct);
        return rows.Select(r => new BillableProcedureView(r.Id, r.Code, r.Description, r.ToothKey, r.Surface, AppMoney.FromDecimal(r.Fee).Amount, AppMoney.Currency)).ToList();
    }
}
