using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01 against real SQL Server and a real on-disk store: reviewing and removing stored files that no document points to (left by a crash or a failed clean-up). A reviewer names the files they saw; each is
/// removed only if, at that moment, no document points to it and it is old enough not to be an import still in progress; each removal is logged with its own entry (a file id, never a name or a patient) and happens
/// with it or not at all; repeating a removal changes nothing; one file that cannot be removed does not stop the others; and the two ways an import can be interrupted leave a state this review and the storage check
/// can see and repair.
/// </summary>
public class DocumentOrphanServiceTests : DocumentTestBase
{
    private DocumentIntegrityService Integrity(AlveraDbContext db) => new(db, _blobs, Clock);
    private Task<OrphanRemovalReport> RemoveAsync(params Guid[] ids) => WithDb(db => Integrity(db).RemoveOrphansAsync(ids, S.Actor, default));
    private Task<IReadOnlyList<OrphanFile>> OrphansAsync() => WithDb(db => Integrity(db).ListOrphansAsync(default));

    private string PathOf(Guid blobId) => Path.Combine(_storageRoot, blobId.ToString("N"));

    /// <summary>A stored file with no document, last written the given time ago.</summary>
    private async Task<Guid> StrayAsync(TimeSpan age)
    {
        var blob = await _blobs.StoreAsync(new MemoryStream([1, 2, 3, 4]));
        File.SetLastWriteTimeUtc(PathOf(blob.Id), DateTime.UtcNow - age);
        return blob.Id;
    }

