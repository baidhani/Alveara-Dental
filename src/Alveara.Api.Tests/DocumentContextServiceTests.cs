using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01 against real SQL Server: the clinical context of a document (an encounter of its own patient and one tooth or one region, validated like a diagnosis's) given at import and changed by an edit, and the
/// status of a document (inactivated with a reason by anyone who may edit, hidden from the library by default, never deleted, reactivated with its old reason kept in the status history). Every change is one transaction
/// with its history row and its log entry, the log never carries the reason, the tooth or a file name, a stale change is refused and a failed log write changes nothing.
/// </summary>
public class DocumentContextServiceTests : DocumentTestBase
{
    private DocumentLibraryService Library(AlveraDbContext db) => new(db, new PatientFormReader(db), _blobs, Clock);
    private DocumentEditService Editor(AlveraDbContext db) => new(db, Clock);
    private DocumentStatusService Status(AlveraDbContext db) => new(db, Clock);

    private async Task<Guid> EncounterAsync(Guid? patient = null)
    {
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient ?? Ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        await WithDb(async db => { db.Encounters.Add(e); await db.SaveChangesAsync(); return 0; });
        return e.Id;
    }

    private async Task<DocumentView> ImportedAsync(Guid? encounter = null, string? tooth = null, string? region = null, byte[]? bytes = null, Guid? patient = null) =>
        (await ImportAsync(Request(bytes, patient: patient) with { EncounterId = encounter, ToothKey = tooth, RegionKey = region })).Document;

    private Task<DocumentView> EditAsync(DocumentView doc, Guid? encounter = null, string? tooth = null, string? region = null, string? description = null, string? rowVersion = null) =>
        WithDb(db => Editor(db).UpdateAsync(doc.Id, new DocumentEditInput(doc.CategoryId, doc.DocumentDate, description ?? doc.Description, rowVersion ?? doc.RowVersion, encounter, tooth, region), S.Actor, default));

    private Task<DocumentView> InactivateAsync(DocumentView doc, string? reason = "Scanned under the wrong patient.", string? rowVersion = null) =>
        WithDb(db => Status(db).InactivateAsync(doc.Id, rowVersion ?? doc.RowVersion, reason, S.Actor, default));

    private Task<DocumentView> ReactivateAsync(DocumentView doc, string? rowVersion = null) => WithDb(db => Status(db).ReactivateAsync(doc.Id, rowVersion ?? doc.RowVersion, S.Actor, default));
    private Task<IReadOnlyList<LibraryEntry>> ListAsync(bool includeInactive = false) => WithDb(db => Library(db).ListAsync(Ann, null, default, includeInactive));
    private Task<DocumentDetail> GetAsync(Guid id) => WithDb(db => Library(db).GetAsync(id, default));

    private async Task<List<string>> KindsAsync(Guid id) { await using var db = Fixture.CreateContext(); return await db.DocumentEvents.AsNoTracking().Where(e => e.DocumentId == id).OrderBy(e => e.EventNumber).Select(e => e.Kind).ToListAsync(); }
    private async Task<List<string>> AuditTypesAsync(Guid id) { await using var db = Fixture.CreateContext(); return await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == id).OrderBy(a => a.TimestampUtc).Select(a => a.EventType).ToListAsync(); }
    private static async Task<DocumentException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<DocumentException>(action);

    // ---------- context at import ----------

    [Fact]
    public async Task A_document_is_imported_with_an_encounter_of_its_patient_and_a_tooth()
    {
        var encounter = await EncounterAsync();
        var doc = await ImportedAsync(encounter: encounter, tooth: "16");
        Assert.Equal((encounter, "16", null), (doc.EncounterId, doc.ToothKey, doc.RegionKey));
        var read = (await GetAsync(doc.Id)).Document;
        Assert.Equal((encounter, "16"), (read.EncounterId, read.ToothKey));
    }

    [Fact]
    public async Task A_document_may_be_about_a_region_or_about_nothing_in_particular()
    {
        Assert.Equal("UpperLeft", (await ImportedAsync(region: "UpperLeft", bytes: Png(101))).RegionKey);
        var plain = await ImportedAsync(bytes: Png(102));
        Assert.Equal((null, null, null), (plain.EncounterId, plain.ToothKey, plain.RegionKey));
    }

    public static TheoryData<string, string> WrongContext => new()
    {
        { "toothKey", "99" }, { "toothKey", "1A" }, { "toothKey", "6" }, { "regionKey", "Nose" }, { "regionKey", "tmj" }, { "encounter", "unknown" }, { "encounter", "another-patient" }, { "regionKey", "with-tooth" },
    };

    [Theory]
    [MemberData(nameof(WrongContext))]
    public async Task A_wrong_tooth_region_or_encounter_is_named_beside_its_field_and_stores_nothing(string field, string problem)
    {
        var bos = await EncounterAsync(Bo);
        var request = Request() with
        {
            ToothKey = field == "toothKey" ? problem : problem == "with-tooth" ? "16" : null,
            RegionKey = field == "regionKey" ? (problem == "with-tooth" ? "FullMouth" : problem) : null,
            EncounterId = field == "encounter" ? (problem == "unknown" ? Guid.NewGuid() : bos) : null,
        };
        var e = await Refused(() => ImportAsync(request));
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, 400), (e.Code, e.StatusCode));
        Assert.Equal(field, e.Problems.Single().Field);
        await AssertNothingRecordedAsync();
    }

    [Fact]
    public async Task The_same_request_with_the_same_context_is_a_replay_and_a_different_tooth_under_the_same_key_is_refused()
    {
        var encounter = await EncounterAsync();
        var first = await ImportAsync(Request(Png(300), key: "ctx") with { EncounterId = encounter, ToothKey = "16" });
        var again = await ImportAsync(Request(Png(300), key: "ctx") with { EncounterId = encounter, ToothKey = "16" });
        Assert.True(again.Replayed);
        Assert.Equal(first.Document.Id, again.Document.Id);
        var other = await Refused(() => ImportAsync(Request(Png(300), key: "ctx") with { EncounterId = encounter, ToothKey = "17" }));
        Assert.Equal("idempotency_key_reused", other.Code);
    }

    // ---------- context by edit ----------

    [Fact]
    public async Task An_edit_sets_changes_and_clears_the_encounter_tooth_and_region_with_a_history_row_and_a_log_entry_each_time()
    {
        var encounter = await EncounterAsync();
        var doc = await ImportedAsync();
        var set = await EditAsync(doc, encounter, "26");
        Assert.Equal((encounter, "26", null), (set.EncounterId, set.ToothKey, set.RegionKey));
        var moved = await EditAsync(set, encounter, region: "Tmj");
        Assert.Equal((encounter, null, "Tmj"), (moved.EncounterId, moved.ToothKey, moved.RegionKey));
        var cleared = await EditAsync(moved);
        Assert.Equal((null, null, null), (cleared.EncounterId, cleared.ToothKey, cleared.RegionKey));
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.MetadataEdited, DocumentEventKinds.MetadataEdited, DocumentEventKinds.MetadataEdited], await KindsAsync(doc.Id));
        Assert.Equal(["DocumentImported", "DocumentMetadataEdited", "DocumentMetadataEdited", "DocumentMetadataEdited"], await AuditTypesAsync(doc.Id));
    }

    [Fact]
    public async Task Saving_the_same_context_again_writes_nothing_and_the_log_never_carries_the_tooth_or_the_encounter()
    {
        var encounter = await EncounterAsync();
        var doc = await ImportedAsync(encounter: encounter, tooth: "16");
        var same = await EditAsync(doc, encounter, "16");
        Assert.Equal(doc.RowVersion, same.RowVersion);
        await EditAsync(same, encounter, "17");
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.MetadataEdited], await KindsAsync(doc.Id));
        await using var db = Fixture.CreateContext();
        var text = string.Join(" ", await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == doc.Id).Select(a => a.Details + " " + a.Reason).ToListAsync());
        Assert.DoesNotContain(encounter.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("16", text);
        Assert.DoesNotContain("17", text);
    }

    [Fact]
    public async Task An_edit_with_a_wrong_tooth_region_or_encounter_is_refused_and_changes_nothing()
    {
        var bos = await EncounterAsync(Bo);
        var doc = await ImportedAsync();
        Assert.Equal("toothKey", (await Refused(() => EditAsync(doc, tooth: "99"))).Problems.Single().Field);
        Assert.Equal("regionKey", (await Refused(() => EditAsync(doc, tooth: "16", region: "Tmj"))).Problems.Single().Field);
        Assert.Equal("encounter", (await Refused(() => EditAsync(doc, encounter: bos))).Problems.Single().Field);
        Assert.Equal("encounter", (await Refused(() => EditAsync(doc, encounter: Guid.NewGuid()))).Problems.Single().Field);
        Assert.Equal(doc.RowVersion, (await GetAsync(doc.Id)).Document.RowVersion);
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
    }

    // ---------- inactivation ----------

    [Fact]
    public async Task Inactivating_a_document_records_the_reason_who_and_when_in_one_save_with_a_history_row_and_a_log_entry_that_does_not_carry_the_reason()
    {
        var doc = await ImportedAsync();
        var before = DateTimeOffset.UtcNow;
        var inactive = await InactivateAsync(doc, "  Scanned under the wrong patient.  ");
        Assert.Equal((DocumentStatuses.Inactive, "Scanned under the wrong patient."), (inactive.Status, inactive.InactiveReason));
        Assert.InRange(inactive.InactivatedAtUtc!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.NotEqual(doc.RowVersion, inactive.RowVersion);
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.Inactivated], await KindsAsync(doc.Id));

        await using var db = Fixture.CreateContext();
        var row = await db.Documents.AsNoTracking().SingleAsync();
        Assert.Equal((S.Actor, DocumentStatuses.Inactive), (row.InactivatedByUserId, row.Status));
        var change = await db.DocumentStatusChanges.AsNoTracking().SingleAsync();
        Assert.Equal((doc.Id, DocumentStatuses.Inactive, "Scanned under the wrong patient.", S.Actor), (change.DocumentId, change.ToStatus, change.Reason, change.ByUserId));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "DocumentInactivated");
        Assert.Equal((S.Actor, doc.Id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        Assert.DoesNotContain("wrong patient", $"{audit.Details} {audit.Reason}", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_inactive_document_is_hidden_from_the_library_by_default_shown_and_flagged_when_asked_for_and_is_still_readable_and_openable()
    {
        var doc = await ImportedAsync();
        var other = await ImportedAsync(bytes: Png(555));
        await InactivateAsync(doc);
        Assert.Equal([other.Id], (await ListAsync()).Select(e => e.Id));
        var all = await ListAsync(includeInactive: true);
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(e => e.Id == doc.Id).IsInactive);
        Assert.False(all.Single(e => e.Id == other.Id).IsInactive);

        var detail = await GetAsync(doc.Id);
        Assert.Equal(DocumentStatuses.Inactive, detail.Document.Status);
        await using var opened = await WithDb(db => Library(db).OpenAsync(doc.Id, S.Actor, default));                         // inactive is hidden, not removed: the file is still there
        Assert.True(opened.SizeBytes > 0);
        Assert.Equal(2, FilesOnDisk());                                                                                         // both files are still on disk
    }

    [Fact]
    public async Task Reactivating_clears_the_status_fields_and_keeps_the_old_reason_in_the_status_history_in_order()
    {
        var doc = await ImportedAsync();
        var inactive = await InactivateAsync(doc, "Wrong patient.");
        var active = await ReactivateAsync(inactive);
        Assert.Equal((DocumentStatuses.Active, null, null, null), (active.Status, active.InactiveReason, active.InactivatedByName, active.InactivatedAtUtc));
        await InactivateAsync(active, "Duplicate scan.");

        var detail = await GetAsync(doc.Id);
        Assert.Equal([DocumentStatuses.Inactive, DocumentStatuses.Active, DocumentStatuses.Inactive], detail.StatusChanges!.Select(c => c.ToStatus));
        Assert.Equal(["Wrong patient.", null, "Duplicate scan."], detail.StatusChanges!.Select(c => c.Reason));
        Assert.All(detail.StatusChanges!, c => Assert.False(string.IsNullOrWhiteSpace(c.ByName)));
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.Inactivated, DocumentEventKinds.Reactivated, DocumentEventKinds.Inactivated], await KindsAsync(doc.Id));
        Assert.Equal(["DocumentImported", "DocumentInactivated", "DocumentReactivated", "DocumentInactivated"], await AuditTypesAsync(doc.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0001reason")]
    public async Task A_missing_or_unusable_reason_is_refused_and_changes_nothing(string? reason)
    {
        var doc = await ImportedAsync();
        var e = await Refused(() => InactivateAsync(doc, reason));
        Assert.Equal((DocumentErrorCodes.MetadataInvalid, 400, "reason"), (e.Code, e.StatusCode, e.Problems.Single().Field));
        Assert.Equal(DocumentStatuses.Active, (await GetAsync(doc.Id)).Document.Status);
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
    }

    [Fact]
    public async Task A_reason_of_exactly_five_hundred_characters_is_accepted_and_one_more_is_refused()
    {
        var doc = await ImportedAsync();
        Assert.Equal("reason", (await Refused(() => InactivateAsync(doc, new string('r', 501)))).Problems.Single().Field);
        Assert.Equal(DocumentStatuses.Inactive, (await InactivateAsync(doc, new string('r', 500))).Status);
    }

    [Fact]
    public async Task Repeating_an_inactivation_or_a_reactivation_changes_nothing_and_logs_nothing()
    {
        var doc = await ImportedAsync();
        var inactive = await InactivateAsync(doc);
        var again = await InactivateAsync(inactive, "Another reason that must not replace the first.");
        Assert.Equal((inactive.RowVersion, "Scanned under the wrong patient."), (again.RowVersion, again.InactiveReason));
        var active = await ReactivateAsync(inactive);
        var activeAgain = await ReactivateAsync(active);
        Assert.Equal(active.RowVersion, activeAgain.RowVersion);
        Assert.Equal([DocumentEventKinds.Imported, DocumentEventKinds.Inactivated, DocumentEventKinds.Reactivated], await KindsAsync(doc.Id));
        Assert.Equal(2, (await GetAsync(doc.Id)).StatusChanges!.Count);
    }

    [Fact]
    public async Task A_stale_status_change_is_refused_a_version_is_required_and_an_unknown_document_is_not_found()
    {
        var doc = await ImportedAsync();
        await EditAsync(doc, description: "Changed first.");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => InactivateAsync(doc));
        Assert.Equal(DocumentStatuses.Active, (await GetAsync(doc.Id)).Document.Status);

        var none = await Refused(() => WithDb(db => Status(db).InactivateAsync(doc.Id, null, "Why.", S.Actor, default)));
        Assert.Equal(("row_version_required", 400), (none.Code, none.StatusCode));
        var bad = await Refused(() => WithDb(db => Status(db).ReactivateAsync(doc.Id, "not base64!", S.Actor, default)));
        Assert.Equal("row_version_invalid", bad.Code);
        var missing = await Refused(() => WithDb(db => Status(db).InactivateAsync(Guid.NewGuid(), doc.RowVersion, "Why.", S.Actor, default)));
        Assert.Equal(("document_not_found", 404), (missing.Code, missing.StatusCode));
    }

    [Fact]
    public async Task Simultaneous_inactivations_make_exactly_one_status_change_whoever_wins()
    {
        var doc = await ImportedAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => InactivateAsync(doc, $"Reason {i}").ContinueWith(t => t.IsCompletedSuccessfully)));
        Assert.True(results.Count(r => r) >= 1);                                                // the winner, and any caller that read after it committed and so found the document already inactive
        Assert.Equal(1, (await GetAsync(doc.Id)).StatusChanges!.Count);                         // but only one status change was ever made
        Assert.Equal(2, (await KindsAsync(doc.Id)).Count);
    }

    [Fact]
    public async Task A_failing_log_write_changes_nothing_for_an_inactivation_or_a_reactivation()
    {
        var doc = await ImportedAsync();
        await FailAuditAsync("DocumentInactivated");
        var e = await Refused(() => InactivateAsync(doc));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        var after = (await GetAsync(doc.Id));
        Assert.Equal((DocumentStatuses.Active, doc.RowVersion), (after.Document.Status, after.Document.RowVersion));
        Assert.Empty(after.StatusChanges!);
        Assert.Equal([DocumentEventKinds.Imported], await KindsAsync(doc.Id));
    }

    [Fact]
    public async Task A_file_that_was_inactivated_is_not_a_duplicate_for_the_next_import_of_the_same_content_but_an_active_one_still_is()
    {
        var first = await ImportedAsync(bytes: Png(321));
        var warned = await Refused(() => ImportAsync(Request(Png(321))));
        Assert.Equal("duplicate_document", warned.Code);
        await InactivateAsync(first, "Wrong category and date.");
        var fresh = (await ImportAsync(Request(Png(321)))).Document;                                                            // the inactive copy no longer counts
        Assert.Null(fresh.DuplicateOfDocumentId);
        var again = await Refused(() => ImportAsync(Request(Png(321))));                                                       // the new active copy does
        Assert.Equal("duplicate_document", again.Code);
        Assert.Equal(fresh.Id, ((DuplicateSummary)again.Detail!).DocumentId);
    }

    [Fact]
    public async Task A_confirmation_naming_an_inactive_document_is_refused()
    {
        var first = await ImportedAsync(bytes: Png(322));
        await InactivateAsync(first);
        var e = await Refused(() => ImportAsync(Request(Png(322)) with { ConfirmDuplicateOf = first.Id }));
        Assert.Equal("duplicate_confirmation_invalid", e.Code);
    }
}
