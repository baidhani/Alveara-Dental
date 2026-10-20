using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-010 against real SQL Server: importing a document (docs/DOCUMENTS.md, "What happens to a file on import"). A document is stored and linked to its patient with the right category, date, description, hash
/// and type; every refusal leaves nothing stored and nothing recorded; a duplicate is warned about, and added only on an explicit, verified confirmation, recorded as a duplicate; a repeated import changes nothing;
/// the log and the history are written in the same save; a failing store, a failing log write and simultaneous imports leave a correct state; the history numbers have no gap; and the log never carries a filename,
/// a description or a patient.
/// </summary>
public class DocumentImportServiceTests : SafetyTestBase
{
    private string _storageRoot = string.Empty;
    private RecordingBlobStorage _blobs = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _storageRoot = Path.Combine(AlveraTestEnvironment.StorageFolder, $"documents-{Guid.NewGuid():N}");
        _blobs = new RecordingBlobStorage(new LocalDiskBlobStorage(_storageRoot));
    }

    /// <summary>The real store, counting what it was asked to do and able to fail on request.</summary>
    private sealed class RecordingBlobStorage(IBlobStorage inner) : IBlobStorage
    {
        public int Stored; public int Deleted; public bool FailStore; public bool FailDelete;
        public async Task<StoredBlob> StoreAsync(Stream content, CancellationToken cancellationToken = default)
        {
            if (FailStore) throw new IOException("disk full");
            var blob = await inner.StoreAsync(content, cancellationToken);
            Interlocked.Increment(ref Stored);
            return blob;
        }
        public Task<Stream> OpenReadAsync(Guid blobId, CancellationToken cancellationToken = default) => inner.OpenReadAsync(blobId, cancellationToken);
        public Task<bool> VerifyIntegrityAsync(Guid blobId, string expectedSha256, CancellationToken cancellationToken = default) => inner.VerifyIntegrityAsync(blobId, expectedSha256, cancellationToken);
        public Task DeleteAsync(Guid blobId, CancellationToken cancellationToken = default)
        {
            if (FailDelete) throw new IOException("locked");
            Interlocked.Increment(ref Deleted);
            return inner.DeleteAsync(blobId, cancellationToken);
        }
    }

    private int FilesOnDisk() => Directory.Exists(_storageRoot) ? Directory.GetFiles(_storageRoot).Length : 0;

    // a real PNG header followed by filler, so that content detection recognizes it
    private static byte[] Png(int extra = 100, byte fill = 7) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, extra)];
    private static byte[] Pdf(int extra = 100) => [.. "%PDF-1.7\n"u8.ToArray(), .. Enumerable.Repeat((byte)'x', extra)];
    private static byte[] Program() { var b = new byte[0x100]; b[0] = (byte)'M'; b[1] = (byte)'Z'; b[0x3C] = 0x80; b[0x80] = (byte)'P'; b[0x81] = (byte)'E'; return b; }

    private DocumentImportRequest Request(byte[]? bytes = null, string name = "bitewing.png", Guid? patient = null, Guid? category = null, DateOnly? date = null, string? description = "Bitewing, left side.",
        string? key = null, Guid? confirm = null, long? length = null)
    {
        bytes ??= Png();
        return new DocumentImportRequest(patient ?? Ann, new MemoryStream(bytes), length ?? bytes.Length, name, category ?? DocumentCategorySeeds.Radiograph, date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            description, key ?? $"imp-{Guid.NewGuid():N}", confirm);
    }

    private Task<DocumentImportResult> ImportAsync(DocumentImportRequest request, Guid? actor = null) =>
        WithDb(db => new DocumentImportService(db, _blobs, Clock).ImportAsync(request, actor ?? S.Actor, default));

    private static async Task<DocumentException> Refused(Func<Task<DocumentImportResult>> action) => await Assert.ThrowsAsync<DocumentException>(action);

    private async Task<(int Documents, int Events, int Audits)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Documents.CountAsync(), await db.DocumentEvents.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("Document")));
    }

    private async Task AssertNothingRecordedAsync() { Assert.Equal((0, 0, 0), await CountsAsync()); Assert.Equal(0, FilesOnDisk()); }

    private Task FailAuditAsync(string eventType) => WithDb(async db => { await db.Database.ExecuteSqlRawAsync($@"CREATE TRIGGER [TR_Test_RefuseAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{eventType}') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END"); return 0; });

    // ---------- the happy path ----------

    [Fact]
    public async Task An_imported_document_is_stored_and_linked_to_its_patient_with_the_right_category_date_description_hash_and_type()
    {
        var bytes = Png(500);
        var date = new DateOnly(2026, 10, 1);
        var result = await ImportAsync(Request(bytes, "Bitewing Left.PNG", category: DocumentCategorySeeds.Photo, date: date, description: "  Retaken after the first was blurred.  "));

        Assert.False(result.Replayed);
        var v = result.Document;
        Assert.Equal((Ann, DocumentCategorySeeds.Photo, "photo", "Photo", date, "Retaken after the first was blurred."), (v.PatientId, v.CategoryId, v.CategoryCode, v.CategoryName, v.DocumentDate, v.Description));
        Assert.Equal(("Bitewing Left.PNG", "png", true, (long)bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))), (v.OriginalFileName, v.DetectedType, v.TypeVerified, v.SizeBytes, v.Sha256));
        Assert.Null(v.DuplicateOfDocumentId);

        await using var db = Fixture.CreateContext();
        var row = await db.Documents.AsNoTracking().SingleAsync();
        Assert.Equal((v.Id, "image/png", S.Actor), (row.Id, row.ContentType, row.ImportedByUserId));
        await using var stored = await _blobs.OpenReadAsync(row.BlobId);
        var ms = new MemoryStream(); await stored.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());                                           // the stored file is exactly the uploaded one
        Assert.True(await _blobs.VerifyIntegrityAsync(row.BlobId, row.Sha256));
    }

    [Fact]
    public async Task A_document_may_have_no_description_and_a_file_of_another_type_is_stored_as_not_verified()
    {
        var result = await ImportAsync(Request("notes,csv\n1,2"u8.ToArray(), "notes.csv", description: "  "));
        Assert.Null(result.Document.Description);
        Assert.Equal(("other", false), (result.Document.DetectedType, result.Document.TypeVerified));
        await using var db = Fixture.CreateContext();
        Assert.Equal("application/octet-stream", (await db.Documents.AsNoTracking().SingleAsync()).ContentType);
    }

    [Fact]
    public async Task The_type_comes_from_the_content_not_from_the_name()
    {
        var pdf = await ImportAsync(Request(Pdf(), "scan.pdf", category: DocumentCategorySeeds.LabReport));
        Assert.Equal("pdf", pdf.Document.DetectedType);
        var unnamed = await ImportAsync(Request(Png(), "scan-without-extension"));
        Assert.Equal(("png", true), (unnamed.Document.DetectedType, unnamed.Document.TypeVerified));
    }

    [Fact]
    public async Task The_original_name_is_normalized_and_never_decides_where_the_file_is_stored()
    {
        var v = (await ImportAsync(Request(Png(), @"C:\Users\front desk\Scans\ann (1).png"))).Document;
        Assert.Equal("ann (1).png", v.OriginalFileName);
        await using var db = Fixture.CreateContext();
        var row = await db.Documents.AsNoTracking().SingleAsync();
        Assert.All(Directory.GetFiles(_storageRoot), f => Assert.Equal(row.BlobId.ToString("N"), Path.GetFileName(f)));
    }

    [Fact]
    public async Task One_save_writes_the_document_the_history_row_and_the_log_with_the_user_and_time_and_the_log_carries_no_filename_description_or_patient()
    {
        var before = DateTimeOffset.UtcNow;
        var v = (await ImportAsync(Request(Png(), "ann-okafor-xray.png", description: "Ann Okafor, upper left molar."))).Document;
        Assert.Equal((1, 1, 1), await CountsAsync());

        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType.StartsWith("Document"));
        Assert.Equal(("DocumentImported", S.Actor), (audit.EventType, audit.PerformedByUserAccountId));
        Assert.InRange(audit.TimestampUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(v.Id, audit.TargetUserAccountId);
        var text = $"{audit.Details} {audit.Reason} {audit.EntityType}";
        Assert.DoesNotContain("okafor", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".png", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Ann.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("molar", text, StringComparison.OrdinalIgnoreCase);

        var ev = await db.DocumentEvents.AsNoTracking().SingleAsync();
        Assert.Equal((1L, DocumentEventKinds.Imported, v.Id, Ann, S.Actor), (ev.EventNumber, ev.Kind, ev.DocumentId, ev.PatientId, ev.ByUserId));
        Assert.Null(ev.RelatedDocumentId);
    }

    // ---------- every refusal stores nothing ----------

    [Fact]
    public async Task An_unknown_patient_is_refused()
    {
        var e = await Refused(() => ImportAsync(Request(patient: Guid.NewGuid())));
        Assert.Equal(("patient_not_found", 404), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has\u0001control")]
    public async Task A_missing_or_invalid_save_key_is_refused(string? key)
    {
        var r = Request() with { IdempotencyKey = key };
        var e = await Refused(() => ImportAsync(r));
        Assert.Equal(("idempotency_key_required", 400), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        var e = await Refused(() => ImportAsync(Request([], "empty.png")));
        Assert.Equal((DocumentErrorCodes.FileEmpty, 400), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_file_over_the_configured_maximum_is_refused_before_it_is_stored_and_one_at_the_maximum_is_accepted()
    {
        await WithDb(db => db.Database.ExecuteSqlRawAsync($"UPDATE [DocumentSettings] SET [MaxUploadBytes] = {DocumentFileRules.MinUploadLimitBytes}"));
        var atLimit = new byte[DocumentFileRules.MinUploadLimitBytes]; Png().CopyTo(atLimit, 0);
        var over = new byte[atLimit.Length + 1]; Png().CopyTo(over, 0);
        var e = await Refused(() => ImportAsync(Request(over, "big.png")));
        Assert.Equal((DocumentErrorCodes.FileTooLarge, 413), (e.Code, e.StatusCode));
        Assert.Equal(0, _blobs.Stored);                                                // refused before anything was written
        await AssertNothingRecordedAsync();

        await ImportAsync(Request(atLimit, "exactly.png"));
        Assert.Equal(1, (await CountsAsync()).Documents);
    }

    [Fact]
    public async Task A_declared_length_that_is_smaller_than_the_real_file_cannot_get_a_large_file_past_the_limit()
    {
        await WithDb(db => db.Database.ExecuteSqlRawAsync($"UPDATE [DocumentSettings] SET [MaxUploadBytes] = {DocumentFileRules.MinUploadLimitBytes}"));
        var big = new byte[DocumentFileRules.MinUploadLimitBytes + 10]; Png().CopyTo(big, 0);
        var e = await Refused(() => ImportAsync(Request(big, "liar.png", length: 1000)));
        Assert.Equal(DocumentErrorCodes.FileTooLarge, e.Code);
        await AssertNothingRecordedAsync();                                            // the stored copy was removed
        Assert.Equal(1, _blobs.Stored); Assert.Equal(1, _blobs.Deleted);
    }

    [Theory]
    [InlineData("..\\..\\x\u202Egnp.exe")]
    [InlineData("a|b.png")]
    [InlineData("CON.png")]
    [InlineData("   ")]
    public async Task An_unusable_file_name_is_refused(string name)
    {
        var e = await Refused(() => ImportAsync(Request(name: name)));
        Assert.Equal((DocumentErrorCodes.FileNameInvalid, 400), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("page.HTML")]
    [InlineData("report.pdf.exe")]
    [InlineData("script.js")]
    [InlineData("run.bat. ")]
    [InlineData("setup.exe::$DATA")]
    public async Task A_blocked_file_type_is_refused_whatever_the_case_or_trick(string name)
    {
        var e = await Refused(() => ImportAsync(Request(name: name)));
        Assert.Equal((DocumentErrorCodes.FileTypeBlocked, 400), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_type_an_admin_added_to_the_blocked_list_is_refused_and_a_removed_one_is_accepted_again()
    {
        await WithDb(async db => { db.BlockedFileExtensions.Add(new BlockedFileExtension { Extension = "swf", AddedByUserId = S.Actor, AddedAtUtc = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); return 0; });
        Assert.Equal(DocumentErrorCodes.FileTypeBlocked, (await Refused(() => ImportAsync(Request(name: "movie.swf")))).Code);
        await WithDb(db => db.Database.ExecuteSqlRawAsync("DELETE FROM [BlockedFileExtensions] WHERE [Extension] = 'swf'"));
        await ImportAsync(Request(name: "movie.swf"));
        Assert.Equal(1, (await CountsAsync()).Documents);
    }

    [Fact]
    public async Task A_windows_program_is_refused_even_under_a_harmless_name()
    {
        var e = await Refused(() => ImportAsync(Request(Program(), "invoice.pdf")));
        Assert.Equal((DocumentErrorCodes.ExecutableContent, 400), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task A_file_whose_content_is_not_the_type_it_claims_is_refused()
    {
        var e = await Refused(() => ImportAsync(Request(Png(), "scan.pdf")));
        Assert.Equal((DocumentErrorCodes.FileTypeMismatch, 400), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task Every_wrong_entry_is_listed_and_nothing_is_stored_for_an_incorrect_category_or_metadata()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var e = await Refused(() => ImportAsync(Request(category: Guid.NewGuid(), date: tomorrow, description: new string('d', 501))));
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, 400), (e.Code, e.StatusCode));
        Assert.Equal(["category", "description", "documentDate"], e.Problems.Select(p => p.Field).OrderBy(x => x));
        await AssertNothingRecordedAsync();

        var missing = await Refused(() => ImportAsync(Request() with { CategoryId = null, DocumentDate = null }));
        Assert.Equal(["category", "documentDate"], missing.Problems.Select(p => p.Field).OrderBy(x => x));
        var control = await Refused(() => ImportAsync(Request(description: "bad\u0001text")));
        Assert.Equal(["description"], control.Problems.Select(p => p.Field));
        var ancient = await Refused(() => ImportAsync(Request(date: new DateOnly(1800, 1, 1))));
        Assert.Equal(["documentDate"], ancient.Problems.Select(p => p.Field));
    }

    [Fact]
    public async Task A_deactivated_category_is_refused_for_a_new_import_and_a_document_keeps_its_category_when_it_is_deactivated_later()
    {
        var kept = (await ImportAsync(Request(category: DocumentCategorySeeds.Other))).Document;
        await WithDb(db => db.Database.ExecuteSqlRawAsync($"UPDATE [DocumentCategories] SET [IsActive] = 0 WHERE [Id] = '{DocumentCategorySeeds.Other}'"));
        var e = await Refused(() => ImportAsync(Request(category: DocumentCategorySeeds.Other)));
        Assert.Equal(("category", "inactive"), (e.Problems.Single().Field, e.Problems.Single().Code));
        await using var db = Fixture.CreateContext();
        Assert.Equal(DocumentCategorySeeds.Other, (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == kept.Id)).CategoryId);
    }

    [Fact]
    public async Task Today_is_accepted_as_a_date_and_the_day_after_is_not()
    {
        var today = DateOnly.FromDateTime(Clock.ToPracticeLocal(Clock.UtcNow).DateTime);
        await ImportAsync(Request(date: today));
        Assert.Equal("documentDate", (await Refused(() => ImportAsync(Request(date: today.AddDays(1))))).Problems.Single().Field);
    }

    // ---------- duplicates ----------

    [Fact]
    public async Task The_same_file_for_the_same_patient_is_warned_about_with_the_existing_document_and_nothing_is_stored_or_recorded()
    {
        var first = (await ImportAsync(Request(Png(300), "first.png", date: new DateOnly(2026, 9, 1)))).Document;
        var e = await Refused(() => ImportAsync(Request(Png(300), "second.png")));
        Assert.Equal(("duplicate_document", 409), (e.Code, e.StatusCode));
        var summary = Assert.IsType<DuplicateSummary>(e.Detail);
        Assert.Equal((first.Id, "Radiograph", new DateOnly(2026, 9, 1)), (summary.DocumentId, summary.CategoryName, summary.DocumentDate));
        Assert.Equal((1, 1, 1), await CountsAsync());
        Assert.Equal(1, FilesOnDisk());                                                // the second copy was removed
    }

    [Fact]
    public async Task A_duplicate_warning_uses_up_no_save_key_so_the_confirmed_retry_may_reuse_it()
    {
        var first = (await ImportAsync(Request(Png(300)))).Document;
        var key = "same-key";
        await Refused(() => ImportAsync(Request(Png(300), key: key)));
        var added = (await ImportAsync(Request(Png(300), key: key, confirm: first.Id))).Document;
        Assert.Equal(first.Id, added.DuplicateOfDocumentId);
    }

    [Fact]
    public async Task A_confirmed_duplicate_is_added_and_recorded_as_one_in_the_history_and_the_log()
    {
        var first = (await ImportAsync(Request(Png(300)))).Document;
        var copy = (await ImportAsync(Request(Png(300), "again.png", confirm: first.Id))).Document;
        Assert.Equal(first.Id, copy.DuplicateOfDocumentId);
        Assert.NotEqual(first.Id, copy.Id);
        Assert.Equal(2, FilesOnDisk());

        await using var db = Fixture.CreateContext();
        var ev = await db.DocumentEvents.AsNoTracking().SingleAsync(e => e.DocumentId == copy.Id);
        Assert.Equal((DocumentEventKinds.ImportedAsDuplicate, first.Id, 2L), (ev.Kind, ev.RelatedDocumentId, ev.EventNumber));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.TargetUserAccountId == copy.Id);
        Assert.Equal(("DocumentDuplicateImported", S.Actor), (audit.EventType, audit.PerformedByUserAccountId));
        Assert.DoesNotContain(first.Id.ToString(), audit.Details ?? string.Empty);      // ids of other documents are history, not log text
    }

    [Fact]
    public async Task A_confirmation_that_does_not_match_is_refused_and_stores_nothing()
    {
        await ImportAsync(Request(Png(300)));
        var different = (await ImportAsync(Request(Png(400, 9)))).Document;
        var bos = (await ImportAsync(Request(Png(300), patient: Bo))).Document;          // the same file is not a duplicate for another patient
        Assert.Null(bos.DuplicateOfDocumentId);
        var before = await CountsAsync(); var files = FilesOnDisk();

        foreach (var wrong in new[] { Guid.NewGuid(), different.Id, bos.Id })                // unknown, a different file, another patient's document
        {
            var e = await Refused(() => ImportAsync(Request(Png(300), confirm: wrong)));
            Assert.Equal(("duplicate_confirmation_invalid", 409), (e.Code, e.StatusCode));
        }
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(files, FilesOnDisk());
    }

    [Fact]
    public async Task A_confirmation_for_a_file_that_is_not_a_duplicate_is_refused()
    {
        var other = (await ImportAsync(Request(Png(300)))).Document;
        var e = await Refused(() => ImportAsync(Request(Png(999, 3), confirm: other.Id)));
        Assert.Equal("duplicate_confirmation_invalid", e.Code);
        Assert.Equal(1, (await CountsAsync()).Documents);
    }

    // ---------- repeats ----------

    [Fact]
    public async Task The_same_request_repeated_with_the_same_key_returns_the_same_document_and_changes_nothing()
    {
        var first = await ImportAsync(Request(Png(300), key: "once"));
        var second = await ImportAsync(Request(Png(300), key: "once"));
        Assert.True(second.Replayed);
        Assert.Equal(first.Document.Id, second.Document.Id);
        Assert.Equal((1, 1, 1), await CountsAsync());
        Assert.Equal(1, FilesOnDisk());
    }

    [Fact]
    public async Task The_same_key_for_a_different_file_or_different_details_is_refused()
    {
        await ImportAsync(Request(Png(300), key: "k1"));
        var file = await Refused(() => ImportAsync(Request(Png(301), key: "k1")));
        Assert.Equal(("idempotency_key_reused", 409), (file.Code, file.StatusCode));
        var details = await Refused(() => ImportAsync(Request(Png(300), key: "k1", description: "Different words.")));
        Assert.Equal("idempotency_key_reused", details.Code);
        var category = await Refused(() => ImportAsync(Request(Png(300), key: "k1", category: DocumentCategorySeeds.Photo)));
        Assert.Equal("idempotency_key_reused", category.Code);
        Assert.Equal((1, 1, 1), await CountsAsync());
        Assert.Equal(1, FilesOnDisk());
    }

    [Fact]
    public async Task Simultaneous_imports_with_one_key_make_one_document_whichever_one_wins()
    {
        // the losers must always see a repeat, never a duplicate warning: several rounds, because the interleaving differs from run to run
        for (var round = 0; round < 5; round++)
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ImportAsync(Request(Png(300 + round), key: $"race-{round}"))));
            Assert.Single(results.Select(r => r.Document.Id).Distinct());
            Assert.Equal(1, results.Count(r => !r.Replayed));
        }
        Assert.Equal((5, 5, 5), await CountsAsync());
        Assert.Equal(5, FilesOnDisk());
    }

    // ---------- failures ----------

    [Fact]
    public async Task A_failing_store_saves_nothing_and_reports_a_clear_error()
    {
        _blobs.FailStore = true;
        var e = await Refused(() => ImportAsync(Request()));
        Assert.Equal(("store_failed", 503), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
        _blobs.FailStore = false;
        await ImportAsync(Request());                                                 // and a retry then succeeds
        Assert.Equal(1, (await CountsAsync()).Documents);
    }

    [Fact]
    public async Task A_failing_log_write_fails_the_import_and_removes_the_stored_file()
    {
        await FailAuditAsync("DocumentImported");
        var e = await Refused(() => ImportAsync(Request()));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        await AssertNothingRecordedAsync();
        Assert.Equal(_blobs.Stored, _blobs.Deleted);
    }

    [Fact]
    public async Task A_failing_log_write_for_a_confirmed_duplicate_fails_that_import_too_and_changes_nothing()
    {
        var first = (await ImportAsync(Request(Png(300)))).Document;
        await FailAuditAsync("DocumentDuplicateImported");
        var e = await Refused(() => ImportAsync(Request(Png(300), confirm: first.Id)));
        Assert.Equal("save_failed", e.Code);
        Assert.Equal((1, 1, 1), await CountsAsync());
        Assert.Equal(1, FilesOnDisk());
    }

    [Fact]
    public async Task A_failure_to_remove_the_file_does_not_hide_the_original_error()
    {
        await FailAuditAsync("DocumentImported");
        _blobs.FailDelete = true;
        var e = await Refused(() => ImportAsync(Request()));
        Assert.Equal("save_failed", e.Code);                                          // the caller still learns why; the leftover file is logged for cleanup
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_cancelled_import_leaves_nothing_behind()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WithDb(db => new DocumentImportService(db, _blobs, Clock).ImportAsync(Request(), S.Actor, cts.Token)));
        await AssertNothingRecordedAsync();
    }

    // ---------- the history numbers ----------

    [Fact]
    public async Task Simultaneous_imports_get_history_numbers_with_no_gap_and_no_reuse_and_a_failed_import_does_not_leave_a_hole()
    {
        await Refused(() => ImportAsync(Request(name: "x.exe")));                      // refused: uses no number
        var imports = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ImportAsync(Request(Png(100 + i), key: $"n{i}"))));
        await FailAuditAsync("DocumentImported");
        await Refused(() => ImportAsync(Request(Png(777))));                           // failed after the document was staged: rolls back, uses no number
        await WithDb(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefuseAudit]"));
        await ImportAsync(Request(Png(888)));

        await using var db = Fixture.CreateContext();
        Assert.Equal(Enumerable.Range(1, 9).Select(i => (long)i), await db.DocumentEvents.AsNoTracking().OrderBy(e => e.EventNumber).Select(e => e.EventNumber).ToListAsync());
        Assert.Equal(9, (await db.DocumentEventCounters.AsNoTracking().SingleAsync()).LastEventNumber);
        Assert.Equal(imports.Length + 1, await db.Documents.CountAsync());
    }

    [Fact]
    public async Task A_reader_that_has_seen_a_number_has_seen_every_lower_one()
    {
        // an import that is still open holds the counter row, so a later import cannot become visible before it: numbers become visible in commit order
        await ImportAsync(Request(Png(100)));
        var tasks = Enumerable.Range(0, 6).Select(i => ImportAsync(Request(Png(200 + i), key: $"c{i}"))).ToList();
        while (!tasks.All(t => t.IsCompleted))
        {
            await using var db = Fixture.CreateContext();
            var numbers = await db.DocumentEvents.AsNoTracking().Select(e => e.EventNumber).ToListAsync();
            if (numbers.Count > 0) Assert.Equal(Enumerable.Range(1, (int)numbers.Max()).Select(i => (long)i), numbers.OrderBy(n => n));
        }
        await Task.WhenAll(tasks);
    }
}