    private async Task<Guid> BlobOfAsync(Guid documentId) { await using var db = Fixture.CreateContext(); return (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId)).BlobId; }
    private async Task<List<string>> RemovalAuditAsync() { await using var db = Fixture.CreateContext(); return await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "DocumentOrphanRemoved").Select(a => a.Details + "|" + a.TargetUserAccountId).ToListAsync(); }

    // ---------- removing ----------

    [Fact]
    public async Task An_unreferenced_old_file_is_removed_and_logged_with_its_id_the_user_and_the_time_and_nothing_else()
    {
        var orphan = await StrayAsync(TimeSpan.FromHours(3));
        var before = DateTimeOffset.UtcNow;
        var report = await RemoveAsync(orphan);
        Assert.Equal([(orphan, OrphanOutcomes.Removed)], report.Results.Select(r => (r.BlobId, r.Outcome)));
        Assert.False(File.Exists(PathOf(orphan)));
        Assert.Empty(await OrphansAsync());

        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "DocumentOrphanRemoved");
        Assert.Equal((S.Actor, orphan), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        Assert.InRange(audit.TimestampUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.DoesNotContain(".png", $"{audit.Details} {audit.Reason}");
    }

    [Fact]
    public async Task Repeating_a_removal_changes_nothing_and_logs_nothing_more()
    {
        var orphan = await StrayAsync(TimeSpan.FromHours(3));
        await RemoveAsync(orphan);
        var again = await RemoveAsync(orphan);
        Assert.Equal(OrphanOutcomes.AlreadyGone, again.Results.Single().Outcome);
        Assert.Single(await RemovalAuditAsync());
    }

    [Fact]
    public async Task A_file_a_document_points_to_is_never_removed_however_old_it_is()
    {
        var doc = (await ImportAsync(Request())).Document;
        var blob = await BlobOfAsync(doc.Id);
        File.SetLastWriteTimeUtc(PathOf(blob), DateTime.UtcNow.AddHours(-9));
        var report = await RemoveAsync(blob);
        Assert.Equal(OrphanOutcomes.StillReferenced, report.Results.Single().Outcome);
        Assert.True(File.Exists(PathOf(blob)));
        Assert.Empty(await RemovalAuditAsync());
    }

    [Fact]
    public async Task A_file_younger_than_an_hour_is_never_removed_because_it_may_be_an_import_in_progress()
    {
        var recent = await StrayAsync(TimeSpan.FromMinutes(20));
        var report = await RemoveAsync(recent);
        Assert.Equal(OrphanOutcomes.TooNew, report.Results.Single().Outcome);
        Assert.True(File.Exists(PathOf(recent)));
        Assert.Empty(await RemovalAuditAsync());
    }

    [Fact]
    public async Task A_file_that_gained_a_document_after_the_reviewer_saw_it_is_not_removed()
    {
        // the file was listed as unreferenced, then a document came to point to it (an administrator repaired a record): the removal re-checks at the moment it acts
        var orphan = await StrayAsync(TimeSpan.FromHours(3));
        Assert.Contains(await OrphansAsync(), o => o.BlobId == orphan);
        var doc = (await ImportAsync(Request())).Document;
        await WithDb(db => db.Database.ExecuteSqlAsync($"ALTER TABLE [Documents] DISABLE TRIGGER [TR_Documents_Immutable]"));
        try { await WithDb(db => db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [BlobId] = {orphan} WHERE [Id] = {doc.Id}")); }
        finally { await WithDb(db => db.Database.ExecuteSqlAsync($"ALTER TABLE [Documents] ENABLE TRIGGER [TR_Documents_Immutable]")); }
        var report = await RemoveAsync(orphan);
        Assert.Equal(OrphanOutcomes.StillReferenced, report.Results.Single().Outcome);
        Assert.True(File.Exists(PathOf(orphan)));
    }

    [Fact]
    public async Task A_mixed_request_gets_one_outcome_per_file_and_only_the_valid_ones_are_removed()
    {
        var old1 = await StrayAsync(TimeSpan.FromHours(2));
        var old2 = await StrayAsync(TimeSpan.FromHours(4));
        var recent = await StrayAsync(TimeSpan.FromMinutes(5));
        var doc = await BlobOfAsync((await ImportAsync(Request())).Document.Id);
        var unknown = Guid.NewGuid();
        var report = await RemoveAsync(old1, recent, doc, unknown, old2);
        Assert.Equal(new[] { (old1, "Removed"), (recent, "TooNew"), (doc, "StillReferenced"), (unknown, "AlreadyGone"), (old2, "Removed") }, report.Results.Select(r => (r.BlobId, r.Outcome)));
        Assert.Equal(2, report.Removed);
        Assert.Equal(2, (await RemovalAuditAsync()).Count);
        Assert.True(File.Exists(PathOf(recent)) && File.Exists(PathOf(doc)));
    }

    [Fact]
    public async Task The_same_file_named_twice_is_handled_once()
    {
        var orphan = await StrayAsync(TimeSpan.FromHours(2));
        var report = await RemoveAsync(orphan, orphan);
        Assert.Equal([OrphanOutcomes.Removed], report.Results.Select(r => r.Outcome));
        Assert.Single(await RemovalAuditAsync());
    }

    [Fact]
    public async Task A_removal_that_cannot_delete_the_file_is_logged_as_nothing_and_does_not_stop_the_others()
    {
        var a = await StrayAsync(TimeSpan.FromHours(2));
        var b = await StrayAsync(TimeSpan.FromHours(3));
        _blobs.FailDelete = true;
        var failed = await RemoveAsync(a);
        Assert.Equal(OrphanOutcomes.Failed, failed.Results.Single().Outcome);
        Assert.Empty(await RemovalAuditAsync());                                                                           // no entry for a removal that did not happen
        Assert.True(File.Exists(PathOf(a)));
        _blobs.FailDelete = false;
        var report = await RemoveAsync(a, b);
        Assert.Equal([OrphanOutcomes.Removed, OrphanOutcomes.Removed], report.Results.Select(r => r.Outcome));
    }

    [Fact]
    public async Task A_failing_log_write_means_the_file_is_not_removed()
    {
        var orphan = await StrayAsync(TimeSpan.FromHours(2));
        await FailAuditAsync("DocumentOrphanRemoved");
        var report = await RemoveAsync(orphan);
        Assert.Equal(OrphanOutcomes.Failed, report.Results.Single().Outcome);
        Assert.True(File.Exists(PathOf(orphan)));
    }

    [Fact]
    public async Task Simultaneous_removals_of_one_file_remove_it_once_and_log_it_once()
    {
        var orphan = await StrayAsync(TimeSpan.FromHours(2));
        _blobs.ArmInfoGate(callers: 4, timeout: TimeSpan.FromSeconds(1.5));                                                  // every caller looks at the file at the same moment, so without the lock each would find it and remove it
        var reports = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RemoveAsync(orphan)));
        var outcomes = reports.Select(r => r.Results.Single().Outcome).ToList();
        Assert.Equal(1, outcomes.Count(o => o == OrphanOutcomes.Removed));
        Assert.All(outcomes.Where(o => o != OrphanOutcomes.Removed), o => Assert.Equal(OrphanOutcomes.AlreadyGone, o));
        Assert.Single(await RemovalAuditAsync());
        Assert.False(File.Exists(PathOf(orphan)));
    }

    [Fact]
    public async Task Nothing_named_or_too_many_named_is_refused()
    {
        var none = await Assert.ThrowsAsync<DocumentException>(() => RemoveAsync());
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, 400, "blobIds"), (none.Code, none.StatusCode, none.Problems.Single().Field));
        var many = await Assert.ThrowsAsync<DocumentException>(() => RemoveAsync([.. Enumerable.Range(0, 501).Select(_ => Guid.NewGuid())]));
        Assert.Equal("blobIds", many.Problems.Single().Field);
        var limit = await RemoveAsync([.. Enumerable.Range(0, 500).Select(_ => Guid.NewGuid())]);                          // exactly 500 is accepted
        Assert.Equal(500, limit.Results.Count);
    }

    // ---------- the two ways an import can be interrupted ----------

    [Fact]
    public async Task A_crash_between_storing_the_file_and_recording_the_document_leaves_a_file_the_review_finds_and_removes()
    {
        // the store succeeded and the process died before the record was saved: simulated by storing a file with no document, as an import does first
        var blob = await _blobs.StoreAsync(new MemoryStream(Png()));
        Assert.Empty(await OrphansAsync());                                                                                // too new to be listed: it could still be an import in progress
        File.SetLastWriteTimeUtc(PathOf(blob.Id), DateTime.UtcNow.AddHours(-2));
        Assert.Equal([blob.Id], (await OrphansAsync()).Select(o => o.BlobId));
        Assert.Equal(OrphanOutcomes.Removed, (await RemoveAsync(blob.Id)).Results.Single().Outcome);
        Assert.Equal(0, (await CountsAsync()).Documents);                                                                  // and no document ever existed for it
    }

    [Fact]
    public async Task An_import_whose_clean_up_failed_leaves_a_file_that_is_found_later_and_the_failure_was_reported_at_the_time()
    {
        await FailAuditAsync("DocumentImported");
        _blobs.FailDelete = true;
        var e = await Assert.ThrowsAsync<DocumentException>(() => ImportAsync(Request()));
        Assert.Equal("save_failed", e.Code);                                                                              // the importer was told nothing was imported
        Assert.Equal((0, 0), ((await CountsAsync()).Documents, (await CountsAsync()).Events));
        var leftover = Directory.GetFiles(_storageRoot).Single();
        File.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddHours(-2));
        _blobs.FailDelete = false;
        var orphan = Assert.Single(await OrphansAsync());
        Assert.Equal(OrphanOutcomes.Removed, (await RemoveAsync(orphan.BlobId)).Results.Single().Outcome);
        Assert.Empty(Directory.GetFiles(_storageRoot));
    }

    [Fact]
    public async Task A_document_whose_file_was_lost_afterwards_is_found_by_the_storage_check_and_refused_when_opened()
    {
        var doc = (await ImportAsync(Request())).Document;
        File.Delete(PathOf(await BlobOfAsync(doc.Id)));
        var report = await WithDb(db => Integrity(db).CheckAllAsync(S.Actor, default));
        Assert.Equal(1, report.Missing);
        var e = await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => new DocumentLibraryService(db, new Alveara.Api.Architecture.Forms.PatientFormReader(db), _blobs, Clock).OpenAsync(doc.Id, S.Actor, default)));
        Assert.Equal("file_missing", e.Code);
    }
}
