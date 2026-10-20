using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-010 against real SQL Server: the administrator's document settings. Categories can be added, renamed, deactivated and reactivated but never deleted, and the last active one cannot be deactivated; the
/// size limit is held between 1 MB and 1 GB and takes effect on the next import; the blocked list takes additions and removals but its built-in types are locked; every change is logged in the same save with the user
/// and time, a stale change is refused, a failed log write changes nothing, and repeating a change is quiet.
/// </summary>
public class DocumentSettingsServiceTests : DocumentTestBase
{
    private DocumentSettingsService Svc(AlveraDbContext db) => new(db, Clock);

    private Task<CategoryView> AddCategoryAsync(string? name) => WithDb(db => Svc(db).AddCategoryAsync(name, S.Actor, default));
    private Task<CategoryView> EditCategoryAsync(CategoryView c, string? name = null, bool? active = null, string? rowVersion = null) =>
        WithDb(db => Svc(db).UpdateCategoryAsync(c.Id, new CategoryEditInput(name, active, rowVersion ?? c.RowVersion), S.Actor, default));
    private Task<IReadOnlyList<CategoryView>> CategoriesAsync(bool all = true) => WithDb(db => Svc(db).ListCategoriesAsync(all, default));
    private Task<DocumentSettingsView> SettingsAsync() => WithDb(db => Svc(db).GetSettingsAsync(default));
    private Task<DocumentSettingsView> SetLimitAsync(long? bytes, string? rowVersion = null) => WithDb(async db => await Svc(db).SetMaxUploadBytesAsync(bytes, rowVersion ?? (await Svc(db).GetSettingsAsync(default)).RowVersion, S.Actor, default));
    private Task<BlockedExtensionView> AddBlockedAsync(string? ext) => WithDb(db => Svc(db).AddBlockedAsync(ext, S.Actor, default));
    private Task RemoveBlockedAsync(string? ext) => WithDb(async db => { await Svc(db).RemoveBlockedAsync(ext, S.Actor, default); return 0; });
    private async Task<List<string>> AuditTypesAsync(string prefix) { await using var db = Fixture.CreateContext(); return await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith(prefix)).OrderBy(a => a.TimestampUtc).Select(a => a.EventType).ToListAsync(); }
    private async Task<CategoryView> Seeded(Guid id) => (await CategoriesAsync()).Single(c => c.Id == id);

    // ---------- categories ----------

    [Fact]
    public async Task The_nine_starting_categories_are_listed_in_order_and_inactive_ones_only_when_asked_for()
    {
        Assert.Equal(["Radiograph", "Photo", "Referral letter", "Medical clearance", "Lab report", "Insurance card", "Identification", "Correspondence", "Other"], (await CategoriesAsync()).Select(c => c.Name));
        await EditCategoryAsync(await Seeded(DocumentCategorySeeds.Other), active: false);
        Assert.DoesNotContain(await CategoriesAsync(all: false), c => c.Id == DocumentCategorySeeds.Other);
        Assert.Contains(await CategoriesAsync(all: true), c => c.Id == DocumentCategorySeeds.Other);
    }

    [Fact]
    public async Task A_new_category_gets_a_stable_code_goes_last_and_can_be_used_for_an_import_and_is_logged()
    {
        var added = await AddCategoryAsync("  Consent  copy (signed) ");
        Assert.Equal(("Consent copy (signed)", "consent_copy_signed", true), (added.Name, added.Code, added.IsActive));
        Assert.Equal(added.Id, (await CategoriesAsync()).Last().Id);
        var doc = (await ImportAsync(Request(category: added.Id))).Document;
        Assert.Equal("consent_copy_signed", doc.CategoryCode);

        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "DocumentCategoryAdded");
        Assert.Equal((added.Id, S.Actor), (audit.TargetUserAccountId, audit.PerformedByUserAccountId));
    }

    [Fact]
    public async Task Two_names_that_make_the_same_code_still_get_different_codes()
    {
        var a = await AddCategoryAsync("Lab/Report 2");
        var b = await AddCategoryAsync("Lab Report 2!");
        Assert.NotEqual(a.Code, b.Code);
        Assert.StartsWith("lab_report_2", b.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("bad\u0001name")]
    public async Task An_unusable_category_name_is_refused(string? name)
    {
        var e = await Assert.ThrowsAsync<DocumentException>(() => AddCategoryAsync(name));
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, 400, "name"), (e.Code, e.StatusCode, e.Problems.Single().Field));
        Assert.Equal(9, (await CategoriesAsync()).Count);
    }

    [Fact]
    public async Task A_category_name_over_eighty_characters_is_refused_and_exactly_eighty_is_accepted()
    {
        await Assert.ThrowsAsync<DocumentException>(() => AddCategoryAsync(new string('n', 81)));
        await AddCategoryAsync(new string('n', 80));
    }

    [Fact]
    public async Task A_name_already_in_use_is_refused_whatever_the_case()
    {
        var e = await Assert.ThrowsAsync<DocumentException>(() => AddCategoryAsync("RADIOGRAPH"));
        Assert.Equal(("category_exists", 409), (e.Code, e.StatusCode));
        Assert.Equal(9, (await CategoriesAsync()).Count);
    }

    [Fact]
    public async Task Renaming_keeps_the_code_and_every_document_in_it_and_is_logged_and_a_name_in_use_is_refused()
    {
        var doc = (await ImportAsync(Request(category: DocumentCategorySeeds.Photo))).Document;
        var renamed = await EditCategoryAsync(await Seeded(DocumentCategorySeeds.Photo), name: "Clinical photo");
        Assert.Equal(("Clinical photo", "photo"), (renamed.Name, renamed.Code));
        Assert.Equal("Clinical photo", (await WithDb(db => new DocumentLibraryService(db, new Alveara.Api.Architecture.Forms.PatientFormReader(db), _blobs, Clock).GetAsync(doc.Id, default))).Document.CategoryName);
        Assert.Equal(["DocumentCategoryRenamed"], await AuditTypesAsync("DocumentCategoryRenamed"));

        var clash = await Assert.ThrowsAsync<DocumentException>(() => EditCategoryAsync(renamed, name: "radiograph"));
        Assert.Equal("category_exists", clash.Code);
    }

    [Fact]
    public async Task Deactivating_and_reactivating_a_category_are_logged_and_repeating_them_changes_nothing()
    {
        var other = await Seeded(DocumentCategorySeeds.Other);
        var off = await EditCategoryAsync(other, active: false);
        Assert.False(off.IsActive);
        var again = await EditCategoryAsync(off, active: false);
        Assert.Equal(off.RowVersion, again.RowVersion);
        var on = await EditCategoryAsync(off, active: true);
        Assert.True(on.IsActive);
        Assert.Equal(["DocumentCategoryDeactivated", "DocumentCategoryReactivated"], await AuditTypesAsync("DocumentCategory"));
    }

    [Fact]
    public async Task A_category_can_be_renamed_and_deactivated_in_one_change_with_both_logged()
    {
        var edited = await EditCategoryAsync(await Seeded(DocumentCategorySeeds.Other), name: "Misc", active: false);
        Assert.Equal(("Misc", false), (edited.Name, edited.IsActive));
        Assert.Equal(["DocumentCategoryDeactivated", "DocumentCategoryRenamed"], (await AuditTypesAsync("DocumentCategory")).Order());
    }

    [Fact]
    public async Task The_last_active_category_cannot_be_deactivated()
    {
        foreach (var c in (await CategoriesAsync()).Where(c => c.Id != DocumentCategorySeeds.Other)) await EditCategoryAsync(c, active: false);
        var last = await Seeded(DocumentCategorySeeds.Other);
        var e = await Assert.ThrowsAsync<DocumentException>(() => EditCategoryAsync(last, active: false));
        Assert.Equal(("last_active_category", 409), (e.Code, e.StatusCode));
        Assert.True((await Seeded(DocumentCategorySeeds.Other)).IsActive);
    }

    [Fact]
    public async Task A_stale_category_change_is_refused_a_version_is_required_and_an_unknown_category_is_not_found()
    {
        var photo = await Seeded(DocumentCategorySeeds.Photo);
        await EditCategoryAsync(photo, name: "Photograph");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => EditCategoryAsync(photo, name: "Picture"));
        Assert.Equal("Photograph", (await Seeded(DocumentCategorySeeds.Photo)).Name);
        Assert.Equal("row_version_required", (await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => Svc(db).UpdateCategoryAsync(photo.Id, new CategoryEditInput("x", null, null), S.Actor, default)))).Code);
        Assert.Equal("category_not_found", (await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => Svc(db).UpdateCategoryAsync(Guid.NewGuid(), new CategoryEditInput("x", null, photo.RowVersion), S.Actor, default)))).Code);
    }

    [Fact]
    public async Task A_failing_log_write_means_a_category_is_not_added_or_changed()
    {
        await FailAuditAsync("DocumentCategoryAdded");
        var e = await Assert.ThrowsAsync<DocumentException>(() => AddCategoryAsync("Consent"));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal(9, (await CategoriesAsync()).Count);
    }

    // ---------- the size limit ----------

    [Fact]
    public async Task The_size_limit_starts_at_one_hundred_megabytes_with_its_allowed_range()
    {
        var s = await SettingsAsync();
        Assert.Equal((104_857_600L, 1_048_576L, 1_073_741_824L), (s.MaxUploadBytes, s.MinAllowedBytes, s.MaxAllowedBytes));
    }

    [Fact]
    public async Task Changing_the_limit_is_logged_and_takes_effect_on_the_next_import()
    {
        var before = DateTimeOffset.UtcNow;
        var changed = await SetLimitAsync(DocumentFileRules.MinUploadLimitBytes);
        Assert.Equal(DocumentFileRules.MinUploadLimitBytes, changed.MaxUploadBytes);
        var big = new byte[DocumentFileRules.MinUploadLimitBytes + 1]; Png().CopyTo(big, 0);
        Assert.Equal("file_too_large", (await Refused(() => ImportAsync(Request(big, "big.png")))).Code);

        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "DocumentSizeLimitChanged");
        Assert.Equal(S.Actor, audit.PerformedByUserAccountId);
        Assert.InRange(audit.TimestampUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var row = await db.DocumentSettings.AsNoTracking().SingleAsync();
        Assert.Equal(S.Actor, row.UpdatedByUserId);
    }

    [Theory]
    [InlineData(1_048_575L)]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(1_073_741_825L)]
    public async Task A_limit_outside_one_megabyte_to_one_gigabyte_is_refused(long bytes)
    {
        var e = await Assert.ThrowsAsync<DocumentException>(() => SetLimitAsync(bytes));
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, "maxUploadBytes"), (e.Code, e.Problems.Single().Field));
        Assert.Equal(104_857_600L, (await SettingsAsync()).MaxUploadBytes);
        Assert.Empty(await AuditTypesAsync("DocumentSizeLimit"));
    }

    [Fact]
    public async Task The_limits_themselves_are_accepted_a_missing_value_is_refused_and_the_same_value_changes_nothing()
    {
        await SetLimitAsync(DocumentFileRules.MaxUploadLimitBytes);
        var low = await SetLimitAsync(DocumentFileRules.MinUploadLimitBytes);
        var same = await SetLimitAsync(DocumentFileRules.MinUploadLimitBytes);
        Assert.Equal(low.RowVersion, same.RowVersion);
        Assert.Equal(2, (await AuditTypesAsync("DocumentSizeLimit")).Count);
        Assert.Equal("metadata_invalid", (await Assert.ThrowsAsync<DocumentException>(() => SetLimitAsync(null))).Code);
    }

    [Fact]
    public async Task A_stale_limit_change_is_refused_and_a_version_is_required()
    {
        var read = await SettingsAsync();
        await SetLimitAsync(DocumentFileRules.MinUploadLimitBytes * 2);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SetLimitAsync(DocumentFileRules.MinUploadLimitBytes * 3, read.RowVersion));
        Assert.Equal(DocumentFileRules.MinUploadLimitBytes * 2, (await SettingsAsync()).MaxUploadBytes);
        Assert.Equal("row_version_required", (await Assert.ThrowsAsync<DocumentException>(() => WithDb(db => Svc(db).SetMaxUploadBytesAsync(DocumentFileRules.MinUploadLimitBytes * 4, null, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task A_failing_log_write_leaves_the_limit_as_it_was()
    {
        await FailAuditAsync("DocumentSizeLimitChanged");
        Assert.Equal("save_failed", (await Assert.ThrowsAsync<DocumentException>(() => SetLimitAsync(DocumentFileRules.MinUploadLimitBytes))).Code);
        Assert.Equal(104_857_600L, (await SettingsAsync()).MaxUploadBytes);
    }

    // ---------- the blocked list ----------

    [Fact]
    public async Task The_blocked_list_shows_the_fifteen_built_in_types_as_locked()
    {
        var list = await WithDb(db => Svc(db).ListBlockedAsync(default));
        Assert.Equal(15, list.Count);
        Assert.All(list, b => Assert.True(b.IsBuiltIn));
        Assert.Contains(list, b => b.Extension == "exe");
    }

    [Theory]
    [InlineData("swf", "swf")]
    [InlineData(".SWF ", "swf")]
    [InlineData("  .Iso", "iso")]
    public async Task An_added_type_is_normalized_blocked_for_the_next_import_and_logged_and_adding_it_again_changes_nothing(string typed, string stored)
    {
        var added = await AddBlockedAsync(typed);
        Assert.Equal((stored, false), (added.Extension, added.IsBuiltIn));
        Assert.Equal("file_type_blocked", (await Refused(() => ImportAsync(Request(name: $"thing.{stored}")))).Code);
        await AddBlockedAsync(stored);
        Assert.Equal([$"BlockedExtensionAdded"], await AuditTypesAsync("BlockedExtensionAdded"));
        Assert.Equal(16, (await WithDb(db => Svc(db).ListBlockedAsync(default))).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("pdf.exe")]
    [InlineData("tar/gz")]
    [InlineData("seventeen-letters-x")]
    public async Task An_unusable_extension_is_refused(string? ext)
    {
        var e = await Assert.ThrowsAsync<DocumentException>(() => AddBlockedAsync(ext));
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, "extension"), (e.Code, e.Problems.Single().Field));
        Assert.Equal(15, (await WithDb(db => Svc(db).ListBlockedAsync(default))).Count);
    }

    [Fact]
    public async Task Adding_a_built_in_type_again_is_quiet_and_removing_one_is_refused()
    {
        var again = await AddBlockedAsync("exe");
        Assert.True(again.IsBuiltIn);
        var e = await Assert.ThrowsAsync<DocumentException>(() => RemoveBlockedAsync("EXE"));
        Assert.Equal(("built_in_locked", 409), (e.Code, e.StatusCode));
        Assert.Equal("file_type_blocked", (await Refused(() => ImportAsync(Request(name: "setup.exe")))).Code);
        Assert.Empty(await AuditTypesAsync("BlockedExtension"));
    }

    [Fact]
    public async Task An_added_type_can_be_removed_and_is_then_accepted_and_removing_it_again_changes_nothing()
    {
        await AddBlockedAsync("swf");
        await RemoveBlockedAsync(".swf");
        await RemoveBlockedAsync("swf");
        await ImportAsync(Request(name: "movie.swf"));
        Assert.Equal(["BlockedExtensionAdded", "BlockedExtensionRemoved"], await AuditTypesAsync("BlockedExtension"));
        Assert.Equal(15, (await WithDb(db => Svc(db).ListBlockedAsync(default))).Count);
    }

    [Fact]
    public async Task Removing_an_unusable_extension_is_refused()
    {
        Assert.Equal("metadata_invalid", (await Assert.ThrowsAsync<DocumentException>(() => RemoveBlockedAsync("a b"))).Code);
    }

    [Fact]
    public async Task Simultaneous_additions_of_one_type_make_one_entry_and_one_log_entry()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AddBlockedAsync("swf")));
        Assert.All(results, r => Assert.Equal("swf", r.Extension));
        Assert.Equal(16, (await WithDb(db => Svc(db).ListBlockedAsync(default))).Count);
        Assert.Single(await AuditTypesAsync("BlockedExtensionAdded"));
    }

    [Fact]
    public async Task A_failing_log_write_means_the_blocked_list_does_not_change()
    {
        await FailAuditAsync("BlockedExtensionAdded");
        Assert.Equal("save_failed", (await Assert.ThrowsAsync<DocumentException>(() => AddBlockedAsync("swf"))).Code);
        Assert.Equal(15, (await WithDb(db => Svc(db).ListBlockedAsync(default))).Count);
    }
}
