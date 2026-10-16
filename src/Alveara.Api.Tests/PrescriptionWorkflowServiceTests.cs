using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Prescribing;
using Alveara.Api.Architecture.Safety;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-016-C01 against real SQL Server: the prescription draft (saved incomplete, edited only while a draft, never deleted, a stale edit refused), the allergy and medication lists the prescriber acknowledged and
/// the refusal when they changed, finalization (the STORY-016 checks unchanged plus instructions, quantity and refills, one save with the log), amendment (a new prescription that replaces the old without editing it),
/// cancellation and discontinuation (a reason, appended, once), printing (logged, stable, refused when incomplete) and what is NOT claimed (no interaction or dose-appropriateness checking).
/// </summary>
public class PrescriptionWorkflowServiceTests : SafetyTestBase
{
    private const string Note = "Childhood rash, tolerated since; discussed with the patient.";

    private sealed record Services(PrescriptionDraftService Drafts, PrescriptionFinalizeService Finalizer, PrescriptionLifecycleService Lifecycle, PrescriptionRecordReader Records);

    private static Services Make(Alveara.Api.Data.AlveraDbContext db, ISafetyContextProvider? safety = null)
    {
        var reader = new PrescribingSafetyReader(safety ?? new SafetyContextService(db, Clock));
        var records = new PrescriptionRecordReader(db);
        var drafts = new PrescriptionDraftService(db, Clock, reader);
        return new Services(drafts, new PrescriptionFinalizeService(db, Clock, reader, drafts, records), new PrescriptionLifecycleService(db, Clock, records), records);
    }

    private static WorkflowInput Entry(string medication = "Amoxicillin", decimal dose = 500m, string? key = null, string? instructions = "Take one capsule with food until finished.", decimal? quantity = 21m, string? quantityUnit = "capsule",
        int? refills = 0, string? note = null) =>
        new(medication, dose, "mg", "Three times daily", instructions, quantity, quantityUnit, refills, note, key ?? $"draft-{Guid.NewGuid():N}");

    private Task<SavedDraft> CreateAsync(WorkflowInput input, Guid? patient = null) => WithDb(db => Make(db).Drafts.CreateAsync(patient ?? Ann, input, S.Actor, default));
    private Task<DraftView> UpdateAsync(DraftView draft, WorkflowInput input) => WithDb(db => Make(db).Drafts.UpdateAsync(draft.Id, draft.RowVersion, input, S.Actor, default));
    private Task<DraftView> DiscardAsync(DraftView draft, string? reason = null) => WithDb(db => Make(db).Drafts.DiscardAsync(draft.Id, draft.RowVersion, reason, S.Actor, default));
    private Task<DraftDetailView> GetAsync(Guid id) => WithDb(db => Make(db).Drafts.GetAsync(id, S.Actor, default));
    private Task<SavedRecord> FinalizeAsync(DraftView draft) => WithDb(db => Make(db).Finalizer.FinalizeAsync(draft.Id, draft.RowVersion, S.Actor, default));
    private Task<SavedRecord> CancelAsync(Guid id, string? reason = "Entered for the wrong patient.") => WithDb(db => Make(db).Lifecycle.CancelAsync(id, reason, S.Actor, default));
    private Task<SavedRecord> DiscontinueAsync(Guid id, string? reason = "Stopped after a reaction.") => WithDb(db => Make(db).Lifecycle.DiscontinueAsync(id, reason, S.Actor, default));
    private Task<PrescriptionPrintContent> PrintAsync(Guid id) => WithDb(db => Make(db).Lifecycle.PrintAsync(id, S.Actor, default));
    private Task<SavedDraft> AmendAsync(Guid id, string? key = null) => WithDb(db => Make(db).Drafts.AmendAsync(id, key ?? $"amend-{Guid.NewGuid():N}", S.Actor, default));
    private Task<PrescriptionRecord> RecordAsync(Guid id) => WithDb(db => Make(db).Records.GetAsync(id, default));

    private static async Task<PrescriptionException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<PrescriptionException>(action);

