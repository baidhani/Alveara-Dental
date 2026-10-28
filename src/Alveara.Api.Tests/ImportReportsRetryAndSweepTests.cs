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
/// ALV-018-C02 slice 6 against a real SQL Server, a real folder and the real durable job runner. Retry: a commit whose jobs all failed is stuck, the batch says it can be retried, the retry starts a new round that finishes the
/// batch without repeating a row already done (an entry made before the "crash" is the row's result), a commit with live jobs, a finished batch and an unknown batch are refused, and a balance batch needs ManageBilling. Reconciliation:
/// a committed patient batch and a committed balance batch reconcile (rows, records made, ledger total) and a doctored row is reported as a named problem. Error report: every rejected, skipped and unresolved row with its fixed message,
/// no value from the file, formula-looking cells neutralised. Sweep: an old unreferenced file is removed, a young one and every batch's own file are kept, a repeat changes nothing, and a missing file is reported.
/// </summary>
public class ImportReportsRetryAndSweepTests : IAsyncLifetime
{
    private const string BalanceHeader = "Patient First,Patient Last,DOB,Amount,As Of,Currency";
    private const string PatientHeader = "First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip";
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

    private ImportIntakeService Intake() => new(_fixture.CreateContext(), _files, Opts, _clock, NullLogger<ImportIntakeService>.Instance);

    private ImportCommitService Commit() { var db = _fixture.CreateContext(); return new ImportCommitService(db, new BackgroundJobQueue(db), _files, Opts, _clock, Intake()); }

    private ImportRetryService Retry() { var db = _fixture.CreateContext(); return new ImportRetryService(db, new BackgroundJobQueue(db), Opts, _clock, Intake()); }

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

    private async Task<Guid> DryRunAsync(string kind, byte[] csv)
    {
        var intake = Intake();
        var db = _fixture.CreateContext();
        var mapping = new ImportMappingService(db, _clock);
        var batch = (await intake.UploadAsync(kind, "Old system", csv, _actor, default)).Batch.Id;
        var suggestion = await mapping.GetSuggestionAsync(batch, default);
        await mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
        await new ImportDryRunService(db, _files, Opts, _clock, mapping, intake).RunAsync(batch, _actor, default);
        return batch;
    }

    private static byte[] Balances(params string[] rows) => Encoding.UTF8.GetBytes(BalanceHeader + "\r\n" + string.Join("\r\n", rows) + "\r\n");

    private static byte[] TwoBalances() => Balances("Ann,Rivera,1950-01-10,125.50,,", "Bo,Moss,1962-03-04,40.00,,");

    private static byte[] PatientFile(params string[] rows) => Encoding.UTF8.GetBytes(PatientHeader + "\r\n" + string.Join("\r\n", rows) + "\r\n");

    private async Task<int> CountAsync<T>(Func<AlveraDbContext, IQueryable<T>> query) where T : class { await using var db = _fixture.CreateContext(); return await query(db).CountAsync(); }

