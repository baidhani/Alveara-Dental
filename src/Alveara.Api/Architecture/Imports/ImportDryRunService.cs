using System.Security.Cryptography;
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
public sealed record ImportRowView(int RowNumber, string Status, string? Reason, IReadOnlyDictionary<string, string>? Errors, IReadOnlyList<Guid> MatchedPatientIds, int? DuplicateOfRow);

/// <summary>
/// ALV-018-C02 slice 2: the dry-run of a patient file. It does every material check that registration would do, and writes no patient: only the batch's row results and one audit entry, in ONE transaction, so a
/// dry-run either replaces the earlier results completely or changes nothing. The order is deliberate:
/// 1. the batch must be open, hold patients and have a confirmed mapping; the stored file is read back and its SHA-256 must equal the one recorded at upload (a changed or missing file is refused, never guessed at);
/// 2. every row is judged by the front desk's own rules (<see cref="PatientInput"/>, with the practice's requirements): all of a row's problems are reported at once, as the same messages, never a value;
/// 3. a valid row is compared with the registered patients and the rows before it, with the same deterministic rules as registration's duplicate check (<see cref="PatientDuplicateDetector.Compare"/>), in bulk (one
///    query per group of birth dates, not one per row): the same name and birth date is Skipped (already registered, or a repeat of an earlier row), a likely match is Unresolved (a person decides, a later slice),
///    nothing is ever merged; otherwise the row is Valid. Rows that repeat an earlier row are compared with the earlier rows that could still register, so only the first of a group can be Valid.
/// Running it again gives the same results for the same file, mapping and registered patients. Failure modes handled: no such batch, an unsupported kind, a finished batch, no mapping, a changed or missing
/// stored file, a concurrent change (409 batch_changed) or a concurrent dry-run (409 dry_run_in_progress); nothing partial is ever saved. Not handled: a patient registered by someone else between the dry-run and
/// the commit (the commit checks again, a later slice). No external call is made; the waits are the database and the disk, bounded by the request's cancellation token.
/// </summary>
public class ImportDryRunService(AlveraDbContext db, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock, ImportMappingService mappings, ImportIntakeService intake)
{
    private const int ChunkSize = 1_000, PoolCap = 500, MaxMatched = 10, DefaultLimit = 100, MaxLimit = 500;
    private readonly ImportOptions _options = options.Value;

    private sealed record Outcome(int RowNumber, string Status, string? Reason, IReadOnlyDictionary<string, string>? Errors, IReadOnlyList<Guid>? Matched, int? DuplicateOfRow);

    private sealed record Existing(Guid Id, DateOnly DateOfBirth, string FirstName, string LastName, string Phone, string? Email);

    private sealed record EarlierRow(int RowNumber, DuplicateProbe Probe);

    public async Task<ImportDryRunResult> RunAsync(Guid batchId, Guid actor, CancellationToken ct)
    {
        var batch = await mappings.RequireBatchAsync(batchId, tracked: true, ct);
        if (batch.MappingJson is null) throw new ImportException("mapping_required", "Confirm the column mapping before the dry-run.", 409);
        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(batch.MappingJson) ?? [];
        var table = await ReadTableAsync(batch, ct);
        var requirements = await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct);

        var outcomes = await EvaluateAsync(table, mapping, requirements, ct);
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
            r.MatchedPatientIdsJson is null ? [] : JsonSerializer.Deserialize<List<Guid>>(r.MatchedPatientIdsJson) ?? [], r.DuplicateOfRow)).ToList();
    }

    /// <summary>Reads the stored file back and proves it is the file that was uploaded: same bytes (SHA-256), still a sound table.</summary>
    private async Task<CsvTable> ReadTableAsync(ImportBatch batch, CancellationToken ct)
    {
        byte[] content;
        try
        {
            await using var stream = await files.Blobs.OpenReadAsync(batch.FileBlobId, ct);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            content = copy.ToArray();
        }
        catch (FileNotFoundException)
        {
            throw new ImportException("import_file_missing", "The stored import file cannot be found.", 409);
        }

        if (Convert.ToHexStringLower(SHA256.HashData(content)) != batch.FileSha256) throw new ImportException("import_file_changed", "The stored import file no longer matches the file that was uploaded.", 409);
        return CsvParser.Parse(content, _options).Table ?? throw new ImportException("import_file_changed", "The stored import file is no longer a sound table.", 409);
    }

    private async Task<List<Outcome>> EvaluateAsync(CsvTable table, IReadOnlyDictionary<string, string> mapping, PatientRequirements requirements, CancellationToken ct)
    {
        var index = mapping.ToDictionary(kv => kv.Value, kv => table.Headers.ToList().IndexOf(kv.Key));
        string? Cell(string[] row, string field) => index.TryGetValue(field, out var i) && i >= 0 ? row[i] : null;

        var outcomes = new List<Outcome>(table.Rows.Count);
        var valid = new List<(int RowNumber, CleanPatient Clean)>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            var number = i + 2;                                                           // the header is row 1
            var fields = new PatientFields(Cell(row, "firstName"), Cell(row, "middleName"), Cell(row, "lastName"), Cell(row, "dateOfBirth"), Cell(row, "sex"), Cell(row, "phone"), Cell(row, "email"),
                Cell(row, "addressLine1"), Cell(row, "addressLine2"), Cell(row, "city"), Cell(row, "state"), Cell(row, "postalCode"));
            try
            {
                valid.Add((number, PatientInput.Validate(fields, clock, requirements)));
            }
            catch (PatientException ex) when (ex.Code == "validation_failed")
            {
                outcomes.Add(new Outcome(number, ImportRowStatus.Rejected, ImportRowReason.InvalidFields, ex.FieldErrors, null, null));
            }
        }

        var pools = await LoadPoolsAsync(valid.Select(v => v.Clean.DateOfBirth).Distinct().ToList(), ct);
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

    // An earlier row is compared through its probe: its names are already normalized and its phone is digits only, which the comparison normalizes again harmlessly.

    /// <summary>The registered patients for a set of birth dates, in groups (one query per group of dates), at most <see cref="PoolCap"/> for each date as in the front desk's check.</summary>
    private async Task<Dictionary<DateOnly, List<Existing>>> LoadPoolsAsync(IReadOnlyList<DateOnly> dates, CancellationToken ct)
    {
        var pools = new Dictionary<DateOnly, List<Existing>>();
        foreach (var chunk in dates.Chunk(ChunkSize))
        {
            var found = await db.Patients.AsNoTracking().Where(p => chunk.Contains(p.DateOfBirth)).OrderBy(p => p.Id)
                .Select(p => new Existing(p.Id, p.DateOfBirth, p.FirstName, p.LastName, p.Phone, p.Email)).ToListAsync(ct);
            foreach (var group in found.GroupBy(p => p.DateOfBirth)) pools[group.Key] = group.Take(PoolCap).ToList();
        }

        return pools;
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
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 })        // a row seen twice by two dry-runs, or the loser of a deadlock between them
        {
            throw new ImportException("dry_run_in_progress", "A dry-run of this batch is already running. Wait for it and look at its results.", 409);
        }
    }
}
