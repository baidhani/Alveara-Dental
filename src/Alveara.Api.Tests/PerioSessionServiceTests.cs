using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-012-C01 behaviour, against real SQL Server: a chart entered as a draft a tooth at a time and then finalized. Entry never loses what was already valid (a wrong entry refuses the whole save and
/// changes nothing); a stale edit is refused; missing and excluded teeth are skipped without corrupting the order; every change is logged with user and time in the same save; finalizing is all or
/// nothing and repeatable (a failed audit write or a failed insert leaves the draft open and untouched); closed sessions never change; and finalized charts can be linked by reference.
/// </summary>
public class PerioSessionServiceTests : SafetyTestBase
{
    private static PerioReadingInput R(string tooth = "16", string site = "B", int pd = 3, int rec = 1, bool bleed = false, bool? pus = null, bool? plaque = null) => new(tooth, site, pd, rec, bleed, pus, plaque);
    private static PerioToothInput T(string tooth = "16", int? mobility = null, int? furcation = null, bool excluded = false) => new(tooth, mobility, furcation, excluded);
    private static PerioEntryBatch Batch(PerioReadingInput[]? readings = null, PerioToothInput[]? teeth = null, PerioSiteRef[]? clearSites = null, string[]? clearTeeth = null) => new(readings, teeth, clearSites, clearTeeth);
    private static PerioReadingInput[] Tooth(string tooth, int pd = 3) => [.. PerioRules.Sites.Select(s => R(tooth, s, pd))];

    private Task<T> Svc<T>(Func<PerioSessionService, Task<T>> action) => WithDb(db => action(new PerioSessionService(db, Clock)));
    private Task<PerioSessionView> StartAsync(Guid? patient = null, Guid? actor = null) => Svc(s => s.StartAsync(patient ?? Ann, actor ?? S.Actor, default));
    private Task<PerioSessionView> SaveAsync(PerioSessionView session, PerioEntryBatch batch, Guid? actor = null, string? version = null) => Svc(s => s.SaveEntriesAsync(session.Id, version ?? session.RowVersion, batch, actor ?? S.Actor, default));
    private Task<PerioExamView> FinalizeAsync(PerioSessionView session, string? version = null, Guid? actor = null) => Svc(s => s.FinalizeAsync(session.Id, version ?? session.RowVersion, actor ?? S.Actor, default));
    private Task<PerioSessionView> GetAsync(Guid id) => Svc(s => s.GetAsync(id, default));
    private static async Task<PerioException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<PerioException>(action);

    private async Task<(int Sessions, int Readings, int Teeth, int Exams, int ExamReadings)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.PerioSessions.CountAsync(), await db.PerioSessionReadings.CountAsync(), await db.PerioSessionTeeth.CountAsync(), await db.PerioExams.CountAsync(), await db.PerioReadings.CountAsync());
    }

    private async Task MakeMissingAsync(string tooth, Guid? patient = null) =>
        await WithDb(db => new OdontogramService(db, Clock).RecordAsync(patient ?? Ann, tooth, null, FindingConditions.Missing, FindingStates.Existing, S.Actor, default));

    // ---------- starting ----------

    [Fact]
    public async Task Starting_makes_one_draft_per_patient_and_starting_again_returns_it()
    {
        var first = await StartAsync();
        var again = await StartAsync();
        Assert.Equal((first.Id, PerioSessionStatuses.Draft), (again.Id, again.Status));
        Assert.Equal(("Dr. Okafor", 192), (first.StartedByName, first.ChartableSites));
        Assert.Null(first.ExamId);
        Assert.Equal(("18", "DB"), (first.Next!.ToothKey, first.Next.Site));
        Assert.NotEqual(first.Id, (await StartAsync(Bo)).Id);                                          // another patient has their own
        Assert.Equal(2, (await AuditAsync(nameof(PerioSession))).Count(a => a.Type == "PerioSessionStarted"));      // one per session started (Ann's second call started nothing), never one per call
    }

    [Fact]
    public async Task Eight_simultaneous_starts_end_with_one_draft_and_everyone_gets_it()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => StartAsync())));
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal(1, (await CountsAsync()).Sessions);
    }

    [Fact]
    public async Task Starting_for_an_unknown_patient_is_a_404_and_the_current_draft_is_null_until_one_is_started()
    {
        Assert.Equal("patient_not_found", (await Refused(() => StartAsync(Guid.NewGuid()))).Code);
        Assert.Null(await Svc(s => s.CurrentAsync(Ann, default)));
        var started = await StartAsync();
        Assert.Equal(started.Id, (await Svc(s => s.CurrentAsync(Ann, default)))!.Id);
    }

    // ---------- entering a tooth at a time ----------

    [Fact]
    public async Task A_tooth_is_saved_with_its_six_sites_and_grades_and_the_draft_reads_them_back_in_site_order_with_derived_attachment_loss()
    {
        var s0 = await StartAsync();
        var s1 = await SaveAsync(s0, Batch([R("16", "ML", 5, 2, true, true, false), R("16", "DB", 3, 0, false, false, true), R("16", "B", 4, 1), R("16", "MB", 2, 0), R("16", "DL", 6, 1), R("16", "L", 3, 0)], [T("16", 1, 2)]));
        Assert.Equal(new[] { "DB", "B", "MB", "DL", "L", "ML" }, s1.Readings.Select(r => r.Site));
        var ml = s1.Readings.Single(r => r.Site == "ML");
        Assert.Equal((5, 2, 7, true, true, false), (ml.ProbingDepthMm, ml.RecessionMm, ml.AttachmentLossMm, ml.Bleeding, ml.Suppuration, ml.Plaque));
        Assert.Null(s1.Readings.Single(r => r.Site == "B").Suppuration);                               // not assessed stays not assessed
        Assert.Equal(new PerioToothView("16", 1, 2, false), Assert.Single(s1.Teeth));
        Assert.NotEqual(s0.RowVersion, s1.RowVersion);
        Assert.Equal(("18", "DB"), (s1.Next!.ToothKey, s1.Next.Site));                                  // 18 still comes first in the sweep
    }

    [Fact]
    public async Task A_site_can_be_changed_and_a_site_or_a_tooth_cleared()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R("16", "B", 3), R("16", "MB", 4)], [T("16", 1)]));
        s = await SaveAsync(s, Batch([R("16", "B", 7, 2, true)]));
        Assert.Equal((7, true), (s.Readings.Single(r => r.Site == "B").ProbingDepthMm, s.Readings.Single(r => r.Site == "B").Bleeding));
        s = await SaveAsync(s, Batch(clearSites: [new PerioSiteRef("16", "MB")]));
        Assert.Equal(new[] { "B" }, s.Readings.Select(r => r.Site));
        s = await SaveAsync(s, Batch(clearTeeth: ["16"]));
        Assert.Empty(s.Teeth);
        s = await SaveAsync(s, Batch(clearSites: [new PerioSiteRef("47", "DL")]));                    // clearing what is not there is quiet
        Assert.Single(s.Readings);
    }

    [Fact]
    public async Task An_empty_save_changes_nothing_and_not_even_the_version()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R()]));
        var same = await SaveAsync(s, Batch());
        Assert.Equal(s.RowVersion, same.RowVersion);
        Assert.Equal(s.RowVersion, (await SaveAsync(s, null!)).RowVersion);
    }

    [Fact]
    public async Task Every_save_is_logged_with_the_user_and_a_timestamp_and_the_log_never_holds_a_tooth_or_a_measurement()
    {
        var s = await SaveAsync(await StartAsync(actor: S.Actor), Batch([R("47", "DL", 11, 4, true)]), actor: Other);
        await SaveAsync(s, Batch([R("47", "ML", 6, 2)]), actor: S.Actor);
        var audit = await AuditAsync(nameof(PerioSession));
        Assert.Equal(new[] { "PerioSessionStarted", "PerioSessionUpdated", "PerioSessionUpdated" }, audit.Select(a => a.Type));
        Assert.Equal(new Guid?[] { S.Actor, Other, S.Actor }, audit.Select(a => a.By));
        Assert.All(audit, a => { Assert.DoesNotContain("47", a.Details); Assert.DoesNotContain("11", a.Details); Assert.DoesNotContain("DL", a.Details); });
        Assert.Equal("Dr. Okafor", (await GetAsync(s.Id)).UpdatedByName);                                       // the last writer is shown
    }

    // ---------- wrong entries never discard valid ones ----------

    [Fact]
    public async Task A_wrong_entry_refuses_the_whole_save_names_every_problem_and_leaves_the_draft_exactly_as_it_was()
    {
        var s = await SaveAsync(await StartAsync(), Batch(Tooth("18"), [T("18", 1)]));                   // a valid tooth is already entered
        var before = await GetAsync(s.Id);
        var e = await Refused(() => SaveAsync(s, Batch([R("17", "B", 99, 1), R("17", "MB", 3, 1), R("19", "B")], [T("11", furcation: 2), T("17", 9)])));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(new[] { "out_of_range", "unknown_tooth", "furcation_not_applicable", "out_of_range" }, e.Problems.Select(p => p.Code));
        var after = await GetAsync(s.Id);
        Assert.Equal(before.RowVersion, after.RowVersion);                                                 // not even touched
        Assert.Equal(6, after.Readings.Count);                                                             // tooth 18 is all still there, and 17's valid site was not saved
        Assert.All(after.Readings, r => Assert.Equal("18", r.ToothKey));
        Assert.Equal(1, (await CountsAsync()).Teeth);
        Assert.Single(await AuditAsync(nameof(PerioSession)), a => a.Type == "PerioSessionUpdated");     // the refused save was not logged as a change
    }

    [Fact]
    public async Task A_correction_to_the_refused_save_then_goes_through_and_the_earlier_entries_are_still_there()
    {
        var s = await SaveAsync(await StartAsync(), Batch(Tooth("18")));
        await Refused(() => SaveAsync(s, Batch([R("17", "B", 40)])));
        s = await SaveAsync(s, Batch([R("17", "B", 4)]));
        Assert.Equal(7, s.Readings.Count);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("!!!")]
    public async Task A_missing_or_invalid_row_version_is_a_400_and_changes_nothing(string? version)
    {
        var s = await StartAsync();
        var e = await Refused(() => Svc(x => x.SaveEntriesAsync(s.Id, version, Batch([R()]), S.Actor, default)));
        Assert.Equal(400, e.StatusCode);
        Assert.StartsWith("row_version_", e.Code);
        Assert.Equal(0, (await CountsAsync()).Readings);
    }

    [Fact]
    public async Task A_save_to_an_unknown_session_is_a_404()
        => Assert.Equal("session_not_found", (await Refused(() => Svc(x => x.SaveEntriesAsync(Guid.NewGuid(), Convert.ToBase64String(new byte[8]), Batch([R()]), S.Actor, default)))).Code);

    // ---------- a stale edit ----------

    [Fact]
    public async Task A_stale_edit_is_refused_as_a_conflict_nothing_is_merged_and_the_first_writers_entries_stand()
    {
        var read = await StartAsync();                                                                   // two people read the same version
        await SaveAsync(read, Batch([R("16", "B", 3)]), actor: S.Actor);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SaveAsync(read, Batch([R("17", "B", 9)]), actor: Other));
        var now = await GetAsync(read.Id);
        Assert.Equal("16", Assert.Single(now.Readings).ToothKey);
        Assert.Equal((1, 0), ((await CountsAsync()).Readings, (await CountsAsync()).Exams));
        var reloaded = await SaveAsync(now, Batch([R("17", "B", 9)]), actor: Other);                      // after reloading, the second person can go on
        Assert.Equal(2, reloaded.Readings.Count);
    }

    [Fact]
    public async Task Two_people_saving_the_same_version_at_the_same_moment_one_wins_and_the_other_is_told()
    {
        var s = await StartAsync();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            try { await SaveAsync(s, Batch([R("16", PerioRules.Sites[i], 3)])); return "saved"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Equal(1, outcomes.Count(o => o == "saved"));
        Assert.Equal(1, (await CountsAsync()).Readings);                                                  // only the winner's entry exists
    }

    // ---------- excluded and missing teeth ----------

    [Fact]
    public async Task An_excluded_tooth_is_skipped_by_the_sweep_without_disturbing_the_order_and_takes_no_readings()
    {
        var s = await SaveAsync(await StartAsync(), Batch(teeth: [T("18", excluded: true)]));
        Assert.Equal(("17", "DB"), (s.Next!.ToothKey, s.Next.Site));
        Assert.Equal(186, s.ChartableSites);
        var e = await Refused(() => SaveAsync(s, Batch([R("18", "B")])));
        Assert.Equal("excluded_tooth", Assert.Single(e.Problems).Code);
        Assert.Empty((await GetAsync(s.Id)).Readings);
    }

    [Fact]
    public async Task Excluding_a_tooth_that_has_readings_is_refused_unless_the_same_save_clears_them()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R("16", "B"), R("16", "MB")]));
        Assert.Equal("excluded_tooth", (await Refused(() => SaveAsync(s, Batch(teeth: [T("16", excluded: true)])))).Problems.First().Code);
        Assert.Equal(2, (await GetAsync(s.Id)).Readings.Count);                                            // unchanged
        s = await SaveAsync(s, Batch(teeth: [T("16", excluded: true)], clearSites: [new("16", "B"), new("16", "MB")]));
        Assert.Empty(s.Readings);
        Assert.True(Assert.Single(s.Teeth).Excluded);
    }

    [Fact]
    public async Task A_tooth_the_odontogram_records_as_missing_is_listed_skipped_by_the_sweep_and_cannot_be_charted_unless_marked_not_charted()
    {
        await MakeMissingAsync("18");
        var s = await StartAsync();
        Assert.Equal(new[] { "18" }, s.AbsentTeeth);
        Assert.Equal(("17", "DB"), (s.Next!.ToothKey, s.Next.Site));                                      // keyboard entry never stops on a missing tooth
        Assert.Equal(186, s.ChartableSites);
        var e = await Refused(() => SaveAsync(s, Batch([R("18", "B")])));
        var p = Assert.Single(e.Problems);
        Assert.Equal(("tooth_absent", "18"), (p.Code, p.ToothKey));
        s = await SaveAsync(s, Batch(teeth: [T("18", excluded: true)]));                                   // the way out
        Assert.True(Assert.Single(s.Teeth).Excluded);
    }

    [Fact]
    public async Task A_change_in_the_odontogram_never_blocks_a_save_of_an_unrelated_tooth_but_does_stop_the_finalize()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R("17", "B", 3)]));
        await MakeMissingAsync("17");                                                                       // now the odontogram says 17 is missing
        s = await SaveAsync(s, Batch([R("16", "B", 3)]));                                                   // an unrelated save still works
        var e = await Refused(() => FinalizeAsync(s));
        Assert.Equal(("validation_failed", "tooth_absent"), (e.Code, e.Problems.Single().Code));
        Assert.Equal(PerioSessionStatuses.Draft, (await GetAsync(s.Id)).Status);                            // still open for the person to deal with
        Assert.Equal(0, (await CountsAsync()).Exams);
    }

    [Fact]
    public async Task The_sweep_advances_site_by_site_and_ends_when_every_chartable_site_has_a_reading()
    {
        var s = await StartAsync();
        var sweep = PerioSiteModel.Sweep(new HashSet<string> { "16" });
        s = await SaveAsync(s, Batch(teeth: [T("16", excluded: true)]));
        Assert.Equal(sweep[0], (s.Next!.ToothKey, s.Next.Site));
        for (var i = 0; i < 3; i++)
        {
            s = await SaveAsync(s, Batch([R(sweep[i].Tooth, sweep[i].Site)]));
            Assert.Equal(sweep[i + 1], (s.Next!.ToothKey, s.Next.Site));
        }
        s = await SaveAsync(s, Batch([.. sweep.Skip(3).Select(x => R(x.Tooth, x.Site))]));
        Assert.Null(s.Next);
        Assert.Equal(186, s.Readings.Count);
    }

    // ---------- finalizing ----------

    [Fact]
    public async Task Finalizing_saves_the_immutable_chart_with_every_reading_and_whole_tooth_record_and_closes_the_session()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R("16", "DB", 3, 1, true, true, true), R("16", "B", 4, 0)], [T("16", 2, 3), T("17", excluded: true)]));
        var exam = await FinalizeAsync(s);
        Assert.Equal(("Dr. Okafor", 2), (exam.RecordedByName, exam.ReadingCount));
        Assert.Equal(new[] { "DB", "B" }, exam.Readings.Select(r => r.Site));
        Assert.Equal((true, true, 4), (exam.Readings[0].Suppuration, exam.Readings[0].Plaque, exam.Readings[0].AttachmentLossMm));
        Assert.Equal(new[] { new PerioToothView("16", 2, 3, false), new PerioToothView("17", null, null, true) }, exam.Teeth);
        var closed = await GetAsync(s.Id);
        Assert.Equal((PerioSessionStatuses.Finalized, exam.Id), (closed.Status, closed.ExamId));
        var history = await WithDb(db => new PerioService(db, Clock).HistoryAsync(Ann, null, default));
        Assert.Equal(exam.Id, Assert.Single(history.Exams).Id);                                              // it is an ordinary chart: STORY-012's history shows it
        var audit = (await AuditAsync(nameof(PerioExam))).Select(a => a.Type).ToList();
        Assert.Equal(new[] { "PerioExamRecorded" }, audit);
        Assert.Contains(await AuditAsync(nameof(PerioSession)), a => a.Type == "PerioSessionFinalized");
    }

    [Fact]
    public async Task Finalizing_twice_returns_the_same_chart_with_no_second_chart_or_log_entry_even_with_an_old_version()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R()]));
        var first = await FinalizeAsync(s);
        var again = await FinalizeAsync(s, version: Convert.ToBase64String(new byte[8]));
        Assert.Equal(first.Id, again.Id);
        Assert.Equal((1, 1, 1), ((await CountsAsync()).Exams, (await AuditAsync(nameof(PerioExam))).Count, (await AuditAsync(nameof(PerioSession))).Count(a => a.Type == "PerioSessionFinalized")));
    }

    [Fact]
    public async Task Six_simultaneous_finalizes_make_one_chart_and_all_get_it()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R()]));
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => FinalizeAsync(s))));
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal((1, 1), ((await CountsAsync()).Exams, (await AuditAsync(nameof(PerioExam))).Count));
    }

    [Fact]
    public async Task A_draft_with_no_readings_cannot_be_finalized_and_neither_can_one_changed_by_someone_else_since_and_both_stay_open()
    {
        var s = await StartAsync();
        Assert.Equal("required", (await Refused(() => FinalizeAsync(s))).Problems.Single().Code);
        var stale = await SaveAsync(s, Batch([R("16", "B")]));
        await SaveAsync(stale, Batch([R("16", "MB")]));                                                      // someone else saves after the version `stale` was read
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => FinalizeAsync(stale));
        Assert.Equal((PerioSessionStatuses.Draft, 0, 2), ((await GetAsync(s.Id)).Status, (await CountsAsync()).Exams, (await GetAsync(s.Id)).Readings.Count));
    }

    // ---------- partial failure: all or nothing ----------

    [Fact]
    public async Task A_failed_audit_write_finalizes_nothing_and_the_same_finalize_works_once_the_log_is_back()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R("16", "B"), R("17", "B")], [T("16", 1)]));
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefusePerioExamAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'PerioExam') THROW 59000, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var e = await Refused(() => FinalizeAsync(s));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal((PerioSessionStatuses.Draft, 0, 0, 2), ((await GetAsync(s.Id)).Status, (await CountsAsync()).Exams, (await CountsAsync()).ExamReadings, (await GetAsync(s.Id)).Readings.Count));
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefusePerioExamAudit]");
        Assert.Equal(2, (await FinalizeAsync(await GetAsync(s.Id))).ReadingCount);
        Assert.Equal(1, (await CountsAsync()).Exams);
    }

    [Fact]
    public async Task A_failed_insert_halfway_through_finalizing_leaves_no_half_chart_and_the_draft_open()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R("16", "B"), R("17", "B"), R("18", "B")]));
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefusePerioReading] ON [PerioReadings] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE ToothKey = '18') THROW 59001, 'storage unavailable', 1;
END");
        var e = await Refused(() => FinalizeAsync(s));
        Assert.Equal("save_failed", e.Code);
        Assert.DoesNotContain("storage", e.Message);
        Assert.Equal((0, 0), ((await CountsAsync()).Exams, (await CountsAsync()).ExamReadings));
        var draft = await GetAsync(s.Id);
        Assert.Equal((PerioSessionStatuses.Draft, 3), (draft.Status, draft.Readings.Count));                // the session is still open with everything in it
    }

    [Fact]
    public async Task A_failed_audit_write_on_a_save_applies_none_of_the_batch()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R("16", "B")]));
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefusePerioSessionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'PerioSessionUpdated') THROW 59002, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        Assert.Equal("save_failed", (await Refused(() => SaveAsync(s, Batch([R("16", "MB"), R("17", "B")], [T("17", 1)])))).Code);
        var after = await GetAsync(s.Id);
        Assert.Equal((s.RowVersion, 1, 0), (after.RowVersion, after.Readings.Count, after.Teeth.Count));
    }

    // ---------- closed sessions ----------

    [Fact]
    public async Task A_finalized_or_abandoned_session_refuses_further_entries_and_a_new_session_can_then_be_started()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R()]));
        await FinalizeAsync(s);
        Assert.Equal(("session_closed", 409), ((await Refused(() => SaveAsync(s, Batch([R("17")])))).Code, (await Refused(() => SaveAsync(s, Batch([R("17")])))).StatusCode));

        var next = await StartAsync();
        Assert.NotEqual(s.Id, next.Id);
        var abandoned = await Svc(x => x.AbandonAsync(next.Id, next.RowVersion, S.Actor, default));
        Assert.Equal(PerioSessionStatuses.Abandoned, abandoned.Status);
        Assert.Equal("session_closed", (await Refused(() => SaveAsync(next, Batch([R("17")])))).Code);
        Assert.Equal("session_closed", (await Refused(() => FinalizeAsync(next))).Code);
        Assert.NotEqual(next.Id, (await StartAsync()).Id);                                                    // and a fresh draft can be started again
    }

    [Fact]
    public async Task Abandoning_is_quiet_when_repeated_refused_for_a_finalized_session_and_logged_once()
    {
        var s = await SaveAsync(await StartAsync(), Batch([R()]));
        await Svc(x => x.AbandonAsync(s.Id, s.RowVersion, S.Actor, default));
        await Svc(x => x.AbandonAsync(s.Id, "stale", S.Actor, default));
        Assert.Single(await AuditAsync(nameof(PerioSession)), a => a.Type == "PerioSessionAbandoned");
        Assert.Equal(1, (await GetAsync(s.Id)).Readings.Count);                                               // the entries stay in the record

        var other = await SaveAsync(await StartAsync(), Batch([R()]));
        await FinalizeAsync(other);
        Assert.Equal("session_closed", (await Refused(() => Svc(x => x.AbandonAsync(other.Id, other.RowVersion, S.Actor, default)))).Code);
    }

    // ---------- links ----------

    [Fact]
    public async Task A_finalized_chart_can_be_linked_to_a_diagnosis_a_plan_an_encounter_and_a_history_entry_and_a_repeat_is_quiet()
    {
        var exam = await FinalizeAsync(await SaveAsync(await StartAsync(), Batch([R()])));
        PerioExamView linked = exam;
        foreach (var type in PerioLinkTypes.All) linked = await Svc(s => s.LinkAsync(exam.Id, type, "ref-1", S.Actor, default));
        linked = await Svc(s => s.LinkAsync(exam.Id, "Diagnosis", " ref-1 ", Other, default));                    // the same link again, even with spaces
        Assert.Equal(new[] { "Diagnosis", "TreatmentPlan", "Encounter", "HistoryEntry" }, linked.Links!.Select(l => l.LinkType));
        Assert.Equal("Dr. Okafor", linked.Links![0].LinkedByName);
        Assert.Equal(exam.Readings, linked.Readings);                                                                // linking never changes the chart
        Assert.Equal(4, (await AuditAsync(nameof(PerioExam))).Count(a => a.Type == "PerioExamLinked"));
    }

    [Theory]
    [InlineData("diagnosis", "x")] [InlineData("Procedure", "x")] [InlineData(null, "x")] [InlineData("Diagnosis", "")] [InlineData("Diagnosis", null)]
    public async Task A_link_with_an_unknown_type_or_no_reference_is_refused_and_a_long_reference_too(string? type, string? reference)
    {
        var exam = await FinalizeAsync(await SaveAsync(await StartAsync(), Batch([R()])));
        Assert.Equal("validation_failed", (await Refused(() => Svc(s => s.LinkAsync(exam.Id, type, reference, S.Actor, default)))).Code);
        Assert.Equal("validation_failed", (await Refused(() => Svc(s => s.LinkAsync(exam.Id, "Diagnosis", new string('x', 101), S.Actor, default)))).Code);
        Assert.Empty((await Svc(s => s.ExamAsync(exam.Id, default))).Links!);
    }

    [Fact]
    public async Task Linking_an_unknown_chart_is_a_404()
        => Assert.Equal("exam_not_found", (await Refused(() => Svc(s => s.LinkAsync(Guid.NewGuid(), "Diagnosis", "x", S.Actor, default)))).Code);
}
