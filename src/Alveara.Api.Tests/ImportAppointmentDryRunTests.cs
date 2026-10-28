using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 4a, the mapping and dry-run of an appointment file against a real SQL Server (the practice's real providers, operatories, appointment types and weekly hours, and appointments made through the real
/// scheduler): a valid row is Valid and nothing is scheduled; every problem with a row's values is reported at once as a fixed message, never a value; the patient must be exactly one registered, active patient with that
/// name and birth date and each of provider, operatory and type one active record with that name (ignoring capitals and spacing); a start in the past, in a daylight-saving gap or fold, outside the provider's hours or
/// across their closing time is refused for the scheduler's own reason; a period held by an existing appointment is refused (provider, then operatory, then patient, back-to-back allowed) and so is a period held by an
/// earlier row of the file, naming that row; an appointment already on the schedule exactly as the row says, or an exact repeat within the file, is Skipped; the dry-run is repeatable, writes no appointment and no
/// scheduling audit entry, and a large file is dry-run in bulk (the time is reported, not asserted).
/// </summary>
public class ImportAppointmentDryRunTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Header = "Patient First,Patient Last,DOB,Provider,Operatory,Type,Start,Duration,Notes";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"alveara-import-test-{Guid.NewGuid():N}");
    private readonly IPracticeClock _clock = SchedulingTestSupport.Clock;
    private SchedulingTestSupport _s = null!;
    private ImportFileStorage _files = null!;
    private Guid _actor;

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

    private static string Appt(string first, string last, string dob, string provider, string operatory, string type, string start, string duration = "", string notes = "") =>
        $"{first},{last},{dob},{provider},{operatory},{type},{start},{duration},{notes}";

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", rows) + "\r\n");

    private (ImportIntakeService Intake, ImportMappingService Mapping, ImportDryRunService DryRun) Services()
    {
        var db = _fixture.CreateContext();
        var options = Options.Create(new ImportOptions());
        var intake = new ImportIntakeService(db, _files, options, _clock, NullLogger<ImportIntakeService>.Instance);
        var mapping = new ImportMappingService(db, _clock);
        return (intake, mapping, new ImportDryRunService(db, _files, options, _clock, mapping, intake));
    }

    private async Task<Guid> RegisterAsync(string first, string last, string dob, string phone)
    {
        var service = new PatientRegistrationService(_fixture.CreateContext(), _clock);
        var result = await service.RegisterAsync(new RegisterPatientRequest(first, null, last, dob, "Female", phone, null, "1 Main St", null, "Austin", "TX", "78701"), $"test-{Guid.NewGuid():N}", _actor, default);
        return result.Patient.Id;
    }

    private async Task<Guid> UploadAsync(byte[] csv, bool map = true)
    {
        var s = Services();
        var batch = (await s.Intake.UploadAsync(ImportSourceKind.Appointments, "Old system", csv, _actor, default)).Batch.Id;
        if (map)
        {
            var suggestion = await s.Mapping.GetSuggestionAsync(batch, default);
            await s.Mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
        }

        return batch;
    }

    private async Task<Guid> DryRunBatchAsync(byte[] csv)
    {
        var batch = await UploadAsync(csv);
        await Services().DryRun.RunAsync(batch, _actor, default);
        return batch;
    }

    private async Task<Dictionary<int, ImportRowView>> RowsAsync(Guid batch) => (await Services().DryRun.ListRowsAsync(batch, null, 500, 1, default)).ToDictionary(r => r.RowNumber);

    private async Task<int> CountAsync<T>(Func<AlveraDbContext, IQueryable<T>> query) where T : class { await using var db = _fixture.CreateContext(); return await query(db).CountAsync(); }

    private async Task BookAsync(Guid patient, DateTime start, Guid provider, Guid operatory) => await _s.ScheduleAsync(_s.Request(patient, start, provider, operatory, _s.Long60));

    [Fact]
    public async Task The_suggested_mapping_names_the_obvious_columns_and_an_appointment_batch_is_mapped_like_a_patient_batch()
    {
        var batch = await UploadAsync(Csv(Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00")), map: false);

        var suggestion = await Services().Mapping.GetSuggestionAsync(batch, default);

        Assert.Equal("patientFirstName", suggestion.Suggested["Patient First"]);
        Assert.Equal("start", suggestion.Suggested["Start"]);
        Assert.Empty(suggestion.MissingRequired);
        Assert.Equal(AppointmentImportFields.All.Select(f => f.Key), suggestion.Fields.Select(f => f.Key));
        var refused = await Assert.ThrowsAsync<ImportMappingRejectedException>(() => Services().Mapping.SaveAsync(batch, new Dictionary<string, string?> { ["Provider"] = "provider" }, _actor, default));
        Assert.Contains("required_field_unmapped", refused.Problems.Select(p => p.Code));
    }

    [Fact]
    public async Task Valid_rows_are_valid_and_a_dry_run_schedules_nothing_and_writes_no_scheduling_audit_entry()
    {
        var ann = await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        var bo = await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");
        var batch = await UploadAsync(Csv(
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00"),
            Appt("  bo ", "MOSS", "1962-03-04", "dr.   patel", "OP 2", "quick check", "2030-01-14T10:30", "30", "bring x-rays")));

        var result = await Services().DryRun.RunAsync(batch, _actor, default);

        Assert.Equal((2, 0, 0, 0), (result.Valid, result.Rejected, result.Skipped, result.Unresolved));
        Assert.All((await RowsAsync(batch)).Values, r => Assert.Equal(ImportRowStatus.Valid, r.Status));
        var saved = (await Services().Intake.GetAsync(batch, default))!;
        Assert.Equal((ImportBatchStatus.DryRunComplete, 2, 0), (saved.Status, saved.AttemptedRows, saved.AcceptedRows));
        Assert.Equal(0, await _s.CountAsync());                                                      // a dry-run schedules nothing
        Assert.Equal(0, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == SchedulingAuditEvents.Scheduled)));
        Assert.NotEqual(ann, bo);
    }

    [Fact]
    public async Task Every_problem_with_a_rows_values_is_reported_at_once_as_a_fixed_message_and_never_a_value_from_the_file()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        var batch = await DryRunBatchAsync(Csv(
            Appt("", "", "", "", "", "", ""),                                                                                              // row 2: nothing at all
            Appt("Ann", "Rivera", "1950-13-45", "Dr. Rivera", "Op 1", "Exam", "tomorrow at nine"),                                         // row 3: not a date, not a time
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00", "abc"),                                  // row 4: duration not a number
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00", "7"),                                    // row 5: not a multiple of five
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00", "", new string('n', 1001)),              // row 6: a note over the limit
            Appt("ZEBRA", "Quill", "1950-01-10", "Dr. Nobody", "Op 9", "Surgery", "2030-01-14 09:00")));                                  // row 7: no such patient, provider, operatory or type

        await Services().DryRun.RunAsync(batch, _actor, default);

        var rows = await RowsAsync(batch);
        Assert.All(rows.Values, r => Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.InvalidFields), (r.Status, r.Reason)));
        Assert.Equal(["appointmentType", "operatory", "patientDateOfBirth", "patientFirstName", "patientLastName", "provider", "start"], rows[2].Errors!.Keys.Order());
        Assert.Equal(["patientDateOfBirth", "start"], rows[3].Errors!.Keys.Order());
        Assert.Equal("Duration must be a whole number of minutes.", rows[4].Errors!["durationMinutes"]);
        Assert.Contains("durationMinutes", rows[5].Errors!.Keys);
        Assert.Equal("A note can be at most 1000 characters.", rows[6].Errors!["notes"]);
        Assert.Equal(["appointmentType", "operatory", "patient", "provider"], rows[7].Errors!.Keys.Order());
        Assert.Equal("No registered patient has this name and date of birth. Import or register the patient first.", rows[7].Errors!["patient"]);
        await using var db = _fixture.CreateContext();
        var stored = string.Concat(await db.ImportBatchRows.AsNoTracking().Select(r => r.ErrorsJson).ToListAsync());
        foreach (var value in new[] { "ZEBRA", "Quill", "Nobody", "Surgery", "tomorrow", "1950-13-45" }) Assert.DoesNotContain(value, stored);          // only fixed messages are stored
    }

    [Fact]
    public async Task An_inactive_patient_provider_or_operatory_is_refused_and_a_name_is_matched_ignoring_capitals_and_spacing()
    {
        var cy = await RegisterAsync("Cy", "Lee", "1980-05-05", "555-500-5000");
        await using (var db = _fixture.CreateContext())
        {
            (await db.Patients.SingleAsync(p => p.Id == cy)).IsActive = false;
            (await db.ProviderProfiles.SingleAsync(p => p.Id == _s.ProviderB)).IsActive = false;
            (await db.Operatories.SingleAsync(o => o.Id == _s.Op2)).IsActive = false;
            await db.SaveChangesAsync();
        }

        var batch = await DryRunBatchAsync(Csv(Appt("Cy", "Lee", "1980-05-05", "Dr. Patel", "Op 2", "Exam", "2030-01-14 09:00")));

        var row = (await RowsAsync(batch))[2];
        Assert.Equal(ImportRowStatus.Rejected, row.Status);
        Assert.Equal("This patient is inactive.", row.Errors!["patient"]);
        Assert.Equal("This provider is inactive.", row.Errors["provider"]);
        Assert.Equal("This operatory is inactive.", row.Errors["operatory"]);
    }

    [Fact]
    public async Task A_start_in_the_past_in_a_daylight_saving_gap_or_fold_or_outside_the_providers_hours_is_refused_for_the_schedulers_own_reason()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        string Row(string start) => Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", start);
        var batch = await DryRunBatchAsync(Csv(
            Row("2020-01-13 09:00"),                                                               // row 2: the past
            Row("2030-01-13 09:00"),                                                               // row 3: a Sunday: the provider does not work
            Row("2030-01-15 06:00"),                                                               // row 4: before opening
            Row("2030-01-15 16:30"),                                                               // row 5: an hour from half past four runs past closing
            Row("2030-03-10 02:30"),                                                               // row 6: does not exist (spring forward)
            Row("2030-11-03 01:30")));                                                             // row 7: happens twice (fall back)

        await Services().DryRun.RunAsync(batch, _actor, default);

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.StartInPast), (rows[2].Status, rows[2].Reason));
        Assert.Equal("An appointment must start in the future.", rows[2].Errors!["start"]);
        foreach (var n in new[] { 3, 4, 5 })
        {
            Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.ProviderUnavailable), (rows[n].Status, rows[n].Reason));
            Assert.StartsWith("The provider is not available at that time (", rows[n].Errors!["provider"]);
        }

        foreach (var n in new[] { 6, 7 })
        {
            Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.InvalidFields), (rows[n].Status, rows[n].Reason));
            Assert.StartsWith("That time does not exist or is ambiguous", rows[n].Errors!["start"]);
        }
    }

    [Fact]
    public async Task A_period_held_by_an_existing_appointment_is_refused_provider_then_operatory_then_patient_and_back_to_back_is_fine()
    {
        var ann = await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");
        await BookAsync(ann, SchedulingTestSupport.At(9), _s.ProviderA, _s.Op1);                                                              // 09:00 to 10:00: Dr. Rivera, Op 1, Ann

        var batch = await DryRunBatchAsync(Csv(
            Appt("Bo", "Moss", "1962-03-04", "Dr. Rivera", "Op 2", "Exam", "2030-01-14 09:30"),                                              // row 2: the provider is busy
            Appt("Bo", "Moss", "1962-03-04", "Dr. Patel", "Op 1", "Exam", "2030-01-14 09:30"),                                               // row 3: the operatory is busy
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Patel", "Op 2", "Exam", "2030-01-14 09:30"),                                            // row 4: the patient is busy
            Appt("Bo", "Moss", "1962-03-04", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 10:00"),                                              // row 5: starts as the other ends: fine
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00")));                                         // row 6: exactly the appointment that is there

        var result = await Services().DryRun.RunAsync(batch, _actor, default);

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.ProviderDoubleBooked), (rows[2].Status, rows[2].Reason));
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.OperatoryConflict), (rows[3].Status, rows[3].Reason));
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.PatientDoubleBooked), (rows[4].Status, rows[4].Reason));
        Assert.Equal("The provider already has an appointment during that time.", rows[2].Errors!["schedule"]);
        Assert.Equal(ImportRowStatus.Valid, rows[5].Status);
        Assert.Equal((ImportRowStatus.Skipped, ImportRowReason.AlreadyScheduled), (rows[6].Status, rows[6].Reason));
        Assert.Equal((1, 3, 1), (result.Valid, result.Rejected, result.Skipped));
        Assert.Equal(1, await _s.CountAsync());                                                      // still only the one booked before
    }

    [Fact]
    public async Task A_period_held_by_an_earlier_row_of_the_file_is_refused_naming_that_row_and_an_exact_repeat_is_skipped()
    {
        foreach (var (first, last, dob, phone) in new[] { ("Cy", "Lee", "1980-05-05", "555-500-5000"), ("Di", "Nash", "1975-04-04", "555-400-4000"), ("Ed", "Ortiz", "1985-09-09", "555-900-9000") })
            await RegisterAsync(first, last, dob, phone);

        var batch = await DryRunBatchAsync(Csv(
            Appt("Cy", "Lee", "1980-05-05", "Dr. Patel", "Op 2", "Exam", "2030-01-14 13:00"),                                                // row 2: fine, and it holds Dr. Patel, Op 2 and Cy
            Appt("Di", "Nash", "1975-04-04", "Dr. Patel", "Op 1", "Exam", "2030-01-14 13:30"),                                               // row 3: Dr. Patel is held by row 2
            Appt("Ed", "Ortiz", "1985-09-09", "Dr. Rivera", "Op 2", "Exam", "2030-01-14 13:30"),                                             // row 4: Op 2 is held by row 2
            Appt("Cy", "Lee", "1980-05-05", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 13:30"),                                               // row 5: Cy is held by row 2
            Appt("Cy", "Lee", "1980-05-05", "Dr. Patel", "Op 2", "Exam", "2030-01-14 13:00")));                                              // row 6: exactly row 2 again

        await Services().DryRun.RunAsync(batch, _actor, default);

        var rows = await RowsAsync(batch);
        Assert.Equal(ImportRowStatus.Valid, rows[2].Status);
        Assert.Equal([(ImportRowReason.ProviderDoubleBooked, 2), (ImportRowReason.OperatoryConflict, 2), (ImportRowReason.PatientDoubleBooked, 2)],
            new[] { 3, 4, 5 }.Select(n => (rows[n].Reason!, rows[n].DuplicateOfRow!.Value)));
        Assert.Equal((ImportRowStatus.Skipped, ImportRowReason.DuplicateInFile, 2), (rows[6].Status, rows[6].Reason, rows[6].DuplicateOfRow));
        Assert.Equal(0, await _s.CountAsync());
    }

    [Fact]
    public async Task Running_the_dry_run_again_gives_the_same_results_and_a_rejected_row_holds_no_time()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");
        var batch = await UploadAsync(Csv(
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2020-01-13 09:00"),                                           // row 2: the past: rejected, so it holds nothing
            Appt("Bo", "Moss", "1962-03-04", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00"),                                              // row 3: valid
            Appt("Ann", "Rivera", "1950-01-10", "Dr. Patel", "Op 2", "Exam", "2030-01-14 09:00")));                                          // row 4: valid: Ann is not held by the rejected row 2

        await Services().DryRun.RunAsync(batch, _actor, default);
        var first = await RowsAsync(batch);
        await Services().DryRun.RunAsync(batch, _actor, default);
        var second = await RowsAsync(batch);

        Assert.Equal([ImportRowStatus.Rejected, ImportRowStatus.Valid, ImportRowStatus.Valid], first.OrderBy(r => r.Key).Select(r => r.Value.Status));
        Assert.Equal(first.OrderBy(r => r.Key).Select(r => (r.Value.Status, r.Value.Reason)), second.OrderBy(r => r.Key).Select(r => (r.Value.Status, r.Value.Reason)));
        Assert.Equal(3, await CountAsync(db => db.ImportBatchRows));
    }

    [Fact]
    public async Task Commit_and_decisions_are_not_available_for_appointment_files_until_their_slice()
    {
        var batch = await DryRunBatchAsync(Csv(Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", "2030-01-14 09:00")));
        var db = _fixture.CreateContext();
        var intake = new ImportIntakeService(db, _files, Options.Create(new ImportOptions()), _clock, NullLogger<ImportIntakeService>.Instance);

        var decision = await Assert.ThrowsAsync<ImportException>(() => new ImportDecisionService(db, _clock, intake).DecideAsync(batch, 2, ImportDecision.Skip, _actor, default));

        Assert.Equal("kind_not_supported_yet", decision.Code);
    }

    [Fact]
    public async Task A_large_file_is_dry_run_in_bulk_and_the_time_is_reported()
    {
        var rows = int.TryParse(Environment.GetEnvironmentVariable("ALVERA_IMPORT_ROWS"), out var n) && n > 0 ? n : 1_000;
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        var text = new StringBuilder(Header).Append("\r\n");
        var day = new DateTime(2030, 1, 14);
        for (var i = 0; i < rows;)
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { day = day.AddDays(1); continue; }
            for (var hour = 8; hour <= 16 && i < rows; hour++, i++) text.Append(Appt("Ann", "Rivera", "1950-01-10", "Dr. Rivera", "Op 1", "Exam", $"{day:yyyy-MM-dd} {hour:D2}:00")).Append("\r\n");
            day = day.AddDays(1);
        }

        var batch = await UploadAsync(Encoding.UTF8.GetBytes(text.ToString()));

        var watch = Stopwatch.StartNew();
        var result = await Services().DryRun.RunAsync(batch, _actor, default);
        watch.Stop();

        output.WriteLine($"appointment dry-run of {rows} rows: {watch.Elapsed.TotalSeconds:F1} s");
        Assert.Equal((rows, 0, 0, 0), (result.Valid, result.Rejected, result.Skipped, result.Unresolved));
        Assert.Equal(0, await _s.CountAsync());
    }
}
