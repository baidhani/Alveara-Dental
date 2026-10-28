using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 5b, committing an opening-balance file against a real SQL Server, a real folder and the real durable job runner: each valid row becomes the FIRST entry on the patient's ledger (an Adjustment named for the
/// batch, under the key import-bal-{batch}-{row}) and its result is saved at once; the ledger total the batch reports is what the ledger holds; a balance whose patient gained any entry after the dry-run, or became inactive, is
/// rejected and never added to a history; an entry already under the row's key is the row's result (a replay adds nothing); all-or-nothing is refused; a caller without ManageBilling is refused before anything changes; the billing
/// rule itself refuses a second balance, a reused key with another amount, a bad amount and an unknown patient; no name or amount-from-the-file reaches the audit log.
/// </summary>
public class ImportBalanceCommitTests : IAsyncLifetime
{
    private const string Header = "Patient First,Patient Last,DOB,Amount,As Of,Currency";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"alveara-import-test-{Guid.NewGuid():N}");
    private readonly IPracticeClock _clock = SchedulingTestSupport.Clock;
    private readonly ImportOptions _options = new();
    private ImportFileStorage _files = null!;
    private Guid _actor;

    private sealed class TestWorkScopeFactory(TestDatabaseFixture fixture, IPracticeClock clock) : IImportWorkScopeFactory
    {
        public ImportWorkScope Create()
        {
            var db = fixture.CreateContext();
            return new ImportWorkScope(db, null!, db, new BillingService(db, clock));
        }
    }

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

    private static string Bal(string first, string last, string dob, string amount) => $"{first},{last},{dob},{amount},,";

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", rows) + "\r\n");

    private (ImportIntakeService Intake, ImportMappingService Mapping, ImportDryRunService DryRun, ImportCommitService Commit) Services()
    {
        var db = _fixture.CreateContext();
        var intake = new ImportIntakeService(db, _files, Opts, _clock, NullLogger<ImportIntakeService>.Instance);
        var mapping = new ImportMappingService(db, _clock);
        return (intake, mapping, new ImportDryRunService(db, _files, Opts, _clock, mapping, intake), new ImportCommitService(db, new BackgroundJobQueue(db), _files, Opts, _clock, intake));
    }

    private BackgroundJobRunner Runner() => new(_fixture.CreateContext(), [new ImportCommitJobHandler(_files, Opts, _clock, new TestWorkScopeFactory(_fixture, _clock))], NullLogger<BackgroundJobRunner>.Instance);

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

    private async Task<(Guid Ann, Guid Bo)> TwoPatientsAsync() =>
        (await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000"), await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000"));

    private static byte[] TwoBalances() => Csv(Bal("Ann", "Rivera", "1950-01-10", "125.50"), Bal("Bo", "Moss", "1962-03-04", "40.00"));

    private async Task<Guid> DryRunBatchAsync(byte[] csv)
    {
        var s = Services();
        var batch = (await s.Intake.UploadAsync(ImportSourceKind.OpeningBalances, "Old system", csv, _actor, default)).Batch.Id;
        var suggestion = await s.Mapping.GetSuggestionAsync(batch, default);
        await s.Mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
        await s.DryRun.RunAsync(batch, _actor, default);
        return batch;
    }

    private Task<ImportBatchView> CommitAsync(Guid batch, int expected, string policy = ImportBatchPolicy.CommitValidRows, bool billing = true) =>
        Services().Commit.CommitAsync(batch, policy, expected, _actor, default, billing);

    private async Task<int> CountAsync<T>(Func<AlveraDbContext, IQueryable<T>> query) where T : class { await using var db = _fixture.CreateContext(); return await query(db).CountAsync(); }

    private async Task<ImportBatch> BatchAsync(Guid id) { await using var db = _fixture.CreateContext(); return await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id); }

    private async Task<Dictionary<int, ImportBatchRow>> RowsAsync(Guid batch) { await using var db = _fixture.CreateContext(); return await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batch).ToDictionaryAsync(r => r.RowNumber); }

    private async Task<List<BillingEntry>> EntriesAsync(Guid patient) { await using var db = _fixture.CreateContext(); return await db.BillingEntries.AsNoTracking().Where(e => e.PatientId == patient).OrderBy(e => e.EntryNumber).ToListAsync(); }

    private static async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<ImportException>(action)).Code;

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
    public async Task Each_valid_row_becomes_the_first_ledger_entry_and_the_batch_reports_the_ledger_total()
    {
        var (ann, bo) = await TwoPatientsAsync();
        var batch = await DryRunBatchAsync(TwoBalances());

        await CommitAsync(batch, 2);
        await DrainAsync();

        var done = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.Completed, 2, 165.50m, 165.50m), (done.Status, done.CommitOutcome, done.AcceptedRows, done.ValidAmount, done.AcceptedAmount));
        var rows = await RowsAsync(batch);
        foreach (var (patient, row, amount) in new[] { (ann, 2, 125.50m), (bo, 3, 40.00m) })
        {
            var entries = await EntriesAsync(patient);
            var entry = Assert.Single(entries);
            Assert.Equal((1, BillingKinds.Adjustment, amount, BillingService.OpeningBalanceReason, (Guid?)batch, ImportBalanceCommit.EntryKey(batch, row)), (entry.EntryNumber, entry.Kind, entry.Amount, entry.Reason, entry.CorrelationId, entry.IdempotencyKey));
            Assert.Equal((ImportRowStatus.Accepted, (Guid?)entry.Id), (rows[row].Status, rows[row].ResultRecordId));
        }

        Assert.Equal(2, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == "BillingOpeningBalanceRecorded")));
        await using var audit = _fixture.CreateContext();
        var everything = string.Join(" ", await audit.AuditLogEntries.AsNoTracking().Select(a => a.Details + " " + a.Reason).ToListAsync());
        Assert.DoesNotContain("Rivera", everything);
        Assert.DoesNotContain("Moss", everything);
    }

    [Fact]
    public async Task An_entry_already_under_the_rows_key_is_the_rows_result_and_nothing_is_added_twice()
    {
        var (ann, _) = await TwoPatientsAsync();
        var batch = await DryRunBatchAsync(TwoBalances());
        var crashed = await new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(ann, 125.50m, ImportBalanceCommit.EntryKey(batch, 2), batch, _actor, default);   // the entry was made, the row never saved

        await CommitAsync(batch, 2);
        await DrainAsync();

        var entry = Assert.Single(await EntriesAsync(ann));
        Assert.Equal(crashed, entry.Id);
        var row = (await RowsAsync(batch))[2];
        Assert.Equal((ImportRowStatus.Accepted, (Guid?)crashed), (row.Status, row.ResultRecordId));
        Assert.Equal(165.50m, (await BatchAsync(batch)).AcceptedAmount);
        Assert.Equal("batch_not_open", await CodeAsync(() => CommitAsync(batch, 0)));                       // a finished batch cannot be committed again
        Assert.Single(await EntriesAsync(ann));
    }

    [Fact]
    public async Task A_patient_who_gains_history_after_the_dry_run_is_rejected_and_the_balance_is_never_added_to_it()
    {
        var (ann, bo) = await TwoPatientsAsync();
        var batch = await DryRunBatchAsync(TwoBalances());
        await GiveHistoryAsync(bo);                                                                        // an entry was made after the dry-run

        await CommitAsync(batch, 2);
        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Accepted, ImportRowStatus.Rejected, ImportRowReason.AccountNotEmpty), (rows[2].Status, rows[3].Status, rows[3].Reason));
        var only = Assert.Single(await EntriesAsync(bo));
        Assert.Equal(("history-1", 10m), (only.IdempotencyKey, only.Amount));                               // the history is untouched
        var done = await BatchAsync(batch);
        Assert.Equal((ImportCommitOutcome.CompletedWithExceptions, 1, 1, 125.50m, 165.50m), (done.CommitOutcome, done.AcceptedRows, done.RejectedRows, done.AcceptedAmount, done.ValidAmount));
        Assert.Single(await EntriesAsync(ann));
    }

    [Fact]
    public async Task A_patient_made_inactive_after_the_dry_run_is_rejected_and_no_entry_is_made()
    {
        var (ann, bo) = await TwoPatientsAsync();
        var batch = await DryRunBatchAsync(TwoBalances());
        await using (var db = _fixture.CreateContext())
        {
            (await db.Patients.SingleAsync(p => p.Id == bo)).IsActive = false;
            await db.SaveChangesAsync();
        }

        await CommitAsync(batch, 2);
        await DrainAsync();

        var rows = await RowsAsync(batch);
        Assert.Equal((ImportRowStatus.Rejected, ImportRowReason.InvalidFields), (rows[3].Status, rows[3].Reason));
        Assert.Empty(await EntriesAsync(bo));
        Assert.Single(await EntriesAsync(ann));
    }

    [Fact]
    public async Task All_or_nothing_is_refused_and_a_caller_without_manage_billing_is_refused_before_anything_changes()
    {
        await TwoPatientsAsync();
        var batch = await DryRunBatchAsync(TwoBalances());

        Assert.Equal("billing_permission_required", await CodeAsync(() => CommitAsync(batch, 2, billing: false)));
        Assert.Equal("policy_not_supported", await CodeAsync(() => CommitAsync(batch, 2, ImportBatchPolicy.AllOrNothing)));

        var after = await BatchAsync(batch);
        Assert.Equal(ImportBatchStatus.DryRunComplete, after.Status);
        Assert.Equal(0, await CountAsync(db => db.BackgroundJobs.Where(j => j.JobType == ImportCommitJobs.JobType)));
        Assert.Equal(0, await CountAsync(db => db.BillingEntries));
    }

    [Fact]
    public async Task The_billing_rule_refuses_a_second_balance_a_reused_key_with_another_amount_a_bad_amount_and_an_unknown_patient()
    {
        var (ann, _) = await TwoPatientsAsync();
        var correlation = Guid.NewGuid();
        var first = await new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(ann, 20.00m, "key-1", correlation, _actor, default);

        var replay = await new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(ann, 20.00m, "key-1", correlation, _actor, default);
        var reused = await Assert.ThrowsAsync<BillingException>(() => new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(ann, 21.00m, "key-1", correlation, _actor, default));
        var second = await Assert.ThrowsAsync<BillingException>(() => new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(ann, 20.00m, "key-2", correlation, _actor, default));
        var unknown = await Assert.ThrowsAsync<BillingException>(() => new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(Guid.NewGuid(), 20.00m, "key-3", correlation, _actor, default));

        Assert.Equal(first, replay);
        Assert.Equal("idempotency_key_reused", reused.Code);
        Assert.Equal(("account_not_empty", 409), (second.Code, second.StatusCode));
        Assert.Equal("patient_not_found", unknown.Code);
        foreach (var bad in new decimal?[] { 0m, -5m, 1.234m, BillingLimits.AmountMax + 1, null })
            await Assert.ThrowsAsync<BillingException>(() => new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(ann, bad, "key-4", correlation, _actor, default));
        Assert.Single(await EntriesAsync(ann));
    }
}

