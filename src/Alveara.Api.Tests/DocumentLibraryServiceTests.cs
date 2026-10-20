using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Forms;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-010 against real SQL Server: reading a patient's library (imported documents and signed forms together, signed forms read-only and never copied), one document with its history, opening a file (logged in the
/// same save, served from the stored copy, refused when the file is missing) and editing the category, date and description (a history row and a log entry for each kind of change, a stale edit refused, the file itself
/// untouched).
/// </summary>
public class DocumentLibraryServiceTests : DocumentTestBase
{
    private DocumentLibraryService Library(Alveara.Api.Data.AlveraDbContext db) => new(db, new PatientFormReader(db), _blobs, Clock);
    private DocumentEditService Editor(Alveara.Api.Data.AlveraDbContext db) => new(db, Clock);

    private Task<IReadOnlyList<LibraryEntry>> ListAsync(Guid? patient = null, Guid? category = null) => WithDb(db => Library(db).ListAsync(patient ?? Ann, category, default));
    private Task<DocumentDetail> GetAsync(Guid id) => WithDb(db => Library(db).GetAsync(id, default));
    private Task<OpenedDocument> OpenAsync(Guid id, Guid? actor = null) => WithDb(db => Library(db).OpenAsync(id, actor ?? S.Actor, default));
    private Task<DocumentView> EditAsync(DocumentView doc, Guid? category = null, DateOnly? date = null, string? description = null, string? rowVersion = null, bool keepDescription = false) =>
        WithDb(db => Editor(db).UpdateAsync(doc.Id, new DocumentEditInput(category ?? doc.CategoryId, date ?? doc.DocumentDate, description ?? doc.Description, rowVersion ?? doc.RowVersion), S.Actor, default));
    private async Task<DocumentView> ImportedAsync(byte[]? bytes = null, string name = "bitewing.png", Guid? patient = null, Guid? category = null, DateOnly? date = null) =>
        (await ImportAsync(Request(bytes, name, patient, category, date))).Document;
    private async Task<List<string>> KindsAsync(Guid documentId) { await using var db = Fixture.CreateContext(); return await db.DocumentEvents.AsNoTracking().Where(e => e.DocumentId == documentId).OrderBy(e => e.EventNumber).Select(e => e.Kind).ToListAsync(); }
    private async Task<List<string>> AuditTypesAsync(Guid documentId) { await using var db = Fixture.CreateContext(); return await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == documentId).OrderBy(a => a.TimestampUtc).Select(a => a.EventType).ToListAsync(); }

    // ---------- the library ----------

    [Fact]
    public async Task The_library_lists_the_patients_documents_newest_date_first_with_their_details_and_nobody_elses()
    {
        var older = await ImportedAsync(Png(100), "older.png", date: new DateOnly(2026, 1, 5), category: DocumentCategorySeeds.Photo);
        var newer = await ImportedAsync(Png(200), "newer.png", date: new DateOnly(2026, 3, 9));
        await ImportedAsync(Png(300), "bos.png", Bo);

        var list = await ListAsync();
        Assert.Equal([newer.Id, older.Id], list.Select(e => e.Id));
        var entry = list[1];
        Assert.Equal((LibraryEntry.DocumentKind, "older.png", "Photo", new DateOnly(2026, 1, 5), "png", (long?)older.SizeBytes, false, false), (entry.Kind, entry.Title, entry.CategoryName, entry.Date, entry.DetectedType, entry.SizeBytes, entry.IsVoided, entry.IsDuplicate));
        Assert.Equal(older.Id, entry.Document!.Id);
        Assert.Empty(await ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_library_can_be_filtered_by_category_and_a_duplicate_is_marked()
    {
        var xray = await ImportedAsync(Png(100));
        await ImportedAsync(Png(200), "pic.png", category: DocumentCategorySeeds.Photo);
        var copy = (await ImportAsync(Request(Png(100), "copy.png", confirm: xray.Id))).Document;

        var radiographs = await ListAsync(category: DocumentCategorySeeds.Radiograph);
        Assert.Equal(new[] { xray.Id, copy.Id }.Order(), radiographs.Select(e => e.Id).Order());
        Assert.True(radiographs.Single(e => e.Id == copy.Id).IsDuplicate);
        Assert.False(radiographs.Single(e => e.Id == xray.Id).IsDuplicate);
    }

    [Fact]
    public async Task Signed_forms_appear_in_the_library_read_only_labelled_and_flagged_when_voided_and_are_left_alone_by_the_library()
    {
        var forms = new FormTestSupport(Fixture);
        var template = await forms.CreateTemplateAsync();
        var (signed, _) = await forms.SignAsync(await forms.ReadyAsync(Ann, template.Template.Id));
        var (voided, _) = await forms.SignAsync(await forms.ReadyAsync(Ann, (await forms.CreateTemplateAsync("financial-policy", FormCategories.Financial)).Template.Id));
        await using (var db = Fixture.CreateContext()) await forms.Forms(db).VoidAsync(voided.Summary.Id, "Wrong chart", voided.RowVersion, true, forms.Actor, default);
        var doc = await ImportedAsync();
        var hashBefore = (await forms.ReloadAsync(signed.Summary.Id)).Snapshot!.SnapshotHash;

        var list = await ListAsync();
        Assert.Equal(3, list.Count);
        var entry = list.Single(e => e.Id == signed.Snapshot!.Id);
        Assert.Equal((LibraryEntry.SignedFormKind, LibraryEntry.SignedFormCategory, signed.Summary.Id, false, true), (entry.Kind, entry.CategoryName, entry.PatientFormId, entry.IsVoided, entry.Document is null));
        Assert.True(list.Single(e => e.Id == voided.Snapshot!.Id).IsVoided);
        Assert.Contains(list, e => e.Id == doc.Id);

        // listing wrote nothing and the signed version is exactly as it was
        Assert.Equal(hashBefore, (await forms.ReloadAsync(signed.Summary.Id)).Snapshot!.SnapshotHash);
        Assert.Equal(1, (await CountsAsync()).Documents);                              // the signed forms were not copied into Documents
        Assert.Equal(0, await Fixture.CreateContext().DocumentEvents.CountAsync(e => e.DocumentId == signed.Snapshot!.Id));
    }

    [Fact]
    public async Task Filtering_by_a_category_leaves_signed_forms_out()
    {
        var forms = new FormTestSupport(Fixture);
        await forms.SignAsync(await forms.ReadyAsync(Ann, (await forms.CreateTemplateAsync()).Template.Id));
        await ImportedAsync();
        Assert.Equal([LibraryEntry.DocumentKind], (await ListAsync(category: DocumentCategorySeeds.Radiograph)).Select(e => e.Kind));
    }

    // ---------- one document ----------

    [Fact]
    public async Task One_document_is_read_with_its_history_in_order_naming_who_and_when()
    {
        var doc = await ImportedAsync();
        await EditAsync(doc, category: DocumentCategorySeeds.Photo);
        var detail = await GetAsync(doc.Id);
        Assert.Equal(doc.Id, detail.Document.Id);
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.Categorized], detail.History.Select(h => h.Kind));
        Assert.Equal(detail.History.Select(h => h.EventNumber).Order(), detail.History.Select(h => h.EventNumber));
        var moved = detail.History[1];
        Assert.Equal(("Radiograph", "Photo"), (moved.FromCategoryName, moved.ToCategoryName));
        Assert.All(detail.History, h => Assert.False(string.IsNullOrWhiteSpace(h.ByName)));
    }

    [Fact]
    public async Task An_unknown_document_is_not_found()
    {
        var e = await Assert.ThrowsAsync<DocumentException>(() => GetAsync(Guid.NewGuid()));
        Assert.Equal(("document_not_found", 404), (e.Code, e.StatusCode));
    }

    // ---------- opening ----------

    [Fact]
    public async Task Opening_a_file_returns_the_stored_bytes_with_the_servers_own_type_and_logs_the_open_in_the_history_and_the_log()
    {
        var bytes = Png(400);
        var doc = await ImportedAsync(bytes, "ann-okafor.png");
        var before = DateTimeOffset.UtcNow;
        await using var opened = await OpenAsync(doc.Id);
        var ms = new MemoryStream(); await opened.Content.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
        Assert.Equal(("ann-okafor.png", "image/png", true, (long)bytes.Length), (opened.FileName, opened.ContentType, opened.InlineSafe, opened.SizeBytes));

        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.Opened], await KindsAsync(doc.Id));
        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "DocumentOpened");
        Assert.Equal((S.Actor, doc.Id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        Assert.InRange(audit.TimestampUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.DoesNotContain("okafor", audit.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var ev = await db.DocumentEvents.AsNoTracking().SingleAsync(e => e.Kind == DocumentEventKinds.Opened);
        Assert.Equal((S.Actor, Ann), (ev.ByUserId, ev.PatientId));
    }

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("tiff", false)]
    [InlineData("other", false)]
    public async Task Only_pdf_png_and_jpeg_may_be_shown_in_the_browser_and_every_other_file_is_a_download(string type, bool inline)
    {
        byte[] bytes = type switch { "pdf" => Pdf(), "tiff" => [(byte)'I', (byte)'I', 0x2A, 0x00, 1, 2, 3, 4], _ => "just text"u8.ToArray() };
        var doc = await ImportedAsync(bytes, type == "other" ? "notes.txt" : $"scan.{type}");
        await using var opened = await OpenAsync(doc.Id);
        Assert.Equal(inline, opened.InlineSafe);
        if (type == "other") Assert.Equal("application/octet-stream", opened.ContentType);
    }

    [Fact]
    public async Task Opening_when_the_stored_file_is_missing_says_so_and_logs_no_open()
    {
        var doc = await ImportedAsync();
        foreach (var f in Directory.GetFiles(_storageRoot)) File.Delete(f);
        var e = await Assert.ThrowsAsync<DocumentException>(() => OpenAsync(doc.Id));
        Assert.Equal("file_missing", e.Code);
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
    }

    [Fact]
    public async Task A_failing_log_write_means_the_file_is_not_opened_and_nothing_is_recorded()
    {
        var doc = await ImportedAsync();
        await FailAuditAsync("DocumentOpened");
        var e = await Assert.ThrowsAsync<DocumentException>(() => OpenAsync(doc.Id));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
    }

    // ---------- editing ----------

    [Fact]
    public async Task Recategorizing_changes_the_category_keeps_the_old_one_in_the_history_and_logs_it_and_leaves_the_file_untouched()
    {
        var doc = await ImportedAsync(Png(300));
        var edited = await EditAsync(doc, category: DocumentCategorySeeds.ReferralLetter, keepDescription: true);
        Assert.Equal((DocumentCategorySeeds.ReferralLetter, "Referral letter"), (edited.CategoryId, edited.CategoryName));
        Assert.NotEqual(doc.RowVersion, edited.RowVersion);
        Assert.Equal((doc.Sha256, doc.OriginalFileName, doc.SizeBytes, doc.ImportedAtUtc, doc.Description), (edited.Sha256, edited.OriginalFileName, edited.SizeBytes, edited.ImportedAtUtc, edited.Description));

        await using var db = Fixture.CreateContext();
        var ev = await db.DocumentEvents.AsNoTracking().SingleAsync(e => e.Kind == DocumentEventKinds.Categorized);
        Assert.Equal((DocumentCategorySeeds.Radiograph, DocumentCategorySeeds.ReferralLetter, S.Actor), (ev.FromCategoryId, ev.ToCategoryId, ev.ByUserId));
        Assert.Equal(["DocumentImported", "DocumentCategorized"], await AuditTypesAsync(doc.Id));
        Assert.DoesNotContain(doc.Description!, (await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "DocumentCategorized").Select(a => a.Details).SingleAsync()) ?? string.Empty);
    }

    [Fact]
    public async Task Editing_the_date_and_description_is_logged_as_a_metadata_edit()
    {
        var doc = await ImportedAsync();
        var edited = await EditAsync(doc, date: new DateOnly(2026, 2, 2), description: "  Retaken.  ");
        Assert.Equal((new DateOnly(2026, 2, 2), "Retaken.", doc.CategoryId), (edited.DocumentDate, edited.Description, edited.CategoryId));
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.MetadataEdited], await KindsAsync(doc.Id));
        Assert.Equal(["DocumentImported", "DocumentMetadataEdited"], await AuditTypesAsync(doc.Id));
    }

    [Fact]
    public async Task Changing_the_category_and_the_details_together_writes_both_history_rows_and_both_log_entries_in_order()
    {
        var doc = await ImportedAsync();
        await EditAsync(doc, category: DocumentCategorySeeds.Photo, date: new DateOnly(2026, 2, 2), description: "Both.");
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.Categorized, DocumentEventKinds.MetadataEdited], await KindsAsync(doc.Id));
        Assert.Equal(["DocumentImported", "DocumentCategorized", "DocumentMetadataEdited"], await AuditTypesAsync(doc.Id));
        await using var db = Fixture.CreateContext();
        Assert.Equal(Enumerable.Range(1, 3).Select(i => (long)i), await db.DocumentEvents.AsNoTracking().OrderBy(e => e.EventNumber).Select(e => e.EventNumber).ToListAsync());
    }

    [Fact]
    public async Task Saving_without_changing_anything_writes_nothing()
    {
        var doc = await ImportedAsync();
        var same = await EditAsync(doc, keepDescription: true);
        Assert.Equal(doc.RowVersion, same.RowVersion);
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
        Assert.Equal(["DocumentImported"], await AuditTypesAsync(doc.Id));
    }

    [Fact]
    public async Task A_wrong_edit_names_each_field_and_changes_nothing()
    {
        var doc = await ImportedAsync();
        var e = await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => Editor(db).UpdateAsync(doc.Id,
            new DocumentEditInput(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), new string('x', 501), doc.RowVersion), S.Actor, default)));
        Assert.Equal(DocumentErrorCodes.MetadataInvalid, e.Code);
        Assert.Equal(["category", "description", "documentDate"], e.Problems.Select(p => p.Field).Order());
        Assert.Equal(doc.RowVersion, (await GetAsync(doc.Id)).Document.RowVersion);
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
    }

    [Fact]
    public async Task A_deactivated_category_cannot_be_chosen_but_a_document_that_already_has_it_can_still_be_edited()
    {
        var doc = await ImportedAsync(category: DocumentCategorySeeds.Other);
        await WithDb(db => db.Database.ExecuteSqlRawAsync($"UPDATE [DocumentCategories] SET [IsActive] = 0 WHERE [Id] = '{DocumentCategorySeeds.Other}'"));
        var kept = await EditAsync(doc, description: "Still fine.");
        Assert.Equal(DocumentCategorySeeds.Other, kept.CategoryId);
        var photo = await ImportedAsync(Png(999), "p.png");
        var e = await Assert.ThrowsAsync<DocumentException>(() => EditAsync(photo, category: DocumentCategorySeeds.Other));
        Assert.Equal("inactive", e.Problems.Single().Code);
    }

    [Fact]
    public async Task A_stale_edit_is_refused_and_changes_nothing_and_a_missing_version_is_required()
    {
        var doc = await ImportedAsync();
        await EditAsync(doc, description: "First change.");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => EditAsync(doc, description: "Second change from the old screen."));
        Assert.Equal("First change.", (await GetAsync(doc.Id)).Document.Description);

        var none = await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => Editor(db).UpdateAsync(doc.Id, new DocumentEditInput(doc.CategoryId, doc.DocumentDate, "x", null), S.Actor, default)));
        Assert.Equal(("row_version_required", 400), (none.Code, none.StatusCode));
        var bad = await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => Editor(db).UpdateAsync(doc.Id, new DocumentEditInput(doc.CategoryId, doc.DocumentDate, "x", "not base64!"), S.Actor, default)));
        Assert.Equal("row_version_invalid", bad.Code);
        var missing = await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => Editor(db).UpdateAsync(Guid.NewGuid(), new DocumentEditInput(doc.CategoryId, doc.DocumentDate, "x", doc.RowVersion), S.Actor, default)));
        Assert.Equal("document_not_found", missing.Code);
    }

    [Fact]
    public async Task Two_simultaneous_edits_of_one_version_let_exactly_one_win()
    {
        var doc = await ImportedAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => EditAsync(doc, description: $"Edit {i}").ContinueWith(t => t.IsCompletedSuccessfully)));
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(2, (await KindsAsync(doc.Id)).Count);                              // the import and the one edit
    }

    [Fact]
    public async Task A_failing_log_write_fails_the_edit_and_changes_nothing()
    {
        var doc = await ImportedAsync();
        await FailAuditAsync("DocumentCategorized");
        var e = await Assert.ThrowsAsync<DocumentException>(() => EditAsync(doc, category: DocumentCategorySeeds.Photo));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        var after = (await GetAsync(doc.Id)).Document;
        Assert.Equal((DocumentCategorySeeds.Radiograph, doc.RowVersion), (after.CategoryId, after.RowVersion));
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
    }
}
