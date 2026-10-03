using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// STORY-005: reads an encounter or a patient's encounters. Read-only (no tracking, no writes); gating by permission is the controller's job. A finalized encounter
/// is shown exactly as stored, with its addenda beside it, oldest first - an addendum is never merged into the original.
/// </summary>
public class EncounterReader(AlveraDbContext db)
{
    public async Task<EncounterDetail> GetAsync(Guid encounterId, CancellationToken ct)
    {
        var e = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == encounterId, ct)
            ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        var entries = await db.EncounterEntries.AsNoTracking().Where(x => x.EncounterId == encounterId && x.RemovedAtUtc == null).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var marks = await db.EncounterSectionMarks.AsNoTracking().Where(x => x.EncounterId == encounterId).ToListAsync(ct);
        var addenda = await db.EncounterAddenda.AsNoTracking().Where(x => x.EncounterId == encounterId).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var events = await db.EncounterEvents.AsNoTracking().Where(x => x.EncounterId == encounterId).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToListAsync(ct);

        var sections = new List<SectionView>();
        foreach (var kind in EncounterEntryKinds.All)
        {
            var mine = entries.Where(x => x.Kind == kind).Select(ToView).ToList();
            var mark = marks.SingleOrDefault(m => m.Section == kind);
            var status = mine.Count > 0 ? SectionStatuses.Recorded : mark is not null ? SectionStatuses.NoneReported : SectionStatuses.Empty;
            sections.Add(new SectionView(kind, status, mine, mark?.MarkedAtUtc, mark?.MarkedByUserId));
        }
        var missing = sections.Where(s => s.Status == SectionStatuses.Empty).Select(s => s.Kind).ToList();
        return new EncounterDetail(
            e.Id, e.PatientId, e.AppointmentId, e.EncounterAtUtc, e.Status, missing.Count == 0, missing, sections,
            addenda.Select(a => new AddendumView(a.Id, a.Text, a.CreatedAtUtc, a.CreatedByUserId)).ToList(),
            events.Select(v => new EventView(v.EventType, v.ActorUserId, v.OccurredAtUtc, v.Detail)).ToList(),
            e.CreatedAtUtc, e.CreatedByUserId, e.FinalizedAtUtc, e.FinalizedByUserId, Convert.ToBase64String(e.RowVersion));
    }

    /// <summary>A patient's encounters, newest first.</summary>
    public async Task<IReadOnlyList<EncounterSummary>> ListForPatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new ClinicalException("patient_not_found", "That patient was not found.", 404);
        var encounters = await db.Encounters.AsNoTracking().Where(x => x.PatientId == patientId).OrderByDescending(x => x.EncounterAtUtc).ThenByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var ids = encounters.Select(x => x.Id).ToList();
        var entries = await db.EncounterEntries.AsNoTracking().Where(x => ids.Contains(x.EncounterId) && x.RemovedAtUtc == null).Select(x => new { x.EncounterId, x.Kind }).ToListAsync(ct);
        var marks = await db.EncounterSectionMarks.AsNoTracking().Where(x => ids.Contains(x.EncounterId)).Select(x => new { x.EncounterId, x.Section }).ToListAsync(ct);
        var addenda = await db.EncounterAddenda.AsNoTracking().Where(x => ids.Contains(x.EncounterId)).GroupBy(x => x.EncounterId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
        return encounters.Select(e =>
        {
            var addressed = entries.Where(x => x.EncounterId == e.Id).Select(x => x.Kind).Concat(marks.Where(x => x.EncounterId == e.Id).Select(x => x.Section)).Distinct().Count();
            return new EncounterSummary(e.Id, e.AppointmentId, e.EncounterAtUtc, e.Status, addressed == EncounterEntryKinds.All.Count,
                entries.Count(x => x.EncounterId == e.Id), addenda.SingleOrDefault(a => a.Id == e.Id)?.N ?? 0);
        }).ToList();
    }

    private static EntryView ToView(EncounterEntry x) =>
        new(x.Id, x.Kind, x.Name, x.Detail, x.Reaction, x.Severity, x.Dose, x.Frequency, x.CreatedAtUtc, x.CreatedByUserId, x.UpdatedAtUtc);
}
