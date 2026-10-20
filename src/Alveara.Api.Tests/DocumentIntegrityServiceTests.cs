using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01 against real SQL Server and a real on-disk store: the storage check. A stored file is Ok when it is there with the size and hash recorded at import, Missing when it is not on disk and Corrupt when its
/// size or content no longer matches; the result is kept in its own row (so a check never makes someone's open edit look stale), a re-check replaces it, one run over every document counts what it found and logs only counts,
/// a file that cannot be read just now is skipped and never marked corrupt, files nothing points to are listed only once they are old enough not to be an import in progress, opening refuses a file that is known
/// or seen to be damaged, and a failed log write saves no result.
/// </summary>
public class DocumentIntegrityServiceTests : DocumentTestBase
{
    private DocumentIntegrityService Integrity(AlveraDbContext db) => new(db, _blobs, Clock);
    private DocumentLibraryService Library(AlveraDbContext db) => new(db, new PatientFormReader(db), _blobs, Clock);

    private Task<IntegrityResult> CheckAsync(Guid id) => WithDb(db => Integrity(db).CheckDocumentAsync(id, S.Actor, default));
    private Task<StorageReport> CheckAllAsync(CancellationToken ct = default) => WithDb(db => Integrity(db).CheckAllAsync(S.Actor, ct));
    private Task<StorageSummary> SummaryAsync() => WithDb(db => Integrity(db).SummaryAsync(default));
    private Task<IReadOnlyList<OrphanFile>> OrphansAsync() => WithDb(db => Integrity(db).ListOrphansAsync(default));

