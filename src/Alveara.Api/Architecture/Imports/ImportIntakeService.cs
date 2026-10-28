using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>A request that cannot be accepted as asked (an unknown kind, a missing or bad label, a batch that does not exist). <see cref="Code"/> is stable; the screens map it to words.</summary>
public sealed class ImportException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>A file that was not accepted because of what is in it. <see cref="Problems"/> lists what to correct (at most <see cref="ImportLimits.MaxProblemsListed"/>); <see cref="TotalProblems"/> counts them all.</summary>
public sealed class ImportFileRejectedException(IReadOnlyList<ImportProblem> problems, int totalProblems)
    : Exception("The file was not accepted. Correct the problems listed and send it again.")
{
    public IReadOnlyList<ImportProblem> Problems { get; } = problems;
    public int TotalProblems { get; } = totalProblems;
}

/// <summary>One batch as the screens show it. No value from the file: the file is a hash and a size, its columns are names, its rows are counts.</summary>
public sealed record ImportBatchView(
    Guid Id, string SourceKind, string SourceLabel, string Status, string Policy, string FileSha256, long FileSizeBytes, int RowCount, IReadOnlyList<string> ColumnNames,
    int AttemptedRows, int AcceptedRows, int RejectedRows, int SkippedRows, int UnresolvedRows, string CreatedBy, DateTimeOffset CreatedAtUtc,
    string? CommitOutcome = null, DateTimeOffset? CommitStartedAtUtc = null, DateTimeOffset? CommittedAtUtc = null, int CommitRound = 0, bool MappingConfirmed = false, DateTimeOffset? DryRunAtUtc = null);

/// <summary>What an upload gives back: the batch, and whether this call made it (false when the same file of the same kind was already waiting, which is returned instead of a second batch).</summary>
public sealed record ImportUploadResult(ImportBatchView Batch, bool Created);

/// <summary>
/// ALV-018-C02 slice 1: takes an import file in. Who may do it is decided by the permission on the controller. The order is deliberate:
/// 1. the request is checked (kind, label), then the whole file is read as untrusted text (<see cref="CsvParser"/>); a file with any problem stores nothing, is logged as <c>ImportFileRejected</c> (who, the kind, the
///    number of problems and their codes, never a value from the file) and comes back as <see cref="ImportFileRejectedException"/> with the problems listed;
/// 2. a sound file is hashed; if an open batch for the same kind and hash exists it is returned and nothing is made (a repeat, a retry after an uncertain answer, or two people sending the same file);
/// 3. otherwise the file is stored in the import store and the batch and its <c>ImportBatchCreated</c> audit entry are saved in ONE save: a batch never exists without its log entry. If the save fails the stored
///    file is deleted again (compensation). If two requests race, the filtered unique index lets one win and the other returns the winner's batch.
/// What happens if this fails: the caller gets an error and no batch exists; a file stored before a crash between steps 3's store and save is left in the import folder (it is never listed by the document scan; a
/// sweep of such files belongs to the clean-up slice). It does not retry by itself: the operator sends the file again, which is safe (step 2). Retry after an uncertain answer returns the same batch.
/// Failure modes handled: invalid kind or label, empty, oversize, non-UTF-8, malformed, over-limit files, repeat, race, a failing store or save. Not handled here: mapping, validation of values, any write to a patient,
/// appointment or ledger (later slices). No external call is made; the only waits are the database and the disk, bounded by the request's cancellation token.
/// </summary>
public class ImportIntakeService(AlveraDbContext db, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock, ILogger<ImportIntakeService> log)
{
    public const int DefaultLimit = 50, MaxLimit = 200;
    private readonly ImportOptions _options = options.Value;

