using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-013 behaviour, against real SQL Server: a diagnosis is saved linked to its patient and encounter, with an optional treatment-plan forward reference that is opaque, shown as unresolved and kept
/// through every correction and withdrawal; incorrect data is refused with every problem named and nothing saved; every entry is logged with user and time in the same save (and a failed log or a
/// failed insert stores nothing); repeats are quiet; and every failure path - a wrong encounter, a stale edit, a withdrawn diagnosis, simultaneous saves.
/// </summary>
public class DiagnosisServiceTests : SafetyTestBase
{
    private Guid _annEncounter, _boEncounter;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _annEncounter = await EncounterAsync(Ann);
        _boEncounter = await EncounterAsync(Bo);
    }

    private async Task<Guid> EncounterAsync(Guid patient, string status = EncounterStatuses.Draft)
    {
        await using var db = Fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = new DateTimeOffset(2030, 1, 14, 15, 0, 0, TimeSpan.Zero), Status = status, CreatedAtUtc = DateTimeOffset.UtcNow };
        if (status == EncounterStatuses.Finalized) (e.FinalizedAtUtc, e.FinalizedByUserId) = (DateTimeOffset.UtcNow, S.Actor);
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private static DiagnosisInput In(Guid encounter, string label = "Chronic periodontitis", string? tooth = null, string? notes = null, string? plan = null) => new(encounter, label, tooth, notes, plan);
    private Task<T> Svc<T>(Func<DiagnosisService, Task<T>> action) => WithDb(db => action(new DiagnosisService(db, Clock)));
    private Task<DiagnosisView> RecordAsync(DiagnosisInput? input = null, string? key = null, Guid? patient = null, Guid? actor = null) =>
        Svc(s => s.RecordAsync(patient ?? Ann, key ?? Guid.NewGuid().ToString("N"), input ?? In(_annEncounter), actor ?? S.Actor, default));
    private Task<DiagnosisView> CorrectAsync(DiagnosisView d, DiagnosisCorrection c, string? reason = "Clarified at review", string? version = null, Guid? actor = null) =>
        Svc(s => s.CorrectAsync(d.Id, version ?? d.RowVersion, c, reason, actor ?? S.Actor, default));
    private Task<DiagnosisView> WithdrawAsync(DiagnosisView d, string? reason = "Entered on the wrong patient", string? version = null) => Svc(s => s.WithdrawAsync(d.Id, version ?? d.RowVersion, reason, S.Actor, default));
    private Task<DiagnosisHistoryView> HistoryAsync(Guid id) => Svc(s => s.HistoryAsync(id, default));
    private static async Task<DiagnosisException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<DiagnosisException>(action);
    private static DiagnosisCorrection Same(DiagnosisView d) => new(d.Label, d.ToothKey, d.Notes, null, false);

    private async Task<(int Diagnoses, int Versions, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Diagnoses.CountAsync(), await db.DiagnosisVersions.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(Diagnosis)));
    }

    // ---------- acceptance 1: recorded, and linked to the patient, the encounter and the treatment plan reference ----------

    [Fact]
    public async Task A_diagnosis_is_saved_linked_to_the_patient_and_the_encounter_with_a_treatment_plan_reference_shown_as_unresolved()
    {
        var d = await RecordAsync(In(_annEncounter, "  Caries   on the occlusal surface ", "16", "Sensitive to cold.", "  plan-2026-07 "));
        Assert.Equal((Ann, _annEncounter, "Caries on the occlusal surface", "16", "Sensitive to cold."), (d.PatientId, d.EncounterId, d.Label, d.ToothKey, d.Notes));
        Assert.Equal(("plan-2026-07", "Unresolved"), (d.TreatmentPlanReference, d.TreatmentPlanReferenceState));        // normalized, and never presented as resolved
        Assert.Equal(("Dr. Okafor", DiagnosisStatuses.Active), (d.RecordedByName, d.Status));
        Assert.Equal(new DateTimeOffset(2030, 1, 14, 15, 0, 0, TimeSpan.Zero), d.EncounterAtUtc);
        var stored = await WithDb(db => db.Diagnoses.AsNoTracking().SingleAsync());
        Assert.Equal((Ann, _annEncounter, "plan-2026-07"), (stored.PatientId, stored.EncounterId, stored.TreatmentPlanReference));
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Without_a_treatment_plan_reference_the_diagnosis_says_nothing_about_one()
    {
        var d = await RecordAsync();
        Assert.Equal((null, null), (d.TreatmentPlanReference, d.TreatmentPlanReferenceState));
    }

    [Fact]
    public async Task A_reference_that_names_nothing_that_exists_is_kept_as_given_and_never_looked_up_or_rejected_for_not_resolving()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
        Assert.Equal(("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "Unresolved"), (d.TreatmentPlanReference, d.TreatmentPlanReferenceState));
    }

    [Fact]
    public async Task A_diagnosis_can_be_recorded_against_a_finalized_encounter_because_it_changes_nothing_in_its_notes()
    {
        var finalized = await EncounterAsync(Ann, EncounterStatuses.Finalized);
        Assert.Equal(finalized, (await RecordAsync(In(finalized))).EncounterId);
    }

    // ---------- the links are authoritative: failure path "diagnosis fails to link to patient record" ----------

    [Fact]
    public async Task An_encounter_of_another_patient_or_one_that_does_not_exist_is_refused_the_same_way_and_nothing_is_saved()
    {
        foreach (var encounter in new[] { _boEncounter, Guid.NewGuid() })
        {
            var e = await Refused(() => RecordAsync(In(encounter)));
            Assert.Equal(("encounter_not_found", 404), (e.Code, e.StatusCode));
            Assert.Equal("encounterId", Assert.Single(e.Problems).Field);
        }
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task An_unknown_patient_is_a_404_and_saves_nothing()
    {
        var e = await Refused(() => RecordAsync(patient: Guid.NewGuid()));
        Assert.Equal(("patient_not_found", 404), (e.Code, e.StatusCode));
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    // ---------- acceptance 2: incorrect data is refused and the person is told what to correct ----------

    [Fact]
    public async Task Incorrect_data_is_refused_naming_every_problem_and_nothing_at_all_is_saved()
    {
        var e = await Refused(() => RecordAsync(new DiagnosisInput(_annEncounter, "", "19", "bell\a", "   ")));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(["label:required", "toothKey:unknown_tooth", "notes:invalid_characters", "treatmentPlanReference:blank"], e.Problems.Select(p => $"{p.Field}:{p.Code}").ToArray());
        Assert.All(e.Problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Message)));
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task A_missing_save_key_is_refused_and_so_is_a_65_character_one(string? key)
    {
        Assert.Equal("idempotencyKey", Assert.Single((await Refused(() => Svc(s => s.RecordAsync(Ann, key, In(_annEncounter), S.Actor, default)))).Problems).Field);
        Assert.Equal("idempotencyKey", Assert.Single((await Refused(() => RecordAsync(key: new string('k', 65)))).Problems).Field);
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task No_entry_at_all_is_refused()
        => Assert.Equal("diagnosis", Assert.Single((await Refused(() => Svc(s => s.RecordAsync(Ann, "k", null, S.Actor, default)))).Problems).Field);

    // ---------- acceptance 3 (trust): every entry is logged with user and timestamp ----------

    [Fact]
    public async Task Every_entry_is_logged_with_the_user_and_a_timestamp_and_the_log_never_holds_what_was_diagnosed()
    {
        var d = await RecordAsync(In(_annEncounter, "Chronic periodontitis", "36", "private note", "plan-secret-7"), actor: S.Actor);
        d = await CorrectAsync(d, new("Generalized gingivitis", "36", "private note", null, false), actor: Other);
        await WithdrawAsync(d);
        var audit = await AuditAsync(nameof(Diagnosis));
        Assert.Equal(new[] { "DiagnosisRecorded", "DiagnosisCorrected", "DiagnosisWithdrawn" }, audit.Select(a => a.Type));
        Assert.Equal(new Guid?[] { S.Actor, Other, S.Actor }, audit.Select(a => a.By));
        Assert.All(audit, a =>
        {
            foreach (var secret in new[] { "periodontitis", "gingivitis", "36", "private", "plan-secret-7", "wrong patient" }) Assert.DoesNotContain(secret, a.Details);
        });
        await using var db = Fixture.CreateContext();
        Assert.All(await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(Diagnosis)).ToListAsync(), a => Assert.NotEqual(default, a.TimestampUtc));
    }

    // ---------- failure path: "system fails to log diagnosis entries" and a failed save ----------

    [Fact]
    public async Task A_failed_audit_write_saves_nothing_and_says_so_and_the_same_save_works_once_the_log_is_back()
    {
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseDiagnosisAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'Diagnosis') THROW 59000, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var e = await Refused(() => RecordAsync(key: "visit-1"));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message);                                                  // no internals leak to the person
        Assert.Equal((0, 0, 0), await CountsAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefuseDiagnosisAudit]");
        await RecordAsync(key: "visit-1");
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_failed_audit_write_on_a_correction_or_a_withdrawal_changes_nothing()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-7"));
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseChangeAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType IN ('DiagnosisCorrected', 'DiagnosisWithdrawn')) THROW 59001, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        Assert.Equal("save_failed", (await Refused(() => CorrectAsync(d, Same(d) with { Label = "Something else" }))).Code);
        Assert.Equal("save_failed", (await Refused(() => WithdrawAsync(d))).Code);
        var after = await Svc(s => s.GetAsync(d.Id, default));
        Assert.Equal((d.Label, DiagnosisStatuses.Active, d.RowVersion, "plan-7"), (after.Label, after.Status, after.RowVersion, after.TreatmentPlanReference));
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_failed_insert_halfway_through_leaves_no_half_saved_diagnosis()
    {
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseVersion] ON [DiagnosisVersions] AFTER INSERT AS
BEGIN
    THROW 59002, 'storage unavailable', 1;
END");
        var e = await Refused(() => RecordAsync());
        Assert.Equal("save_failed", e.Code);
        Assert.DoesNotContain("storage", e.Message);
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    // ---------- idempotency ----------

    [Fact]
    public async Task The_same_key_and_entry_returns_the_diagnosis_already_saved_with_no_second_diagnosis_or_log_entry_even_after_a_correction()
    {
        var first = await RecordAsync(In(_annEncounter, "Gingivitis", plan: "plan-7"), key: "visit-9");
        var again = await RecordAsync(In(_annEncounter, "  Gingivitis ", plan: " plan-7 "), key: "visit-9");          // the same entry once normalized
        Assert.Equal(first.Id, again.Id);
        Assert.Equal((1, 1, 1), await CountsAsync());
        await CorrectAsync(again, Same(again) with { Label = "Generalized gingivitis" });
        Assert.Equal(first.Id, (await RecordAsync(In(_annEncounter, "Gingivitis", plan: "plan-7"), key: "visit-9")).Id);      // a retry of the original save still finds it
        Assert.Equal(1, (await CountsAsync()).Diagnoses);
    }

    [Fact]
    public async Task The_same_key_with_a_different_entry_is_refused_rather_than_ignored()
    {
        await RecordAsync(In(_annEncounter, "Gingivitis"), key: "visit-9");
        foreach (var other in new[] { In(_annEncounter, "Periodontitis"), In(_annEncounter, "Gingivitis", plan: "plan-1"), In(_annEncounter, "Gingivitis", "16") })
            Assert.Equal(("idempotency_key_reused", 409), ((await Refused(() => RecordAsync(other, key: "visit-9"))).Code, (await Refused(() => RecordAsync(other, key: "visit-9"))).StatusCode));
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_key_belongs_to_one_patient_so_another_patient_can_use_the_same_text()
    {
        await RecordAsync(key: "visit-1");
        await RecordAsync(In(_boEncounter), key: "visit-1", patient: Bo);
        Assert.Equal(2, (await CountsAsync()).Diagnoses);
    }

    [Fact]
    public async Task Eight_simultaneous_saves_with_one_key_create_one_diagnosis_and_all_get_it()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => RecordAsync(In(_annEncounter, plan: "plan-7"), key: "double-click"))));
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    // ---------- corrections: the reference is never removed or replaced by accident ----------

    [Fact]
    public async Task A_correction_changes_what_was_corrected_keeps_the_treatment_plan_reference_and_is_logged_with_its_reason()
    {
        var d = await RecordAsync(In(_annEncounter, "Caries", "16", "n", "plan-7"));
        var c = await CorrectAsync(d, new("Deep caries", "17", "revised note", null, false), "Wrong tooth");
        Assert.Equal(("Deep caries", "17", "revised note", "plan-7", "Unresolved"), (c.Label, c.ToothKey, c.Notes, c.TreatmentPlanReference, c.TreatmentPlanReferenceState));
        Assert.NotEqual(d.RowVersion, c.RowVersion);
        Assert.Equal(("Dr. Okafor", true), (c.UpdatedByName, c.UpdatedAtUtc is not null));
        var h = await HistoryAsync(d.Id);
        Assert.Equal(new[] { ("Recorded", "Caries", (string?)null), ("Corrected", "Deep caries", "Wrong tooth") }, h.Versions.Select(v => (v.ChangeType, v.Label, v.Reason)));
        Assert.All(h.Versions, v => Assert.Equal("plan-7", v.TreatmentPlanReference));                     // the reference is in every snapshot
    }

    [Fact]
    public async Task The_reference_is_replaced_only_when_asked_and_the_old_value_stays_in_the_history()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-7"));
        var c = await CorrectAsync(d, Same(d) with { TreatmentPlanReference = "  plan-8 " }, "Wrong plan");
        Assert.Equal("plan-8", c.TreatmentPlanReference);
        Assert.Equal(new[] { "plan-7", "plan-8" }, (await HistoryAsync(d.Id)).Versions.Select(v => v.TreatmentPlanReference));
    }

    [Fact]
    public async Task The_reference_is_removed_only_by_an_explicit_clear_which_is_recorded()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-7"));
        var c = await CorrectAsync(d, Same(d) with { ClearTreatmentPlanReference = true }, "Entered by mistake");
        Assert.Equal((null, null), (c.TreatmentPlanReference, c.TreatmentPlanReferenceState));
        Assert.Equal(new string?[] { "plan-7", null }, (await HistoryAsync(d.Id)).Versions.Select(v => v.TreatmentPlanReference));
    }

    [Fact]
    public async Task Replacing_and_clearing_in_one_correction_is_refused_and_a_blank_replacement_is_refused_not_read_as_clearing()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-7"));
        Assert.Equal("treatmentPlanReference:conflict", Assert.Single((await Refused(() => CorrectAsync(d, Same(d) with { TreatmentPlanReference = "plan-8", ClearTreatmentPlanReference = true }))).Problems.Select(p => $"{p.Field}:{p.Code}")));
        Assert.Equal("treatmentPlanReference:blank", Assert.Single((await Refused(() => CorrectAsync(d, Same(d) with { TreatmentPlanReference = "  " }))).Problems.Select(p => $"{p.Field}:{p.Code}")));
        Assert.Equal("plan-7", (await Svc(s => s.GetAsync(d.Id, default))).TreatmentPlanReference);
    }

    [Fact]
    public async Task A_correction_with_bad_data_or_no_reason_is_refused_naming_every_problem_and_changes_nothing()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-7"));
        var e = await Refused(() => CorrectAsync(d, new("", "19", null, null, false), reason: "  "));
        Assert.Equal(["reason:required", "label:required", "toothKey:unknown_tooth"], e.Problems.Select(p => $"{p.Field}:{p.Code}").ToArray());
        Assert.Equal("reason:too_long", Assert.Single((await Refused(() => CorrectAsync(d, Same(d) with { Label = "x" }, reason: new string('r', 501)))).Problems.Select(p => $"{p.Field}:{p.Code}")));
        var after = await Svc(s => s.GetAsync(d.Id, default));
        Assert.Equal((d.Label, d.RowVersion), (after.Label, after.RowVersion));
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_correction_that_changes_nothing_is_quiet_no_version_no_log_entry_and_not_even_the_row_version_moves()
    {
        var d = await RecordAsync(In(_annEncounter, "Caries", "16", "n", "plan-7"));
        var same = await CorrectAsync(d, new("  Caries ", "16", "n", null, false));
        Assert.Equal(d.RowVersion, same.RowVersion);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_stale_correction_is_refused_as_a_conflict_nothing_is_merged_and_the_first_change_stands()
    {
        var read = await RecordAsync();                                                              // two people read the same version
        await CorrectAsync(read, Same(read) with { Label = "First change" }, actor: S.Actor);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => CorrectAsync(read, Same(read) with { Label = "Second change" }, actor: Other));
        Assert.Equal("First change", (await Svc(s => s.GetAsync(read.Id, default))).Label);
        Assert.Equal(2, (await CountsAsync()).Versions);
    }

    [Fact]
    public async Task Six_simultaneous_corrections_of_one_version_one_wins_and_the_others_are_told()
    {
        var d = await RecordAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            try { await CorrectAsync(d, Same(d) with { Label = $"Change {i}" }); return "saved"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Equal(1, outcomes.Count(o => o == "saved"));
        Assert.Equal(2, (await CountsAsync()).Versions);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("!!!")]
    public async Task A_missing_or_invalid_row_version_is_a_400_and_changes_nothing(string? version)
    {
        var d = await RecordAsync();
        var e = await Refused(() => Svc(s => s.CorrectAsync(d.Id, version, Same(d) with { Label = "x" }, "why", S.Actor, default)));
        Assert.Equal(400, e.StatusCode);
        Assert.StartsWith("row_version_", e.Code);
        Assert.Equal("row_version", (await Refused(() => Svc(s => s.WithdrawAsync(d.Id, version, "why", S.Actor, default)))).Code[..11]);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    // ---------- withdrawing ----------

    [Fact]
    public async Task A_wrong_diagnosis_is_withdrawn_with_a_reason_stays_in_the_record_and_keeps_its_treatment_plan_reference()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-7"));
        var w = await WithdrawAsync(d, "Entered on the wrong patient");
        Assert.Equal((DiagnosisStatuses.Withdrawn, "Entered on the wrong patient", "Dr. Okafor", "plan-7", "Unresolved"), (w.Status, w.WithdrawnReason, w.WithdrawnByName, w.TreatmentPlanReference, w.TreatmentPlanReferenceState));
        var h = await HistoryAsync(d.Id);
        Assert.Equal(new[] { "Recorded", "Withdrawn" }, h.Versions.Select(v => v.ChangeType));
        Assert.Equal((DiagnosisStatuses.Withdrawn, "plan-7"), (h.Versions[1].Status, h.Versions[1].TreatmentPlanReference));
        Assert.Equal(1, await WithDb(db => db.Diagnoses.CountAsync()));                                // never deleted
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task Withdrawing_needs_a_reason_and_changes_nothing_without_one(string? reason)
    {
        var d = await RecordAsync();
        Assert.Equal("reason_required", (await Refused(() => WithdrawAsync(d, reason))).Code);
        Assert.Equal(DiagnosisStatuses.Active, (await Svc(s => s.GetAsync(d.Id, default))).Status);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task Withdrawing_twice_is_quiet_and_a_withdrawn_diagnosis_cannot_be_corrected()
    {
        var d = await RecordAsync();
        var w = await WithdrawAsync(d);
        var again = await WithdrawAsync(d, "another reason", version: "stale");                       // quiet whatever version the caller holds
        Assert.Equal((w.RowVersion, "Entered on the wrong patient"), (again.RowVersion, again.WithdrawnReason));
        Assert.Equal((1, 2, 2), await CountsAsync());
        var e = await Refused(() => CorrectAsync(w, Same(w) with { Label = "x" }));
        Assert.Equal(("diagnosis_withdrawn", 409), (e.Code, e.StatusCode));
    }

    // ---------- reading ----------

    [Fact]
    public async Task A_patients_diagnoses_are_listed_newest_first_by_patient_and_encounter_and_withdrawn_ones_only_on_request()
    {
        var second = await EncounterAsync(Ann);
        var a = await RecordAsync(In(_annEncounter, "First"));
        await Task.Delay(20);
        var b = await RecordAsync(In(second, "Second"));
        await Task.Delay(20);
        var c = await RecordAsync(In(second, "Third"));
        await RecordAsync(In(_boEncounter, "Bo's"), patient: Bo);
        await WithdrawAsync(b);
        Assert.Equal(new[] { "Third", "First" }, (await Svc(s => s.ListAsync(Ann, null, false, default))).Select(x => x.Label));
        Assert.Equal(new[] { "Third", "Second", "First" }, (await Svc(s => s.ListAsync(Ann, null, true, default))).Select(x => x.Label));
        Assert.Equal(new[] { "Third" }, (await Svc(s => s.ListAsync(Ann, second, false, default))).Select(x => x.Label));
        Assert.Empty(await Svc(s => s.ListAsync(Ann, _boEncounter, true, default)));                   // another patient's encounter lists nothing
        Assert.Equal(new[] { "Bo's" }, (await Svc(s => s.ListAsync(Bo, null, false, default))).Select(x => x.Label));
        Assert.NotEqual(a.Id, c.Id);
    }

    [Fact]
    public async Task The_history_lists_every_step_in_order_with_who_and_when_and_unknown_ids_are_404s()
    {
        var d = await RecordAsync(actor: S.Actor);
        await CorrectAsync(d, Same(d) with { Label = "Changed" }, actor: Other);
        var h = await HistoryAsync(d.Id);
        Assert.Equal(new[] { (1, "Dr. Okafor"), (2, "Hana Hygienist") }, h.Versions.Select(v => (v.VersionNumber, v.ActorName!)));
        Assert.All(h.Versions, v => Assert.NotEqual(default, v.OccurredAtUtc));
        Assert.Equal("diagnosis_not_found", (await Refused(() => HistoryAsync(Guid.NewGuid()))).Code);
        Assert.Equal("diagnosis_not_found", (await Refused(() => Svc(s => s.GetAsync(Guid.NewGuid(), default)))).Code);
        Assert.Equal("diagnosis_not_found", (await Refused(() => Svc(s => s.CorrectAsync(Guid.NewGuid(), Convert.ToBase64String(new byte[8]), Same(d), "why", S.Actor, default)))).Code);
        Assert.Equal("patient_not_found", (await Refused(() => Svc(s => s.ListAsync(Guid.NewGuid(), null, false, default)))).Code);
    }
}
