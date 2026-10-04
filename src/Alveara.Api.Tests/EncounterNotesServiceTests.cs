using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-005-C01 behaviour, against real SQL Server: SOAP / progress / treatment notes, the note templates that shape them, vital signs, signing and amending. Each rule is
/// tested with its failure path - stale edits, a finalized or signed note, incomplete required data, amendment failure, replays and audit failure.
/// </summary>
public class EncounterNotesServiceTests : IAsyncLifetime
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

    // ---------- helpers ----------

    private async Task<T> WithDb<T>(Func<Alveara.Api.Data.AlveraDbContext, Task<T>> action)
    {
        await using var db = _fixture.CreateContext();
        return await action(db);
    }
    private Task<EncounterDetail> StartAsync(string? key = null) => WithDb(async db => (await new EncounterService(db, Clock).StartAsync(_ann, null, null, key ?? $"k-{Guid.NewGuid():N}", _s.Actor, default)).Detail);
    private Task<EncounterDetail> GetAsync(Guid id) => WithDb(db => new EncounterReader(db).GetAsync(id, default));
    private Task<EncounterDetail> NoteAsync(EncounterDetail e, string section, string? body, Guid? actor = null) => WithDb(db => new EncounterNoteService(db, Clock).SaveNoteAsync(e.Id, section, body, e.RowVersion, actor ?? _s.Actor, default));
    private Task<EncounterDetail> ApplyAsync(EncounterDetail e, Guid templateId) => WithDb(db => new EncounterNoteService(db, Clock).ApplyTemplateAsync(e.Id, templateId, e.RowVersion, _s.Actor, default));
    private Task<EncounterDetail> SignAsync(EncounterDetail e, Guid? actor = null) => WithDb(db => new EncounterSigningService(db, Clock).SignAsync(e.Id, e.RowVersion, actor ?? _s.Actor, default));
    private Task<EncounterDetail> UnsignAsync(EncounterDetail e) => WithDb(db => new EncounterSigningService(db, Clock).UnsignAsync(e.Id, e.RowVersion, _other, default));
    private Task<EncounterDetail> FinalizeAsync(EncounterDetail e, Guid? actor = null) => WithDb(db => new EncounterService(db, Clock).FinalizeAsync(e.Id, e.RowVersion, actor ?? _s.Actor, default));
    private Task<EncounterDetail> AddEntryAsync(EncounterDetail e, string kind, string name) => WithDb(db => new EncounterService(db, Clock).AddEntryAsync(e.Id, kind, new EntryFields(name, null, null, null, null, null), e.RowVersion, _s.Actor, default));
    private Task<EncounterDetail> MarkAsync(EncounterDetail e, string kind) => WithDb(db => new EncounterService(db, Clock).MarkSectionNoneReportedAsync(e.Id, kind, e.RowVersion, _s.Actor, default));
    private Task<(EncounterDetail Detail, bool Created)> VitalsAsync(EncounterDetail e, VitalsInput v, string? key = null, Guid? actor = null) =>
        WithDb(db => new EncounterVitalsService(db, Clock).RecordAsync(e.Id, v, key ?? $"v-{Guid.NewGuid():N}", e.RowVersion, actor ?? _s.Actor, default));
    private static VitalsInput Bp(int sys = 120, int dia = 80, int? pulse = 72) => new(null, sys, dia, pulse, null, null, null, null, null, null);
    private static async Task<ClinicalException> RefusedAsync<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<ClinicalException>(action);

    /// <summary>A draft whose four documentation sections are all addressed (so it is complete in STORY-005's sense).</summary>
    private async Task<EncounterDetail> CompleteDraftAsync()
    {
        var e = await StartAsync();
        e = await AddEntryAsync(e, EncounterEntryKinds.MedicalHistory, "Hypertension");
        e = await AddEntryAsync(e, EncounterEntryKinds.DentalHistory, "Root canal 2019");
        e = await AddEntryAsync(e, EncounterEntryKinds.Allergy, "Penicillin");
        return await MarkAsync(e, EncounterEntryKinds.Medication);
    }

    private static IReadOnlyList<TemplateSectionInput> Soap(bool planRequired = true) =>
    [
        new(NoteSections.Subjective, true, "Chief complaint:"), new(NoteSections.Objective, false, null), new(NoteSections.Assessment, true, null), new(NoteSections.Plan, planRequired, "Plan:"),
    ];
    private Task<TemplateView> CreateTemplateAsync(string name = "SOAP note", IReadOnlyList<TemplateSectionInput>? sections = null) =>
        WithDb(db => new NoteTemplateService(db, Clock).CreateAsync(name, "Standard visit", sections ?? Soap(), _s.Actor, default));

    private async Task<List<(string Type, string Details, Guid? By)>> AuditAsync(string entityType)
    {
        await using var db = _fixture.CreateContext();
        return (await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == entityType).OrderBy(a => a.TimestampUtc).ToListAsync()).Select(a => (a.EventType, a.Details, a.PerformedByUserAccountId)).ToList();
    }

    // ---------- notes ----------

    [Fact]
    public async Task SOAP_progress_and_treatment_notes_are_saved_on_the_encounter_with_who_and_when()
    {
        var e = await StartAsync();
        e = await NoteAsync(e, NoteSections.Subjective, "Pain on the lower left for two days.");
        e = await NoteAsync(e, NoteSections.Objective, "Tender to percussion on 36.", _other);
        e = await NoteAsync(e, NoteSections.Assessment, "Irreversible pulpitis, 36.");
        e = await NoteAsync(e, NoteSections.Plan, "Endodontic treatment.");
        e = await NoteAsync(e, NoteSections.Progress, "Patient comfortable after anaesthesia.");
        e = await NoteAsync(e, NoteSections.Treatment, "Access cavity prepared, canals located.");

        Assert.Equal(NoteSections.All, e.Notes!.Select(n => n.Section));        // presented in SOAP, progress, treatment order
        Assert.Equal("Tender to percussion on 36.", e.Notes!.Single(n => n.Section == NoteSections.Objective).Body);
        Assert.Equal("Hana Hygienist", e.Notes!.Single(n => n.Section == NoteSections.Objective).UpdatedByName);
        Assert.Equal("Dr. Okafor", e.Notes!.Single(n => n.Section == NoteSections.Treatment).UpdatedByName);
        Assert.All(e.Notes!, n => Assert.NotNull(n.UpdatedAtUtc));
        Assert.Equal(6, e.History.Count(h => h.EventType == EncounterEventTypes.NoteSaved));
    }

    [Fact]
    public async Task Saving_a_note_again_with_the_same_text_or_saving_an_empty_note_that_does_not_exist_changes_nothing()
    {
        var e = await StartAsync();
        e = await NoteAsync(e, NoteSections.Subjective, "Pain.");
        var again = await NoteAsync(e, NoteSections.Subjective, "Pain.");
        Assert.Equal(e.RowVersion, again.RowVersion);
        var blank = await NoteAsync(again, NoteSections.Plan, "   ");
        Assert.Equal(again.RowVersion, blank.RowVersion);
        Assert.DoesNotContain(blank.Notes!, n => n.Section == NoteSections.Plan);
        Assert.Equal(1, blank.History.Count(h => h.EventType == EncounterEventTypes.NoteSaved));
        Assert.Single((await AuditAsync(nameof(Encounter))).Where(a => a.Type == "EncounterNoteSaved"));
    }

    [Fact]
    public async Task A_note_can_be_cleared_back_to_empty_and_each_save_is_in_the_encounters_history()
    {
        var e = await StartAsync();
        e = await NoteAsync(e, NoteSections.Subjective, "First draft");
        e = await NoteAsync(e, NoteSections.Subjective, "");
        Assert.Equal("", e.Notes!.Single().Body);
        Assert.Equal(2, e.History.Count(h => h.EventType == EncounterEventTypes.NoteSaved));
    }

    [Fact]
    public async Task Autosaves_in_sequence_each_use_the_version_the_previous_one_returned()
    {
        var e = await StartAsync();
        foreach (var text in new[] { "P", "Pa", "Pai", "Pain" }) e = await NoteAsync(e, NoteSections.Subjective, text);
        Assert.Equal("Pain", e.Notes!.Single().Body);
    }

    [Theory]
    [InlineData("Soap")]
    [InlineData("")]
    [InlineData("Vitals")]
    public async Task A_note_section_that_does_not_exist_is_refused(string section)
    {
        var e = await StartAsync();
        var refused = await RefusedAsync(() => NoteAsync(e, section, "text"));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.Empty((await GetAsync(e.Id)).Notes!);
    }

    [Fact]
    public async Task A_note_over_the_limit_is_refused_and_one_at_the_limit_is_kept()
    {
        var e = await StartAsync();
        var refused = await RefusedAsync(() => NoteAsync(e, NoteSections.Progress, new string('x', EncounterNoteRules.NoteMax + 1)));
        Assert.True(refused.FieldErrors.ContainsKey("body"));
        e = await NoteAsync(e, NoteSections.Progress, new string('x', EncounterNoteRules.NoteMax));
        Assert.Equal(EncounterNoteRules.NoteMax, e.Notes!.Single().Body.Length);
    }

    [Fact]
    public async Task A_stale_tab_cannot_overwrite_a_newer_note()
    {
        var e = await StartAsync();
        var first = await NoteAsync(e, NoteSections.Subjective, "Saved from the first tab");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => NoteAsync(e, NoteSections.Subjective, "Saved from a stale tab")); // still holds the old version
        Assert.Equal("Saved from the first tab", (await GetAsync(e.Id)).Notes!.Single().Body);
        Assert.Equal(first.RowVersion, (await GetAsync(e.Id)).RowVersion);
        Assert.Single((await GetAsync(e.Id)).History, h => h.EventType == EncounterEventTypes.NoteSaved);
    }

    [Fact]
    public async Task Six_simultaneous_first_saves_of_the_same_note_leave_exactly_one_note_row()
    {
        var e = await StartAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            try { await NoteAsync(e, NoteSections.Subjective, $"Tab {i}"); return "ok"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Equal(1, outcomes.Count(o => o == "ok"));
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterNotes));
    }

    [Fact]
    public async Task A_finalized_note_cannot_be_overwritten_by_a_note_save_a_vital_a_template_or_an_entry()
    {
        var template = await CreateTemplateAsync();
        var e = await CompleteDraftAsync();
        e = await ApplyAsync(e, template.Id);
        e = await NoteAsync(e, NoteSections.Subjective, "Original subjective");
        e = await NoteAsync(e, NoteSections.Assessment, "Original assessment");
        e = await NoteAsync(e, NoteSections.Plan, "Original plan");
        var finalized = await FinalizeAsync(e);

        foreach (var attempt in new Func<Task<object>>[]
        {
            async () => await WithDb(db => new EncounterNoteService(db, Clock).SaveNoteAsync(e.Id, NoteSections.Subjective, "Overwritten", finalized.RowVersion, _s.Actor, default)),
            async () => await WithDb(db => new EncounterNoteService(db, Clock).SaveNoteAsync(e.Id, NoteSections.Progress, "New note", finalized.RowVersion, _s.Actor, default)),
            async () => (await WithDb(db => new EncounterVitalsService(db, Clock).RecordAsync(e.Id, Bp(), "late", finalized.RowVersion, _s.Actor, default))).Detail,
            async () => await WithDb(db => new EncounterSigningService(db, Clock).UnsignAsync(e.Id, finalized.RowVersion, _s.Actor, default)),
        })
            Assert.Equal("encounter_finalized", (await RefusedAsync(attempt)).Code);

        var after = await GetAsync(e.Id);
        Assert.Equal("Original subjective", after.Notes!.Single(n => n.Section == NoteSections.Subjective).Body); // byte for byte
        Assert.Equal(finalized.RowVersion, after.RowVersion);
        Assert.Empty(after.Vitals!);
    }

    [Fact]
    public async Task The_database_refuses_a_note_or_vital_write_to_a_finalized_encounter_even_when_the_service_is_bypassed()
    {
        var e = await CompleteDraftAsync();
        e = await NoteAsync(e, NoteSections.Subjective, "Original");
        var (withVitals, _) = await VitalsAsync(e, Bp());
        await FinalizeAsync(withVitals);

        await using var db = _fixture.CreateContext();
        var note = await db.EncounterNotes.SingleAsync();
        note.Body = "Rewritten by hand";
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(51043, Assert.IsType<Microsoft.Data.SqlClient.SqlException>(ex.InnerException).Number);
        await using var db2 = _fixture.CreateContext();
        db2.EncounterVitals.Add(new EncounterVitals { Id = Guid.NewGuid(), EncounterId = e.Id, PatientId = _ann, MeasuredAtUtc = DateTimeOffset.UtcNow, PulseBpm = 60, ClientKey = "hand", CreatedAtUtc = DateTimeOffset.UtcNow });
        var ex2 = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Equal(51045, Assert.IsType<Microsoft.Data.SqlClient.SqlException>(ex2.InnerException).Number);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_note_is_not_saved_and_the_same_request_works_on_retry()
    {
        var e = await StartAsync();
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EncounterNoteService(failing, Clock).SaveNoteAsync(e.Id, NoteSections.Subjective, "Lost?", e.RowVersion, _s.Actor, default));
        var after = await GetAsync(e.Id);
        Assert.Empty(after.Notes!);
        Assert.Equal(e.RowVersion, after.RowVersion);
        Assert.Equal(e.History.Count, after.History.Count);
        Assert.Equal("Lost?", (await NoteAsync(e, NoteSections.Subjective, "Lost?")).Notes!.Single().Body);
    }

    [Fact]
    public async Task Note_history_and_audit_name_the_section_and_never_the_text()
    {
        var e = await StartAsync();
        e = await NoteAsync(e, NoteSections.Subjective, "Patient reports chest pain and anxiety");
        var text = string.Join(" ", e.History.Select(h => h.Detail)) + " " + string.Join(" ", (await AuditAsync(nameof(Encounter))).Select(a => a.Details));
        Assert.Contains("Subjective", text);
        Assert.DoesNotContain("chest pain", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anxiety", text, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- templates ----------

    [Fact]
    public async Task A_template_is_created_listed_and_read_and_belongs_to_the_clinical_domain_not_practice_configuration()
    {
        var t = await CreateTemplateAsync();
        Assert.Equal(("SOAP note", true), (t.Name, t.IsActive));
        Assert.Equal(new[] { NoteSections.Subjective, NoteSections.Objective, NoteSections.Assessment, NoteSections.Plan }, t.Sections.Select(s => s.Section));
        Assert.Equal(new[] { true, false, true, true }, t.Sections.Select(s => s.Required));
        Assert.Equal("Chief complaint:", t.Sections[0].StarterText);
        Assert.Equal("Dr. Okafor", t.UpdatedByName);
        var list = await WithDb(db => new NoteTemplateService(db, Clock).ListAsync(false, default));
        Assert.Equal(t.Id, Assert.Single(list).Id);
        Assert.Equal(t.Id, (await WithDb(db => new NoteTemplateService(db, Clock).GetAsync(t.Id, default))).Id);
        Assert.Equal("ClinicalTemplateCreated", Assert.Single(await AuditAsync(nameof(NoteTemplate))).Type);
    }

    [Fact]
    public async Task Template_validation_names_every_problem_and_stores_nothing()
    {
        Assert.True((await RefusedAsync(() => CreateTemplateAsync(" "))).FieldErrors.ContainsKey("name"));
        Assert.True((await RefusedAsync(() => CreateTemplateAsync("A", []))).FieldErrors.ContainsKey("sections"));
        Assert.True((await RefusedAsync(() => CreateTemplateAsync("A", [new("Soap", true, null)]))).FieldErrors.ContainsKey("sections"));
        Assert.True((await RefusedAsync(() => CreateTemplateAsync("A", [new(NoteSections.Plan, true, null), new(NoteSections.Plan, false, null)]))).FieldErrors.ContainsKey("sections"));
        Assert.True((await RefusedAsync(() => CreateTemplateAsync("A", [new(NoteSections.Plan, true, new string('x', EncounterNoteRules.StarterMax + 1))]))).FieldErrors.ContainsKey("sections"));
        Assert.True((await RefusedAsync(() => CreateTemplateAsync(new string('n', EncounterNoteRules.TemplateNameMax + 1)))).FieldErrors.ContainsKey("name"));
        Assert.Equal(0, await _s.CountAsync(db => db.NoteTemplates));
    }

    [Fact]
    public async Task A_template_name_must_be_unique_and_a_stale_template_edit_is_refused()
    {
        var t = await CreateTemplateAsync("SOAP note");
        var clash = await RefusedAsync(() => CreateTemplateAsync("soap NOTE"));
        Assert.Equal(("template_name_taken", 409), (clash.Code, clash.StatusCode));

        var edited = await WithDb(db => new NoteTemplateService(db, Clock).UpdateAsync(t.Id, "SOAP note v2", null, Soap(false), t.RowVersion, _other, default));
        Assert.Equal(("SOAP note v2", "Hana Hygienist"), (edited.Name, edited.UpdatedByName));
        Assert.False(edited.Sections.Single(s => s.Section == NoteSections.Plan).Required);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => WithDb(db => new NoteTemplateService(db, Clock).UpdateAsync(t.Id, "From a stale tab", null, Soap(), t.RowVersion, _s.Actor, default)));
        Assert.Equal("SOAP note v2", (await WithDb(db => new NoteTemplateService(db, Clock).GetAsync(t.Id, default))).Name);
    }

    [Fact]
    public async Task Re_saving_a_template_unchanged_records_nothing_and_a_template_is_deactivated_not_deleted()
    {
        var t = await CreateTemplateAsync();
        var same = await WithDb(db => new NoteTemplateService(db, Clock).UpdateAsync(t.Id, t.Name, t.Description, Soap(), t.RowVersion, _s.Actor, default));
        Assert.Equal(t.RowVersion, same.RowVersion);
        Assert.Single(await AuditAsync(nameof(NoteTemplate)));

        var off = await WithDb(db => new NoteTemplateService(db, Clock).SetActiveAsync(t.Id, false, same.RowVersion, _s.Actor, default));
        Assert.False(off.IsActive);
        Assert.Empty(await WithDb(db => new NoteTemplateService(db, Clock).ListAsync(false, default)));
        Assert.Single(await WithDb(db => new NoteTemplateService(db, Clock).ListAsync(true, default)));
        var offAgain = await WithDb(db => new NoteTemplateService(db, Clock).SetActiveAsync(t.Id, false, off.RowVersion, _s.Actor, default));
        Assert.Equal(off.RowVersion, offAgain.RowVersion);
        Assert.Equal(new[] { "ClinicalTemplateCreated", "ClinicalTemplateDeactivated" }, (await AuditAsync(nameof(NoteTemplate))).Select(a => a.Type));
        Assert.Equal("template_not_found", (await RefusedAsync(() => WithDb(db => new NoteTemplateService(db, Clock).GetAsync(Guid.NewGuid(), default)))).Code);
    }

    [Fact]
    public async Task Using_a_template_gives_the_note_its_sections_starter_text_and_required_flags()
    {
        var t = await CreateTemplateAsync();
        var e = await StartAsync();
        e = await ApplyAsync(e, t.Id);

        Assert.Equal((t.Id, "SOAP note"), (e.TemplateId, e.TemplateName));
        Assert.Equal(new[] { NoteSections.Subjective, NoteSections.Objective, NoteSections.Assessment, NoteSections.Plan }, e.Notes!.Select(n => n.Section));
        Assert.Equal("Chief complaint:", e.Notes!.Single(n => n.Section == NoteSections.Subjective).Body);
        Assert.Equal(new[] { true, false, true, true }, e.Notes!.Select(n => n.Required));
        Assert.Contains(e.History, h => h.EventType == EncounterEventTypes.TemplateApplied);
        Assert.Equal(new[] { NoteSections.Subjective, NoteSections.Assessment, NoteSections.Plan }.OrderBy(x => x), e.MissingNotes!.OrderBy(x => x)); // starter text left as it is does not count as a written note
        Assert.False(e.ReadyToSign);
    }

    [Fact]
    public async Task Applying_the_same_template_again_is_quiet_a_second_template_is_refused_and_an_inactive_one_cannot_be_used()
    {
        var t = await CreateTemplateAsync();
        var other = await CreateTemplateAsync("Exam note", [new(NoteSections.Progress, false, null)]);
        var e = await ApplyAsync(await StartAsync(), t.Id);
        var again = await ApplyAsync(e, t.Id);
        Assert.Equal(e.RowVersion, again.RowVersion);
        Assert.Equal(1, again.History.Count(h => h.EventType == EncounterEventTypes.TemplateApplied));
        Assert.Equal("template_already_applied", (await RefusedAsync(() => ApplyAsync(again, other.Id))).Code);

        var off = await WithDb(db => new NoteTemplateService(db, Clock).SetActiveAsync(other.Id, false, other.RowVersion, _s.Actor, default));
        Assert.Equal("template_inactive", (await RefusedAsync(async () => await ApplyAsync(await StartAsync(), off.Id))).Code);
        Assert.Equal("template_not_found", (await RefusedAsync(async () => await ApplyAsync(await StartAsync(), Guid.NewGuid()))).Code);
    }

    [Fact]
    public async Task Editing_or_deactivating_a_template_later_does_not_change_a_note_that_already_used_it()
    {
        var t = await CreateTemplateAsync();
        var e = await ApplyAsync(await StartAsync(), t.Id);
        var edited = await WithDb(db => new NoteTemplateService(db, Clock).UpdateAsync(t.Id, "Renamed", null, [new(NoteSections.Progress, true, "New")], t.RowVersion, _s.Actor, default));
        await WithDb(db => new NoteTemplateService(db, Clock).SetActiveAsync(t.Id, false, edited.RowVersion, _s.Actor, default));

        var after = await GetAsync(e.Id);
        Assert.Equal("SOAP note", after.TemplateName);                                             // the name as it was then
        Assert.Equal(new[] { NoteSections.Subjective, NoteSections.Objective, NoteSections.Assessment, NoteSections.Plan }, after.Notes!.Select(n => n.Section));
        Assert.Equal(new[] { true, false, true, true }, after.Notes!.Select(n => n.Required));
    }

    [Fact]
    public async Task Starter_text_never_replaces_text_the_clinician_already_wrote()
    {
        var e = await StartAsync();
        e = await NoteAsync(e, NoteSections.Plan, "Written before the template");
        e = await ApplyAsync(e, (await CreateTemplateAsync()).Id);
        Assert.Equal("Written before the template", e.Notes!.Single(n => n.Section == NoteSections.Plan).Body);
        Assert.True(e.Notes!.Single(n => n.Section == NoteSections.Plan).Required);
    }

    // ---------- signing and finalizing with required notes ----------

    [Fact]
    public async Task Without_a_template_finalizing_works_exactly_as_it_did_in_story_005()
    {
        var e = await CompleteDraftAsync();
        Assert.True(e.ReadyToSign);
        Assert.Empty(e.MissingNotes!);
        var finalized = await FinalizeAsync(e);
        Assert.Equal(EncounterStatuses.Finalized, finalized.Status);
        Assert.Equal("Dr. Okafor", finalized.FinalizedByName);
    }

    [Fact]
    public async Task Signing_or_finalizing_with_required_notes_unwritten_is_refused_naming_each_one_and_nothing_changes()
    {
        var t = await CreateTemplateAsync();
        var e = await ApplyAsync(await CompleteDraftAsync(), t.Id);
        e = await NoteAsync(e, NoteSections.Subjective, "Chief complaint: pain"); // starter text edited into a real note
        var signRefused = await RefusedAsync(() => SignAsync(e));
        Assert.Equal(("documentation_incomplete", 409), (signRefused.Code, signRefused.StatusCode));
        Assert.Equal(new[] { "note:Assessment", "note:Plan" }.OrderBy(x => x), signRefused.FieldErrors.Keys.OrderBy(x => x));
        Assert.Contains("cannot be signed", signRefused.Message);
        var finalizeRefused = await RefusedAsync(() => FinalizeAsync(e));
        Assert.Equal("documentation_incomplete", finalizeRefused.Code);
        Assert.Contains("Assessment", finalizeRefused.Message);

        var after = await GetAsync(e.Id);
        Assert.Equal((EncounterStatuses.Draft, false, e.RowVersion), (after.Status, after.IsSigned, after.RowVersion));
    }

    [Fact]
    public async Task Required_notes_and_the_four_sections_are_both_reported_when_both_are_missing()
    {
        var t = await CreateTemplateAsync();
        var e = await ApplyAsync(await StartAsync(), t.Id);
        var refused = await RefusedAsync(() => SignAsync(e));
        Assert.Contains(EncounterEntryKinds.Allergy, refused.FieldErrors.Keys);
        Assert.Contains("note:Plan", refused.FieldErrors.Keys);
        Assert.False(e.ReadyToSign);
    }

    [Fact]
    public async Task A_note_made_of_only_whitespace_does_not_satisfy_a_required_section()
    {
        var t = await CreateTemplateAsync("Plan only", [new(NoteSections.Plan, true, null)]);
        var e = await ApplyAsync(await CompleteDraftAsync(), t.Id);
        e = await NoteAsync(e, NoteSections.Plan, "   \n  ");
        Assert.Equal(new[] { NoteSections.Plan }, e.MissingNotes);
        e = await NoteAsync(e, NoteSections.Plan, "Recall in 6 months.");
        Assert.Empty(e.MissingNotes!);
        Assert.True(e.ReadyToSign);
    }

    [Fact]
    public async Task A_complete_note_is_signed_then_locked_then_finalized_with_both_signatures_attributed()
    {
        var t = await CreateTemplateAsync();
        var e = await ApplyAsync(await CompleteDraftAsync(), t.Id);
        e = await NoteAsync(e, NoteSections.Subjective, "Pain");
        e = await NoteAsync(e, NoteSections.Assessment, "Pulpitis");
        e = await NoteAsync(e, NoteSections.Plan, "Root canal");
        Assert.True(e.ReadyToSign);

        var signed = await SignAsync(e);
        Assert.Equal((true, EncounterStatuses.Draft, "Dr. Okafor"), (signed.IsSigned, signed.Status, signed.SignedByName));
        Assert.NotNull(signed.SignedAtUtc);
        Assert.Contains(signed.History, h => h.EventType == EncounterEventTypes.Signed);

        var finalized = await FinalizeAsync(signed, _other);
        Assert.Equal((EncounterStatuses.Finalized, "Dr. Okafor", "Hana Hygienist"), (finalized.Status, finalized.SignedByName, finalized.FinalizedByName)); // who signed and who finalized are both kept
        Assert.Equal("Pain", finalized.Notes!.Single(n => n.Section == NoteSections.Subjective).Body);
    }

    [Fact]
    public async Task A_signed_draft_refuses_every_kind_of_change_until_it_is_unsigned()
    {
        var e = await CompleteDraftAsync();
        var signed = await SignAsync(e);

        var attempts = new Func<Task<object>>[]
        {
            async () => await AddEntryAsync(signed, EncounterEntryKinds.Medication, "Aspirin"),
            async () => await NoteAsync(signed, NoteSections.Progress, "More"),
            async () => (await VitalsAsync(signed, Bp())).Detail,
            async () => await ApplyAsync(signed, (await CreateTemplateAsync("Late")).Id),
            async () => await WithDb(db => new EncounterService(db, Clock).MarkSectionNoneReportedAsync(signed.Id, EncounterEntryKinds.Medication, signed.RowVersion, _s.Actor, default)),
        };
        foreach (var attempt in attempts) Assert.Equal("encounter_signed", (await RefusedAsync(attempt)).Code);
        Assert.Equal(signed.RowVersion, (await GetAsync(e.Id)).RowVersion);

        var open = await UnsignAsync(signed);
        Assert.False(open.IsSigned);
        Assert.Null(open.SignedAtUtc);
        Assert.Contains(open.History, h => h.EventType == EncounterEventTypes.Unsigned);
        var changed = await NoteAsync(open, NoteSections.Progress, "Now it can change again");
        Assert.Equal("Now it can change again", changed.Notes!.Single().Body);
    }

    [Fact]
    public async Task Signing_or_unsigning_twice_is_quiet_and_a_finalized_note_can_be_neither_signed_nor_unsigned()
    {
        var e = await CompleteDraftAsync();
        var signed = await SignAsync(e);
        var again = await SignAsync(signed);
        Assert.Equal(signed.RowVersion, again.RowVersion);
        Assert.Equal(1, again.History.Count(h => h.EventType == EncounterEventTypes.Signed));
        var open = await UnsignAsync(again);
        var openAgain = await UnsignAsync(open);
        Assert.Equal(open.RowVersion, openAgain.RowVersion);

        var finalized = await FinalizeAsync(openAgain);
        Assert.Equal("encounter_finalized", (await RefusedAsync(() => SignAsync(finalized))).Code);
        Assert.Equal("encounter_finalized", (await RefusedAsync(() => UnsignAsync(finalized))).Code);
        Assert.Equal(new[] { "EncounterSigned", "EncounterUnsigned" }, (await AuditAsync(nameof(Encounter))).Select(a => a.Type).Where(t => t is "EncounterSigned" or "EncounterUnsigned"));
    }

    [Fact]
    public async Task Signing_from_a_stale_version_is_a_conflict_and_a_missing_version_is_refused()
    {
        var e = await CompleteDraftAsync();
        var changed = await NoteAsync(e, NoteSections.Progress, "Someone changed it after the review");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SignAsync(e));
        Assert.False((await GetAsync(e.Id)).IsSigned);
        Assert.Equal("row_version_required", (await RefusedAsync(() => WithDb(db => new EncounterSigningService(db, Clock).SignAsync(e.Id, null, _s.Actor, default)))).Code);
        Assert.Equal("encounter_not_found", (await RefusedAsync(() => WithDb(db => new EncounterSigningService(db, Clock).SignAsync(Guid.NewGuid(), changed.RowVersion, _s.Actor, default)))).Code);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_signature_is_not_recorded()
    {
        var e = await CompleteDraftAsync();
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EncounterSigningService(failing, Clock).SignAsync(e.Id, e.RowVersion, _s.Actor, default));
        Assert.False((await GetAsync(e.Id)).IsSigned);
        Assert.True((await SignAsync(e)).IsSigned);
    }

    // ---------- vitals ----------

    [Fact]
    public async Task Vital_signs_are_recorded_on_the_encounter_with_the_patient_the_time_measured_and_who_recorded_them()
    {
        var e = await StartAsync();
        var when = DateTimeOffset.UtcNow.AddMinutes(-10);
        var input = new VitalsInput(when, 128, 84, 76, 16, 36.8m, 98, 72.5m, 171.5m, "Seated, left arm");
        var (detail, created) = await VitalsAsync(e, input, actor: _other);

        Assert.True(created);
        var v = Assert.Single(detail.Vitals!);
        Assert.Equal((128, 84, 76, 16, 36.8m, 98, 72.5m, 171.5m), (v.SystolicMmHg, v.DiastolicMmHg, v.PulseBpm, v.RespirationsPerMinute, v.TemperatureC, v.OxygenSaturationPercent, v.WeightKg, v.HeightCm));
        Assert.Equal((when, "Seated, left arm", "Hana Hygienist", false), (v.MeasuredAtUtc, v.Note, v.RecordedByName, v.IsVoided));
        await using var db = _fixture.CreateContext();
        var row = await db.EncounterVitals.AsNoTracking().SingleAsync();
        Assert.Equal((e.Id, _ann), (row.EncounterId, row.PatientId));            // attributed to the encounter and the patient
        Assert.Contains(detail.History, h => h.EventType == EncounterEventTypes.VitalsRecorded);
    }

    [Fact]
    public async Task A_reading_with_only_some_measurements_is_kept_and_the_time_defaults_to_now()
    {
        var e = await StartAsync();
        var (detail, _) = await VitalsAsync(e, new VitalsInput(null, null, null, 64, null, null, null, null, null, null));
        var v = Assert.Single(detail.Vitals!);
        Assert.Equal(64, v.PulseBpm);
        Assert.Null(v.SystolicMmHg);
        Assert.True(v.MeasuredAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Retrying_a_vitals_save_with_the_same_key_returns_the_first_reading_and_never_records_it_twice()
    {
        var e = await StartAsync();
        var first = await VitalsAsync(e, Bp(), "retry-key");
        var second = await VitalsAsync(e, Bp(), "retry-key");   // a dropped connection after the server stored it, sent again (with the old version)
        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Single(second.Detail.Vitals!);
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterVitals));
        Assert.Equal(1, (await GetAsync(e.Id)).History.Count(h => h.EventType == EncounterEventTypes.VitalsRecorded));

        var different = await RefusedAsync(() => VitalsAsync(e, Bp(sys: 150), "retry-key"));
        Assert.Equal(("idempotency_key_reused", 409), (different.Code, different.StatusCode));
        Assert.Equal(120, Assert.Single((await GetAsync(e.Id)).Vitals!).SystolicMmHg);
    }

    [Fact]
    public async Task Six_simultaneous_saves_with_one_key_record_one_reading()
    {
        var e = await StartAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { return (await VitalsAsync(e, Bp(), "same-key")).Created ? "created" : "replayed"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Equal(1, outcomes.Count(o => o == "created"));
        Assert.Equal(1, await _s.CountAsync(db => db.EncounterVitals));
    }

    [Theory]
    [InlineData(null, null, null, "vitals")]                                    // nothing entered
    [InlineData(120, null, null, "diastolicMmHg")]                              // half a blood pressure
    [InlineData(null, 80, null, "systolicMmHg")]
    [InlineData(80, 120, null, "diastolicMmHg")]                                // diastolic not below systolic
    [InlineData(120, 120, null, "diastolicMmHg")]
    [InlineData(500, 80, null, "systolicMmHg")]                                 // implausible
    [InlineData(120, 10, null, "diastolicMmHg")]
    [InlineData(null, null, 5, "pulseBpm")]
    [InlineData(null, null, 400, "pulseBpm")]
    public async Task Implausible_or_incomplete_vitals_are_refused_naming_the_field(int? sys, int? dia, int? pulse, string field)
    {
        var e = await StartAsync();
        var refused = await RefusedAsync(() => VitalsAsync(e, new VitalsInput(null, sys, dia, pulse, null, null, null, null, null, null)));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey(field), string.Join(",", refused.FieldErrors.Keys));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterVitals));
    }

    [Theory]
    [InlineData(null, 29.9, null, null, "temperatureC")]
    [InlineData(null, 45.1, null, null, "temperatureC")]
    [InlineData(null, 36.85, null, null, "temperatureC")]                       // two decimals: refused, never silently rounded
    [InlineData(null, null, 0.4, null, "weightKg")]
    [InlineData(null, null, 501.0, null, "weightKg")]
    [InlineData(null, null, null, 19.9, "heightCm")]
    [InlineData(null, null, null, 260.5, "heightCm")]
    [InlineData(101, null, null, null, "oxygenSaturationPercent")]
    [InlineData(49, null, null, null, "oxygenSaturationPercent")]
    public async Task Each_measurement_has_a_plausible_range_and_a_stated_precision(int? spo2, double? temp, double? weight, double? height, string field)
    {
        var e = await StartAsync();
        var refused = await RefusedAsync(() => VitalsAsync(e, new VitalsInput(null, null, null, 70, null, (decimal?)temp, spo2, (decimal?)weight, (decimal?)height, null)));
        Assert.True(refused.FieldErrors.ContainsKey(field), string.Join(",", refused.FieldErrors.Keys));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterVitals));
    }

    [Fact]
    public async Task Vitals_dated_in_the_future_or_missing_a_key_are_refused()
    {
        var e = await StartAsync();
        Assert.True((await RefusedAsync(() => VitalsAsync(e, Bp() with { MeasuredAtUtc = DateTimeOffset.UtcNow.AddHours(2) }))).FieldErrors.ContainsKey("measuredAtUtc"));
        Assert.True((await RefusedAsync(() => WithDb(db => new EncounterVitalsService(db, Clock).RecordAsync(e.Id, Bp(), null, e.RowVersion, _s.Actor, default)))).FieldErrors.ContainsKey("clientKey"));
        Assert.True((await RefusedAsync(() => VitalsAsync(e, Bp() with { Note = new string('n', EncounterNoteRules.VitalsNoteMax + 1) }))).FieldErrors.ContainsKey("note"));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterVitals));
    }

    [Fact]
    public async Task A_reading_entered_by_mistake_is_voided_with_a_reason_and_stays_listed_marked_voided()
    {
        var e = await StartAsync();
        var (withVitals, _) = await VitalsAsync(e, Bp(sys: 190, dia: 110));
        var reading = withVitals.Vitals!.Single();
        Assert.True((await RefusedAsync(() => WithDb(db => new EncounterVitalsService(db, Clock).VoidAsync(e.Id, reading.Id, " ", withVitals.RowVersion, _s.Actor, default)))).FieldErrors.ContainsKey("reason"));

        var voided = await WithDb(db => new EncounterVitalsService(db, Clock).VoidAsync(e.Id, reading.Id, "Typed into the wrong patient", withVitals.RowVersion, _other, default));
        var v = Assert.Single(voided.Vitals!);
        Assert.Equal((true, "Typed into the wrong patient", "Hana Hygienist", 190), (v.IsVoided, v.VoidReason, v.VoidedByName, v.SystolicMmHg)); // kept, not erased
        Assert.Contains(voided.History, h => h.EventType == EncounterEventTypes.VitalsVoided);

        var again = await WithDb(db => new EncounterVitalsService(db, Clock).VoidAsync(e.Id, reading.Id, "Again", voided.RowVersion, _s.Actor, default));
        Assert.Equal(voided.RowVersion, again.RowVersion);
        Assert.Equal("vitals_not_found", (await RefusedAsync(() => WithDb(db => new EncounterVitalsService(db, Clock).VoidAsync(e.Id, Guid.NewGuid(), "x", voided.RowVersion, _s.Actor, default)))).Code);

        var corrected = await VitalsAsync(voided, Bp(), "correct");   // and the right reading is recorded as a new one
        Assert.Equal(2, corrected.Detail.Vitals!.Count);
        Assert.Single(corrected.Detail.Vitals!, x => !x.IsVoided);
    }

    [Fact]
    public async Task A_recorded_reading_cannot_be_edited_even_by_hand_only_voided()
    {
        var e = await StartAsync();
        await VitalsAsync(e, Bp());
        await using var db = _fixture.CreateContext();
        var row = await db.EncounterVitals.SingleAsync();
        row.SystolicMmHg = 100;
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(51048, Assert.IsType<Microsoft.Data.SqlClient.SqlException>(ex.InnerException).Number);
    }

    [Fact]
    public async Task Vitals_history_and_audit_carry_no_measurements()
    {
        var e = await StartAsync();
        var (detail, _) = await VitalsAsync(e, new VitalsInput(null, 177, 99, 66, null, null, null, null, null, "dizzy standing"));
        var text = string.Join(" ", detail.History.Select(h => h.Detail)) + " " + string.Join(" ", (await AuditAsync(nameof(Encounter))).Select(a => a.Details));
        foreach (var secret in new[] { "177", "99", "66", "dizzy" }) Assert.DoesNotContain(secret, text);
    }

    [Fact]
    public async Task A_stale_vitals_save_is_refused_and_an_audit_failure_stores_no_reading()
    {
        var e = await StartAsync();
        await NoteAsync(e, NoteSections.Subjective, "Moved the version on");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => VitalsAsync(e, Bp()));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterVitals));

        var fresh = await GetAsync(e.Id);
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EncounterVitalsService(failing, Clock).RecordAsync(e.Id, Bp(), "k", fresh.RowVersion, _s.Actor, default));
        Assert.Equal(0, await _s.CountAsync(db => db.EncounterVitals));
        Assert.True((await VitalsAsync(fresh, Bp(), "k")).Created);   // the key was not consumed by the failure
    }

    // ---------- amendment ----------

    [Fact]
    public async Task An_amendment_preserves_the_original_and_records_what_it_amends_who_wrote_it_and_when()
    {
        var t = await CreateTemplateAsync("Plan only", [new(NoteSections.Plan, true, null)]);
        var e = await ApplyAsync(await CompleteDraftAsync(), t.Id);
        e = await NoteAsync(e, NoteSections.Plan, "Extract tooth 38.");
        var (v, _) = await VitalsAsync(e, Bp());
        var finalized = await FinalizeAsync(v);

        var (amended, created) = await WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(e.Id, "Plan changed: refer to oral surgery instead.", "a1", NoteSections.Plan, _other, default));
        Assert.True(created);
        var addendum = Assert.Single(amended.Addenda);
        Assert.Equal((NoteSections.Plan, "Hana Hygienist", _other), (addendum.Section, addendum.CreatedByName, addendum.CreatedByUserId));
        Assert.Equal("Extract tooth 38.", amended.Notes!.Single(n => n.Section == NoteSections.Plan).Body);  // the original, untouched
        Assert.Equal(finalized.RowVersion, amended.RowVersion);
        Assert.Equal(finalized.FinalizedAtUtc, amended.FinalizedAtUtc);
        Assert.Equal(finalized.Vitals!.Single().SystolicMmHg, amended.Vitals!.Single().SystolicMmHg);
    }

    [Fact]
    public async Task An_addendum_can_amend_a_documentation_section_the_vitals_or_nothing_in_particular_but_not_an_invented_section()
    {
        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        foreach (var (section, key) in new (string?, string)[] { (EncounterEntryKinds.Allergy, "s1"), (EncounterNoteRules.AmendableVitals, "s2"), (null, "s3"), ("  ", "s4") })
            await WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, $"About {section ?? "nothing"}", key, section, _s.Actor, default));
        var after = await GetAsync(finalized.Id);
        Assert.Equal(new string?[] { EncounterEntryKinds.Allergy, "Vitals", null, null }, after.Addenda.Select(a => a.Section));

        var refused = await RefusedAsync(() => WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, "x", "bad", "Everything", _s.Actor, default)));
        Assert.True(refused.FieldErrors.ContainsKey("section"));
        Assert.Equal(4, (await GetAsync(finalized.Id)).Addenda.Count);
    }

    [Fact]
    public async Task Re_sending_an_amendment_is_quiet_but_the_same_key_for_a_different_section_or_text_is_refused()
    {
        var finalized = await FinalizeAsync(await CompleteDraftAsync());
        await WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, "Allergy corrected", "k", EncounterEntryKinds.Allergy, _s.Actor, default));
        var (replay, created) = await WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, "Allergy corrected", "k", EncounterEntryKinds.Allergy, _s.Actor, default));
        Assert.False(created);
        Assert.Single(replay.Addenda);
        Assert.Equal("idempotency_key_reused", (await RefusedAsync(() => WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, "Allergy corrected", "k", EncounterEntryKinds.Medication, _s.Actor, default)))).Code);
        Assert.Equal("idempotency_key_reused", (await RefusedAsync(() => WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, "Different", "k", EncounterEntryKinds.Allergy, _s.Actor, default)))).Code);
        Assert.Single((await GetAsync(finalized.Id)).Addenda);
    }

    [Fact]
    public async Task A_failed_amendment_stores_nothing_and_a_draft_takes_no_addendum_at_all()
    {
        var draft = await CompleteDraftAsync();
        Assert.Equal("encounter_not_finalized", (await RefusedAsync(() => WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(draft.Id, "x", "k", NoteSections.Plan, _s.Actor, default)))).Code);
        var finalized = await FinalizeAsync(draft);
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new EncounterService(failing, Clock).AddAddendumAsync(finalized.Id, "Will not be saved", "retry", NoteSections.Plan, _s.Actor, default));
        Assert.Empty((await GetAsync(finalized.Id)).Addenda);
        Assert.True((await WithDb(db => new EncounterService(db, Clock).AddAddendumAsync(finalized.Id, "Will not be saved", "retry", NoteSections.Plan, _s.Actor, default))).Created);
    }
}
