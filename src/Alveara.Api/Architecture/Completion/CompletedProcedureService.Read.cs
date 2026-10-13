using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>The read side: one completed procedure, or a patient's, as it stands now. Current values come from the latest correction of each field, else the original; the original row and every amendment stay readable.</summary>
public partial class CompletedProcedureService
{
    /// <summary>A patient's completed procedures, newest performed first. Reads need no write permission and change nothing.</summary>
    public async Task<IReadOnlyList<CompletedProcedureDetail>> ListAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new TreatmentPlanException("patient_not_found", "That patient was not found.", 404);
        var rows = await db.CompletedProcedures.AsNoTracking().Where(c => c.PatientId == patientId).ToListAsync(ct);
        return (await DetailsAsync(rows, ct)).OrderByDescending(d => d.PerformedAtUtc).ThenBy(d => d.ItemNumber).ToList();
    }

    public async Task<CompletedProcedureDetail> DetailAsync(Guid completedProcedureId, CancellationToken ct)
    {
        var row = await db.CompletedProcedures.AsNoTracking().SingleOrDefaultAsync(c => c.Id == completedProcedureId, ct) ?? throw NotFound();
        return (await DetailsAsync([row], ct)).Single();
    }

    private async Task<IReadOnlyList<CompletedProcedureDetail>> DetailsAsync(IReadOnlyList<CompletedProcedure> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();
        var itemIds = rows.Select(r => r.PlanItemId).ToList();
        var amendments = (await db.CompletedProcedureAmendments.AsNoTracking().Where(a => ids.Contains(a.CompletedProcedureId)).OrderBy(a => a.AmendmentNumber).ToListAsync(ct)).ToLookup(a => a.CompletedProcedureId);
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var planIds = rows.Select(r => r.PlanId).Distinct().ToList();
        var titles = await db.TreatmentPlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var versionIds = rows.Select(r => r.ProcedureVersionId).Distinct().ToList();
        var versions = await db.ProcedureVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Description, ct);
        var definitions = items.Values.Select(i => i.ProcedureId).Distinct().ToList();
        var codes = await db.ProcedureDefinitions.AsNoTracking().Where(d => definitions.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Code, ct);

        var providerIds = rows.Select(r => r.ProviderProfileId).Concat(amendments.SelectMany(g => g).Select(a => a.ProviderProfileId).OfType<Guid>()).Distinct().ToList();
        var providers = await (from p in db.ProviderProfiles.AsNoTracking() join s in db.StaffProfiles.AsNoTracking() on p.StaffProfileId equals s.Id where providerIds.Contains(p.Id) select new { p.Id, s.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var encounterIds = rows.Select(r => r.EncounterId).Concat(amendments.SelectMany(g => g).Select(a => a.EncounterId).OfType<Guid>()).Distinct().ToList();
        var encounters = await db.Encounters.AsNoTracking().Where(e => encounterIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.EncounterAtUtc, ct);
        var people = await ClinicalNames.ResolveAsync(db, rows.Select(r => (Guid?)r.FinalizedByUserId).Concat(amendments.SelectMany(g => g).Select(a => (Guid?)a.AmendedByUserId)), ct);

        var setIds = rows.Select(r => r.SetId).Distinct().ToList();
        var sets = await db.CompletionSets.AsNoTracking().Where(s => setIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => new { s.CorrelationId, s.AdoptionReason }, ct);
        var entries = await db.BillingEntries.AsNoTracking().Where(e => e.PlanItemId != null && itemIds.Contains(e.PlanItemId.Value)).ToDictionaryAsync(e => e.PlanItemId!.Value, ct);

        string Provider(Guid id) => providers.GetValueOrDefault(id) ?? ClinicalNames.Fallback;
        return rows.Select(r =>
        {
            var item = items[r.PlanItemId];
            var made = amendments[r.Id].ToList();
            var providerId = made.LastOrDefault(a => a.ProviderProfileId is not null)?.ProviderProfileId ?? r.ProviderProfileId;
            var performed = made.LastOrDefault(a => a.PerformedAtUtc is not null)?.PerformedAtUtc ?? r.PerformedAtUtc;
            var encounterId = made.LastOrDefault(a => a.EncounterId is not null)?.EncounterId ?? r.EncounterId;
            ChargeEffect? charge = item.Fee <= 0m || !entries.TryGetValue(item.Id, out var entry)
                ? new ChargeEffect(ChargeOutcomes.None, null, null)
                : new ChargeEffect(entry.CorrelationId == sets[r.SetId].CorrelationId ? ChargeOutcomes.Created : ChargeOutcomes.Existing, entry.EntryNumber, entry.Amount);
            var views = made.Select(a => new AmendmentView(a.AmendmentNumber, a.Reason, a.ProviderProfileId, a.ProviderProfileId is { } p ? Provider(p) : null, a.PerformedAtUtc, a.EncounterId,
                a.EncounterId is { } e ? encounters.GetValueOrDefault(e) : null, ClinicalNames.Name(people, a.AmendedByUserId) ?? ClinicalNames.Fallback, a.AmendedAtUtc)).ToList();
            return new CompletedProcedureDetail(r.Id, r.SetId, r.PatientId, r.PlanId, titles[r.PlanId], r.PlanItemId, item.ItemNumber, r.PlanRevisionNumber, codes[item.ProcedureId], versions[r.ProcedureVersionId],
                r.ToothKey, r.Surface, encounterId, encounters.GetValueOrDefault(encounterId), providerId, Provider(providerId), performed, r.EncounterId, r.ProviderProfileId, r.PerformedAtUtc,
                r.ChartOutcome, r.ToothFindingId, charge, ClinicalNames.Name(people, r.FinalizedByUserId) ?? ClinicalNames.Fallback, r.FinalizedAtUtc, made.Count == 0 ? 0 : made[^1].AmendmentNumber, views, sets[r.SetId].AdoptionReason is not null, sets[r.SetId].AdoptionReason);
        }).ToList();
    }
}
