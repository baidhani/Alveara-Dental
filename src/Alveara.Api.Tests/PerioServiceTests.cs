using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Periodontal;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-012 behaviour, against real SQL Server: a chart records probing depth, recession and bleeding per site; incorrect data is refused with every problem named and nothing saved;
/// every chart is logged with user and timestamp in the same save (and a failed audit write or a failed save stores nothing); a retry with the same key is quiet; simultaneous saves with
/// one key end with one chart; and a patient's history reads newest first.
/// </summary>
public class PerioServiceTests : SafetyTestBase
{
    private static PerioReadingInput R(string tooth = "16", string site = "B", int pd = 3, int rec = 1, bool bleed = true) => new(tooth, site, pd, rec, bleed);

    private Task<PerioExamView> RecordAsync(string? key = null, IReadOnlyList<PerioReadingInput>? readings = null, Guid? patient = null, Guid? actor = null) =>
        WithDb(db => new PerioService(db, Clock).RecordAsync(patient ?? Ann, key ?? Guid.NewGuid().ToString("N"), readings ?? [R()], actor ?? S.Actor, default));

    private Task<PerioHistoryView> HistoryAsync(Guid? patient = null, int? take = null) => WithDb(db => new PerioService(db, Clock).HistoryAsync(patient ?? Ann, take, default));

    private static async Task<PerioException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<PerioException>(action);

