using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>What a person decides about a row that may be a registered person: it is a different person (register it), or the same person (skip it).</summary>
public static class ImportDecision
{
    public const string RegisterAsNew = "RegisterAsNew";
    public const string Skip = "Skip";
    public static readonly IReadOnlyList<string> All = [RegisterAsNew, Skip];
}

public sealed record ImportDecisionResult(ImportRowView Row, ImportBatchView Batch);

/// <summary>
/// ALV-018-C02 slice 3: the queue of likely duplicates. A row the dry-run left Unresolved (it looks like someone already registered, or like an earlier row of the file) waits for a person: <c>RegisterAsNew</c> says it is a different
/// person (the row becomes Valid and will be registered even though it resembles another), <c>Skip</c> says it is the same person (the row becomes Skipped and nothing is registered). Nothing is ever merged here: an exact match (the
/// same name and birth date) is never a queue item, the dry-run already skipped it. The decision is recorded with who made it and when, and the batch counters follow. A decided row may be decided again (changed between the two answers)
/// while the batch is still waiting for its commit; saving a new mapping clears every decision (the rows are read afresh), and a new dry-run keeps a decision only while its row is still a likely duplicate. Repeating the same decision
/// changes nothing. Failure modes handled: an unknown decision, batch or row, a batch that is not waiting for its commit, a row that is not a likely duplicate, two people at once (409 batch_changed). No external call is made.
/// </summary>
public class ImportDecisionService(AlveraDbContext db, IPracticeClock clock, ImportIntakeService intake)
{
    public async Task<ImportDecisionResult> DecideAsync(Guid batchId, int rowNumber, string? decision, Guid actor, CancellationToken ct)
    {
        if (decision is null || !ImportDecision.All.Contains(decision)) throw new ImportException("invalid_decision", $"The decision must be one of {string.Join(", ", ImportDecision.All)}.", 400);
        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new ImportException("batch_not_found", "There is no import batch with that id.", 404);
        if (batch.SourceKind != ImportSourceKind.Patients) throw new ImportException("kind_not_supported_yet", $"Decisions are not available for {batch.SourceKind} files yet.", 409);
        if (batch.Status == ImportBatchStatus.Uploaded) throw new ImportException("dry_run_required", "Run the dry-run first; there are no results to decide on.", 409);
        if (batch.Status != ImportBatchStatus.DryRunComplete) throw new ImportException("batch_not_open", $"This batch is {batch.Status}; its rows can no longer be decided.", 409);
        var row = await db.ImportBatchRows.SingleOrDefaultAsync(r => r.BatchId == batchId && r.RowNumber == rowNumber, ct) ?? throw new ImportException("row_not_found", "There is no such row in this batch.", 404);

        var reviewed = row.Reason is ImportRowReason.ReviewedNewPerson or ImportRowReason.ReviewedSamePerson;
        if (!(reviewed || (row.Status == ImportRowStatus.Unresolved && row.Reason == ImportRowReason.PossibleDuplicate)))
            throw new ImportException("row_not_reviewable", "Only a row that may be a registered person can be decided.", 409);

        var (status, reason) = decision == ImportDecision.RegisterAsNew ? (ImportRowStatus.Valid, ImportRowReason.ReviewedNewPerson) : (ImportRowStatus.Skipped, ImportRowReason.ReviewedSamePerson);
        if (row.Status == status && row.Reason == reason) return await ResultAsync(row, batchId, ct);              // the same decision again: nothing changes

        var wasUnresolved = row.Status == ImportRowStatus.Unresolved;
        var wasSkipped = row.Status == ImportRowStatus.Skipped;
        var now = clock.UtcNow;
        row.Status = status;
        row.Reason = reason;
        row.DecisionByUserId = actor;
        row.DecidedAtUtc = now;
        row.UpdatedAtUtc = now;
        if (wasUnresolved) batch.UnresolvedRows--;
        if (wasSkipped) batch.SkippedRows--;
        if (status == ImportRowStatus.Skipped) batch.SkippedRows++;
        batch.UpdatedAtUtc = now;
        AuditService.Record(db, AuditEventTypes.ImportRowDecided, nameof(ImportBatch), batch.Id, actor, $"Import row {rowNumber} decided: {decision}.", null, batch.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ImportException("batch_changed", "Someone else changed this batch. Reload it and try again.", 409);
        }

        return await ResultAsync(row, batchId, ct);
    }

    private async Task<ImportDecisionResult> ResultAsync(ImportBatchRow row, Guid batchId, CancellationToken ct) => new(
        new ImportRowView(row.RowNumber, row.Status, row.Reason, row.ErrorsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(row.ErrorsJson),
            row.MatchedPatientIdsJson is null ? [] : JsonSerializer.Deserialize<List<Guid>>(row.MatchedPatientIdsJson) ?? [], row.DuplicateOfRow, row.ResultRecordId, row.DecisionByUserId is not null),
        (await intake.GetAsync(batchId, ct))!);
}
