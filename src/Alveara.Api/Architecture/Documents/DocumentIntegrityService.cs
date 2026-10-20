using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>The result of checking one document's stored file.</summary>
public sealed record IntegrityResult(Guid DocumentId, string Status, DateTimeOffset CheckedAtUtc);

/// <summary>A document whose last storage check found its file missing or corrupt.</summary>
public sealed record StorageProblem(Guid DocumentId, Guid PatientId, string FileName, string Status, DateTimeOffset CheckedAtUtc);

/// <summary>A stored file that no document points to, old enough that it cannot be an import still in progress.</summary>
public sealed record OrphanFile(Guid BlobId, long SizeBytes, DateTimeOffset LastWriteUtc);

/// <summary>What a storage check of every document found. <see cref="Skipped"/> counts files that could not be read just now (for example one that is locked); their last result is left as it was.</summary>
public sealed record StorageReport(int Checked, int Ok, int Missing, int Corrupt, int Skipped, IReadOnlyList<StorageProblem> Problems, IReadOnlyList<OrphanFile> Orphans, DateTimeOffset RanAtUtc);

/// <summary>The current picture without running a check: how many documents have each stored result (and how many were never checked), the problems, and the unreferenced files on disk right now.</summary>
public sealed record StorageSummary(int Documents, int Ok, int Missing, int Corrupt, int NeverChecked, IReadOnlyList<StorageProblem> Problems, IReadOnlyList<OrphanFile> Orphans);

/// <summary>What happened to one file a reviewer asked to remove.</summary>
public static class OrphanOutcomes
{
    public const string Removed = "Removed";
    public const string AlreadyGone = "AlreadyGone";
    public const string StillReferenced = "StillReferenced";
    public const string TooNew = "TooNew";
    public const string Failed = "Failed";
}

public sealed record OrphanRemoval(Guid BlobId, string Outcome);

public sealed record OrphanRemovalReport(IReadOnlyList<OrphanRemoval> Results)
{
    public int Removed => Results.Count(r => r.Outcome == OrphanOutcomes.Removed);
}

/// <summary>
/// ALV-010-C01: checking that the stored files of documents are present and intact, and finding stored files that no document points to (docs/DOCUMENTS.md, "Storage integrity").
///
/// How the promises are kept:
/// - <b>What a result means.</b> Ok: the file is on disk with the size and SHA-256 recorded at import. Missing: it is not on disk. Corrupt: its size or content no longer matches. A file that cannot be read just now
///   (locked, a transient I/O error) is neither: a single check says so (<c>file_unreadable</c>, 503) and a run skips it and counts it, leaving its last result as it was.
/// - <b>A check never makes an edit look stale.</b> The result is its own row per document (replaced by each check), not columns on the document, so a document's row version does not move.
/// - <b>One save with its log entry.</b> A single check saves its result with a <c>DocumentIntegrityChecked</c> entry; a run over every document saves all its results with one <c>DocumentStorageChecked</c> entry,
///   in one transaction, and the entry carries counts only (never a file name or a patient). A failed log write saves no result (<c>save_failed</c>, 503). A run honours cancellation and saves nothing if cancelled.
/// - <b>Unreferenced files are listed, then removed only by a reviewer.</b> A file counts only once it is older than <see cref="OrphanGracePeriod"/>, so an import still being written is never listed; a temporary
///   file or a stray file with another name is not a stored file. <see cref="RemoveOrphansAsync"/> removes the files a reviewer names, each in its own transaction: it takes a lock on that file (so simultaneous
///   removals act once), re-checks at that moment that no document points to it and that it is old enough, writes a <c>DocumentOrphanRemoved</c> entry (the file's id, never a name or a patient) and only then
///   deletes the file; if the entry cannot be saved or the delete fails the file stays and nothing is logged (outcome Failed), and one failure never stops the others. Nothing is ever removed automatically.
/// - Not handled: a run hashes every file, so it takes time proportional to the library's size and is meant to be run on request (or off hours), not on every page view.
/// </summary>
public class DocumentIntegrityService(AlveraDbContext db, IBlobStorage blobs, IPracticeClock clock, ILogger<DocumentIntegrityService>? logger = null)
{
    public static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromHours(1);

    private sealed record Target(Guid Id, Guid PatientId, string FileName, Guid BlobId, long SizeBytes, string Sha256);