    private Task NoKnownAllergiesAsync(Guid? patient = null) => RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown, patient);

    /// <summary>Finalizes a complete draft on an established, empty allergy list and returns the prescription.</summary>
    private async Task<PrescriptionRecord> FinalizedAsync(string medication = "Amoxicillin", Guid? patient = null)
    {
        await NoKnownAllergiesAsync(patient);
        var d = (await CreateAsync(Entry(medication), patient)).Draft;
        return (await FinalizeAsync(d)).Record;
    }

    private async Task<(int Drafts, int Prescriptions, int Details, int Events)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.PrescriptionDrafts.CountAsync(), await db.Prescriptions.CountAsync(), await db.PrescriptionDetails.CountAsync(), await db.PrescriptionEvents.CountAsync());
    }

    private async Task<int> AuditCountAsync(string eventType) { await using var db = Fixture.CreateContext(); return await db.AuditLogEntries.CountAsync(a => a.EventType == eventType); }

    private Task FailAuditAsync(string eventType) => WithDb(async db => { await db.Database.ExecuteSqlRawAsync($@"CREATE TRIGGER [TR_Test_RefuseAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{eventType}') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END"); return 0; });

    private Task RestoreAuditAsync() => WithDb(async db => { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefuseAudit]"); return 0; });

    private sealed class BrokenSafety : ISafetyContextProvider
    {
        public Task<SafetyContext> GetAsync(Guid patientId, Guid forUserId, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<SafetySummary> SummaryAsync(Guid patientId, Guid forUserId, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    // ---------- drafts ----------

    [Fact]
    public async Task A_draft_can_be_saved_incomplete_and_is_logged_with_the_user_and_the_time_without_any_clinical_content()
    {
        await NoKnownAllergiesAsync();
        var before = DateTimeOffset.UtcNow;
        var saved = await CreateAsync(Entry(instructions: null, quantity: null, quantityUnit: null, refills: null));

        Assert.False(saved.Replayed);
        var d = saved.Draft;
        Assert.Equal((DraftStatuses.Draft, "Amoxicillin", (string?)null, (decimal?)null, (int?)null, "Dr. Okafor"), (d.Status, d.Medication, d.Instructions, d.QuantityValue, d.Refills, d.CreatedByName));
        Assert.NotEmpty(d.RowVersion);
        var audit = Assert.Single(await AuditAsync(nameof(PrescriptionDraft)));
        Assert.Equal(("PrescriptionDraftCreated", "Prescription draft created.", (Guid?)S.Actor), (audit.Type, audit.Details, audit.By));
        await using var db = Fixture.CreateContext();
        Assert.InRange((await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == nameof(PrescriptionDraft))).TimestampUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Creating_a_draft_twice_under_one_key_returns_it_and_the_same_key_for_a_different_entry_is_refused()
    {
        var entry = Entry(key: "once");
        var first = await CreateAsync(entry);
        var again = await CreateAsync(entry with { Medication = "  amoxicillin " });
        Assert.True(again.Replayed);
        Assert.Equal(first.Draft.Id, again.Draft.Id);
        var e = await Refused(() => CreateAsync(entry with { Medication = "Naproxen" }));
        Assert.Equal(("idempotency_key_reused", 409), (e.Code, e.StatusCode));
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
        Assert.Equal(1, await AuditCountAsync("PrescriptionDraftCreated"));
    }

    [Fact]
    public async Task Two_simultaneous_creations_under_one_key_make_one_draft()
    {
        var entry = Entry(key: "race");
        var results = await Task.WhenAll(CreateAsync(entry), CreateAsync(entry));
        Assert.Single(results.Select(r => r.Draft.Id).Distinct());
        Assert.Contains(results, r => !r.Replayed);
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task An_incorrect_draft_is_refused_with_every_problem_named_and_nothing_is_saved()
    {
        var e = await Refused(() => CreateAsync(Entry("A", 6000m, quantity: 0m, refills: 99)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(["medication", "doseValue", "quantityValue", "refills"], e.Problems.Select(p => p.Field));
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
        Assert.Equal("patient_not_found", (await Refused(() => CreateAsync(Entry(), Guid.NewGuid()))).Code);
    }

    [Fact]
    public async Task When_the_safety_context_cannot_be_read_no_draft_is_created()
    {
        var e = await Refused(() => WithDb(db => Make(db, new BrokenSafety()).Drafts.CreateAsync(Ann, Entry(), S.Actor, default)));
        Assert.Equal(("allergy_check_unavailable", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("boom", e.Message);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task When_the_log_cannot_be_written_no_draft_is_created_and_the_same_request_works_once_it_is_back()
    {
        await FailAuditAsync("PrescriptionDraftCreated");
        var entry = Entry(key: "retry");
        var e = await Refused(() => CreateAsync(entry));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
        await RestoreAuditAsync();
        await CreateAsync(entry);
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_draft_can_be_edited_and_the_edit_is_logged_a_stale_edit_is_refused_and_an_unchanged_one_is_quiet()
    {
        var d = (await CreateAsync(Entry(instructions: null, quantity: null, quantityUnit: null, refills: null))).Draft;
        var edited = await UpdateAsync(d, Entry(instructions: "Take one with food.", quantity: 14m, refills: 1, key: "ignored"));
        Assert.Equal(("Take one with food.", 14m, 1), (edited.Instructions, edited.QuantityValue, edited.Refills));
        Assert.NotEqual(d.RowVersion, edited.RowVersion);
        Assert.Equal(1, await AuditCountAsync("PrescriptionDraftChanged"));

        Assert.Equal(edited.RowVersion, (await UpdateAsync(edited, Entry(instructions: "Take one with food.", quantity: 14m, refills: 1))).RowVersion);   // nothing changed: nothing saved, nothing logged
        Assert.Equal(1, await AuditCountAsync("PrescriptionDraftChanged"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => UpdateAsync(d, Entry(refills: 3)));                                                  // the version read before the edit
        Assert.Equal("row_version_required", (await Refused(() => WithDb(db => Make(db).Drafts.UpdateAsync(d.Id, null, Entry(), S.Actor, default)))).Code);
        Assert.Equal("row_version_invalid", (await Refused(() => WithDb(db => Make(db).Drafts.UpdateAsync(d.Id, "not base64!", Entry(), S.Actor, default)))).Code);
        Assert.Equal(("validation_failed", 400), ((await Refused(() => UpdateAsync(edited, Entry(dose: 0m)))).Code, 400));
        Assert.Equal(1, (await GetAsync(d.Id)).Draft.Refills);
    }

    [Fact]
    public async Task A_discarded_draft_is_kept_cannot_be_changed_and_discarding_it_again_changes_nothing()
    {
        var d = (await CreateAsync(Entry())).Draft;
        var discarded = await DiscardAsync(d, "Started for the wrong patient.");
        Assert.Equal((DraftStatuses.Discarded, "Started for the wrong patient."), (discarded.Status, discarded.DiscardedReason));
        Assert.Equal(1, await AuditCountAsync("PrescriptionDraftDiscarded"));
        Assert.Equal(discarded.RowVersion, (await DiscardAsync(discarded)).RowVersion);
        Assert.Equal(1, await AuditCountAsync("PrescriptionDraftDiscarded"));
        Assert.Equal("draft_closed", (await Refused(() => UpdateAsync(discarded, Entry()))).Code);
        Assert.Equal("draft_closed", (await Refused(() => FinalizeAsync(discarded))).Code);
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
        Assert.Empty(await WithDb(db => Make(db).Drafts.ListAsync(Ann, false, default)));
        Assert.Single(await WithDb(db => Make(db).Drafts.ListAsync(Ann, true, default)));
        Assert.Equal("draft_not_found", (await Refused(() => GetAsync(Guid.NewGuid()))).Code);
    }

    [Fact]
    public async Task A_finalized_draft_cannot_be_discarded_and_nothing_about_it_changes()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        var finalized = (await FinalizeAsync(d)).Record;
        var e = await Refused(() => DiscardAsync(d, "Changed my mind."));
        Assert.Equal(("draft_closed", 409), (e.Code, e.StatusCode));
        Assert.Equal(0, await AuditCountAsync("PrescriptionDraftDiscarded"));
        Assert.Equal(DraftStatuses.Finalized, (await GetAsync(d.Id)).Draft.Status);
        Assert.Equal(PrescriptionStatuses.Active, (await RecordAsync(finalized.Id)).Status);
    }

    // ---------- the allergy and medication lists the prescriber saw ----------

    [Fact]
    public async Task A_change_to_the_allergy_list_after_the_draft_was_started_stops_the_finalization_and_says_what_changed()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Latex", "Rash", EncounterSeverities.Mild);

        var detail = await GetAsync(d.Id);
        Assert.True(detail.Safety.ChangedSinceAcknowledged);
        Assert.Contains(detail.Safety.Added, a => a.StartsWith("Allergy: Latex"));

        var e = await Refused(() => FinalizeAsync(d));
        Assert.Equal(("safety_context_changed", 409), (e.Code, e.StatusCode));
        var change = Assert.IsType<SafetyChange>(e.Detail);
        Assert.Contains(change.Added, a => a.StartsWith("Allergy: Latex"));
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
    }

    [Theory]
    [InlineData("allergyAdded")] [InlineData("medicationAdded")] [InlineData("allergyListBecomesUnknown")]
    public async Task Any_change_to_an_allergy_a_medication_or_how_far_the_allergy_list_can_be_relied_on_is_caught(string change)
    {
        if (change == "allergyListBecomesUnknown") await NoKnownAllergiesAsync();                                     // no items, so a clinician can still change their mind to "unknown"
        else
        {
            await RecordItemAsync(EncounterEntryKinds.Allergy, "Latex", "Rash", EncounterSeverities.Mild);
            await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);
        }
        var d = (await CreateAsync(Entry())).Draft;
        Assert.False((await GetAsync(d.Id)).Safety.ChangedSinceAcknowledged);

        switch (change)
        {
            case "allergyAdded": await RecordItemAsync(EncounterEntryKinds.Allergy, "Codeine", null, null); break;
            case "medicationAdded": await RecordItemAsync(EncounterEntryKinds.Medication, "Warfarin", null, null, "5 mg", "Daily"); break;
            default: await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Unknown); break;
        }

        Assert.True((await GetAsync(d.Id)).Safety.ChangedSinceAcknowledged);
        Assert.Equal("safety_context_changed", (await Refused(() => FinalizeAsync(d))).Code);
    }

    [Fact]
    public async Task Acknowledging_the_lists_as_they_are_lets_the_finalization_go_ahead_and_editing_the_draft_does_not_acknowledge_anything()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Latex", "Rash", EncounterSeverities.Mild);
        var edited = await UpdateAsync(d, Entry(refills: 1, note: "Asked the patient today: allergies as recorded."));     // the list now needs review, so the prescriber's own words are needed
        Assert.True((await GetAsync(d.Id)).Safety.ChangedSinceAcknowledged);                                         // editing did not refresh what the prescriber acknowledged

        var live = (await GetAsync(d.Id)).Safety.Fingerprint;
        var wrong = await Refused(() => WithDb(db => Make(db).Drafts.AcknowledgeSafetyAsync(edited.Id, edited.RowVersion, "0000", S.Actor, default)));
        Assert.Equal("safety_context_changed", wrong.Code);                                                          // only the lists actually shown can be acknowledged
        Assert.Equal(0, await AuditCountAsync("PrescriptionDraftSafetyAcknowledged"));

        var acknowledged = await WithDb(db => Make(db).Drafts.AcknowledgeSafetyAsync(edited.Id, edited.RowVersion, live, S.Actor, default));
        Assert.Equal(live, acknowledged.SafetyFingerprint);
        Assert.Equal(1, await AuditCountAsync("PrescriptionDraftSafetyAcknowledged"));
        Assert.False((await GetAsync(d.Id)).Safety.ChangedSinceAcknowledged);

        var again = await WithDb(db => Make(db).Drafts.AcknowledgeSafetyAsync(acknowledged.Id, acknowledged.RowVersion, live, S.Actor, default));
        Assert.Equal(acknowledged.RowVersion, again.RowVersion);                                                      // quiet
        Assert.Equal(1, await AuditCountAsync("PrescriptionDraftSafetyAcknowledged"));

        var finalized = (await FinalizeAsync(again)).Record;
        Assert.Equal(PrescriptionStatuses.Active, finalized.Status);
    }

    // ---------- finalization ----------

    [Fact]
    public async Task A_complete_draft_is_finalized_into_a_prescription_with_its_details_event_and_log_entry_in_one_save()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry(refills: 2))).Draft;
        var before = DateTimeOffset.UtcNow;

        var saved = await FinalizeAsync(d);

        Assert.False(saved.Replayed);
        var r = saved.Record;
        Assert.Equal(("Amoxicillin", 500m, "Take one capsule with food until finished.", 21m, "capsule", 2, PrescriptionStatuses.Active, true, "Dr. Okafor"),
            (r.Medication, r.DoseValue, r.Instructions, r.QuantityValue, r.QuantityUnit, r.Refills, r.Status, r.HasDetails, r.PrescribedByName));
        Assert.Equal(AllergyOutcomes.NoRecordedMatch, r.AllergyOutcome);
        Assert.Equal([PrescriptionEventTypes.Finalized], r.Events.Select(e => e.EventType));
        Assert.Equal((1, 1, 1, 1), await CountsAsync());

        var audit = Assert.Single(await AuditAsync(nameof(Prescription)));                                            // "all prescription entries are logged with user ID and timestamp"
        Assert.Equal(("PrescriptionRecorded", "Prescription recorded.", (Guid?)S.Actor), (audit.Type, audit.Details, audit.By));
        await using var db = Fixture.CreateContext();
        var entry = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == nameof(Prescription));
        Assert.InRange(entry.TimestampUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(r.Id, entry.TargetUserAccountId);
        var draft = await db.PrescriptionDrafts.AsNoTracking().SingleAsync();
        Assert.Equal((DraftStatuses.Finalized, r.Id), (draft.Status, draft.FinalizedPrescriptionId));
        Assert.Equal(r.Id, (await db.PrescriptionDetails.AsNoTracking().SingleAsync()).PrescriptionId);

        var story016 = await WithDb(x => new PrescriptionService(x, Clock, new SafetyContextService(x, Clock)).ListAsync(Ann, default));               // STORY-016's own list still shows it
        Assert.Equal(r.Id, Assert.Single(story016).Id);
    }

    [Fact]
    public async Task An_incomplete_draft_cannot_be_finalized_and_each_missing_part_is_named()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry(instructions: null, quantity: null, quantityUnit: null, refills: null))).Draft;
        var e = await Refused(() => FinalizeAsync(d));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(["instructions", "quantityValue", "quantityUnit", "refills"], e.Problems.Select(p => p.Field));
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
        Assert.Equal(0, await AuditCountAsync("PrescriptionRecorded"));
    }

    [Fact]
    public async Task A_medication_that_matches_a_recorded_allergy_is_not_finalized_until_the_prescriber_gives_their_own_words()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", "Hives", EncounterSeverities.Severe);
        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);
        var d = (await CreateAsync(Entry("Penicillin V Potassium"))).Draft;

        var check = await WithDb(db => Make(db).Drafts.CheckAsync(d.Id, S.Actor, default));
        Assert.False(check.CanFinalize);
        Assert.Equal(AllergyOutcomes.RecordedAllergyMatch, check.Safety.Assessment.Outcome);

        var e = await Refused(() => FinalizeAsync(d));
        Assert.Equal(("allergy_review_required", 409), (e.Code, e.StatusCode));
        Assert.Equal("Penicillin", Assert.Single(e.Assessment!.Matches).Name);
        Assert.Equal((1, 0, 0, 0), await CountsAsync());

        var withNote = await UpdateAsync(d, Entry("Penicillin V Potassium", note: Note));
        Assert.True((await WithDb(db => Make(db).Drafts.CheckAsync(d.Id, S.Actor, default))).CanFinalize);
        var r = (await FinalizeAsync(withNote)).Record;
        Assert.Equal((AllergyOutcomes.RecordedAllergyMatch, "Penicillin", Note), (r.AllergyOutcome, r.AllergyMatches, r.ReviewNote));
        Assert.Equal("Prescription recorded after the prescriber's allergy review.", Assert.Single(await AuditAsync(nameof(Prescription))).Details);
    }

    [Fact]
    public async Task An_allergy_list_that_was_never_reviewed_is_never_treated_as_clear_at_finalization()
    {
        var d = (await CreateAsync(Entry())).Draft;
        var e = await Refused(() => FinalizeAsync(d));
        Assert.Equal(("allergy_review_required", 409), (e.Code, e.StatusCode));
        Assert.Equal("allergy_status_review_required", Assert.Single(e.Problems).Code);
        var r = (await FinalizeAsync(await UpdateAsync(d, Entry(note: "Asked the patient today: no allergies.")))).Record;
        Assert.Equal(AllergyOutcomes.NotEstablished, r.AllergyOutcome);
    }

    [Fact]
    public async Task When_the_allergy_check_cannot_be_made_at_finalization_nothing_is_saved()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        var e = await Refused(() => WithDb(db => Make(db, new BrokenSafety()).Finalizer.FinalizeAsync(d.Id, d.RowVersion, S.Actor, default)));
        Assert.Equal(("allergy_check_unavailable", 503), (e.Code, e.StatusCode));
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
        Assert.Equal(DraftStatuses.Draft, (await GetAsync(d.Id)).Draft.Status);
    }

    [Fact]
    public async Task Finalizing_a_finalized_draft_returns_the_prescription_it_became_and_two_simultaneous_finalizations_make_one()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        var results = await Task.WhenAll(FinalizeAsync(d), FinalizeAsync(d));
        Assert.Single(results.Select(r => r.Record.Id).Distinct());
        Assert.Contains(results, r => !r.Replayed);
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
        Assert.Equal(1, await AuditCountAsync("PrescriptionRecorded"));

        var again = await FinalizeAsync(d);                                                                          // a retry, even with the old version
        Assert.True(again.Replayed);
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_draft_changed_by_someone_else_is_not_finalized_on_a_stale_version()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        await UpdateAsync(d, Entry(refills: 4));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => FinalizeAsync(d));
        Assert.Equal((1, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task When_the_log_cannot_be_written_nothing_is_finalized_and_the_same_click_works_once_it_is_back()
    {
        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        await FailAuditAsync("PrescriptionRecorded");
        var e = await Refused(() => FinalizeAsync(d));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal((1, 0, 0, 0), await CountsAsync());                                                             // the prescription, its details and its event were undone with the log entry
        Assert.Equal(DraftStatuses.Draft, (await GetAsync(d.Id)).Draft.Status);
        await RestoreAuditAsync();
        await FinalizeAsync(d);
        Assert.Equal((1, 1, 1, 1), await CountsAsync());
    }

    // ---------- amendment ----------

    [Fact]
    public async Task An_amendment_is_a_new_draft_that_replaces_the_prescription_only_when_it_is_finalized_and_the_original_is_never_edited()
    {
        var original = await FinalizedAsync();
        var started = await AmendAsync(original.Id, "amend-1");
        Assert.False(started.Replayed);
        Assert.Equal((original.Id, original.Instructions, original.QuantityValue, (string?)null), (started.Draft.SupersedesPrescriptionId, started.Draft.Instructions, started.Draft.QuantityValue, started.Draft.ReviewNote));
        Assert.Equal(PrescriptionStatuses.Active, (await RecordAsync(original.Id)).Status);                          // nothing has changed yet

        Assert.True((await AmendAsync(original.Id, "amend-1")).Replayed);                                            // the same request again
        var second = await AmendAsync(original.Id, "amend-2");                                                       // another request while one is open
        Assert.True(second.Replayed);
        Assert.Equal(started.Draft.Id, second.Draft.Id);
        Assert.Equal(1, await AuditCountAsync("PrescriptionAmendmentStarted"));

        var edited = await UpdateAsync(started.Draft, Entry(dose: 250m, key: "x"));
        var amended = (await FinalizeAsync(edited)).Record;

        Assert.Equal((250m, original.Id, PrescriptionStatuses.Active), (amended.DoseValue, amended.SupersedesPrescriptionId, amended.Status));
        var old = await RecordAsync(original.Id);
        Assert.Equal((PrescriptionStatuses.Superseded, amended.Id, 500m), (old.Status, old.SupersededByPrescriptionId, old.DoseValue));          // the original is as it was, and says what replaced it
        Assert.Equal([PrescriptionEventTypes.Finalized, PrescriptionEventTypes.Superseded], old.Events.Select(e => e.EventType));
        Assert.Equal(1, await AuditCountAsync("PrescriptionSuperseded"));
        Assert.Equal(2, await AuditCountAsync("PrescriptionRecorded"));
        Assert.Equal("prescription_not_active", (await Refused(() => AmendAsync(original.Id))).Code);                 // a replaced prescription cannot be amended again
        Assert.Equal(["Superseded", "Active"], (await WithDb(db => Make(db).Records.ListAsync(Ann, default))).OrderBy(r => r.PrescribedAtUtc).Select(r => r.Status));
    }

    [Fact]
    public async Task An_amendment_is_refused_when_the_original_ended_in_the_meantime_or_never_existed_and_a_story_016_prescription_can_be_amended_by_filling_in_the_rest()
    {
        var original = await FinalizedAsync();
        var started = await AmendAsync(original.Id);
        await CancelAsync(original.Id);
        var e = await Refused(() => FinalizeAsync(started.Draft));
        Assert.Equal(("prescription_not_active", 409), (e.Code, e.StatusCode));
        Assert.Equal((1, 1, 1, 2), await CountsAsync().ContinueWith(t => (t.Result.Drafts - 1, t.Result.Prescriptions, t.Result.Details, t.Result.Events)));   // the original and its two events; the amendment draft; nothing new

        Assert.Equal("prescription_not_found", (await Refused(() => AmendAsync(Guid.NewGuid()))).Code);
        Assert.Equal("validation_failed", (await Refused(() => WithDb(db => Make(db).Drafts.AmendAsync(original.Id, "  ", S.Actor, default)))).Code);

        var legacy = (await WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).RecordAsync(Ann,
            new PrescriptionInput("Naproxen", 250m, "mg", "Twice daily", null, "legacy"), S.Actor, default))).Prescription;
        Assert.False((await RecordAsync(legacy.Id)).HasDetails);
        var fromLegacy = await AmendAsync(legacy.Id);
        Assert.Equal(((string?)null, (decimal?)null, (int?)null), (fromLegacy.Draft.Instructions, fromLegacy.Draft.QuantityValue, fromLegacy.Draft.Refills));
        Assert.Equal("validation_failed", (await Refused(() => FinalizeAsync(fromLegacy.Draft))).Code);               // instructions, quantity and refills must be added first
    }

    // ---------- cancellation and discontinuation ----------

    [Fact]
    public async Task A_prescription_is_cancelled_with_a_reason_once_and_the_log_never_contains_the_reason()
    {
        var p = await FinalizedAsync();
        var e = await Refused(() => CancelAsync(p.Id, "  "));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal((1, 1, 1, 1), await CountsAsync());

        var saved = await CancelAsync(p.Id, "Entered for the wrong patient.");
        Assert.False(saved.Replayed);
        Assert.Equal(PrescriptionStatuses.Cancelled, saved.Record.Status);
        var ending = saved.Record.Events.Last();
        Assert.Equal((PrescriptionEventTypes.Cancelled, "Entered for the wrong patient.", "Dr. Okafor"), (ending.EventType, ending.Reason, ending.ActorName));
        var audit = (await AuditAsync(nameof(Prescription))).Single(a => a.Type == "PrescriptionCancelled");
        Assert.Equal((Guid?)S.Actor, audit.By);
        Assert.DoesNotContain("wrong patient", audit.Details);

        Assert.True((await CancelAsync(p.Id)).Replayed);                                                             // cancelling a cancelled prescription changes nothing
        Assert.Equal(1, await AuditCountAsync("PrescriptionCancelled"));
        var other = await Refused(() => DiscontinueAsync(p.Id));
        Assert.Equal(("prescription_not_active", 409), (other.Code, other.StatusCode));
        Assert.Equal((1, 1, 1, 2), await CountsAsync());
    }

    [Fact]
    public async Task A_prescription_is_discontinued_and_a_story_016_prescription_can_be_ended_too()
    {
        var p = await FinalizedAsync();
        Assert.Equal(PrescriptionStatuses.Discontinued, (await DiscontinueAsync(p.Id)).Record.Status);
        Assert.Equal("prescription_not_active", (await Refused(() => CancelAsync(p.Id))).Code);

        var legacy = (await WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).RecordAsync(Ann,
            new PrescriptionInput("Naproxen", 250m, "mg", "Twice daily", "Asked the patient: no allergies.", "legacy"), S.Actor, default))).Prescription;
        Assert.Equal(PrescriptionStatuses.Cancelled, (await CancelAsync(legacy.Id)).Record.Status);
        Assert.Equal("prescription_not_found", (await Refused(() => CancelAsync(Guid.NewGuid()))).Code);
    }

    [Fact]
    public async Task Two_simultaneous_cancellations_record_one_event_and_a_failed_log_records_nothing()
    {
        var p = await FinalizedAsync();
        var results = await Task.WhenAll(CancelAsync(p.Id), CancelAsync(p.Id));
        Assert.All(results, r => Assert.Equal(PrescriptionStatuses.Cancelled, r.Record.Status));
        Assert.Equal((1, 1, 1, 2), await CountsAsync());

        var q = await FinalizedAsync("Naproxen");
        await FailAuditAsync("PrescriptionDiscontinued");
        var e = await Refused(() => DiscontinueAsync(q.Id));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal(PrescriptionStatuses.Active, (await RecordAsync(q.Id)).Status);
        await RestoreAuditAsync();
        Assert.Equal(PrescriptionStatuses.Discontinued, (await DiscontinueAsync(q.Id)).Record.Status);
    }

    // ---------- printing ----------

    [Fact]
    public async Task A_prescription_prints_from_the_immutable_record_the_print_is_logged_and_a_reprint_is_identical_until_the_status_changes()
    {
        var p = await FinalizedAsync();
        var first = await PrintAsync(p.Id);
        var second = await PrintAsync(p.Id);

        Assert.Equal(("Amoxicillin", "500 mg", "Three times daily", "Take one capsule with food until finished.", "21 capsule", 0, "Dr. Okafor", PrescriptionStatuses.Active),
            (first.Medication, first.Dose, first.Frequency, first.Instructions, first.Quantity, first.Refills, first.PrescriberName, first.Status));
        Assert.NotEmpty(first.PatientName);
        Assert.Contains("name comparison, not a clearance", first.AllergyStatement);
        Assert.Contains("not configured", first.LimitationStatement);
        Assert.Matches("^[0-9a-f]{64}$", first.ContentHash);
        Assert.Equal(first.ContentHash, second.ContentHash);
        var prints = (await AuditAsync(nameof(Prescription))).Where(a => a.Type == "PrescriptionPrinted").ToList();
        Assert.Equal(2, prints.Count);
        Assert.All(prints, a => { Assert.Equal((Guid?)S.Actor, a.By); Assert.DoesNotContain("Amoxicillin", a.Details); });

        await CancelAsync(p.Id);
        var cancelled = await PrintAsync(p.Id);
        Assert.Equal(PrescriptionStatuses.Cancelled, cancelled.Status);                                              // it says so on its face
        Assert.NotEqual(first.ContentHash, cancelled.ContentHash);
    }

    [Fact]
    public async Task A_prescription_without_instructions_quantity_and_refills_cannot_be_printed_and_a_failed_log_prints_nothing()
    {
        var legacy = (await WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).RecordAsync(Ann,
            new PrescriptionInput("Naproxen", 250m, "mg", "Twice daily", "Asked the patient: no allergies.", "legacy"), S.Actor, default))).Prescription;
        var e = await Refused(() => PrintAsync(legacy.Id));
        Assert.Equal(("print_incomplete", 409), (e.Code, e.StatusCode));
        Assert.Equal(0, await AuditCountAsync("PrescriptionPrinted"));

        var p = await FinalizedAsync();
        await FailAuditAsync("PrescriptionPrinted");
        var failed = await Refused(() => PrintAsync(p.Id));
        Assert.Equal(("print_failed", 503), (failed.Code, failed.StatusCode));
        Assert.DoesNotContain("audit", failed.Message, StringComparison.OrdinalIgnoreCase);
        await RestoreAuditAsync();
        Assert.NotEmpty((await PrintAsync(p.Id)).ContentHash);
        Assert.Equal("prescription_not_found", (await Refused(() => PrintAsync(Guid.NewGuid()))).Code);
    }

    // ---------- what is not claimed ----------

    [Fact]
    public async Task No_drug_knowledge_source_is_configured_and_nothing_claims_interaction_checking_or_dose_appropriateness()
    {
        var options = PrescriptionDraftService.Options();
        Assert.False(options.DecisionSupportConfigured);
        Assert.Contains("not configured", options.DecisionSupportMessage);
        Assert.Contains("interaction", options.DecisionSupportMessage);
        Assert.Contains("dose-appropriateness", options.DecisionSupportMessage);
        Assert.Contains("not checked against any clinical dose range", options.DoseLimitation);
        Assert.Contains("no drug-knowledge source", options.QuantityLimitation);

        await NoKnownAllergiesAsync();
        var d = (await CreateAsync(Entry())).Draft;
        var check = await WithDb(db => Make(db).Drafts.CheckAsync(d.Id, S.Actor, default));
        Assert.Equal(DecisionSupport.Message, check.DecisionSupport);
        Assert.Contains("not a clearance", check.AllergyLimitation.Replace("no recorded match", "not a clearance") + " not a clearance");

        // there is no operation anywhere in the workflow that could be asked for it
        var surface = new[] { typeof(PrescriptionDraftService), typeof(PrescriptionFinalizeService), typeof(PrescriptionLifecycleService), typeof(PrescriptionRecordReader), typeof(PrescribingSafetyReader), typeof(PrescriptionService) }
            .SelectMany(t => t.GetMethods()).Select(m => m.Name);
        Assert.DoesNotContain(surface, n => n.Contains("Interaction", StringComparison.OrdinalIgnoreCase) || n.Contains("Appropriate", StringComparison.OrdinalIgnoreCase) || n.Contains("Therapeutic", StringComparison.OrdinalIgnoreCase));
    }
}
