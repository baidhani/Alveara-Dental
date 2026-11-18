using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// ALV-N023 (owner decision DR-8): a completion's performed date may be after its encounter's date (attribution to a Draft or Finalized encounter is allowed). It is never refused or altered;
/// the screens show how many practice days later it was, so an accidental mismatch is noticed.
/// </summary>
public static class CompletionDates
{
    /// <summary>Whole practice days from the encounter's date to the performed date; null when the performed date is not after it (or the encounter is unknown).</summary>
    public static int? DaysAfter(IPracticeClock clock, DateTimeOffset performedUtc, DateTimeOffset encounterUtc)
    {
        if (encounterUtc == default) return null;
        var days = DateOnly.FromDateTime(clock.ToPracticeLocal(performedUtc).DateTime).DayNumber - DateOnly.FromDateTime(clock.ToPracticeLocal(encounterUtc).DateTime).DayNumber;
        return days > 0 ? days : null;
    }
}
