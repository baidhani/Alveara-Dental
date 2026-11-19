using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Billing;

/// <summary>One person who may be recorded as having paid a patient's account, and how they are related to it.</summary>
public sealed record PayerChoice(Guid PatientId, string Name, string Relationship);

/// <summary>Who may be recorded as the payer of this patient's payments, and who is chosen by default (the party responsible for the account now).</summary>
public sealed record PayerChoicesView(Guid PatientId, Guid DefaultPayerId, IReadOnlyList<PayerChoice> Payers);

/// <summary>
/// ALV-N024 (PD-18, OQ-1; briefing revision 2 S5; approved by BRIEFING-R02): who may pay. The set is bounded to established relationships - the patient, the patient's registered guarantor, the members of
/// the patient's recorded household, and anyone ever recorded as responsible for the account (kept so earlier payments stay valid). "Verified" means recorded through the audited patient-record
/// workflow (OQ-1); there is no free search for any patient. Only active patients are offered (the patient always is). Payments and corrections are refused (<c>payer_not_eligible</c>) outside the set;
/// the database rule 51142 enforces the same set for writes that bypass this service. The default payer is the party responsible for the account now (F-205).
/// </summary>
public partial class BillingService
{
    private const string PayerRefusal = "Only the patient, their guarantor, a member of their household or someone recorded as responsible for this account can be recorded as having paid it.";

    public async Task<PayerChoicesView> GetPayerChoicesAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var payers = await EligiblePayersAsync(patientId, ct);
        var responsible = (await GetResponsibilityAsync(patientId, ct)).ResponsiblePatientId;
        return new PayerChoicesView(patientId, payers.Any(p => p.PatientId == responsible) ? responsible : patientId, payers);
    }

    private async Task<IReadOnlyList<PayerChoice>> EligiblePayersAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);
        var accountId = await db.BillingAccounts.AsNoTracking().Where(a => a.PatientId == patientId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
        var recorded = accountId is Guid id ? await db.BillingResponsibilities.AsNoTracking().Where(r => r.AccountId == id).Select(r => r.ResponsiblePatientId).Distinct().ToListAsync(ct) : [];
        var household = patient.HouseholdId is Guid h
            ? await db.Patients.AsNoTracking().Where(p => p.HouseholdId == h && p.Id != patientId).Select(p => new { p.Id, p.HouseholdRelationship }).ToListAsync(ct) : [];

        var reasons = new Dictionary<Guid, List<string>>();
        void Add(Guid who, string why) { if (who != patientId) (reasons.TryGetValue(who, out var list) ? list : reasons[who] = []).Add(why); }
        if (patient.GuarantorPatientId is Guid g) Add(g, "registered guarantor");
        foreach (var member in household) Add(member.Id, member.HouseholdRelationship is { } rel ? $"household member ({rel.ToLowerInvariant()})" : "household member");
        foreach (var r in recorded) Add(r, "recorded as responsible");

        var ids = reasons.Keys.ToList();
        var people = await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IsActive).ToListAsync(ct);
        var others = people.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Select(p => new PayerChoice(p.Id, NameOf(p), string.Join(", ", reasons[p.Id].Distinct())));
        return [new PayerChoice(patientId, NameOf(patient), "the patient"), .. others];
    }

    /// <summary>The payer to record (null: the patient), or a refusal when the person is outside the set.</summary>
    private async Task<Guid?> EligiblePayerOrRefuseAsync(Guid patientId, Guid? payer, CancellationToken ct)
    {
        if (payer is not Guid who || who == patientId) return null;
        if ((await EligiblePayersAsync(patientId, ct)).Any(p => p.PatientId == who)) return who;
        throw new BillingException("payer_not_eligible", PayerRefusal, 400, new Dictionary<string, string> { ["paidBy"] = PayerRefusal });
    }
}
