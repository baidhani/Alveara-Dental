using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 3: the work of a commit, one job at a time. It only STAGES changes on the context it is given (the background runner's, which saves them together with the job's receipt), never saving itself.
/// <b>rows</b> step: for the rows of its window that are still Valid, it judges each row again as it stands NOW (a dry-run can be stale): the front desk's field rules (the practice's requirements may have changed), then
/// the registered patients with the same birth date (someone may have registered the person since): the same name and birth date is Skipped, a likely match is Unresolved unless a person decided it is a different person, and
/// only a row that passes is registered, through the same patient-building step as the front desk (<see cref="PatientRegistrationService.StagePatient"/>) under the key <c>import-{batch}-{row}</c>, so replaying the row can never make a second patient
/// (a patient already under that key is simply recorded as the row's result). With the all-or-nothing policy the window is the whole file and ONE row that does not pass refuses the whole commit: nothing is registered, the batch goes
/// back to needing a dry-run (outcome Refused). <b>finish</b> step: waits (by putting its successor on the queue) while any rows job of the round is still pending or running, then settles the batch: the counters are recounted from the
/// rows, a batch with no valid row left is Committed (Completed, or CompletedWithExceptions when rows were rejected, skipped or left unresolved), one that still has valid rows (a rows job failed for good) goes back to waiting for a commit
/// (Interrupted) and keeps what was registered. A job of an old round, or for a batch that is no longer committing, does nothing. Failure modes handled: a stale dry-run, a duplicate registered meanwhile, a replayed job, a refused
/// all-or-nothing, a failed job. Not handled: the stored file changing during a commit (the job throws, is retried, and ends Failed, which the finish step reports as Interrupted). The audit entries carry ids and counts, never a value.
/// </summary>
public sealed class ImportCommitEngine(AlveraDbContext db, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock)
{
    private const int MaxMatched = 10, MaxFinishWaits = 20_000;
    private readonly ImportOptions _options = options.Value;

    private enum Verdict { Register, AlreadyRegistered, Replay, Likely, Invalid }

    private sealed record Plan(ImportBatchRow Row, Verdict Verdict, CleanPatient? Clean, IReadOnlyDictionary<string, string>? Errors, IReadOnlyList<Guid> Matched);

    public Task ExecuteAsync(ImportCommitJob job, CancellationToken ct) => job.Step == ImportCommitJobs.FinishStep ? FinishAsync(job, ct) : RowsAsync(job, ct);

    private async Task<ImportBatch?> CommittingBatchAsync(ImportCommitJob job, CancellationToken ct)
    {
        var batch = await db.ImportBatches.SingleOrDefaultAsync(b => b.Id == job.BatchId, ct);
        return batch is { Status: ImportBatchStatus.Committing } && batch.CommitRound == job.Round ? batch : null;
    }

    private async Task RowsAsync(ImportCommitJob job, CancellationToken ct)
    {
        var batch = await CommittingBatchAsync(job, ct);
        if (batch is null) return;                                                       // refused, finished or an older round: nothing to do
        var rows = await db.ImportBatchRows.Where(r => r.BatchId == batch.Id && r.RowNumber >= job.FromRow && r.RowNumber <= job.ToRow && r.Status == ImportRowStatus.Valid).OrderBy(r => r.RowNumber).ToListAsync(ct);
        if (rows.Count == 0) return;                                                     // already done (a replay) or nothing valid in this window

        var actor = batch.CommitConfirmedByUserId ?? throw new InvalidOperationException("A committing batch records who confirmed it.");
        var table = await ImportPatientRows.ReadTableAsync(files, batch, _options, ct);
        var index = ImportPatientRows.FieldIndex(table, JsonSerializer.Deserialize<Dictionary<string, string>>(batch.MappingJson ?? "{}") ?? []);
        var requirements = await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct);

        var plans = await PlanAsync(batch, rows, table, index, requirements, ct);
        if (batch.Policy == ImportBatchPolicy.AllOrNothing && plans.Any(p => p.Verdict is Verdict.AlreadyRegistered or Verdict.Likely or Verdict.Invalid))
        {
            Refuse(batch, plans, actor);
            return;
        }

