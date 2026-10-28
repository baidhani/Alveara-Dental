using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

public sealed record ImportDryRunResult(ImportBatchView Batch, int Valid, int Rejected, int Skipped, int Unresolved);

/// <summary>One row's outcome as the screens show it: no value from the file, only what to correct (the front desk's own field messages) or whom it matches.</summary>
public sealed record ImportRowView(int RowNumber, string Status, string? Reason, IReadOnlyDictionary<string, string>? Errors, IReadOnlyList<Guid> MatchedPatientIds, int? DuplicateOfRow, Guid? ResultPatientId, bool Decided);

/// <summary>
/// ALV-018-C02 slice 2: the dry-run of a patient file. It does every material check that registration would do, and writes no patient: only the batch's row results and one audit entry, in ONE transaction, so a
/// dry-run either replaces the earlier results completely or changes nothing. The order is deliberate:
/// 1. the batch must be open, hold patients and have a confirmed mapping; the stored file is read back and its SHA-256 must equal the one recorded at upload (a changed or missing file is refused, never guessed at);
/// 2. every row is judged by the front desk's own rules (<see cref="PatientInput"/>, with the practice's requirements): all of a row's problems are reported at once, as the same messages, never a value;
/// 3. a valid row is compared with the registered patients and the rows before it, with the same deterministic rules as registration's duplicate check (<see cref="PatientDuplicateDetector.Compare"/>), in bulk (one
///    query per group of birth dates, not one per row): the same name and birth date is Skipped (already registered, or a repeat of an earlier row), a likely match is Unresolved (a person decides, see
///    <see cref="ImportDecisionService"/>), nothing is ever merged; otherwise the row is Valid. Rows that repeat an earlier row are compared with the earlier rows that could still register, so only the first of a group can be Valid.
/// 4. (slice 3) a decision a person already made about a likely duplicate is kept when the row is still a likely duplicate (a reviewed new person is Valid, a reviewed same person is Skipped); a row that is no longer a
///    likely duplicate (it is now an exact match, or invalid) loses its decision. Saving a mapping clears every decision, see <see cref="ImportMappingService"/>.
/// Running it again gives the same results for the same file, mapping, decisions and registered patients. Failure modes handled: no such batch, an unsupported kind, a finished or committing batch, no mapping, a
/// changed or missing stored file, a concurrent change (409 batch_changed) or a concurrent dry-run (409 dry_run_in_progress); nothing partial is ever saved. Not handled: a patient registered by someone else between
/// the dry-run and the commit (the commit checks again, see <see cref="ImportCommitEngine"/>). No external call is made; the waits are the database and the disk, bounded by the request's cancellation token.
/// </summary>
public class ImportDryRunService(AlveraDbContext db, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock, ImportMappingService mappings, ImportIntakeService intake)
{
    private const int ChunkSize = 1_000, MaxMatched = 10, DefaultLimit = 100, MaxLimit = 500;
    private readonly ImportOptions _options = options.Value;

    private sealed record Outcome(int RowNumber, string Status, string? Reason, IReadOnlyDictionary<string, string>? Errors, IReadOnlyList<Guid>? Matched, int? DuplicateOfRow, bool KeepDecision = false);

    private sealed record EarlierRow(int RowNumber, DuplicateProbe Probe);

    public async Task<ImportDryRunResult> RunAsync(Guid batchId, Guid actor, CancellationToken ct)
    {
        var batch = await mappings.RequireBatchAsync(batchId, tracked: true, ct);
        if (batch.MappingJson is null) throw new ImportException("mapping_required", "Confirm the column mapping before the dry-run.", 409);
        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(batch.MappingJson) ?? [];
        var table = await ImportPatientRows.ReadTableAsync(files, batch, _options, ct);
        var requirements = await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct);

        var outcomes = await EvaluateAsync(table, mapping, requirements, ct);
        await KeepDecisionsAsync(batch.Id, outcomes, ct);
        await SaveAsync(batch, outcomes, actor, ct);

