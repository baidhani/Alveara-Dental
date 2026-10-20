using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// STORY-010: importing a document (docs/DOCUMENTS.md, "What happens to a file on import"). The checks run cheapest first and every refusal happens before anything is recorded; a file is stored only once it has passed
/// every check that can be made without storing it, and it is removed again if the import does not complete.
///
/// How the promises are kept:
/// - <b>Nothing half-done.</b> The document, its history row and its log entry are written in one transaction. If anything after the file was stored fails, the stored file is removed and the caller gets a clear
///   error (<c>save_failed</c> or <c>store_failed</c>, 503) with nothing recorded. A failure to remove the file is logged for cleanup and never hides the original error.
/// - <b>A failed log write fails the import.</b> The log entry is part of the same transaction.
/// - <b>Repeats are quiet.</b> The same request under the same key returns the same document and changes nothing (the redundant stored copy is removed); the same key for a different file or different details is refused
///   (<c>idempotency_key_reused</c>, 409). Two simultaneous requests with one key make one document (the database's unique key decides).
/// - <b>A duplicate is warned about, never silently added.</b> The same content for the same patient is refused with the existing document's summary (<c>duplicate_document</c>, 409) and stores nothing; it is added
///   only when the caller names that document in <c>ConfirmDuplicateOf</c>, which the server checks, and is then recorded as a duplicate. A duplicate warning uses up no key.
/// - <b>The log is PHI-free.</b> Only the document's id, the action and a category code are written, never a filename, a description or a patient.
/// - Not handled: two simultaneous imports of the same new file under different keys both succeed, because the duplicate check reads before either commits; the second is an ordinary document.
/// </summary>
public class DocumentImportService(AlveraDbContext db, IBlobStorage blobs, IPracticeClock clock, ILogger<DocumentImportService>? logger = null)
{
    private const int KeyMax = 64;

