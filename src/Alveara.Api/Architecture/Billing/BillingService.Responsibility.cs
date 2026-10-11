using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C01: who is financially responsible for an account, kept apart from patient identity, and the practice's payment methods.
///
/// Responsibility is recorded by billing as an append-only history ("from this date, this patient is responsible", with who and why); the current party is the latest record. Until billing records one, the account
/// follows the guarantor registered on the patient (ALV-003-C01), and failing that the patient. Recording a responsible party never changes the patient's registered guarantor and changing the registered guarantor
/// never rewrites a recorded responsibility: the two are independent. Recording the party that is already responsible changes nothing.
/// </summary>
public partial class BillingService
{
    private DateOnly Today() => DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime);

    private static string NameOf(Patients.Patient p) => $"{p.FirstName} {p.LastName}";

    /// <summary>Who is responsible for the patient's account now, and every change recorded so far (oldest first).</summary>
    public async Task<BillingResponsibilityView> GetResponsibilityAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);
        var account = await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        var rows = account is null ? [] : await db.BillingResponsibilities.AsNoTracking().Where(r => r.AccountId == account.Id).OrderBy(r => r.Sequence).ToListAsync(ct);

        var ids = rows.Select(r => r.ResponsiblePatientId).Append(patientId).Concat(patient.GuarantorPatientId is Guid g ? [g] : []).Distinct().ToList();
        var people = await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.Select(r => (Guid?)r.SetByUserId), ct);
        var history = rows.Select(r => new BillingResponsibilityItem(r.Sequence, r.ResponsiblePatientId, NameOf(people[r.ResponsiblePatientId]), r.ResponsiblePatientId == patientId, r.EffectiveFrom, r.Reason, r.SetAtUtc,
            ClinicalNames.Name(names, r.SetByUserId))).ToList();

        var today = Today();
        var current = rows.LastOrDefault(r => r.EffectiveFrom <= today);
        if (current is not null)
            return new BillingResponsibilityView(patientId, current.ResponsiblePatientId, NameOf(people[current.ResponsiblePatientId]), current.ResponsiblePatientId == patientId, "Recorded", current.EffectiveFrom,
                account is null ? null : Convert.ToBase64String(account.RowVersion), history);
        if (patient.GuarantorPatientId is Guid registered && people.TryGetValue(registered, out var guarantor) && guarantor.IsActive)
            return new BillingResponsibilityView(patientId, registered, NameOf(guarantor), false, "RegisteredGuarantor", null, account is null ? null : Convert.ToBase64String(account.RowVersion), history);
        return new BillingResponsibilityView(patientId, patientId, NameOf(patient), true, "Self", null, account is null ? null : Convert.ToBase64String(account.RowVersion), history);
    }

    /// <summary>
    /// Records who is responsible for the patient's account from a date. The party must be the patient or another active patient; the date cannot be in the future or before the one it replaces; the account version
    /// is needed once the account exists. Naming the party who is already responsible changes nothing. Written with its audit entry in one save.
    /// </summary>
    public async Task<BillingResponsibilityView> RecordResponsibilityAsync(Guid patientId, ResponsibilityInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var reason = BillingRules.CleanText(errors, "reason", input.Reason, required: false, missing: "");
        var today = Today();
        var from = input.EffectiveFrom ?? today;
        if (from > today) errors["effectiveFrom"] = "The date cannot be in the future.";
        Patients.Patient? responsible = null;
        if (input.ResponsiblePatientId is not Guid responsibleId) errors["responsiblePatientId"] = "Choose who is responsible for this account.";
        else
        {
            responsible = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == responsibleId, ct);
            if (responsible is null) errors["responsiblePatientId"] = "That patient was not found.";
            else if (!responsible.IsActive) errors["responsiblePatientId"] = "Only an active patient can be responsible for an account.";
        }
        if (errors.Count > 0) throw BillingRules.Invalid(errors);

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.PatientId == patientId, ct);
        var now = clock.UtcNow;
        var sequence = 1;
        if (account is not null)
        {
            var last = await db.BillingResponsibilities.AsNoTracking().Where(r => r.AccountId == account.Id).OrderByDescending(r => r.Sequence).FirstOrDefaultAsync(ct);
            if (last is not null)
            {
                sequence = last.Sequence + 1;
                if (last.ResponsiblePatientId == responsible!.Id) return await GetResponsibilityAsync(patientId, ct);                    // already the responsible party: nothing to record
                if (from < last.EffectiveFrom) throw BillingRules.Invalid(new Dictionary<string, string> { ["effectiveFrom"] = $"The date cannot be before the one it replaces ({last.EffectiveFrom:yyyy-MM-dd})." });
            }
            Touch(account, BillingRules.ParseVersion(input.RowVersion), now);
        }
        else
        {
            account = Open(null, patientId, actor, now);                                                                                // the account is opened by recording who is responsible for it
        }

        db.BillingResponsibilities.Add(new BillingResponsibility
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, Sequence = sequence, ResponsiblePatientId = responsible!.Id, EffectiveFrom = from, Reason = reason, SetAtUtc = now, SetByUserId = actor,
        });
        AuditService.Record(db, "BillingResponsibilityChanged", nameof(BillingAccount), account.Id, actor, responsible.Id == patientId ? "The patient was recorded as responsible for their own account." : "Another patient was recorded as responsible for the account.", reason);
        await SaveAsync(account.Id, ct);
        return await GetResponsibilityAsync(patientId, ct);
    }

    /// <summary>
    /// Every account the patient is responsible for (their own and those of everyone whose current responsible party they are), with what is owed on each and in total. Each balance is that account's own ledger
    /// figure, rebuilt from its entries; a patient who is responsible for nobody (and has no account) has an empty summary.
    /// </summary>
    public async Task<GuarantorSummaryView> GetGuarantorSummaryAsync(Guid guarantorPatientId, CancellationToken ct)
    {
        await RequirePatientAsync(guarantorPatientId, ct);
        var guarantor = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == guarantorPatientId, ct);
        var today = Today();
        var accounts = await db.BillingAccounts.AsNoTracking().ToListAsync(ct);
        var latest = (await db.BillingResponsibilities.AsNoTracking().Where(r => r.EffectiveFrom <= today).ToListAsync(ct)).GroupBy(r => r.AccountId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Sequence).Last());
        var patients = await db.Patients.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);

        Guid ResponsibleFor(BillingAccount a)
        {
            if (latest.TryGetValue(a.Id, out var row)) return row.ResponsiblePatientId;
            var patient = patients[a.PatientId];
            return patient.GuarantorPatientId is Guid g && patients.TryGetValue(g, out var gp) && gp.IsActive ? g : a.PatientId;
        }

        var rows = new List<GuarantorAccountView>();
        foreach (var account in accounts.Where(a => ResponsibleFor(a) == guarantorPatientId).OrderBy(a => a.PatientId == guarantorPatientId ? 0 : 1).ThenBy(a => patients[a.PatientId].LastName).ThenBy(a => patients[a.PatientId].FirstName))
        {
            var ledger = await LoadLedgerAsync(account.Id, ct);
            var math = ledger.Math;
            var unapplied = ledger.Entries.Where(e => e.Kind == BillingKinds.Payment).Aggregate(AppMoney.Zero, (sum, e) => sum + AppMoney.FromDecimal(BillingLedgerMath.Unapplied(math, ledger.MathAllocations, e.EntryNumber))).Amount;
            var open = BillingLedgerMath.OpenCharges(math, ledger.MathAllocations).Aggregate(AppMoney.Zero, (sum, o) => sum + AppMoney.FromDecimal(o.Open)).Amount;
            rows.Add(new GuarantorAccountView(account.PatientId, NameOf(patients[account.PatientId]), account.PatientId == guarantorPatientId, BillingLedgerMath.Balance(math), open, unapplied));
        }
        var total = rows.Aggregate(AppMoney.Zero, (sum, r) => sum + AppMoney.FromDecimal(r.BalanceOwed)).Amount;
        return new GuarantorSummaryView(guarantorPatientId, NameOf(guarantor), AppMoney.Currency, total, rows);
    }
}
