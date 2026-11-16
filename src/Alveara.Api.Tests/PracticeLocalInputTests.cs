using System.Globalization;
using Alveara.Api.Architecture.Time;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A3 (briefing section 5, BR-2): the one practice-local input parser, on its own. Exact invariant ASCII patterns only; a wall-clock value of
/// unspecified kind; converted through the practice clock (America/Chicago); stable codes and field names; the same result on every machine culture.
/// </summary>
[Collection(CultureSensitiveTests.Name)]   // some cases change the thread culture
public class PracticeLocalInputTests
{
    private static readonly IPracticeClock Chicago = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));

    [Theory]
    [InlineData("2030-01-14T09:30", false, 2030, 1, 14, 9, 30, 0)]
    [InlineData("2030-01-14T09:30", true, 2030, 1, 14, 9, 30, 0)]
    [InlineData("2030-01-14T09:30:45", true, 2030, 1, 14, 9, 30, 45)]
    [InlineData("2030-12-31T23:59", false, 2030, 12, 31, 23, 59, 0)]
    public void The_documented_patterns_give_an_unspecified_wall_clock_value(string text, bool seconds, int y, int mo, int d, int h, int mi, int s)
    {
        Assert.True(PracticeLocalInput.TryParse(text, seconds, out var wall));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), wall);
        Assert.Equal(DateTimeKind.Unspecified, wall.Kind);
    }

    [Theory]
    [InlineData("2030-01-14T09:30Z", true)]
    [InlineData("2030-01-14T09:30:00Z", true)]
    [InlineData("2030-01-14T09:30+01:00", true)]
    [InlineData("2030-01-14T09:30:00-06:00", true)]
    [InlineData(" 2030-01-14T09:30", true)]
    [InlineData("2030-01-14T09:30 ", true)]
    [InlineData("2030-01-14 09:30", true)]
    [InlineData("2030-01-14T09:30:00.5", true)]
    [InlineData("2030-1-4T9:5", true)]
    [InlineData("14/01/2030 09:30", true)]
    [InlineData("2030-01-14", true)]
    [InlineData("2030-01-14T09.30", true)]
    [InlineData("٢٠٣٠-٠١-١٤T٠٩:٣٠", true)]   // Arabic-Indic digits
    [InlineData("๒๐๓๐-๐๑-๑๔T๐๙:๓๐", true)]   // Thai digits
    [InlineData("2030-01-14T09:30:00", false)]   // seconds on a minutes-only field
    [InlineData("", true)]
    [InlineData("2030-02-30T09:30", true)]   // no such day
    [InlineData("2030-01-14T24:00", true)]
    public void Every_other_text_is_refused(string text, bool seconds) => Assert.False(PracticeLocalInput.TryParse(text, seconds, out _));

    private static LocalTimeInputException Refused(string? local, DateTimeOffset? utc = null, bool seconds = false) =>
        Assert.Throws<LocalTimeInputException>(() => PracticeLocalInput.Resolve(local, utc, "dueAtLocal", "dueAtUtc", seconds, Chicago));

    [Fact]
    public void A_local_value_is_converted_through_the_practice_clock_on_normal_and_daylight_days()
    {
        Assert.Equal(new DateTimeOffset(2030, 1, 14, 15, 30, 0, TimeSpan.Zero), PracticeLocalInput.Resolve("2030-01-14T09:30", null, "f", "g", false, Chicago));   // CST -6
        Assert.Equal(new DateTimeOffset(2030, 7, 14, 14, 30, 0, TimeSpan.Zero), PracticeLocalInput.Resolve("2030-07-14T09:30", null, "f", "g", false, Chicago));   // CDT -5
        Assert.Equal(new DateTimeOffset(2030, 1, 14, 15, 30, 45, TimeSpan.Zero), PracticeLocalInput.Resolve("2030-01-14T09:30:45", null, "f", "g", true, Chicago));
    }

    [Fact]
    public void Each_refusal_has_its_stable_code_and_names_its_field()
    {
        var bad = Refused("2030-01-14T09:30Z");
        Assert.Equal(("invalid_local_time", 400), (bad.Code, bad.StatusCode));
        Assert.Equal(["dueAtLocal"], bad.FieldErrors.Keys);

        var gap = Refused("2030-03-10T02:30");   // spring forward: 02:00-03:00 does not exist in Chicago
        Assert.Equal("local_time_does_not_exist", gap.Code);
        Assert.Equal(["dueAtLocal"], gap.FieldErrors.Keys);

        var overlap = Refused("2030-11-03T01:30");   // fall back: 01:00-02:00 happens twice
        Assert.Equal("local_time_ambiguous", overlap.Code);
        Assert.Equal(["dueAtLocal"], overlap.FieldErrors.Keys);

        var both = Refused("2030-01-14T09:30", DateTimeOffset.UtcNow);
        Assert.Equal("ambiguous_time", both.Code);
        Assert.Equal(["dueAtLocal", "dueAtUtc"], both.FieldErrors.Keys.Order());
    }

    [Fact]
    public void The_legacy_utc_form_alone_is_still_accepted_and_neither_is_not_an_error_here()
    {
        var utc = new DateTimeOffset(2030, 1, 14, 15, 30, 0, TimeSpan.Zero);
        Assert.Equal(utc, PracticeLocalInput.Resolve(null, utc, "f", "g", false, Chicago));
        Assert.Null(PracticeLocalInput.Resolve(null, null, "f", "g", false, Chicago));   // each route keeps its own rule for a missing time
    }

    private static CultureInfo DottedTime()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        c.DateTimeFormat.TimeSeparator = ".";
        return c;
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("dotted-time")]
    [InlineData("ar-SA")]
    public void The_same_text_gives_the_same_result_on_every_machine_culture(string culture)
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture == "dotted-time" ? DottedTime() : CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.Equal(new DateTimeOffset(2030, 1, 14, 15, 30, 0, TimeSpan.Zero), PracticeLocalInput.Resolve("2030-01-14T09:30", null, "f", "g", false, Chicago));
            Assert.False(PracticeLocalInput.TryParse("2030-01-14T09.30", false, out _));
        }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved; }
    }
}
