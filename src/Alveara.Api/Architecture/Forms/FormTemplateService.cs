using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Forms;

public record TemplateVersionView(Guid Id, int VersionNumber, string Title, string Body, IReadOnlyList<FormFieldDefinition> Fields, string ContentHash,
    string? ChangeNote, DateTimeOffset CreatedAtUtc, bool IntegrityVerified);

public record TemplateView(Guid Id, string Key, string Category, bool IsActive, string RowVersion, TemplateVersionView? Current, int VersionCount, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);

public record TemplateDetail(TemplateView Template, IReadOnlyList<TemplateVersionView> Versions);

public record TemplateContent(string? Title, string? Body, IReadOnlyList<FormFieldDefinition>? Fields, string? ChangeNote);

/// <summary>
/// ALV-N010: template administration. Creating a template publishes version 1; every edit publishes a NEW version (the previous
/// one is never touched), guarded by the template's row version so two administrators cannot silently overwrite one another.
/// Re-saving unchanged content publishes nothing, so a repeated submit is harmless. Audit entries name the template key and
/// version only.
/// </summary>
public class FormTemplateService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<TemplateDetail> CreateAsync(string? key, string? category, TemplateContent content, Guid actor, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var k = FormDefinition.NormalizeKey(key, errors);
        if (!FormCategories.IsValid(category)) errors["category"] = "Choose privacy, financial, general consent or treatment.";
        var (title, body, fields) = Validate(content, errors);
        if (errors.Count > 0) throw new FormException("validation_failed", "Some fields need attention.", 400, errors);

        var now = clock.UtcNow;
        var template = new FormTemplate { Id = Guid.NewGuid(), Key = k, Category = category!, CreatedAtUtc = now, CreatedByUserId = actor };
        var version = NewVersion(template.Id, 1, title, body, fields, content.ChangeNote, now, actor);
        template.CurrentVersionId = version.Id; // not a foreign key, so the template and its first version are one atomic save
        db.FormTemplates.Add(template);
        db.FormTemplateVersions.Add(version);
        AuditService.Record(db, FormAuditEvents.TemplateCreated, nameof(FormTemplate), template.Id, actor, $"Form template '{k}' created ({category}); version 1 published.");
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(FormTemplate), template.Id, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new FormException("template_key_taken", $"A template with the key '{k}' already exists.", 409);
        }
        return await GetAsync(template.Id, ct);
    }

    /// <summary>Publishes a new version. If the content is identical to the current version nothing is published and the current state is returned.</summary>
    public async Task<TemplateDetail> PublishVersionAsync(Guid id, TemplateContent content, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var template = await db.FormTemplates.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound();
        FormWrite.ApplyExpectedVersion(db, template, rowVersion);
        var errors = new Dictionary<string, string>();
        var (title, body, fields) = Validate(content, errors);
        if (errors.Count > 0) throw new FormException("validation_failed", "Some fields need attention.", 400, errors);

        var fieldsJson = FormDefinition.SerializeFields(fields);
        var hash = FormDefinition.ContentHash(title, body, fieldsJson);
        var current = await db.FormTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == template.CurrentVersionId, ct);
        if (current.ContentHash == hash) { db.ChangeTracker.Clear(); return await GetAsync(id, ct); }

        var number = await db.FormTemplateVersions.Where(v => v.TemplateId == id).MaxAsync(v => v.VersionNumber, ct) + 1;
        var now = clock.UtcNow;
        var version = NewVersion(id, number, title, body, fields, content.ChangeNote, now, actor);
        db.FormTemplateVersions.Add(version);
        template.CurrentVersionId = version.Id;
        template.UpdatedAtUtc = now; template.UpdatedByUserId = actor;
        AuditService.Record(db, FormAuditEvents.TemplateVersionPublished, nameof(FormTemplate), id, actor, $"Form template '{template.Key}' version {number} published.");
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(FormTemplate), id, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException(nameof(FormTemplate), id);
        }
        return await GetAsync(id, ct);
    }

    public async Task<TemplateDetail> SetActiveAsync(Guid id, bool isActive, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var template = await db.FormTemplates.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound();
        FormWrite.ApplyExpectedVersion(db, template, rowVersion);
        if (template.IsActive == isActive) { db.ChangeTracker.Clear(); return await GetAsync(id, ct); }
        template.IsActive = isActive;
        template.UpdatedAtUtc = clock.UtcNow; template.UpdatedByUserId = actor;
        AuditService.Record(db, FormAuditEvents.TemplateStatusChanged, nameof(FormTemplate), id, actor,
            $"Form template '{template.Key}' {(isActive ? "reactivated" : "inactivated")}.");
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(FormTemplate), id, ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<TemplateView>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var templates = await db.FormTemplates.AsNoTracking().Where(t => includeInactive || t.IsActive).OrderBy(t => t.Category).ThenBy(t => t.Key).ToListAsync(ct);
        var views = new List<TemplateView>();
        foreach (var t in templates) views.Add(await ViewAsync(t, ct));
        return views;
    }

    public async Task<TemplateDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var template = await db.FormTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound();
        var versions = await db.FormTemplateVersions.AsNoTracking().Where(v => v.TemplateId == id).OrderByDescending(v => v.VersionNumber).ToListAsync(ct);
        return new TemplateDetail(await ViewAsync(template, ct), versions.Select(ToView).ToList());
    }

    internal static TemplateVersionView ToView(FormTemplateVersion v) =>
        new(v.Id, v.VersionNumber, v.Title, v.Body, FormDefinition.ParseFields(v.FieldsJson), v.ContentHash, v.ChangeNote, v.CreatedAtUtc, FormDefinition.VerifyIntegrity(v));

    private async Task<TemplateView> ViewAsync(FormTemplate t, CancellationToken ct)
    {
        var current = t.CurrentVersionId is null ? null : await db.FormTemplateVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == t.CurrentVersionId, ct);
        var count = await db.FormTemplateVersions.CountAsync(v => v.TemplateId == t.Id, ct);
        return new TemplateView(t.Id, t.Key, t.Category, t.IsActive, Convert.ToBase64String(t.RowVersion), current is null ? null : ToView(current), count, t.CreatedAtUtc, t.UpdatedAtUtc);
    }

    private static (string Title, string Body, IReadOnlyList<FormFieldDefinition> Fields) Validate(TemplateContent c, IDictionary<string, string> errors) =>
        (FormDefinition.NormalizeText(c.Title, FormDefinition.MaxTitle, "title", "Title", errors),
         FormDefinition.NormalizeText(c.Body, FormDefinition.MaxBody, "body", "Form text", errors, multiline: true),
         FormDefinition.NormalizeFields(c.Fields, errors));

    private static FormTemplateVersion NewVersion(Guid templateId, int number, string title, string body, IReadOnlyList<FormFieldDefinition> fields, string? note, DateTimeOffset now, Guid actor)
    {
        var fieldsJson = FormDefinition.SerializeFields(fields);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return new FormTemplateVersion
        {
            Id = Guid.NewGuid(), TemplateId = templateId, VersionNumber = number, Title = title, Body = body, FieldsJson = fieldsJson,
            ContentHash = FormDefinition.ContentHash(title, body, fieldsJson), ChangeNote = trimmed is { Length: > 400 } ? trimmed[..400] : trimmed,
            CreatedAtUtc = now, CreatedByUserId = actor,
        };
    }

    private static FormException NotFound() => new("template_not_found", "That form template was not found.", 404);
}
