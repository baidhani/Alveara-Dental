using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 3, committing a patient file against a real SQL Server, a real folder and the real durable job runner: the confirmation is checked before anything is written (policy, the number of valid rows shown,
/// problems, size); valid rows are registered through the front desk's own patient-building step and every registration is audited with the batch and row (never a value); each row is judged again at commit time, so a
/// person registered since the dry-run is skipped and a likely match is left unresolved, never duplicated; all-or-nothing registers everything or, if a row is no longer valid, nothing at all and asks for a new dry-run;
/// the work is split into windows of rows and a finish job settles the batch (counters reconciled with the rows, outcome); a repeated confirmation, a replayed job, a patient already registered under the row's key and a job
/// recovered after a crash register nothing twice; a job that fails for good leaves the batch Interrupted with what was registered kept; and a person's decisions about likely duplicates move rows between the queues and
/// are honoured by the commit.
/// </summary>
public class ImportCommitTests : IAsyncLifetime
{
    private const string Header = "First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"alveara-import-test-{Guid.NewGuid():N}");
    private readonly IPracticeClock _clock = new PracticeClock(TimeZoneInfo.Utc);
    private readonly ImportOptions _options = new();
    private ImportFileStorage _files = null!;
    private Guid _actor;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _files = new ImportFileStorage(_folder);
        _actor = Guid.NewGuid();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _actor, Username = $"admin-{Guid.NewGuid():N}", Role = Role.Admin, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private IOptions<ImportOptions> Opts => Options.Create(_options);

    private static string Row(string first, string last, string dob, string phone = "555-100-1000", string email = "") => $"{first},{last},{dob},{phone},{email},4 Pine St,Austin,TX,78701";

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", rows) + "\r\n");

    private (ImportIntakeService Intake, ImportMappingService Mapping, ImportDryRunService DryRun, ImportDecisionService Decision, ImportCommitService Commit) Services()
    {
        var db = _fixture.CreateContext();
        var intake = new ImportIntakeService(db, _files, Opts, _clock, NullLogger<ImportIntakeService>.Instance);
        var mapping = new ImportMappingService(db, _clock);
        return (intake, mapping, new ImportDryRunService(db, _files, Opts, _clock, mapping, intake), new ImportDecisionService(db, _clock, intake),
            new ImportCommitService(db, new BackgroundJobQueue(db), _files, Opts, _clock, intake));
    }

    private BackgroundJobRunner Runner() => new(_fixture.CreateContext(), [new ImportCommitJobHandler(_files, Opts, _clock)], NullLogger<BackgroundJobRunner>.Instance);

    /// <summary>Runs the queue until no commit job is waiting any more (a finish job that has to wait puts its successor on the queue, so this takes more than one pass).</summary>
    private async Task DrainAsync()
    {
        for (var i = 0; i < 80; i++)
        {
            var processed = await Runner().ProcessOnceAsync([ImportCommitJobs.JobType], default);
            if (processed == 0 && await CountAsync(db => db.BackgroundJobs.Where(j => j.JobType == ImportCommitJobs.JobType && j.Status == BackgroundJobStatus.Pending)) == 0) return;
        }

        throw new Xunit.Sdk.XunitException("The commit jobs never ran out.");
    }

    private async Task<Guid> DryRunBatchAsync(byte[] csv)
    {
        var s = Services();
        var batch = (await s.Intake.UploadAsync(ImportSourceKind.Patients, "Source", csv, _actor, default)).Batch.Id;
        var suggestion = await s.Mapping.GetSuggestionAsync(batch, default);
        await s.Mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
        await s.DryRun.RunAsync(batch, _actor, default);
        return batch;
    }

    private Task<ImportBatchView> CommitAsync(Guid batch, string? policy, int? expected) => Services().Commit.CommitAsync(batch, policy, expected, _actor, default);

    private async Task<Guid> RegisterAsync(string first, string last, string dob, string phone = "555-100-1000", string? key = null)
    {
        var service = new PatientRegistrationService(_fixture.CreateContext(), _clock);
        var result = await service.RegisterAsync(new RegisterPatientRequest(first, null, last, dob, "Female", phone, null, "1 Main St", null, "Austin", "TX", "78701"), key ?? $"test-{Guid.NewGuid():N}", _actor, default);
        return result.Patient.Id;
    }

    private async Task<int> CountAsync<T>(Func<AlveraDbContext, IQueryable<T>> query) where T : class { await using var db = _fixture.CreateContext(); return await query(db).CountAsync(); }

    private async Task<ImportBatch> BatchAsync(Guid id) { await using var db = _fixture.CreateContext(); return await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id); }

    private async Task<Dictionary<int, ImportBatchRow>> RowsAsync(Guid batch) { await using var db = _fixture.CreateContext(); return await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batch).ToDictionaryAsync(r => r.RowNumber); }

    private static async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<ImportException>(action)).Code;

    private Task<int> AuditAsync(string eventType) => CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == eventType));

    private static readonly byte[] ThreeValid = Csv(Row("Ann", "Rivera", "1950-01-10"), Row("Bo", "Moss", "1962-03-04", "555-300-3000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000"));

    [Fact]
    public async Task Committing_the_valid_rows_registers_each_patient_under_its_key_audits_it_without_a_value_and_reconciles_the_counters()
    {
        var batch = await DryRunBatchAsync(ThreeValid);

        var started = await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 3);

        Assert.Equal((ImportBatchStatus.Committing, 1), (started.Status, started.CommitRound));
        Assert.Equal(0, await CountAsync(db => db.Patients));                                       // nothing is written until the jobs run
        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.Completed, 3, 3, 0, 0, 0), (saved.Status, saved.CommitOutcome, saved.AttemptedRows, saved.AcceptedRows, saved.RejectedRows, saved.SkippedRows, saved.UnresolvedRows));
        Assert.NotNull(saved.CommittedAtUtc);
        var rows = await RowsAsync(batch);
        Assert.All(rows.Values, r => Assert.Equal(ImportRowStatus.Accepted, r.Status));
        await using var db = _fixture.CreateContext();
        var patients = await db.Patients.AsNoTracking().ToListAsync();
        Assert.Equal(rows.Values.Select(r => r.ResultRecordId!.Value).Order(), patients.Select(p => p.Id).Order());
        Assert.Equal(rows.Keys.Select(n => $"import-{batch:N}-{n}").Order(), patients.Select(p => p.RegistrationKey).Order());
        Assert.All(patients, p => Assert.Equal(_actor, p.CreatedByUserId));
        Assert.Equal(3, await AuditAsync(PatientAuditEvents.Registered));
        Assert.Equal((1, 1), (await AuditAsync(AuditEventTypes.ImportCommitStarted), await AuditAsync(AuditEventTypes.ImportCommitCompleted)));
        var everything = await db.AuditLogEntries.AsNoTracking().Select(a => a.Details + " " + a.Reason).ToListAsync();
        Assert.DoesNotContain(everything, text => text.Contains("Rivera") || text.Contains("Moss") || text.Contains("Lee"));
        Assert.Contains(everything, text => text.Contains($"import batch {batch}, row 2"));
    }

    [Fact]
    public async Task All_or_nothing_registers_the_whole_file_in_one_job_and_skipped_rows_do_not_stop_it()
    {
        await RegisterAsync("Dee", "Park", "1971-02-02");
        var batch = await DryRunBatchAsync(Csv(Row("Ann", "Rivera", "1950-01-10"), Row("Dee", "Park", "1971-02-02"), Row("Cy", "Lee", "1980-05-05", "555-500-5000")));

        await CommitAsync(batch, ImportBatchPolicy.AllOrNothing, 2);

        Assert.Equal(2, await CountAsync(db => db.BackgroundJobs.Where(j => j.JobType == ImportCommitJobs.JobType)));       // one rows job for the whole file and one finish job
        await DrainAsync();
        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.CompletedWithExceptions, 2, 1), (saved.Status, saved.CommitOutcome, saved.AcceptedRows, saved.SkippedRows));
        Assert.Equal(3, await CountAsync(db => db.Patients));                                       // the one registered before and the two new ones
    }

    [Fact]
    public async Task The_confirmation_is_checked_before_anything_is_written()
    {
        var uploadedOnly = (await Services().Intake.UploadAsync(ImportSourceKind.Patients, "Source", ThreeValid, _actor, default)).Batch.Id;
        var batch = await DryRunBatchAsync(Csv(Row("Ann", "Rivera", "1950-01-10"), Row("Bo", "Moss", "1962-03-04", "555-300-3000"), Row("", "Lee", "1980-05-05", "555-500-5000")));
        var allInvalid = await DryRunBatchAsync(Csv(Row("", "Rivera", "1950-01-10", "555-111-1111"), Row("", "Park", "1971-02-02", "555-222-2222")));
        var balances = (await Services().Intake.UploadAsync(ImportSourceKind.OpeningBalances, "Source", ThreeValid, _actor, default)).Batch.Id;

        Assert.Equal("batch_not_found", await CodeAsync(() => CommitAsync(Guid.NewGuid(), ImportBatchPolicy.CommitValidRows, 1)));
        Assert.Equal("billing_permission_required", await CodeAsync(() => CommitAsync(balances, ImportBatchPolicy.CommitValidRows, 1)));   // a balance batch also needs ManageBilling, checked first
        Assert.Equal("dry_run_required", await CodeAsync(() => CommitAsync(uploadedOnly, ImportBatchPolicy.CommitValidRows, 3)));
        Assert.Equal("invalid_policy", await CodeAsync(() => CommitAsync(batch, null, 2)));
        Assert.Equal("invalid_policy", await CodeAsync(() => CommitAsync(batch, "Everything", 2)));
        Assert.Equal("expected_valid_rows_required", await CodeAsync(() => CommitAsync(batch, ImportBatchPolicy.CommitValidRows, null)));
        Assert.Equal("results_changed", await CodeAsync(() => CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 3)));          // the screen showed 3, the batch has 2
        Assert.Equal("nothing_to_commit", await CodeAsync(() => CommitAsync(allInvalid, ImportBatchPolicy.CommitValidRows, 0)));
        Assert.Equal("batch_has_problems", await CodeAsync(() => CommitAsync(batch, ImportBatchPolicy.AllOrNothing, 2)));          // a rejected row stops all-or-nothing

        _options.MaxAllOrNothingRows = 2;
        var three = await DryRunBatchAsync(Csv(Row("Eve", "Fox", "1960-06-06", "555-666-6666"), Row("Fay", "Gray", "1961-07-07", "555-777-7777"), Row("Gus", "Hill", "1962-08-08", "555-888-8888")));
        Assert.Equal("too_large_for_all_or_nothing", await CodeAsync(() => CommitAsync(three, ImportBatchPolicy.AllOrNothing, 3)));

        foreach (var id in new[] { uploadedOnly, batch, allInvalid, three }) Assert.NotEqual(ImportBatchStatus.Committing, (await BatchAsync(id)).Status);
        Assert.Equal(0, await CountAsync(db => db.BackgroundJobs));
        Assert.Equal(0, await CountAsync(db => db.Patients));
        Assert.Equal(0, await AuditAsync(AuditEventTypes.ImportCommitStarted));
    }

    [Fact]
    public async Task Valid_rows_only_leaves_the_rejected_skipped_and_unresolved_rows_where_they_are_and_says_so()
    {
        await RegisterAsync("Eve", "Park", "1971-02-02");
        var batch = await DryRunBatchAsync(Csv(
            Row("Ann", "Rivera", "1950-01-10"),                                                    // row 2: valid
            Row("", "Moss", "1962-03-04"),                                                         // row 3: rejected
            Row("Eve", "Park", "1971-02-02"),                                                      // row 4: already registered
            Row("Eva", "Park", "1971-02-02", "555-777-7777"),                                      // row 5: a likely match of Eve Park
            Row("Cy", "Lee", "1980-05-05", "555-500-5000")));                                      // row 6: valid

        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 2);
        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.CompletedWithExceptions, 5, 2, 1, 1, 1), (saved.Status, saved.CommitOutcome, saved.AttemptedRows, saved.AcceptedRows, saved.RejectedRows, saved.SkippedRows, saved.UnresolvedRows));
        var rows = await RowsAsync(batch);
        Assert.Equal([ImportRowStatus.Accepted, ImportRowStatus.Rejected, ImportRowStatus.Skipped, ImportRowStatus.Unresolved, ImportRowStatus.Accepted], rows.OrderBy(r => r.Key).Select(r => r.Value.Status));
        Assert.Equal(3, await CountAsync(db => db.Patients));
    }

    [Fact]
    public async Task A_row_is_judged_again_at_commit_time_so_a_person_registered_since_the_dry_run_is_never_duplicated()
    {
        var batch = await DryRunBatchAsync(ThreeValid);
        var bo = await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");                    // registered after the dry-run: the same person as row 3
        await RegisterAsync("Cyrus", "Lee", "1980-05-05", "555-999-9999");                           // registered after the dry-run: a likely match of row 4

        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 3);
        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Accepted, ImportRowStatus.Skipped, ImportRowStatus.Unresolved), (rows[2].Status, rows[3].Status, rows[4].Status));
        Assert.Equal((ImportRowReason.AlreadyRegistered, ImportRowReason.PossibleDuplicate), (rows[3].Reason, rows[4].Reason));
        Assert.Equal([bo], System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(rows[3].MatchedPatientIdsJson!));
        Assert.Equal(3, await CountAsync(db => db.Patients));                                       // the two registered meanwhile and only one new one
        var saved = await BatchAsync(batch);
        Assert.Equal((1, 0, 1, 1, ImportCommitOutcome.CompletedWithExceptions), (saved.AcceptedRows, saved.RejectedRows, saved.SkippedRows, saved.UnresolvedRows, saved.CommitOutcome));
    }

    [Fact]
    public async Task All_or_nothing_registers_nothing_when_a_row_is_no_longer_valid_and_asks_for_a_new_dry_run()
    {
        var batch = await DryRunBatchAsync(ThreeValid);
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");

        await CommitAsync(batch, ImportBatchPolicy.AllOrNothing, 3);
        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Uploaded, ImportCommitOutcome.Refused, 0), (saved.Status, saved.CommitOutcome, saved.AcceptedRows));
        Assert.Null(saved.DryRunAtUtc);
        Assert.Equal(1, await CountAsync(db => db.Patients));                                       // only the one registered meanwhile: not one of the three rows was registered
        Assert.Equal(1, await AuditAsync(AuditEventTypes.ImportCommitRefused));
        Assert.Equal(0, await AuditAsync(AuditEventTypes.ImportCommitCompleted));

        var s = Services();
        await s.DryRun.RunAsync(batch, _actor, default);                                             // the new dry-run shows each row as it is now
        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Valid, ImportRowStatus.Skipped, ImportRowStatus.Valid), (rows[2].Status, rows[3].Status, rows[4].Status));
        Assert.Null((await BatchAsync(batch)).CommitOutcome);
        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 2);
        await DrainAsync();
        Assert.Equal(3, await CountAsync(db => db.Patients));
        Assert.Equal(2, (await BatchAsync(batch)).CommitRound);
    }

    [Fact]
    public async Task The_valid_rows_are_committed_in_windows_and_a_finish_job_settles_the_batch_once_they_are_all_over()
    {
        _options.CommitChunkRows = 2;
        var batch = await DryRunBatchAsync(Csv(
            Row("Ann", "Rivera", "1950-01-10"), Row("Bo", "Moss", "1962-03-04", "555-300-3000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000"),
            Row("Di", "Nash", "1975-04-04", "555-400-4000"), Row("Ed", "Ortiz", "1985-09-09", "555-900-9000")));

        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 5);

        var keys = await CountAsync(db => db.BackgroundJobs.Where(j => j.IdempotencyKey.StartsWith($"import-commit:{batch:N}:1:")));
        Assert.Equal(3, keys);                                                                       // rows 2-3, 4-5 and 6
        await DrainAsync();
        Assert.Equal(5, await CountAsync(db => db.Patients));
        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, 5), (saved.Status, saved.AcceptedRows));
        Assert.Equal(1, await AuditAsync(AuditEventTypes.ImportCommitCompleted));                    // the batch is settled once, however many windows ran
    }

    [Fact]
    public async Task Confirming_again_replaying_a_job_or_meeting_a_patient_already_under_the_rows_key_registers_nothing_twice()
    {
        var batch = await DryRunBatchAsync(ThreeValid);
        var already = await RegisterAsync("Ann", "Rivera", "1950-01-10", key: $"import-{batch:N}-2");     // an earlier attempt of row 2 that committed the patient

        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 3);
        var again = await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 3);                      // a repeated confirmation while the batch is Committing
        Assert.Equal(ImportBatchStatus.Committing, again.Status);
        Assert.Equal(2, await CountAsync(db => db.BackgroundJobs.Where(j => j.JobType == ImportCommitJobs.JobType)));      // still one rows job and one finish job

        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Accepted, already), (rows[2].Status, rows[2].ResultRecordId));  // the patient already there is the row's result
        Assert.Equal(3, await CountAsync(db => db.Patients));
        Assert.Equal(1, await AuditAsync(AuditEventTypes.ImportCommitStarted));

        var replay = new ImportCommitEngine(_fixture.CreateContext(), _files, Opts, _clock);
        await replay.ExecuteAsync(new ImportCommitJob(batch, 1, ImportCommitJobs.RowsStep, 2, 4, 0), default);                  // the same window again, after the batch is finished
        Assert.Equal(3, await CountAsync(db => db.Patients));
        Assert.Equal("batch_not_open", await CodeAsync(() => CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 0)));
    }

    [Fact]
    public async Task A_job_recovered_after_a_crash_finishes_the_commit_without_registering_anyone_twice()
    {
        var batch = await DryRunBatchAsync(ThreeValid);
        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 3);
        await using (var db = _fixture.CreateContext())                                              // a runner that claimed the rows job and then died: InProgress for longer than the stuck threshold
        {
            var job = await db.BackgroundJobs.SingleAsync(j => j.IdempotencyKey == ImportCommitJobs.RowsKey(batch, 1, 2));
            job.Status = BackgroundJobStatus.InProgress;
            job.AttemptCount = 1;
            job.StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await Runner().RecoverStuckJobsAsync(default));
        await DrainAsync();

        Assert.Equal(3, await CountAsync(db => db.Patients));
        Assert.Equal(ImportBatchStatus.Committed, (await BatchAsync(batch)).Status);
    }

    [Fact]
    public async Task A_job_that_fails_for_good_leaves_the_batch_interrupted_with_what_was_registered_kept()
    {
        _options.CommitChunkRows = 2;
        var batch = await DryRunBatchAsync(Csv(Row("Ann", "Rivera", "1950-01-10"), Row("Bo", "Moss", "1962-03-04", "555-300-3000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000"), Row("Di", "Nash", "1975-04-04", "555-400-4000")));
        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 4);
        await using (var db = _fixture.CreateContext())                                              // make the second window fail every time: its rows are for a file that is not there any more
        {
            var second = await db.BackgroundJobs.SingleAsync(j => j.IdempotencyKey == ImportCommitJobs.RowsKey(batch, 1, 4));
            second.PayloadJson = "not json";                                                         // a payload the handler cannot read: it throws every time
            await db.SaveChangesAsync();
        }

        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.DryRunComplete, ImportCommitOutcome.Interrupted, 2), (saved.Status, saved.CommitOutcome, saved.AcceptedRows));
        Assert.Equal(2, await CountAsync(db => db.Patients));                                       // the first window stays registered
        Assert.Equal(1, await CountAsync(db => db.BackgroundJobs.Where(j => j.Status == BackgroundJobStatus.Failed)));                  // after its bounded attempts
        var rows = await RowsAsync(batch);
        Assert.Equal([ImportRowStatus.Accepted, ImportRowStatus.Accepted, ImportRowStatus.Valid, ImportRowStatus.Valid], rows.OrderBy(r => r.Key).Select(r => r.Value.Status));
    }

    [Fact]
    public async Task A_new_commit_after_an_interruption_registers_only_the_rows_that_are_left()
    {
        _options.CommitChunkRows = 2;
        var batch = await DryRunBatchAsync(Csv(Row("Ann", "Rivera", "1950-01-10"), Row("Bo", "Moss", "1962-03-04", "555-300-3000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000"), Row("Di", "Nash", "1975-04-04", "555-400-4000")));
        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 4);
        await using (var db = _fixture.CreateContext())
        {
            (await db.BackgroundJobs.SingleAsync(j => j.IdempotencyKey == ImportCommitJobs.RowsKey(batch, 1, 4))).PayloadJson = "not json";
            await db.SaveChangesAsync();
        }

        await DrainAsync();
        Assert.Equal(ImportCommitOutcome.Interrupted, (await BatchAsync(batch)).CommitOutcome);

        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 2);                                // two valid rows are left
        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.Completed, 4, 2), (saved.Status, saved.CommitOutcome, saved.AcceptedRows, saved.CommitRound));
        Assert.Equal(4, await CountAsync(db => db.Patients));
        Assert.Equal(4, (await RowsAsync(batch)).Values.Select(r => r.ResultRecordId).Distinct().Count());
    }

    [Fact]
    public async Task A_person_decides_a_likely_duplicate_and_the_commit_honours_the_decision_without_ever_merging()
    {
        var ann = await RegisterAsync("Ann", "Rivera", "1950-01-10");
        var batch = await DryRunBatchAsync(Csv(
            Row("Anna", "Rivera", "1950-01-10", "555-200-2000"),                                   // row 2: a likely match of Ann Rivera
            Row("Anne", "Rivera", "1950-01-10", "555-210-2100"),                                   // row 3: another likely match
            Row("Cy", "Lee", "1980-05-05", "555-500-5000")));                                      // row 4: valid
        var s = Services();

        Assert.Equal("row_not_reviewable", await CodeAsync(() => s.Decision.DecideAsync(batch, 4, ImportDecision.Skip, _actor, default)));      // a valid row is not a queue item
        Assert.Equal("invalid_decision", await CodeAsync(() => s.Decision.DecideAsync(batch, 2, "Merge", _actor, default)));
        Assert.Equal("row_not_found", await CodeAsync(() => s.Decision.DecideAsync(batch, 99, ImportDecision.Skip, _actor, default)));
        Assert.Equal("batch_not_found", await CodeAsync(() => s.Decision.DecideAsync(Guid.NewGuid(), 2, ImportDecision.Skip, _actor, default)));

        var asNew = await s.Decision.DecideAsync(batch, 2, ImportDecision.RegisterAsNew, _actor, default);
        var same = await s.Decision.DecideAsync(batch, 3, ImportDecision.Skip, _actor, default);

        Assert.Equal((ImportRowStatus.Valid, ImportRowReason.ReviewedNewPerson, true), (asNew.Row.Status, asNew.Row.Reason, asNew.Row.Decided));
        Assert.Equal((ImportRowStatus.Skipped, ImportRowReason.ReviewedSamePerson), (same.Row.Status, same.Row.Reason));
        Assert.Equal((0, 1), (same.Batch.UnresolvedRows, same.Batch.SkippedRows));
        Assert.Equal(2, await AuditAsync(AuditEventTypes.ImportRowDecided));
        await s.Decision.DecideAsync(batch, 2, ImportDecision.RegisterAsNew, _actor, default);              // the same decision again changes nothing
        Assert.Equal(2, await AuditAsync(AuditEventTypes.ImportRowDecided));

        await CommitAsync(batch, ImportBatchPolicy.AllOrNothing, 2);                                    // every row is valid or skipped now, so all-or-nothing is allowed
        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Accepted, ImportRowStatus.Skipped, ImportRowStatus.Accepted), (rows[2].Status, rows[3].Status, rows[4].Status));
        Assert.NotNull(rows[2].DecisionByUserId);
        Assert.Equal(3, await CountAsync(db => db.Patients));                                       // Ann, the reviewed new person and Cy: two records for similar people, never merged
        Assert.Equal(0, await CountAsync(db => db.Patients.Where(p => p.MergedAtUtc != null)));
        Assert.NotEqual(ann, rows[2].ResultRecordId);
    }

    [Fact]
    public async Task A_decision_can_be_changed_while_the_batch_waits_and_the_counters_follow()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10");
        var batch = await DryRunBatchAsync(Csv(Row("Anna", "Rivera", "1950-01-10", "555-200-2000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000")));
        var s = Services();

        var first = await s.Decision.DecideAsync(batch, 2, ImportDecision.Skip, _actor, default);
        Assert.Equal((0, 1), (first.Batch.UnresolvedRows, first.Batch.SkippedRows));
        var changed = await s.Decision.DecideAsync(batch, 2, ImportDecision.RegisterAsNew, _actor, default);

        Assert.Equal((ImportRowStatus.Valid, 0, 0), (changed.Row.Status, changed.Batch.UnresolvedRows, changed.Batch.SkippedRows));
    }

    [Fact]
    public async Task A_decision_survives_a_new_dry_run_while_the_row_is_still_a_likely_duplicate_and_a_new_mapping_clears_every_decision()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10");
        var batch = await DryRunBatchAsync(Csv(Row("Anna", "Rivera", "1950-01-10", "555-200-2000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000")));
        var s = Services();
        await s.Decision.DecideAsync(batch, 2, ImportDecision.RegisterAsNew, _actor, default);

        await Services().DryRun.RunAsync(batch, _actor, default);

        var kept = (await RowsAsync(batch))[2];
        Assert.Equal((ImportRowStatus.Valid, ImportRowReason.ReviewedNewPerson), (kept.Status, kept.Reason));
        Assert.NotNull(kept.DecisionByUserId);

        var suggestion = await Services().Mapping.GetSuggestionAsync(batch, default);
        await Services().Mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
        await Services().DryRun.RunAsync(batch, _actor, default);

        var cleared = (await RowsAsync(batch))[2];
        Assert.Equal((ImportRowStatus.Unresolved, ImportRowReason.PossibleDuplicate), (cleared.Status, cleared.Reason));
        Assert.Null(cleared.DecisionByUserId);
    }

    [Fact]
    public async Task A_decision_needs_a_dry_run_and_a_batch_that_is_still_waiting_for_its_commit()
    {
        var uploaded = (await Services().Intake.UploadAsync(ImportSourceKind.Patients, "Source", ThreeValid, _actor, default)).Batch.Id;
        var batch = await DryRunBatchAsync(Csv(Row("Dee", "Park", "1971-02-02")));
        await CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 1);

        Assert.Equal("dry_run_required", await CodeAsync(() => Services().Decision.DecideAsync(uploaded, 2, ImportDecision.Skip, _actor, default)));
        Assert.Equal("batch_not_open", await CodeAsync(() => Services().Decision.DecideAsync(batch, 2, ImportDecision.Skip, _actor, default)));       // Committing
    }
}