    public async Task<DocumentImportResult> ImportAsync(DocumentImportRequest request, Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = CheckKey(request.IdempotencyKey);
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == request.PatientId, ct)) throw new DocumentException("patient_not_found", "That patient was not found.", 404);

        var settings = await db.DocumentSettings.AsNoTracking().SingleAsync(ct);
        if (DocumentFileRules.CheckSize(request.Length, settings.MaxUploadBytes) is { } sizeError) throw Refusal(sizeError, settings.MaxUploadBytes);
        var name = DocumentFileRules.NormalizeFileName(request.FileName);
        if (!name.IsOk) throw Refusal(name.Error!);
        var blocked = (await db.BlockedFileExtensions.AsNoTracking().Select(b => b.Extension).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (DocumentFileRules.CheckBlocked(name.Extension, blocked) is { } blockedError) throw Refusal(blockedError);
        var detected = DocumentFileRules.DetectContent(await ReadHeaderAsync(request.Content, ct));
        if (DocumentFileRules.CheckContent(name.Extension, detected) is { } contentError) throw Refusal(contentError);
        var type = DocumentFileRules.Classify(detected);
        var meta = await DocumentMetadataChecker.CheckAsync(db, clock, request.PatientId, request.CategoryId, request.DocumentDate, request.Description, request.EncounterId, request.ToothKey, request.RegionKey, null, ct);
        var (category, description) = (meta.Category, meta.Description);

        request.Content.Position = 0;
        var blob = await StoreAsync(request.Content, ct);
        var hash = RequestHash(request.PatientId, blob.Sha256Hash, category.Id, request.DocumentDate!.Value, description, name.Name!, request.ConfirmDuplicateOf, meta.EncounterId, meta.ToothKey, meta.RegionKey);
        try
        {
            // the declared length is only a claim: what was really stored decides
            if (DocumentFileRules.CheckSize(blob.SizeBytes, settings.MaxUploadBytes) is { } storedSizeError) throw Refusal(storedSizeError, settings.MaxUploadBytes);

            if (await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.PatientId == request.PatientId && d.IdempotencyKey == key, ct) is { } existing)
            {
                await DiscardAsync(blob.Id);
                return await ReplayAsync(existing, hash, ct);
            }

            var duplicateOf = await CheckDuplicateAsync(request, blob.Sha256Hash, ct);
            var document = new PatientDocument
            {
                Id = Guid.NewGuid(), PatientId = request.PatientId, CategoryId = category.Id, DocumentDate = request.DocumentDate!.Value, Description = description, DuplicateOfDocumentId = duplicateOf,
                OriginalFileName = name.Name!, DetectedType = type.DetectedType, ContentType = type.ContentType, SizeBytes = blob.SizeBytes, Sha256 = blob.Sha256Hash, BlobId = blob.Id,
                ImportedByUserId = actor, ImportedAtUtc = clock.UtcNow, IdempotencyKey = key, RequestHash = hash, EncounterId = meta.EncounterId, ToothKey = meta.ToothKey, RegionKey = meta.RegionKey,
            };
            await RecordAsync(document, category.Code, actor, ct);
            return new DocumentImportResult(await DocumentMapper.ViewAsync(db, document, ct), false);
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            // a simultaneous import under the same key won: answer with its document (or refuse if it was a different request)
            db.ChangeTracker.Clear();
            await DiscardAsync(blob.Id);
            var winner = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.PatientId == request.PatientId && d.IdempotencyKey == key, ct) ?? throw SaveFailed(ex);
            return await ReplayAsync(winner, hash, ct);
        }
        catch (DocumentException ex)
        {
            db.ChangeTracker.Clear();
            await DiscardAsync(blob.Id);
            // a simultaneous import under the same key can commit between our key check and our duplicate check; the document the duplicate check just found is then that import's own, so this is a repeat, not a duplicate
            if (ex.Code is "duplicate_document" or "duplicate_confirmation_invalid"
                && await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.PatientId == request.PatientId && d.IdempotencyKey == key, ct) is { } sameKey)
                return await ReplayAsync(sameKey, hash, ct);
            throw;
        }
        catch (OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            await DiscardAsync(blob.Id);
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            await DiscardAsync(blob.Id);
            throw SaveFailed(ex);
        }
    }

    // ---------- checks ----------

    private static string CheckKey(string? raw)
    {
        var key = raw?.Trim() ?? string.Empty;
        if (key.Length is 0 or > KeyMax || key.Any(char.IsControl))
            throw new DocumentException("idempotency_key_required", $"A save key of 1 to {KeyMax} characters without control characters is required, so that a retry cannot import the file twice.", 400);
        return key;
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream content, CancellationToken ct)
    {
        content.Position = 0;
        var buffer = new byte[DocumentFileRules.HeaderBytesToRead];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await content.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }
        content.Position = 0;
        return buffer[..read];
    }

    /// <summary>The document this one duplicates (when the importer confirmed it), or null when it is not a duplicate. Refuses an unconfirmed duplicate and a confirmation that does not match.</summary>
    private async Task<Guid?> CheckDuplicateAsync(DocumentImportRequest r, string sha256, CancellationToken ct)
    {
        var same = await db.Documents.AsNoTracking().Where(d => d.PatientId == r.PatientId && d.Sha256 == sha256 && d.Status == DocumentStatuses.Active).OrderBy(d => d.ImportedAtUtc).ThenBy(d => d.Id).ToListAsync(ct);
        if (same.Count == 0)
        {
            if (r.ConfirmDuplicateOf is not null) throw InvalidConfirmation();
            return null;
        }
        if (r.ConfirmDuplicateOf is null)
        {
            var first = same[0];
            var view = await DocumentMapper.ViewAsync(db, first, ct);
            throw new DocumentException("duplicate_document", "This patient already has this file. Nothing was imported. Add it anyway only if you mean to.", 409,
                detail: new DuplicateSummary(first.Id, view.CategoryName, first.DocumentDate, view.ImportedByName, first.ImportedAtUtc));
        }
        return same.Any(d => d.Id == r.ConfirmDuplicateOf) ? r.ConfirmDuplicateOf : throw InvalidConfirmation();
    }

    private static DocumentException InvalidConfirmation() =>
        new("duplicate_confirmation_invalid", "The document you confirmed is not a copy of this file for this patient. Nothing was imported.", 409);

    // ---------- storing and recording ----------

    private async Task<StoredBlob> StoreAsync(Stream content, CancellationToken ct)
    {
        try
        {
            return await blobs.StoreAsync(content, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning("document_store_failed error_class={ErrorClass}", ex.GetType().Name);
            throw new DocumentException("store_failed", "The file could not be stored, so nothing was imported. Try again; if it keeps failing, contact support.", 503);
        }
    }

    /// <summary>One transaction: the document and its log entry, then (as late as possible, so the counter's lock is held only briefly) the numbered history row.</summary>
    private async Task RecordAsync(PatientDocument document, string categoryCode, Guid actor, CancellationToken ct)
    {
        var duplicate = document.DuplicateOfDocumentId is not null;
        var correlation = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Documents.Add(document);
        AuditService.Record(db, duplicate ? "DocumentDuplicateImported" : "DocumentImported", "Document", document.Id, actor,
            duplicate ? $"Document imported as a duplicate (category {categoryCode})." : $"Document imported (category {categoryCode}).", correlationId: correlation);
        await db.SaveChangesAsync(ct);

        db.DocumentEvents.Add(new DocumentEvent
        {
            EventNumber = await DocumentEventNumbers.IssueAsync(db, ct), Kind = duplicate ? DocumentEventKinds.ImportedAsDuplicate : DocumentEventKinds.Imported, DocumentId = document.Id,
            RelatedDocumentId = document.DuplicateOfDocumentId, PatientId = document.PatientId, ByUserId = actor, AtUtc = document.ImportedAtUtc,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<DocumentImportResult> ReplayAsync(PatientDocument existing, string hash, CancellationToken ct)
    {
        if (existing.RequestHash != hash)
            throw new DocumentException("idempotency_key_reused", "That save key was already used for a different file or different details. Nothing was imported. Use a new key to import another.", 409);
        return new DocumentImportResult(await DocumentMapper.ViewAsync(db, existing, ct), true);
    }

    /// <summary>Removes a stored file that no document will use. Never throws: a failure is logged for cleanup, and must not hide the error that made the file unneeded.</summary>
    private async Task DiscardAsync(Guid blobId)
    {
        try
        {
            await blobs.DeleteAsync(blobId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError("document_blob_cleanup_failed blob_id={BlobId} error_class={ErrorClass}", blobId, ex.GetType().Name);
        }
    }

    private DocumentException SaveFailed(Exception ex)
    {
        logger?.LogWarning("document_not_saved error_class={ErrorClass}", ex.InnerException?.GetType().Name ?? ex.GetType().Name);
        return new DocumentException("save_failed", "The document could not be saved, so nothing was imported. Try again; if it keeps failing, contact support.", 503);
    }

    private static bool IsUnique(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static DocumentException Refusal(string code, long limit = 0) => new(code, code switch
    {
        DocumentErrorCodes.FileEmpty => "The file is empty. Nothing was imported.",
        DocumentErrorCodes.FileTooLarge => $"The file is larger than the {limit / (1024 * 1024)} MB limit. Nothing was imported.",
        DocumentErrorCodes.FileTypeBlocked => "This type of file cannot be imported. Nothing was imported.",
        DocumentErrorCodes.FileTypeMismatch => "The file is not the type its name says. Nothing was imported.",
        DocumentErrorCodes.ExecutableContent => "This file is a program, which cannot be imported whatever it is called. Nothing was imported.",
        DocumentErrorCodes.FileNameInvalid => "The file name cannot be used. Rename the file and try again. Nothing was imported.",
        _ => "The file was refused. Nothing was imported.",
    }, code == DocumentErrorCodes.FileTooLarge ? 413 : 400);

    /// <summary>A fingerprint of everything the importer asked for, so the same key with anything different is refused and the same request is a replay.</summary>
    private static string RequestHash(Guid patientId, string sha256, Guid categoryId, DateOnly date, string? description, string fileName, Guid? confirmDuplicateOf, Guid? encounterId, string? toothKey, string? regionKey)
    {
        var text = string.Join('|', "document-import-v1", patientId.ToString("N"), sha256, categoryId.ToString("N"), date.ToString("yyyy-MM-dd"), description ?? string.Empty, fileName, confirmDuplicateOf?.ToString("N") ?? string.Empty, encounterId?.ToString("N") ?? string.Empty, toothKey ?? string.Empty, regionKey ?? string.Empty);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