    private async Task<(Guid Id, string Path)> ImportedAsync(byte[]? bytes = null, string name = "bitewing.png")
    {
        var doc = (await ImportAsync(Request(bytes, name))).Document;
        await using var db = Fixture.CreateContext();
        var blob = (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == doc.Id)).BlobId;
        return (doc.Id, Path.Combine(_storageRoot, blob.ToString("N")));
    }

    private static void Flip(string path) { var b = File.ReadAllBytes(path); b[^1] ^= 0xFF; File.WriteAllBytes(path, b); }
    private async Task<int> RowsAsync() { await using var db = Fixture.CreateContext(); return await db.DocumentIntegrity.CountAsync(); }
    private async Task<DocumentView> ViewAsync(Guid id) => (await WithDb(db => Library(db).GetAsync(id, default))).Document;

    // ---------- one document ----------

    [Fact]
    public async Task An_intact_file_is_ok_and_the_result_is_kept_with_who_and_when_without_moving_the_documents_row_version()
    {
        var (id, _) = await ImportedAsync();
        var before = await ViewAsync(id);
        Assert.Equal(("Unchecked", null), (before.IntegrityStatus, before.IntegrityCheckedAtUtc));
        var result = await CheckAsync(id);
        Assert.Equal((id, IntegrityStatuses.Ok), (result.DocumentId, result.Status));
        var after = await ViewAsync(id);
        Assert.Equal((IntegrityStatuses.Ok, before.RowVersion), (after.IntegrityStatus, after.RowVersion));
        Assert.NotNull(after.IntegrityCheckedAtUtc);
        await using var db = Fixture.CreateContext();
        Assert.Equal(S.Actor, (await db.DocumentIntegrity.AsNoTracking().SingleAsync()).CheckedByUserId);
    }

    [Fact]
    public async Task A_file_that_is_not_on_disk_is_missing()
    {
        var (id, path) = await ImportedAsync();
        File.Delete(path);
        Assert.Equal(IntegrityStatuses.Missing, (await CheckAsync(id)).Status);
    }

    [Fact]
    public async Task A_file_with_changed_content_of_the_same_size_and_a_file_that_was_cut_short_are_both_corrupt()
    {
        var (same, samePath) = await ImportedAsync(Png(200));
        var (short1, shortPath) = await ImportedAsync(Png(300));
        Flip(samePath);
        File.WriteAllBytes(shortPath, File.ReadAllBytes(shortPath)[..50]);
        Assert.Equal(IntegrityStatuses.Corrupt, (await CheckAsync(same)).Status);
        Assert.Equal(IntegrityStatuses.Corrupt, (await CheckAsync(short1)).Status);
    }

    [Fact]
    public async Task A_re_check_replaces_the_result_so_a_restored_file_is_ok_again_and_there_is_one_row_per_document()
    {
        var (id, path) = await ImportedAsync();
        var original = File.ReadAllBytes(path);
        File.Delete(path);
        Assert.Equal(IntegrityStatuses.Missing, (await CheckAsync(id)).Status);
        File.WriteAllBytes(path, original);
        Assert.Equal(IntegrityStatuses.Ok, (await CheckAsync(id)).Status);
        Assert.Equal(1, await RowsAsync());
        Assert.Equal(IntegrityStatuses.Ok, (await ViewAsync(id)).IntegrityStatus);
    }

    [Fact]
    public async Task An_unknown_document_is_not_found()
    {
        var e = await Assert.ThrowsAsync<DocumentException>(() => CheckAsync(Guid.NewGuid()));
        Assert.Equal(("document_not_found", 404), (e.Code, e.StatusCode));
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task A_failing_log_write_saves_no_result()
    {
        var (id, _) = await ImportedAsync();
        await FailAuditAsync("DocumentIntegrityChecked");
        var e = await Assert.ThrowsAsync<DocumentException>(() => CheckAsync(id));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal(0, await RowsAsync());
    }

    // ---------- every document ----------

    [Fact]
    public async Task A_run_over_every_document_counts_what_it_found_lists_the_problems_and_logs_only_counts()
    {
        var (a, _) = await ImportedAsync(Png(100), "a-private-name.png");
        var (b, bPath) = await ImportedAsync(Png(200), "b-private-name.png");
        var (c, cPath) = await ImportedAsync(Png(300), "c-private-name.png");
        var (d, _) = await ImportedAsync(Png(400), "d-private-name.png");
        File.Delete(bPath);
        Flip(cPath);

        var report = await CheckAllAsync();
        Assert.Equal((4, 2, 1, 1, 0), (report.Checked, report.Ok, report.Missing, report.Corrupt, report.Skipped));
        Assert.Equal(new[] { (b, IntegrityStatuses.Missing), (c, IntegrityStatuses.Corrupt) }.Order(), report.Problems.Select(p => (p.DocumentId, p.Status)).Order());
        Assert.Contains(report.Problems, p => p.FileName == "b-private-name.png" && p.PatientId == Ann);
        Assert.Equal(4, await RowsAsync());
        _ = (a, d);

        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(x => x.EventType == "DocumentStorageChecked");
        Assert.Equal(S.Actor, audit.PerformedByUserAccountId);
        Assert.Contains("4", audit.Details);
        Assert.DoesNotContain("private-name", $"{audit.Details} {audit.Reason}");
        Assert.DoesNotContain(Ann.ToString(), $"{audit.Details} {audit.Reason}", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Running_the_check_twice_gives_the_same_result_and_no_extra_rows()
    {
        var (_, path) = await ImportedAsync();
        await ImportedAsync(Png(222));
        Flip(path);
        var first = await CheckAllAsync();
        var second = await CheckAllAsync();
        Assert.Equal((first.Checked, first.Ok, first.Missing, first.Corrupt), (second.Checked, second.Ok, second.Missing, second.Corrupt));
        Assert.Equal(2, await RowsAsync());
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_just_now_is_skipped_and_its_last_result_is_left_alone()
    {
        var (locked, lockedPath) = await ImportedAsync(Png(100));
        await ImportedAsync(Png(200));
        Assert.Equal(IntegrityStatuses.Ok, (await CheckAsync(locked)).Status);
        await using (var held = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = await CheckAllAsync();
            Assert.Equal((2, 1, 0, 0, 1), (report.Checked, report.Ok, report.Missing, report.Corrupt, report.Skipped));
            Assert.Empty(report.Problems);                                                                      // a locked file is not a damaged file
        }
        Assert.Equal(IntegrityStatuses.Ok, (await ViewAsync(locked)).IntegrityStatus);
    }

    [Fact]
    public async Task A_cancelled_run_saves_nothing()
    {
        await ImportedAsync();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CheckAllAsync(cts.Token));
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task An_inactive_documents_file_is_checked_like_any_other()
    {
        var (id, path) = await ImportedAsync();
        await WithDb(async db => { await new DocumentStatusService(db, Clock).InactivateAsync(id, (await ViewAsync(id)).RowVersion, "Wrong patient.", S.Actor, default); return 0; });
        File.Delete(path);
        Assert.Equal(1, (await CheckAllAsync()).Missing);
    }

    // ---------- files nothing points to ----------

    private Guid Stray(DateTimeOffset writtenAt, string? name = null)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(_storageRoot, name ?? id.ToString("N"));
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, writtenAt.UtcDateTime);
        return id;
    }

    [Fact]
    public async Task A_stored_file_nothing_points_to_is_listed_once_it_is_older_than_an_hour_and_the_files_of_documents_are_not()
    {
        var (_, docPath) = await ImportedAsync();
        File.SetLastWriteTimeUtc(docPath, DateTime.UtcNow.AddHours(-6));                                                       // old enough to be listed, if nothing pointed to it
        var (inactive, inactivePath) = await ImportedAsync(Png(333));
        File.SetLastWriteTimeUtc(inactivePath, DateTime.UtcNow.AddHours(-6));
        await WithDb(async db => { await new DocumentStatusService(db, Clock).InactivateAsync(inactive, (await ViewAsync(inactive)).RowVersion, "Duplicate.", S.Actor, default); return 0; });
        var old = Stray(DateTimeOffset.UtcNow.AddHours(-3));
        var recent = Stray(DateTimeOffset.UtcNow.AddMinutes(-10));
        Stray(DateTimeOffset.UtcNow.AddHours(-5), $"{Guid.NewGuid():N}.tmp-{Guid.NewGuid():N}");                              // a file still being written is not a blob
        Stray(DateTimeOffset.UtcNow.AddHours(-5), "notes.txt");                                                              // neither is a stray file with another name

        var orphans = await OrphansAsync();
        Assert.Equal([old], orphans.Select(o => o.BlobId));
        Assert.Equal(3, orphans.Single().SizeBytes);
        Assert.DoesNotContain(orphans, o => o.BlobId == recent);
        Assert.True(File.Exists(docPath));                                                                                   // listing removes nothing
    }

    [Fact]
    public async Task The_summary_counts_each_stored_result_and_the_documents_never_checked_and_lists_the_problems_and_the_orphans()
    {
        var (a, aPath) = await ImportedAsync(Png(100));
        var (b, _) = await ImportedAsync(Png(200));
        await ImportedAsync(Png(300));
        File.Delete(aPath);
        await CheckAsync(a);
        await CheckAsync(b);
        var orphan = Stray(DateTimeOffset.UtcNow.AddHours(-2));

        var summary = await SummaryAsync();
        Assert.Equal((3, 1, 1, 0, 1), (summary.Documents, summary.Ok, summary.Missing, summary.Corrupt, summary.NeverChecked));
        Assert.Equal([a], summary.Problems.Select(p => p.DocumentId));
        Assert.Equal([orphan], summary.Orphans.Select(o => o.BlobId));
    }

    // ---------- opening ----------

    private Task<OpenedDocument> OpenAsync(Guid id) => WithDb(db => Library(db).OpenAsync(id, S.Actor, default));

    [Fact]
    public async Task Opening_a_file_that_is_gone_or_cut_short_is_refused_with_a_clear_code_and_logs_no_open()
    {
        var (gone, gonePath) = await ImportedAsync(Png(100));
        var (cut, cutPath) = await ImportedAsync(Png(200));
        File.Delete(gonePath);
        File.WriteAllBytes(cutPath, File.ReadAllBytes(cutPath)[..40]);
        Assert.Equal(("file_missing", 404), await CodeOf(() => OpenAsync(gone)));
        Assert.Equal(("file_corrupt", 409), await CodeOf(() => OpenAsync(cut)));
        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await db.DocumentEvents.CountAsync(e => e.Kind == DocumentEventKinds.Opened));
    }

    private static async Task<(string, int)> CodeOf(Func<Task<OpenedDocument>> open)
    {
        var e = await Assert.ThrowsAsync<DocumentException>(open);
        return (e.Code, e.StatusCode);
    }

    [Fact]
    public async Task A_file_the_last_check_found_corrupt_is_not_served_until_a_re_check_finds_it_intact()
    {
        var (id, path) = await ImportedAsync(Png(200));
        var original = File.ReadAllBytes(path);
        Flip(path);                                                                                                           // same size, so only the hash can tell
        Assert.Equal(IntegrityStatuses.Corrupt, (await CheckAsync(id)).Status);
        Assert.Equal(("file_corrupt", 409), await CodeOf(() => OpenAsync(id)));
        File.WriteAllBytes(path, original);
        Assert.Equal(("file_corrupt", 409), await CodeOf(() => OpenAsync(id)));                                              // still refused: it was found damaged and has not been checked since
        Assert.Equal(IntegrityStatuses.Ok, (await CheckAsync(id)).Status);
        await using var opened = await OpenAsync(id);
        Assert.True(opened.SizeBytes > 0);
    }

    [Fact]
    public async Task A_file_that_was_missing_and_is_back_with_the_right_size_opens_without_waiting_for_a_re_check()
    {
        var (id, path) = await ImportedAsync(Png(200));
        var original = File.ReadAllBytes(path);
        File.Delete(path);
        Assert.Equal(IntegrityStatuses.Missing, (await CheckAsync(id)).Status);
        File.WriteAllBytes(path, original);
        await using var opened = await OpenAsync(id);
        Assert.Equal(original.Length, opened.SizeBytes);
    }
}
