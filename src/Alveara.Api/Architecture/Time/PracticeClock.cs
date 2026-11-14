using System.Globalization;
namespace Alveara.Api.Architecture.Time;

/// <summary>
/// The practice's time model, per ALV-N002:
/// - <see cref="UtcNow"/> is the unambiguous instant used for audit/system timestamps.
/// - Practice-local appointment times are always converted through <see cref="PracticeTimeZone"/>
///   rather than the server's own local time zone, so a Windows server in a different time
///   zone than the practice never silently shifts appointment times.
/// - Date-only values (date of birth, etc.) must use <see cref="DateOnly"/>, never
///   <see cref="DateTime"/>, so they can never be accidentally time-zone-shifted.
/// </summary>
public interface IPracticeClock
{
    TimeZoneInfo PracticeTimeZone { get; }
    DateTimeOffset UtcNow { get; }

    /// <summary>Converts a UTC instant to the practice's local wall-clock time for display.</summary>
    DateTimeOffset ToPracticeLocal(DateTimeOffset utcInstant);

    /// <summary>
    /// Converts a practice-local wall-clock time (e.g. an appointment slot the front desk typed
    /// in) to an unambiguous UTC instant for storage.
    /// </summary>
    /// <exception cref="LocalTimeConversionException">
    /// Thrown when <paramref name="localTime"/> falls in a DST "spring-forward" gap (does not
    /// exist) or "fall-back" overlap (ambiguous) — this story requires explicit handling, not a
    /// silent guess.
    /// </exception>
    DateTimeOffset FromPracticeLocal(DateTime localTime, LocalTimeAmbiguityPolicy ambiguityPolicy = LocalTimeAmbiguityPolicy.Reject);

    /// <summary>
    /// ALV-N021 A2 (T-3, decision 02): the UTC instant at which the practice-local calendar day <paramref name="date"/> begins. THE one rule for a practice
    /// day's boundaries: a day is [StartOfPracticeDayUtc(d), StartOfPracticeDayUtc(d + 1)), half-open. Unlike <see cref="FromPracticeLocal"/> it never refuses:
    /// when midnight does not exist (a clock change at midnight skips it) the day begins at the first local instant that does exist, and when midnight
    /// happens twice (a fall-back at midnight) the day begins at the earlier of the two. Every caller (calendar, visit board, reports and their drill-downs)
    /// uses this; none may convert a practice midnight itself. It is a default member so every clock (production, chain host, tests) shares the same rule.
    /// </summary>
    DateTimeOffset StartOfPracticeDayUtc(DateOnly date) => PracticeDayBoundary.StartUtc(PracticeTimeZone, date);
}

/// <summary>ALV-N021 A2 (T-3): the arithmetic behind <see cref="IPracticeClock.StartOfPracticeDayUtc"/>, kept pure so it is tested on any zone.</summary>
public static class PracticeDayBoundary
{
    public static DateTimeOffset StartUtc(TimeZoneInfo zone, DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var minute = 0;
        while (zone.IsInvalidTime(local) && minute < 24 * 60) { local = local.AddMinutes(1); minute++; }   // midnight skipped: the first minute that exists
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);   // repeated: the larger offset is the earlier instant
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}

/// <summary>
/// How to resolve an ambiguous local time (DST "fall back" — the same wall-clock time occurs
/// twice). There is no ambiguity-resolution policy for an invalid time (DST "spring forward" —
/// the wall-clock time never occurs) because there's no valid instant to pick; that always throws.
/// </summary>
public enum LocalTimeAmbiguityPolicy
{
    /// <summary>Throw — the caller (e.g. the scheduler UI) must ask the user to disambiguate.</summary>
    Reject,

    /// <summary>Resolve to the earlier of the two possible instants (before the clocks fall back).</summary>
    EarlierInstant,

    /// <summary>Resolve to the later of the two possible instants (after the clocks fall back).</summary>
    LaterInstant,
}

public sealed class LocalTimeConversionException(string message) : Exception(message);

public sealed class PracticeClock : IPracticeClock
{
    public PracticeClock(TimeZoneInfo practiceTimeZone)
    {
        PracticeTimeZone = practiceTimeZone;
    }

    public TimeZoneInfo PracticeTimeZone { get; }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset ToPracticeLocal(DateTimeOffset utcInstant)
    {
        var localTime = TimeZoneInfo.ConvertTime(utcInstant, PracticeTimeZone);
        return localTime;
    }

    public DateTimeOffset FromPracticeLocal(DateTime localTime, LocalTimeAmbiguityPolicy ambiguityPolicy = LocalTimeAmbiguityPolicy.Reject)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);

        if (PracticeTimeZone.IsInvalidTime(unspecified))
        {
            throw new LocalTimeConversionException(
                string.Create(CultureInfo.InvariantCulture, $"{localTime:yyyy-MM-dd HH:mm} does not exist in {PracticeTimeZone.Id} (DST spring-forward gap). ") +
                "Ask the user to pick a valid time.");
        }

        if (PracticeTimeZone.IsAmbiguousTime(unspecified))
        {
            var offsets = PracticeTimeZone.GetAmbiguousTimeOffsets(unspecified);
            var chosenOffset = ambiguityPolicy switch
            {
                LocalTimeAmbiguityPolicy.EarlierInstant => offsets.Max(), // larger UTC offset = earlier UTC instant
                LocalTimeAmbiguityPolicy.LaterInstant => offsets.Min(),
                _ => throw new LocalTimeConversionException(
                    string.Create(CultureInfo.InvariantCulture, $"{localTime:yyyy-MM-dd HH:mm} is ambiguous in {PracticeTimeZone.Id} (DST fall-back overlap: ") +
                    "occurs twice). Ask the user to disambiguate rather than guessing."),
            };
            return new DateTimeOffset(unspecified, chosenOffset);
        }

        var offset = PracticeTimeZone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }
}
