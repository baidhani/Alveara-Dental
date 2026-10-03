using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-005 behaviour, against real SQL Server: documenting an encounter (medical and dental history, allergies, medications), the "incomplete documentation" rule
/// and its none-reported escape, finalizing, addenda that preserve the original, the audit trail with user and time, and each failure path - incomplete
/// documentation, amendment failure, data loss, audit failure and concurrency. The database-level guarantees are tested in <see cref="ClinicalEncounterSchemaTests"/>.
/// </summary>
public class EncounterServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private EncounterService Svc(Alveara.Api.Data.AlveraDbContext db) => new(db, Clock);

    private async Task<T> RunAsync<T>(Func<EncounterService, Task<T>> action)
    {
        await using var db = _fixture.CreateContext();
        return await action(Svc(db));
    }

    private async Task<EncounterDetail> StartAsync(Guid? patient = null, Guid? appointment = null, string? key = null) =>
        (await RunAsync(s => s.StartAsync(patient ?? _ann, appointment, null, key, _s.Actor, default))).Detail;

    private Task<EncounterDetail> AddAsync(EncounterDetail e, string kind, string name, string? detail = null, string? reaction = null, string? severity = null, string? dose = null, string? frequency = null) =>
        RunAsync(s => s.AddEntryAsync(e.Id, kind, new EntryFields(name, detail, reaction, severity, dose, frequency), e.RowVersion, _s.Actor, default));

    private Task<EncounterDetail> MarkAsync(EncounterDetail e, string kind) => RunAsync(s => s.MarkSectionNoneReportedAsync(e.Id, kind, e.RowVersion, _s.Actor, default));
    private Task<EncounterDetail> FinalizeAsync(EncounterDetail e) => RunAsync(s => s.FinalizeAsync(e.Id, e.RowVersion, _s.Actor, default));
    private Task<EncounterDetail> GetAsync(Guid id) => RunAsync(s => new EncounterReader(_fixture.CreateContext()).GetAsync(id, default));

    /// <summary>A complete draft: medical history, dental history and an allergy recorded, medications marked none reported.</summary>
    private async Task<EncounterDetail> CompleteDraftAsync()
    {
        var e = await StartAsync();
        e = await AddAsync(e, EncounterEntryKinds.MedicalHistory, "Hypertension", detail: "Controlled with medication");
        e = await AddAsync(e, EncounterEntryKinds.DentalHistory, "Root canal, lower left, 2019");
        e = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives", severity: EncounterSeverities.Moderate);
        return await MarkAsync(e, EncounterEntryKinds.Medication);
    }

    private static async Task<ClinicalException> RefusedAsync<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<ClinicalException>(action);

    private async Task<List<(string Type, string Details, Guid? By, DateTimeOffset At)>> AuditAsync(Guid encounterId)
    {
        await using var db = _fixture.CreateContext();
        return (await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(Encounter) && a.TargetUserAccountId == encounterId).OrderBy(a => a.TimestampUtc).ToListAsync())
            .Select(a => (a.EventType, a.Details, a.PerformedByUserAccountId, a.TimestampUtc)).ToList();
    }

    // ---------- acceptance 1: a documented encounter records medical and dental history, allergies and medications ----------

    [Fact]
    public async Task A_documented_encounter_records_medical_and_dental_history_allergies_and_medications_with_who_and_when()
    {
        var e = await StartAsync();
        Assert.Equal(EncounterStatuses.Draft, e.Status);
        Assert.False(e.IsComplete);
        Assert.Equal(EncounterEntryKinds.All, e.MissingSections);

        e = await AddAsync(e, EncounterEntryKinds.MedicalHistory, "Hypertension", detail: "Controlled");
        e = await AddAsync(e, EncounterEntryKinds.DentalHistory, "Root canal, 2019");
        e = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives", severity: EncounterSeverities.Moderate);
        e = await AddAsync(e, EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg", frequency: "daily");

        Assert.True(e.IsComplete);
        Assert.Empty(e.MissingSections);
        var byKind = e.Sections.ToDictionary(s => s.Kind);
        Assert.All(byKind.Values, s => Assert.Equal(SectionStatuses.Recorded, s.Status));
        var allergy = Assert.Single(byKind[EncounterEntryKinds.Allergy].Entries);
        Assert.Equal(("Penicillin", "Hives", EncounterSeverities.Moderate), (allergy.Name, allergy.Reaction, allergy.Severity));
        var medication = Assert.Single(byKind[EncounterEntryKinds.Medication].Entries);
        Assert.Equal(("Lisinopril", "10 mg", "daily"), (medication.Name, medication.Dose, medication.Frequency));
        Assert.Equal("Controlled", Assert.Single(byKind[EncounterEntryKinds.MedicalHistory].Entries).Detail);
        Assert.All(byKind.Values.SelectMany(s => s.Entries), x => Assert.Equal(_s.Actor, x.CreatedByUserId));
        Assert.All(byKind.Values.SelectMany(s => s.Entries), x => Assert.True(x.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5)));

        // a fresh read of the same encounter shows exactly the same thing: it was stored, not just returned
        Assert.Equal(e.Sections.SelectMany(s => s.Entries).Select(x => x.Name).Order(), (await GetAsync(e.Id)).Sections.SelectMany(s => s.Entries).Select(x => x.Name).Order());
    }

    [Fact]
    public async Task An_encounter_started_from_an_appointment_is_that_appointments_one_encounter_and_asking_again_returns_it()
    {
        var appointment = (await _s.ScheduleAsync(_s.Request(_ann, At(9)))).Appointment;
        var first = await RunAsync(s => s.StartAsync(_ann, appointment.Id, null, null, _s.Actor, default));
        Assert.True(first.Created);
        Assert.Equal(appointment.Id, first.Detail.AppointmentId);
        Assert.Equal(appointment.StartUtc, first.Detail.EncounterAtUtc);

        var again = await RunAsync(s => s.StartAsync(_ann, appointment.Id, null, null, _s.Actor, default));
        Assert.False(again.Created);
        Assert.Equal(first.Detail.Id, again.Detail.Id);
        Assert.Equal(1, await _s.CountAsync(db => db.Encounters));
    }

    [Fact]
    public async Task A_retried_start_with_the_same_key_returns_the_first_encounter_and_a_key_cannot_be_reused_for_another_patient()
    {
        var first = await RunAsync(s => s.StartAsync(_ann, null, null, "start-key-1", _s.Actor, default));
        var retry = await RunAsync(s => s.StartAsync(_ann, null, null, "start-key-1", _s.Actor, default));
        Assert.True(first.Created);
        Assert.False(retry.Created);
        Assert.Equal(first.Detail.Id, retry.Detail.Id);
        Assert.Equal(1, await _s.CountAsync(db => db.Encounters));

        var bob = await _s.PatientAsync("Bob", "Ray");
        Assert.Equal("idempotency_key_reused", (await RefusedAsync(() => RunAsync(s => s.StartAsync(bob, null, null, "start-key-1", _s.Actor, default)))).Code);
    }

    [Fact]
    public async Task Starting_is_refused_for_an_unknown_or_inactive_patient_a_mismatched_appointment_and_an_absurd_date()
    {
        Assert.Equal("patient_not_found", (await RefusedAsync(() => RunAsync(s => s.StartAsync(Guid.NewGuid(), null, null, null, _s.Actor, default)))).Code);

        var inactive = await _s.PatientAsync("Ina", "Cole");
        await using (var db = _fixture.CreateContext())
        {
            (await db.Patients.SingleAsync(p => p.Id == inactive)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal("patient_inactive", (await RefusedAsync(() => RunAsync(s => s.StartAsync(inactive, null, null, null, _s.Actor, default)))).Code);

        var bob = await _s.PatientAsync("Bob", "Ray");
        var bobsAppointment = (await _s.ScheduleAsync(_s.Request(bob, At(11)))).Appointment.Id;
        Assert.Equal("appointment_patient_mismatch", (await RefusedAsync(() => RunAsync(s => s.StartAsync(_ann, bobsAppointment, null, null, _s.Actor, default)))).Code);
        Assert.Equal("appointment_not_found", (await RefusedAsync(() => RunAsync(s => s.StartAsync(_ann, Guid.NewGuid(), null, null, _s.Actor, default)))).Code);
        Assert.Equal("validation_failed", (await RefusedAsync(() => RunAsync(s => s.StartAsync(_ann, null, DateTimeOffset.UtcNow.AddYears(1), null, _s.Actor, default)))).Code);
        Assert.Equal(0, await _s.CountAsync(db => db.Encounters));
    }

    // ---------- failure path: incomplete documentation ----------

    [Fact]
    public async Task An_incomplete_encounter_cannot_be_finalized_and_the_refusal_names_what_is_missing_and_changes_nothing()
    {
        var e = await StartAsync();
        e = await AddAsync(e, EncounterEntryKinds.MedicalHistory, "Asthma");

        var refused = await RefusedAsync(() => FinalizeAsync(e));
        Assert.Equal(("documentation_incomplete", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(new[] { EncounterEntryKinds.DentalHistory, EncounterEntryKinds.Allergy, EncounterEntryKinds.Medication }.Order(), refused.FieldErrors.Keys.Order());
        Assert.Contains("Dental history", refused.Message);

        var after = await GetAsync(e.Id);
        Assert.Equal((EncounterStatuses.Draft, e.RowVersion), (after.Status, after.RowVersion));
        Assert.DoesNotContain(after.History, h => h.EventType == EncounterEventTypes.Finalized);
    }

    [Fact]
    public async Task A_section_with_nothing_to_report_is_marked_reviewed_none_reported_so_no_fact_has_to_be_invented()
    {
        var e = await StartAsync();
        foreach (var kind in EncounterEntryKinds.All) e = await MarkAsync(e, kind);
        Assert.True(e.IsComplete);
        Assert.All(e.Sections, s => Assert.Equal((SectionStatuses.NoneReported, 0), (s.Status, s.Entries.Count)));
        Assert.All(e.Sections, s => Assert.Equal(_s.Actor, s.ReviewedByUserId));

        var finalized = await FinalizeAsync(e);
        Assert.Equal(EncounterStatuses.Finalized, finalized.Status);
    }

    [Fact]
    public async Task A_none_reported_section_cannot_also_have_entries_and_the_review_can_be_cleared_while_it_is_a_draft()
    {
        var e = await StartAsync();
        e = await MarkAsync(e, EncounterEntryKinds.Allergy);
        Assert.Equal("section_marked_none_reported", (await RefusedAsync(() => AddAsync(e, EncounterEntryKinds.Allergy, "Latex"))).Code);

        e = await RunAsync(s => s.ClearSectionReviewAsync(e.Id, EncounterEntryKinds.Allergy, e.RowVersion, _s.Actor, default));
        Assert.Equal(SectionStatuses.Empty, e.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Status);
        e = await AddAsync(e, EncounterEntryKinds.Allergy, "Latex");
        Assert.Equal("section_has_entries", (await RefusedAsync(() => MarkAsync(e, EncounterEntryKinds.Allergy))).Code);
    }

    [Fact]
    public async Task Marking_a_marked_section_and_clearing_an_unmarked_one_are_quiet_repeats()
    {
        var e = await StartAsync();
        var marked = await MarkAsync(e, EncounterEntryKinds.Medication);
        var again = await MarkAsync(marked, EncounterEntryKinds.Medication);
        Assert.Equal(marked.RowVersion, again.RowVersion);
        Assert.Single(again.History, h => h.EventType == EncounterEventTypes.SectionMarked);

        var cleared = await RunAsync(s => s.ClearSectionReviewAsync(e.Id, EncounterEntryKinds.Allergy, again.RowVersion, _s.Actor, default));
        Assert.Equal(again.RowVersion, cleared.RowVersion);
    }

    [Theory]
    [InlineData("MedicalHistory", "", null, null, null, null, "name")]
    [InlineData("MedicalHistory", "   ", null, null, null, null, "name")]
    [InlineData("Medication", "Lisinopril", null, "Hives", null, null, "reaction")]
    [InlineData("Medication", "Lisinopril", null, null, "Mild", null, "severity")]
    [InlineData("Allergy", "Penicillin", null, null, "Critical", null, "severity")]
    [InlineData("Allergy", "Penicillin", null, null, null, "10 mg", "dose")]
    [InlineData("DentalHistory", "Crown", null, null, null, "daily", "frequency")]
    public async Task Invalid_entries_are_refused_with_the_field_that_needs_attention_and_nothing_is_stored(string kind, string name, string? detail, string? reaction, string? severity, string? dose, string field)
    {
        var e = await StartAsync();
        var refused = await RefusedAsync(() => AddAsync(e, kind, name, detail, reaction, severity, dose, frequency: field == "frequency" ? "daily" : null));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.Contains(field, refused.FieldErrors.Keys);
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterEntries));
    }

    [Fact]
    public async Task Over_long_values_an_unknown_section_and_a_missing_or_invalid_version_are_refused()
    {
        var e = await StartAsync();
        Assert.Contains("name", (await RefusedAsync(() => AddAsync(e, EncounterEntryKinds.Allergy, new string('x', EncounterRules.NameMax + 1)))).FieldErrors.Keys);
        Assert.Contains("detail", (await RefusedAsync(() => AddAsync(e, EncounterEntryKinds.MedicalHistory, "ok", detail: new string('x', EncounterRules.DetailMax + 1)))).FieldErrors.Keys);
        Assert.Equal("validation_failed", (await RefusedAsync(() => AddAsync(e, "Vitals", "BP"))).Code);
        Assert.Equal("row_version_required", (await RefusedAsync(() => RunAsync(s => s.AddEntryAsync(e.Id, EncounterEntryKinds.Allergy, new EntryFields("x", null, null, null, null, null), null, _s.Actor, default)))).Code);
        Assert.Equal("row_version_invalid", (await RefusedAsync(() => RunAsync(s => s.AddEntryAsync(e.Id, EncounterEntryKinds.Allergy, new EntryFields("x", null, null, null, null, null), "not-base64!", _s.Actor, default)))).Code);
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterEntries));
    }

    // ---------- changing and removing entries; repeats do not duplicate ----------

    [Fact]
    public async Task Adding_the_same_entry_twice_is_a_quiet_repeat_and_a_different_entry_under_the_same_name_is_refused()
    {
        var e = await StartAsync();
        var once = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        var twice = await AddAsync(once, EncounterEntryKinds.Allergy, "penicillin ", reaction: "Hives"); // same entry, retyped
        Assert.Equal(once.RowVersion, twice.RowVersion);
        Assert.Single(twice.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries);
        Assert.Single(twice.History, h => h.EventType == EncounterEventTypes.EntryAdded);

        Assert.Equal("duplicate_entry", (await RefusedAsync(() => AddAsync(twice, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Rash"))).Code);
    }

    [Fact]
    public async Task Fixing_only_the_capital_letters_of_an_entry_is_a_real_change()
    {
        var e = await AddAsync(await StartAsync(), EncounterEntryKinds.Allergy, "penicillin");
        var entry = e.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries.Single();
        var fixedCase = await RunAsync(s => s.UpdateEntryAsync(e.Id, entry.Id, new EntryFields("Penicillin", null, null, null, null, null), e.RowVersion, _s.Actor, default));
        Assert.NotEqual(e.RowVersion, fixedCase.RowVersion);
        Assert.Equal("Penicillin", fixedCase.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries.Single().Name);
    }

    [Fact]
    public async Task An_entry_can_be_changed_with_a_history_row_and_changing_nothing_changes_nothing()
    {
        var e = await StartAsync();
        e = await AddAsync(e, EncounterEntryKinds.Medication, "Lisinopril", dose: "10 mg", frequency: "daily");
        var entry = e.Sections.Single(s => s.Kind == EncounterEntryKinds.Medication).Entries.Single();

        var unchanged = await RunAsync(s => s.UpdateEntryAsync(e.Id, entry.Id, new EntryFields("Lisinopril", null, null, null, "10 mg", "daily"), e.RowVersion, _s.Actor, default));
        Assert.Equal(e.RowVersion, unchanged.RowVersion);

        var changed = await RunAsync(s => s.UpdateEntryAsync(e.Id, entry.Id, new EntryFields("Lisinopril", null, null, null, "20 mg", "daily"), e.RowVersion, _s.Actor, default));
        Assert.NotEqual(e.RowVersion, changed.RowVersion);
        var updated = changed.Sections.Single(s => s.Kind == EncounterEntryKinds.Medication).Entries.Single();
        Assert.Equal(("20 mg", _s.Actor), (updated.Dose, updated.UpdatedAtUtc is null ? Guid.Empty : _s.Actor));
        Assert.Equal(1, changed.History.Count(h => h.EventType == EncounterEventTypes.EntryChanged));

        Assert.Equal("duplicate_entry", (await RefusedAsync(async () =>
        {
            var withSecond = await AddAsync(changed, EncounterEntryKinds.Medication, "Metformin", dose: "500 mg");
            return await RunAsync(s => s.UpdateEntryAsync(e.Id, entry.Id, new EntryFields("Metformin", null, null, null, "20 mg", "daily"), withSecond.RowVersion, _s.Actor, default));
        })).Code);
    }

    [Fact]
    public async Task A_removed_entry_leaves_the_active_chart_but_is_kept_with_who_and_when_so_nothing_typed_is_lost()
    {
        var e = await StartAsync();
        e = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives");
        var entry = e.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries.Single();

        var removed = await RunAsync(s => s.RemoveEntryAsync(e.Id, entry.Id, e.RowVersion, _s.Actor, default));
        Assert.Empty(removed.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries);
        Assert.Equal(SectionStatuses.Empty, removed.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Status);

        await using (var db = _fixture.CreateContext())
        {
            var kept = await db.EncounterEntries.SingleAsync(x => x.Id == entry.Id);
            Assert.Equal(("Penicillin", "Hives", _s.Actor), (kept.Name, kept.Reaction, kept.RemovedByUserId));
            Assert.NotNull(kept.RemovedAtUtc);
        }

        var again = await RunAsync(s => s.RemoveEntryAsync(e.Id, entry.Id, removed.RowVersion, _s.Actor, default));
        Assert.Equal(removed.RowVersion, again.RowVersion); // removing a removed entry is a quiet repeat
        Assert.Equal("entry_removed", (await RefusedAsync(() => RunAsync(s => s.UpdateEntryAsync(e.Id, entry.Id, new EntryFields("Penicillin", null, "Rash", null, null, null), again.RowVersion, _s.Actor, default)))).Code);
        var readded = await AddAsync(again, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Hives"); // the name is free again
        Assert.Single(readded.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries);
    }

    [Fact]
    public async Task An_unknown_entry_is_a_404()
    {
        var e = await StartAsync();
        Assert.Equal("entry_not_found", (await RefusedAsync(() => RunAsync(s => s.RemoveEntryAsync(e.Id, Guid.NewGuid(), e.RowVersion, _s.Actor, default)))).Code);
        Assert.Equal("encounter_not_found", (await RefusedAsync(() => RunAsync(s => s.RemoveEntryAsync(Guid.NewGuid(), Guid.NewGuid(), e.RowVersion, _s.Actor, default)))).Code);
    }

    // ---------- acceptance 2: finalized, then amended with an addendum that preserves the original ----------

    [Fact]
    public async Task Finalizing_records_who_and_when_and_a_finalized_encounter_refuses_every_change_leaving_the_original_exactly_as_it_was()
    {
        var draft = await CompleteDraftAsync();
        var finalized = await FinalizeAsync(draft);
        Assert.Equal((EncounterStatuses.Finalized, _s.Actor), (finalized.Status, finalized.FinalizedByUserId));
        Assert.NotNull(finalized.FinalizedAtUtc);
        var entry = finalized.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries.Single();

        Assert.Equal("encounter_finalized", (await RefusedAsync(() => AddAsync(finalized, EncounterEntryKinds.Medication, "Added later"))).Code);
        Assert.Equal("encounter_finalized", (await RefusedAsync(() => RunAsync(s => s.UpdateEntryAsync(finalized.Id, entry.Id, new EntryFields("Rewritten", null, null, null, null, null), finalized.RowVersion, _s.Actor, default)))).Code);
        Assert.Equal("encounter_finalized", (await RefusedAsync(() => RunAsync(s => s.RemoveEntryAsync(finalized.Id, entry.Id, finalized.RowVersion, _s.Actor, default)))).Code);
        Assert.Equal("encounter_finalized", (await RefusedAsync(() => MarkAsync(finalized, EncounterEntryKinds.DentalHistory))).Code);
        Assert.Equal("encounter_finalized", (await RefusedAsync(() => RunAsync(s => s.ClearSectionReviewAsync(finalized.Id, EncounterEntryKinds.Medication, finalized.RowVersion, _s.Actor, default)))).Code);

        var after = await GetAsync(finalized.Id);
        Assert.Equal(finalized.RowVersion, after.RowVersion);
        Assert.Equal(finalized.Sections.SelectMany(s => s.Entries).Select(x => (x.Name, x.Reaction)).Order(), after.Sections.SelectMany(s => s.Entries).Select(x => (x.Name, x.Reaction)).Order());
        Assert.Equal(finalized.History.Count, after.History.Count);
    }

    [Fact]
    public async Task An_addendum_is_added_beside_the_untouched_original_with_who_and_when_and_several_stay_in_order()
    {
        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        var original = finalized.Sections.SelectMany(s => s.Entries).Select(x => (x.Id, x.Name, x.Reaction, x.Severity)).Order().ToList();

        var first = await RunAsync(s => s.AddAddendumAsync(finalized.Id, "Patient also reports a latex sensitivity.", "add-1", _s.Actor, default));
        Assert.True(first.Created);
        var second = await RunAsync(s => s.AddAddendumAsync(finalized.Id, "Allergy severity updated verbally to severe.", "add-2", _s.Actor, default));

        Assert.Equal(new[] { "Patient also reports a latex sensitivity.", "Allergy severity updated verbally to severe." }, second.Detail.Addenda.Select(a => a.Text));
        Assert.All(second.Detail.Addenda, a => Assert.Equal(_s.Actor, a.CreatedByUserId));
        Assert.True(second.Detail.Addenda[0].CreatedAtUtc <= second.Detail.Addenda[1].CreatedAtUtc);
        Assert.Equal(EncounterStatuses.Finalized, second.Detail.Status);
        Assert.Equal(finalized.RowVersion, second.Detail.RowVersion); // the original encounter row is not touched at all
        Assert.Equal(original, second.Detail.Sections.SelectMany(s => s.Entries).Select(x => (x.Id, x.Name, x.Reaction, x.Severity)).Order().ToList());
        Assert.Equal(2, second.Detail.History.Count(h => h.EventType == EncounterEventTypes.AddendumAdded));
    }

    [Fact]
    public async Task An_addendum_is_refused_on_a_draft_when_blank_without_a_key_or_too_long()
    {
        var draft = await StartAsync();
        Assert.Equal("encounter_not_finalized", (await RefusedAsync(() => RunAsync(s => s.AddAddendumAsync(draft.Id, "Too early", "k", _s.Actor, default)))).Code);

        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        Assert.Contains("text", (await RefusedAsync(() => RunAsync(s => s.AddAddendumAsync(finalized.Id, "   ", "k", _s.Actor, default)))).FieldErrors.Keys);
        Assert.Contains("clientKey", (await RefusedAsync(() => RunAsync(s => s.AddAddendumAsync(finalized.Id, "Text", null, _s.Actor, default)))).FieldErrors.Keys);
        Assert.Contains("text", (await RefusedAsync(() => RunAsync(s => s.AddAddendumAsync(finalized.Id, new string('x', EncounterRules.AddendumMax + 1), "k", _s.Actor, default)))).FieldErrors.Keys);
        Assert.Equal("encounter_not_found", (await RefusedAsync(() => RunAsync(s => s.AddAddendumAsync(Guid.NewGuid(), "Text", "k", _s.Actor, default)))).Code);
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterAddenda));
    }

    [Fact]
    public async Task A_retried_addendum_is_added_once_and_the_same_key_with_different_text_is_refused_not_merged()
    {
        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        var first = await RunAsync(s => s.AddAddendumAsync(finalized.Id, "Note one", "same-key", _s.Actor, default));
        var retry = await RunAsync(s => s.AddAddendumAsync(finalized.Id, "Note one", "same-key", _s.Actor, default));
        Assert.True(first.Created);
        Assert.False(retry.Created);
        Assert.Single(retry.Detail.Addenda);
        Assert.Equal("idempotency_key_reused", (await RefusedAsync(() => RunAsync(s => s.AddAddendumAsync(finalized.Id, "Different text", "same-key", _s.Actor, default)))).Code);
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterAddenda));
    }

    [Fact]
    public async Task Six_simultaneous_submits_of_one_addendum_produce_exactly_one()
    {
        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => RunAsync(s => s.AddAddendumAsync(finalized.Id, "One note, sent six times", "race-key", _s.Actor, default)))));
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterAddenda));
        Assert.Equal(1, (await GetAsync(finalized.Id)).History.Count(h => h.EventType == EncounterEventTypes.AddendumAdded));
    }

    // ---------- failure path: amendment failure ----------

    [Fact]
    public async Task If_an_addendum_cannot_be_recorded_nothing_is_saved_and_the_same_request_can_simply_be_retried()
    {
        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EncounterService(failing, Clock).AddAddendumAsync(finalized.Id, "Will not be saved", "retry-key", _s.Actor, default));

        var after = await GetAsync(finalized.Id);
        Assert.Empty(after.Addenda);                                            // no addendum...
        Assert.Equal(finalized.History.Count, after.History.Count);             // ...and no history row for it
        Assert.Equal(EncounterStatuses.Finalized, after.Status);                // the original is untouched

        var retried = await RunAsync(s => s.AddAddendumAsync(finalized.Id, "Will not be saved", "retry-key", _s.Actor, default));
        Assert.True(retried.Created);                                           // the key was not consumed by the failure
        Assert.Single(retried.Detail.Addenda);
    }

    // ---------- acceptance 3 (trust): every documentation change is logged with user and timestamp ----------

    [Fact]
    public async Task Every_documentation_change_is_audited_with_the_user_and_a_timestamp_and_the_audit_text_contains_no_clinical_content()
    {
        var e = await StartAsync();
        e = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin", reaction: "Anaphylaxis", severity: EncounterSeverities.Severe);
        var entry = e.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries.Single();
        e = await RunAsync(s => s.UpdateEntryAsync(e.Id, entry.Id, new EntryFields("Penicillin", "Confirmed by testing", "Anaphylaxis", EncounterSeverities.Severe, null, null), e.RowVersion, _s.Actor, default));
        e = await RunAsync(s => s.RemoveEntryAsync(e.Id, entry.Id, e.RowVersion, _s.Actor, default));
        e = await AddAsync(e, EncounterEntryKinds.MedicalHistory, "Diabetes type 2");
        e = await AddAsync(e, EncounterEntryKinds.DentalHistory, "Extraction 2021");
        e = await MarkAsync(e, EncounterEntryKinds.Allergy);
        e = await MarkAsync(e, EncounterEntryKinds.Medication);
        e = await RunAsync(s => s.ClearSectionReviewAsync(e.Id, EncounterEntryKinds.Medication, e.RowVersion, _s.Actor, default));
        e = await MarkAsync(e, EncounterEntryKinds.Medication);
        var finalized = await FinalizeAsync(e);
        await RunAsync(s => s.AddAddendumAsync(finalized.Id, "Patient confirms no new medication.", "k1", _s.Actor, default));

        var audit = await AuditAsync(finalized.Id);
        Assert.Equal(new[]
        {
            "EncounterStarted", "EncounterEntryAdded", "EncounterEntryChanged", "EncounterEntryRemoved", "EncounterEntryAdded", "EncounterEntryAdded",
            "EncounterSectionReviewed", "EncounterSectionReviewed", "EncounterSectionReviewCleared", "EncounterSectionReviewed", "EncounterFinalized", "EncounterAddendumAdded",
        }, audit.Select(a => a.Type));
        Assert.All(audit, a => Assert.Equal(_s.Actor, a.By));
        Assert.All(audit, a => Assert.True(a.At > DateTimeOffset.UtcNow.AddMinutes(-5) && a.At <= DateTimeOffset.UtcNow.AddMinutes(1)));
        // no name, reaction, detail or note ever reaches the audit log
        var text = string.Join(" ", audit.Select(a => a.Details));
        foreach (var secret in new[] { "Penicillin", "Anaphylaxis", "Confirmed by testing", "Diabetes", "Extraction", "no new medication" })
            Assert.DoesNotContain(secret, text);

        // the encounter's own history has one row per change, in order, with the same user
        var after = await GetAsync(finalized.Id);
        Assert.Equal(audit.Count, after.History.Count);
        Assert.All(after.History, h => Assert.Equal(_s.Actor, h.ActorUserId));
        Assert.DoesNotContain("Penicillin", string.Join(" ", after.History.Select(h => h.Detail)));
    }

    [Fact]
    public async Task Quiet_repeats_and_refused_requests_write_neither_history_nor_audit()
    {
        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        var before = (await AuditAsync(finalized.Id)).Count;

        await FinalizeAsync(finalized);                                                                  // finalize again
        await RefusedAsync(() => AddAsync(finalized, EncounterEntryKinds.Allergy, "Refused"));           // refused change
        await RefusedAsync(() => RunAsync(s => s.AddAddendumAsync(finalized.Id, "", "k", _s.Actor, default))); // invalid addendum

        Assert.Equal(before, (await AuditAsync(finalized.Id)).Count);
        Assert.Equal(before, (await GetAsync(finalized.Id)).History.Count);
    }

    // ---------- failure path: audit failure ----------

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_change_is_not_made_and_the_request_can_be_retried()
    {
        var e = await StartAsync();
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EncounterService(failing, Clock).AddEntryAsync(e.Id, EncounterEntryKinds.Allergy, new EntryFields("Penicillin", null, null, null, null, null), e.RowVersion, _s.Actor, default));

        var unchanged = await GetAsync(e.Id);
        Assert.Equal(e.RowVersion, unchanged.RowVersion);
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterEntries));
        Assert.Equal(e.History.Count, unchanged.History.Count);

        var retried = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin"); // the same version works once the log is back
        Assert.Single(retried.Sections.Single(s => s.Kind == EncounterEntryKinds.Allergy).Entries);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_encounter_is_not_finalized()
    {
        var draft = await CompleteDraftAsync();
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EncounterService(failing, Clock).FinalizeAsync(draft.Id, draft.RowVersion, _s.Actor, default));

        var after = await GetAsync(draft.Id);
        Assert.Equal((EncounterStatuses.Draft, draft.RowVersion), (after.Status, after.RowVersion));
        Assert.Null(after.FinalizedAtUtc);
        Assert.Equal(EncounterStatuses.Finalized, (await FinalizeAsync(draft)).Status); // and it can simply be retried
    }

    // ---------- failure path: concurrency issues ----------

    [Fact]
    public async Task A_stale_editor_is_refused_and_the_other_editors_change_stands()
    {
        var e = await StartAsync();
        var first = await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin");           // the first editor saves from version V
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AddAsync(e, EncounterEntryKinds.Medication, "Aspirin")); // the second still holds V

        var after = await GetAsync(e.Id);
        Assert.Equal(first.RowVersion, after.RowVersion);
        Assert.Equal(new[] { "Penicillin" }, after.Sections.SelectMany(s => s.Entries).Select(x => x.Name));
    }

    [Fact]
    public async Task An_edit_made_from_a_version_that_was_finalized_in_the_meantime_cannot_slip_in()
    {
        var draft = await CompleteDraftAsync();
        var finalized = await FinalizeAsync(draft);
        // a second clinician still looking at the draft tries to add an entry with the draft's version
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => AddAsync(draft, EncounterEntryKinds.Medication, "Added after finalize"));
        Assert.True(refused is ConcurrencyConflictException || (refused is ClinicalException { Code: "encounter_finalized" }));

        var after = await GetAsync(finalized.Id);
        Assert.Equal(finalized.RowVersion, after.RowVersion);
        Assert.DoesNotContain(after.Sections.SelectMany(s => s.Entries), x => x.Name == "Added after finalize");
    }

    [Fact]
    public async Task Six_clinicians_finalizing_at_once_finalize_it_exactly_once()
    {
        var draft = await CompleteDraftAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { return (await RunAsync(s => s.FinalizeAsync(draft.Id, draft.RowVersion, _s.Actor, default))).Status; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Contains(EncounterStatuses.Finalized, outcomes);
        var after = await GetAsync(draft.Id);
        Assert.Equal(EncounterStatuses.Finalized, after.Status);
        Assert.Equal(1, after.History.Count(h => h.EventType == EncounterEventTypes.Finalized));
        Assert.Equal(1, (await AuditAsync(draft.Id)).Count(a => a.Type == "EncounterFinalized"));
    }

    [Fact]
    public async Task Six_simultaneous_adds_of_the_same_entry_from_one_version_store_it_once()
    {
        var e = await StartAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await AddAsync(e, EncounterEntryKinds.Allergy, "Penicillin"); return "ok"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Contains("ok", outcomes);
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterEntries));
        Assert.Equal(1, (await GetAsync(e.Id)).History.Count(h => h.EventType == EncounterEventTypes.EntryAdded));
    }

    [Fact]
    public async Task Finalizing_from_a_stale_version_is_a_conflict_and_a_lost_edit_is_never_finalized_over()
    {
        var draft = await CompleteDraftAsync();
        var changed = await AddAsync(draft, EncounterEntryKinds.Allergy, "Latex"); // someone else changes it after the clinician reviewed it
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => FinalizeAsync(draft));
        Assert.Equal(EncounterStatuses.Draft, (await GetAsync(draft.Id)).Status);
        Assert.Equal(EncounterStatuses.Finalized, (await FinalizeAsync(changed)).Status);
    }

    // ---------- reading ----------

    [Fact]
    public async Task A_patients_encounters_are_listed_newest_first_with_their_completeness_and_counts()
    {
        var older = await RunAsync(s => s.StartAsync(_ann, null, DateTimeOffset.UtcNow.AddDays(-30), null, _s.Actor, default));
        var newer = await CompleteDraftAsync();
        var finalized = await FinalizeAsync(newer);
        await RunAsync(s => s.AddAddendumAsync(finalized.Id, "Addendum", "k", _s.Actor, default));

        await using var db = _fixture.CreateContext();
        var list = await new EncounterReader(db).ListForPatientAsync(_ann, default);
        Assert.Equal(new[] { finalized.Id, older.Detail.Id }, list.Select(x => x.Id));
        Assert.Equal((EncounterStatuses.Finalized, true, 3, 1), (list[0].Status, list[0].IsComplete, list[0].EntryCount, list[0].AddendumCount));
        Assert.Equal((EncounterStatuses.Draft, false, 0, 0), (list[1].Status, list[1].IsComplete, list[1].EntryCount, list[1].AddendumCount));
        Assert.Equal("patient_not_found", (await RefusedAsync(() => new EncounterReader(db).ListForPatientAsync(Guid.NewGuid(), default))).Code);
    }
}
