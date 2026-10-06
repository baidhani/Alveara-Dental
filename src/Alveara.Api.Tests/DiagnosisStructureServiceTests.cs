using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-013-C01 behaviour, against real SQL Server: a diagnosis is stored structurally with or without coding; optional coding, source and region survive a round trip, a replay and every later change;
/// an amendment keeps the prior values with who, when and why and carries the treatment-plan forward reference and its provenance through untouched; Resolved and Active are a recorded, reversible
/// lifecycle and Withdrawn stays final; links to findings and periodontal charts are same-patient only, idempotent and append-only; a treatment-plan reference can never be marked resolved; and every
/// failure path (a wrong coding, an invalid link, a stale amendment, a withdrawn diagnosis, a failed log) leaves nothing behind.
/// </summary>
public class DiagnosisStructureServiceTests : SafetyTestBase
{
    private Guid _annEncounter;
    private int _findings;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _annEncounter = await EncounterAsync(Ann);
    }

    private async Task<Guid> EncounterAsync(Guid patient)
    {
        await using var db = Fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = new DateTimeOffset(2030, 1, 14, 15, 0, 0, TimeSpan.Zero), Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private async Task<Guid> FindingAsync(Guid patient)
    {
        await using var db = Fixture.CreateContext();
        var f = new ToothFinding { Id = Guid.NewGuid(), PatientId = patient, ToothKey = ToothKeys.All[_findings++ % 32], Condition = "Crown", ConditionScope = "WholeTooth", State = "Diagnosed", Status = "Active", CreatedAtUtc = DateTimeOffset.UtcNow };
        db.ToothFindings.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private async Task<Guid> ChartAsync(Guid patient)
    {
        await using var db = Fixture.CreateContext();
        var e = new PerioExam { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = Guid.NewGuid().ToString("N"), RecordedAtUtc = new DateTimeOffset(2030, 2, 3, 9, 0, 0, TimeSpan.Zero), RecordedByUserId = S.Actor, ReadingCount = 1 };
        db.PerioExams.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private static DiagnosisInput In(Guid encounter, string? system = null, string? code = null, string? source = null, string? note = null, string? region = null, string? tooth = null, string? plan = null, string? state = null)
        => new(encounter, "Chronic periodontitis", tooth, null, plan, system, code, source, note, region, state);

    private Task<T> Svc<T>(Func<DiagnosisService, Task<T>> action) => WithDb(db => action(new DiagnosisService(db, Clock)));
    private Task<DiagnosisView> RecordAsync(DiagnosisInput? input = null, string? key = null, Guid? actor = null) => Svc(s => s.RecordAsync(Ann, key ?? Guid.NewGuid().ToString("N"), input ?? In(_annEncounter), actor ?? S.Actor, default));
    private Task<DiagnosisView> AmendAsync(DiagnosisView d, DiagnosisAmendment a, string? reason = "Mapped to the practice's code list", string? version = null, Guid? actor = null) => Svc(s => s.AmendAsync(d.Id, version ?? d.RowVersion, a, reason, actor ?? S.Actor, default));
    private Task<DiagnosisView> ResolveAsync(DiagnosisView d, string? reason = "Healed at review", string? version = null) => Svc(s => s.ResolveAsync(d.Id, version ?? d.RowVersion, reason, S.Actor, default));
    private Task<DiagnosisView> ReactivateAsync(DiagnosisView d, string? reason = "Came back", string? version = null) => Svc(s => s.ReactivateAsync(d.Id, version ?? d.RowVersion, reason, S.Actor, default));
    private Task<DiagnosisView> WithdrawAsync(DiagnosisView d) => Svc(s => s.WithdrawAsync(d.Id, d.RowVersion, "Entered on the wrong patient", S.Actor, default));
    private Task<DiagnosisView> LinkAsync(DiagnosisView d, string type, Guid target) => Svc(s => s.LinkAsync(d.Id, type, target, S.Actor, default));
    private Task<DiagnosisView> GetAsync(Guid id) => Svc(s => s.GetAsync(id, default));
    private Task<DiagnosisHistoryView> HistoryAsync(Guid id) => Svc(s => s.HistoryAsync(id, default));
    private static async Task<DiagnosisException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<DiagnosisException>(action);
    private static DiagnosisAmendment Same(DiagnosisView d) => new(d.ToothKey, d.RegionKey, d.CodingSystem, d.Code, d.Source == DiagnosisSources.Manual ? null : d.Source, d.SourceNote);

    private async Task<(int Diagnoses, int Versions, int Audit, int Links)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Diagnoses.CountAsync(), await db.DiagnosisVersions.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(Diagnosis)), await db.DiagnosisLinks.CountAsync());
    }

    // ---------- stored structurally, with or without a code system; round trip ----------

    [Fact]
    public async Task A_diagnosis_is_stored_structurally_when_no_coding_system_is_configured_and_reads_as_manual_and_uncoded()
    {
        var d = await RecordAsync();
        Assert.Equal((null, null, DiagnosisSources.Manual, null, null, DiagnosisStatuses.Active), (d.CodingSystem, d.Code, d.Source, d.SourceNote, d.RegionKey, d.Status));
        Assert.Empty(d.Links!);
    }

    [Fact]
    public async Task Coding_source_and_region_survive_a_round_trip_through_the_record_the_read_the_list_and_the_history()
    {
        var d = await RecordAsync(In(_annEncounter, "ICD-10-CM", "K05.311", "Mapped", "From the imported chart", "UpperArch"));
        Assert.Equal(("ICD-10-CM", "K05.311", "Mapped", "From the imported chart", "UpperArch"), (d.CodingSystem, d.Code, d.Source, d.SourceNote, d.RegionKey));
        var read = await GetAsync(d.Id);
        Assert.Equal((d.CodingSystem, d.Code, d.Source, d.SourceNote, d.RegionKey), (read.CodingSystem, read.Code, read.Source, read.SourceNote, read.RegionKey));
        var listed = (await Svc(s => s.ListAsync(Ann, null, false, default))).Single();
        Assert.Equal(("ICD-10-CM", "K05.311", "Mapped", "UpperArch"), (listed.CodingSystem, listed.Code, listed.Source, listed.RegionKey));
        var v = (await HistoryAsync(d.Id)).Versions.Single();
        Assert.Equal((DiagnosisChangeTypes.Recorded, "ICD-10-CM", "K05.311", "Mapped", "From the imported chart", "UpperArch"), (v.ChangeType, v.CodingSystem, v.Code, v.Source, v.SourceNote, v.RegionKey));
    }

    [Fact]
    public async Task The_same_key_and_the_same_structured_entry_replays_and_a_different_code_under_the_key_is_refused()
    {
        var first = await RecordAsync(In(_annEncounter, "Local", "A1"), key: "visit-1");
        var again = await RecordAsync(In(_annEncounter, "Local", "A1"), key: "visit-1");
        Assert.Equal(first.Id, again.Id);
        Assert.Equal("idempotency_key_reused", (await Refused(() => RecordAsync(In(_annEncounter, "Local", "A2"), key: "visit-1"))).Code);
        Assert.Equal("idempotency_key_reused", (await Refused(() => RecordAsync(In(_annEncounter, region: "UpperArch"), key: "visit-1"))).Code);
        Assert.Equal((1, 1, 1, 0), await CountsAsync());
    }

    // ---------- failure paths of the entry ----------

    [Fact]
    public async Task An_unknown_coding_system_a_half_given_coding_a_bad_region_and_a_tooth_with_a_region_are_refused_naming_every_problem_and_nothing_is_saved()
    {
        var e = await Refused(() => RecordAsync(new DiagnosisInput(_annEncounter, "Perio", "16", null, null, "SNOMED", null, "Wrong", null, "Nowhere")));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        var found = e.Problems.Select(p => $"{p.Field}:{p.Code}").ToArray();
        foreach (var x in new[] { "codingSystem:unsupported_system", "code:coding_incomplete", "source:unsupported_source", "regionKey:unknown_region" }) Assert.Contains(x, found);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    // ---------- amendment preserves the prior value and attribution ----------

    [Fact]
    public async Task An_amendment_changes_the_structure_keeps_the_prior_values_in_the_history_with_who_when_and_why_and_is_logged()
    {
        var d = await RecordAsync(In(_annEncounter, tooth: "16"));
        var amended = await AmendAsync(d, new(null, "UpperRight", "ICD-10-CM", "K05.3", "Mapped", "Mapped from the old system"), "Moved to the arch and coded", actor: Other);
        Assert.Equal((null, "UpperRight", "ICD-10-CM", "K05.3", "Mapped"), (amended.ToothKey, amended.RegionKey, amended.CodingSystem, amended.Code, amended.Source));
        Assert.Equal(("Hana Hygienist", DiagnosisStatuses.Active), (amended.UpdatedByName, amended.Status));
        var h = (await HistoryAsync(d.Id)).Versions;
        Assert.Equal(new[] { DiagnosisChangeTypes.Recorded, DiagnosisChangeTypes.Amended }, h.Select(v => v.ChangeType));
        Assert.Equal(("16", null, null, null, "Manual", null), (h[0].ToothKey, h[0].RegionKey, h[0].CodingSystem, h[0].Code, h[0].Source, h[0].Reason));        // the prior authoritative value is still there
        Assert.Equal((null, "UpperRight", "ICD-10-CM", "K05.3", "Mapped", "Moved to the arch and coded", "Hana Hygienist"), (h[1].ToothKey, h[1].RegionKey, h[1].CodingSystem, h[1].Code, h[1].Source, h[1].Reason, h[1].ActorName));
        Assert.True(h[1].OccurredAtUtc >= h[0].OccurredAtUtc);
        var audit = await AuditAsync(nameof(Diagnosis));
        Assert.Equal(new[] { "DiagnosisRecorded", "DiagnosisAmended" }, audit.Select(a => a.Type));
        Assert.All(audit, a => { foreach (var secret in new[] { "K05", "UpperRight", "Mapped", "16" }) Assert.DoesNotContain(secret, a.Details); });
    }

    [Fact]
    public async Task An_amendment_carries_the_treatment_plan_reference_and_its_provenance_through_untouched_and_it_stays_unresolved()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-2026-07"), actor: S.Actor);
        var amended = await AmendAsync(d, new(null, null, "Local", "P9", null, null), actor: Other);
        Assert.Equal(("plan-2026-07", "Unresolved"), (amended.TreatmentPlanReference, amended.TreatmentPlanReferenceState));
        var resolved = await ResolveAsync(amended);
        var h = (await HistoryAsync(d.Id)).Versions;
        Assert.All(h, v => Assert.Equal(("plan-2026-07", "Unresolved"), (v.TreatmentPlanReference, v.TreatmentPlanReferenceState)));        // every step carries it, with its own actor and time
        Assert.Equal(new[] { "Dr. Okafor", "Hana Hygienist", "Dr. Okafor" }, h.Select(v => v.ActorName));
        Assert.Equal("plan-2026-07", resolved.TreatmentPlanReference);
        var withdrawn = await WithdrawAsync(resolved);
        Assert.Equal(("plan-2026-07", DiagnosisStatuses.Withdrawn), (withdrawn.TreatmentPlanReference, withdrawn.Status));
    }

    [Fact]
    public async Task An_amendment_that_changes_nothing_is_quiet_and_one_without_a_reason_or_with_bad_data_changes_nothing()
    {
        var d = await RecordAsync(In(_annEncounter, "Local", "A1"));
        var quiet = await AmendAsync(d, Same(d));
        Assert.Equal(d.RowVersion, quiet.RowVersion);
        Assert.Equal((1, 1, 1, 0), await CountsAsync());
        Assert.Equal("validation_failed", (await Refused(() => AmendAsync(d, Same(d) with { CodingSystem = "Local", Code = "B2" }, reason: null))).Code);
        var bad = await Refused(() => AmendAsync(d, new("16", "UpperArch", "Nope", "x", "Bad", null)));
        Assert.Contains("regionKey:conflicts_with_tooth", bad.Problems.Select(p => $"{p.Field}:{p.Code}"));
        Assert.Contains("codingSystem:unsupported_system", bad.Problems.Select(p => $"{p.Field}:{p.Code}"));
        Assert.Equal((1, 1, 1, 0), await CountsAsync());
    }

    [Fact]
    public async Task An_amendment_can_remove_the_coding_and_the_old_coding_stays_in_the_history()
    {
        var d = await RecordAsync(In(_annEncounter, "ICD-10-CM", "K05.3"));
        var removed = await AmendAsync(d, new(null, null, null, null, null, null), "The code was wrong");
        Assert.Equal((null, null), (removed.CodingSystem, removed.Code));
        Assert.Equal(("ICD-10-CM", "K05.3"), ((await HistoryAsync(d.Id)).Versions[0].CodingSystem, (await HistoryAsync(d.Id)).Versions[0].Code));
    }

    [Fact]
    public async Task A_stale_amendment_is_refused_as_a_conflict_and_changes_nothing()
    {
        var d = await RecordAsync();
        await AmendAsync(d, new(null, null, "Local", "A1", null, null));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AmendAsync(d, new(null, null, "Local", "B2", null, null)));
        Assert.Equal("A1", (await GetAsync(d.Id)).Code);
        Assert.Equal("row_version_required", (await Refused(() => Svc(s => s.AmendAsync(d.Id, null, new(null, null, null, null, null, null), "x", S.Actor, default)))).Code);
    }

    // ---------- the lifecycle: Active, Resolved, Withdrawn ----------

    [Fact]
    public async Task A_diagnosis_can_be_resolved_and_reactivated_each_with_a_reason_and_a_history_entry_and_a_resolved_one_stays_in_the_default_list()
    {
        var d = await RecordAsync();
        var resolved = await ResolveAsync(d, "Healed at review");
        Assert.Equal(DiagnosisStatuses.Resolved, resolved.Status);
        Assert.Single(await Svc(s => s.ListAsync(Ann, null, false, default)));                                    // resolved is still on the patient's list
        var back = await ReactivateAsync(resolved, "Came back");
        Assert.Equal(DiagnosisStatuses.Active, back.Status);
        var h = (await HistoryAsync(d.Id)).Versions;
        Assert.Equal(new[] { DiagnosisChangeTypes.Recorded, DiagnosisChangeTypes.Resolved, DiagnosisChangeTypes.Reactivated }, h.Select(v => v.ChangeType));
        Assert.Equal(new[] { null, "Healed at review", "Came back" }, h.Select(v => v.Reason));
        Assert.Equal(new[] { DiagnosisStatuses.Active, DiagnosisStatuses.Resolved, DiagnosisStatuses.Active }, h.Select(v => v.Status));
        Assert.Equal(new[] { "DiagnosisRecorded", "DiagnosisResolved", "DiagnosisReactivated" }, (await AuditAsync(nameof(Diagnosis))).Select(a => a.Type));
    }

    [Fact]
    public async Task Resolving_a_resolved_diagnosis_and_reactivating_an_active_one_are_quiet_and_a_reason_is_required()
    {
        var d = await RecordAsync();
        Assert.Equal(d.RowVersion, (await ReactivateAsync(d)).RowVersion);
        var resolved = await ResolveAsync(d);
        Assert.Equal(resolved.RowVersion, (await ResolveAsync(resolved)).RowVersion);
        Assert.Equal("reason_required", (await Refused(() => ResolveAsync(d, reason: null))).Code);
        Assert.Equal("reason_required", (await Refused(() => ReactivateAsync(resolved, reason: "  "))).Code);
        Assert.Equal(2, (await HistoryAsync(d.Id)).Versions.Count);                                        // the quiet repeats added no history
    }

    [Fact]
    public async Task A_withdrawn_diagnosis_can_be_neither_resolved_reactivated_amended_nor_linked_and_a_resolved_one_can_still_be_withdrawn_or_corrected()
    {
        var d = await RecordAsync();
        var resolved = await ResolveAsync(d);
        var corrected = await Svc(s => s.CorrectAsync(resolved.Id, resolved.RowVersion, new("Generalized gingivitis", null, null, null, false), "Clarified", S.Actor, default));
        Assert.Equal((DiagnosisStatuses.Resolved, "Generalized gingivitis"), (corrected.Status, corrected.Label));
        var withdrawn = await WithdrawAsync(corrected);
        Assert.Equal(DiagnosisStatuses.Withdrawn, withdrawn.Status);
        var target = await FindingAsync(Ann);
        foreach (var attempt in new Func<Task<DiagnosisView>>[] { () => ResolveAsync(withdrawn), () => ReactivateAsync(withdrawn), () => AmendAsync(withdrawn, Same(withdrawn)), () => LinkAsync(withdrawn, "Finding", target) })
            Assert.Equal(("diagnosis_withdrawn", 409), ((await Refused(attempt)).Code, (await Refused(attempt)).StatusCode));
    }

    [Fact]
    public async Task A_stale_status_change_is_a_conflict_and_changes_nothing()
    {
        var d = await RecordAsync();
        await ResolveAsync(d);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Svc(s => s.WithdrawAsync(d.Id, d.RowVersion, "late", S.Actor, default)));
        Assert.Equal(DiagnosisStatuses.Resolved, (await GetAsync(d.Id)).Status);
    }

    // ---------- links to findings and periodontal charts ----------

    [Fact]
    public async Task A_diagnosis_is_linked_to_a_finding_and_a_periodontal_chart_of_the_same_patient_with_who_and_when_and_linking_twice_is_quiet()
    {
        var d = await RecordAsync();
        var finding = await FindingAsync(Ann);
        var chart = await ChartAsync(Ann);
        var linked = await LinkAsync(d, "Finding", finding);
        linked = await LinkAsync(linked, "PerioExam", chart);
        await LinkAsync(linked, "Finding", finding);                                                        // the same link again changes nothing
        var read = await GetAsync(d.Id);
        Assert.Equal(new[] { "Finding", "PerioExam" }, read.Links!.Select(l => l.LinkType));
        Assert.Equal(new[] { finding, chart }, read.Links!.Select(l => l.TargetId));
        Assert.Contains("Crown on tooth", read.Links![0].Summary);
        Assert.Equal("Periodontal chart of 2030-02-03", read.Links![1].Summary);
        Assert.All(read.Links!, l => Assert.Equal("Dr. Okafor", l.LinkedByName));
        Assert.Equal((1, 1, 3, 2), await CountsAsync());                                                      // record + two links; the repeat wrote nothing
        Assert.Equal(new[] { "DiagnosisRecorded", "DiagnosisLinked", "DiagnosisLinked" }, (await AuditAsync(nameof(Diagnosis))).Select(a => a.Type));
    }

    [Fact]
    public async Task A_link_to_another_patients_record_or_to_nothing_is_refused_the_same_way_and_nothing_is_saved()
    {
        var d = await RecordAsync();
        var other = await FindingAsync(Bo);
        var otherChart = await ChartAsync(Bo);
        foreach (var (type, target) in new[] { ("Finding", other), ("PerioExam", otherChart), ("Finding", Guid.NewGuid()), ("PerioExam", Guid.NewGuid()) })
        {
            var e = await Refused(() => LinkAsync(d, type, target));
            Assert.Equal(("link_target_not_found", 404), (e.Code, e.StatusCode));
        }
        Assert.Equal((1, 1, 1, 0), await CountsAsync());
    }

    [Fact]
    public async Task An_unsupported_link_type_including_a_treatment_plan_and_a_missing_target_are_refused()
    {
        var d = await RecordAsync();
        foreach (var type in new[] { "TreatmentPlan", "finding", "", null })
            Assert.Equal("unsupported_link_type", (await Refused(() => Svc(s => s.LinkAsync(d.Id, type, Guid.NewGuid(), S.Actor, default)))).Problems.Single().Code);
        Assert.Equal("required", (await Refused(() => Svc(s => s.LinkAsync(d.Id, "Finding", null, S.Actor, default)))).Problems.Single().Code);
        Assert.Equal("diagnosis_not_found", (await Refused(() => Svc(s => s.LinkAsync(Guid.NewGuid(), "Finding", Guid.NewGuid(), S.Actor, default)))).Code);
        Assert.Equal((1, 1, 1, 0), await CountsAsync());
    }

    // ---------- the treatment-plan reference is never resolved ----------

    [Fact]
    public async Task A_request_to_mark_the_treatment_plan_reference_resolved_is_refused_on_every_path_and_nothing_changes()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-1"));
        var asks = new Func<Task<DiagnosisView>>[]
        {
            () => RecordAsync(In(_annEncounter, plan: "plan-2", state: "Resolved")),
            () => Svc(s => s.CorrectAsync(d.Id, d.RowVersion, new("Chronic periodontitis", null, null, null, false, "Resolved"), "x", S.Actor, default)),
            () => AmendAsync(d, new(null, null, "Local", "A1", null, null, "Valid")),
        };
        foreach (var ask in asks) Assert.Contains("treatmentPlanReferenceState:not_supported", (await Refused(ask)).Problems.Select(p => $"{p.Field}:{p.Code}"));
        Assert.Equal((1, 1, 1, 0), await CountsAsync());
        Assert.Equal(("plan-1", "Unresolved"), ((await GetAsync(d.Id)).TreatmentPlanReference, (await GetAsync(d.Id)).TreatmentPlanReferenceState));
        // saying it is Unresolved is accepted: it is the only state there is
        Assert.Equal("Unresolved", (await RecordAsync(In(_annEncounter, plan: "plan-3", state: "Unresolved"))).TreatmentPlanReferenceState);
    }

    [Fact]
    public async Task A_diagnosis_with_a_treatment_plan_reference_or_links_is_never_deleted_and_no_treatment_plan_is_consulted()
    {
        var d = await RecordAsync(In(_annEncounter, plan: "plan-1"));
        await LinkAsync(d, "Finding", await FindingAsync(Ann));
        await using var db = Fixture.CreateContext();
        var e = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [Diagnoses]"));
        Assert.Equal(51071, e.Number);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name LIKE '%TreatmentPlan%'").SingleAsync());
    }

    // ---------- a failed log saves nothing ----------

    [Fact]
    public async Task A_failed_audit_write_on_an_amendment_a_status_change_or_a_link_changes_nothing()
    {
        var d = await RecordAsync();
        var finding = await FindingAsync(Ann);
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseDiagnosisAudit2] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'Diagnosis') THROW 59000, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        foreach (var attempt in new Func<Task<DiagnosisView>>[] { () => AmendAsync(d, new(null, null, "Local", "A1", null, null)), () => ResolveAsync(d), () => LinkAsync(d, "Finding", finding) })
            Assert.Equal(("save_failed", 503), ((await Refused(attempt)).Code, (await Refused(attempt)).StatusCode));
        Assert.Equal((1, 1, 1, 0), await CountsAsync());
        var unchanged = await GetAsync(d.Id);
        Assert.Equal((DiagnosisStatuses.Active, null), (unchanged.Status, unchanged.Code));
    }
}
