using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01 against real SQL Server: finding documents in a patient's library by category, encounter, tooth or region, date range, status and a word of the file name or description; the filters combine, a wrong
/// value is named and refused rather than ignored, text is matched literally (a percent sign is a percent sign), signed forms appear only when nothing narrows to a document's own properties, and the thumbnail of a
/// PNG or JPEG is served without writing an open to the history.
/// </summary>
public class DocumentLibraryFilterTests : DocumentTestBase
{
    private DocumentLibraryService Library(AlveraDbContext db) => new(db, new PatientFormReader(db), _blobs, Clock);
    private Task<IReadOnlyList<LibraryEntry>> ListAsync(DocumentLibraryFilter filter, Guid? patient = null) => WithDb(db => Library(db).ListAsync(patient ?? Ann, filter, default));

    private async Task<Guid> EncounterAsync()
    {
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = Ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        await WithDb(async db => { db.Encounters.Add(e); await db.SaveChangesAsync(); return 0; });
        return e.Id;
    }

    private async Task<DocumentView> ImportedAsync(string name, int salt, Guid? category = null, DateOnly? date = null, string? description = null, Guid? encounter = null, string? tooth = null, string? region = null) =>
        (await ImportAsync(Request(Png(salt), name, category: category, date: date, description: description) with { EncounterId = encounter, ToothKey = tooth, RegionKey = region })).Document;

    private static readonly DateOnly D1 = new(2026, 3, 1);
    private static readonly DateOnly D2 = new(2026, 6, 15);
    private static readonly DateOnly D3 = new(2026, 9, 30);

    /// <summary>Six documents that differ in each property, so each filter picks a different set.</summary>
    private async Task<(Dictionary<string, Guid> Ids, Guid Encounter)> SeedAsync()
    {
        var encounter = await EncounterAsync();
        var ids = new Dictionary<string, Guid>
        {
            ["xray16"] = (await ImportedAsync("xray-upper-molar.png", 1, DocumentCategorySeeds.Radiograph, D1, "Bitewing left", encounter, "16")).Id,
            ["xray26"] = (await ImportedAsync("xray-26.png", 2, DocumentCategorySeeds.Radiograph, D2, "Periapical", encounter, "26")).Id,
            ["photo"] = (await ImportedAsync("smile-photo.png", 3, DocumentCategorySeeds.Photo, D3, "Front view", null, null, "FullMouth")).Id,
            ["letter"] = (await ImportedAsync("referral-letter.png", 4, DocumentCategorySeeds.ReferralLetter, D2, "To the periodontist", null, null, "UpperLeft")).Id,
            ["plain"] = (await ImportedAsync("scan-of-id.png", 5, DocumentCategorySeeds.Identification, D1, null)).Id,
        };
        var gone = await ImportedAsync("old-scan.png", 6, DocumentCategorySeeds.Radiograph, D1, "Wrong patient");
        await WithDb(async db => { await new DocumentStatusService(db, Clock).InactivateAsync(gone.Id, gone.RowVersion, "Wrong patient.", S.Actor, default); return 0; });
        ids["inactive"] = gone.Id;
        return (ids, encounter);
    }

    private static List<Guid> Of(IEnumerable<LibraryEntry> entries) => entries.Where(e => e.Kind == LibraryEntry.DocumentKind).Select(e => e.Id).Order().ToList();
    private static List<Guid> Ids(Dictionary<string, Guid> all, params string[] names) => names.Select(n => all[n]).Order().ToList();

    // ---------- each filter ----------

    [Fact]
    public async Task No_filter_shows_the_active_documents_and_leaves_out_the_inactive_one()
    {
        var (ids, _) = await SeedAsync();
        Assert.Equal(Ids(ids, "xray16", "xray26", "photo", "letter", "plain"), Of(await ListAsync(new DocumentLibraryFilter())));
    }

