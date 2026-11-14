using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Time;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (T-3, decision 02): one rule for where a practice-local day begins and ends, owned by the practice clock (<see cref="IPracticeClock.StartOfPracticeDayUtc"/>) and used by the
/// calendar, the visit board and the reports. A day is [start(d), start(d + 1)). When midnight does not exist the day begins at the first local minute that does; when midnight happens twice
/// it begins at the earlier one. Tested on America/Chicago (changes at 02:00) and on a synthetic zone whose clocks change AT midnight, so the edge cannot be dodged by the zone chosen.
/// </summary>
public class PracticeDayBoundaryTests
{
    private static readonly TimeZoneInfo Chicago = SchedulingTestSupport.Clock.PracticeTimeZone;

    /// <summary>UTC-4 standard, UTC-3 summer; summer starts at 00:00 on 14 Jan (midnight skipped, the day begins at 01:00) and ends at 01:00 on 1 Jul (00:00-00:59 happens twice).</summary>
    internal static readonly TimeZoneInfo MidnightZone = TimeZoneInfo.CreateCustomTimeZone("Test/Midnight-Change", TimeSpan.FromHours(-4), "Midnight change", "Midnight standard", "Midnight summer",
    [
        TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 1, 14),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 7, 1)),
    ]);

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Fact]
    public void An_ordinary_chicago_day_runs_from_local_midnight_to_the_next()
    {
        Assert.Equal(Utc(2030, 1, 14, 6), PracticeDayBoundary.StartUtc(Chicago, new DateOnly(2030, 1, 14)));                   // CST, UTC-6
        Assert.Equal(Utc(2030, 7, 1, 5), PracticeDayBoundary.StartUtc(Chicago, new DateOnly(2030, 7, 1)));                     // CDT, UTC-5
    }

    [Fact]
    public void Chicago_clock_change_days_are_23_and_25_hours_long()
    {
        var spring = new DateOnly(2030, 3, 10);
        var autumn = new DateOnly(2030, 11, 3);
        Assert.Equal(TimeSpan.FromHours(23), PracticeDayBoundary.StartUtc(Chicago, spring.AddDays(1)) - PracticeDayBoundary.StartUtc(Chicago, spring));
        Assert.Equal(TimeSpan.FromHours(25), PracticeDayBoundary.StartUtc(Chicago, autumn.AddDays(1)) - PracticeDayBoundary.StartUtc(Chicago, autumn));
    }

    [Fact]
    public void A_skipped_midnight_starts_the_day_at_the_first_local_minute_that_exists()
    {
        var gapDay = new DateOnly(2030, 1, 14);
        Assert.Throws<LocalTimeConversionException>(() => new PracticeClock(MidnightZone).FromPracticeLocal(gapDay.ToDateTime(TimeOnly.MinValue)));   // the old conversion refused this day
        Assert.Equal(Utc(2030, 1, 14, 4), PracticeDayBoundary.StartUtc(MidnightZone, gapDay));                                   // 01:00 at UTC-3
        Assert.Equal(Utc(2030, 1, 15, 3), PracticeDayBoundary.StartUtc(MidnightZone, gapDay.AddDays(1)));                       // next midnight at UTC-3: a 23-hour day
    }

    [Fact]
    public void A_repeated_midnight_starts_the_day_at_the_earlier_of_the_two()
    {
        var repeatDay = new DateOnly(2030, 7, 1);
        Assert.Equal(Utc(2030, 7, 1, 3), PracticeDayBoundary.StartUtc(MidnightZone, repeatDay));                                 // first 00:00, still summer time (UTC-3)
        Assert.Equal(TimeSpan.FromHours(25), PracticeDayBoundary.StartUtc(MidnightZone, repeatDay.AddDays(1)) - PracticeDayBoundary.StartUtc(MidnightZone, repeatDay));
    }

    [Fact]
    public void The_clock_member_is_the_shared_rule_for_every_clock()
    {
        IPracticeClock production = new PracticeClock(MidnightZone);
        IPracticeClock test = new TestClock(Utc(2030, 1, 1, 0));
        Assert.Equal(PracticeDayBoundary.StartUtc(MidnightZone, new DateOnly(2030, 1, 14)), production.StartOfPracticeDayUtc(new DateOnly(2030, 1, 14)));
        Assert.Equal(PracticeDayBoundary.StartUtc(Chicago, new DateOnly(2030, 3, 10)), test.StartOfPracticeDayUtc(new DateOnly(2030, 3, 10)));
    }

    [Fact]
    public async Task A_report_range_includes_its_last_day_and_ends_where_the_next_day_begins_even_on_a_skipped_midnight()
    {
        // no provider or location is asked for, so the resolver never reads the database
        var resolver = new ReportFilterResolver(null!, new PracticeClock(MidnightZone), Options.Create(new ReportOptions()));
        var oneDay = await resolver.ResolveAsync(new ReportFilterInput("2030-01-14", "2030-01-14", null, null), default);
        Assert.Equal((Utc(2030, 1, 14, 4), Utc(2030, 1, 15, 3)), (oneDay.StartUtc, oneDay.EndUtc));
        var twoDays = await resolver.ResolveAsync(new ReportFilterInput("2030-01-13", "2030-01-14", null, null), default);
        Assert.Equal((Utc(2030, 1, 13, 4), Utc(2030, 1, 15, 3)), (twoDays.StartUtc, twoDays.EndUtc));                           // 13 Jan at UTC-4, inclusive of the 14th
        Assert.Equal(2, twoDays.Days);
    }

    [Fact]
    public void Negative_control_no_api_code_converts_a_practice_midnight_itself()
    {
        // the rule lives once, in PracticeDayBoundary; a calendar, board or report that converts "day at 00:00" on its own (the old code) is caught here
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Alveara.Api", "Alveara.Api.csproj"))) dir = dir.Parent;
        var api = Path.Combine(dir!.FullName, "src", "Alveara.Api");
        var oldRule = new Regex(@"FromPracticeLocal\([^;]*TimeOnly\.MinValue|IsInvalidTime\(", RegexOptions.Compiled);
        var offenders = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.EndsWith("PracticeClock.cs"))
            .SelectMany(f => File.ReadAllLines(f).Select((t, i) => (f, i, t))).Where(x => oldRule.IsMatch(x.t))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}").ToList();
        Assert.True(offenders.Count == 0, "practice midnight converted outside the shared rule at: " + string.Join(", ", offenders));
    }
}
