using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the package of every patient against a real SQL Server: every patient who has not been merged away is in it and nobody else, each with an own readable page and an own folder; the rows of each table are
/// the whole table's; documents are grouped by patient; the history row and the audit entry carry counts only; too many patients, or a package over the size limit, are refused with nothing left behind and the lock
/// released; a second all-patients export is refused while the first runs; a permission withdrawn part way, or just before delivery, stops the export and delivers nothing; a cancelled export leaves nothing behind; the
/// real permission recheck reads the account and the policy afresh; and with snapshot isolation on the export reads one moment while without it it says it read table by table.
/// </summary>
public class AllPatientsPackageTests : MergeChartTestBase
{
    private string _temp = null!;
    private LocalDiskBlobStorage _blobs = null!;
    private static readonly IExportAuthorization AllowAll = new ScriptedAuthorization((_, _) => Task.CompletedTask);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _temp = Path.Combine(Path.GetTempPath(), $"alveara-export-all-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temp);
        _blobs = new LocalDiskBlobStorage(Path.Combine(_temp, "blobs"));
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    /// <summary>Stands in for the permission recheck: runs <c>onCall</c> with the number of the call (1 is the check before anything is read).</summary>
    private sealed class ScriptedAuthorization(Func<int, CancellationToken, Task> onCall) : IExportAuthorization
    {
        private int _calls;
        public int Calls => _calls;
        public Task EnsureStillAllowedAsync(Guid actor, Permission permission, CancellationToken ct) => onCall(Interlocked.Increment(ref _calls), ct);
    }

    private static PatientException Revoked() => new("permission_revoked", "The export was stopped because the permission to run it is no longer held. Nothing was delivered.", 403);

    // Manifest only by default: the chart's one document has no file on disk. The documents are tested on their own below.
    private Task<DataPackageFile> ExportAllAsync(IExportAuthorization? authorization = null, PackageChoices? choices = null, int? maxPatients = null, long? maxBytes = null, CancellationToken ct = default)
    {
        var services = new ServiceCollection().AddScoped(_ => Fixture.CreateContext()).BuildServiceProvider();
        var db = Fixture.CreateContext();
        var options = Options.Create(new DataExportOptions { TempDirectory = _temp, MaxPatients = maxPatients ?? 5000, MaxPackageBytes = maxBytes ?? new DataExportOptions().MaxPackageBytes });
        var documents = new PackageDocumentWriter(db, _blobs, options, NullLogger<PackageDocumentWriter>.Instance);
        var builder = new PatientDataPackageBuilder(db, Clock, options, new ReadableRecordLoader(db, Clock), documents, NullLogger<PatientDataPackageBuilder>.Instance);
        var factory = services.GetRequiredService<IServiceScopeFactory>();
        return new PatientDataPackageService(db, builder, authorization ?? AllowAll, Clock, factory, NullLogger<PatientDataPackageService>.Instance)
            .ExportAllAsync(S.Actor, ct, choices ?? new PackageChoices(IncludeDocumentFiles: false));
    }

    private static Dictionary<string, byte[]> Entries(Stream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }

    private static JsonElement Json(byte[] bytes) => JsonDocument.Parse(bytes).RootElement;

    private static IEnumerable<JsonElement> Rows(Dictionary<string, byte[]> entries, string domain, string table) =>
        Json(entries[$"data/{domain}.json"]).GetProperty("tables").GetProperty(table).EnumerateArray();

    private async Task<(Dictionary<string, byte[]> Entries, JsonElement Manifest, Guid ExportId)> ExportedAsync(PackageChoices? choices = null)
    {
        await using var file = await ExportAllAsync(choices: choices);
        var entries = Entries(file.Content);
        return (entries, Json(entries["manifest.json"]), file.ExportId);
    }

    private async Task MergeBoIntoAnnAsync()
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Bo, Ann, default));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Bo, Ann, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion, preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));
    }

    private async Task AssertNothingDeliveredAsync(string failedResultExpected = DataExportResult.Failed)
    {
        Assert.Equal(failedResultExpected, (await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync())).Result);
        Assert.Equal(0, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.AllPatientDataPackageExported)));
        Assert.Equal(1, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.DataExportFailed)));
        Assert.Empty(Directory.GetFiles(_temp));
    }

    [Fact]
    public async Task Every_patient_not_merged_away_is_in_the_package_with_a_page_and_a_folder_of_their_own_and_nobody_else()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var patients = await WithDb(db => db.Patients.AsNoTracking().Where(p => p.MergedAtUtc == null).ToListAsync());

        var (entries, manifest, _) = await ExportedAsync();

        Assert.True(patients.Count >= 3, "Ann, Bo and the dependant");
        Assert.Equal(("AllPatients", patients.Count), (manifest.GetProperty("scope").GetProperty("kind").GetString(), manifest.GetProperty("scope").GetProperty("patientCount").GetInt32()));
        Assert.Equal(JsonValueKind.Null, manifest.GetProperty("scope").GetProperty("subjectRecordId").ValueKind);
        Assert.Equal(patients.Select(p => p.Id).Order(), Rows(entries, "registration", "Patients").Select(r => r.GetProperty("Id").GetGuid()).Order());
        Assert.Equal(patients.Select(p => p.Id).Order(), manifest.GetProperty("patientFolders").EnumerateArray().Select(f => f.GetProperty("patientId").GetGuid()).Order());
        Assert.Equal(JsonValueKind.Null, manifest.GetProperty("readableRecord").ValueKind);                              // one per patient, listed below
        var pages = manifest.GetProperty("readableRecords").EnumerateArray().ToList();
        Assert.Equal(patients.Count, pages.Count);
        Assert.Equal(patients.Count, entries.Keys.Count(k => k.StartsWith("readable-records/")));
        foreach (var patient in patients)
        {
            var page = System.Text.Encoding.UTF8.GetString(entries[$"readable-records/{patient.Id:D}.html"]);
            Assert.Contains($"<h1>{patient.FirstName} {patient.LastName}</h1>", page);
            Assert.Contains(patient.Id, pages.Select(p => p.GetProperty("patientId").GetGuid()));
        }
        var allAppointments = await WithDb(db => db.Appointments.CountAsync());
        Assert.Equal(allAppointments, Rows(entries, "appointments", "Appointments").Count());                              // the whole table, every patient's rows
        Assert.True(Rows(entries, "appointments", "Appointments").Select(r => r.GetProperty("PatientId").GetGuid()).Distinct().Count() >= 2, "more than one patient's rows");
        Assert.Equal("Complete", manifest.GetProperty("result").GetString());
        Assert.Contains("every patient", System.Text.Encoding.UTF8.GetString(entries["README.txt"]));
    }

    [Fact]
    public async Task A_patient_merged_away_is_not_in_the_package()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        await MergeBoIntoAnnAsync();
        var expected = await WithDb(db => db.Patients.CountAsync(p => p.MergedAtUtc == null));

        var (entries, manifest, _) = await ExportedAsync();

        Assert.Equal(expected, manifest.GetProperty("scope").GetProperty("patientCount").GetInt32());
        Assert.DoesNotContain(Rows(entries, "registration", "Patients"), r => r.GetProperty("Id").GetGuid() == Bo);
        Assert.False(entries.ContainsKey($"readable-records/{Bo:D}.html"));
    }

    [Fact]
    public async Task The_history_row_and_the_audit_entry_count_the_patients_and_hold_no_patient_detail()
    {
        await GiveRetiringChartAsync();
        var ann = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann));
        var count = await WithDb(db => db.Patients.CountAsync(p => p.MergedAtUtc == null));

        await using var file = await ExportAllAsync();

        var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync(h => h.ExportId == file.ExportId));
        Assert.Equal((DataExportKind.Package, DataExportScope.AllPatients, (Guid?)null, S.Actor, DataExportResult.Complete), (row.Kind, row.Scope, row.SubjectRecordId, row.RequestedByUserId, row.Result));
        Assert.Equal((count, file.SizeBytes, file.Sha256), (row.PatientCount, row.SizeBytes, row.Sha256));
        var entry = await WithDb(db => db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.AllPatientDataPackageExported));
        Assert.Equal((S.Actor, file.ExportId), (entry.PerformedByUserAccountId, entry.CorrelationId));
        Assert.Contains($"{count} patients", entry.Details);
        Assert.DoesNotContain(ann.FirstName, entry.Details);
        Assert.StartsWith("alveara-all-patients-package-", file.FileName);
    }

    [Fact]
    public async Task Folders_follow_the_naming_choice_for_every_patient_and_the_page_paths_follow_the_folders()
    {
        await GiveRetiringChartAsync();

        var (entries, manifest, _) = await ExportedAsync(new PackageChoices(IncludeDocumentFiles: false, FolderNaming: FolderNaming.NameAndId));

        var folders = manifest.GetProperty("patientFolders").EnumerateArray().ToList();
        Assert.All(folders, f => Assert.EndsWith($"_{f.GetProperty("patientId").GetGuid():D}", f.GetProperty("folder").GetString()));
        Assert.Equal(folders.Count, folders.Select(f => f.GetProperty("folder").GetString()).Distinct().Count());
        Assert.All(folders, f => Assert.True(entries.ContainsKey($"readable-records/{f.GetProperty("folder").GetString()}.html")));
    }

    [Fact]
    public async Task Documents_are_grouped_by_patient_and_each_file_comes_back_byte_for_byte()
    {
        var annFile = RandomNumberGenerator.GetBytes(700);
        var boFile = RandomNumberGenerator.GetBytes(300);
        var annDoc = await AddDocumentAsync(Ann, "ann.png", annFile);
        var boDoc = await AddDocumentAsync(Bo, "bo.png", boFile);

        var (entries, manifest, _) = await ExportedAsync(new PackageChoices(IncludeDocumentFiles: true));

        Assert.Equal(annFile, entries[$"documents/{Ann:D}/{annDoc:D}/ann.png"]);
        Assert.Equal(boFile, entries[$"documents/{Bo:D}/{boDoc:D}/bo.png"]);
        Assert.Equal("Complete", manifest.GetProperty("result").GetString());
        Assert.All(manifest.GetProperty("documents").EnumerateArray(), d => Assert.Equal("Included", d.GetProperty("status").GetString()));
        Assert.Equal(2, manifest.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task Over_the_patient_limit_is_refused_nothing_is_left_behind_and_the_next_export_is_not_blocked()
    {
        var refused = await Assert.ThrowsAsync<PatientException>(() => ExportAllAsync(maxPatients: 1));

        Assert.Equal(("too_many_patients", 413), (refused.Code, refused.StatusCode));
        await AssertNothingDeliveredAsync();
        await using var next = await ExportAllAsync();                                                                       // the lock and the patient list were released
        Assert.Equal("Complete", next.Result);
    }

    [Fact]
    public async Task A_package_over_the_size_limit_is_refused_not_cut_short_and_nothing_is_left_behind()
    {
        await GiveRetiringChartAsync();

        var refused = await Assert.ThrowsAsync<PatientException>(() => ExportAllAsync(maxBytes: 2000));

        Assert.Equal(("package_too_large", 413), (refused.Code, refused.StatusCode));
        await AssertNothingDeliveredAsync();
    }

    [Fact]
    public async Task A_second_all_patients_export_is_refused_while_the_first_is_running_and_the_first_still_completes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new ScriptedAuthorization(async (call, _) => { if (call == 1) { entered.SetResult(); await release.Task; } });
        var running = ExportAllAsync(first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(60));                                                              // the first holds the lock now

        var refused = await Assert.ThrowsAsync<PatientException>(() => ExportAllAsync());
        release.SetResult();
        await using var finished = await running;

        Assert.Equal(("export_in_progress", 409), (refused.Code, refused.StatusCode));
        Assert.Equal("Complete", finished.Result);
        var results = await WithDb(db => db.DataExportHistory.AsNoTracking().OrderBy(h => h.RequestedAtUtc).Select(h => h.Result).ToListAsync());
        Assert.Equal([DataExportResult.Complete, DataExportResult.Failed], results.Order().ToList());
    }

    [Fact]
    public async Task A_permission_withdrawn_part_way_stops_the_export_and_delivers_nothing()
    {
        await GiveRetiringChartAsync();
        var authorization = new ScriptedAuthorization((call, _) => call == 3 ? throw Revoked() : Task.CompletedTask);

        var stopped = await Assert.ThrowsAsync<PatientException>(() => ExportAllAsync(authorization));

        Assert.Equal(("permission_revoked", 403), (stopped.Code, stopped.StatusCode));
        Assert.Equal(3, authorization.Calls);                                                                                // it stopped there, it did not read on
        await AssertNothingDeliveredAsync();
    }

    [Fact]
    public async Task A_permission_withdrawn_just_before_delivery_stops_the_export_and_delivers_nothing()
    {
        await GiveRetiringChartAsync();
        var counting = new ScriptedAuthorization((_, _) => Task.CompletedTask);
        await using (await ExportAllAsync(counting)) { }
        var lastCall = counting.Calls;                                                                                       // the check before delivery is the last one
        var authorization = new ScriptedAuthorization((call, _) => call == lastCall ? throw Revoked() : Task.CompletedTask);

        var stopped = await Assert.ThrowsAsync<PatientException>(() => ExportAllAsync(authorization));

        Assert.Equal("permission_revoked", stopped.Code);
        Assert.Equal(lastCall, authorization.Calls);
        Assert.Equal(1, await WithDb(db => db.DataExportHistory.CountAsync(h => h.Result == DataExportResult.Failed)));
        Assert.Equal(1, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.DataExportFailed)));
        Assert.Empty(Directory.GetFiles(_temp));
    }

    [Fact]
    public async Task A_cancelled_export_leaves_nothing_behind_and_does_not_block_the_next()
    {
        await GiveRetiringChartAsync();
        using var cancel = new CancellationTokenSource();
        var authorization = new ScriptedAuthorization((call, _) => { if (call == 3) cancel.Cancel(); return Task.CompletedTask; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExportAllAsync(authorization, ct: cancel.Token));

        await AssertNothingDeliveredAsync();
        await using var next = await ExportAllAsync();
        Assert.Equal("Complete", next.Result);
    }

    [Fact]
    public async Task The_real_recheck_reads_the_account_and_the_policy_afresh()
    {
        var admin = await AccountAsync(Role.Admin); var disabledAdmin = await AccountAsync(Role.Admin, disabled: true); var dentist = await AccountAsync(Role.Dentist);
        var services = new ServiceCollection().AddScoped(_ => Fixture.CreateContext()).AddSingleton(PolicyTestSupport.Defaults()).BuildServiceProvider();
        var recheck = new ExportAuthorization(services.GetRequiredService<IServiceScopeFactory>());

        await recheck.EnsureStillAllowedAsync(admin, Permission.ExportAllPatientData, default);                                // an administrator always holds it
        var disabled = await Assert.ThrowsAsync<PatientException>(() => recheck.EnsureStillAllowedAsync(disabledAdmin, Permission.ExportAllPatientData, default));
        var withoutIt = await Assert.ThrowsAsync<PatientException>(() => recheck.EnsureStillAllowedAsync(dentist, Permission.ExportAllPatientData, default));
        var unknown = await Assert.ThrowsAsync<PatientException>(() => recheck.EnsureStillAllowedAsync(Guid.NewGuid(), Permission.ExportAllPatientData, default));

        Assert.All(new[] { disabled, withoutIt, unknown }, e => Assert.Equal(("permission_revoked", 403), (e.Code, e.StatusCode)));
        await AccountAsync(Role.Admin);                                                                                        // the database refuses to disable the last enabled administrator
        await WithDb(db => db.UserAccounts.Where(u => u.Id == admin).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsDisabled, true)));       // disabled while an export runs
        await Assert.ThrowsAsync<PatientException>(() => recheck.EnsureStillAllowedAsync(admin, Permission.ExportAllPatientData, default));
    }

    [Fact]
    public async Task Without_snapshot_isolation_the_export_reads_table_by_table_and_says_so_and_with_it_reads_one_moment()
    {
        await GiveRetiringChartAsync();
        var (without, manifestWithout, _) = await ExportedAsync();
        Assert.Equal("TableByTable", manifestWithout.GetProperty("readConsistency").GetString());
        Assert.Contains("one after another", System.Text.Encoding.UTF8.GetString(without["README.txt"]));

        await WithDb(db => db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON"));
        try
        {
            // a change made by someone else while the export runs, to a table the export reads LATER
            var change = new ScriptedAuthorization(async (call, _) => { if (call == 2) await WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE [PatientRecallPlans] SET [IntervalDays] = 365 WHERE [PatientId] = {0}", Ann)); });
            await using var file = await ExportAllAsync(change);
            var entries = Entries(file.Content);

            Assert.Equal("Snapshot", Json(entries["manifest.json"]).GetProperty("readConsistency").GetString());
            Assert.Contains("one database snapshot", System.Text.Encoding.UTF8.GetString(entries["README.txt"]));
            Assert.Equal(120, Rows(entries, "recall", "PatientRecallPlans").Single(r => r.GetProperty("PatientId").GetGuid() == Ann).GetProperty("IntervalDays").GetInt32());    // the moment the export began
            Assert.Equal(365, await WithDb(db => db.PatientRecallPlans.AsNoTracking().Where(p => p.PatientId == Ann).Select(p => p.IntervalDays).SingleAsync()));                   // and the change did happen
        }
        finally
        {
            await WithDb(db => db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION OFF"));
        }
    }

    private async Task<Guid> AccountAsync(Role role, bool disabled = false)
    {
        var id = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = id, Username = $"u-{Guid.NewGuid():N}", Role = role, IsDisabled = disabled, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddDocumentAsync(Guid patient, string fileName, byte[] content)
    {
        var blob = await _blobs.StoreAsync(new MemoryStream(content));
        var id = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        db.Add(new PatientDocument
        {
            Id = id, PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), OriginalFileName = fileName, DetectedType = "png", ContentType = "image/png",
            SizeBytes = blob.SizeBytes, Sha256 = blob.Sha256Hash, BlobId = blob.Id, ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('b', 64),
        });
        await db.SaveChangesAsync();
        return id;
    }
}
