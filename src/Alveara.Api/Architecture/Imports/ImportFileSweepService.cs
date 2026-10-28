using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>What a sweep did: the files looked at, removed (no batch refers to them and they are past the grace period), kept, failed to remove, and the open batches whose own file is missing (reported, never "fixed").</summary>
public sealed record ImportFileSweepResult(int Found, int Removed, int Kept, int Failed, int BatchesMissingFile);

/// <summary>
/// ALV-018-C02 slice 6: removes the files in the import folder that no batch refers to. They come from one narrow window (the process dies after a file is stored and before its batch is saved; the intake service deletes the file
/// itself on any failure it can see). The sweep reads only the import folder (never the document store), takes the ids of every file a batch refers to, and removes a file only if it is not one of them, is older than
/// <see cref="ImportOptions.OrphanFileGraceHours"/> (a younger one may belong to an upload in progress) and is still unreferenced when checked again just before the delete. A batch's file is never touched, whatever the batch's
/// state. It is safe to repeat. Failure modes handled: a file that cannot be removed (counted and logged by class and id, the sweep goes on), a repeat, a concurrent upload. The audit entry carries counts only.
/// </summary>
public class ImportFileSweepService(AlveraDbContext db, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock, ILogger<ImportFileSweepService> log)
{
    private readonly ImportOptions _options = options.Value;

    public async Task<ImportFileSweepResult> SweepAsync(Guid actor, CancellationToken ct)
    {
        var blobs = new List<BlobInfo>();
        await foreach (var blob in files.Blobs.ListAsync(ct)) blobs.Add(blob);              // listed first: the folder is not changed while it is enumerated

        var referenced = (await db.ImportBatches.AsNoTracking().Select(b => b.FileBlobId).ToListAsync(ct)).ToHashSet();
        var cutoff = clock.UtcNow - TimeSpan.FromHours(Math.Max(0, _options.OrphanFileGraceHours));
        int removed = 0, kept = 0, failed = 0;
        foreach (var blob in blobs)
        {
            if (referenced.Contains(blob.Id) || blob.LastWriteUtc > cutoff || await db.ImportBatches.AsNoTracking().AnyAsync(b => b.FileBlobId == blob.Id, ct)) { kept++; continue; }
            try
            {
                await files.Blobs.DeleteAsync(blob.Id, ct);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                log.LogError("event=import_file_sweep_failed outcome=failure error_class={ErrorClass} blob_id={BlobId}", ex.GetType().Name, blob.Id);
            }
        }

        var missing = 0;
        foreach (var blobId in await db.ImportBatches.AsNoTracking().Where(b => ImportBatchStatus.Open.Contains(b.Status)).Select(b => b.FileBlobId).ToListAsync(ct))
            if (await files.Blobs.GetInfoAsync(blobId, ct) is null) missing++;

        AuditService.Record(db, AuditEventTypes.ImportFilesSwept, nameof(ImportBatch), Guid.NewGuid(), actor,
            $"Import file sweep: {blobs.Count} file(s) found, {removed} removed, {kept} kept, {failed} could not be removed; {missing} open batch(es) have no stored file.");
        await db.SaveChangesAsync(ct);
        return new ImportFileSweepResult(blobs.Count, removed, kept, failed, missing);
    }
}
