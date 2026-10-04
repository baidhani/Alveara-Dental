using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-005-C01 behaviour, against real SQL Server: a patient's longitudinal clinical record - items that live across encounters, their status lifecycle, history-preserving
/// correction, who reviewed each section and when, the "none known" / "unknown" / "not reviewed" distinction (so no fact is ever invented), encounter linkage, and each
/// failure path (stale edit, duplicates, audit failure, replays).
/// </summary>
public class ClinicalRecordServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;
    private readonly Guid _other = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _s.Actor, Username = $"dr-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });
        db.UserAccounts.Add(new UserAccount { Id = _other, Username = $"hyg-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var people = new StaffProviderService(db, Clock);
        await people.CreateStaffAsync("Dr. Okafor", null, _s.Actor, _s.Actor, default);
        await people.CreateStaffAsync("Hana Hygienist", null, _other, _s.Actor, default);
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<T> RunAsync<T>(Func<ClinicalRecordService, Task<T>> action)
    {
        await using var db = _fixture.CreateContext();
        return await action(new ClinicalRecordService(db, Clock));
    }

    private Task<ClinicalRecordView> AddAsync(string kind, string name, string? detail = null, string? reaction = null, string? severity = null, string? dose = null, string? frequency = null,
        string? status = null, Guid? encounterId = null, Guid? patient = null, Guid? actor = null) =>
        RunAsync(s => s.AddItemAsync(patient ?? _ann, kind, new EntryFields(name, detail, reaction, severity, dose, frequency), status, encounterId, actor ?? _s.Actor, default));

    private static ItemView Item(ClinicalRecordView v, string kind, string name) => v.Sections.Single(s => s.Kind == kind).Items.Single(i => i.Name == name);
    private static RecordSectionView Section(ClinicalRecordView v, string kind) => v.Sections.Single(s => s.Kind == kind);
    private Task<ClinicalRecordView> GetAsync(Guid? patient = null) => new ClinicalRecordReader(_fixture.CreateContext()).GetAsync(patient ?? _ann, default);
    private Task<ItemHistoryView> HistoryAsync(Guid itemId) => new ClinicalRecordReader(_fixture.CreateContext()).HistoryAsync(itemId, default);
    private Task<ClinicalRecordView> ReviewAsync(string section, string state, Guid? encounterId = null, Guid? actor = null) =>
        RunAsync(s => s.SetReviewAsync(_ann, section, state, encounterId, actor ?? _s.Actor, default));
    private static async Task<ClinicalException> RefusedAsync<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<ClinicalException>(action);

    private async Task<List<(string Type, string Details, Guid? By)>> AuditAsync(string entityType)
    {
        await using var db = _fixture.CreateContext();
        return (await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == entityType).OrderBy(a => a.TimestampUtc).ToListAsync()).Select(a => (a.EventType, a.Details, a.PerformedByUserAccountId)).ToList();
    }

    // ---------- capture with attribution ----------

    [Fact]
    public async Task Medical_history_dental_history_allergies_and_medications_are_captured_with_who_and_when()
    {
        await AddAsync(EncounterEntryKinds.MedicalHistory, "Hypertension", detail: "Since 2018");
        await AddAsync(EncounterEntryKinds.DentalHistory, "Root canal, lower left");
        await AddAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives", severity: EncounterSeverities.Moderate);
        var v = await AddAsync(EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg", frequency: "daily");

        Assert.All(v.Sections, s => Assert.Single(s.Items));
        var allergy = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        Assert.Equal(("Hives", EncounterSeverities.Moderate, ClinicalItemStatuses.Active), (allergy.Reaction, allergy.Severity, allergy.Status));
        Assert.Equal("Dr. Okafor", allergy.CreatedByName);                       // attribution is a name, not a bare id
        Assert.True(allergy.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
        var med = Item(v, EncounterEntryKinds.Medication, "Lisinopril");
        Assert.Equal(("10 mg", "daily"), (med.Dose, med.Frequency));
        Assert.Equal("Since 2018", Item(v, EncounterEntryKinds.MedicalHistory, "Hypertension").Detail);
    }

    [Fact]
    public async Task An_account_without_a_staff_profile_is_attributed_as_a_staff_member_never_as_nothing()
    {
        var stranger = Guid.NewGuid();
        var v = await AddAsync(EncounterEntryKinds.Allergy, "Latex", actor: stranger);
        Assert.Equal(ClinicalNames.Fallback, Item(v, EncounterEntryKinds.Allergy, "Latex").CreatedByName);
    }

    [Fact]
    public async Task One_patients_record_never_shows_another_patients_items()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        await AddAsync(EncounterEntryKinds.Allergy, "Penicillin");
        var bos = await GetAsync(bo);
        Assert.All(bos.Sections, s => Assert.Empty(s.Items));
    }

    // ---------- lifecycle: status and history-preserving correction ----------

    [Fact]
    public async Task An_allergy_can_become_inactive_or_resolved_and_a_medication_discontinued_with_every_step_kept_in_its_history()
    {
        var v = await AddAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        var allergy = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        v = await RunAsync(s => s.SetStatusAsync(allergy.Id, ClinicalItemStatuses.Inactive, allergy.RowVersion, "Not currently relevant", null, _s.Actor, default));
        allergy = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        v = await RunAsync(s => s.SetStatusAsync(allergy.Id, ClinicalItemStatuses.Resolved, allergy.RowVersion, "Tolerated a challenge dose", null, _other, default));
        allergy = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        Assert.Equal(ClinicalItemStatuses.Resolved, allergy.Status);

        var history = await HistoryAsync(allergy.Id);
        Assert.Equal(new[] { 1, 2, 3 }, history.Versions.Select(x => x.VersionNumber));
        Assert.Equal(new[] { ClinicalChangeTypes.Added, ClinicalChangeTypes.StatusChanged, ClinicalChangeTypes.StatusChanged }, history.Versions.Select(x => x.ChangeType));
        Assert.Equal(new[] { ClinicalItemStatuses.Active, ClinicalItemStatuses.Inactive, ClinicalItemStatuses.Resolved }, history.Versions.Select(x => x.Status));
        Assert.Equal(new[] { "Dr. Okafor", "Dr. Okafor", "Hana Hygienist" }, history.Versions.Select(x => x.ActorName));
        Assert.Equal("Tolerated a challenge dose", history.Versions[2].Reason);

        var m = Item(await AddAsync(EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg"), EncounterEntryKinds.Medication, "Lisinopril");
        var stopped = await RunAsync(s => s.SetStatusAsync(m.Id, ClinicalItemStatuses.Discontinued, m.RowVersion, null, null, _s.Actor, default));
        Assert.Equal(ClinicalItemStatuses.Discontinued, Item(stopped, EncounterEntryKinds.Medication, "Lisinopril").Status);
    }

    [Theory]
    [InlineData(EncounterEntryKinds.Medication, "Resolved")]
    [InlineData(EncounterEntryKinds.Allergy, "Discontinued")]
    [InlineData(EncounterEntryKinds.MedicalHistory, "Discontinued")]
    [InlineData(EncounterEntryKinds.DentalHistory, "resolved")]
    [InlineData(EncounterEntryKinds.Allergy, "Gone")]
    public async Task A_status_that_does_not_apply_to_the_kind_of_item_is_refused(string kind, string status)
    {
        var item = Item(await AddAsync(kind, "Thing"), kind, "Thing");
        var refused = await RefusedAsync(() => RunAsync(s => s.SetStatusAsync(item.Id, status, item.RowVersion, null, null, _s.Actor, default)));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey("status"));
        Assert.Single((await HistoryAsync(item.Id)).Versions); // nothing was recorded
    }

    [Fact]
    public async Task Correcting_an_item_keeps_what_it_said_before_who_corrected_it_and_why()
    {
        var v = await AddAsync(EncounterEntryKinds.Allergy, "Penicilin", reaction: "Rash", severity: EncounterSeverities.Mild); // misspelled
        var item = Item(v, EncounterEntryKinds.Allergy, "Penicilin");
        v = await RunAsync(s => s.UpdateItemAsync(item.Id, new EntryFields("Penicillin", null, "Hives", EncounterSeverities.Severe, null, null), item.RowVersion, "Spelling and a worse reaction", null, _other, default));

        var fixedItem = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        Assert.Equal(("Hives", EncounterSeverities.Severe), (fixedItem.Reaction, fixedItem.Severity));
        Assert.Equal("Hana Hygienist", fixedItem.UpdatedByName);
        var history = await HistoryAsync(item.Id);
        Assert.Equal(("Penicilin", "Rash", EncounterSeverities.Mild), (history.Versions[0].Name, history.Versions[0].Reaction, history.Versions[0].Severity)); // the original, still there
        Assert.Equal(("Penicillin", "Hives", EncounterSeverities.Severe), (history.Versions[1].Name, history.Versions[1].Reaction, history.Versions[1].Severity));
        Assert.Equal((ClinicalChangeTypes.Changed, "Spelling and a worse reaction", "Hana Hygienist"), (history.Versions[1].ChangeType, history.Versions[1].Reason, history.Versions[1].ActorName));
    }

    [Fact]
    public async Task An_item_entered_in_error_is_kept_with_its_reason_and_no_longer_shown_and_a_reason_is_required()
    {
        var item = Item(await AddAsync(EncounterEntryKinds.Allergy, "Latex"), EncounterEntryKinds.Allergy, "Latex");
        var noReason = await RefusedAsync(() => RunAsync(s => s.RemoveInErrorAsync(item.Id, item.RowVersion, "  ", null, _s.Actor, default)));
        Assert.True(noReason.FieldErrors.ContainsKey("reason"));
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems.Where(i => i.RemovedAtUtc == null)));

        var v = await RunAsync(s => s.RemoveInErrorAsync(item.Id, item.RowVersion, "Wrong patient", null, _s.Actor, default));
        Assert.Empty(Section(v, EncounterEntryKinds.Allergy).Items);
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems));  // the row is still there
        var history = await HistoryAsync(item.Id);
        Assert.Equal((ClinicalChangeTypes.RemovedInError, "Wrong patient"), (history.Versions[^1].ChangeType, history.Versions[^1].Reason));

        // the same name can be entered again, correctly, as a new item; the removed one keeps its own history
        var again = await AddAsync(EncounterEntryKinds.Allergy, "Latex");
        Assert.NotEqual(item.Id, Item(again, EncounterEntryKinds.Allergy, "Latex").Id);
    }

    [Fact]
    public async Task A_removed_item_cannot_be_changed_and_removing_it_again_changes_nothing()
    {
        var item = Item(await AddAsync(EncounterEntryKinds.Allergy, "Latex"), EncounterEntryKinds.Allergy, "Latex");
        await RunAsync(s => s.RemoveInErrorAsync(item.Id, item.RowVersion, "Wrong patient", null, _s.Actor, default));
        var versions = (await HistoryAsync(item.Id)).Versions.Count;
        await using var db = _fixture.CreateContext();
        var stored = await db.ClinicalRecordItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        var current = Convert.ToBase64String(stored.RowVersion);

        var again = await RunAsync(s => s.RemoveInErrorAsync(item.Id, current, "Still wrong", null, _other, default));
        Assert.Equal(versions, (await HistoryAsync(item.Id)).Versions.Count);
        Assert.Empty(Section(again, EncounterEntryKinds.Allergy).Items);
        Assert.Equal("item_removed", (await RefusedAsync(() => RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Inactive, current, null, null, _s.Actor, default)))).Code);
    }

    // ---------- quiet repeats and duplicates ----------

    [Fact]
    public async Task Repeating_an_add_a_status_a_change_or_a_review_changes_nothing()
    {
        var v = await AddAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        var item = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        await AddAsync(EncounterEntryKinds.Allergy, "PENICILLIN", reaction: "Hives");                                    // a retried add, retyped in another case
        await RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Active, item.RowVersion, null, null, _s.Actor, default)); // the status it already has
        await RunAsync(s => s.UpdateItemAsync(item.Id, new EntryFields("Penicillin", null, "Hives", null, null, null), item.RowVersion, null, null, _s.Actor, default)); // the values it already has
        var after = await GetAsync();

        Assert.Equal(item.RowVersion, Item(after, EncounterEntryKinds.Allergy, "Penicillin").RowVersion); // no new version
        Assert.Single((await HistoryAsync(item.Id)).Versions);
        Assert.Equal(1, (await AuditAsync(nameof(ClinicalRecordItem))).Count);
    }

    [Fact]
    public async Task Adding_a_name_that_is_listed_with_different_values_is_refused_and_names_the_existing_status()
    {
        var item = Item(await AddAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives"), EncounterEntryKinds.Allergy, "Penicillin");
        await RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Resolved, item.RowVersion, null, null, _s.Actor, default));
        var refused = await RefusedAsync(() => AddAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Anaphylaxis"));
        Assert.Equal(("duplicate_item", 409), (refused.Code, refused.StatusCode));
        Assert.Contains("Resolved", refused.Message);
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems));
    }

    [Fact]
    public async Task Renaming_an_item_to_another_listed_name_is_refused()
    {
        await AddAsync(EncounterEntryKinds.Medication, "Aspirin");
        var b = Item(await AddAsync(EncounterEntryKinds.Medication, "Ibuprofen"), EncounterEntryKinds.Medication, "Ibuprofen");
        var refused = await RefusedAsync(() => RunAsync(s => s.UpdateItemAsync(b.Id, new EntryFields("aspirin", null, null, null, null, null), b.RowVersion, null, null, _s.Actor, default)));
        Assert.Equal("duplicate_item", refused.Code);
    }

    [Fact]
    public async Task The_same_name_may_be_listed_under_different_kinds()
    {
        await AddAsync(EncounterEntryKinds.MedicalHistory, "Asthma");
        var v = await AddAsync(EncounterEntryKinds.DentalHistory, "Asthma");
        Assert.Equal(2, v.Sections.Sum(s => s.Items.Count));
    }

    [Fact]
    public async Task Six_simultaneous_adds_of_the_same_item_store_it_once()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await AddAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives"); return "ok"; }
            catch (ClinicalException ex) { return ex.Code; }
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));       // identical retries all return the one item
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems));
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItemVersions));
    }

    // ---------- validation ----------

    [Theory]
    [InlineData("", "name")]
    [InlineData("   ", "name")]
    public async Task An_item_needs_a_name(string name, string field)
    {
        var refused = await RefusedAsync(() => AddAsync(EncounterEntryKinds.Allergy, name));
        Assert.Equal("validation_failed", refused.Code);
        Assert.True(refused.FieldErrors.ContainsKey(field));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordItems));
    }

    [Fact]
    public async Task Fields_that_do_not_belong_to_the_kind_are_refused_and_limits_are_enforced()
    {
        var wrong = await RefusedAsync(() => AddAsync(EncounterEntryKinds.Medication, "Aspirin", reaction: "Rash", severity: "Mild"));
        Assert.True(wrong.FieldErrors.ContainsKey("reaction") && wrong.FieldErrors.ContainsKey("severity"));
        var big = await RefusedAsync(() => AddAsync(EncounterEntryKinds.MedicalHistory, new string('x', EncounterRules.NameMax + 1)));
        Assert.True(big.FieldErrors.ContainsKey("name"));
        var kind = await RefusedAsync(() => AddAsync("Vitals", "x"));
        Assert.Equal("validation_failed", kind.Code);
        var reason = await RefusedAsync(() => RunAsync(async s =>
        {
            var i = Item(await AddAsync(EncounterEntryKinds.Allergy, "Latex"), EncounterEntryKinds.Allergy, "Latex");
            return await s.SetStatusAsync(i.Id, ClinicalItemStatuses.Inactive, i.RowVersion, new string('r', ClinicalRecordRules.ReasonMax + 1), null, _s.Actor, default);
        }));
        Assert.True(reason.FieldErrors.ContainsKey("reason"));
    }

    [Fact]
    public async Task A_missing_or_malformed_version_is_refused_before_anything_changes()
    {
        var item = Item(await AddAsync(EncounterEntryKinds.Allergy, "Latex"), EncounterEntryKinds.Allergy, "Latex");
        Assert.Equal("row_version_required", (await RefusedAsync(() => RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Inactive, null, null, null, _s.Actor, default)))).Code);
        Assert.Equal("row_version_invalid", (await RefusedAsync(() => RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Inactive, "not base64!", null, null, _s.Actor, default)))).Code);
        Assert.Equal("item_not_found", (await RefusedAsync(() => RunAsync(s => s.SetStatusAsync(Guid.NewGuid(), ClinicalItemStatuses.Inactive, item.RowVersion, null, null, _s.Actor, default)))).Code);
        Assert.Single((await HistoryAsync(item.Id)).Versions);
    }

    [Fact]
    public async Task An_unknown_or_inactive_patient_is_refused_for_writes_but_an_inactive_chart_can_still_be_read()
    {
        Assert.Equal("patient_not_found", (await RefusedAsync(() => AddAsync(EncounterEntryKinds.Allergy, "Latex", patient: Guid.NewGuid()))).Code);
        await AddAsync(EncounterEntryKinds.Allergy, "Latex");
        await using (var db = _fixture.CreateContext())
        {
            var p = await db.Patients.SingleAsync(x => x.Id == _ann);
            p.IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal("patient_inactive", (await RefusedAsync(() => AddAsync(EncounterEntryKinds.Allergy, "Penicillin"))).Code);
        Assert.Equal("patient_inactive", (await RefusedAsync(() => ReviewAsync(EncounterEntryKinds.Medication, ClinicalReviewStates.NoneKnown))).Code);
        Assert.Single(Section(await GetAsync(), EncounterEntryKinds.Allergy).Items); // still readable
        await Assert.ThrowsAsync<ClinicalException>(() => GetAsync(Guid.NewGuid()));
    }

    // ---------- unknown / not reviewed / none known ----------

    [Fact]
    public async Task Not_reviewed_none_known_and_unknown_are_three_different_statements_and_none_of_them_is_an_invented_item()
    {
        var fresh = await GetAsync();
        Assert.All(fresh.Sections, s => Assert.Equal((ClinicalSectionStatuses.NotReviewed, 0, (DateTimeOffset?)null), (s.Status, s.Items.Count, s.ReviewedAtUtc))); // saying nothing is "not reviewed"

        var v = await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        v = await ReviewAsync(EncounterEntryKinds.Medication, ClinicalReviewStates.Unknown);
        Assert.Equal(ClinicalSectionStatuses.NoneKnown, Section(v, EncounterEntryKinds.Allergy).Status);
        Assert.Equal(ClinicalSectionStatuses.Unknown, Section(v, EncounterEntryKinds.Medication).Status);
        Assert.Equal(ClinicalSectionStatuses.NotReviewed, Section(v, EncounterEntryKinds.MedicalHistory).Status);
        Assert.All(v.Sections, s => Assert.Empty(s.Items));                       // no statement created an item
        Assert.Equal("Dr. Okafor", Section(v, EncounterEntryKinds.Allergy).ReviewedByName);
        Assert.NotNull(Section(v, EncounterEntryKinds.Allergy).ReviewedAtUtc);
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordItems));
    }

    [Fact]
    public async Task Marking_a_section_none_known_or_unknown_needs_an_empty_section_and_confirming_needs_items()
    {
        await AddAsync(EncounterEntryKinds.Allergy, "Penicillin");
        Assert.Equal("section_has_items", (await RefusedAsync(() => ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown))).Code);
        Assert.Equal("section_has_items", (await RefusedAsync(() => ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Unknown))).Code);
        Assert.Equal("section_empty", (await RefusedAsync(() => ReviewAsync(EncounterEntryKinds.Medication, ClinicalReviewStates.Reviewed))).Code);
        Assert.Equal("validation_failed", (await RefusedAsync(() => ReviewAsync(EncounterEntryKinds.Medication, "Fine"))).Code);
        Assert.Equal("validation_failed", (await RefusedAsync(() => ReviewAsync("Vitals", ClinicalReviewStates.NoneKnown))).Code);
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalSectionReviews));
    }

    [Fact]
    public async Task Adding_an_item_withdraws_a_none_known_statement_and_records_that_it_did()
    {
        await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        var v = await AddAsync(EncounterEntryKinds.Allergy, "Penicillin");
        Assert.Equal(ClinicalSectionStatuses.NeedsReview, Section(v, EncounterEntryKinds.Allergy).Status); // listed, and nobody has confirmed the list
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalSectionReviews));
        Assert.Contains(v.Timeline, e => e.EventType == "ReviewCleared" && e.Section == EncounterEntryKinds.Allergy);
    }

    [Fact]
    public async Task A_confirmed_list_is_reviewed_until_an_item_changes_and_then_needs_review_again()
    {
        var v = await AddAsync(EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg");
        Assert.Equal(ClinicalSectionStatuses.NeedsReview, Section(v, EncounterEntryKinds.Medication).Status);   // listed but never confirmed

        v = await ReviewAsync(EncounterEntryKinds.Medication, ClinicalReviewStates.Reviewed, actor: _other);
        var section = Section(v, EncounterEntryKinds.Medication);
        Assert.Equal((ClinicalSectionStatuses.Reviewed, "Hana Hygienist"), (section.Status, section.ReviewedByName));

        await Task.Delay(20);
        var item = section.Items.Single();
        v = await RunAsync(s => s.UpdateItemAsync(item.Id, new EntryFields("Lisinopril", null, null, null, "20 mg", null), item.RowVersion, "Dose increased", null, _s.Actor, default));
        Assert.Equal(ClinicalSectionStatuses.NeedsReview, Section(v, EncounterEntryKinds.Medication).Status);   // the list changed after the confirmation

        v = await ReviewAsync(EncounterEntryKinds.Medication, ClinicalReviewStates.Reviewed);
        Assert.Equal(ClinicalSectionStatuses.Reviewed, Section(v, EncounterEntryKinds.Medication).Status);
        Assert.Equal("Dr. Okafor", Section(v, EncounterEntryKinds.Medication).ReviewedByName);                 // and the latest reviewer is the one on record
    }

    [Fact]
    public async Task Six_clinicians_adding_to_a_section_marked_none_known_at_once_all_succeed_and_the_statement_is_withdrawn_once()
    {
        await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            try { await AddAsync(EncounterEntryKinds.Allergy, $"Allergen {i}"); return "ok"; }
            catch (Exception ex) { return ex.GetType().Name; } // the race for the one statement they all delete must not surface as a failure
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));
        Assert.Equal(6, await _s.CountAsync(db => db.ClinicalRecordItems));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalSectionReviews));
        Assert.Equal(6, await _s.CountAsync(db => db.ClinicalRecordItemVersions));
    }

    [Fact]
    public async Task Six_clinicians_withdrawing_the_same_statement_at_once_all_get_the_record_and_it_is_withdrawn()
    {
        await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { return Section(await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NotReviewed), EncounterEntryKinds.Allergy).Status; }
            catch (Exception ex) { return ex.GetType().Name; }
        })));
        Assert.All(outcomes, o => Assert.Equal(ClinicalSectionStatuses.NotReviewed, o));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalSectionReviews));
    }

    [Fact]
    public async Task Saying_the_same_thing_again_records_nothing_and_a_statement_can_be_withdrawn()
    {
        await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        Assert.Equal(1, (await GetAsync()).Timeline.Count(e => e.EventType == "SectionReviewed"));
        Assert.Single(await AuditAsync(nameof(ClinicalSectionReview)));

        var v = await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NotReviewed);
        Assert.Equal(ClinicalSectionStatuses.NotReviewed, Section(v, EncounterEntryKinds.Allergy).Status);
        Assert.Null(Section(v, EncounterEntryKinds.Allergy).ReviewedAtUtc);
        var again = await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NotReviewed);          // withdrawing nothing is quiet
        Assert.Equal(v.Timeline.Count, again.Timeline.Count);

        // none known can change to unknown (a different statement), and that is recorded
        await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);
        var changed = await ReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Unknown);
        Assert.Equal(ClinicalSectionStatuses.Unknown, Section(changed, EncounterEntryKinds.Allergy).Status);
    }

    // ---------- encounter linkage ----------

    [Fact]
    public async Task A_change_made_during_an_encounter_names_that_encounter_in_the_items_history_and_the_record_timeline()
    {
        var enc = (await RunEncounterAsync(s => s.StartAsync(_ann, null, null, "k1", _s.Actor, default))).Detail;
        var v = await AddAsync(EncounterEntryKinds.Allergy, "Penicillin", encounterId: enc.Id);
        var item = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        await RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Inactive, item.RowVersion, null, enc.Id, _s.Actor, default));

        Assert.All((await HistoryAsync(item.Id)).Versions, x => Assert.Equal(enc.Id, x.EncounterId));
        Assert.All((await GetAsync()).Timeline, e => Assert.Equal(enc.Id, e.EncounterId));
    }

    [Fact]
    public async Task A_change_cannot_be_linked_to_another_patients_encounter_a_finalized_one_a_signed_one_or_one_that_does_not_exist()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        var boEnc = (await RunEncounterAsync(s => s.StartAsync(bo, null, null, "bo", _s.Actor, default))).Detail;
        Assert.Equal("encounter_patient_mismatch", (await RefusedAsync(() => AddAsync(EncounterEntryKinds.Allergy, "Latex", encounterId: boEnc.Id))).Code);
        Assert.Equal("encounter_not_found", (await RefusedAsync(() => AddAsync(EncounterEntryKinds.Allergy, "Latex", encounterId: Guid.NewGuid()))).Code);

        var annEnc = (await RunEncounterAsync(s => s.StartAsync(_ann, null, null, "ann", _s.Actor, default))).Detail;
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.Encounters.SingleAsync(x => x.Id == annEnc.Id);
            e.SignedAtUtc = DateTimeOffset.UtcNow; e.SignedByUserId = _s.Actor;
            await db.SaveChangesAsync();
        }
        Assert.Equal("encounter_not_open", (await RefusedAsync(() => AddAsync(EncounterEntryKinds.Allergy, "Latex", encounterId: annEnc.Id))).Code);
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordItems));
    }

    private async Task<T> RunEncounterAsync<T>(Func<EncounterService, Task<T>> action)
    {
        await using var db = _fixture.CreateContext();
        return await action(new EncounterService(db, Clock));
    }

    // ---------- audit ----------

    [Fact]
    public async Task Every_change_is_audited_with_user_and_time_and_no_audit_or_history_text_contains_clinical_content()
    {
        var v = await AddAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives", detail: "Patient says swelling");
        var item = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        v = await RunAsync(s => s.UpdateItemAsync(item.Id, new EntryFields("Penicillin", "Patient says swelling", "Anaphylaxis", EncounterSeverities.Severe, null, null), item.RowVersion, "Worse reaction", null, _other, default));
        item = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        v = await RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Inactive, item.RowVersion, null, null, _s.Actor, default));
        item = Item(v, EncounterEntryKinds.Allergy, "Penicillin");
        await RunAsync(s => s.RemoveInErrorAsync(item.Id, item.RowVersion, "Wrong chart", null, _s.Actor, default));
        await ReviewAsync(EncounterEntryKinds.Medication, ClinicalReviewStates.NoneKnown);

        var items = await AuditAsync(nameof(ClinicalRecordItem));
        Assert.Equal(new[] { "ClinicalRecordItemAdded", "ClinicalRecordItemChanged", "ClinicalRecordItemStatusChanged", "ClinicalRecordItemRemoved" }, items.Select(a => a.Type));
        Assert.Equal(new Guid?[] { _s.Actor, _other, _s.Actor, _s.Actor }, items.Select(a => a.By));
        var reviews = await AuditAsync(nameof(ClinicalSectionReview));
        Assert.Equal("ClinicalSectionReviewed", Assert.Single(reviews).Type);

        var text = string.Join(" ", items.Concat(reviews).Select(a => a.Details)) + " " + string.Join(" ", (await GetAsync()).Timeline.Select(e => e.Detail));
        foreach (var secret in new[] { "Penicillin", "Hives", "Anaphylaxis", "swelling", "Worse reaction", "Wrong chart" })
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_change_is_not_made_and_the_same_request_can_be_retried()
    {
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new ClinicalRecordService(failing, Clock).AddItemAsync(_ann, EncounterEntryKinds.Allergy, new EntryFields("Latex", null, null, null, null, null), null, null, _s.Actor, default));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordItems));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordItemVersions));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordEvents));

        var item = Item(await AddAsync(EncounterEntryKinds.Allergy, "Latex"), EncounterEntryKinds.Allergy, "Latex"); // the retry works
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new ClinicalRecordService(failing, Clock).SetStatusAsync(item.Id, ClinicalItemStatuses.Inactive, item.RowVersion, null, null, _s.Actor, default));
        Assert.Equal(ClinicalItemStatuses.Active, Item(await GetAsync(), EncounterEntryKinds.Allergy, "Latex").Status);
        Assert.Single((await HistoryAsync(item.Id)).Versions);
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new ClinicalRecordService(failing, Clock).SetReviewAsync(_ann, EncounterEntryKinds.Medication, ClinicalReviewStates.NoneKnown, null, _s.Actor, default));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalSectionReviews));
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task A_stale_edit_of_an_item_is_refused_and_the_other_editors_change_stands()
    {
        var item = Item(await AddAsync(EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg"), EncounterEntryKinds.Medication, "Lisinopril");
        await RunAsync(s => s.UpdateItemAsync(item.Id, new EntryFields("Lisinopril", null, null, null, "20 mg", null), item.RowVersion, null, null, _s.Actor, default)); // first editor, from version V
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Discontinued, item.RowVersion, null, null, _other, default))); // second, still on V

        var after = Item(await GetAsync(), EncounterEntryKinds.Medication, "Lisinopril");
        Assert.Equal(("20 mg", ClinicalItemStatuses.Active), (after.Dose, after.Status));
        Assert.Equal(2, (await HistoryAsync(item.Id)).Versions.Count);              // the refused edit left no version, event or audit behind
        Assert.Equal(2, (await AuditAsync(nameof(ClinicalRecordItem))).Count);
    }

    [Fact]
    public async Task Six_simultaneous_status_changes_from_one_version_apply_exactly_once()
    {
        var item = Item(await AddAsync(EncounterEntryKinds.Medication, "Lisinopril"), EncounterEntryKinds.Medication, "Lisinopril");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await RunAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Discontinued, item.RowVersion, null, null, _s.Actor, default)); return "ok"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Contains("ok", outcomes);                                            // a request that arrives after the winner finds the status already set and returns it quietly
        Assert.All(outcomes, o => Assert.Contains(o, new[] { "ok", "conflict" }));
        Assert.Equal(2, (await HistoryAsync(item.Id)).Versions.Count);              // but the change itself was applied exactly once
        Assert.Equal(2, (await AuditAsync(nameof(ClinicalRecordItem))).Count);
    }
}