    private async Task<ImportBatch> BatchAsync(Guid id) { await using var db = _fixture.CreateContext(); return await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == id); }

    private async Task<List<BillingEntry>> EntriesAsync(Guid patient) { await using var db = _fixture.CreateContext(); return await db.BillingEntries.AsNoTracking().Where(e => e.PatientId == patient).ToListAsync(); }

    private static async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<ImportException>(action)).Code;

    [Fact]
    public async Task A_stuck_commit_is_offered_a_retry_which_finishes_the_batch_without_repeating_a_row_done_before()
    {
        var ann = await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        var bo = await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");
        var batch = await DryRunAsync(ImportSourceKind.OpeningBalances, TwoBalances());
        var crashed = await new BillingService(_fixture.CreateContext(), _clock).RecordOpeningBalanceAsync(ann, 125.50m, ImportBalanceCommit.EntryKey(batch, 2), batch, _actor, default);   // made, but the row never saved
        await Commit().CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 2, _actor, default, true);

        Assert.False((await Intake().GetAsync(batch, default))!.CanRetry);                                  // jobs are waiting: not stuck
        Assert.Equal("not_stuck", await CodeAsync(() => Retry().RetryAsync(batch, _actor, true, default)));

        await using (var db = _fixture.CreateContext())
            await db.BackgroundJobs.Where(j => j.JobType == ImportCommitJobs.JobType).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BackgroundJobStatus.Failed));      // every job failed for good
        var stuck = (await Intake().GetAsync(batch, default))!;
        Assert.Equal((ImportBatchStatus.Committing, true, 1), (stuck.Status, stuck.CanRetry, stuck.CommitRound));
        Assert.Contains(batch, (await Intake().ListAsync(10, null, default)).Where(b => b.CanRetry).Select(b => b.Id));

        Assert.Equal("billing_permission_required", await CodeAsync(() => Retry().RetryAsync(batch, _actor, false, default)));      // a balance retry needs ManageBilling
        var retried = await Retry().RetryAsync(batch, _actor, true, default);
        Assert.Equal((2, false), (retried.CommitRound, retried.CanRetry));
        await DrainAsync();

        var done = await BatchAsync(batch);
        Assert.Equal((ImportBatchStatus.Committed, ImportCommitOutcome.Completed, 2, 165.50m), (done.Status, done.CommitOutcome, done.AcceptedRows, done.AcceptedAmount));
        Assert.Equal(crashed, Assert.Single(await EntriesAsync(ann)).Id);                                  // the entry made before the stop is the row's result, not repeated
        Assert.Single(await EntriesAsync(bo));
        Assert.Equal(1, await CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == "ImportCommitRetried")));
        Assert.Equal("not_stuck", await CodeAsync(() => Retry().RetryAsync(batch, _actor, true, default)));      // a finished batch is not stuck
        Assert.Equal("batch_not_found", await CodeAsync(() => Retry().RetryAsync(Guid.NewGuid(), _actor, true, default)));
    }

    [Fact]
    public async Task A_committed_balance_batch_and_a_committed_patient_batch_reconcile()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");
        var balances = await DryRunAsync(ImportSourceKind.OpeningBalances, TwoBalances());
        var patients = await DryRunAsync(ImportSourceKind.Patients, PatientFile(
            "Cy,Lee,1980-05-05,555-500-5000,,4 Pine St,Austin,TX,78701", ",Park,1971-02-02,555-222-2222,,4 Pine St,Austin,TX,78701", "Di,Wu,1990-09-09,555-700-7000,,4 Pine St,Austin,TX,78701"));
        await Commit().CommitAsync(balances, ImportBatchPolicy.CommitValidRows, 2, _actor, default, true);
        await Commit().CommitAsync(patients, ImportBatchPolicy.CommitValidRows, 2, _actor, default, true);
        await DrainAsync();

        var money = await new ImportReportService(_fixture.CreateContext()).ReconcileAsync(balances, default);
        var people = await new ImportReportService(_fixture.CreateContext()).ReconcileAsync(patients, default);

        Assert.True(money.Reconciled, string.Join("; ", money.Problems.Select(p => p.Code)));
        Assert.Equal((2, 2, 2, 165.50m, 165.50m, 165.50m), (money.Accepted, money.RecordsFound, money.RecordsMadeByBatch, money.ValidAmount, money.AcceptedAmount, money.LedgerTotal));
        Assert.True(people.Reconciled, string.Join("; ", people.Problems.Select(p => p.Code)));
        Assert.Equal((3, 2, 1, 2, 2), (people.RowCount, people.Accepted, people.Rejected, people.RecordsFound, people.RecordsMadeByBatch));
        Assert.Contains(new ImportReasonCount(ImportRowStatus.Rejected, ImportRowReason.InvalidFields, 1), people.Reasons);
        Assert.True(people.AuditEvents > 0);
        Assert.Null(people.LedgerTotal);
    }

    [Fact]
    public async Task A_row_that_points_at_no_record_is_reported_as_a_named_problem()
    {
        await RegisterAsync("Ann", "Rivera", "1950-01-10", "555-100-1000");
        await RegisterAsync("Bo", "Moss", "1962-03-04", "555-300-3000");
        var batch = await DryRunAsync(ImportSourceKind.OpeningBalances, TwoBalances());
        await using (var db = _fixture.CreateContext())
            await db.ImportBatchRows.Where(r => r.BatchId == batch && r.RowNumber == 2).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ImportRowStatus.Accepted).SetProperty(r => r.ResultRecordId, Guid.NewGuid()));

        var report = await new ImportReportService(_fixture.CreateContext()).ReconcileAsync(batch, default);

        Assert.False(report.Reconciled);
        Assert.Contains("accepted_result_missing", report.Problems.Select(p => p.Code));
        Assert.Contains("unexplained_records", report.Problems.Select(p => p.Code));
    }

    [Fact]
    public async Task The_error_report_lists_every_rejected_skipped_and_unresolved_row_with_fixed_messages_and_no_values()
    {
        await RegisterAsync("Cy", "Lee", "1980-05-05", "555-500-5000");
        var batch = await DryRunAsync(ImportSourceKind.Patients, PatientFile(
            "Ann,Rivera,1950-01-10,555-100-1000,,4 Pine St,Austin,TX,78701", ",Moss,1962-03-04,555-300-3000,,4 Pine St,Austin,TX,78701", "Cy,Lee,1980-05-05,555-500-5000,,4 Pine St,Austin,TX,78701"));

        var bytes = await new ImportReportService(_fixture.CreateContext()).ErrorReportAsync(batch, default);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);                                                        // opens correctly in a spreadsheet
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("row,status,reason,field,message,duplicate_of_row", lines[0]);
        Assert.Contains("3,Rejected,invalid_fields,firstName,First name is required.,", lines);
        Assert.Contains(lines, l => l.StartsWith("4,Skipped,already_registered,,") || l.StartsWith("4,Unresolved,possible_duplicate,,"));
        Assert.DoesNotContain(lines, l => l.StartsWith("2,"));                                                  // a valid row is not an error
        var all = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("Rivera", all);
        Assert.DoesNotContain("Moss", all);
        Assert.DoesNotContain("Lee", all);
    }

    [Fact]
    public void A_cell_a_spreadsheet_could_read_as_a_formula_is_neutralised_and_special_characters_are_quoted()
    {
        Assert.Equal("'=1+1", ImportReportService.Cell("=1+1"));
        Assert.Equal("'@SUM(A1)", ImportReportService.Cell("@SUM(A1)"));
        Assert.Equal("\"a,b\"", ImportReportService.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", ImportReportService.Cell("say \"hi\""));
        Assert.Equal("plain", ImportReportService.Cell("plain"));
    }

    [Fact]
    public async Task The_reports_need_a_dry_run_and_an_existing_batch()
    {
        var uploaded = (await Intake().UploadAsync(ImportSourceKind.OpeningBalances, "Old system", TwoBalances(), _actor, default)).Batch.Id;
        var reports = new ImportReportService(_fixture.CreateContext());

        Assert.Equal("dry_run_required", await CodeAsync(() => reports.ReconcileAsync(uploaded, default)));
        Assert.Equal("dry_run_required", await CodeAsync(() => reports.ErrorReportAsync(uploaded, default)));
        Assert.Equal("batch_not_found", await CodeAsync(() => reports.ReconcileAsync(Guid.NewGuid(), default)));
        Assert.Equal("batch_not_found", await CodeAsync(() => reports.ErrorReportAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task The_sweep_removes_an_old_unreferenced_file_keeps_the_rest_reports_a_missing_file_and_is_safe_to_repeat()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(now);
        var kept = (await Intake().UploadAsync(ImportSourceKind.OpeningBalances, "Old system", TwoBalances(), _actor, default)).Batch.Id;
        var lost = (await Intake().UploadAsync(ImportSourceKind.OpeningBalances, "Other", Balances("Cy,Lee,1980-05-05,10.00,,"), _actor, default)).Batch.Id;
        var keptBlob = (await BatchAsync(kept)).FileBlobId;
        var lostBlob = (await BatchAsync(lost)).FileBlobId;
        Guid oldOrphan, youngOrphan;
        await using (var stream = new MemoryStream("orphan-1"u8.ToArray())) oldOrphan = (await _files.Blobs.StoreAsync(stream)).Id;
        await using (var stream = new MemoryStream("orphan-2"u8.ToArray())) youngOrphan = (await _files.Blobs.StoreAsync(stream)).Id;
        string PathOf(Guid id) => Path.Combine(_folder, id.ToString("N"));
        File.SetLastWriteTimeUtc(PathOf(keptBlob), now.UtcDateTime.AddHours(-48));                             // old, but a batch refers to it
        File.SetLastWriteTimeUtc(PathOf(oldOrphan), now.UtcDateTime.AddHours(-48));
        File.SetLastWriteTimeUtc(PathOf(youngOrphan), now.UtcDateTime.AddHours(-1));
        File.Delete(PathOf(lostBlob));                                                                         // an open batch whose file is gone

        ImportFileSweepService Sweep() { var db = _fixture.CreateContext(); return new ImportFileSweepService(db, _files, Opts, clock, NullLogger<ImportFileSweepService>.Instance); }
        var first = await Sweep().SweepAsync(_actor, default);
        var second = await Sweep().SweepAsync(_actor, default);

        Assert.Equal(new ImportFileSweepResult(3, 1, 2, 0, 1), first);
        Assert.Equal(new ImportFileSweepResult(2, 0, 2, 0, 1), second);
        Assert.False(File.Exists(PathOf(oldOrphan)));
        Assert.True(File.Exists(PathOf(keptBlob)));
        Assert.True(File.Exists(PathOf(youngOrphan)));
        await using var audit = _fixture.CreateContext();
        var details = await audit.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "ImportFilesSwept").Select(a => a.Details).ToListAsync();
        Assert.Equal(2, details.Count);
        Assert.Contains("1 removed", details[0] + details[1]);
    }
}

