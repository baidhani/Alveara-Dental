using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 4b, committing an appointment file against a real SQL Server, a real folder, the real durable job runner and the real scheduler: each valid row is scheduled through the scheduler in its own scope under the
/// key import-appt-{batch}-{row} and its result saved at once; the scheduler's own checks run again as of now, so a clash created after the dry-run rejects the row and nothing is ever double-booked; an appointment already
/// under the row's key is the row's result and one booked by someone else exactly as the row says makes the row Skipped; a reference that changed since the dry-run rejects the row; all-or-nothing is refused for appointments; a
/// job recovered after a crash completes the commit and a job that fails for good leaves the batch Interrupted, after which a new commit schedules only what is left; the counters reconcile and no value from the file reaches the audit log.
/// </summary>
public class ImportAppointmentCommitTests : IAsyncLifetime
{
    private const string Header = "Patient First,Patient Last,DOB,Provider,Operatory,Type,Start,Duration,Notes";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"alveara-import-test-{Guid.NewGuid():N}");
    private readonly IPracticeClock _clock = SchedulingTestSupport.Clock;
    private readonly ImportOptions _options = new();
    private SchedulingTestSupport _s = null!;
    private ImportFileStorage _files = null!;
    private Guid _actor;

    private sealed class TestWorkScopeFactory(TestDatabaseFixture fixture, SchedulingTestSupport support) : IImportWorkScopeFactory
    {
        public ImportWorkScope Create()
        {
            var db = fixture.CreateContext();
            return new ImportWorkScope(db, support.Scheduler(db), db);
        }
    }

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
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

    private static string Appt(string first, string last, string dob, string provider, string operatory, string start) => $"{first},{last},{dob},{provider},{operatory},Exam,{start},,";

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", rows) + "\r\n");

    private (ImportIntakeService Intake, ImportMappingService Mapping, ImportDryRunService DryRun, ImportCommitService Commit) Services()
    {
        var db = _fixture.CreateContext();
        var intake = new ImportIntakeService(db, _files, Opts, _clock, NullLogger<ImportIntakeService>.Instance);
        var mapping = new ImportMappingService(db, _clock);
        return (intake, mapping, new ImportDryRunService(db, _files, Opts, _clock, mapping, intake), new ImportCommitService(db, new BackgroundJobQueue(db), _files, Opts, _clock, intake));
    }

    private BackgroundJobRunner Runner() => new(_fixture.CreateContext(), [new ImportCommitJobHandler(_files, Opts, _clock, new TestWorkScopeFactory(_fixture, _s))], NullLogger<BackgroundJobRunner>.Instance);

    private async Task DrainAsync()
    {
        for (var i = 0; i < 80; i++)
        {
            var processed = await Runner().ProcessOnceAsync([ImportCommitJobs.JobType], default);
            if (processed == 0 && await CountAsync(db => db.BackgroundJobs.Where(j => j.JobType == ImportCommitJobs.JobType && j.Status == BackgroundJobStatus.Pending)) == 0) return;
        }

        throw new Xunit.Sdk.XunitException("The commit jobs never ran out.");
    }

    private async Task<Guid> RegisterAsync(string first, string last, string dob, string phone)
    {
        var service = new PatientRegistrationService(_fixture.CreateContext(), _clock);
        var result = await service.RegisterAsync(new RegisterPatientRequest(first, null, last, dob, "Female", phone, null, "1 Main St", null, "Austin", "TX", "78701"), $"test-{Guid.NewGuid():N}", _actor, default);
        return result.Patient.Id;
    }

    private async Task<Guid> DryRunBatchAsync(byte[] csv)
    {
        var s = Services();
        var batch = (await s.Intake.UploadAsync(ImportSourceKind.Appointments, "Old system", csv, _actor, default)).Batch.Id;
        var suggestion = await s.Mapping.GetSuggestionAsync(batch, default);
        await s.Mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
        await s.DryRun.RunAsync(batch, _actor, default);
        return batch;
    }

    private Task<ImportBatchView> CommitAsync(Guid batch, int expected, string policy = ImportBatchPolicy.CommitValidRows) => Services().Commit.CommitAsync(batch, policy, expected, _actor, default);

    private async Task<int> CountAsync<T>(Func<AlveraDbContext, IQueryable<T>> query) where T : class { await using var db = _fixture.CreateContext(); return await query(db).CountAsync(); }

    private async Task<ImportBatch> BatchAsync(Guid id) { await using var db = _fixture.CreateContext(); return await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id); }

    private async Task<Dictionary<int, ImportBatchRow>> RowsAsync(Guid batch) { await using var db = _fixture.CreateContext(); return await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batch).ToDictionaryAsync(r => r.RowNumber); }

    private static async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<ImportException>(action)).Code;

    private Task<int> AuditAsync(string eventType) => CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == eventType));

    private async Task<(Guid Ann, Guid Bo, Guid Cy)> ThreePatientsAsync() =>
        (await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000"), await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000"), await RegisterAsync("Cy", "Lee", "1980-05-05", "555-500-5000"));

    private static byte[] ThreeValid() => Csv(
        Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "2030-01-14 09:00"),
        Appt("Bo", "Moss", "1962-03-04", "Dr. Patel", "Op 2", "2030-01-14 10:00"),
        Appt("Cy", "Lee", "1980-05-05", "Dr. Rivera", "Op 2", "2030-01-14 11:00"));

    [Fact]
    public async Task Committing_valid_appointments_schedules_each_through_the_scheduler_and_reconciles_the_counters()
    {
        await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(ThreeValid());

        var started = await CommitAsync(batch, 3);

        Assert.Equal(ImportBatchStatus.Committing, started.Status);
        Assert.Equal(0, await _s.CountAsync());                                                      // nothing is scheduled until the jobs run
        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.Completed, 3, 3, 0, 0, 0), (saved.Status, saved.CommitOutcome, saved.AttemptedRows, saved.AcceptedRows, saved.RejectedRows, saved.SkippedRows, saved.UnresolvedRows));
        var rows = await RowsAsync(batch);
        await using var db = _fixture.CreateContext();
        var appointments = await db.Appointments.AsNoTracking().ToListAsync();
        Assert.Equal(3, appointments.Count);
        Assert.All(rows.Values, r => Assert.Equal(ImportRowStatus.Accepted, r.Status));
        Assert.Equal(rows.Values.Select(r => r.ResultRecordId!.Value).Order(), appointments.Select(a => a.Id).Order());
        Assert.Equal(rows.Keys.Select(n => $"import-appt-{batch:N}-{n}").Order(), appointments.Select(a => a.ScheduleKey).Order());
        Assert.Equal(3, await AuditAsync(SchedulingAuditEvents.Scheduled));
        Assert.Equal((1, 1), (await AuditAsync(AuditEventTypes.ImportCommitStarted), await AuditAsync(AuditEventTypes.ImportCommitCompleted)));
        var everything = await db.AuditLogEntries.AsNoTracking().Select(a => a.Details + " " + a.Reason).ToListAsync();
        Assert.DoesNotContain(everything, text => text.Contains("Moss"));                                // a patient's surname never reaches the audit log
    }

    [Fact]
    public async Task All_or_nothing_is_not_available_for_appointments_and_nothing_changes()
    {
        await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(ThreeValid());

        Assert.Equal("policy_not_supported", await CodeAsync(() => CommitAsync(batch, 3, ImportBatchPolicy.AllOrNothing)));

        Assert.Equal(ImportBatchStatus.DryRunComplete, (await BatchAsync(batch)).Status);
        Assert.Equal(0, await CountAsync(db => db.BackgroundJobs));
        Assert.Equal(0, await AuditAsync(AuditEventTypes.ImportCommitStarted));
    }

    [Fact]
    public async Task A_clash_created_after_the_dry_run_rejects_the_row_and_nothing_is_double_booked()
    {
        var (_, _, cy) = await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(Csv(
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "2030-01-14 09:00"),
            Appt("Bo", "Moss", "1962-03-04", "Dr. Patel", "Op 1", "2030-01-14 10:00")));
        await _s.ScheduleAsync(_s.Request(cy, SchedulingTestSupport.At(9, 30), _s.ProviderA, _s.Op2, _s.Long60));                            // after the dry-run: Dr. Rivera is busy 09:30 to 10:30

        await CommitAsync(batch, 2);
        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.ProviderDoubleBooked), (rows[2].Status, rows[2].Reason));
        Assert.Contains("already has an appointment", rows[2].ErrorsJson);
        Assert.Equal(ImportRowStatus.Accepted, rows[3].Status);
        Assert.Equal(2, await _s.CountAsync());                                                      // the one booked meanwhile and the one that fitted
        Assert.Equal(1, await AuditAsync(SchedulingAuditEvents.Rejected));                           // the scheduler recorded its own refusal
        var saved = await BatchAsync(batch);
        Assert.Equal((ImportCommitOutcome.CompletedWithExceptions, 1, 1), (saved.CommitOutcome, saved.AcceptedRows, saved.RejectedRows));
    }

    [Fact]
    public async Task A_row_already_scheduled_under_its_key_is_its_result_and_confirming_again_or_replaying_books_nothing_twice()
    {
        var (ann, _, _) = await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(ThreeValid());
        var earlier = await _s.ScheduleAsync(_s.Request(ann, SchedulingTestSupport.At(9), _s.ProviderA, _s.Op1, _s.Long60), $"import-appt-{batch:N}-2");        // an earlier attempt of row 2 that got as far as scheduling

        await CommitAsync(batch, 3);
        var again = await CommitAsync(batch, 3);                                                     // confirmed again while Committing
        Assert.Equal(ImportBatchStatus.Committing, again.Status);
        Assert.Equal(2, await CountAsync(db => db.BackgroundJobs.Where(j => j.JobType == ImportCommitJobs.JobType)));      // one rows job and one finish job
        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Accepted, earlier.Appointment.Id), (rows[2].Status, rows[2].ResultRecordId));
        Assert.Equal(3, await _s.CountAsync());

        var replay = new ImportCommitEngine(_fixture.CreateContext(), _files, Opts, _clock, new TestWorkScopeFactory(_fixture, _s));
        await replay.ExecuteAsync(new ImportCommitJob(batch, 1, ImportCommitJobs.RowsStep, 2, 4, 0), default);              // the same window again, after the batch is finished
        Assert.Equal(3, await _s.CountAsync());
    }

    [Fact]
    public async Task An_identical_appointment_booked_meanwhile_makes_the_row_skipped_not_rejected()
    {
        var (ann, _, _) = await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(Csv(Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "2030-01-14 09:00")));
        await _s.ScheduleAsync(_s.Request(ann, SchedulingTestSupport.At(9), _s.ProviderA, _s.Op1, _s.Long60));                              // exactly the row, booked by someone else, under another key

        await CommitAsync(batch, 1);
        await DrainAsync();

        var row = (await RowsAsync(batch))[2];
        Assert.Equal((ImportRowStatus.Skipped, ImportRowReason.AlreadyScheduled), (row.Status, row.Reason));
        Assert.Null(row.ResultRecordId);
        Assert.Equal(1, await _s.CountAsync());
        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.CompletedWithExceptions, 0, 1), (saved.Status, saved.CommitOutcome, saved.AcceptedRows, saved.SkippedRows));
    }

    [Fact]
    public async Task A_reference_that_changed_since_the_dry_run_rejects_the_row_and_the_others_are_scheduled()
    {
        await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(Csv(
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "2030-01-14 09:00"),
            Appt("Bo", "Moss", "1962-03-04", "Dr. Patel", "Op 2", "2030-01-14 10:00")));
        await using (var db = _fixture.CreateContext())
        {
            (await db.ProviderProfiles.SingleAsync(p => p.Id == _s.ProviderB)).IsActive = false;                                            // Dr. Patel is deactivated after the dry-run
            await db.SaveChangesAsync();
        }

        await CommitAsync(batch, 2);
        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal(ImportRowStatus.Accepted, rows[2].Status);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.InvalidFields), (rows[3].Status, rows[3].Reason));
        Assert.Contains("This provider is inactive.", rows[3].ErrorsJson);
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task Rows_rejected_or_skipped_at_the_dry_run_stay_as_they_are_and_the_counters_reconcile()
    {
        var (ann, _, _) = await ThreePatientsAsync();
        await _s.ScheduleAsync(_s.Request(ann, SchedulingTestSupport.At(9), _s.ProviderA, _s.Op1, _s.Long60));                              // already on the schedule
        var batch = await DryRunBatchAsync(Csv(
            Appt("Bo", "Moss", "1962-03-04", "Dr. Patel", "Op 2", "2030-01-14 10:00"),                                                       // valid
            Appt("Cy", "Lee", "1980-05-05", "Dr. Nobody", "Op 2", "2030-01-14 11:00"),                                                      // rejected: no such provider
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "2030-01-14 09:00")));                                                // skipped: already scheduled

        await CommitAsync(batch, 1);
        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.CompletedWithExceptions, 3, 1, 1, 1, 0), (saved.Status, saved.CommitOutcome, saved.AttemptedRows, saved.AcceptedRows, saved.RejectedRows, saved.SkippedRows, saved.UnresolvedRows));
        Assert.Equal(2, await _s.CountAsync());
    }

    [Fact]
    public async Task A_job_recovered_after_a_crash_finishes_the_commit_without_booking_anything_twice()
    {
        await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(ThreeValid());
        await CommitAsync(batch, 3);
        await using (var db = _fixture.CreateContext())
        {
            var job = await db.BackgroundJobs.SingleAsync(j => j.IdempotencyKey == ImportCommitJobs.RowsKey(batch, 1, 2));
            job.Status = BackgroundJobStatus.InProgress;
            job.AttemptCount = 1;
            job.StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await Runner().RecoverStuckJobsAsync(default));
        await DrainAsync();

        Assert.Equal(3, await _s.CountAsync());
        Assert.Equal(ImportBatchStatus.Committed, (await BatchAsync(batch)).Status);
    }

    [Fact]
    public async Task A_job_that_fails_for_good_leaves_the_batch_interrupted_and_a_new_commit_schedules_only_what_is_left()
    {
        _options.CommitChunkRows = 1;
        await ThreePatientsAsync();
        var batch = await DryRunBatchAsync(ThreeValid());
        await CommitAsync(batch, 3);
        await using (var db = _fixture.CreateContext())
        {
            (await db.BackgroundJobs.SingleAsync(j => j.IdempotencyKey == ImportCommitJobs.RowsKey(batch, 1, 3))).PayloadJson = "not json";                  // the window of row 3 cannot be read
            await db.SaveChangesAsync();
        }

        await DrainAsync();

        var interrupted = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.DryRunComplete, ImportCommitOutcome.Interrupted, 2), (interrupted.Status, interrupted.CommitOutcome, interrupted.AcceptedRows));
        Assert.Equal(2, await _s.CountAsync());                                                      // rows 2 and 4 are scheduled and stay so

        await CommitAsync(batch, 1);
        await DrainAsync();

        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.Completed, 3, 2), (saved.Status, saved.CommitOutcome, saved.AcceptedRows, saved.CommitRound));
        Assert.Equal(3, await _s.CountAsync());
    }
}
