using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Prescribing;
using Alveara.Api.Architecture.Safety;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-016 against real SQL Server: prescribing with an allergy check and a structural dose check. A correct entry is saved with its allergy result and logged with the user and the time; an entry that matches a
/// recorded allergy, or that is made on an allergy list nobody has reviewed, is refused until the prescriber gives their own review, and then saved with it; an incorrect entry is refused with every problem named
/// and nothing saved; a retry replays; a changed request under the same key conflicts; when the log cannot be written, or the allergy check cannot be made, nothing is saved and the caller is told.
/// </summary>
public class PrescriptionServiceTests : SafetyTestBase
{
    private const string Note = "Childhood rash, tolerated since; discussed with the patient.";

    private static PrescriptionInput Entry(string medication = "Ibuprofen", decimal dose = 400m, string unit = "mg", string frequency = "Three times daily", string? note = null, string? key = null) =>
        new(medication, dose, unit, frequency, note, key ?? $"rx-{Guid.NewGuid():N}");

    private Task<SavedPrescription> RecordAsync(PrescriptionInput input, Guid? patient = null) =>
        WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).RecordAsync(patient ?? Ann, input, S.Actor, default));

    private Task<PrescriptionPreview> PreviewAsync(PrescriptionInput input) =>
        WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).PreviewAsync(Ann, input, S.Actor, default));

    private static async Task<PrescriptionException> RefusedAsync(Func<Task<SavedPrescription>> action) => await Assert.ThrowsAsync<PrescriptionException>(action);

    private async Task<(int Rows, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Prescriptions.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType == "PrescriptionRecorded"));
    }

    /// <summary>An established, empty allergy list: a clinician recorded that none is known.</summary>
    private Task NoKnownAllergiesAsync() => RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown);

    // ---------- the correct entry, and what is logged ----------

    [Fact]
    public async Task A_correct_entry_on_an_established_allergy_list_is_saved_with_its_allergy_result_and_logged_with_the_user_and_the_time()
    {
        await NoKnownAllergiesAsync();
        var before = DateTimeOffset.UtcNow;

        var saved = await RecordAsync(Entry("  Ibuprofen   400 ", 400m, "MG", "three times daily"));

        Assert.False(saved.Replayed);
        var p = saved.Prescription;
        Assert.Equal(("Ibuprofen 400", 400m, "mg", "Three times daily"), (p.Medication, p.DoseValue, p.DoseUnit, p.Frequency));
        Assert.Equal((AllergyOutcomes.NoRecordedMatch, AllergyListStates.Established, (string?)null, (string?)null), (p.AllergyOutcome, p.AllergyListState, p.AllergyMatches, p.ReviewNote));
        Assert.Equal("Dr. Okafor", p.PrescribedByName);

        var audit = Assert.Single(await AuditAsync(nameof(Prescription)));                                       // "all prescription entries are logged with user ID and timestamp"
        Assert.Equal(("PrescriptionRecorded", "Prescription recorded.", (Guid?)S.Actor), (audit.Type, audit.Details, audit.By));
        await using var db = Fixture.CreateContext();
        var entry = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == nameof(Prescription));
        Assert.InRange(entry.TimestampUtc, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(p.Id, entry.TargetUserAccountId);
        Assert.NotNull(entry.CorrelationId);
        var row = await db.Prescriptions.AsNoTracking().SingleAsync();
        Assert.Equal((S.Actor, p.Id, entry.CorrelationId), (row.PrescriberUserId, row.Id, row.CorrelationId));
    }

    [Fact]
    public async Task The_audit_entry_never_contains_the_medication_the_dose_or_an_allergy()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", "Hives", EncounterSeverities.Severe);
        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);
        await RecordAsync(Entry("Penicillin V Potassium", 500m, note: Note));
        var details = (await AuditAsync(nameof(Prescription))).Single().Details;
        Assert.Equal("Prescription recorded after the prescriber's allergy review.", details);
        Assert.DoesNotContain("Penicillin", details);
        Assert.DoesNotContain("500", details);
        Assert.DoesNotContain("Hives", details);
    }

    [Fact]
    public async Task An_unrelated_recorded_allergy_does_not_stop_a_correct_entry_and_the_result_says_no_recorded_match()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Latex", "Rash", EncounterSeverities.Mild);
        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);
        var saved = await RecordAsync(Entry());
        Assert.Equal((AllergyOutcomes.NoRecordedMatch, AllergyListStates.Established), (saved.Prescription.AllergyOutcome, saved.Prescription.AllergyListState));
    }

    // ---------- the allergy check ----------

    [Fact]
    public async Task A_medication_that_matches_a_recorded_allergy_is_refused_until_the_prescriber_reviews_it_and_nothing_is_saved_meanwhile()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", "Hives", EncounterSeverities.Severe);
        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);

        var e = await RefusedAsync(() => RecordAsync(Entry("Penicillin V Potassium", 500m)));

        Assert.Equal(("allergy_review_required", 409), (e.Code, e.StatusCode));
        Assert.Equal(AllergyOutcomes.RecordedAllergyMatch, e.Assessment!.Outcome);
        Assert.Equal("Penicillin", Assert.Single(e.Assessment.Matches).Name);
        Assert.Equal(("reviewNote", "allergy_review_required"), (Assert.Single(e.Problems).Field, e.Problems[0].Code));
        Assert.Equal((0, 0), await CountsAsync());
    }

    [Fact]
    public async Task With_the_prescribers_own_review_the_matching_entry_is_saved_and_keeps_the_match_and_the_note()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", "Hives", EncounterSeverities.Severe);
        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);

        var saved = (await RecordAsync(Entry("Penicillin V Potassium", 500m, note: Note))).Prescription;

        Assert.Equal((AllergyOutcomes.RecordedAllergyMatch, "Penicillin", Note), (saved.AllergyOutcome, saved.AllergyMatches, saved.ReviewNote));
        Assert.Equal((1, 1), await CountsAsync());
    }

    [Theory]
    [InlineData("none")]                                   // nobody has said anything about allergies
    [InlineData("unknown")]                                // a clinician recorded that allergies are unknown
    [InlineData("changed")]                                // an allergy was added after the list was confirmed
    public async Task An_allergy_list_that_is_not_established_is_never_treated_as_clear_the_save_waits_for_the_prescribers_own_words(string state)
    {
        switch (state)
        {
            case "unknown": await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Unknown); break;
            case "changed":
                await RecordItemAsync(EncounterEntryKinds.Allergy, "Latex", null, EncounterSeverities.Mild);
                await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);
                await RecordItemAsync(EncounterEntryKinds.Allergy, "Codeine", null, null);
                break;
        }

        var e = await RefusedAsync(() => RecordAsync(Entry()));

        Assert.Equal(("allergy_review_required", 409), (e.Code, e.StatusCode));
        Assert.Equal(AllergyOutcomes.NotEstablished, e.Assessment!.Outcome);
        Assert.Equal("allergy_status_review_required", Assert.Single(e.Problems).Code);
        Assert.Equal((0, 0), await CountsAsync());

        var saved = (await RecordAsync(Entry(note: "Asked the patient today: no allergies."))).Prescription;
        Assert.Equal((AllergyOutcomes.NotEstablished, "Asked the patient today: no allergies."), (saved.AllergyOutcome, saved.ReviewNote));
        Assert.Equal((1, 1), await CountsAsync());
    }

    [Fact]
    public async Task When_the_safety_context_cannot_be_read_the_save_is_refused_and_nothing_is_saved()
    {
        var e = await RefusedAsync(() => WithDb(db => new PrescriptionService(db, Clock, new BrokenSafety()).RecordAsync(Ann, Entry(), S.Actor, default)));
        Assert.Equal(("allergy_check_unavailable", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("boom", e.Message);
        Assert.Equal((0, 0), await CountsAsync());
    }

    private sealed class BrokenSafety : ISafetyContextProvider
    {
        public Task<SafetyContext> GetAsync(Guid patientId, Guid forUserId, CancellationToken ct) => throw new InvalidOperationException("boom");
        public Task<SafetySummary> SummaryAsync(Guid patientId, Guid forUserId, CancellationToken ct) => throw new InvalidOperationException("boom");
    }

    // ---------- the incorrect entry ----------

    [Fact]
    public async Task An_incorrect_entry_is_refused_with_every_problem_named_and_nothing_is_saved()
    {
        await NoKnownAllergiesAsync();
        var e = await RefusedAsync(() => RecordAsync(Entry("A", 50000m, "stone", "whenever")));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(["medication", "doseUnit", "frequency"], e.Problems.Select(p => p.Field));
        Assert.Equal((0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_dose_that_is_only_a_typing_slip_is_refused_with_a_message_that_says_it_is_not_a_clinical_limit()
    {
        await NoKnownAllergiesAsync();
        var e = await RefusedAsync(() => RecordAsync(Entry("Ibuprofen", 4000m, "tablet")));
        var problem = Assert.Single(e.Problems);
        Assert.Equal(("doseValue", "above_typing_ceiling"), (problem.Field, problem.Code));
        Assert.Contains("not a clinical dose limit", problem.Message);
        Assert.Equal((0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_missing_entry_and_a_missing_key_are_refused_before_anything_is_looked_up()
    {
        Assert.Equal("validation_failed", (await RefusedAsync(() => WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).RecordAsync(Ann, null, S.Actor, default)))).Code);
        var e = await RefusedAsync(() => RecordAsync(Entry(key: "  ")));
        Assert.Equal("idempotencyKey", Assert.Single(e.Problems).Field);
        Assert.Equal((0, 0), await CountsAsync());
    }

    [Fact]
    public async Task An_unknown_patient_is_not_found_and_nothing_is_saved()
    {
        var e = await RefusedAsync(() => RecordAsync(Entry(), patient: Guid.NewGuid()));
        Assert.Equal(("patient_not_found", 404), (e.Code, e.StatusCode));
        Assert.Equal((0, 0), await CountsAsync());
    }

    // ---------- repeats ----------

    [Fact]
    public async Task The_same_key_and_entry_replay_the_saved_prescription_with_no_second_row_and_no_second_log_entry()
    {
        await NoKnownAllergiesAsync();
        var entry = Entry(key: "once");
        var first = await RecordAsync(entry);
        var again = await RecordAsync(entry with { Medication = "  ibuprofen  ", Frequency = "THREE TIMES DAILY" });          // the same request, differently spelled
        Assert.False(first.Replayed);
        Assert.True(again.Replayed);
        Assert.Equal(first.Prescription.Id, again.Prescription.Id);
        Assert.Equal((1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_replay_answers_with_the_saved_prescription_without_checking_allergies_again()
    {
        await NoKnownAllergiesAsync();
        var entry = Entry(key: "once");
        var first = await RecordAsync(entry);
        var again = await WithDb(db => new PrescriptionService(db, Clock, new BrokenSafety()).RecordAsync(Ann, entry, S.Actor, default));          // the safety context is unreadable now; a replay must not need it
        Assert.True(again.Replayed);
        Assert.Equal(first.Prescription.Id, again.Prescription.Id);
        Assert.Equal((1, 1), await CountsAsync());
    }

    [Theory]
    [InlineData("medication")] [InlineData("dose")] [InlineData("unit")] [InlineData("frequency")] [InlineData("note")]
    public async Task The_same_key_with_a_different_entry_is_a_conflict_and_changes_nothing(string changed)
    {
        await NoKnownAllergiesAsync();
        var entry = Entry(key: "once");
        await RecordAsync(entry);
        var other = changed switch
        {
            "medication" => entry with { Medication = "Naproxen" },
            "dose" => entry with { DoseValue = 200m },
            "unit" => entry with { DoseUnit = "mcg" },
            "frequency" => entry with { Frequency = "Twice daily" },
            _ => entry with { ReviewNote = "A different note" },
        };
        var e = await RefusedAsync(() => RecordAsync(other));
        Assert.Equal(("idempotency_key_reused", 409), (e.Code, e.StatusCode));
        Assert.Equal((1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Two_simultaneous_saves_under_one_key_end_with_one_prescription_and_one_log_entry()
    {
        await NoKnownAllergiesAsync();
        var entry = Entry(key: "race");
        var results = await Task.WhenAll(RecordAsync(entry), RecordAsync(entry));
        Assert.Single(results.Select(r => r.Prescription.Id).Distinct());
        Assert.Contains(results, r => !r.Replayed);                                                               // one of them saved it; the other either replayed it or lost the race and answered with the winner
        Assert.Equal((1, 1), await CountsAsync());
    }

    // ---------- the log cannot be written ----------

    [Fact]
    public async Task When_the_log_cannot_be_written_nothing_is_saved_and_the_caller_is_told_and_the_same_request_works_once_it_is_back()
    {
        await NoKnownAllergiesAsync();
        await WithDb(async db => { await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefusePrescriptionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'PrescriptionRecorded') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END"); return 0; });
        var entry = Entry(key: "retry");

        var e = await RefusedAsync(() => RecordAsync(entry));

        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, 0), await CountsAsync());                                                                // the prescription was undone with its log entry

        await WithDb(async db => { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefusePrescriptionAudit]"); return 0; });
        await RecordAsync(entry);                                                                                 // the same click, now
        Assert.Equal((1, 1), await CountsAsync());
    }

    // ---------- reads and the preview ----------

    [Fact]
    public async Task A_patients_prescriptions_are_listed_newest_first_with_who_prescribed_and_never_another_patients()
    {
        await NoKnownAllergiesAsync();
        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.NoneKnown, patient: Bo);
        await RecordAsync(Entry("Ibuprofen"));
        await Task.Delay(20);
        await RecordAsync(Entry("Naproxen"));
        await RecordAsync(Entry("Acetaminophen"), patient: Bo);

        var list = await WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).ListAsync(Ann, default));

        Assert.Equal(["Naproxen", "Ibuprofen"], list.Select(p => p.Medication));
        Assert.All(list, p => Assert.Equal("Dr. Okafor", p.PrescribedByName));
        await Assert.ThrowsAsync<PrescriptionException>(() => WithDb(db => new PrescriptionService(db, Clock, new SafetyContextService(db, Clock)).ListAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task The_preview_names_the_problems_the_allergy_result_and_the_review_it_requires_and_saves_and_logs_nothing()
    {
        await RecordItemAsync(EncounterEntryKinds.Allergy, "Penicillin", "Hives", EncounterSeverities.Severe);
        await RecordReviewAsync(EncounterEntryKinds.Allergy, ClinicalReviewStates.Reviewed);

        var wrong = await PreviewAsync(Entry("Penicillin V", 99999m, "mg"));
        Assert.Equal(["doseValue"], wrong.Problems.Select(p => p.Field));                                       // the dose is wrong, and the allergy is still reported
        Assert.Equal(AllergyOutcomes.RecordedAllergyMatch, wrong.Assessment!.Outcome);
        Assert.True(wrong.ReviewRequired);
        Assert.Equal("allergy_review_required", wrong.ReviewProblem!.Code);

        var reviewed = await PreviewAsync(Entry("Penicillin V", 500m, note: Note));
        Assert.Empty(reviewed.Problems);
        Assert.Null(reviewed.ReviewProblem);
        Assert.True(reviewed.ReviewRequired);

        var invalidName = await PreviewAsync(Entry("A"));
        Assert.Null(invalidName.Assessment);                                                                     // no valid medication, so no allergy question to ask yet
        Assert.False(invalidName.ReviewRequired);
        Assert.Contains("not checked against any clinical dose range", invalidName.DoseLimitation);
        Assert.Contains("does not know drug families", invalidName.AllergyLimitation);
        Assert.Equal((0, 0), await CountsAsync());
        Assert.Empty(await AuditAsync(nameof(Prescription)));
    }

    [Fact]
    public void The_options_give_the_closed_lists_the_ceilings_and_both_limitations()
    {
        var o = PrescriptionService.Options();
        Assert.Equal(PrescriptionRules.DoseUnits, o.DoseUnits);
        Assert.Equal(PrescriptionRules.Frequencies, o.Frequencies);
        Assert.Equal(5000m, o.TypingCeilings["mg"]);
        Assert.Equal(3, o.DoseDecimals);
        Assert.Contains("no drug-knowledge source", o.DoseLimitation);
        Assert.Contains("not a clearance", o.AllergyLimitation);
    }
}
