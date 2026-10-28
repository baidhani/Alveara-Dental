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
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 2, mapping and the dry-run of a patient file against a real SQL Server and a real folder: the suggested mapping and its checks; a dry-run needs a confirmed mapping and an open batch of
/// patients; it judges every row by the front desk's own rules (each problem at once, as the same messages, never a value), marks the same name and birth date as Skipped (already registered, or a repeat of an
/// earlier row) and a likely match as Unresolved, and only the first of a group can be Valid; it judges a row exactly as the front desk's duplicate check does; it writes no patient and no registration audit entry;
/// running it again gives the same results without duplicating a row; changing the mapping makes the results stale; a changed or missing stored file is refused with nothing changed; two at once leave consistent
/// results; the rows can be listed by status with a cursor; and a large file is dry-run in bulk (the time is reported, not asserted).
/// </summary>
public class ImportPatientDryRunTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Header = "First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"alveara-import-test-{Guid.NewGuid():N}");
    private readonly IPracticeClock _clock = new PracticeClock(TimeZoneInfo.Utc);
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

    private static string Row(string first, string last, string dob, string phone = "555-100-1000", string email = "") => $"{first},{last},{dob},{phone},{email},4 Pine St,Austin,TX,78701";

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", rows) + "\r\n");

    private (ImportIntakeService Intake, ImportMappingService Mapping, ImportDryRunService DryRun) Services()
    {
        var db = _fixture.CreateContext();
        var intake = new ImportIntakeService(db, _files, Options.Create(new ImportOptions()), _clock, NullLogger<ImportIntakeService>.Instance);
        var mapping = new ImportMappingService(db, _clock);
        return (intake, mapping, new ImportDryRunService(db, _files, Options.Create(new ImportOptions()), _clock, mapping, intake));
    }

    private async Task<Guid> PrepareAsync(byte[] csv, bool map = true, string kind = ImportSourceKind.Patients)
    {
        var s = Services();
        var uploaded = await s.Intake.UploadAsync(kind, "Source", csv, _actor, default);
        if (map) await SaveSuggestedMappingAsync(uploaded.Batch.Id);
        return uploaded.Batch.Id;
    }

    private async Task SaveSuggestedMappingAsync(Guid batchId)
    {
        var s = Services();
        var suggestion = await s.Mapping.GetSuggestionAsync(batchId, default);
        await s.Mapping.SaveAsync(batchId, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
    }

    private async Task<Guid> RegisterAsync(string first, string last, string dob, string phone = "555-100-1000", string? email = null)
    {
        var service = new PatientRegistrationService(_fixture.CreateContext(), _clock);
        var result = await service.RegisterAsync(new RegisterPatientRequest(first, null, last, dob, "Female", phone, email, "1 Main St", null, "Austin", "TX", "78701"), $"test-{Guid.NewGuid():N}", _actor, default);
        return result.Patient.Id;
    }

    private Task<ImportDryRunResult> DryRunAsync(Guid batchId) => Services().DryRun.RunAsync(batchId, _actor, default);

    private Task<IReadOnlyList<ImportRowView>> RowsAsync(Guid batchId, string? status = null, int limit = 500, int after = 1) => Services().DryRun.ListRowsAsync(batchId, status, limit, after, default);

    private async Task<int> CountAsync<T>(Func<AlveraDbContext, IQueryable<T>> query) where T : class { await using var db = _fixture.CreateContext(); return await query(db).CountAsync(); }

    private static async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<ImportException>(action)).Code;

    private async Task<ImportBatch> BatchAsync(Guid id) { await using var db = _fixture.CreateContext(); return await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id); }

    [Fact]
    public async Task The_suggested_mapping_names_the_obvious_columns_reports_the_rest_and_a_saved_mapping_is_what_the_dry_run_uses()
    {
        var csv = Encoding.UTF8.GetBytes(Header + ",Notes\r\n" + Row("Ann", "Rivera", "1950-01-10") + ",remember\r\n");
        var batch = await PrepareAsync(csv, map: false);
        var s = Services();

        var suggestion = await s.Mapping.GetSuggestionAsync(batch, default);

        Assert.Equal("firstName", suggestion.Suggested["First Name"]);
        Assert.Equal("postalCode", suggestion.Suggested["Zip"]);
        Assert.Null(suggestion.Current);
        Assert.Equal(["Notes"], suggestion.UnmappedColumns);
        Assert.Empty(suggestion.MissingRequired);

        var saved = await s.Mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);

        Assert.Equal(suggestion.Suggested, saved.Current);
        Assert.Equal(1, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == AuditEventTypes.ImportMappingSaved)));
    }

    [Fact]
    public async Task A_bad_or_missing_mapping_is_refused_with_its_problems_and_a_dry_run_needs_a_confirmed_one()
    {
        var batch = await PrepareAsync(Csv(Row("Ann", "Rivera", "1950-01-10")), map: false);
        var s = Services();

        Assert.Equal("mapping_required", await CodeAsync(() => s.DryRun.RunAsync(batch, _actor, default)));
        Assert.Equal("invalid_mapping", await CodeAsync(() => s.Mapping.SaveAsync(batch, null, _actor, default)));
        Assert.Equal("invalid_mapping", await CodeAsync(() => s.Mapping.SaveAsync(batch, new Dictionary<string, string?>(), _actor, default)));
        var refused = await Assert.ThrowsAsync<ImportMappingRejectedException>(() => s.Mapping.SaveAsync(batch, new Dictionary<string, string?> { ["First Name"] = "firstName", ["Ghost"] = "city" }, _actor, default));

        Assert.Contains("unknown_column", refused.Problems.Select(p => p.Code));
        Assert.Contains("required_field_unmapped", refused.Problems.Select(p => p.Code));
        Assert.Null((await BatchAsync(batch)).MappingJson);                                        // nothing was saved
    }

    [Fact]
    public async Task Every_valid_row_is_valid_the_batch_is_dry_run_and_nothing_is_registered()
    {
        var batch = await PrepareAsync(Csv(Row("Ann", "Rivera", "1950-01-10"), Row("Bo", "Moss", "1962-03-04", "555-300-3000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000", "cy@example.test")));

        var result = await DryRunAsync(batch);

        Assert.Equal((3, 0, 0, 0), (result.Valid, result.Rejected, result.Skipped, result.Unresolved));
        var saved = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.DryRunComplete, 3, 0, 0, 0, 0), (saved.Status, saved.AttemptedRows, saved.AcceptedRows, saved.RejectedRows, saved.SkippedRows, saved.UnresolvedRows));
        Assert.NotNull(saved.DryRunAtUtc);
        Assert.All(await RowsAsync(batch), r => Assert.Equal(ImportRowStatus.Valid, r.Status));
        Assert.Equal(0, await CountAsync(db => db.Patients));                                       // a dry-run registers no patient
        Assert.Equal(0, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == PatientAuditEvents.Registered)));
        Assert.Equal(1, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == AuditEventTypes.ImportDryRunCompleted)));
    }

    [Fact]
    public async Task An_invalid_row_is_rejected_with_the_front_desks_own_messages_at_once_and_never_a_value_from_the_file()
    {
        var batch = await PrepareAsync(Csv(
            Row("", "Rivera", "1950-01-10"),                                                       // row 2: no first name
            Row("Ann", "Rivera", "1950-13-45"),                                                    // row 3: not a date
            Row("Ann", "Rivera", "2999-01-01"),                                                    // row 4: in the future
            Row("Ann", "Rivera", "1950-01-10", phone: "12"),                                       // row 5: phone too short
            Row("Ann", "Rivera", "1950-01-10", email: "not-an-email"),                             // row 6
            Row("ZEBRA", "", "1950-01-10", phone: ""),                                             // row 7: no last name and no phone (two problems at once)
            Row("Dee", "Park", "1971-02-02")));                                                    // row 8: fine

        var result = await DryRunAsync(batch);

        Assert.Equal((1, 6), (result.Valid, result.Rejected));
        var rows = (await RowsAsync(batch)).ToDictionary(r => r.RowNumber);
        Assert.Equal("First name is required.", rows[2].Errors!["firstName"]);
        Assert.Equal("Date of birth must be a real date in the form yyyy-MM-dd.", rows[3].Errors!["dateOfBirth"]);
        Assert.Equal("Date of birth cannot be in the future.", rows[4].Errors!["dateOfBirth"]);
        Assert.Equal("Phone must contain at least 7 digits.", rows[5].Errors!["phone"]);
        Assert.Equal("Email must look like name@example.com.", rows[6].Errors!["email"]);
        Assert.Equal(["lastName", "phone"], rows[7].Errors!.Keys.Order());
        Assert.All(new[] { 2, 3, 4, 5, 6, 7 }, n => Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.InvalidFields), (rows[n].Status, rows[n].Reason)));
        Assert.Equal(ImportRowStatus.Valid, rows[8].Status);

        await using var db = _fixture.CreateContext();
        var stored = string.Concat(await db.ImportBatchRows.AsNoTracking().Select(r => r.ErrorsJson).ToListAsync());
        foreach (var value in new[] { "ZEBRA", "not-an-email", "2999", "1950-13-45" }) Assert.DoesNotContain(value, stored);          // the stored errors are the fixed messages only
    }

    [Fact]
    public async Task A_registered_person_is_skipped_a_likely_match_is_unresolved_and_only_the_first_of_a_repeated_group_is_valid()
    {
        var ann = await RegisterAsync("Ann", "Rivera", "1950-01-10");
        var batch = await PrepareAsync(Csv(
            Row("Ann", "Rivera", "1950-01-10"),                                                    // row 2: the same person as a registered patient
            Row("Anna", "Rivera", "1950-01-10", "555-200-2000"),                                   // row 3: same birth date and last name: a person decides
            Row("Bo", "Moss", "1962-03-04", "555-300-3000"),                                       // row 4: new
            Row("Bo", "Moss", "1962-03-04", "555-300-3000"),                                       // row 5: the same person as row 4
            Row("Beau", "Moss", "1962-03-04", "555-400-4000"),                                     // row 6: likely the same as row 4
            Row("Cy", "Lee", "1980-05-05", "555-500-5000")));                                      // row 7: new

        var result = await DryRunAsync(batch);

        Assert.Equal((2, 0, 2, 2), (result.Valid, result.Rejected, result.Skipped, result.Unresolved));
        var rows = (await RowsAsync(batch)).ToDictionary(r => r.RowNumber);
        Assert.Equal((ImportRowStatus.Skipped, ImportRowReason.AlreadyRegistered, ann), (rows[2].Status, rows[2].Reason, Assert.Single(rows[2].MatchedPatientIds)));
        Assert.Equal((ImportRowStatus.Unresolved, ImportRowReason.PossibleDuplicate, ann), (rows[3].Status, rows[3].Reason, Assert.Single(rows[3].MatchedPatientIds)));
        Assert.Equal(ImportRowStatus.Valid, rows[4].Status);
        Assert.Equal((ImportRowStatus.Skipped, ImportRowReason.DuplicateInFile, 4), (rows[5].Status, rows[5].Reason, rows[5].DuplicateOfRow));
        Assert.Equal((ImportRowStatus.Unresolved, ImportRowReason.PossibleDuplicate, 4), (rows[6].Status, rows[6].Reason, rows[6].DuplicateOfRow));
        Assert.Empty(rows[6].MatchedPatientIds);
        Assert.Equal(ImportRowStatus.Valid, rows[7].Status);
        Assert.Equal(1, await CountAsync(db => db.Patients));                                       // still only the one registered before
    }

    [Fact]
    public async Task A_row_is_judged_exactly_as_the_front_desks_duplicate_check_judges_it()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000", "ann@example.test");
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");
        var probes = new[]
        {
            Row("Ann", "Rivera", "1950-01-10"), Row("Anne", "Rivera", "1950-01-10", "555-111-1111"), Row("Ann", "Rivera", "1950-01-11"), Row("Bob", "Moss", "1962-03-04", "555-222-2222"),
            Row("Bo", "Mose", "1962-03-04", "555-333-3333"), Row("Zed", "Zane", "1962-03-04", "555-300-3000"), Row("Dan", "Fox", "1990-01-01", "555-444-4444"), Row("Eve", "Zane", "1962-03-04", "555-555-5555", "x@example.test"),
        };

        foreach (var probe in probes)
        {
            var batch = await PrepareAsync(Csv(probe));                                            // one row for each file, so the rows cannot affect one another
            await DryRunAsync(batch);
            var row = Assert.Single(await RowsAsync(batch));

            var cells = probe.Split(',');
            var fields = new PatientFields(cells[0], null, cells[1], cells[2], null, cells[3], cells[4].Length == 0 ? null : cells[4], cells[5], null, cells[6], cells[7], cells[8]);
            var candidates = await new PatientRegistrationService(_fixture.CreateContext(), _clock).CheckDuplicatesAsync(fields, default);
            var expected = candidates.Any(c => c.Exact) ? ImportRowStatus.Skipped : candidates.Count > 0 ? ImportRowStatus.Unresolved : ImportRowStatus.Valid;

            Assert.True(expected == row.Status, $"{probe}: the front desk says {expected}, the dry-run says {row.Status}");
            Assert.Equal(candidates.Where(c => expected != ImportRowStatus.Skipped || c.Exact).Select(c => c.Id).Order(), row.MatchedPatientIds.Order());      // a skipped row names the registered person it is, an unresolved one every likely match
        }
    }

    [Fact]
    public async Task Running_the_dry_run_again_gives_the_same_results_without_duplicating_a_row_and_changing_the_mapping_makes_them_stale()
    {
        var batch = await PrepareAsync(Csv(Row("Ann", "Rivera", "1950-01-10"), Row("", "Moss", "1962-03-04"), Row("Cy", "Lee", "1980-05-05", "555-500-5000")));

        await DryRunAsync(batch);
        var first = await RowsAsync(batch);
        await DryRunAsync(batch);
        var second = await RowsAsync(batch);

        Assert.Equal(first.Select(r => (r.RowNumber, r.Status, r.Reason)), second.Select(r => (r.RowNumber, r.Status, r.Reason)));
        Assert.Equal(3, await CountAsync(db => db.ImportBatchRows));
        Assert.Equal(2, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == AuditEventTypes.ImportDryRunCompleted)));

        await SaveSuggestedMappingAsync(batch);                                                     // saving a mapping again: the results were for another mapping
        var stale = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Uploaded, 0), (stale.Status, stale.AttemptedRows));
        Assert.Null(stale.DryRunAtUtc);
        Assert.Equal("dry_run_required", await CodeAsync(() => RowsAsync(batch)));

        await DryRunAsync(batch);
        Assert.Equal(3, await CountAsync(db => db.ImportBatchRows));                                // replaced in place, never doubled
        Assert.Equal(3, (await RowsAsync(batch)).Count);
    }

    [Fact]
    public async Task A_changed_or_missing_stored_file_is_refused_and_nothing_is_changed()
    {
        var changed = await PrepareAsync(Csv(Row("Ann", "Rivera", "1950-01-10")));
        var missing = await PrepareAsync(Csv(Row("Bo", "Moss", "1962-03-04")));
        await File.AppendAllTextAsync(Path.Combine(_folder, (await BatchAsync(changed)).FileBlobId.ToString("N")), "tampered");
        File.Delete(Path.Combine(_folder, (await BatchAsync(missing)).FileBlobId.ToString("N")));

        Assert.Equal("import_file_changed", await CodeAsync(() => DryRunAsync(changed)));
        Assert.Equal("import_file_missing", await CodeAsync(() => DryRunAsync(missing)));

        foreach (var id in new[] { changed, missing }) Assert.Equal(ImportBatchStatus.Uploaded, (await BatchAsync(id)).Status);
        Assert.Equal(0, await CountAsync(db => db.ImportBatchRows));
        Assert.Equal(0, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == AuditEventTypes.ImportDryRunCompleted)));
    }

    [Fact]
    public async Task Two_dry_runs_at_once_leave_one_consistent_set_of_results_and_any_refusal_is_a_stable_code()
    {
        var batch = await PrepareAsync(Csv(Row("Ann", "Rivera", "1950-01-10"), Row("Bo", "Moss", "1962-03-04", "555-300-3000"), Row("Cy", "Lee", "1980-05-05", "555-500-5000")));

        var outcomes = await Task.WhenAll(Settle(DryRunAsync(batch)), Settle(DryRunAsync(batch)));

        Assert.Contains(outcomes, o => o == "ok");
        Assert.All(outcomes, o => Assert.True(o is "ok" or "dry_run_in_progress" or "batch_changed", o));
        Assert.Equal(3, await CountAsync(db => db.ImportBatchRows));
        Assert.Equal(ImportBatchStatus.DryRunComplete, (await BatchAsync(batch)).Status);

        static async Task<string> Settle(Task<ImportDryRunResult> run)
        {
            try { await run; return "ok"; }
            catch (ImportException ex) { return ex.Code; }
        }
    }

    [Fact]
    public async Task The_rows_are_listed_by_status_with_a_cursor_and_a_limit_and_only_after_a_dry_run()
    {
        var batch = await PrepareAsync(Csv(Row("", "A", "1950-01-01"), Row("Bo", "B", "1950-01-02", "555-200-2000"), Row("", "C", "1950-01-03"), Row("Di", "D", "1950-01-04", "555-300-3000"), Row("", "E", "1950-01-05")));
        Assert.Equal("dry_run_required", await CodeAsync(() => RowsAsync(batch)));

        await DryRunAsync(batch);

        Assert.Equal([2, 3, 4, 5, 6], (await RowsAsync(batch)).Select(r => r.RowNumber));
        Assert.Equal([2, 4, 6], (await RowsAsync(batch, ImportRowStatus.Rejected)).Select(r => r.RowNumber));
        Assert.Equal([3, 5], (await RowsAsync(batch, ImportRowStatus.Valid)).Select(r => r.RowNumber));
        Assert.Equal([4, 5], (await RowsAsync(batch, limit: 2, after: 3)).Select(r => r.RowNumber));
        Assert.Single(await RowsAsync(batch, limit: 0, after: 5));                                  // a limit below one means the default; only row 6 is after row 5
        Assert.Equal("invalid_status", await CodeAsync(() => RowsAsync(batch, "Weird")));
        Assert.Equal("batch_not_found", await CodeAsync(() => RowsAsync(Guid.NewGuid())));
    }

    [Fact]
    public async Task Only_an_open_batch_of_patients_can_be_mapped_or_dry_run_and_an_unknown_batch_is_not_found()
    {
        var finished = await PrepareAsync(Csv(Row("Ann", "Rivera", "1950-01-10")));
        await using (var db = _fixture.CreateContext())
        {
            var batch = await db.ImportBatches.SingleAsync(b => b.Id == finished);
            batch.Status = ImportBatchStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var appointments = await PrepareAsync(Csv(Row("Bo", "Moss", "1962-03-04")), map: false, kind: ImportSourceKind.OpeningBalances);          // appointments have their own dry-run now (slice 4a); balances come later
        var s = Services();

        Assert.Equal("batch_not_open", await CodeAsync(() => s.DryRun.RunAsync(finished, _actor, default)));
        Assert.Equal("batch_not_open", await CodeAsync(() => s.Mapping.GetSuggestionAsync(finished, default)));
        Assert.Equal("kind_not_supported_yet", await CodeAsync(() => s.DryRun.RunAsync(appointments, _actor, default)));
        Assert.Equal("kind_not_supported_yet", await CodeAsync(() => s.Mapping.GetSuggestionAsync(appointments, default)));
        Assert.Equal("batch_not_found", await CodeAsync(() => s.DryRun.RunAsync(Guid.NewGuid(), _actor, default)));
        Assert.Equal("batch_not_found", await CodeAsync(() => s.Mapping.SaveAsync(Guid.NewGuid(), new Dictionary<string, string?> { ["x"] = "city" }, _actor, default)));
    }

    [Fact]
    public async Task A_large_file_is_dry_run_in_bulk_and_the_time_is_reported()
    {
        var rows = int.TryParse(Environment.GetEnvironmentVariable("ALVERA_IMPORT_ROWS"), out var n) && n > 0 ? n : 5_000;
        var text = new StringBuilder(Header).Append("\r\n");
        for (var i = 0; i < rows; i++)
            text.Append($"First{i},Last{i},{new DateOnly(1930, 1, 1).AddDays(i % 25_000):yyyy-MM-dd},555-{i:D7},u{i}@example.test,4 Pine St,Austin,TX,78701\r\n");
        var batch = await PrepareAsync(Encoding.UTF8.GetBytes(text.ToString()));

        var watch = Stopwatch.StartNew();
        var result = await DryRunAsync(batch);
        watch.Stop();

        output.WriteLine($"dry-run of {rows} rows: {watch.Elapsed.TotalSeconds:F1} s");
        Assert.Equal((rows, 0, 0, 0), (result.Valid, result.Rejected, result.Skipped, result.Unresolved));
        Assert.Equal(rows, await CountAsync(db => db.ImportBatchRows));
        Assert.Equal(0, await CountAsync(db => db.Patients));
    }
}
