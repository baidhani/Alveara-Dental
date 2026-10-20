using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// STORY-010: the administrator's document settings (docs/DOCUMENTS.md, "Admin, Document settings"): the categories, the size limit and the blocked file types. The permission is checked at the route; this service
/// keeps the data rules.
///
/// How the promises are kept:
/// - <b>Categories are never deleted.</b> They are renamed, deactivated and reactivated; the code never changes (the database refuses a change or a deletion), the last active category cannot be deactivated, and
///   a name cannot be reused whatever its case.
/// - <b>The size limit stays between 1 MB and 1 GB</b> (the database refuses anything else too) and applies from the next import.
/// - <b>The built-in blocked types are locked</b> (<c>built_in_locked</c>, 409; the database refuses it too). Adding a type that is already blocked, or removing one that is not, changes nothing and logs nothing.
/// - <b>Every change is logged in the same save</b> with the user and time; if the log cannot be written nothing changes (<c>save_failed</c>, 503). A change made from a stale screen is refused
///   (<see cref="ConcurrencyConflictException"/>, 409). The log carries category codes and the new limit, never anything about a patient.
/// </summary>
public class DocumentSettingsService(AlveraDbContext db, IPracticeClock clock, ILogger<DocumentSettingsService>? logger = null)
{
    private const int NameMax = 80;
    private const int CodeMax = 40;

    // ---------- categories ----------

    public async Task<IReadOnlyList<CategoryView>> ListCategoriesAsync(bool includeInactive, CancellationToken ct) =>
        (await db.DocumentCategories.AsNoTracking().Where(c => includeInactive || c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct)).Select(View).ToList();