    [Fact]
    public async Task Each_filter_picks_exactly_its_documents()
    {
        var (ids, encounter) = await SeedAsync();
        Assert.Equal(Ids(ids, "xray16", "xray26"), Of(await ListAsync(new DocumentLibraryFilter(CategoryId: DocumentCategorySeeds.Radiograph))));
        Assert.Equal(Ids(ids, "xray16", "xray26"), Of(await ListAsync(new DocumentLibraryFilter(EncounterId: encounter))));
        Assert.Equal(Ids(ids, "xray16"), Of(await ListAsync(new DocumentLibraryFilter(ToothKey: "16"))));
        Assert.Equal(Ids(ids, "letter"), Of(await ListAsync(new DocumentLibraryFilter(RegionKey: "UpperLeft"))));
        Assert.Equal(Ids(ids, "xray26", "letter"), Of(await ListAsync(new DocumentLibraryFilter(From: D2, To: D2))));
        Assert.Equal(Ids(ids, "photo"), Of(await ListAsync(new DocumentLibraryFilter(From: new DateOnly(2026, 9, 1)))));
        Assert.Equal(Ids(ids, "xray16", "plain"), Of(await ListAsync(new DocumentLibraryFilter(To: new DateOnly(2026, 3, 31)))));
    }

    [Fact]
    public async Task The_date_range_includes_both_of_its_ends()
    {
        var (ids, _) = await SeedAsync();
        Assert.Equal(Ids(ids, "xray16", "plain", "xray26", "letter"), Of(await ListAsync(new DocumentLibraryFilter(From: D1, To: D2))));
        Assert.Empty(Of(await ListAsync(new DocumentLibraryFilter(From: D1.AddDays(1), To: D2.AddDays(-1)))));
    }

    [Theory]
    [InlineData("molar", "xray16")]
    [InlineData("MOLAR", "xray16")]
    [InlineData("periodontist", "letter")]
    [InlineData("front view", "photo")]
    [InlineData(".png", "xray16,xray26,photo,letter,plain")]
    public async Task A_word_is_matched_in_the_file_name_or_the_description_whatever_the_case(string text, string expected)
    {
        var (ids, _) = await SeedAsync();
        Assert.Equal(Ids(ids, expected.Split(',')), Of(await ListAsync(new DocumentLibraryFilter(Text: text))));
    }

    [Fact]
    public async Task Text_is_matched_literally_so_a_percent_sign_or_underscore_is_not_a_wildcard()
    {
        await ImportedAsync("discount_50%.png", 11, description: "50% off");
        await ImportedAsync("discountX50Y.png", 12, description: "plain");
        Assert.Equal(["discount_50%.png"], (await ListAsync(new DocumentLibraryFilter(Text: "_50%"))).Select(e => e.Title));
        Assert.Equal(2, (await ListAsync(new DocumentLibraryFilter(Text: "discount"))).Count);
        Assert.Single(await ListAsync(new DocumentLibraryFilter(Text: "%")));                                         // only the name that really contains a percent sign
    }

    [Fact]
    public async Task The_status_filter_shows_active_inactive_or_all_and_a_legacy_include_inactive_listing_still_works()
    {
        var (ids, _) = await SeedAsync();
        Assert.Equal(Ids(ids, "inactive"), Of(await ListAsync(new DocumentLibraryFilter(Status: "Inactive"))));
        Assert.Equal(6, Of(await ListAsync(new DocumentLibraryFilter(Status: "All"))).Count);
        Assert.Equal(5, Of(await ListAsync(new DocumentLibraryFilter(Status: "Active"))).Count);
        Assert.Equal(6, (await WithDb(db => Library(db).ListAsync(Ann, null, default, includeInactive: true))).Count);
    }

    [Fact]
    public async Task Filters_combine_so_a_document_must_match_all_of_them()
    {
        var (ids, encounter) = await SeedAsync();
        Assert.Equal(Ids(ids, "xray26"), Of(await ListAsync(new DocumentLibraryFilter(CategoryId: DocumentCategorySeeds.Radiograph, EncounterId: encounter, From: D2))));
        Assert.Empty(Of(await ListAsync(new DocumentLibraryFilter(CategoryId: DocumentCategorySeeds.Photo, ToothKey: "16"))));
        Assert.Equal(Ids(ids, "xray16"), Of(await ListAsync(new DocumentLibraryFilter(Text: "xray", To: D1))));
    }

    [Fact]
    public async Task One_patients_filter_never_shows_another_patients_documents()
    {
        await SeedAsync();
        await ImportedAsync("bos-xray.png", 99);                                                                    // Ann's, for contrast
        var bos = (await ImportAsync(Request(Png(77), "bos-xray.png", patient: Bo))).Document;
        Assert.DoesNotContain(bos.Id, Of(await ListAsync(new DocumentLibraryFilter(Text: "xray"))));
        Assert.Equal([bos.Id], Of(await ListAsync(new DocumentLibraryFilter(Text: "xray"), Bo)));
    }

