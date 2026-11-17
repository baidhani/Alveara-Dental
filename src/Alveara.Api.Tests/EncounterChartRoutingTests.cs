using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N022 A1 (OD-01, OD-02, OD-04, Addendum 02), against real SQL Server: an encounter's allergy, medication and medical-history actions write to the patient's ONE chart
/// with immutable encounter provenance, while dental history keeps the encounter's own entries. Covers the routing, the allergy match outcomes (active: update; resolved or
/// inactive: explicit reactivate or update, no status preselected; cancel changes nothing; concurrent adds leave one record), the medication / history confirm-current path,
/// removal only of an item added here, legacy entries read-only, the stored contradiction, a finalized encounter's provenance surviving later chart changes, the
/// patient / open-encounter checks, and statements restated or withdrawn per encounter.
/// </summary>
public class EncounterChartRoutingTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<T> RunAsync<T>(Func<EncounterService, Task<T>> action)
    {
        await using var db = _fixture.CreateContext();
        return await action(new EncounterService(db, Clock));
    }

    private async Task<T> ChartAsync<T>(Func<ClinicalRecordService, Task<T>> action)
    {
        await using var db = _fixture.CreateContext();
        return await action(new ClinicalRecordService(db, Clock));
    }

    private async Task<EncounterDetail> StartAsync(Guid? patient = null) => (await RunAsync(s => s.StartAsync(patient ?? _ann, null, null, null, _s.Actor, default))).Detail;
    private Task<EncounterDetail> AddAsync(EncounterDetail e, string kind, string name, string? reaction = null, string? dose = null, string? detail = null) =>
        RunAsync(s => s.AddEntryAsync(e.Id, kind, new EntryFields(name, detail, reaction, null, dose, null), e.RowVersion, _s.Actor, default));
    private Task<EncounterDetail> GetAsync(Guid id) => new EncounterReader(_fixture.CreateContext()).GetAsync(id, default);
    private Task<ClinicalRecordView> AddOnChartAsync(string kind, string name, string? reaction = null, string? status = null) =>
        ChartAsync(s => s.AddItemAsync(_ann, kind, new EntryFields(name, null, reaction, null, null, null), status, null, _s.Actor, default));

    private static SectionView Sec(EncounterDetail e, string kind) => e.Sections.Single(s => s.Kind == kind);
    private static IReadOnlyList<ItemView> Chart(EncounterDetail e, string kind) => Sec(e, kind).ChartItems ?? [];
    private static async Task<ClinicalException> RefusedAsync<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<ClinicalException>(action);

    // ---------- routing ----------

    [Fact]
    public async Task Allergy_medication_and_medical_history_go_to_the_chart_with_this_encounter_as_provenance_and_dental_history_stays_on_the_encounter()
    {
        var e = await StartAsync();
        e = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        e = await AddAsync(e, EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg");
        e = await AddAsync(e, EncounterEntryKinds.MedicalHistory, "Asthma");
        e = await AddAsync(e, EncounterEntryKinds.DentalHistory, "Crown");

        await using var db = _fixture.CreateContext();
        Assert.Equal(new[] { "Allergy", "MedicalHistory", "Medication" }, (await db.ClinicalRecordItems.AsNoTracking().Select(i => i.Kind).ToListAsync()).Order());
        Assert.All(await db.ClinicalRecordItemVersions.AsNoTracking().ToListAsync(), v => Assert.Equal((Guid?)e.Id, v.EncounterId));
        var entry = Assert.Single(await db.EncounterEntries.AsNoTracking().ToListAsync());
        Assert.Equal((EncounterEntryKinds.DentalHistory, "Crown"), (entry.Kind, entry.Name));

        Assert.All(new[] { "Allergy", "Medication", "MedicalHistory" }, k => Assert.Equal(SectionSources.Chart, Sec(e, k).Source));
        Assert.Equal(SectionSources.Encounter, Sec(e, EncounterEntryKinds.DentalHistory).Source);
        Assert.Null(Sec(e, EncounterEntryKinds.DentalHistory).ChartItems);
        // the encounter's history row names the section only, never the clinical text
        Assert.Equal(4, e.History.Count(h => h.EventType == EncounterEventTypes.EntryAdded));
        Assert.DoesNotContain("Penicillin", string.Join(" ", e.History.Select(h => h.Detail)));
        // and the chart lists them for the patient everywhere, not only in this encounter
        var record = await new ClinicalRecordReader(_fixture.CreateContext()).GetAsync(_ann, default);
        Assert.Equal("Penicillin", record.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Items.Single().Name);
    }

    // ---------- allergy match outcomes (Addendum 02) ----------

    [Fact]
    public async Task A_new_allergy_name_is_trimmed_and_its_inner_spaces_collapsed_with_the_case_kept()
    {
        var e = await AddAsync(await StartAsync(), EncounterEntryKinds.Allergy, "  Peanut    Oil  ");
        Assert.Equal("Peanut Oil", Assert.Single(Chart(e, EncounterEntryKinds.Allergy)).Name);
        // medications keep their own text (normalization is allergies only)
        e = await AddAsync(e, EncounterEntryKinds.Medication, "Vitamin  D");
        Assert.Equal("Vitamin  D", Assert.Single(Chart(e, EncounterEntryKinds.Medication)).Name);
    }

    [Fact]
    public async Task Adding_an_allergy_that_is_active_on_the_chart_points_to_it_for_an_update_and_changes_nothing()
    {
        await AddOnChartAsync(EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        var e = await StartAsync();
        var item = (await GetAsync(e.Id)).Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).ChartItems!.Single();

        var refused = await RefusedAsync(() => AddAsync(e, EncounterEntryKinds.Allergy, "PENICILLIN  ", reaction: "Rash")); // case and spacing differ: still the same allergy
        Assert.Equal(("duplicate_item", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(new ExistingItemRef(item.Id, EncounterEntryKinds.Allergy, ClinicalItemStatuses.Active), refused.Existing);
        Assert.Contains("Update that item instead", refused.Message);

        // cancelling is simply not acting on it: nothing was added, changed, or written to the encounter
        var after = await GetAsync(e.Id);
        Assert.Equal(e.RowVersion, after.RowVersion);
        Assert.Equal((item.RowVersion, "Hives"), (Assert.Single(Chart(after, EncounterEntryKinds.Allergy)).RowVersion, Chart(after, EncounterEntryKinds.Allergy)[0].Reaction));
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItemVersions));

        // the update it points to, made from the encounter, is recorded with this encounter
        var updated = await RunAsync(s => s.UpdateEntryAsync(e.Id, item.Id, new EntryFields("Penicillin", null, "Rash", null, null, null), e.RowVersion, _s.Actor, default, item.RowVersion));
        Assert.Equal("Rash", Assert.Single(Chart(updated, EncounterEntryKinds.Allergy)).Reaction);
        Assert.Equal(ClinicalChangeTypes.Changed, Assert.Single(Sec(updated, EncounterEntryKinds.Allergy).RecordedHere!).ChangeType);
    }

    [Fact]
    public async Task Adding_an_allergy_that_is_resolved_on_the_chart_offers_reactivation_with_no_status_chosen_for_the_user()
    {
        var v = await AddOnChartAsync(EncounterEntryKinds.Allergy, "Latex");
        var item = v.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Items.Single();
        v = await ChartAsync(s => s.SetStatusAsync(item.Id, ClinicalItemStatuses.Resolved, item.RowVersion, null, null, _s.Actor, default));
        item = v.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Items.Single();
        var e = await StartAsync();

        var refused = await RefusedAsync(() => AddAsync(e, EncounterEntryKinds.Allergy, "Latex"));
        Assert.Equal((item.Id, ClinicalItemStatuses.Resolved), (refused.Existing!.Id, refused.Existing.Status));
        Assert.Contains("with status Resolved. Reactivate or update that item instead.", refused.Message);
        Assert.Equal(ClinicalItemStatuses.Resolved, Assert.Single(Chart(await GetAsync(e.Id), EncounterEntryKinds.Allergy)).Status); // nothing reactivated by itself

        // the user must choose the status: none given is refused
        Assert.Equal("validation_failed", (await RefusedAsync(() => RunAsync(s => s.SetEntryStatusAsync(e.Id, item.Id, null, e.RowVersion, item.RowVersion, null, _s.Actor, default)))).Code);
        var reactivated = await RunAsync(s => s.SetEntryStatusAsync(e.Id, item.Id, ClinicalItemStatuses.Active, e.RowVersion, item.RowVersion, null, _s.Actor, default));
        Assert.Equal(ClinicalItemStatuses.Active, Assert.Single(Chart(reactivated, EncounterEntryKinds.Allergy)).Status);
        Assert.Equal(ClinicalChangeTypes.StatusChanged, Assert.Single(Sec(reactivated, EncounterEntryKinds.Allergy).RecordedHere!).ChangeType);
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems)); // still one record
    }

    [Fact]
    public async Task Adding_the_same_allergy_with_the_same_values_again_is_a_quiet_repeat()
    {
        var e = await AddAsync(await StartAsync(), EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        var again = await AddAsync(e, EncounterEntryKinds.Allergy, "penicillin", reaction: "Hives");
        Assert.Equal(e.RowVersion, again.RowVersion);
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItemVersions));
    }

    [Fact]
    public async Task Six_simultaneous_adds_of_one_allergy_leave_exactly_one_chart_record()
    {
        var e = await StartAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin"); return "ok"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
            catch (ClinicalException ex) when (ex.Code == "duplicate_item") { return "duplicate"; }
        })));
        Assert.Contains("ok", outcomes);
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems));
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItemVersions));
        Assert.Equal(1, (await GetAsync(e.Id)).History.Count(h => h.EventType == EncounterEventTypes.EntryAdded));
    }

    // ---------- medications and medical history already on the chart (S-1 option A) ----------

    [Fact]
    public async Task A_medication_already_on_the_chart_is_confirmed_current_or_updated_never_added_twice()
    {
        await ChartAsync(s => s.AddItemAsync(_ann, EncounterEntryKinds.Medication, new EntryFields("Lisinopril", null, null, null, "10 mg", null), null, null, _s.Actor, default));
        var e = await StartAsync();
        var refused = await RefusedAsync(() => AddAsync(e, EncounterEntryKinds.Medication, "Lisinopril", dose: "20 mg"));
        Assert.Equal((EncounterEntryKinds.Medication, ClinicalItemStatuses.Active), (refused.Existing!.Kind, refused.Existing.Status));

        var confirmed = await RunAsync(s => s.ConfirmSectionCurrentAsync(e.Id, EncounterEntryKinds.Medication, e.RowVersion, _s.Actor, default));
        var section = Sec(confirmed, EncounterEntryKinds.Medication);
        Assert.Equal((SectionStatuses.Recorded, ClinicalReviewStates.Reviewed, (Guid?)_s.Actor), (section.Status, section.ReviewedHere, section.ReviewedByUserId));
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalRecordItems));
        Assert.Equal("section_not_on_chart", (await RefusedAsync(() => RunAsync(s => s.ConfirmSectionCurrentAsync(e.Id, EncounterEntryKinds.DentalHistory, confirmed.RowVersion, _s.Actor, default)))).Code);
    }

    // ---------- removal, legacy entries, contradictions ----------

    [Fact]
    public async Task Only_an_item_added_in_this_encounter_can_be_removed_from_it_and_it_is_kept_as_entered_in_error()
    {
        await AddOnChartAsync(EncounterEntryKinds.Allergy, "Latex");
        var e = await AddAsync(await StartAsync(), EncounterEntryKinds.Allergy, "Penicillin");
        var latex = Chart(e, EncounterEntryKinds.Allergy).Single(i => i.Name == "Latex");
        var penicillin = Chart(e, EncounterEntryKinds.Allergy).Single(i => i.Name == "Penicillin");

        var notHere = await RefusedAsync(() => RunAsync(s => s.RemoveEntryAsync(e.Id, latex.Id, e.RowVersion, _s.Actor, default, latex.RowVersion, "wrong")));
        Assert.Equal(("item_not_added_here", 409), (notHere.Code, notHere.StatusCode));

        var removed = await RunAsync(s => s.RemoveEntryAsync(e.Id, penicillin.Id, e.RowVersion, _s.Actor, default, penicillin.RowVersion, "Wrong patient's allergy"));
        Assert.Equal(new[] { "Latex" }, Chart(removed, EncounterEntryKinds.Allergy).Select(i => i.Name));
        Assert.Contains(Sec(removed, EncounterEntryKinds.Allergy).RecordedHere!, p => p.ChangeType == ClinicalChangeTypes.RemovedInError && p.Name == "Penicillin");
        await using var db = _fixture.CreateContext();
        Assert.NotNull((await db.ClinicalRecordItems.AsNoTracking().SingleAsync(i => i.Id == penicillin.Id)).RemovedAtUtc); // kept, not deleted
    }

    [Fact]
    public async Task An_item_of_another_patient_cannot_be_changed_through_this_encounter()
    {
        var bob = await _s.PatientAsync("Bob", "Ray");
        var bobs = (await ChartAsync(s => s.AddItemAsync(bob, EncounterEntryKinds.Allergy, new EntryFields("Latex", null, null, null, null, null), null, null, _s.Actor, default)))
            .Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Items.Single();
        var e = await StartAsync();
        Assert.Equal(("entry_not_found", 404), await CodeAsync(() => RunAsync(s => s.UpdateEntryAsync(e.Id, bobs.Id, new EntryFields("Changed", null, null, null, null, null), e.RowVersion, _s.Actor, default, bobs.RowVersion))));
        Assert.Equal(("entry_not_found", 404), await CodeAsync(() => RunAsync(s => s.SetEntryStatusAsync(e.Id, bobs.Id, ClinicalItemStatuses.Resolved, e.RowVersion, bobs.RowVersion, null, _s.Actor, default))));
        // and the chart service refuses linking a change to another patient's encounter, or to one that is no longer open
        Assert.Equal("encounter_patient_mismatch", (await RefusedAsync(() => ChartAsync(s => s.AddItemAsync(bob, EncounterEntryKinds.Allergy, new EntryFields("Nuts", null, null, null, null, null), null, e.Id, _s.Actor, default)))).Code);
        Assert.Equal("Latex", (await new ClinicalRecordReader(_fixture.CreateContext()).GetAsync(bob, default)).Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Items.Single().Name);
    }

    private static async Task<(string, int)> CodeAsync<T>(Func<Task<T>> action) { var ex = await RefusedAsync(action); return (ex.Code, ex.StatusCode); }

    [Fact]
    public async Task Entries_and_marks_recorded_on_the_encounter_before_the_change_are_shown_and_read_only_and_a_contradiction_is_flagged_not_erased()
    {
        await AddOnChartAsync(EncounterEntryKinds.Allergy, "Penicillin");
        var e = await StartAsync();
        var legacyId = Guid.NewGuid();
        await using (var db = _fixture.CreateContext())
        {
            db.EncounterEntries.Add(new EncounterEntry { Id = legacyId, EncounterId = e.Id, Kind = EncounterEntryKinds.Medication, Name = "Aspirin", CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = _s.Actor });
            db.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = e.Id, Section = EncounterEntryKinds.Allergy, State = EncounterSectionStates.NoneReported, MarkedAtUtc = DateTimeOffset.UtcNow, MarkedByUserId = _s.Actor });
            await db.SaveChangesAsync();
        }
        e = await GetAsync(e.Id);

        Assert.Equal("Aspirin", Assert.Single(Sec(e, EncounterEntryKinds.Medication).Entries).Name);
        Assert.Equal(SectionStatuses.Recorded, Sec(e, EncounterEntryKinds.Medication).Status);
        Assert.Equal("legacy_entry_read_only", (await RefusedAsync(() => RunAsync(s => s.UpdateEntryAsync(e.Id, legacyId, new EntryFields("Changed", null, null, null, null, null), e.RowVersion, _s.Actor, default)))).Code);
        Assert.Equal("legacy_entry_read_only", (await RefusedAsync(() => RunAsync(s => s.RemoveEntryAsync(e.Id, legacyId, e.RowVersion, _s.Actor, default)))).Code);

        var allergy = Sec(e, EncounterEntryKinds.Allergy);
        Assert.Contains("Contradiction", allergy.Contradiction);
        Assert.Equal(SectionStatuses.NoneReported, allergy.Status);       // the mark is kept...
        Assert.Equal("Penicillin", Assert.Single(allergy.ChartItems!).Name); // ...and so is the active item
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterSectionMarks));
    }

    // ---------- provenance of a finalized encounter ----------

    [Fact]
    public async Task A_finalized_encounter_keeps_what_it_recorded_when_the_chart_changes_later()
    {
        var e = await StartAsync();
        e = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        foreach (var kind in new[] { EncounterEntryKinds.Medication, EncounterEntryKinds.MedicalHistory, EncounterEntryKinds.DentalHistory })
            e = await RunAsync(s => s.MarkSectionNoneReportedAsync(e.Id, kind, e.RowVersion, _s.Actor, default));
        var finalized = await RunAsync(s => s.FinalizeAsync(e.Id, e.RowVersion, _s.Actor, default));
        var item = Assert.Single(Chart(finalized, EncounterEntryKinds.Allergy));

        await ChartAsync(s => s.UpdateItemAsync(item.Id, new EntryFields("Penicillin", null, "Anaphylaxis", null, null, null), item.RowVersion, null, null, _s.Actor, default));

        var after = await GetAsync(finalized.Id);
        Assert.Equal("Anaphylaxis", Assert.Single(Chart(after, EncounterEntryKinds.Allergy)).Reaction); // the chart now
        var recorded = Assert.Single(Sec(after, EncounterEntryKinds.Allergy).RecordedHere!);             // what this encounter recorded, unchanged
        Assert.Equal((ClinicalChangeTypes.Added, 1, "Penicillin"), (recorded.ChangeType, recorded.VersionNumber, recorded.Name));
        Assert.Equal(finalized.RowVersion, after.RowVersion);
        Assert.Equal(ClinicalReviewStates.NoneKnown, Sec(after, EncounterEntryKinds.Medication).ReviewedHere);

        // the provenance rows themselves cannot be rewritten (append-only in the database)
        await using var db = _fixture.CreateContext();
        var version = await db.ClinicalRecordItemVersions.SingleAsync(v => v.EncounterId == finalized.Id);
        version.Name = "Rewritten";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---------- statements per encounter ----------

    [Fact]
    public async Task A_none_known_already_on_the_chart_is_recorded_when_a_new_encounter_states_it_and_a_repeat_there_is_quiet()
    {
        await ChartAsync(s => s.SetReviewAsync(_ann, EncounterEntryKinds.Medication, ClinicalReviewStates.NoneKnown, null, _s.Actor, default));
        var e = await StartAsync();
        Assert.Equal(SectionStatuses.Empty, Sec(e, EncounterEntryKinds.Medication).Status); // a statement made elsewhere does not document this encounter

        var marked = await RunAsync(s => s.MarkSectionNoneReportedAsync(e.Id, EncounterEntryKinds.Medication, e.RowVersion, _s.Actor, default));
        var section = Sec(marked, EncounterEntryKinds.Medication);
        Assert.Equal((SectionStatuses.NoneReported, ClinicalReviewStates.NoneKnown, (Guid?)_s.Actor), (section.Status, section.ReviewedHere, section.ReviewedByUserId));
        Assert.NotEqual(e.RowVersion, marked.RowVersion);

        var again = await RunAsync(s => s.MarkSectionNoneReportedAsync(e.Id, EncounterEntryKinds.Medication, marked.RowVersion, _s.Actor, default));
        Assert.Equal(marked.RowVersion, again.RowVersion);
    }

    [Fact]
    public async Task An_encounter_withdraws_only_a_statement_it_made()
    {
        await ChartAsync(s => s.SetReviewAsync(_ann, EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown, null, _s.Actor, default));
        var e = await StartAsync();
        var untouched = await RunAsync(s => s.ClearSectionReviewAsync(e.Id, EncounterEntryKinds.Allergy, e.RowVersion, _s.Actor, default));
        Assert.Equal(e.RowVersion, untouched.RowVersion);
        Assert.Equal(1, await _s.CountAsync(db => db.ClinicalSectionReviews)); // the chart's statement stands

        var mine = await RunAsync(s => s.MarkSectionNoneReportedAsync(e.Id, EncounterEntryKinds.Allergy, e.RowVersion, _s.Actor, default));
        var withdrawn = await RunAsync(s => s.ClearSectionReviewAsync(e.Id, EncounterEntryKinds.Allergy, mine.RowVersion, _s.Actor, default));
        Assert.Null(Sec(withdrawn, EncounterEntryKinds.Allergy).ReviewedHere);
        Assert.Equal(SectionStatuses.Empty, Sec(withdrawn, EncounterEntryKinds.Allergy).Status);
    }

    [Fact]
    public async Task Reactivating_an_item_withdraws_a_none_known_statement()
    {
        var v = await AddOnChartAsync(EncounterEntryKinds.Allergy, "Latex", status: ClinicalItemStatuses.Resolved);
        var item = v.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Items.Single();
        await ChartAsync(s => s.SetReviewAsync(_ann, EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown, null, _s.Actor, default)); // allowed: nothing active (OD-04)
        var e = await StartAsync();

        await RunAsync(s => s.SetEntryStatusAsync(e.Id, item.Id, ClinicalItemStatuses.Active, e.RowVersion, item.RowVersion, null, _s.Actor, default));
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalSectionReviews));
        await using var db = _fixture.CreateContext();
        Assert.Contains(await db.ClinicalRecordEvents.AsNoTracking().ToListAsync(), x => x.EventType == "ReviewCleared" && x.EncounterId == e.Id);
    }

    [Fact]
    public async Task A_stale_encounter_editor_cannot_write_to_the_chart()
    {
        var e = await StartAsync();
        await AddAsync(e, EncounterEntryKinds.DentalHistory, "Crown");                          // the encounter moves on
        var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin"));
        Assert.Equal(nameof(Encounter), conflict.EntityType);
        Assert.Equal(0, await _s.CountAsync(db => db.ClinicalRecordItems));
    }
}
