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
        var notes = await db.EncounterNotes.AsNoTracking().Where(x => x.EncounterId == encounterId).ToListAsync(ct);
        var vitals = await db.EncounterVitals.AsNoTracking().Where(x => x.EncounterId == encounterId).OrderBy(x => x.MeasuredAtUtc).ThenBy(x => x.CreatedAtUtc).ToListAsync(ct);
        var chart = await new EncounterChartSections(db).LoadAsync(encounterId, e.PatientId, ct); // ALV-N022 A1
        var names = await ClinicalNames.ResolveAsync(db, notes.Select(n => n.UpdatedByUserId).Concat(vitals.SelectMany(v => new[] { v.CreatedByUserId, v.VoidedByUserId }))
            .Concat(addenda.Select(a => a.CreatedByUserId)).Append(e.SignedByUserId).Append(e.FinalizedByUserId)
            .Concat(chart.LiveItems.SelectMany(i => new[] { i.CreatedByUserId, i.UpdatedByUserId })).Concat(chart.VersionsHere.Select(v => v.ActorUserId)).Concat(events.Select(v => v.ActorUserId)), ct);

        var sections = new List<SectionView>();
        foreach (var kind in EncounterEntryKinds.All)
        {
            var mine = entries.Where(x => x.Kind == kind).Select(ToView).ToList();
            var mark = marks.SingleOrDefault(m => m.Section == kind);
            if (EncounterChartSections.OnChart(kind)) { sections.Add(EncounterChartSections.Build(kind, chart, mine, mark, names)); continue; }
            var status = mine.Count > 0 ? SectionStatuses.Recorded : mark is not null ? SectionStatuses.NoneReported : SectionStatuses.Empty;
            sections.Add(new SectionView(kind, status, mine, mark?.MarkedAtUtc, mark?.MarkedByUserId));
        }
        var missing = sections.Where(s => s.Status == SectionStatuses.Empty).Select(s => s.Kind).ToList();
        var ordered = NoteSections.All.Select(s => notes.SingleOrDefault(n => n.Section == s)).Where(n => n is not null).Select(n => n!).ToList();
        var noteViews = ordered.Select(n => new NoteView(n.Section, n.Body, n.Required, n.UpdatedAtUtc, ClinicalNames.Name(names, n.UpdatedByUserId))).ToList();
        // a required note is unwritten when it is blank or still exactly the template's starter text
        var missingNotes = ordered.Where(n => n.Required && (string.IsNullOrWhiteSpace(n.Body) || (n.StarterText is not null && n.Body.Trim() == n.StarterText.Trim()))).Select(n => n.Section).ToList();
        return new EncounterDetail(
            e.Id, e.PatientId, e.AppointmentId, e.EncounterAtUtc, e.Status, missing.Count == 0, missing, sections,
            addenda.Select(a => new AddendumView(a.Id, a.Text, a.CreatedAtUtc, a.CreatedByUserId, a.Section, ClinicalNames.Name(names, a.CreatedByUserId))).ToList(),
            events.Select(v => new EventView(v.EventType, v.ActorUserId, v.OccurredAtUtc, v.Detail, ClinicalNames.Name(names, v.ActorUserId))).ToList(),   // ALV-N025 (F-087)
            e.CreatedAtUtc, e.CreatedByUserId, e.FinalizedAtUtc, e.FinalizedByUserId, Convert.ToBase64String(e.RowVersion),
            noteViews, vitals.Select(v => ToView(v, names)).ToList(), e.TemplateId, e.TemplateName, missingNotes, missing.Count == 0 && missingNotes.Count == 0,
            e.SignedAtUtc is not null, e.SignedAtUtc, ClinicalNames.Name(names, e.SignedByUserId), ClinicalNames.Name(names, e.FinalizedByUserId));
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
        // ALV-N022 A1: chart changes and statements made in each encounter count as documentation of that encounter, read the same way as the encounter detail
        var versions = await db.ClinicalRecordItemVersions.AsNoTracking().Where(v => v.EncounterId != null && ids.Contains(v.EncounterId.Value)).Select(v => new { EncounterId = v.EncounterId!.Value, v.Kind }).ToListAsync(ct);
        var statements = await db.ClinicalRecordEvents.AsNoTracking().Where(x => x.EncounterId != null && ids.Contains(x.EncounterId.Value) && (x.EventType == "SectionReviewed" || x.EventType == "ReviewCleared"))
            .OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        return encounters.Select(e =>
        {
            var addressed = EncounterEntryKinds.All.Count(kind =>
            {
                var legacyEntries = entries.Any(x => x.EncounterId == e.Id && x.Kind == kind);
                var legacyMark = marks.Any(x => x.EncounterId == e.Id && x.Section == kind);
                if (!EncounterChartSections.OnChart(kind)) return legacyEntries || legacyMark;
                var status = EncounterChartSections.Status(legacyEntries, legacyMark, versions.Any(v => v.EncounterId == e.Id && v.Kind == kind),
                    EncounterChartSections.LastStatement(statements.Where(x => x.EncounterId == e.Id && x.Section == kind)));
                return status != SectionStatuses.Empty;
            });
            return new EncounterSummary(e.Id, e.AppointmentId, e.EncounterAtUtc, e.Status, addressed == EncounterEntryKinds.All.Count,
                entries.Count(x => x.EncounterId == e.Id) + versions.Count(v => v.EncounterId == e.Id), addenda.SingleOrDefault(a => a.Id == e.Id)?.N ?? 0);
        }).ToList();
    }

    private static VitalsView ToView(EncounterVitals v, IReadOnlyDictionary<Guid, string> names) =>
        new(v.Id, v.MeasuredAtUtc, v.SystolicMmHg, v.DiastolicMmHg, v.PulseBpm, v.RespirationsPerMinute, v.TemperatureC, v.OxygenSaturationPercent, v.WeightKg, v.HeightCm, v.Note,
            v.CreatedAtUtc, ClinicalNames.Name(names, v.CreatedByUserId), v.VoidedAtUtc is not null, v.VoidedAtUtc, ClinicalNames.Name(names, v.VoidedByUserId), v.VoidReason);

    private static EntryView ToView(EncounterEntry x) =>
        new(x.Id, x.Kind, x.Name, x.Detail, x.Reaction, x.Severity, x.Dose, x.Frequency, x.CreatedAtUtc, x.CreatedByUserId, x.UpdatedAtUtc);
}
