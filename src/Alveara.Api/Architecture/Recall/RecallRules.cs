using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Recall;

/// <summary>
/// ALV-009-C01: the limits and defaults of recall, owned by this domain rather than by generic practice configuration. A recall interval is a whole number of days; the practice has one default and a patient may have
/// their own. A recall task is made a few days before the patient is due, and only for a patient who was seen recently enough to still be a patient of the practice.
/// </summary>
public static class RecallRules
{
    public const int DefaultIntervalDays = 180;
    public const int MinIntervalDays = 1;
    public const int MaxIntervalDays = 3650;

    /// <summary>A recall task is made this many days before the patient's next-due date (0 to <see cref="MaxLeadDays"/>).</summary>
    public const int DefaultLeadDays = 30;
    public const int MaxLeadDays = 365;

    /// <summary>A patient last seen longer ago than this many months is not recalled automatically: they have lapsed, and the front desk decides whether to reach them.</summary>
    public const int DefaultMaxVisitAgeMonths = 24;
    public const int MaxVisitAgeMonthsLimit = 120;

    /// <summary>A recall falls due at this hour of the practice's own day (never near a daylight-saving change, which happens at 02:00).</summary>
    public const int DueHourLocal = 9;

    public static bool IntervalIsValid(int days) => days is >= MinIntervalDays and <= MaxIntervalDays;
}

/// <summary>Why a patient is or is not due for a recall task now.</summary>
public enum RecallVerdict
{
    /// <summary>Due now (inside the lead time or past the due date): a recall task should exist.</summary>
    Due,
    /// <summary>Seen recently but the lead time has not started.</summary>
    NotDue,
    /// <summary>The patient is not recalled (opted out).</summary>
    OptedOut,
    /// <summary>No completed visit, so there is nothing to count from.</summary>
    NoVisit,
    /// <summary>Last seen longer ago than the limit: not recalled automatically.</summary>
    LapsedPatient,
    /// <summary>A recall task for this patient is already open.</summary>
    OpenRecall,
    /// <summary>The patient already has a future appointment.</summary>
    AlreadyBooked,
}

public sealed record RecallInput(
    DateTimeOffset? LastCompletedVisitUtc, int DefaultIntervalDays, int? PatientIntervalDays, bool OptedOut, int LeadDays, int MaxVisitAgeMonths, bool HasOpenRecallTask, bool HasFutureAppointment);

/// <param name="NextDueUtc">When the patient is next due (null when it cannot be worked out).</param>
/// <param name="IntervalDays">The interval used: the patient's own when set, otherwise the practice default.</param>
public sealed record RecallDecision(RecallVerdict Verdict, DateTimeOffset? NextDueUtc, int? IntervalDays);

/// <summary>
/// ALV-009-C01: the pure arithmetic of recall. The next-due date is the practice-local DATE of the last completed visit plus the interval in whole days, at 09:00 practice time, so a visit late in the evening counts for
/// the day the practice saw it, and a daylight-saving change or a leap day never moves it by a day. It reads nothing and writes nothing.
/// </summary>
public static class RecallCalculator
{
    public static int EffectiveInterval(int defaultIntervalDays, int? patientIntervalDays) => patientIntervalDays ?? defaultIntervalDays;

    public static DateTimeOffset NextDue(DateTimeOffset lastCompletedVisitUtc, int intervalDays, IPracticeClock clock)
    {
        if (!RecallRules.IntervalIsValid(intervalDays)) throw new ArgumentOutOfRangeException(nameof(intervalDays), intervalDays, "A recall interval is 1 to 3650 days.");
        var visitDay = clock.ToPracticeLocal(lastCompletedVisitUtc).Date;
        return clock.FromPracticeLocal(visitDay.AddDays(intervalDays).AddHours(RecallRules.DueHourLocal), LocalTimeAmbiguityPolicy.EarlierInstant);
    }

    public static RecallDecision Evaluate(RecallInput input, DateTimeOffset now, IPracticeClock clock)
    {
        if (input.LeadDays is < 0 or > RecallRules.MaxLeadDays) throw new ArgumentOutOfRangeException(nameof(input), input.LeadDays, "The lead time is 0 to 365 days.");
        if (input.MaxVisitAgeMonths is < 1 or > RecallRules.MaxVisitAgeMonthsLimit) throw new ArgumentOutOfRangeException(nameof(input), input.MaxVisitAgeMonths, "The visit age limit is 1 to 120 months.");
        var interval = EffectiveInterval(input.DefaultIntervalDays, input.PatientIntervalDays);
        if (input.OptedOut) return new RecallDecision(RecallVerdict.OptedOut, null, interval);
        if (input.LastCompletedVisitUtc is not { } last) return new RecallDecision(RecallVerdict.NoVisit, null, interval);

        var next = NextDue(last, interval, clock);
        var today = clock.ToPracticeLocal(now).Date;
        if (clock.ToPracticeLocal(last).Date < today.AddMonths(-input.MaxVisitAgeMonths)) return new RecallDecision(RecallVerdict.LapsedPatient, next, interval);
        if (input.HasOpenRecallTask) return new RecallDecision(RecallVerdict.OpenRecall, next, interval);
        if (input.HasFutureAppointment) return new RecallDecision(RecallVerdict.AlreadyBooked, next, interval);
        return new RecallDecision(now >= next.AddDays(-input.LeadDays) ? RecallVerdict.Due : RecallVerdict.NotDue, next, interval);
    }
}