    public async Task<CategoryView> AddCategoryAsync(string? rawName, Guid actor, CancellationToken ct)
    {
        var name = CleanName(rawName);
        var existing = await db.DocumentCategories.AsNoTracking().ToListAsync(ct);
        if (existing.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) throw NameInUse();

        var category = new DocumentCategory { Id = Guid.NewGuid(), Code = UniqueCode(name, existing), Name = name, SortOrder = (existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder)) + 10, IsActive = true };
        db.DocumentCategories.Add(category);
        AuditService.Record(db, "DocumentCategoryAdded", "DocumentCategory", category.Id, actor, $"Category added (code {category.Code}).");
        try
        {
            await SaveAsync(nameof(DocumentCategory), category.Id, ct);
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.ChangeTracker.Clear();                                                // a simultaneous addition took the name or the code
            throw NameInUse();
        }
        return View(category);
    }

    public async Task<CategoryView> UpdateCategoryAsync(Guid categoryId, CategoryEditInput input, Guid actor, CancellationToken ct)
    {
        var expected = ParseRowVersion(input.RowVersion);
        var category = await db.DocumentCategories.SingleOrDefaultAsync(c => c.Id == categoryId, ct) ?? throw new DocumentException("category_not_found", "That category was not found.", 404);
        db.Entry(category).Property(c => c.RowVersion).OriginalValue = expected;

        var correlation = Guid.NewGuid();
        if (input.Name is not null)
        {
            var name = CleanName(input.Name);
            if (name != category.Name)
            {
                if ((await db.DocumentCategories.AsNoTracking().Where(c => c.Id != categoryId).Select(c => c.Name).ToListAsync(ct)).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) throw NameInUse();
                category.Name = name;
                AuditService.Record(db, "DocumentCategoryRenamed", "DocumentCategory", category.Id, actor, $"Category renamed (code {category.Code}).", correlationId: correlation);
            }
        }
        if (input.IsActive is { } active && active != category.IsActive)
        {
            if (!active && !await db.DocumentCategories.AsNoTracking().AnyAsync(c => c.IsActive && c.Id != categoryId, ct))
                throw new DocumentException("last_active_category", "At least one category must stay in use, so the last one cannot be deactivated.", 409);
            category.IsActive = active;
            AuditService.Record(db, active ? "DocumentCategoryReactivated" : "DocumentCategoryDeactivated", "DocumentCategory", category.Id, actor,
                active ? $"Category reactivated (code {category.Code})." : $"Category deactivated (code {category.Code}).", correlationId: correlation);
        }
        if (db.Entry(category).State == EntityState.Unchanged) return View(category);                       // nothing changed: nothing is saved and nothing is logged

        try
        {
            await SaveAsync(nameof(DocumentCategory), categoryId, ct);
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.ChangeTracker.Clear();
            throw NameInUse();
        }
        return View(category);
    }

    // ---------- the size limit ----------

    public async Task<DocumentSettingsView> GetSettingsAsync(CancellationToken ct) => SettingsView(await db.DocumentSettings.AsNoTracking().SingleAsync(ct));

    public async Task<DocumentSettingsView> SetMaxUploadBytesAsync(long? bytes, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var expected = ParseRowVersion(rowVersion);
        if (bytes is not { } value || DocumentFileRules.ValidateMaxUploadBytes(value) is not null)
            throw Invalid("maxUploadBytes", "out_of_range", $"The size limit must be between {DocumentFileRules.MinUploadLimitBytes / (1024 * 1024)} MB and {DocumentFileRules.MaxUploadLimitBytes / (1024 * 1024)} MB.");

        var settings = await db.DocumentSettings.SingleAsync(ct);
        db.Entry(settings).Property(s => s.RowVersion).OriginalValue = expected;
        if (settings.MaxUploadBytes == value) return SettingsView(settings);                                 // unchanged: nothing is saved and nothing is logged

        settings.MaxUploadBytes = value;
        (settings.UpdatedByUserId, settings.UpdatedAtUtc) = (actor, clock.UtcNow);
        AuditService.Record(db, "DocumentSizeLimitChanged", "DocumentSettings", Guid.Empty, actor, $"Document size limit changed to {value} bytes.");
        await SaveAsync(nameof(DocumentSettings), Guid.Empty, ct);
        return SettingsView(settings);
    }

    // ---------- blocked file types ----------

    public async Task<IReadOnlyList<BlockedExtensionView>> ListBlockedAsync(CancellationToken ct) =>
        (await db.BlockedFileExtensions.AsNoTracking().OrderBy(b => b.Extension).ToListAsync(ct)).Select(b => new BlockedExtensionView(b.Extension, b.IsBuiltIn, b.AddedAtUtc)).ToList();

    public async Task<BlockedExtensionView> AddBlockedAsync(string? extension, Guid actor, CancellationToken ct)
    {
        var normalized = NormalizeExtension(extension);
        if (await FindBlockedAsync(normalized, ct) is { } existing) return ViewOf(existing);

        var row = new BlockedFileExtension { Extension = normalized, IsBuiltIn = false, AddedByUserId = actor, AddedAtUtc = clock.UtcNow };
        db.BlockedFileExtensions.Add(row);
        AuditService.Record(db, "BlockedExtensionAdded", "BlockedFileExtension", Guid.Empty, actor, $"Blocked file type added (.{normalized}).");
        try
        {
            await SaveAsync(nameof(BlockedFileExtension), Guid.Empty, ct);
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.ChangeTracker.Clear();                                                // a simultaneous addition of the same type won: it is blocked, which is what was asked
            return ViewOf(await FindBlockedAsync(normalized, ct) ?? throw SaveFailed(ex));
        }
        return ViewOf(row);
    }

    public async Task RemoveBlockedAsync(string? extension, Guid actor, CancellationToken ct)
    {
        var normalized = NormalizeExtension(extension);
        var row = await db.BlockedFileExtensions.SingleOrDefaultAsync(b => b.Extension == normalized, ct);
        if (row is null) return;                                                      // already not blocked: nothing to do and nothing to log
        if (row.IsBuiltIn) throw new DocumentException("built_in_locked", $"The .{normalized} type is blocked for everyone and cannot be unblocked.", 409);

        db.BlockedFileExtensions.Remove(row);
        AuditService.Record(db, "BlockedExtensionRemoved", "BlockedFileExtension", Guid.Empty, actor, $"Blocked file type removed (.{normalized}).");
        try
        {
            await SaveAsync(nameof(BlockedFileExtension), Guid.Empty, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // a simultaneous removal already deleted it: it is no longer blocked, which is what was asked
        }
    }

    // ---------- helpers ----------

    private Task<BlockedFileExtension?> FindBlockedAsync(string extension, CancellationToken ct) => db.BlockedFileExtensions.AsNoTracking().SingleOrDefaultAsync(b => b.Extension == extension, ct);

    private static BlockedExtensionView ViewOf(BlockedFileExtension b) => new(b.Extension, b.IsBuiltIn, b.AddedAtUtc);

    private static string NormalizeExtension(string? raw)
    {
        var result = DocumentFileRules.NormalizeBlockedExtensionInput(raw);
        return result.IsOk ? result.Extension! : throw Invalid("extension", "invalid", "Enter a file type of 1 to 16 letters or digits, such as swf. Do not include a dot or other punctuation.");
    }

    private static string CleanName(string? raw)
    {
        var name = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0) throw Invalid("name", "required", "Enter a name for the category.");
        if (name.Length > NameMax) throw Invalid("name", "too_long", $"The name can be at most {NameMax} characters.");
        if (name.Any(char.IsControl)) throw Invalid("name", "invalid", "The name cannot contain control characters.");
        if (CodeFromName(name).Length == 0) throw Invalid("name", "invalid", "The name must contain at least one letter or digit.");
        return name;
    }

    private static string CodeFromName(string name)
    {
        var code = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') code.Append(c);
            else if (code.Length > 0 && code[^1] != '_') code.Append('_');
        }
        return code.ToString().Trim('_');
    }

    private static string UniqueCode(string name, IReadOnlyList<DocumentCategory> existing)
    {
        var taken = existing.Select(c => c.Code).ToHashSet();
        var baseCode = CodeFromName(name);
        baseCode = baseCode.Length > CodeMax - 4 ? baseCode[..(CodeMax - 4)].TrimEnd('_') : baseCode;
        if (!taken.Contains(baseCode)) return baseCode;
        for (var n = 2; ; n++)
            if (!taken.Contains($"{baseCode}_{n}")) return $"{baseCode}_{n}";
    }

    private static byte[] ParseRowVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new DocumentException("row_version_required", "The version of what you are changing is required, so a change made by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new DocumentException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400); }
    }

    private async Task SaveAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(entityType, entityId);
        }
        catch (DbUpdateException ex) when (!IsUnique(ex))
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
    }

    private DocumentException SaveFailed(Exception ex)
    {
        logger?.LogWarning("document_settings_not_saved error_class={ErrorClass}", ex.InnerException?.GetType().Name ?? ex.GetType().Name);
        return new DocumentException("save_failed", "The change could not be saved, so nothing was changed. Try again; if it keeps failing, contact support.", 503);
    }

    private static bool IsUnique(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static DocumentException NameInUse() => new("category_exists", "A category with that name already exists.", 409);

    private static DocumentException Invalid(string field, string code, string message) =>
        new(DocumentErrorCodes.MetadataInvalid, "That entry needs correcting. Nothing was saved.", 400, [new DocumentProblem(field, code, message)]);

    private static CategoryView View(DocumentCategory c) => new(c.Id, c.Code, c.Name, c.SortOrder, c.IsActive, Convert.ToBase64String(c.RowVersion));

    private static DocumentSettingsView SettingsView(DocumentSettings s) =>
        new(s.MaxUploadBytes, DocumentFileRules.MinUploadLimitBytes, DocumentFileRules.MaxUploadLimitBytes, Convert.ToBase64String(s.RowVersion));
}
