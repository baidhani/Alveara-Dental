namespace Alveara.Api.Architecture.Reporting;

/// <summary>
/// ALV-N006 slice 5: what one measured metric MEANS. <see cref="Version"/> must change whenever the meaning changes (a different numerator, denominator, source or rule), because a baseline records the version it was taken
/// under and a comparison across versions is refused: two numbers made by different rules are not the same measurement. <see cref="Numerator"/> and <see cref="Denominator"/> say in words what is counted; the
/// denominator's count is the sample size. <see cref="SupportingRoute"/>, where one exists, is the report route that lists the records behind the numerator.
/// </summary>
public sealed record MetricDefinition(string Key, string Name, int Version, string Numerator, string Denominator, string Source, bool LowerIsBetter, string? SupportingRoute);

/// <summary>A thing the product wants to know that the application cannot observe validly, said plainly. Never given a number.</summary>
public sealed record NotMeasurable(string Key, string Name, string Reason);

public static class MetricKeys
{
    public const string SchedulingConflicts = "scheduling_conflicts";
    public const string BillingCorrections = "billing_corrections";
    public const string DuplicateMerges = "duplicate_merges";
    public const string UnfinalizedNotes = "unfinalized_notes";
    public const string LateFollowUps = "late_follow_ups";
    public const string CancellationRate = "cancellation_rate";
    public const string NoShowRate = "no_show_rate";
}

public static class MetricDefinitions
{
    /// <summary>The refusal codes that mean two bookings or rooms collided (as opposed to a request that was simply invalid).</summary>
    public static readonly IReadOnlyList<string> ConflictCodes = ["provider_double_booked", "patient_double_booked", "operatory_conflict", "operatory_occupied"];

    /// <summary>A note is counted as not finalized only once it is at least this many days old: a note from this morning is allowed to be a draft.</summary>
    public const int NoteGraceDays = 7;

    public static readonly IReadOnlyList<MetricDefinition> All =
    [
        new(MetricKeys.SchedulingConflicts, "Scheduling conflicts", 1,
            "booking requests refused because the provider, the patient or the room was already taken", "all booking requests that were either booked or refused",
            "privacy-safe measurement events appointment.rejected and appointment.scheduled", true, null),
        new(MetricKeys.BillingCorrections, "Billing corrections", 1,
            "ledger entries that reverse an earlier entry", "ledger entries recorded that are not a reversal (charges, payments, refunds and adjustments; loaded opening balances are left out)",
            "the billing ledger", true, null),
        new(MetricKeys.DuplicateMerges, "Duplicate patient merges", 1,
            "patient merges completed", "patients registered",
            "the patient merge history and patient registrations", true, null),
        new(MetricKeys.UnfinalizedNotes, "Clinical notes not finalized", 1,
            $"clinical notes still not finalized when the metric is computed", $"clinical notes in the period that are at least {NoteGraceDays} days old when the metric is computed",
            "clinical encounters", true, null),
        new(MetricKeys.LateFollowUps, "Follow-ups not done on time", 1,
            "follow-up tasks that are still open or were completed after their due time", "follow-up tasks due in the period whose due time has passed when the metric is computed",
            "follow-up tasks", true, "follow-ups/tasks"),
        new(MetricKeys.CancellationRate, "Cancellations", 1,
            "appointments cancelled", "appointments that start in the period (any status)",
            "appointments", true, "cancellations/appointments?measure=Cancelled"),
        new(MetricKeys.NoShowRate, "No-shows", 1,
            "appointments marked no-show", "appointments that start in the period (any status)",
            "appointments", true, "cancellations/appointments?measure=NoShow"),
    ];

    public static MetricDefinition Of(string key) => All.Single(m => m.Key == key);

    public static readonly IReadOnlyList<NotMeasurable> Unmeasurable =
    [
        new("duplicate_entry_time", "Time spent entering the same information twice", "The application cannot tell that two entries were the same information or how long either took."),
        new("administrative_time", "Time staff spend on nonclinical administrative work", "The application does not record how long a task takes or who was waiting while it was done."),
        new("nonclinical_overhead_reduction", "Reduction in nonclinical overhead", "This is a product target (a 20 to 30 percent reduction), not a measurement. It becomes a result only if a valid baseline and a valid comparison of a measurable time metric exist, and none does yet."),
    ];

    /// <summary>The sentence every comparison carries: a change between two periods is not shown to be caused by anything.</summary>
    public const string NoCausationNote = "A difference between two periods shows that the numbers differ. It does not show what caused the difference.";
}
