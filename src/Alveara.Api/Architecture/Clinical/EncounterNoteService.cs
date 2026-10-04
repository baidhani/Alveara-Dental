using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-005-C01: the free-text notes of an encounter (SOAP, progress and treatment notes) and the note template applied to it.
///
/// - Notes are saved as they are written (the screen autosaves) and change only while the encounter is an unsigned draft; the service refuses anything else and, once the
///   encounter is finalized, database triggers refuse it independently.
/// - Every save echoes the encounter's row version, so a stale tab can never overwrite a newer note (the shared 409 concurrency conflict).
/// - Applying a template copies what it says at that moment (which sections, which are required, the starter text); editing the template afterwards does not change an
///   encounter that already used it. A template can be applied once per encounter; text already written is never overwritten by starter text, and a required note that still shows only the starter text counts as not yet written.
/// - Repeats are quiet: saving the text a note already holds, or applying the template the encounter already has, changes nothing and moves no version.
/// - History and audit text name the section only, never the note's content.
/// </summary>
public class EncounterNoteService(AlveraDbContext db, IPracticeClock clock)
{
    private EncounterReader Reader => new(db);

    public async Task<EncounterDetail> SaveNoteAsync(Guid encounterId, string? section, string? body, string? rowVersion, Guid actor, CancellationToken ct)
    {
        section = EncounterNoteRules.RequireNoteSection(section);
        body = EncounterNoteRules.CleanNoteBody(body);
        var encounter = await ClinicalWrite.LoadOpenDraftAsync(db, encounterId, rowVersion, ct);

        var note = await db.EncounterNotes.SingleOrDefaultAsync(n => n.EncounterId == encounterId && n.Section == section, ct);
        if (note is null && body.Length == 0) return await Reader.GetAsync(encounterId, ct); // nothing written, nothing to keep
        if (note is not null && note.Body == body) return await Reader.GetAsync(encounterId, ct); // a retried save: already there

        var now = clock.UtcNow;
        if (note is null)
            db.EncounterNotes.Add(new EncounterNote { Id = Guid.NewGuid(), EncounterId = encounterId, Section = section, Body = body, CreatedAtUtc = now, CreatedByUserId = actor, UpdatedAtUtc = now, UpdatedByUserId = actor });
        else
        {
            note.Body = body; note.UpdatedAtUtc = now; note.UpdatedByUserId = actor;
        }
        ClinicalWrite.Touch(encounter, actor, now);
        ClinicalWrite.Stage(db, encounter, EncounterEventTypes.NoteSaved, "EncounterNoteSaved", actor, now, $"{NoteSections.Label(section)} saved.");
        return await SaveAsync(encounterId, ct);
    }

    public async Task<EncounterDetail> ApplyTemplateAsync(Guid encounterId, Guid templateId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await ClinicalWrite.LoadOpenDraftAsync(db, encounterId, rowVersion, ct);
        var template = await db.NoteTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == templateId, ct) ?? throw new ClinicalException("template_not_found", "That template was not found.", 404);
        if (encounter.TemplateId == templateId) return await Reader.GetAsync(encounterId, ct); // already applied
        if (!template.IsActive) throw new ClinicalException("template_inactive", "That template is no longer in use.", 409);
        if (encounter.TemplateId is not null)
            throw new ClinicalException("template_already_applied", "A template was already applied to this note. A note uses one template.", 409);

        var sections = await db.NoteTemplateSections.AsNoTracking().Where(s => s.TemplateId == templateId).OrderBy(s => s.SortOrder).ToListAsync(ct);
        var notes = await db.EncounterNotes.Where(n => n.EncounterId == encounterId).ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var s in sections)
        {
            var note = notes.SingleOrDefault(n => n.Section == s.Section);
            if (note is null)
                db.EncounterNotes.Add(new EncounterNote { Id = Guid.NewGuid(), EncounterId = encounterId, Section = s.Section, Body = s.StarterText ?? "", Required = s.Required, StarterText = s.StarterText, CreatedAtUtc = now, CreatedByUserId = actor, UpdatedAtUtc = now, UpdatedByUserId = actor });
            else
            {
                note.Required = s.Required; note.StarterText = s.StarterText;
                if (note.Body.Length == 0 && s.StarterText is not null) note.Body = s.StarterText; // written text is never replaced by starter text
                note.UpdatedAtUtc = now; note.UpdatedByUserId = actor;
            }
        }
        encounter.TemplateId = templateId;
        encounter.TemplateName = template.Name;
        ClinicalWrite.Touch(encounter, actor, now);
        ClinicalWrite.Stage(db, encounter, EncounterEventTypes.TemplateApplied, "EncounterTemplateApplied", actor, now, $"Template '{template.Name}' applied.");
        return await SaveAsync(encounterId, ct);
    }

    private async Task<EncounterDetail> SaveAsync(Guid encounterId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Encounter), encounterId, ct);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsClinicalTrigger(ex))
        {
            throw new ClinicalException("encounter_finalized", "This encounter was finalized while you were editing it. Reload it; add an addendum to extend it.", 409);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsUniqueViolation(ex))
        {
            throw new ConcurrencyConflictException(nameof(Encounter), encounterId); // two saves created the same note at once
        }
        return await Reader.GetAsync(encounterId, ct);
    }
}
