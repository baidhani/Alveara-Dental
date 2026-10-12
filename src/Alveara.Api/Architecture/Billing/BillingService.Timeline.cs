using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C02: reading the account as a story and by age. Both are derived from the preserved history on every read and store nothing.
/// - <b>Timeline</b>: every entry, application, release, change of responsible party and issued document in the order it happened (entries first when several share an instant, so the cause reads before its
///   effect), each charge naming the completed procedure it bills and when that was performed.
/// - <b>Aging</b>: what is open on each charge, aged from the procedure's performed time (as amended, latest first) in whole UTC days, in the buckets 0-30, 31-60, 61-90 and over 90.
/// </summary>
public partial class BillingService
{
    private static int Rank(string type) => type switch { "Release" => 1, "Application" => 2, "Responsibility" => 3, "Statement" or "Receipt" => 4, _ => 0 };

    public async Task<IReadOnlyList<TimelineEvent>> GetTimelineAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        if (account is null) return [];
        var ledger = await LoadLedgerAsync(account.Id, ct);
        var snapshot = await LoadSnapshotAsync(account.Id, null, ct);
        var service = snapshot.Entries.ToDictionary(e => e.Number, e => e.ServiceDateUtc);
        var responsibilities = await db.BillingResponsibilities.AsNoTracking().Where(r => r.AccountId == account.Id).OrderBy(r => r.Sequence).ToListAsync(ct);
        var documents = await db.FinancialDocuments.AsNoTracking().Where(d => d.AccountId == account.Id).ToListAsync(ct);
        var people = await db.Patients.AsNoTracking().Where(p => responsibilities.Select(r => r.ResponsiblePatientId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var names = await ClinicalNames.ResolveAsync(db, ledger.Entries.Select(e => (Guid?)e.RecordedByUserId).Concat(ledger.Allocations.Select(a => (Guid?)a.AllocatedByUserId)).Concat(ledger.Releases.Select(r => (Guid?)r.ReleasedByUserId))
            .Concat(responsibilities.Select(r => (Guid?)r.SetByUserId)).Concat(documents.Select(d => (Guid?)d.IssuedByUserId)), ct);
        var methods = await db.BillingPaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var itemIds = ledger.Entries.Where(e => e.PlanItemId is not null).Select(e => e.PlanItemId!.Value).ToList();
        var completed = await db.CompletedProcedures.AsNoTracking().Where(p => itemIds.Contains(p.PlanItemId)).ToDictionaryAsync(p => p.PlanItemId, p => p.Id, ct);
        var events = new List<TimelineEvent>();

        foreach (var e in ledger.Entries)
        {
            var method = e.PaymentMethodId is Guid m && methods.TryGetValue(m, out var n) ? n : e.PaymentMethod;
            string? source = e.Kind == BillingKinds.Charge && e.PlanItemId is not null ? $"Completed procedure performed {service[e.EntryNumber]:yyyy-MM-dd}" : null;
            var summary = e.Kind switch
            {
                BillingKinds.Charge => $"Charge for {e.ProcedureCode} {e.ProcedureDescription}",
                BillingKinds.Payment => e.ReplacesEntryId is Guid rp ? $"Payment by {method}, replacing payment {ledger.NumberOf(rp)}" : $"Payment by {method}",
                BillingKinds.Adjustment => e.ReplacesEntryId is Guid ra ? $"Adjustment replacing adjustment {ledger.NumberOf(ra)}: {e.Reason}" : $"Adjustment: {e.Reason}",
                BillingKinds.Reversal => $"Reversal of entry {ledger.NumberOf(e.ReversesEntryId!.Value)}: {e.Reason}",
                BillingKinds.Refund => $"Refund of payment {ledger.NumberOf(e.RefundsEntryId!.Value)} by {method}: {e.Reason}",
                _ => e.Kind,
            };
            events.Add(new TimelineEvent(e.RecordedAtUtc, e.Kind, e.EntryNumber, summary, e.Amount, source, ClinicalNames.Name(names, e.RecordedByUserId), e.PlanItemId,
                e.PlanItemId is Guid item && completed.TryGetValue(item, out var cp) ? cp : null));
        }
        var allocationsById = ledger.Allocations.ToDictionary(a => a.Id);
        foreach (var a in ledger.Allocations)
            events.Add(new TimelineEvent(a.AllocatedAtUtc, "Application", a.AllocationNumber, $"{a.Amount:F2} {AppMoney.Currency} of payment {ledger.NumberOf(a.PaymentEntryId)} applied to charge {ledger.NumberOf(a.ChargeEntryId)}", a.Amount, null, ClinicalNames.Name(names, a.AllocatedByUserId)));
        foreach (var r in ledger.Releases)
        {
            var a = allocationsById[r.AllocationId];
            events.Add(new TimelineEvent(r.ReleasedAtUtc, "Release", r.ReleaseNumber, $"{r.Amount:F2} {AppMoney.Currency} of payment {ledger.NumberOf(a.PaymentEntryId)} released from charge {ledger.NumberOf(a.ChargeEntryId)} ({r.Cause.ToLowerInvariant()})", r.Amount, null,
                ClinicalNames.Name(names, r.ReleasedByUserId)));
        }
        foreach (var r in responsibilities)
            events.Add(new TimelineEvent(r.SetAtUtc, "Responsibility", r.Sequence, $"{NameOf(people[r.ResponsiblePatientId])} responsible from {r.EffectiveFrom:yyyy-MM-dd}", null, null, ClinicalNames.Name(names, r.SetByUserId)));
        foreach (var d in documents)
            events.Add(new TimelineEvent(d.IssuedAtUtc, d.DocumentType, d.DocumentNumber, $"{d.DocumentType} {d.DocumentNumber} issued", null, null, ClinicalNames.Name(names, d.IssuedByUserId)));
        return events.OrderBy(e => e.AtUtc).ThenBy(e => Rank(e.Type)).ThenBy(e => e.Number).ToList();
    }

    public async Task<AgingView> GetAgingAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        var now = clock.UtcNow;
        if (account is null) return new AgingView(now, AppMoney.Currency, new DocumentAging("0.00", "0.00", "0.00", "0.00"), []);
        var snapshot = await LoadSnapshotAsync(account.Id, null, ct);                                  // live: the latest amendment of each completed procedure
        var cutOffs = new FinancialCutOffs(snapshot.Entries.Max(e => e.Number), snapshot.Allocations.Select(a => a.Number).DefaultIfEmpty(0).Max(), snapshot.Releases.Select(r => r.Number).DefaultIfEmpty(0).Max(), 0, now);
        var (charges, buckets) = FinancialDocumentBuilder.AgingAt(snapshot, cutOffs);
        return new AgingView(now, AppMoney.Currency, buckets, charges);
    }
}