        var registration = new PatientRegistrationService(db, clock);
        var now = clock.UtcNow;
        foreach (var plan in plans)
        {
            var row = plan.Row;
            switch (plan.Verdict)
            {
                case Verdict.Register:
                    var patient = registration.StagePatient(plan.Clean!, ImportPatientRows.RegistrationKey(batch.Id, row.RowNumber), actor, $"Patient registered by import batch {batch.Id}, row {row.RowNumber}.", batch.Id);
                    row.Status = ImportRowStatus.Accepted;
                    row.ResultRecordId = patient.Id;
                    break;
                case Verdict.Replay:
                    row.Status = ImportRowStatus.Accepted;                                // registered by an earlier attempt of this very row
                    row.ResultRecordId = plan.Matched[0];
                    break;
                case Verdict.AlreadyRegistered:
                    row.Status = ImportRowStatus.Skipped;
                    row.Reason = ImportRowReason.AlreadyRegistered;
                    row.MatchedPatientIdsJson = JsonSerializer.Serialize(plan.Matched);
                    break;
                case Verdict.Likely:
                    row.Status = ImportRowStatus.Unresolved;
                    row.Reason = ImportRowReason.PossibleDuplicate;
                    row.MatchedPatientIdsJson = JsonSerializer.Serialize(plan.Matched);
                    row.DecisionByUserId = null;
                    row.DecidedAtUtc = null;
                    break;
                default:
                    row.Status = ImportRowStatus.Rejected;
                    row.Reason = ImportRowReason.InvalidFields;
                    row.ErrorsJson = JsonSerializer.Serialize(plan.Errors);
                    break;
            }

            row.UpdatedAtUtc = now;
        }
    }

    private async Task<List<Plan>> PlanAsync(ImportBatch batch, List<ImportBatchRow> rows, CsvTable table, IReadOnlyDictionary<string, int> index, PatientRequirements requirements, CancellationToken ct)
    {
        var plans = new List<Plan>(rows.Count);
        var valid = new List<(ImportBatchRow Row, CleanPatient Clean)>();
        foreach (var row in rows)
        {
            try
            {
                valid.Add((row, PatientInput.Validate(ImportPatientRows.FieldsOf(table.Rows[row.RowNumber - 2], index), clock, requirements)));
            }
            catch (PatientException ex) when (ex.Code == "validation_failed")
            {
                plans.Add(new Plan(row, Verdict.Invalid, null, ex.FieldErrors, []));
            }
        }

        var keys = valid.Select(v => ImportPatientRows.RegistrationKey(batch.Id, v.Row.RowNumber)).ToList();
        var already = await db.Patients.AsNoTracking().Where(p => keys.Contains(p.RegistrationKey)).Select(p => new { p.RegistrationKey, p.Id }).ToDictionaryAsync(p => p.RegistrationKey, p => p.Id, ct);
        var pools = await ImportPatientRows.LoadPoolsAsync(db, valid.Select(v => v.Clean.DateOfBirth).Distinct().ToList(), ct);
        foreach (var (row, clean) in valid)
        {
            if (already.TryGetValue(ImportPatientRows.RegistrationKey(batch.Id, row.RowNumber), out var id)) { plans.Add(new Plan(row, Verdict.Replay, clean, null, [id])); continue; }

            var probe = DuplicateProbe.From(clean);
            var matches = (pools.GetValueOrDefault(clean.DateOfBirth) ?? []).Select(p => (Person: p, Match: PatientDuplicateDetector.Compare(probe, p.FirstName, p.LastName, p.Phone, p.Email))).Where(m => m.Match.Reasons.Count > 0).ToList();
            if (matches.Any(m => m.Match.Exact)) plans.Add(new Plan(row, Verdict.AlreadyRegistered, clean, null, matches.Where(m => m.Match.Exact).Select(m => m.Person.Id).Take(MaxMatched).ToList()));
            else if (matches.Count > 0 && row.Reason != ImportRowReason.ReviewedNewPerson)
                plans.Add(new Plan(row, Verdict.Likely, clean, null, matches.OrderByDescending(m => m.Match.Reasons.Count).ThenBy(m => m.Person.LastName, StringComparer.OrdinalIgnoreCase).Select(m => m.Person.Id).Take(MaxMatched).ToList()));
            else plans.Add(new Plan(row, Verdict.Register, clean, null, []));
        }

        return plans.OrderBy(p => p.Row.RowNumber).ToList();
    }

    /// <summary>An all-or-nothing commit in which a row is no longer valid: nothing is registered and the batch goes back to needing a dry-run, which will show each row as it is now.</summary>
    private void Refuse(ImportBatch batch, List<Plan> plans, Guid actor)
    {
        var now = clock.UtcNow;
        var changed = plans.Count(p => p.Verdict is Verdict.AlreadyRegistered or Verdict.Likely or Verdict.Invalid);
        batch.Status = ImportBatchStatus.Uploaded;
        batch.DryRunAtUtc = null;
        batch.AttemptedRows = batch.AcceptedRows = batch.RejectedRows = batch.SkippedRows = batch.UnresolvedRows = 0;
        batch.CommitOutcome = ImportCommitOutcome.Refused;
        batch.UpdatedAtUtc = now;
        AuditService.Record(db, AuditEventTypes.ImportCommitRefused, nameof(ImportBatch), batch.Id, actor,
            $"Import commit refused ({batch.Policy}): {changed} of {plans.Count} row(s) are no longer valid; nothing was registered. A new dry-run is needed.", null, batch.Id);
    }

    private async Task FinishAsync(ImportCommitJob job, CancellationToken ct)
    {
        var batch = await CommittingBatchAsync(job, ct);
        if (batch is null) return;
        var running = await db.BackgroundJobs.AsNoTracking().AnyAsync(j => j.JobType == ImportCommitJobs.JobType && j.IdempotencyKey.StartsWith(ImportCommitJobs.RowsKeyPrefix(batch.Id, job.Round))
            && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.InProgress), ct);
        if (running && job.Wait < MaxFinishWaits)
        {
            var next = new ImportCommitJob(batch.Id, job.Round, ImportCommitJobs.FinishStep, 0, 0, job.Wait + 1);
            var key = ImportCommitJobs.FinishKey(batch.Id, job.Round, next.Wait);
            if (!await db.BackgroundJobs.AnyAsync(j => j.IdempotencyKey == key, ct))
                db.BackgroundJobs.Add(new BackgroundJob { Id = Guid.NewGuid(), IdempotencyKey = key, JobType = ImportCommitJobs.JobType, PayloadJson = ImportCommitJobs.Payload(next), Status = BackgroundJobStatus.Pending, CreatedAtUtc = DateTimeOffset.UtcNow });
            return;
        }

        var counts = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batch.Id).GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, ct);
        int Count(string status) => counts.GetValueOrDefault(status);
        var now = clock.UtcNow;
        batch.AttemptedRows = batch.RowCount;
        batch.AcceptedRows = Count(ImportRowStatus.Accepted);
        batch.RejectedRows = Count(ImportRowStatus.Rejected);
        batch.SkippedRows = Count(ImportRowStatus.Skipped);
        batch.UnresolvedRows = Count(ImportRowStatus.Unresolved);
        var leftOver = batch.RejectedRows + batch.SkippedRows + batch.UnresolvedRows;
        if (Count(ImportRowStatus.Valid) > 0)
        {
            batch.Status = ImportBatchStatus.DryRunComplete;                             // some rows were never committed: the commit can be confirmed again
            batch.CommitOutcome = ImportCommitOutcome.Interrupted;
        }
        else
        {
            batch.Status = ImportBatchStatus.Committed;
            batch.CommittedAtUtc = now;
            batch.CommitOutcome = leftOver == 0 ? ImportCommitOutcome.Completed : ImportCommitOutcome.CompletedWithExceptions;
        }

        batch.UpdatedAtUtc = now;
        AuditService.Record(db, AuditEventTypes.ImportCommitCompleted, nameof(ImportBatch), batch.Id, batch.CommitConfirmedByUserId,
            $"Import commit ended ({batch.CommitOutcome}): {batch.AttemptedRows} row(s), {batch.AcceptedRows} accepted, {batch.RejectedRows} rejected, {batch.SkippedRows} skipped, {batch.UnresolvedRows} unresolved, {Count(ImportRowStatus.Valid)} not committed.", null, batch.Id);
    }
}
