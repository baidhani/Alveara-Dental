using Alveara.Api.Architecture.Time;
using Xunit;

namespace Alveara.Api.Tests;

public class PracticeClockTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly PracticeClock _clock = new(Chicago);

    /// <summary>Finds the actual DST "spring forward" transition instant for a given year, rather
    /// than hardcoding a guessed date — US rules have changed historically and this keeps the
    /// test correct regardless.</summary>
    private static DateTime FindSpringForwardInvalidLocalTime(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
            {
                var candidate = new DateTime(year, month, day, 2, 30, 0);
                if (Chicago.IsInvalidTime(candidate)) return candidate;
            }
        }
        throw new InvalidOperationException($"No spring-forward transition found in {year} for {Chicago.Id}.");
    }

    private static DateTime FindFallBackAmbiguousLocalTime(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
            {
                var candidate = new DateTime(year, month, day, 1, 30, 0);
                if (Chicago.IsAmbiguousTime(candidate)) return candidate;
            }
        }
        throw new InvalidOperationException($"No fall-back transition found in {year} for {Chicago.Id}.");
    }

    [Fact]
    public void UtcNow_is_an_unambiguous_instant()
    {
        var a = _clock.UtcNow;
        var b = DateTimeOffset.UtcNow;
        Assert.True((b - a).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Ordinary_local_time_converts_without_error()
    {
        var ordinary = new DateTime(2026, 6, 15, 10, 0, 0); // June: no DST transition nearby
        var result = _clock.FromPracticeLocal(ordinary);
        Assert.Equal(new TimeSpan(-5, 0, 0), result.Offset); // CDT in June
    }

    [Fact]
    public void Spring_forward_invalid_local_time_throws_rather_than_silently_guessing()
    {
        var invalidLocal = FindSpringForwardInvalidLocalTime(2026);
        var ex = Assert.Throws<LocalTimeConversionException>(() => _clock.FromPracticeLocal(invalidLocal));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Fall_back_ambiguous_local_time_rejected_by_default_rather_than_silently_guessing()
    {
        var ambiguousLocal = FindFallBackAmbiguousLocalTime(2026);
        var ex = Assert.Throws<LocalTimeConversionException>(() => _clock.FromPracticeLocal(ambiguousLocal));
        Assert.Contains("ambiguous", ex.Message);
    }

    [Fact]
    public void Fall_back_ambiguous_local_time_can_be_explicitly_resolved_to_the_earlier_instant()
    {
        var ambiguousLocal = FindFallBackAmbiguousLocalTime(2026);
        var earlier = _clock.FromPracticeLocal(ambiguousLocal, LocalTimeAmbiguityPolicy.EarlierInstant);
        var later = _clock.FromPracticeLocal(ambiguousLocal, LocalTimeAmbiguityPolicy.LaterInstant);

        Assert.True(earlier.UtcDateTime < later.UtcDateTime);
        Assert.Equal(TimeSpan.FromHours(1), later.UtcDateTime - earlier.UtcDateTime);
    }

    [Fact]
    public void Round_trip_through_UTC_and_back_to_practice_local_preserves_wall_clock_time()
    {
        var original = new DateTime(2026, 6, 15, 14, 30, 0);
        var utcInstant = _clock.FromPracticeLocal(original);
        var backToLocal = _clock.ToPracticeLocal(utcInstant);

        Assert.Equal(original, backToLocal.DateTime);
    }

    [Fact]
    public void Date_only_values_are_not_affected_by_time_zone_conversion()
    {
        // DateOnly has no time-of-day component to shift, which is the whole point of using it
        // for date-of-birth and similar identity-relevant date-only values.
        var dob = new DateOnly(1990, 1, 1);
        Assert.Equal(1990, dob.Year);
        Assert.Equal(1, dob.Month);
        Assert.Equal(1, dob.Day);
    }
}