    public async Task<IntegrityResult> CheckDocumentAsync(Guid documentId, Guid actor, CancellationToken ct) => await CheckDocumentAsync(documentId, actor, retry: true, ct);

    private async Task<IntegrityResult> CheckDocumentAsync(Guid documentId, Guid actor, bool retry, CancellationToken ct)
    {
        var target = await db.Documents.AsNoTracking().Where(d => d.Id == documentId).Select(d => new Target(d.Id, d.PatientId, d.OriginalFileName, d.BlobId, d.SizeBytes, d.Sha256)).SingleOrDefaultAsync(ct)
            ?? throw new DocumentException("document_not_found", "That document was not found.", 404);
        var status = await InspectAsync(target, ct)
            ?? throw new DocumentException("file_unreadable", "The stored file could not be read just now. Try the check again in a moment.", 503);

        var now = clock.UtcNow;
        var row = await db.DocumentIntegrity.SingleOrDefaultAsync(i => i.DocumentId == documentId, ct);
        if (row is null) db.DocumentIntegrity.Add(new DocumentIntegrity { DocumentId = documentId, Status = status, CheckedAtUtc = now, CheckedByUserId = actor });
        else (row.Status, row.CheckedAtUtc, row.CheckedByUserId) = (status, now, actor);
        AuditService.Record(db, "DocumentIntegrityChecked", "Document", documentId, actor, $"Storage checked: {status}.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (retry && IsUnique(ex))
        {
            db.ChangeTracker.Clear();                                                // a simultaneous check of the same document saved its row first: save this one over it
            return await CheckDocumentAsync(documentId, actor, retry: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
        return new IntegrityResult(documentId, status, now);
    }

    public async Task<StorageReport> CheckAllAsync(Guid actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var targets = await db.Documents.AsNoTracking().OrderBy(d => d.ImportedAtUtc).ThenBy(d => d.Id)
            .Select(d => new Target(d.Id, d.PatientId, d.OriginalFileName, d.BlobId, d.SizeBytes, d.Sha256)).ToListAsync(ct);

        var results = new List<(Target Target, string Status)>();
        var skipped = 0;
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (await InspectAsync(target, ct) is { } status) results.Add((target, status)); else skipped++;
        }

        var now = clock.UtcNow;
        var existing = await db.DocumentIntegrity.ToDictionaryAsync(i => i.DocumentId, ct);
        foreach (var (target, status) in results)
        {
            if (existing.TryGetValue(target.Id, out var row)) (row.Status, row.CheckedAtUtc, row.CheckedByUserId) = (status, now, actor);
            else db.DocumentIntegrity.Add(new DocumentIntegrity { DocumentId = target.Id, Status = status, CheckedAtUtc = now, CheckedByUserId = actor });
        }
        var orphans = await ListOrphansAsync(ct);
        int Count(string s) => results.Count(r => r.Status == s);
        AuditService.Record(db, "DocumentStorageChecked", "DocumentStorage", Guid.Empty, actor,
            $"Storage checked: {targets.Count} documents, {Count(IntegrityStatuses.Ok)} ok, {Count(IntegrityStatuses.Missing)} missing, {Count(IntegrityStatuses.Corrupt)} corrupt, {skipped} skipped, {orphans.Count} unreferenced files.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }

        var problems = results.Where(r => r.Status != IntegrityStatuses.Ok).Select(r => new StorageProblem(r.Target.Id, r.Target.PatientId, r.Target.FileName, r.Status, now)).ToList();
        return new StorageReport(targets.Count, Count(IntegrityStatuses.Ok), Count(IntegrityStatuses.Missing), Count(IntegrityStatuses.Corrupt), skipped, problems, orphans, now);
    }

    public async Task<StorageSummary> SummaryAsync(CancellationToken ct)
    {
        var documents = await db.Documents.AsNoTracking().CountAsync(ct);
        var counts = await db.DocumentIntegrity.AsNoTracking().GroupBy(i => i.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Of(string s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        var problems = await (from i in db.DocumentIntegrity.AsNoTracking()
                              join d in db.Documents.AsNoTracking() on i.DocumentId equals d.Id
                              where i.Status != IntegrityStatuses.Ok
                              orderby i.CheckedAtUtc descending
                              select new StorageProblem(d.Id, d.PatientId, d.OriginalFileName, i.Status, i.CheckedAtUtc)).ToListAsync(ct);
        return new StorageSummary(documents, Of(IntegrityStatuses.Ok), Of(IntegrityStatuses.Missing), Of(IntegrityStatuses.Corrupt), documents - counts.Sum(c => c.Count), problems, await ListOrphansAsync(ct));
    }

    public const int MaxRemovalBatch = 500;

    public async Task<OrphanRemovalReport> RemoveOrphansAsync(IReadOnlyList<Guid> blobIds, Guid actor, CancellationToken ct)
    {
        var ids = blobIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxRemovalBatch)
            throw new DocumentException(DocumentErrorCodes.MetadataInvalid, "That entry needs correcting. Nothing was removed.", 400,
                [new DocumentProblem("blobIds", ids.Count == 0 ? "required" : "too_many", $"Name between 1 and {MaxRemovalBatch} files to remove.")]);

        var results = new List<OrphanRemoval>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(new OrphanRemoval(id, await RemoveOneAsync(id, actor, ct)));
        }
        return new OrphanRemovalReport(results);
    }

    private async Task<string> RemoveOneAsync(Guid blobId, Guid actor, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await TakeLockAsync(blobId, ct);                                                   // held until this transaction ends, so a simultaneous removal of the same file waits and then finds it gone
            if (await db.Documents.AsNoTracking().AnyAsync(d => d.BlobId == blobId, ct)) return OrphanOutcomes.StillReferenced;
            var info = await blobs.GetInfoAsync(blobId, ct);
            if (info is null) return OrphanOutcomes.AlreadyGone;
            if (info.LastWriteUtc >= clock.UtcNow - OrphanGracePeriod) return OrphanOutcomes.TooNew;

            AuditService.Record(db, "DocumentOrphanRemoved", "StoredFile", blobId, actor, "Unreferenced stored file removed.");
            await db.SaveChangesAsync(ct);                                                     // the entry first: if it cannot be saved the file is not touched
            await blobs.DeleteAsync(blobId, ct);                                               // if this throws, leaving the transaction rolls the entry back
            await transaction.CommitAsync(ct);
            return OrphanOutcomes.Removed;
        }
        catch (OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("document_orphan_not_removed blob_id={BlobId} error_class={ErrorClass}", blobId, ex.InnerException?.GetType().Name ?? ex.GetType().Name);
            return OrphanOutcomes.Failed;
        }
    }

    private async Task TakeLockAsync(Guid blobId, CancellationToken ct)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync("EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
            [result, new SqlParameter("@resource", $"document-orphan:{blobId:N}")], ct);
        if (result.Value is not int code || code < 0) throw new InvalidOperationException("The removal lock for the stored file could not be taken.");
    }

    public async Task<IReadOnlyList<OrphanFile>> ListOrphansAsync(CancellationToken ct)
    {
        var known = (await db.Documents.AsNoTracking().Select(d => d.BlobId).ToListAsync(ct)).ToHashSet();
        var cutoff = clock.UtcNow - OrphanGracePeriod;
        var orphans = new List<OrphanFile>();
        await foreach (var blob in blobs.ListAsync(ct))
            if (!known.Contains(blob.Id) && blob.LastWriteUtc < cutoff) orphans.Add(new OrphanFile(blob.Id, blob.SizeBytes, blob.LastWriteUtc));
        return orphans.OrderBy(o => o.LastWriteUtc).ThenBy(o => o.BlobId).ToList();
    }

    /// <summary>The status of one document's file, or null when it could not be read just now (which says nothing about whether it is damaged).</summary>
    private async Task<string?> InspectAsync(Target target, CancellationToken ct)
    {
        try
        {
            var info = await blobs.GetInfoAsync(target.BlobId, ct);
            if (info is null) return IntegrityStatuses.Missing;
            if (info.SizeBytes != target.SizeBytes) return IntegrityStatuses.Corrupt;
            return await blobs.VerifyIntegrityAsync(target.BlobId, target.Sha256, ct) ? IntegrityStatuses.Ok : IntegrityStatuses.Corrupt;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("document_storage_unreadable document_id={DocumentId} error_class={ErrorClass}", target.Id, ex.GetType().Name);
            return null;
        }
    }

    private DocumentException SaveFailed(Exception ex)
    {
        logger?.LogWarning("document_storage_not_saved error_class={ErrorClass}", ex.InnerException?.GetType().Name ?? ex.GetType().Name);
        return new DocumentException("save_failed", "The result of the storage check could not be saved. Try again; if it keeps failing, contact support.", 503);
    }

    private static bool IsUnique(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };
}
