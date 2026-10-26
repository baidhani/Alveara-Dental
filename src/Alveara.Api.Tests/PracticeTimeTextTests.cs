using Xunit;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, how a stored instant is written for a person, without a database: in the practice's own time zone with the offset beside it, across both sides of a daylight-saving change, with the practice-local date
/// (not the UTC date) when the two differ, and the same wherever the server runs.
/// </summary>
public class PracticeTimeTextTests
{
    private static readonly IPracticeClock Chicago = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
    private static readonly IPracticeClock Utc = new PracticeClock(TimeZoneInfo.Utc);

    [Fact]
    public void An_instant_is_written_in_the_practice_time_zone_with_its_offset_in_winter_and_in_summer()
    {
        Assert.Equal("2026-01-15 12:00 (UTC-06:00)", PracticeTimeText.Moment(Chicago, new DateTimeOffset(2026, 1, 15, 18, 0, 0, TimeSpan.Zero)));      // standard time
        Assert.Equal("2026-07-15 13:00 (UTC-05:00)", PracticeTimeText.Moment(Chicago, new DateTimeOffset(2026, 7, 15, 18, 0, 0, TimeSpan.Zero)));      // daylight time
    }

    [Fact]
    public void Either_side_of_a_clock_change_is_unambiguous_because_the_offset_is_shown()
    {
        // 2026-11-01 07:30Z is 01:30 CDT (UTC-05:00); 08:30Z the same wall-clock hour again, 01:30 CST (UTC-06:00)
        Assert.Equal("2026-11-01 01:30 (UTC-05:00)", PracticeTimeText.Moment(Chicago, new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2026-11-01 01:30 (UTC-06:00)", PracticeTimeText.Moment(Chicago, new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void The_date_of_an_instant_is_the_practice_local_date_so_a_late_evening_visit_does_not_move_to_the_next_day()
    {
        var lateEvening = new DateTimeOffset(2026, 1, 16, 3, 30, 0, TimeSpan.Zero);                      // 21:30 on the 15th in Chicago

        Assert.Equal("2026-01-15", PracticeTimeText.Day(Chicago, lateEvening));
        Assert.Equal("2026-01-16", PracticeTimeText.Day(Utc, lateEvening));
    }

    [Fact]
    public void A_practice_in_UTC_is_written_with_a_zero_offset_and_an_offset_given_in_another_zone_changes_nothing()
    {
        var instant = new DateTimeOffset(2026, 3, 1, 9, 5, 0, TimeSpan.Zero);

        Assert.Equal("2026-03-01 09:05 (UTC+00:00)", PracticeTimeText.Moment(Utc, instant));
        Assert.Equal(PracticeTimeText.Moment(Chicago, instant), PracticeTimeText.Moment(Chicago, instant.ToOffset(TimeSpan.FromHours(9))));      // the same instant, however it arrives
    }
}