/// <summary>ALV-018-C02 slice 5b over the real API: the administrator confirms a balance commit (202, Committing); all-or-nothing is 409 policy_not_supported.</summary>
public class ImportBalanceCommitApiTests : IAsyncLifetime
{
    private const string BatchesUrl = "/api/import-batches";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture);
        await new SchedulingTestSupport(_fixture).ArrangeAsync();
        _admin = await _api.SessionAsync("Admin");
        await using var db = _fixture.CreateContext();
        var registration = new PatientRegistrationService(db, SchedulingTestSupport.Clock);
        var actor = (await db.UserAccounts.AsNoTracking().FirstAsync()).Id;
        await registration.RegisterAsync(new RegisterPatientRequest("Ann", null, "Rivera", "1950-01-10", "Female", "555-100-1000", null, "1 Main St", null, "Austin", "TX", "78701"), $"test-{Guid.NewGuid():N}", actor, default);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(Session session, HttpMethod method, string url, object? body = null, byte[]? raw = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", session.Csrf);
        if (raw is not null) { request.Content = new ByteArrayContent(raw); request.Content.Headers.ContentType = new("text/csv"); }
        else if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        var response = await session.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task The_administrator_confirms_a_balance_commit_and_all_or_nothing_is_refused()
    {
        var csv = Encoding.UTF8.GetBytes("Patient First,Patient Last,DOB,Amount\r\nAnn,Rivera,1950-01-10,75.25\r\n");
        var id = (await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}?sourceKind=OpeningBalances&label=Old%20system", raw: csv)).Body.GetProperty("batch").GetProperty("id").GetGuid();
        var suggestion = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/mapping-suggestion");
        var mapping = suggestion.Body.GetProperty("suggested").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        await SendAsync(_admin, HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { mapping });
        var dry = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run");
        Assert.Equal(1, dry.Body.GetProperty("valid").GetInt32());

        var refused = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/commit", new { policy = "AllOrNothing", expectedValidRows = 1 });
        var accepted = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/commit", new { policy = "CommitValidRows", expectedValidRows = 1 });

        Assert.Equal((HttpStatusCode.Conflict, "policy_not_supported"), (refused.Status, refused.Body.GetProperty("error").GetString()));
        Assert.Equal(HttpStatusCode.Accepted, accepted.Status);
        Assert.Equal("Committing", accepted.Body.GetProperty("status").GetString());
    }
}
