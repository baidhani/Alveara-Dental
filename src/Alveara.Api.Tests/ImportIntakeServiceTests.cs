using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 1, taking an import file in, against a real SQL Server and a real folder: a sound file becomes an Uploaded batch with its hash, size, row and column counts, the column names and one audit entry,
/// and is stored byte for byte; an unknown kind or a bad label is refused with nothing stored; a malformed file is refused with its problems, logged by codes only and stored nowhere; sending the same file again
/// (a retry, or two people) returns the same batch and stores nothing new, also when two requests race; the same file of another kind is its own batch and a finished batch does not block a new one; a save that
/// fails deletes the stored file again; the label limit is exact; the list is newest first with a cursor, names the maker or says unknown; and no value from a file ever reaches the audit log.
/// </summary>
public class ImportIntakeServiceTests : IAsyncLifetime
{
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
        db.StaffProfiles.Add(new StaffProfile { Id = Guid.NewGuid(), UserAccountId = _actor, DisplayName = "Dr. Import", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private ImportIntakeService Service() => new(_fixture.CreateContext(), _files, Options.Create(new ImportOptions()), _clock, NullLogger<ImportIntakeService>.Instance);

    private static byte[] Csv(params string[] lines) => Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");

    private static readonly byte[] Sound = Csv("First,Last,DateOfBirth", "Ann,Rivera,1950-01-10", "Bo,Moss,1962-03-04");

    private int StoredFiles() => Directory.Exists(_folder) ? Directory.GetFiles(_folder).Length : 0;

    private async Task<List<ImportBatch>> BatchesAsync() { await using var db = _fixture.CreateContext(); return await db.ImportBatches.AsNoTracking().OrderBy(b => b.CreatedAtUtc).ToListAsync(); }

    private async Task<List<string>> AuditDetailsAsync(string eventType)
    {
        await using var db = _fixture.CreateContext();
        return await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == eventType).Select(a => a.Details).ToListAsync();
    }

    [Fact]
    public async Task A_sound_file_becomes_an_uploaded_batch_with_its_hash_counts_and_columns_and_is_stored_byte_for_byte()
    {
        var result = await Service().UploadAsync(ImportSourceKind.Patients, "  Old system export, October ", Sound, _actor, default);

        Assert.True(result.Created);
        var view = result.Batch;
        Assert.Equal((ImportSourceKind.Patients, "Old system export, October", ImportBatchStatus.Uploaded, ImportBatchPolicy.AllOrNothing), (view.SourceKind, view.SourceLabel, view.Status, view.Policy));
        Assert.Equal((2, Sound.LongLength, Convert.ToHexStringLower(SHA256.HashData(Sound)), "Dr. Import"), (view.RowCount, view.FileSizeBytes, view.FileSha256, view.CreatedBy));
        Assert.Equal(["First", "Last", "DateOfBirth"], view.ColumnNames);
        Assert.Equal((0, 0, 0, 0, 0), (view.AttemptedRows, view.AcceptedRows, view.RejectedRows, view.SkippedRows, view.UnresolvedRows));

        var stored = Assert.Single(await BatchesAsync());
        await using (var read = await _files.Blobs.OpenReadAsync(stored.FileBlobId))
        {
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            Assert.Equal(Sound, copy.ToArray());
        }

        var audit = Assert.Single(await AuditDetailsAsync(AuditEventTypes.ImportBatchCreated));
        Assert.Contains("Patients", audit);
        Assert.Contains("2 row(s)", audit);
        Assert.Contains(view.FileSha256, audit);
    }

    [Fact]
    public async Task An_unknown_kind_or_a_missing_or_bad_label_is_refused_and_nothing_is_stored()
    {
        foreach (var kind in new string?[] { null, "", "Everything", "patients " + "x" })
            Assert.Equal("invalid_source_kind", (await Assert.ThrowsAsync<ImportException>(() => Service().UploadAsync(kind, "Source", Sound, _actor, default))).Code);
        foreach (var label in new string?[] { null, "", "   ", new string('x', 101), "has\u0007bell" })
            Assert.Equal("invalid_label", (await Assert.ThrowsAsync<ImportException>(() => Service().UploadAsync(ImportSourceKind.Patients, label, Sound, _actor, default))).Code);

        Assert.Empty(await BatchesAsync());
        Assert.Equal(0, StoredFiles());
    }

    [Fact]
    public async Task The_label_limit_is_exact_and_the_kind_is_matched_after_trimming()
    {
        var result = await Service().UploadAsync(" Patients ", new string('x', 100), Sound, _actor, default);

        Assert.Equal(100, result.Batch.SourceLabel.Length);
        Assert.Equal(ImportSourceKind.Patients, result.Batch.SourceKind);
    }

    [Fact]
    public async Task A_malformed_file_is_refused_with_its_problems_logged_by_codes_only_and_stored_nowhere()
    {
        var bad = Csv("First,Last", "ZEBRA-VALUE", "Bo,Moss,extra");

        var refused = await Assert.ThrowsAsync<ImportFileRejectedException>(() => Service().UploadAsync(ImportSourceKind.Patients, "Source", bad, _actor, default));

        Assert.Equal(["row_width", "row_width"], refused.Problems.Select(p => p.Code));
        Assert.Equal(2, refused.TotalProblems);
        Assert.Empty(await BatchesAsync());
        Assert.Equal(0, StoredFiles());
        var audit = Assert.Single(await AuditDetailsAsync(AuditEventTypes.ImportFileRejected));
        Assert.Contains("row_width", audit);
        Assert.DoesNotContain("ZEBRA", audit);
        Assert.Empty(await AuditDetailsAsync(AuditEventTypes.ImportBatchCreated));
    }

    [Fact]
    public async Task Sending_the_same_file_again_returns_the_same_batch_and_stores_and_logs_nothing_new()
    {
        var first = await Service().UploadAsync(ImportSourceKind.Patients, "Source", Sound, _actor, default);
        var again = await Service().UploadAsync(ImportSourceKind.Patients, "A different label", Sound, _actor, default);

        Assert.True(first.Created);
        Assert.False(again.Created);
        Assert.Equal(first.Batch.Id, again.Batch.Id);
        Assert.Equal("Source", again.Batch.SourceLabel);                                            // the existing batch is returned as it is
        Assert.Single(await BatchesAsync());
        Assert.Equal(1, StoredFiles());
        Assert.Single(await AuditDetailsAsync(AuditEventTypes.ImportBatchCreated));
    }

    [Fact]
    public async Task The_same_file_as_another_kind_is_its_own_batch_and_a_finished_batch_does_not_block_a_new_one()
    {
        var patients = await Service().UploadAsync(ImportSourceKind.Patients, "Source", Sound, _actor, default);
        var balances = await Service().UploadAsync(ImportSourceKind.OpeningBalances, "Source", Sound, _actor, default);
        Assert.NotEqual(patients.Batch.Id, balances.Batch.Id);

        await using (var db = _fixture.CreateContext())
        {
            var batch = await db.ImportBatches.SingleAsync(b => b.Id == patients.Batch.Id);
            batch.Status = ImportBatchStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var again = await Service().UploadAsync(ImportSourceKind.Patients, "Source", Sound, _actor, default);

        Assert.True(again.Created);
        Assert.NotEqual(patients.Batch.Id, again.Batch.Id);
        Assert.Equal(3, (await BatchesAsync()).Count);
        Assert.Equal(3, StoredFiles());
    }

    [Fact]
    public async Task Two_requests_with_the_same_file_at_once_make_one_batch_and_leave_one_stored_file()
    {
        var results = await Task.WhenAll(Service().UploadAsync(ImportSourceKind.Patients, "Source", Sound, _actor, default), Service().UploadAsync(ImportSourceKind.Patients, "Source", Sound, _actor, default));

        Assert.Equal(results[0].Batch.Id, results[1].Batch.Id);
        Assert.Equal(1, results.Count(r => r.Created));                                             // exactly one request made the batch
        Assert.Single(await BatchesAsync());
        Assert.Equal(1, StoredFiles());                                                             // the loser's stored file was deleted again
    }

    [Fact]
    public async Task A_save_that_fails_deletes_the_stored_file_and_leaves_no_batch()
    {
        var unknownActor = Guid.NewGuid();                                                          // no such account: the batch's foreign key refuses it

        await Assert.ThrowsAsync<DbUpdateException>(() => Service().UploadAsync(ImportSourceKind.Patients, "Source", Sound, unknownActor, default));

        Assert.Empty(await BatchesAsync());
        Assert.Equal(0, StoredFiles());
        Assert.Empty(await AuditDetailsAsync(AuditEventTypes.ImportBatchCreated));                  // the audit entry was part of the same failed save
    }

    [Fact]
    public async Task The_list_is_newest_first_with_a_cursor_and_names_the_maker_or_says_unknown()
    {
        var other = Guid.NewGuid();
        await using (var db = _fixture.CreateContext())
        {
            db.UserAccounts.Add(new UserAccount { Id = other, Username = $"u-{Guid.NewGuid():N}", Role = Role.Admin, CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();                                                            // an account with no staff profile
        }

        var a = await Service().UploadAsync(ImportSourceKind.Patients, "First", Csv("A", "1"), _actor, default);
        await Task.Delay(20);
        var b = await Service().UploadAsync(ImportSourceKind.Appointments, "Second", Csv("A", "2"), other, default);
        await Task.Delay(20);
        var c = await Service().UploadAsync(ImportSourceKind.OpeningBalances, "Third", Csv("A", "3"), _actor, default);

        var all = await Service().ListAsync(50, null, default);
        var page = await Service().ListAsync(1, c.Batch.CreatedAtUtc, default);

        Assert.Equal([c.Batch.Id, b.Batch.Id, a.Batch.Id], all.Select(v => v.Id));
        Assert.Equal(["Dr. Import", "Unknown staff member", "Dr. Import"], all.Select(v => v.CreatedBy));
        Assert.Equal([b.Batch.Id], page.Select(v => v.Id));
        Assert.Single(await Service().ListAsync(0, null, default));                                  // a limit below one is raised to one
        Assert.Null(await Service().GetAsync(Guid.NewGuid(), default));
        Assert.Equal(a.Batch.Id, (await Service().GetAsync(a.Batch.Id, default))!.Id);
    }

    [Fact]
    public async Task No_value_from_a_file_ever_reaches_the_audit_log()
    {
        await Service().UploadAsync(ImportSourceKind.Patients, "Source", Csv("First,Last", "ZEBRA-FIRST,ZEBRA-LAST"), _actor, default);
        await Assert.ThrowsAsync<ImportFileRejectedException>(() => Service().UploadAsync(ImportSourceKind.Patients, "Source", Csv("First,Last", "ZEBRA-ONLY"), _actor, default));

        await using var db = _fixture.CreateContext();
        var everything = await db.AuditLogEntries.AsNoTracking().Select(a => a.Details + " " + a.Reason).ToListAsync();

        Assert.NotEmpty(everything);
        Assert.DoesNotContain(everything, text => text.Contains("ZEBRA", StringComparison.OrdinalIgnoreCase));
    }
}
