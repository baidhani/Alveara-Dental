using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// STORY-010: reading a patient's library and opening a file (docs/DOCUMENTS.md, "What happens when a file is opened"). Listing and reading write nothing. Signed forms are listed through the existing
/// <see cref="PatientFormReader.SignedDocumentsAsync"/> seam, as read-only entries that point at the immutable snapshot; they are never copied into Documents and nothing here can change one.
///
/// Opening is logged before the file is handed over: the history row and the log entry are written in one transaction, and if they cannot be written the file is not opened (<c>save_failed</c>, 503). A stored file that
/// has gone missing is reported (<c>file_missing</c>) and nothing is logged as opened. Not handled: the file's integrity is not re-hashed on every open (a 1 GB file would make every open slow); the backup
/// verification covers corruption.
/// </summary>
public class DocumentLibraryService(AlveraDbContext db, PatientFormReader forms, IBlobStorage blobs, IPracticeClock clock, ILogger<DocumentLibraryService>? logger = null)
{
    private static readonly HashSet<string> InlineTypes = ["pdf", "png", "jpeg"];

    /// <summary>The largest picture served as a thumbnail; a larger one shows an icon and is opened to be seen.</summary>
    public const long ThumbnailMaxBytes = 5 * 1024 * 1024;

    /// <summary>Refuses an unknown patient (404), so a route can tell "no documents yet" from "no such patient".</summary>
    public async Task EnsurePatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new DocumentException("patient_not_found", "That patient was not found.", 404);
    }

    /// <summary>
    /// A patient's library, narrowed by <paramref name="filter"/> (category, encounter, tooth or region, date range, status, a word of the file name or description). The filters combine; a wrong value is refused
    /// (<c>metadata_invalid</c>, naming the field) rather than ignored; text is matched literally. Inactive documents are left out unless the status asks for them. Signed forms are listed only when nothing narrows to a
    /// document's own properties; the date range and the text apply to them as well (by signing date and form title).
    /// </summary>
    public async Task<IReadOnlyList<LibraryEntry>> ListAsync(Guid patientId, DocumentLibraryFilter filter, CancellationToken ct)
    {
        var f = CheckFilter(filter);
        var query = db.Documents.AsNoTracking().Where(d => d.PatientId == patientId);
        if (f.CategoryId is { } category) query = query.Where(d => d.CategoryId == category);
        if (f.EncounterId is { } encounter) query = query.Where(d => d.EncounterId == encounter);
        if (f.ToothKey is { } tooth) query = query.Where(d => d.ToothKey == tooth);
        if (f.RegionKey is { } region) query = query.Where(d => d.RegionKey == region);
        if (f.From is { } from) query = query.Where(d => d.DocumentDate >= from);
        if (f.To is { } to) query = query.Where(d => d.DocumentDate <= to);
        if (f.Status != DocumentLibraryFilter.StatusAll) query = query.Where(d => d.Status == f.Status);
        if (f.Text is { } text)
        {
            var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
            query = query.Where(d => EF.Functions.Like(d.OriginalFileName, pattern, "\\") || (d.Description != null && EF.Functions.Like(d.Description, pattern, "\\")));
        }

        var rows = await query.ToListAsync(ct);
        var entries = (await DocumentMapper.ViewsAsync(db, rows, ct))
            .Select(v => new LibraryEntry(LibraryEntry.DocumentKind, v.Id, null, v.OriginalFileName, v.CategoryName, v.DocumentDate, v.DetectedType, v.SizeBytes, v.ImportedByName, v.ImportedAtUtc, false,
                v.DuplicateOfDocumentId is not null, v, v.Status == DocumentStatuses.Inactive)).ToList();

        var narrowsToDocuments = f.CategoryId is not null || f.EncounterId is not null || f.ToothKey is not null || f.RegionKey is not null || f.Status == DocumentLibraryFilter.StatusInactive;
        if (!narrowsToDocuments)
            foreach (var form in await forms.SignedDocumentsAsync(patientId, ct))
            {
                var date = DateOnly.FromDateTime(clock.ToPracticeLocal(form.SignedAtUtc).DateTime);
                if ((f.From is { } start && date < start) || (f.To is { } end && date > end)) continue;
                if (f.Text is { } word && !form.Title.Contains(word, StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(new LibraryEntry(LibraryEntry.SignedFormKind, form.SnapshotId, form.PatientFormId, form.Title, LibraryEntry.SignedFormCategory, date, "form", null, null, form.SignedAtUtc, form.IsVoided, false, null));
            }

        return entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.AtUtc).ThenBy(e => e.Id).ToList();
    }

    /// <summary>The patient's library. Inactive documents are left out unless <paramref name="includeInactive"/> is set (they are never deleted, only hidden).</summary>
    public Task<IReadOnlyList<LibraryEntry>> ListAsync(Guid patientId, Guid? categoryId, CancellationToken ct, bool includeInactive = false) =>
        ListAsync(patientId, new DocumentLibraryFilter(CategoryId: categoryId, Status: includeInactive ? DocumentLibraryFilter.StatusAll : DocumentLibraryFilter.StatusActive), ct);

    private static DocumentLibraryFilter CheckFilter(DocumentLibraryFilter filter)
    {
        var problems = new List<DocumentProblem>();
        var tooth = string.IsNullOrWhiteSpace(filter.ToothKey) ? null : filter.ToothKey.Trim();
        var region = string.IsNullOrWhiteSpace(filter.RegionKey) ? null : filter.RegionKey.Trim();
        var text = string.IsNullOrWhiteSpace(filter.Text) ? null : filter.Text.Trim();
        var status = string.IsNullOrWhiteSpace(filter.Status) ? DocumentLibraryFilter.StatusActive : filter.Status.Trim();
        if (tooth is not null && !ToothKeys.IsValid(tooth)) problems.Add(new("toothKey", "unknown_tooth", "Use the two-digit FDI tooth number, such as 16 or 55."));
        if (region is not null && !DiagnosisRegions.All.Contains(region)) problems.Add(new("regionKey", "unknown_region", $"Use one of {string.Join(", ", DiagnosisRegions.All)}."));
        else if (region is not null && tooth is not null) problems.Add(new("regionKey", "conflicts_with_tooth", "Filter by a tooth or by a region, not both."));
        if (filter.From is { } from && filter.To is { } to && from > to) problems.Add(new("from", "after_to", "The start of the date range is after its end."));
        if (text is not null && text.Length > DocumentLibraryFilter.TextMax) problems.Add(new("text", "too_long", $"Search for at most {DocumentLibraryFilter.TextMax} characters."));
        if (status is not (DocumentLibraryFilter.StatusActive or DocumentLibraryFilter.StatusInactive or DocumentLibraryFilter.StatusAll))
            problems.Add(new("status", "unknown", "The status is Active, Inactive or All."));
        if (problems.Count > 0) throw new DocumentException(DocumentErrorCodes.MetadataInvalid, "The filter has entries that need correcting.", 400, problems);
        return filter with { ToothKey = tooth, RegionKey = region, Text = text, Status = status };
    }

    public async Task<DocumentDetail> GetAsync(Guid documentId, CancellationToken ct)
    {
        var row = await FindAsync(documentId, ct);
        var events = await db.DocumentEvents.AsNoTracking().Where(e => e.DocumentId == documentId).OrderBy(e => e.EventNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, events.Select(e => (Guid?)e.ByUserId), ct);
        var categoryIds = events.SelectMany(e => new[] { e.FromCategoryId, e.ToCategoryId }).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var categories = await db.DocumentCategories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        string? CategoryName(Guid? id) => id is null ? null : categories.GetValueOrDefault(id.Value);

        var changes = await db.DocumentStatusChanges.AsNoTracking().Where(c => c.DocumentId == documentId).OrderBy(c => c.AtUtc).ThenBy(c => c.Id).ToListAsync(ct);
        var changeNames = await ClinicalNames.ResolveAsync(db, changes.Select(c => (Guid?)c.ByUserId), ct);
        return new DocumentDetail(await DocumentMapper.ViewAsync(db, row, ct), events.Select(e =>
            new DocumentHistoryEntry(e.EventNumber, e.Kind, e.AtUtc, ClinicalNames.Name(names, e.ByUserId)!, CategoryName(e.FromCategoryId), CategoryName(e.ToCategoryId), e.RelatedDocumentId)).ToList(),
            changes.Select(c => new StatusChangeEntry(c.ToStatus, c.Reason, ClinicalNames.Name(changeNames, c.ByUserId)!, c.AtUtc)).ToList());
    }

    public async Task<OpenedDocument> OpenAsync(Guid documentId, Guid actor, CancellationToken ct)
    {
        var row = await FindAsync(documentId, ct);
        var stream = await OpenStoredAsync(row, ct);

        try
        {
            var correlation = Guid.NewGuid();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            AuditService.Record(db, "DocumentOpened", "Document", row.Id, actor, "Document opened.", correlationId: correlation);
            await db.SaveChangesAsync(ct);
            db.DocumentEvents.Add(new DocumentEvent
            {
                EventNumber = await DocumentEventNumbers.IssueAsync(db, ct), Kind = DocumentEventKinds.Opened, DocumentId = row.Id, PatientId = row.PatientId, ByUserId = actor, AtUtc = clock.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            await stream.DisposeAsync();
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            await stream.DisposeAsync();
            logger?.LogWarning("document_open_not_logged error_class={ErrorClass}", ex.InnerException?.GetType().Name ?? ex.GetType().Name);
            throw new DocumentException("save_failed", "The document could not be opened because the action could not be logged. Try again; if it keeps failing, contact support.", 503);
        }
        return new OpenedDocument(stream, row.OriginalFileName, row.ContentType, InlineTypes.Contains(row.DetectedType), row.SizeBytes);
    }

    /// <summary>
    /// The thumbnail of a picture: the stored PNG or JPEG itself (the browser scales it), up to <see cref="ThumbnailMaxBytes"/>, with the same checks as opening but <b>no history row and no log entry</b>: a thumbnail
    /// is decoration in a list, and only a deliberate open or preview is a view. Any other type is <c>not_an_image</c> (415) and a larger picture <c>thumbnail_too_large</c> (413), so a screen can show an icon.
    /// </summary>
    public async Task<OpenedDocument> OpenThumbnailAsync(Guid documentId, CancellationToken ct)
    {
        var row = await FindAsync(documentId, ct);
        if (row.DetectedType is not ("png" or "jpeg")) throw new DocumentException("not_an_image", "Only PNG and JPEG pictures have a thumbnail.", 415);
        if (row.SizeBytes > ThumbnailMaxBytes) throw new DocumentException("thumbnail_too_large", "This picture is too large for a thumbnail. Open it to see it.", 413);
        return new OpenedDocument(await OpenStoredAsync(row, ct), row.OriginalFileName, row.ContentType, true, row.SizeBytes);
    }

    /// <summary>
    /// Opens a document's stored file after a cheap check, before anything is served or logged: the file is there with the size recorded at import, and the last full check did not find it damaged (opening does
    /// not re-hash a large file).
    /// </summary>
    private async Task<Stream> OpenStoredAsync(PatientDocument row, CancellationToken ct)
    {
        var info = await blobs.GetInfoAsync(row.BlobId, ct);
        if (info is null) throw Missing(row.Id);
        var lastResult = await db.DocumentIntegrity.AsNoTracking().Where(i => i.DocumentId == row.Id).Select(i => i.Status).SingleOrDefaultAsync(ct);
        if (info.SizeBytes != row.SizeBytes || lastResult == IntegrityStatuses.Corrupt)
        {
            logger?.LogError("document_file_corrupt document_id={DocumentId}", row.Id);
            throw new DocumentException("file_corrupt", "The stored file does not match what was imported, so it is not opened. Contact support; the record is intact.", 409);
        }
        try
        {
            return await blobs.OpenReadAsync(row.BlobId, ct);
        }
        catch (FileNotFoundException)
        {
            throw Missing(row.Id);
        }
    }

    private DocumentException Missing(Guid documentId)
    {
        logger?.LogError("document_file_missing document_id={DocumentId}", documentId);
        return new DocumentException("file_missing", "The stored file could not be found. Contact support; the record is intact.", 404);
    }

    private async Task<PatientDocument> FindAsync(Guid documentId, CancellationToken ct) =>
        await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId, ct) ?? throw new DocumentException("document_not_found", "That document was not found.", 404);
}