    public async Task<ImportUploadResult> UploadAsync(string? sourceKind, string? label, byte[] content, Guid actor, CancellationToken ct)
    {
        var kind = sourceKind?.Trim();
        if (kind is null || !ImportSourceKind.All.Contains(kind)) throw new ImportException("invalid_source_kind", $"Choose what the file holds: {string.Join(", ", ImportSourceKind.All)}.", 400);
        var cleanLabel = CleanLabel(label);

        var parsed = CsvParser.Parse(content, _options);
        if (parsed.Table is not { } table)
        {
            var codes = parsed.Problems.Select(p => p.Code).Distinct().ToList();
            AuditService.Record(db, AuditEventTypes.ImportFileRejected, nameof(ImportBatch), Guid.NewGuid(), actor,
                $"Import file not accepted ({kind}): {parsed.TotalProblems} problem(s) ({string.Join(", ", codes)}).");
            await db.SaveChangesAsync(ct);                                              // a rejection that cannot be logged is a failure, so it propagates
            throw new ImportFileRejectedException(parsed.Problems, parsed.TotalProblems);
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(content));
        var existing = await FindOpenAsync(kind, sha, ct);
        if (existing is not null) return new ImportUploadResult(existing, false);

        Guid blobId;
        await using (var stream = new MemoryStream(content, writable: false))
        {
            var stored = await files.Blobs.StoreAsync(stream, ct);
            blobId = stored.Id;
            if (stored.Sha256Hash != sha)
            {
                await DeleteStoredAsync(blobId);
                throw new InvalidOperationException("The stored import file does not match the file that was sent.");
            }
        }

        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(), SourceKind = kind, SourceLabel = cleanLabel, Status = ImportBatchStatus.Uploaded, Policy = ImportBatchPolicy.AllOrNothing, FileBlobId = blobId, FileSha256 = sha,
            FileSizeBytes = content.LongLength, RowCount = table.Rows.Count, ColumnCount = table.Headers.Count, ColumnNamesJson = JsonSerializer.Serialize(table.Headers),
            CreatedByUserId = actor, CreatedAtUtc = clock.UtcNow,
        };
        db.ImportBatches.Add(batch);
        AuditService.Record(db, AuditEventTypes.ImportBatchCreated, nameof(ImportBatch), batch.Id, actor,
            $"Import batch created ({kind}): {batch.RowCount} row(s), {batch.ColumnCount} column(s), {batch.FileSizeBytes} bytes, sha256 {sha}.", null, batch.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();                                                   // another request saved the same file first: use its batch
            await DeleteStoredAsync(blobId);
            var winner = await FindOpenAsync(kind, sha, ct) ?? throw new InvalidOperationException("A unique-index conflict was reported but no open batch exists.", ex);
            return new ImportUploadResult(winner, false);
        }
        catch
        {
            await DeleteStoredAsync(blobId);
            throw;
        }

        return new ImportUploadResult((await GetAsync(batch.Id, ct))!, true);
    }

    public async Task<ImportBatchView?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await QueryRows().Where(x => x.Batch.Id == id).SingleOrDefaultAsync(ct);
        return row is null ? null : ToView(row.Batch, row.Name);
    }

    public async Task<IReadOnlyList<ImportBatchView>> ListAsync(int limit, DateTimeOffset? before, CancellationToken ct)
    {
        var q = QueryRows();
        if (before is { } cursor) q = q.Where(x => x.Batch.CreatedAtUtc < cursor);
        var rows = await q.OrderByDescending(x => x.Batch.CreatedAtUtc).ThenByDescending(x => x.Batch.Id).Take(Math.Clamp(limit, 1, MaxLimit)).ToListAsync(ct);
        return rows.Select(r => ToView(r.Batch, r.Name)).ToList();
    }

    private IQueryable<BatchRow> QueryRows() =>
        db.ImportBatches.AsNoTracking().Select(b => new BatchRow { Batch = b, Name = db.StaffProfiles.Where(s => s.UserAccountId == b.CreatedByUserId).Select(s => s.DisplayName).FirstOrDefault() });

    private sealed class BatchRow
    {
        public required ImportBatch Batch { get; init; }
        public string? Name { get; init; }
    }

    private static ImportBatchView ToView(ImportBatch b, string? name) => new(
        b.Id, b.SourceKind, b.SourceLabel, b.Status, b.Policy, b.FileSha256, b.FileSizeBytes, b.RowCount, JsonSerializer.Deserialize<List<string>>(b.ColumnNamesJson) ?? [],
        b.AttemptedRows, b.AcceptedRows, b.RejectedRows, b.SkippedRows, b.UnresolvedRows, name ?? "Unknown staff member", b.CreatedAtUtc,
        b.CommitOutcome, b.CommitStartedAtUtc, b.CommittedAtUtc, b.CommitRound, b.MappingJson is not null, b.DryRunAtUtc);

    private async Task<ImportBatchView?> FindOpenAsync(string kind, string sha, CancellationToken ct)
    {
        var id = await db.ImportBatches.AsNoTracking().Where(b => b.SourceKind == kind && b.FileSha256 == sha && ImportBatchStatus.Open.Contains(b.Status)).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await GetAsync(id.Value, ct);
    }

    private static string CleanLabel(string? label)
    {
        var text = label?.Trim() ?? "";
        if (text.Length == 0) throw new ImportException("invalid_label", "Say where the file came from, for example \"Old system export, October\".", 400);
        if (text.Length > ImportLimits.LabelMax) throw new ImportException("invalid_label", $"The source name can be at most {ImportLimits.LabelMax} characters.", 400);
        if (text.Any(char.IsControl)) throw new ImportException("invalid_label", "The source name cannot contain control characters.", 400);
        return text;
    }

    /// <summary>A unique-index violation (2601 or 2627) anywhere in the chain of inner exceptions: EF may hand the error back wrapped by its execution strategy.</summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SqlException { Number: 2601 or 2627 }) return true;
        return false;
    }

    /// <summary>Deletes a file stored for a batch that was not saved. A deletion that fails is logged by class and id (never swallowed) and leaves the file for the clean-up slice; it never replaces the error being handled.</summary>
    private async Task DeleteStoredAsync(Guid blobId)
    {
        try { await files.Blobs.DeleteAsync(blobId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError("event=import_file_cleanup_failed outcome=failure error_class={ErrorClass} blob_id={BlobId}", ex.GetType().Name, blobId);
        }
    }
}