        var counts = outcomes.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count());
        int Count(string status) => counts.GetValueOrDefault(status);
        return new ImportDryRunResult((await intake.GetAsync(batch.Id, ct))!, Count(ImportRowStatus.Valid), Count(ImportRowStatus.Rejected), Count(ImportRowStatus.Skipped), Count(ImportRowStatus.Unresolved));
    }

    public async Task<IReadOnlyList<ImportRowView>> ListRowsAsync(Guid batchId, string? status, int limit, int after, CancellationToken ct)
    {
        var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new ImportException("batch_not_found", "There is no import batch with that id.", 404);
        if (status is not null && !ImportRowStatus.All.Contains(status)) throw new ImportException("invalid_status", $"The status must be one of {string.Join(", ", ImportRowStatus.All)}.", 400);
        if (batch.DryRunAtUtc is null) throw new ImportException("dry_run_required", "Run the dry-run first; there are no results to show.", 409);

        var query = db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batchId && r.RowNumber > after);
        if (status is not null) query = query.Where(r => r.Status == status);
        var rows = await query.OrderBy(r => r.RowNumber).Take(Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit)).ToListAsync(ct);
        return rows.Select(r => new ImportRowView(
            r.RowNumber, r.Status, r.Reason, r.ErrorsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(r.ErrorsJson),
            r.MatchedPatientIdsJson is null ? [] : JsonSerializer.Deserialize<List<Guid>>(r.MatchedPatientIdsJson) ?? [], r.DuplicateOfRow, r.ResultRecordId, r.DecisionByUserId is not null)).ToList();
    }

    private async Task<List<Outcome>> EvaluateAsync(CsvTable table, IReadOnlyDictionary<string, string> mapping, PatientRequirements requirements, CancellationToken ct)
    {
        var index = ImportPatientRows.FieldIndex(table, mapping);
        var outcomes = new List<Outcome>(table.Rows.Count);
        var valid = new List<(int RowNumber, CleanPatient Clean)>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var number = i + 2;                                                           // the header is row 1
            try
            {
                valid.Add((number, PatientInput.Validate(ImportPatientRows.FieldsOf(table.Rows[i], index), clock, requirements)));
            }
            catch (PatientException ex) when (ex.Code == "validation_failed")
            {
                outcomes.Add(new Outcome(number, ImportRowStatus.Rejected, ImportRowReason.InvalidFields, ex.FieldErrors, null, null));
            }
        }

        var pools = await ImportPatientRows.LoadPoolsAsync(db, valid.Select(v => v.Clean.DateOfBirth).Distinct().ToList(), ct);
        var earlier = new Dictionary<DateOnly, List<EarlierRow>>();                       // rows before this one, by birth date, that could still register
        foreach (var (number, clean) in valid)
        {
            var probe = DuplicateProbe.From(clean);
            var pool = pools.GetValueOrDefault(clean.DateOfBirth) ?? [];
            var matches = pool.Select(p => (Person: p, Match: PatientDuplicateDetector.Compare(probe, p.FirstName, p.LastName, p.Phone, p.Email))).Where(m => m.Match.Reasons.Count > 0).ToList();
            var before = earlier.GetValueOrDefault(clean.DateOfBirth) ?? [];
            var fileMatches = before.Select(e => (Row: e, Match: PatientDuplicateDetector.Compare(probe, e.Probe.First, e.Probe.Last, e.Probe.PhoneDigits, e.Probe.Email))).Where(m => m.Match.Reasons.Count > 0).ToList();

            if (matches.Any(m => m.Match.Exact))
            {
                outcomes.Add(new Outcome(number, ImportRowStatus.Skipped, ImportRowReason.AlreadyRegistered, null, matches.Where(m => m.Match.Exact).Select(m => m.Person.Id).Take(MaxMatched).ToList(), null));
                continue;
            }

            if (fileMatches.FirstOrDefault(m => m.Match.Exact) is { Row: not null } repeat)
            {
                outcomes.Add(new Outcome(number, ImportRowStatus.Skipped, ImportRowReason.DuplicateInFile, null, null, repeat.Row.RowNumber));
                continue;
            }

            if (matches.Count > 0 || fileMatches.Count > 0)
            {
                var ids = matches.OrderByDescending(m => m.Match.Reasons.Count).ThenBy(m => m.Person.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Person.FirstName, StringComparer.OrdinalIgnoreCase)
                    .Select(m => m.Person.Id).Take(MaxMatched).ToList();
                outcomes.Add(new Outcome(number, ImportRowStatus.Unresolved, ImportRowReason.PossibleDuplicate, null, ids, fileMatches.Count > 0 ? fileMatches[0].Row.RowNumber : null));
            }
            else
            {
                outcomes.Add(new Outcome(number, ImportRowStatus.Valid, null, null, null, null));
            }

            if (!earlier.TryGetValue(clean.DateOfBirth, out var list)) earlier[clean.DateOfBirth] = list = [];
            list.Add(new EarlierRow(number, probe));
        }

        return outcomes.OrderBy(o => o.RowNumber).ToList();
    }

    /// <summary>A person's decision about a likely duplicate survives a new dry-run while the row is still a likely duplicate: a reviewed new person comes back Valid and a reviewed same person Skipped.</summary>
    private async Task KeepDecisionsAsync(Guid batchId, List<Outcome> outcomes, CancellationToken ct)
    {
        var decided = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batchId && r.DecisionByUserId != null).ToDictionaryAsync(r => r.RowNumber, r => r.Reason, ct);
        if (decided.Count == 0) return;
        for (var i = 0; i < outcomes.Count; i++)
        {
            var o = outcomes[i];
            if (o.Status != ImportRowStatus.Unresolved || !decided.TryGetValue(o.RowNumber, out var reason)) continue;
            outcomes[i] = reason == ImportRowReason.ReviewedNewPerson ? o with { Status = ImportRowStatus.Valid, Reason = reason, KeepDecision = true }
                : reason == ImportRowReason.ReviewedSamePerson ? o with { Status = ImportRowStatus.Skipped, Reason = reason, KeepDecision = true } : o;
        }
    }

    private async Task SaveAsync(ImportBatch batch, IReadOnlyList<Outcome> outcomes, Guid actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var chunk in outcomes.Chunk(ChunkSize))
            {
                var numbers = chunk.Select(o => o.RowNumber).ToList();
                var existing = await db.ImportBatchRows.Where(r => r.BatchId == batch.Id && numbers.Contains(r.RowNumber)).ToDictionaryAsync(r => r.RowNumber, ct);
                foreach (var o in chunk)
                {
                    if (!existing.TryGetValue(o.RowNumber, out var row))
                    {
                        row = new ImportBatchRow { Id = Guid.NewGuid(), BatchId = batch.Id, RowNumber = o.RowNumber, Status = o.Status };
                        db.ImportBatchRows.Add(row);
                    }

                    row.Status = o.Status;
                    row.Reason = o.Reason;
                    row.ErrorsJson = o.Errors is null ? null : JsonSerializer.Serialize(o.Errors);
                    row.MatchedPatientIdsJson = o.Matched is { Count: > 0 } ? JsonSerializer.Serialize(o.Matched) : null;
                    row.DuplicateOfRow = o.DuplicateOfRow;
                    row.ResultRecordId = null;                                           // nothing is committed by a dry-run
                    if (!o.KeepDecision) { row.DecisionByUserId = null; row.DecidedAtUtc = null; }
                    row.UpdatedAtUtc = now;
                }

                await db.SaveChangesAsync(ct);
                foreach (var entry in db.ChangeTracker.Entries<ImportBatchRow>().ToList()) entry.State = EntityState.Detached;      // keep memory bounded for a large file
            }

            int Count(string status) => outcomes.Count(o => o.Status == status);
            batch.AttemptedRows = outcomes.Count;
            batch.AcceptedRows = 0;                                                       // nothing is committed by a dry-run
            batch.RejectedRows = Count(ImportRowStatus.Rejected);
            batch.SkippedRows = Count(ImportRowStatus.Skipped);
            batch.UnresolvedRows = Count(ImportRowStatus.Unresolved);
            batch.Status = ImportBatchStatus.DryRunComplete;
            batch.DryRunAtUtc = now;
            batch.CommitOutcome = null;                                                   // a refused commit is answered by this fresh dry-run
            batch.UpdatedAtUtc = now;
            AuditService.Record(db, AuditEventTypes.ImportDryRunCompleted, nameof(ImportBatch), batch.Id, actor,
                $"Import dry-run completed ({batch.SourceKind}): {outcomes.Count} row(s), {Count(ImportRowStatus.Valid)} valid, {batch.RejectedRows} rejected, {batch.SkippedRows} skipped, {batch.UnresolvedRows} unresolved.", null, batch.Id);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ImportException("batch_changed", "Someone else changed this batch. Reload it and try again.", 409);
        }
        catch (Exception ex) when (IsLostRace(ex))
        {
            throw new ImportException("dry_run_in_progress", "A dry-run of this batch is already running. Wait for it and look at its results.", 409);
        }
    }

    /// <summary>
    /// A row seen twice by two dry-runs (unique index 2601 or 2627), or the loser of a deadlock between them (1205). EF may hand the error back wrapped (a <see cref="DbUpdateException"/>, itself inside the
    /// "transient failure" <see cref="InvalidOperationException"/> of its execution strategy), so the whole chain of inner exceptions is looked at, not only the first.
    /// </summary>
    private static bool IsLostRace(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SqlException { Number: 2601 or 2627 or 1205 }) return true;
        return false;
    }
}
