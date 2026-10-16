using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// ALV-016-C01: reads finalized prescriptions with what finalizing added (the details), their events and the status the events give. A prescription saved by STORY-016 has neither details nor events: it reads as Active
/// with its instructions, quantity and refills absent, never invented. Read-only.
/// </summary>
public class PrescriptionRecordReader(AlveraDbContext db)
{
    public async Task<PrescriptionRecord> GetAsync(Guid prescriptionId, CancellationToken ct) =>
        (await ReadAsync(await db.Prescriptions.AsNoTracking().Where(p => p.Id == prescriptionId).ToListAsync(ct), ct)).SingleOrDefault()
        ?? throw new PrescriptionException("prescription_not_found", "That prescription was not found.", 404);

    public async Task<IReadOnlyList<PrescriptionRecord>> ListAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PrescriptionException("patient_not_found", "That patient was not found.", 404);
        return await ReadAsync(await db.Prescriptions.AsNoTracking().Where(p => p.PatientId == patientId).OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).ToListAsync(ct), ct);
    }

    private async Task<List<PrescriptionRecord>> ReadAsync(List<Prescription> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var details = await db.PrescriptionDetails.AsNoTracking().Where(d => ids.Contains(d.PrescriptionId)).ToDictionaryAsync(d => d.PrescriptionId, ct);
        var events = (await db.PrescriptionEvents.AsNoTracking().Where(e => ids.Contains(e.PrescriptionId)).ToListAsync(ct))
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.EventType == PrescriptionEventTypes.Finalized ? 0 : 1).GroupBy(e => e.PrescriptionId).ToDictionary(g => g.Key, g => g.ToList());
        var names = await ClinicalNames.ResolveAsync(db, rows.Select(r => (Guid?)r.PrescriberUserId).Concat(events.Values.SelectMany(v => v).Select(e => (Guid?)e.ActorUserId)), ct);
        // the prescription that replaced this one is the one whose detail says it supersedes it
        var replacedBy = await db.PrescriptionDetails.AsNoTracking().Where(d => d.SupersedesPrescriptionId != null && ids.Contains(d.SupersedesPrescriptionId.Value))
            .ToDictionaryAsync(d => d.SupersedesPrescriptionId!.Value, d => d.PrescriptionId, ct);

        return rows.Select(r =>
        {
            details.TryGetValue(r.Id, out var d);
            var evs = events.GetValueOrDefault(r.Id) ?? [];
            return new PrescriptionRecord(r.Id, r.PatientId, r.Medication, r.DoseValue, r.DoseUnit, r.Frequency, d?.Instructions, d?.QuantityValue, d?.QuantityUnit, d?.Refills,
                r.AllergyOutcome, r.AllergyListState, r.AllergyMatches, r.ReviewNote, ClinicalNames.Name(names, r.PrescriberUserId), r.CreatedAtUtc, PrescriptionStatuses.From(evs.Select(e => e.EventType)),
                d?.SupersedesPrescriptionId, replacedBy.TryGetValue(r.Id, out var successor) ? successor : null, d is not null,
                evs.Select(e => new PrescriptionEventView(e.EventType, e.Reason, ClinicalNames.Name(names, e.ActorUserId), e.OccurredAtUtc, e.SupersededByPrescriptionId)).ToList());
        }).ToList();
    }
}
