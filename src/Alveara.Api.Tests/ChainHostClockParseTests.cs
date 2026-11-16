using System.Globalization;
using Alveara.Api.ChainHost;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2-R02 (review CR-1, decision 02 T-2): the chain host's documented ISO 8601 <c>CHAIN_CLOCK_UTC</c> is read the same on every machine culture - a Thai (Buddhist
/// calendar) machine, a machine whose time separator is "." and the invariant culture all accept the same text as the same UTC instant, and refuse the same bad text.
/// The source guard that keeps this parse on the invariant culture is in <see cref="TimeCultureGuardTests"/>.
/// </summary>
[Collection(CultureSensitiveTests.Name)]   // changes the thread culture: never beside another such test
public class ChainHostClockParseTests
{
    private static CultureInfo Culture(string name) => name switch
    {
        "th-TH" => CultureInfo.GetCultureInfo("th-TH"),
        "dotted-time" => DottedTime(),
        _ => CultureInfo.InvariantCulture,
    };

    private static CultureInfo DottedTime()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        c.DateTimeFormat.TimeSeparator = ".";
        return c;
    }

    private static (bool Ok, DateTimeOffset Utc) ParseUnder(string culture, string? value)
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture(culture);
        try { return (ChainHostMain.TryParseClockUtc(value, out var utc), utc); }
        finally { (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved; }
    }

    public static TheoryData<string> Cultures => new() { "th-TH", "dotted-time", "invariant" };

    private static readonly DateTimeOffset Expected = new(2030, 1, 14, 13, 30, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_same_iso_instant_is_accepted_as_the_same_utc_value_on_every_machine_culture(string culture)
    {
        foreach (var text in new[] { "2030-01-14T13:30:00Z", "2030-01-14T07:30:00-06:00", "2030-01-14T13:30:00" })   // Z, an offset, and no offset (read as UTC)
        {
            var (ok, utc) = ParseUnder(culture, text);
            Assert.True(ok, $"{text} under {culture}");
            Assert.Equal(Expected, utc);
            Assert.Equal(TimeSpan.Zero, utc.Offset);
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_documented_default_is_accepted_on_every_machine_culture(string culture)
    {
        var (ok, utc) = ParseUnder(culture, ChainHostMain.DefaultClockUtc);
        Assert.True(ok);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero), utc);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Text_that_is_not_an_instant_is_refused_on_every_machine_culture(string culture)
    {
        foreach (var text in new[] { "not a time", "", null, "2030-13-40T25:61:00Z" })
            Assert.False(ParseUnder(culture, text).Ok, $"'{text}' under {culture}");
    }
}