/// <summary>ALV-018-C02 slice 6, an appointment batch reconciles: the appointments carrying the batch's keys are the Accepted rows' records.</summary>
public class ImportAppointmentReconciliationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"alveara-import-test-{Guid.NewGuid():N}");
    private readonly IPracticeClock _clock = SchedulingTestSupport.Clock;
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

    [Fact]
    public async Task A_committed_appointment_batch_reconciles()
    {
        var options = Options.Create(new ImportOptions());
        var registration = new PatientRegistrationService(_fixture.CreateContext(), _clock);
        await registration.RegisterAsync(new RegisterPatientRequest("Ann", null, "Rivera", "1950-01-10", "Female", "555-100-1000", null, "1 Main St", null, "Austin", "TX", "78701"), $"test-{Guid.NewGuid():N}", _actor, default);
        var csv = Encoding.UTF8.GetBytes("Patient First,Patient Last,DOB,Provider,Operatory,Type,Start,Duration,Notes\r\nAnn,Rivera,1950-01-10,Dr. Rivera,Op 1,Exam,2030-01-14 09:00,,\r\n");
        var db = _fixture.CreateContext();
        var intake = new ImportIntakeService(db, _files, options, _clock, NullLogger<ImportIntakeService>.Instance);
        var mapping = new ImportMappingService(db, _clock);
        var batch = (await intake.UploadAsync(ImportSourceKind.Appointments, "Old system", csv, _actor, default)).Batch.Id;
        var suggestion = await mapping.GetSuggestionAsync(batch, default);
        await mapping.SaveAsync(batch, suggestion.Suggested.ToDictionary(kv => kv.Key, kv => (string?)kv.Value), _actor, default);
        await new ImportDryRunService(db, _files, options, _clock, mapping, intake).RunAsync(batch, _actor, default);
        var commitDb = _fixture.CreateContext();
        await new ImportCommitService(commitDb, new BackgroundJobQueue(commitDb), _files, options, _clock, intake).CommitAsync(batch, ImportBatchPolicy.CommitValidRows, 1, _actor, default);
        for (var i = 0; i < 40; i++)
        {
            var runner = new BackgroundJobRunner(_fixture.CreateContext(), [new ImportCommitJobHandler(_files, options, _clock, new TestWorkScopeFactory(_fixture, _s))], NullLogger<BackgroundJobRunner>.Instance);
            if (await runner.ProcessOnceAsync([ImportCommitJobs.JobType], default) == 0) break;
        }

        var report = await new ImportReportService(_fixture.CreateContext()).ReconcileAsync(batch, default);

        Assert.True(report.Reconciled, string.Join("; ", report.Problems.Select(p => p.Code)));
        Assert.Equal((ImportBatchStatus.Committed, 1, 1, 1), (report.Status, report.Accepted, report.RecordsFound, report.RecordsMadeByBatch));
    }
}

