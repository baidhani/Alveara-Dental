using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 5a, the mapping and dry-run of an opening-balance file against a real SQL Server: a valid row is Valid, the batch records the total of the valid balances (what the operator reconciles with the old
/// system) and nothing is written to any ledger; every problem with a row's values is reported at once as a fixed message, never a value from the file; a credit, zero, a fraction of a cent, a figure above the ceiling,
/// a currency other than USD and an as-of date in the future are refused; the patient must be exactly one registered, active patient with that name and birth date; a second row for the same patient is refused and names the
/// first; a patient whose ledger already has any entry is refused (a balance is never added to a history); the dry-run is repeatable and a new mapping makes its results (and the total) stale.
/// </summary>
public class ImportBalanceDryRunTests : IAsyncLifetime
{
    private const string Header = "Patient First,Patient Last,DOB,Amount,As Of,Currency";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"alveara-import-test-{Guid.NewGuid():N}");
    private readonly IPracticeClock _clock = SchedulingTestSupport.Clock;
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

    private static string Bal(string first, string last, string dob, string amount, string asOf = "", string currency = "") => $"{first},{last},{dob},{amount},{asOf},{currency}";

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
        var batch = (await s.Intake.UploadAsync(ImportSourceKind.OpeningBalances, "Old system", csv, _actor, default)).Batch.Id;
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

    private async Task<int> CountAsync<T>(Func<Alveara.Api.Data.AlveraDbContext, IQueryable<T>> query) where T : class { await using var db = _fixture.CreateContext(); return await query(db).CountAsync(); }

    private async Task GiveHistoryAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var account = new BillingAccount { Id = Guid.NewGuid(), PatientId = patient, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = _actor };
        db.BillingAccounts.Add(account);
        db.BillingEntries.Add(new BillingEntry
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patient, EntryNumber = 1, Kind = BillingKinds.Adjustment, Amount = 10m, Reason = "Earlier balance", IdempotencyKey = "history-1",
            RecordedAtUtc = DateTimeOffset.UtcNow, RecordedByUserId = _actor,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_suggested_mapping_names_the_obvious_columns_and_the_optional_ones_may_be_left_out()
    {
        var batch = await UploadAsync(Csv(Bal("Ann", "Rivera", "1950-01-10", "10.00")), map: false);

        var suggestion = await Services().Mapping.GetSuggestionAsync(batch, default);

        Assert.Equal("patientFirstName", suggestion.Suggested["Patient First"]);
        Assert.Equal("amount", suggestion.Suggested["Amount"]);
        Assert.Equal("asOfDate", suggestion.Suggested["As Of"]);
        Assert.Equal("currency", suggestion.Suggested["Currency"]);
        Assert.Empty(suggestion.MissingRequired);
        Assert.Equal(OpeningBalanceImportFields.All.Select(f => f.Key), suggestion.Fields.Select(f => f.Key));
        Assert.Equal(["patientFirstName", "patientLastName", "patientDateOfBirth", "amount"], suggestion.Fields.Where(f => f.Required).Select(f => f.Key));
    }

    [Fact]
    public async Task Valid_rows_are_valid_the_batch_records_their_total_and_nothing_is_written_to_a_ledger()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-100-1001");

        var batch = await DryRunBatchAsync(Csv(Bal("Ann", "Rivera", "1950-01-10", "125.50", "2020-01-31", "usd"), Bal("Bo", "Moss", "1962-03-04", "40")));

        var rows = await RowsAsync(batch);
        Assert.Equal([ImportRowStatus.Valid, ImportRowStatus.Valid], rows.Values.Select(r => r.Status));
        await using var db = _fixture.CreateContext();
        var stored = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batch);
        Assert.Equal((165.50m, 0m, ImportBatchStatus.DryRunComplete), (stored.ValidAmount, stored.AcceptedAmount, stored.Status));
        Assert.Equal(0, await CountAsync(d => d.BillingAccounts));
        Assert.Equal(0, await CountAsync(d => d.BillingEntries));
        Assert.Equal(1, await CountAsync(d => d.AuditLogEntries.Where(a => a.EventType == AuditEventTypes.ImportDryRunCompleted)));
    }

    [Fact]
    public async Task Every_problem_with_a_rows_values_is_reported_at_once_as_a_fixed_message_and_never_echoes_the_file()
    {
        foreach (var (first, phone) in new[] { ("Ann", "555-100-1000") }) await RegisterAsync(first, "Rivera", "1950-01-10", phone);
        var batch = await DryRunBatchAsync(Csv(
            Bal("Ann", "Rivera", "1950-01-10", "-5.00"),               // 2: a credit
            Bal("Ann", "Rivera", "1950-01-10", "0"),                   // 3: zero
            Bal("Ann", "Rivera", "1950-01-10", "10.005"),              // 4: a fraction of a cent
            Bal("Ann", "Rivera", "1950-01-10", "1000000.01"),          // 5: above the ceiling
            Bal("Ann", "Rivera", "1950-01-10", "SECRET-TEXT"),         // 6: not a number
            Bal("Ann", "Rivera", "1950-01-10", "\"1,250.00\""),        // 7: a thousands separator
            Bal("Ann", "Rivera", "1950-01-10", "10.00", "2999-01-01"), // 8: as of the future
            Bal("Ann", "Rivera", "1950-01-10", "10.00", "", "EUR"),    // 9: another currency
            Bal("", "", "", "")));                                     // 10: nothing at all

        var rows = await RowsAsync(batch);

        Assert.All(rows.Values, r => Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.InvalidFields), (r.Status, r.Reason)));
        Assert.Equal("amount", Assert.Single(rows[2].Errors!).Key);
        Assert.Contains("credit", rows[2].Errors!["amount"]);
        Assert.Equal("amount", Assert.Single(rows[3].Errors!).Key);
        Assert.Equal("amount", Assert.Single(rows[4].Errors!).Key);
        Assert.Equal("amount", Assert.Single(rows[5].Errors!).Key);
        Assert.Equal("amount", Assert.Single(rows[6].Errors!).Key);
        Assert.Equal("amount", Assert.Single(rows[7].Errors!).Key);
        Assert.Equal("asOfDate", Assert.Single(rows[8].Errors!).Key);
        Assert.Equal("currency", Assert.Single(rows[9].Errors!).Key);
        Assert.Equal(new[] { "patientDateOfBirth", "patientFirstName", "patientLastName", "amount" }.Order(), rows[10].Errors!.Keys.Order());
        var everything = JsonSerializer.Serialize(rows.Values.Select(r => r.Errors));
        Assert.DoesNotContain("SECRET-TEXT", everything);
        Assert.DoesNotContain("1,250.00", everything);
        Assert.DoesNotContain("EUR", everything);
        await using var db = _fixture.CreateContext();
        Assert.Equal(0m, (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batch)).ValidAmount);
    }

    [Fact]
    public async Task The_patient_must_be_exactly_one_registered_active_patient_and_the_reason_is_a_fixed_message()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        var inactive = await RegisterAsync("Cy", "Dunn", "1970-05-06", "555-100-1002");
        await using (var db = _fixture.CreateContext()) await db.Patients.Where(p => p.Id == inactive).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));

        var batch = await DryRunBatchAsync(Csv(Bal("Ann", "Rivera", "1950-01-11", "10.00"), Bal("Cy", "Dunn", "1970-05-06", "10.00"), Bal("ann", " RIVERA ", "1950-01-10", "10.00")));

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.InvalidFields), (rows[2].Status, rows[2].Reason));
        Assert.Contains("No registered patient", rows[2].Errors!["patient"]);                // the wrong birth date is no match
        Assert.Contains("inactive", rows[3].Errors!["patient"]);
        Assert.Equal(ImportRowStatus.Valid, rows[4].Status);                                  // capitals and spacing do not matter
    }

    [Fact]
    public async Task A_second_row_for_the_same_patient_is_refused_and_names_the_first_row()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");

        var batch = await DryRunBatchAsync(Csv(Bal("Ann", "Rivera", "1950-01-10", "10.00"), Bal("Ann", "Rivera", "1950-01-10", "20.00")));

        var rows = await RowsAsync(batch);
        Assert.Equal(ImportRowStatus.Valid, rows[2].Status);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.PatientRepeatedInFile, 2), (rows[3].Status, rows[3].Reason, rows[3].DuplicateOfRow));
        await using var db = _fixture.CreateContext();
        Assert.Equal(10.00m, (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batch)).ValidAmount);          // only the first row counts
    }

    [Fact]
    public async Task A_patient_whose_ledger_already_has_an_entry_is_refused_and_a_patient_without_one_is_not()
    {
        var ann = await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-100-1001");
        await GiveHistoryAsync(ann);

        var batch = await DryRunBatchAsync(Csv(Bal("Ann", "Rivera", "1950-01-10", "10.00"), Bal("Bo", "Moss", "1962-03-04", "30.00")));

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.AccountNotEmpty), (rows[2].Status, rows[2].Reason));
        Assert.Equal(ImportRowStatus.Valid, rows[3].Status);
        await using var db = _fixture.CreateContext();
        Assert.Equal(30.00m, (await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batch)).ValidAmount);
        Assert.Equal(1, await CountAsync(d => d.BillingEntries));                           // the dry-run wrote nothing
    }

    [Fact]
    public async Task The_dry_run_is_repeatable_and_a_new_mapping_makes_the_results_and_the_total_stale()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        var batch = await DryRunBatchAsync(Csv(Bal("Ann", "Rivera", "1950-01-10", "10.00")));
        var s = Services();

        await s.DryRun.RunAsync(batch, _actor, default);                                      // again: the same results

        await using (var db = _fixture.CreateContext())
        {
            var again = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batch);
            Assert.Equal((10.00m, 1), (again.ValidAmount, await db.ImportBatchRows.CountAsync(r => r.BatchId == batch)));
        }

        var suggestion = await s.Mapping.GetSuggestionAsync(batch, default);
        await s.Mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);

        await using var after = _fixture.CreateContext();
        var stale = await after.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == batch);
        Assert.Equal((ImportBatchStatus.Uploaded, 0m, 0m), (stale.Status, stale.ValidAmount, stale.AcceptedAmount));
    }

    [Fact]
    public async Task A_batch_of_balances_cannot_be_committed_yet()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        var batch = await DryRunBatchAsync(Csv(Bal("Ann", "Rivera", "1950-01-10", "10.00")));
        var db = _fixture.CreateContext();
        var options = Options.Create(new ImportOptions());
        var service = new ImportCommitService(db, null!, _files, options, _clock, Services().Intake);                // the refusal comes before the queue is ever used

        var ex = await Assert.ThrowsAsync<ImportException>(() => service.CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 1, _actor, default));

        Assert.Equal("kind_not_supported_yet", ex.Code);
    }
}