    // ---------- signed forms ----------

    [Fact]
    public async Task Signed_forms_appear_unless_a_filter_narrows_to_a_documents_own_properties_and_the_date_and_text_filters_apply_to_them_too()
    {
        var forms = new FormTestSupport(Fixture);
        var template = await forms.CreateTemplateAsync();
        var (signed, _) = await forms.SignAsync(await forms.ReadyAsync(Ann, template.Template.Id));
        await ImportedAsync("xray.png", 1, DocumentCategorySeeds.Radiograph, D1, "Bitewing", null, "16");
        var today = DateOnly.FromDateTime(Clock.ToPracticeLocal(Clock.UtcNow).DateTime);

        Assert.Contains(await ListAsync(new DocumentLibraryFilter()), e => e.Id == signed.Snapshot!.Id);
        Assert.Contains(await ListAsync(new DocumentLibraryFilter(Text: "privacy")), e => e.Id == signed.Snapshot!.Id);               // by the form's title
        Assert.DoesNotContain(await ListAsync(new DocumentLibraryFilter(Text: "bitewing")), e => e.Kind == LibraryEntry.SignedFormKind);
        Assert.Contains(await ListAsync(new DocumentLibraryFilter(From: today)), e => e.Id == signed.Snapshot!.Id);
        Assert.DoesNotContain(await ListAsync(new DocumentLibraryFilter(To: D1)), e => e.Kind == LibraryEntry.SignedFormKind);
        foreach (var narrowing in new[]
        {
            new DocumentLibraryFilter(CategoryId: DocumentCategorySeeds.Radiograph), new DocumentLibraryFilter(EncounterId: Guid.NewGuid()), new DocumentLibraryFilter(ToothKey: "16"),
            new DocumentLibraryFilter(RegionKey: "Tmj"), new DocumentLibraryFilter(Status: "Inactive"),
        })
            Assert.DoesNotContain(await ListAsync(narrowing), e => e.Kind == LibraryEntry.SignedFormKind);
    }

    // ---------- wrong values ----------

    public static TheoryData<string, string> WrongFilters => new()
    {
        { "toothKey", "99" }, { "regionKey", "Nose" }, { "regionKey", "with-tooth" }, { "range", "from-after-to" }, { "text", "too-long" }, { "status", "Deleted" },
    };

