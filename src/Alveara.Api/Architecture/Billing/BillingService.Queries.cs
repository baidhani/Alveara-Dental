using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Treatment;

using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

public partial class BillingService
{
    /// <summary>
    /// The patient's account: the entries in the order they were made, each with the balance owed after it, whether it has been reversed (and by what), what a charge has been paid and has left, what a payment
    /// has been applied to and has not yet, and every allocation; plus the balance now. A patient with nothing billed yet has a zero balance and no row version. Every number is the ledger's, rebuilt from the
    /// preserved history by <see cref="BillingLedgerMath"/>.
    /// </summary>
    public async Task<BillingAccountView> GetAccountAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        if (account is null) return new BillingAccountView(patientId, false, 0m, AppMoney.Currency, null, [], [], 0m, 0m, []);

        var ledger = await LoadLedgerAsync(account.Id, ct);
        var math = ledger.Math;
        var maths = ledger.MathAllocations;
        var running = BillingLedgerMath.RunningBalances(math);
        var reversed = ledger.Entries.Where(e => e.ReversesEntryId is not null).ToDictionary(e => e.ReversesEntryId!.Value, e => e.EntryNumber);
        var names = await ClinicalNames.ResolveAsync(db, ledger.Entries.Select(e => (Guid?)e.RecordedByUserId).Concat(ledger.Allocations.Select(a => (Guid?)a.AllocatedByUserId)), ct);
        var methods = await db.BillingPaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var payerIds = ledger.Entries.Where(e => e.PaidByPatientId is not null).Select(e => e.PaidByPatientId!.Value).Distinct().ToList();
        var payers = await db.Patients.AsNoTracking().Where(p => payerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}", ct);
        var releases = ledger.MathReleases;
        var open = BillingLedgerMath.OpenCharges(math, maths, releases).ToDictionary(o => o.Charge, o => o.Open);

        var views = new List<BillingEntryView>();
        var standing = new List<BillingEntryStanding>();
        for (var i = 0; i < ledger.Entries.Count; i++)
        {
            var e = ledger.Entries[i];
            var isCharge = e.Kind == BillingKinds.Charge;
            var isPayment = e.Kind == BillingKinds.Payment;
            views.Add(new BillingEntryView(e.EntryNumber, e.Kind, e.Amount, running[i], e.PlanItemId, e.ProcedureCode, e.ProcedureDescription, e.PaymentMethod, e.Reason, e.RecordedAtUtc,
                ClinicalNames.Name(names, e.RecordedByUserId), ReversesEntryNumber: e.ReversesEntryId is Guid id ? ledger.NumberOf(id) : null, RefundsEntryNumber: e.RefundsEntryId is Guid rf ? ledger.NumberOf(rf) : null, ReplacesEntryNumber: e.ReplacesEntryId is Guid rp ? ledger.NumberOf(rp) : null,
                PaymentMethodName: e.PaymentMethodId is Guid m && methods.TryGetValue(m, out var mn) ? mn : e.PaymentMethod,
                PaidByName: e.PaidByPatientId is Guid p && payers.TryGetValue(p, out var pn) ? pn : null));
            standing.Add(new BillingEntryStanding(e.EntryNumber, reversed.ContainsKey(e.Id), reversed.TryGetValue(e.Id, out var by) ? by : null,
                isCharge ? BillingLedgerMath.Paid(math, maths, e.EntryNumber, releases) : null, isCharge ? open.GetValueOrDefault(e.EntryNumber) : null,
                isPayment ? BillingLedgerMath.Applied(maths, e.EntryNumber, releases) : null, isPayment ? BillingLedgerMath.Unapplied(math, maths, e.EntryNumber, releases) : null,
                isPayment ? BillingLedgerMath.Refunded(math, e.EntryNumber) : null, isPayment ? BillingLedgerMath.Refundable(math, e.EntryNumber) : null));
        }
        var releasedPayments = BillingLedgerMath.Reversed(math);
        var allocationViews = ledger.Allocations.OrderBy(a => a.AllocationNumber)
            .Select(a => new BillingAllocationView(a.AllocationNumber, ledger.NumberOf(a.PaymentEntryId), ledger.NumberOf(a.ChargeEntryId), a.Amount, releasedPayments.Contains(ledger.NumberOf(a.PaymentEntryId)), a.AllocatedAtUtc,
                ClinicalNames.Name(names, a.AllocatedByUserId), ReleasedAmount: (a.Amount - ledger.Remaining(a)))).ToList();
        var unapplied = ledger.Entries.Where(e => e.Kind == BillingKinds.Payment).Aggregate(AppMoney.Zero, (sum, e) => sum + AppMoney.FromDecimal(BillingLedgerMath.Unapplied(math, maths, e.EntryNumber, releases))).Amount;
        var openTotal = open.Values.Aggregate(AppMoney.Zero, (sum, v) => sum + AppMoney.FromDecimal(v)).Amount;
        return new BillingAccountView(patientId, true, running.Count == 0 ? 0m : running[^1], AppMoney.Currency, Convert.ToBase64String(account.RowVersion), views, allocationViews, unapplied, openTotal, standing);
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
