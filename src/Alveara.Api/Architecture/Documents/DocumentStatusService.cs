using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// ALV-010-C01: making a document inactive and active again (docs/DOCUMENTS.md, "Inactive documents"). An inactive document is hidden from the library by default and ignored by the duplicate check, but it stays: the
/// file, the record and every history row are kept for ever (the database refuses a deletion). Inactivating needs a reason (1 to 500 characters); it is stored in the append-only status history, never in the log or
/// the document history, so a reactivation does not lose it. Held by the roles that may edit documents.
///
/// How the promises are kept:
/// - <b>One transaction.</b> The document's new status, the status-history row, the history row and the log entry are saved together; a failed log write changes nothing (<c>save_failed</c>, 503).
/// - <b>Repeats are quiet.</b> Inactivating an inactive document or reactivating an active one changes nothing and logs nothing (the first reason stays).
/// - <b>No silent overwrite.</b> The caller echoes the row version it read; a change made by someone else since is refused (<see cref="ConcurrencyConflictException"/>, 409).
/// </summary>
public class DocumentStatusService(AlveraDbContext db, IPracticeClock clock, ILogger<DocumentStatusService>? logger = null)
{
    private const int ReasonMax = 500;

    public async Task<DocumentView> InactivateAsync(Guid documentId, string? rowVersion, string? rawReason, Guid actor, CancellationToken ct)
    {
        var expected = DocumentRowVersion.Parse(rowVersion);
        var reason = CheckReason(rawReason);
        var document = await FindAsync(documentId, ct);
        if (document.Status == DocumentStatuses.Inactive) return await DocumentMapper.ViewAsync(db, document, ct);
        db.Entry(document).Property(d => d.RowVersion).OriginalValue = expected;

        var now = clock.UtcNow;
        (document.Status, document.InactiveReason, document.InactivatedByUserId, document.InactivatedAtUtc) = (DocumentStatuses.Inactive, reason, actor, now);
        await SaveChangeAsync(document, DocumentStatuses.Inactive, reason, DocumentEventKinds.Inactivated, "DocumentInactivated", "Document inactivated.", actor, now, ct);
        return await DocumentMapper.ViewAsync(db, document, ct);
    }

    public async Task<DocumentView> ReactivateAsync(Guid documentId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var expected = DocumentRowVersion.Parse(rowVersion);
        var document = await FindAsync(documentId, ct);
        if (document.Status == DocumentStatuses.Active) return await DocumentMapper.ViewAsync(db, document, ct);
        db.Entry(document).Property(d => d.RowVersion).OriginalValue = expected;

        (document.Status, document.InactiveReason, document.InactivatedByUserId, document.InactivatedAtUtc) = (DocumentStatuses.Active, null, null, null);
        await SaveChangeAsync(document, DocumentStatuses.Active, null, DocumentEventKinds.Reactivated, "DocumentReactivated", "Document reactivated.", actor, clock.UtcNow, ct);
        return await DocumentMapper.ViewAsync(db, document, ct);
    }

    private async Task SaveChangeAsync(PatientDocument document, string toStatus, string? reason, string eventKind, string auditType, string auditDetails, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var correlation = Guid.NewGuid();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.DocumentStatusChanges.Add(new DocumentStatusChange { Id = Guid.NewGuid(), DocumentId = document.Id, ToStatus = toStatus, Reason = reason, ByUserId = actor, AtUtc = now });
            AuditService.Record(db, auditType, "Document", document.Id, actor, auditDetails, correlationId: correlation);
            await db.SaveChangesAsync(ct);                                                                  // the version check happens here

            db.DocumentEvents.Add(new DocumentEvent { EventNumber = await DocumentEventNumbers.IssueAsync(db, ct), Kind = eventKind, DocumentId = document.Id, PatientId = document.PatientId, ByUserId = actor, AtUtc = now });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(nameof(PatientDocument), document.Id);
        }
        catch (OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("document_status_not_saved error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, correlation);
            throw new DocumentException("save_failed", "The document could not be changed, so nothing was saved. Try again; if it keeps failing, contact support.", 503);
        }
    }

    private static string CheckReason(string? raw)
    {
        var reason = raw?.Trim() ?? string.Empty;
        if (reason.Length == 0) throw Invalid("required", "Say why the document is being made inactive.");
        if (reason.Length > ReasonMax) throw Invalid("too_long", $"The reason can be at most {ReasonMax} characters.");
        if (reason.Any(char.IsControl)) throw Invalid("invalid", "The reason cannot contain control characters.");
        return reason;

        static DocumentException Invalid(string code, string message) =>
            new(DocumentErrorCodes.MetadataInvalid, "That entry needs correcting. Nothing was changed.", 400, [new DocumentProblem("reason", code, message)]);
    }

    private async Task<PatientDocument> FindAsync(Guid documentId, CancellationToken ct) =>
        await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, ct) ?? throw new DocumentException("document_not_found", "That document was not found.", 404);
}

/// <summary>Reads the row version a caller echoes: required, and a value that is not one is refused rather than guessed at.</summary>
internal static class DocumentRowVersion
{
    public static byte[] Parse(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new DocumentException("row_version_required", "The version of the document you are changing is required, so a change made by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new DocumentException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400); }
    }
}
