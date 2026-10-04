using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

public sealed record TemplateSectionView(string Section, bool Required, string? StarterText);

public sealed record TemplateView(Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<TemplateSectionView> Sections, DateTimeOffset? UpdatedAtUtc, string? UpdatedByName, string RowVersion);

/// <summary>
/// ALV-005-C01: the clinical note templates - which note sections a note has, which are required before signing, and optional starter text. Owned by the clinical-documentation
/// domain (its own tables and its own permission, ManageClinicalTemplates), not generic practice configuration.
///
/// A template is deactivated, never deleted (a trigger refuses deletion), so encounters that used it keep their record. Editing a template changes only the encounters that
/// use it from now on: applying one copies its sections and required flags onto the encounter, so a finished or in-progress note never changes under the clinician.
/// A change echoes the template's row version (stale edits are the shared 409), names must be unique, and every change is audited.
/// </summary>
public class NoteTemplateService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<IReadOnlyList<TemplateView>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var templates = await db.NoteTemplates.AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);
        var ids = templates.Select(t => t.Id).ToList();
        var sections = await db.NoteTemplateSections.AsNoTracking().Where(s => ids.Contains(s.TemplateId)).OrderBy(s => s.SortOrder).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, templates.Select(t => t.UpdatedByUserId), ct);
        return templates.Select(t => ToView(t, sections.Where(s => s.TemplateId == t.Id), names)).ToList();
    }

    public async Task<TemplateView> GetAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(true, ct)).SingleOrDefault(t => t.Id == id) ?? throw new ClinicalException("template_not_found", "That template was not found.", 404);

    public async Task<TemplateView> CreateAsync(string? name, string? description, IReadOnlyList<TemplateSectionInput>? sections, Guid actor, CancellationToken ct)
    {
        var (n, d, clean) = EncounterNoteRules.ValidateTemplate(name, description, sections);
        var now = clock.UtcNow;
        var template = new NoteTemplate { Id = Guid.NewGuid(), Name = n, Description = d, IsActive = true, CreatedAtUtc = now, CreatedByUserId = actor, UpdatedAtUtc = now, UpdatedByUserId = actor };
        db.NoteTemplates.Add(template);
        AddSections(template.Id, clean);
        AuditService.Record(db, "ClinicalTemplateCreated", nameof(NoteTemplate), template.Id, actor, $"Note template '{n}' created.");
        await SaveAsync(template.Id, n, ct);
        return await GetAsync(template.Id, ct);
    }

    public async Task<TemplateView> UpdateAsync(Guid id, string? name, string? description, IReadOnlyList<TemplateSectionInput>? sections, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var (n, d, clean) = EncounterNoteRules.ValidateTemplate(name, description, sections);
        var template = await LoadAsync(id, rowVersion, ct);
        var existing = await db.NoteTemplateSections.Where(s => s.TemplateId == id).ToListAsync(ct);
        var unchanged = template.Name == n && template.Description == d && existing.Count == clean.Count
            && clean.All(c => existing.Any(e => e.Section == c.Section && e.Required == c.Required && e.StarterText == c.StarterText));
        if (unchanged) return await GetAsync(id, ct); // nothing to change: no new version, no audit entry

        var now = clock.UtcNow;
        template.Name = n; template.Description = d; template.UpdatedAtUtc = now; template.UpdatedByUserId = actor;
        Reconcile(id, existing, clean);
        AuditService.Record(db, "ClinicalTemplateUpdated", nameof(NoteTemplate), id, actor, $"Note template '{n}' updated.");
        await SaveAsync(id, n, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Takes a template out of use (or back into use). Encounters that used it keep what they copied; setting the state it already has changes nothing.</summary>
    public async Task<TemplateView> SetActiveAsync(Guid id, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var template = await LoadAsync(id, rowVersion, ct);
        if (template.IsActive == active) return await GetAsync(id, ct);
        template.IsActive = active; template.UpdatedAtUtc = clock.UtcNow; template.UpdatedByUserId = actor;
        AuditService.Record(db, active ? "ClinicalTemplateActivated" : "ClinicalTemplateDeactivated", nameof(NoteTemplate), id, actor, $"Note template '{template.Name}' {(active ? "put back in use" : "taken out of use")}.");
        await SaveAsync(id, template.Name, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Brings the stored sections to the new list in place (matched by section), so a re-saved section is an update and never a delete-then-insert of the same key.</summary>
    private void Reconcile(Guid templateId, List<NoteTemplateSection> existing, IReadOnlyList<TemplateSectionInput> wanted)
    {
        foreach (var gone in existing.Where(e => wanted.All(w => w.Section != e.Section))) db.NoteTemplateSections.Remove(gone);
        for (var i = 0; i < wanted.Count; i++)
        {
            var current = existing.SingleOrDefault(e => e.Section == wanted[i].Section);
            if (current is null)
                db.NoteTemplateSections.Add(new NoteTemplateSection { Id = Guid.NewGuid(), TemplateId = templateId, Section = wanted[i].Section!, SortOrder = i, Required = wanted[i].Required, StarterText = wanted[i].StarterText });
            else
            {
                current.SortOrder = i; current.Required = wanted[i].Required; current.StarterText = wanted[i].StarterText;
            }
        }
    }

    private void AddSections(Guid templateId, IReadOnlyList<TemplateSectionInput> sections)
    {
        for (var i = 0; i < sections.Count; i++)
            db.NoteTemplateSections.Add(new NoteTemplateSection { Id = Guid.NewGuid(), TemplateId = templateId, Section = sections[i].Section!, SortOrder = i, Required = sections[i].Required, StarterText = sections[i].StarterText });
    }

    private async Task<NoteTemplate> LoadAsync(Guid id, string? rowVersion, CancellationToken ct)
    {
        var template = await db.NoteTemplates.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new ClinicalException("template_not_found", "That template was not found.", 404);
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ClinicalException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            db.Entry(template).Property(t => t.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new ClinicalException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
        return template;
    }

    private async Task SaveAsync(Guid id, string name, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(NoteTemplate), id, ct);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsUniqueViolation(ex))
        {
            throw new ClinicalException("template_name_taken", $"A template named '{name}' already exists. Choose a different name.", 409, new Dictionary<string, string> { ["name"] = "That name is already used by another template." });
        }
    }

    private static TemplateView ToView(NoteTemplate t, IEnumerable<NoteTemplateSection> sections, IReadOnlyDictionary<Guid, string> names) =>
        new(t.Id, t.Name, t.Description, t.IsActive, sections.Select(s => new TemplateSectionView(s.Section, s.Required, s.StarterText)).ToList(), t.UpdatedAtUtc,
            ClinicalNames.Name(names, t.UpdatedByUserId), Convert.ToBase64String(t.RowVersion));
}