    [Theory]
    [MemberData(nameof(WrongFilters))]
    public async Task A_wrong_filter_is_named_and_refused_not_silently_ignored(string field, string problem)
    {
        var filter = problem switch
        {
            "99" => new DocumentLibraryFilter(ToothKey: "99"),
            "Nose" => new DocumentLibraryFilter(RegionKey: "Nose"),
            "with-tooth" => new DocumentLibraryFilter(ToothKey: "16", RegionKey: "Tmj"),
            "from-after-to" => new DocumentLibraryFilter(From: D3, To: D1),
            "too-long" => new DocumentLibraryFilter(Text: new string('t', DocumentLibraryFilter.TextMax + 1)),
            _ => new DocumentLibraryFilter(Status: "Deleted"),
        };
        var e = await Assert.ThrowsAsync<DocumentException>(() => ListAsync(filter));
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, 400), (e.Code, e.StatusCode));
        Assert.Equal(field == "range" ? "from" : field, e.Problems.Single().Field);
    }

    [Fact]
    public async Task A_text_of_exactly_one_hundred_characters_is_accepted()
    {
        await SeedAsync();
        Assert.Empty(await ListAsync(new DocumentLibraryFilter(Text: new string('t', DocumentLibraryFilter.TextMax))));
    }

    // ---------- thumbnails ----------

    private Task<OpenedDocument> ThumbAsync(Guid id) => WithDb(db => Library(db).OpenThumbnailAsync(id, default));
    private async Task<int> OpensAsync() { await using var db = Fixture.CreateContext(); return await db.DocumentEvents.CountAsync(e => e.Kind == DocumentEventKinds.Opened); }

    [Fact]
    public async Task A_png_and_a_jpeg_have_a_thumbnail_that_is_the_stored_picture_and_serving_it_writes_no_open()
    {
        var png = (await ImportAsync(Request(Png(300), "a.png"))).Document;
        var jpeg = (await ImportAsync(Request([0xFF, 0xD8, 0xFF, .. Enumerable.Repeat((byte)1, 200)], "b.jpg"))).Document;
        await using (var a = await ThumbAsync(png.Id)) { Assert.Equal(("image/png", true, (long)png.SizeBytes), (a.ContentType, a.InlineSafe, a.SizeBytes)); }
        await using (var b = await ThumbAsync(jpeg.Id)) { Assert.Equal("image/jpeg", b.ContentType); }
        Assert.Equal(0, await OpensAsync());
        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(a => a.EventType == "DocumentOpened"));
    }

    [Fact]
    public async Task Other_types_and_pictures_over_five_megabytes_have_no_thumbnail_and_the_reason_is_a_code_not_a_guess()
    {
        var pdf = (await ImportAsync(Request([.. "%PDF-1.7\n"u8.ToArray(), .. Enumerable.Repeat((byte)'x', 50)], "a.pdf", category: DocumentCategorySeeds.LabReport))).Document;
        var text = (await ImportAsync(Request("notes"u8.ToArray(), "n.txt"))).Document;
        var tiff = (await ImportAsync(Request([(byte)'I', (byte)'I', 0x2A, 0x00, 1, 2, 3, 4], "t.tif"))).Document;
        foreach (var d in new[] { pdf, text, tiff })
        {
            var e = await Assert.ThrowsAsync<DocumentException>(() => ThumbAsync(d.Id));
            Assert.Equal(("not_an_image", 415), (e.Code, e.StatusCode));
        }
        await WithDb(db => db.Database.ExecuteSqlRawAsync($"UPDATE [DocumentSettings] SET [MaxUploadBytes] = {DocumentFileRules.MaxUploadLimitBytes}"));
        var big = new byte[5 * 1024 * 1024 + 1]; Png().CopyTo(big, 0);
        var large = (await ImportAsync(Request(big, "big.png"))).Document;
        var tooBig = await Assert.ThrowsAsync<DocumentException>(() => ThumbAsync(large.Id));
        Assert.Equal(("thumbnail_too_large", 413), (tooBig.Code, tooBig.StatusCode));
        Assert.Equal(0, await OpensAsync());
    }

    [Fact]
    public async Task A_missing_or_damaged_picture_has_no_thumbnail_and_an_unknown_document_is_not_found()
    {
        var gone = (await ImportAsync(Request(Png(100), "g.png"))).Document;
        var cut = (await ImportAsync(Request(Png(200), "c.png"))).Document;
        await using (var db = Fixture.CreateContext())
        {
            var blobs = await db.Documents.AsNoTracking().Where(d => d.Id == gone.Id || d.Id == cut.Id).ToDictionaryAsync(d => d.Id, d => d.BlobId);
            File.Delete(Path.Combine(_storageRoot, blobs[gone.Id].ToString("N")));
            var cutPath = Path.Combine(_storageRoot, blobs[cut.Id].ToString("N"));
            File.WriteAllBytes(cutPath, File.ReadAllBytes(cutPath)[..30]);
        }
        Assert.Equal("file_missing", (await Assert.ThrowsAsync<DocumentException>(() => ThumbAsync(gone.Id))).Code);
        Assert.Equal("file_corrupt", (await Assert.ThrowsAsync<DocumentException>(() => ThumbAsync(cut.Id))).Code);
        Assert.Equal("document_not_found", (await Assert.ThrowsAsync<DocumentException>(() => ThumbAsync(Guid.NewGuid()))).Code);
    }

    [Fact]
    public async Task An_inactive_pictures_thumbnail_is_still_served_because_it_is_hidden_not_removed()
    {
        var doc = (await ImportAsync(Request(Png(100), "i.png"))).Document;
        await WithDb(async db => { await new DocumentStatusService(db, Clock).InactivateAsync(doc.Id, doc.RowVersion, "Wrong patient.", S.Actor, default); return 0; });
        await using var thumb = await ThumbAsync(doc.Id);
        Assert.True(thumb.SizeBytes > 0);
    }
}
