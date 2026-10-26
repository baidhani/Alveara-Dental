using System.Globalization;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: how a stored instant is written in the readable record: in the PRACTICE's own time zone (never the server's), with the offset beside it so the time is unambiguous across daylight-saving changes,
/// for example <c>2026-07-15 13:00 (UTC-05:00)</c>. A date taken from an instant is the practice-local date, so a visit at 11 pm local does not land on the next day. The machine-readable files keep UTC; this is only
/// for a person reading the page.
/// </summary>
public static class PracticeTimeText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Moment(IPracticeClock clock, DateTimeOffset instant)
    {
        var local = clock.ToPracticeLocal(instant);
        return $"{local.ToString("yyyy-MM-dd HH:mm", Inv)} (UTC{local.ToString("zzz", Inv)})";
    }

    public static string Day(IPracticeClock clock, DateTimeOffset instant) => clock.ToPracticeLocal(instant).ToString("yyyy-MM-dd", Inv);
}
