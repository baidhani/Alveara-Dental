using Alveara.Api.Architecture.Periodontal;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-012-C01: choosing which two charts are compared, against real SQL Server. The arithmetic itself is proven in <see cref="PerioComparisonTests"/>.</summary>
public class PerioComparisonServiceTests : SafetyTestBase
{
    private static PerioReadingInput R(string tooth, string site, int pd, int rec = 0, bool bleed = false) => new(tooth, site, pd, rec, bleed);

    private async Task<PerioExamView> ChartAsync(Guid patient, params PerioReadingInput[] readings)
    {
        var session = await WithDb(db => new PerioSessionService(db, Clock).StartAsync(patient, S.Actor, default));
        session = await WithDb(db => new PerioSessionService(db, Clock).SaveEntriesAsync(session.Id, session.RowVersion, new PerioEntryBatch(readings, null, null, null), S.Actor, default));
        var exam = await WithDb(db => new PerioSessionService(db, Clock).FinalizeAsync(session.Id, session.RowVersion, S.Actor, default));
        await Task.Delay(20);                                                                                // distinct moments, so "the one before" is unambiguous
        return exam;
    }

    private Task<PerioComparisonView> CompareAsync(Guid patient, Guid? exam, Guid? session = null, Guid? previous = null) =>
        WithDb(db => new PerioComparisonService(db).CompareAsync(patient, exam, session, previous, default));

    private static async Task<PerioException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<PerioException>(action);

    [Fact]
    public async Task A_saved_chart_is_compared_with_the_latest_finalized_chart_before_it_by_default()
    {
        await ChartAsync(Ann, R("16", "B", 8));
        var middle = await ChartAsync(Ann, R("16", "B", 5));
        var newest = await ChartAsync(Ann, R("16", "B", 3));
        var c = await CompareAsync(Ann, newest.Id);
        var site = Assert.Single(c.Sites);
        Assert.Equal(((int?)5, (int?)3, (int?)-2, "Improved"), (site.PreviousDepthMm, site.CurrentDepthMm, site.DepthChangeMm, site.Trend));      // against the middle one, not the oldest
        var older = await CompareAsync(Ann, middle.Id);
        Assert.Equal(((int?)8, (int?)5, (int?)-3, "Improved"), (older.Sites[0].PreviousDepthMm, older.Sites[0].CurrentDepthMm, older.Sites[0].DepthChangeMm, older.Sites[0].Trend));   // the middle one against the oldest
    }

    [Fact]
    public async Task The_draft_being_entered_is_compared_with_the_latest_finalized_chart()
    {
        await ChartAsync(Ann, R("16", "B", 4));
        var latest = await ChartAsync(Ann, R("16", "B", 6, 1, true));
        var draft = await WithDb(db => new PerioSessionService(db, Clock).StartAsync(Ann, S.Actor, default));
        draft = await WithDb(db => new PerioSessionService(db, Clock).SaveEntriesAsync(draft.Id, draft.RowVersion, new PerioEntryBatch([R("16", "B", 3), R("17", "B", 4)], null, null, null), S.Actor, default));
        var c = await CompareAsync(Ann, null, draft.Id);
        Assert.Equal((1, 1, 1), (c.MatchedSites, c.Improved, c.OnlyCurrent));
        var b16 = c.Sites.Single(x => x is { ToothKey: "16", Site: "B" });
        Assert.Equal(((int?)6, (int?)3, (int?)-3), (b16.PreviousDepthMm, b16.CurrentDepthMm, b16.DepthChangeMm));          // against the latest finalized one (6), not the older (4)
        Assert.Equal(latest.RecordedAtUtc, (await WithDb(db => new PerioService(db, Clock).HistoryAsync(Ann, null, default))).Exams[0].RecordedAtUtc);
    }

    [Fact]
    public async Task A_named_earlier_chart_is_used_instead_of_the_default()
    {
        var oldest = await ChartAsync(Ann, R("16", "B", 9));
        await ChartAsync(Ann, R("16", "B", 5));
        var newest = await ChartAsync(Ann, R("16", "B", 4));
        var c = await CompareAsync(Ann, newest.Id, previous: oldest.Id);
        Assert.Equal(((int?)9, (int?)4, "Improved"), (c.Sites[0].PreviousDepthMm, c.Sites[0].CurrentDepthMm, c.Sites[0].Trend));
    }

    [Fact]
    public async Task With_no_earlier_finalized_chart_there_is_nothing_to_compare_with_and_it_says_so()
    {
        var only = await ChartAsync(Ann, R("16", "B", 4));
        var e = await Refused(() => CompareAsync(Ann, only.Id));
        Assert.Equal(("no_previous_chart", 404), (e.Code, e.StatusCode));
        var draft = await WithDb(db => new PerioSessionService(db, Clock).StartAsync(Bo, S.Actor, default));
        Assert.Equal("no_previous_chart", (await Refused(() => CompareAsync(Bo, null, draft.Id))).Code);       // a patient with no chart at all
    }

    [Fact]
    public async Task A_chart_or_draft_of_another_patient_is_never_compared()
    {
        var anns = await ChartAsync(Ann, R("16", "B", 4));
        await ChartAsync(Ann, R("16", "B", 5));
        var bos = await ChartAsync(Bo, R("16", "B", 3));
        Assert.Equal("exam_not_found", (await Refused(() => CompareAsync(Ann, bos.Id))).Code);
        Assert.Equal("exam_not_found", (await Refused(() => CompareAsync(Ann, anns.Id, previous: bos.Id))).Code);
        var draft = await WithDb(db => new PerioSessionService(db, Clock).StartAsync(Bo, S.Actor, default));
        Assert.Equal("session_not_found", (await Refused(() => CompareAsync(Ann, null, draft.Id))).Code);
    }

    [Fact]
    public async Task Exactly_one_current_chart_must_be_named_and_the_patient_must_exist()
    {
        var exam = await ChartAsync(Ann, R("16", "B", 4));
        Assert.Equal("validation_failed", (await Refused(() => CompareAsync(Ann, null))).Code);
        Assert.Equal("validation_failed", (await Refused(() => CompareAsync(Ann, exam.Id, Guid.NewGuid()))).Code);
        Assert.Equal("patient_not_found", (await Refused(() => CompareAsync(Guid.NewGuid(), exam.Id))).Code);
        Assert.Equal("exam_not_found", (await Refused(() => CompareAsync(Ann, Guid.NewGuid()))).Code);
    }
}
