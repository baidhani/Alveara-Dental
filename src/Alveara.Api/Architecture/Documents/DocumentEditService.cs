using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// STORY-010: editing what is known about a document: its category, date and description (docs/DOCUMENTS.md, "The model"). The file itself never changes (the database refuses it). A recategorization writes a
/// <c>Categorized</c> history row naming both categories and a <c>DocumentCategorized</c> log entry; a change of date or description writes <c>MetadataEdited</c> and <c>DocumentMetadataEdited</c>; both together write both.
/// Each change and its rows are one transaction, so a failed log write changes nothing (<c>save_failed</c>, 503). The caller echoes the row version it read; a change made by someone else since is refused
/// (<see cref="ConcurrencyConflictException"/>, 409). Saving without changing anything writes nothing. The log never carries the description, the date or a filename.
/// </summary>
public class DocumentEditService(AlveraDbContext db, IPracticeClock clock, ILogger<DocumentEditService>? logger = null)
{
    public async Task<DocumentView> UpdateAsync(Guid documentId, DocumentEditInput input, Guid actor, CancellationToken ct)
    {
        var expected = ParseRowVersion(input.RowVersion);
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, ct) ?? throw new DocumentException("document_not_found", "That document was not found.", 404);
        db.Entry(document).Property(d => d.RowVersion).OriginalValue = expected;

        var (category, description) = await DocumentMetadataChecker.CheckAsync(db, clock, input.CategoryId, input.DocumentDate, input.Description, document.CategoryId, ct);
        var date = input.DocumentDate!.Value;
        var recategorized = category.Id != document.CategoryId;
        var detailsChanged = date != document.DocumentDate || description != document.Description;
        if (!recategorized && !detailsChanged) return await DocumentMapper.ViewAsync(db, document, ct);     // nothing changed: nothing is saved and nothing is logged

        var fromCategory = document.CategoryId;
        var correlation = Guid.NewGuid();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            (document.CategoryId, document.DocumentDate, document.Description) = (category.Id, date, description);
            if (recategorized) AuditService.Record(db, "DocumentCategorized", "Document", document.Id, actor, $"Document moved to category {category.Code}.", correlationId: correlation);
            if (detailsChanged) AuditService.Record(db, "DocumentMetadataEdited", "Document", document.Id, actor, "Document date or description edited.", correlationId: correlation);
            await db.SaveChangesAsync(ct);                                                                   // the version check happens here

            if (recategorized) db.DocumentEvents.Add(Event(await DocumentEventNumbers.IssueAsync(db, ct), DocumentEventKinds.Categorized, document, actor, fromCategory, category.Id));
            if (detailsChanged) db.DocumentEvents.Add(Event(await DocumentEventNumbers.IssueAsync(db, ct), DocumentEventKinds.MetadataEdited, document, actor, null, null));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(nameof(PatientDocument), documentId);
        }
        catch (OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("document_edit_not_saved error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, correlation);
            throw new DocumentException("save_failed", "The document could not be saved, so nothing was changed. Try again; if it keeps failing, contact support.", 503);
        }
        return await DocumentMapper.ViewAsync(db, document, ct);
    }

    private DocumentEvent Event(long number, string kind, PatientDocument d, Guid actor, Guid? from, Guid? to) =>
        new() { EventNumber = number, Kind = kind, DocumentId = d.Id, PatientId = d.PatientId, FromCategoryId = from, ToCategoryId = to, ByUserId = actor, AtUtc = clock.UtcNow };

    private static byte[] ParseRowVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new DocumentException("row_version_required", "The version of the document you are changing is required, so a change made by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new DocumentException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400); }
    }
}
