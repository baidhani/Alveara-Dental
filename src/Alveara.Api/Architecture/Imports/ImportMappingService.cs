using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>A mapping that was not saved because of what is in it. <see cref="Problems"/> lists what to correct.</summary>
public sealed class ImportMappingRejectedException(IReadOnlyList<ImportProblem> problems) : Exception("The mapping was not saved. Correct the problems listed and send it again.")
{
    public IReadOnlyList<ImportProblem> Problems { get; } = problems;
}

/// <summary>
/// What the mapping step shows: the fields a column can fill and which are required, the file's columns, the suggested mapping, the confirmed mapping (null until saved), the columns nothing fills (they will be
/// ignored, which the screen says) and the required fields nothing fills yet.
/// </summary>
public sealed record MappingSuggestion(
    IReadOnlyList<ImportField> Fields, IReadOnlyList<string> Columns, IReadOnlyDictionary<string, string> Suggested, IReadOnlyDictionary<string, string>? Current,
    IReadOnlyList<string> UnmappedColumns, IReadOnlyList<string> MissingRequired);

/// <summary>
/// ALV-018-C02 slice 2: the mapping of a patient file's columns to patient fields. The server is the authority: a saved mapping has been checked (<see cref="PatientImportFields.CheckMapping"/>) against the file's
/// own header and the practice's requirements, so a client that ignores the suggestion is still refused with a precise problem. Only a batch that is still open (Uploaded or DryRunComplete) and holds patients can be
/// mapped; changing the mapping of a batch that has been dry-run makes its results stale, so the batch goes back to Uploaded (a new dry-run is needed) and the row results are replaced by the next one. A mapping
/// names columns and fields, never a value. Failure modes handled: no such batch, a kind not supported yet, a finished batch, a bad mapping, two people saving at once (the row version: 409 batch_changed).
/// </summary>
public class ImportMappingService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<MappingSuggestion> GetSuggestionAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await RequireBatchAsync(batchId, tracked: false, ct);
        return await BuildAsync(batch, ct);
    }

    public async Task<MappingSuggestion> SaveAsync(Guid batchId, IReadOnlyDictionary<string, string?>? mapping, Guid actor, CancellationToken ct)
    {
        if (mapping is null || mapping.Count == 0) throw new ImportException("invalid_mapping", "Send the mapping of columns to fields.", 400);
        var batch = await RequireBatchAsync(batchId, tracked: true, ct);
        var columns = ColumnsOf(batch);
        var requirements = await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct);

        var problems = PatientImportFields.CheckMapping(mapping, columns, requirements);
        if (problems.Count > 0) throw new ImportMappingRejectedException(problems);

        var clean = mapping.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value!.Trim());
        var now = clock.UtcNow;
        var staleDecisions = false;
        batch.MappingJson = JsonSerializer.Serialize(clean);
        if (batch.Status == ImportBatchStatus.DryRunComplete)                            // the results were for another mapping: a new dry-run is needed
        {
            batch.Status = ImportBatchStatus.Uploaded;
            batch.DryRunAtUtc = null;
            batch.AttemptedRows = batch.AcceptedRows = batch.RejectedRows = batch.SkippedRows = batch.UnresolvedRows = 0;
            batch.CommitOutcome = null;
            staleDecisions = true;
        }

        batch.UpdatedAtUtc = now;
        AuditService.Record(db, AuditEventTypes.ImportMappingSaved, nameof(ImportBatch), batch.Id, actor, $"Import mapping saved ({batch.SourceKind}): {clean.Count} of {columns.Count} column(s) mapped.", null, batch.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ImportException("batch_changed", "Someone else changed this batch. Reload it and try again.", 409);
        }

        if (staleDecisions)                                                              // a decision was about a row as the old mapping read it
            await db.ImportBatchRows.Where(r => r.BatchId == batch.Id && r.DecisionByUserId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.DecisionByUserId, (Guid?)null).SetProperty(r => r.DecidedAtUtc, (DateTimeOffset?)null), ct);
        return await BuildAsync(batch, ct);
    }

    internal static IReadOnlyList<string> ColumnsOf(ImportBatch batch) => JsonSerializer.Deserialize<List<string>>(batch.ColumnNamesJson) ?? [];

    private async Task<MappingSuggestion> BuildAsync(ImportBatch batch, CancellationToken ct)
    {
        var columns = ColumnsOf(batch);
        var requirements = await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct);
        var current = batch.MappingJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(batch.MappingJson);
        var fields = PatientImportFields.For(requirements);
        var suggested = PatientImportFields.Suggest(columns);
        var effective = current ?? new Dictionary<string, string>(suggested);              // until a mapping is saved, what the screen would start from
        return new MappingSuggestion(fields, columns, suggested, current, columns.Where(c => !effective.ContainsKey(c)).ToList(),
            fields.Where(f => f.Required && !effective.Values.Contains(f.Key)).Select(f => f.Key).ToList());
    }

    /// <summary>Loads a batch that this step may work on: it exists, holds patients (the other kinds arrive in later slices) and is still open.</summary>
    internal async Task<ImportBatch> RequireBatchAsync(Guid id, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.ImportBatches : db.ImportBatches.AsNoTracking();
        var batch = await query.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new ImportException("batch_not_found", "There is no import batch with that id.", 404);
        if (batch.SourceKind != ImportSourceKind.Patients) throw new ImportException("kind_not_supported_yet", $"Mapping and dry-run are not available for {batch.SourceKind} files yet.", 409);
        if (batch.Status is not (ImportBatchStatus.Uploaded or ImportBatchStatus.DryRunComplete)) throw new ImportException("batch_not_open", $"This batch is {batch.Status} and can no longer be changed.", 409);
        return batch;
    }
}