/// <summary>ALV-018-C02 slice 6 over the real API: the four new calls are refused to an anonymous caller (401) and to every role without BulkImportData (403); for the administrator the error report is a no-store CSV
/// download, the reconciliation is a JSON report, retry says not_stuck for a batch that is not committing, and the sweep answers with its counts.</summary>
public class ImportSlice6ApiTests : IAsyncLifetime
{
    private const string BatchesUrl = "/api/import-batches";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;
    private readonly Dictionary<string, Session> _others = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture);
        await new SchedulingTestSupport(_fixture).ArrangeAsync();
        _admin = await _api.SessionAsync("Admin");
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager" }) _others[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static async Task<(HttpStatusCode Status, HttpResponseMessage Raw, string Text)> SendAsync(Session session, HttpMethod method, string url, object? body = null, byte[]? raw = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", session.Csrf);
        if (raw is not null) { request.Content = new ByteArrayContent(raw); request.Content.Headers.ContentType = new("text/csv"); }
        else if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        var response = await session.Client.SendAsync(request);
        return (response.StatusCode, response, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_four_new_calls_are_refused_to_anonymous_callers_and_to_roles_without_the_permission()
    {
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Post, $"{BatchesUrl}/{id}/retry"), (HttpMethod.Get, $"{BatchesUrl}/{id}/reconciliation"), (HttpMethod.Get, $"{BatchesUrl}/{id}/error-report"), (HttpMethod.Post, $"{BatchesUrl}/orphan-sweep"),
        };

        using var anonymous = _api.Factory.CreateClient();
        foreach (var (method, url) in calls)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
        foreach (var (role, session) in _others)
            foreach (var (method, url) in calls)
                Assert.True(HttpStatusCode.Forbidden == (await SendAsync(session, method, url)).Status, $"{role} must not {method} {url}");
    }

    [Fact]
    public async Task The_administrator_downloads_the_error_report_reads_the_reconciliation_and_runs_the_sweep()
    {
        var csv = Encoding.UTF8.GetBytes("First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip\r\nAnn,Rivera,1950-01-10,555-100-1000,,4 Pine St,Austin,TX,78701\r\n,Moss,1962-03-04,555-300-3000,,4 Pine St,Austin,TX,78701\r\n");
        var upload = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}?sourceKind=Patients&label=Old%20system", raw: csv);
        var id = JsonDocument.Parse(upload.Text).RootElement.GetProperty("batch").GetProperty("id").GetGuid();
        var suggestion = JsonDocument.Parse((await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/mapping-suggestion")).Text).RootElement;
        await SendAsync(_admin, HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { mapping = suggestion.GetProperty("suggested").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()) });
        await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run");

        var report = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/error-report");
        var recon = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/reconciliation");
        var retry = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/retry");
        var sweep = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/orphan-sweep");

        Assert.Equal(HttpStatusCode.OK, report.Status);
        Assert.Equal("text/csv", report.Raw.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", report.Raw.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("no-store", report.Raw.Headers.CacheControl?.ToString());
        Assert.Contains("First name is required.", report.Text);
        Assert.DoesNotContain("Rivera", report.Text);
        var json = JsonDocument.Parse(recon.Text).RootElement;
        Assert.Equal((HttpStatusCode.OK, true, 2, 1), (recon.Status, json.GetProperty("reconciled").GetBoolean(), json.GetProperty("rowCount").GetInt32(), json.GetProperty("notCommitted").GetInt32()));
        Assert.Equal((HttpStatusCode.Conflict, "not_stuck"), (retry.Status, JsonDocument.Parse(retry.Text).RootElement.GetProperty("error").GetString()));
        Assert.Equal(HttpStatusCode.OK, sweep.Status);
        Assert.True(JsonDocument.Parse(sweep.Text).RootElement.TryGetProperty("removed", out _));
    }
}