    private async Task<(int Exams, int Readings, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.PerioExams.CountAsync(), await db.PerioReadings.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(PerioExam)));
    }

    // ---------- acceptance 1: charting records probing depth, recession and bleeding ----------

    [Fact]
    public async Task A_chart_records_depth_recession_and_bleeding_for_every_site_with_who_recorded_it()
    {
        var sites = PerioRules.Sites.Select((s, i) => R("36", s, pd: 2 + i, rec: i % 3, bleed: i % 2 == 0)).ToList();
        var exam = await RecordAsync(readings: sites);
        Assert.Equal(("Dr. Okafor", 6), (exam.RecordedByName, exam.ReadingCount));
        Assert.NotEqual(default, exam.RecordedAtUtc);
        Assert.Equal(sites.Select(s => (s.ToothKey, s.Site, s.ProbingDepthMm, s.RecessionMm, s.Bleeding)),
            exam.Readings.Select(r => (r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.Bleeding)));            // kept in site order, nothing altered
        Assert.Equal(sites.Select(s => s.ProbingDepthMm + s.RecessionMm), exam.Readings.Select(r => r.AttachmentLossMm));
        var stored = await WithDb(db => db.PerioReadings.AsNoTracking().OrderBy(r => r.Site).ToListAsync());
        Assert.Equal(6, stored.Count);
        Assert.All(stored, r => Assert.Equal(exam.Id, r.ExamId));
    }

    [Fact]
    public async Task A_full_mouth_of_192_sites_saves_in_one_chart()
    {
        var full = ToothKeysPermanent().SelectMany(t => PerioRules.Sites.Select(s => R(t, s, 3, 0, false))).ToList();
        var exam = await RecordAsync(readings: full);
        Assert.Equal(192, exam.Readings.Count);
        Assert.Equal((1, 192, 1), await CountsAsync());
    }

    private static IEnumerable<string> ToothKeysPermanent() => Alveara.Api.Architecture.Odontogram.ToothKeys.Permanent;

    // ---------- acceptance 2: incorrect data is rejected with a prompt to correct it ----------

    [Fact]
    public async Task Incorrect_data_is_refused_naming_every_problem_and_nothing_at_all_is_saved()
    {
        var e = await Refused(() => RecordAsync(readings: [R("16", "B", 99, -1), R("19", "B"), R("17", "Q"), R("55", "B"), R("26", "B", 3, 1)]));       // the last one is fine, and is not saved either
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Equal(["out_of_range", "out_of_range", "unknown_tooth", "unknown_site", "primary_tooth"], e.Problems.Select(p => p.Code).ToArray());
        Assert.Contains(e.Problems, p => p.ToothKey == "16" && p.Site == "B" && p.Field == "probingDepthMm" && p.Message.Contains("0 to 15 mm"));
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task A_missing_save_key_is_refused_and_a_65_character_key_too(string? key)
    {
        Assert.Equal("idempotencyKey", Assert.Single((await Refused(() => WithDb(db => new PerioService(db, Clock).RecordAsync(Ann, key, [R()], S.Actor, default)))).Problems).Field);
        Assert.Equal("idempotencyKey", Assert.Single((await Refused(() => RecordAsync(key: new string('k', 65)))).Problems).Field);
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task An_empty_chart_is_refused_and_so_is_one_for_an_unknown_patient()
    {
        Assert.Equal("required", Assert.Single((await Refused(() => RecordAsync(readings: []))).Problems).Code);
        var e = await Refused(() => RecordAsync(patient: Guid.NewGuid()));
        Assert.Equal(("patient_not_found", 404), (e.Code, e.StatusCode));
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task The_key_edges_are_accepted_a_one_character_key_and_a_64_character_key()
    {
        await RecordAsync(key: "k");
        await RecordAsync(key: new string('k', 64));
        Assert.Equal(2, (await CountsAsync()).Exams);
    }

    // ---------- acceptance 3 (trust): every chart is logged with user and timestamp ----------

    [Fact]
    public async Task Every_chart_is_logged_with_the_user_and_a_timestamp_and_the_log_never_holds_a_tooth_or_a_measurement()
    {
        await RecordAsync(readings: [R("47", "DL", 11, 4, true)], actor: S.Actor);
        await RecordAsync(readings: [R("47", "DL", 6, 2, false)], actor: Other);
        var audit = await AuditAsync(nameof(PerioExam));
        Assert.Equal(new[] { "PerioExamRecorded", "PerioExamRecorded" }, audit.Select(a => a.Type));
        Assert.Equal(new Guid?[] { S.Actor, Other }, audit.Select(a => a.By));
        Assert.All(audit, a => { Assert.DoesNotContain("47", a.Details); Assert.DoesNotContain("11", a.Details); Assert.DoesNotContain("DL", a.Details); });
        await using var db = Fixture.CreateContext();
        Assert.All(await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(PerioExam)).ToListAsync(), a => Assert.NotEqual(default, a.TimestampUtc));
    }

    // ---------- failure paths: the log write fails; the save fails ----------

    [Fact]
    public async Task The_chart_its_readings_and_its_log_entry_are_saved_together_or_not_at_all()
    {
        await using (var db = Fixture.CreateContext())
        {
            // an audit write that fails: nothing about the chart may be left behind
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefusePerioAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'PerioExam') THROW 59000, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        }
        var e = await Refused(() => RecordAsync(key: "visit-1"));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal((0, 0, 0), await CountsAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefusePerioAudit]");
        Assert.Equal(1, (await RecordAsync(key: "visit-1")).ReadingCount);      // the same retry now works, and creates exactly one chart
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_failed_save_says_nothing_was_recorded_and_leaves_no_half_chart()
    {
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefusePerioReading] ON [PerioReadings] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE ToothKey = '18') THROW 59001, 'storage unavailable', 1;
END");
        var e = await Refused(() => RecordAsync(readings: [R("16", "B"), R("18", "B")]));      // the first reading is fine; the second is refused after it was staged
        Assert.Equal("save_failed", e.Code);
        Assert.DoesNotContain("storage", e.Message);                                          // no internals leak to the person
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    // ---------- idempotency ----------

    [Fact]
    public async Task The_same_key_with_the_same_readings_returns_the_chart_already_saved_with_no_second_chart_or_log_entry()
    {
        var first = await RecordAsync(key: "visit-9", readings: [R("16", "B"), R("16", "MB", 4, 0, false)]);
        var again = await RecordAsync(key: "visit-9", readings: [R("16", "MB", 4, 0, false), R("16", "B")]);     // same chart, sent in another order
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.RecordedAtUtc, again.RecordedAtUtc);
        Assert.Equal((1, 2, 1), await CountsAsync());
    }

    [Fact]
    public async Task The_same_key_with_different_readings_is_refused_rather_than_ignored()
    {
        await RecordAsync(key: "visit-9", readings: [R("16", "B", 3)]);
        var e = await Refused(() => RecordAsync(key: "visit-9", readings: [R("16", "B", 7)]));
        Assert.Equal(("idempotency_key_reused", 409), (e.Code, e.StatusCode));
        Assert.Equal(3, (await HistoryAsync()).Exams.Single().Readings.Single().ProbingDepthMm);       // the saved chart is untouched
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task A_key_belongs_to_one_patient_so_another_patient_can_use_the_same_text()
    {
        await RecordAsync(key: "visit-1");
        await RecordAsync(key: "visit-1", patient: Bo);
        Assert.Equal(2, (await CountsAsync()).Exams);
    }

    [Fact]
    public async Task Eight_simultaneous_saves_with_one_key_create_one_chart_and_all_get_it()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => RecordAsync(key: "double-click", readings: [R("16", "B"), R("17", "B")]))));
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal((1, 2, 1), await CountsAsync());
    }

    [Fact]
    public async Task Simultaneous_saves_with_different_keys_each_make_their_own_chart()
    {
        await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() => RecordAsync(key: $"k{i}"))));
        Assert.Equal((6, 6, 6), await CountsAsync());
    }

    // ---------- reading it back ----------

    [Fact]
    public async Task A_correction_is_a_new_chart_and_the_history_lists_newest_first_keeping_the_earlier_one()
    {
        var first = await RecordAsync(readings: [R("16", "B", 8, 2, true)]);
        await Task.Delay(20);
        var second = await RecordAsync(readings: [R("16", "B", 4, 2, false)], actor: Other);
        var history = await HistoryAsync();
        Assert.Equal(new[] { second.Id, first.Id }, history.Exams.Select(e => e.Id));
        Assert.Equal(("Hana Hygienist", 4), (history.Exams[0].RecordedByName, history.Exams[0].Readings.Single().ProbingDepthMm));
        Assert.Equal(("Dr. Okafor", 8, true), (history.Exams[1].RecordedByName, history.Exams[1].Readings.Single().ProbingDepthMm, history.Exams[1].Readings.Single().Bleeding));
    }

    [Fact]
    public async Task The_history_is_empty_for_a_patient_never_charted_is_per_patient_and_unknown_patients_are_404()
    {
        Assert.Empty((await HistoryAsync()).Exams);
        await RecordAsync();
        Assert.Empty((await HistoryAsync(Bo)).Exams);
        Assert.Equal("patient_not_found", (await Refused(() => HistoryAsync(Guid.NewGuid()))).Code);
    }

    [Fact]
    public async Task The_history_returns_the_latest_20_unless_asked_and_never_more_than_100_or_fewer_than_1()
    {
        foreach (var i in Enumerable.Range(0, 22)) await RecordAsync(key: $"v{i}");
        Assert.Equal(20, (await HistoryAsync()).Exams.Count);
        Assert.Equal(3, (await HistoryAsync(take: 3)).Exams.Count);
        Assert.Single((await HistoryAsync(take: 0)).Exams);
        Assert.Equal(22, (await HistoryAsync(take: 5000)).Exams.Count);
    }
}
