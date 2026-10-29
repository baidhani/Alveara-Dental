namespace Alveara.Api.Architecture.Reporting;

public static class BaselineLimits
{
    public const int NameMax = 100;
    public const int KeyMax = 64;
    public const int MetricKeyMax = 40;
}

/// <summary>
/// ALV-N006 slice 5b: a baseline the practice chose to compare against later: a COMPLETED period (it ends before today in practice time) whose metrics were measured and STORED when it was set, so a later correction to the
/// data cannot silently change what the baseline said. It is recorded once and never changed or deleted (database triggers). <see cref="AsOfUtc"/> is the moment the moment-dependent metrics were judged at. The key makes a
/// repeated request return the same baseline instead of making a second. The name is the practice's own label and is never copied into the audit log.
/// </summary>
public class ReportBaseline
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public required string TimeZoneId { get; set; }
    public DateTimeOffset AsOfUtc { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid SetByUserId { get; set; }
    public DateTimeOffset SetAtUtc { get; set; }
    public List<ReportBaselineMetric> Metrics { get; set; } = [];
}

/// <summary>
/// One metric as measured for a baseline period: the definition version it was measured under, the numerator and the denominator (the sample size) and the rate (null exactly when the denominator is zero). Never changed or
/// deleted (database triggers); one row for each metric of a baseline.
/// </summary>
public class ReportBaselineMetric
{
    public Guid Id { get; set; }
    public Guid BaselineId { get; set; }
    public required string MetricKey { get; set; }
    public int DefinitionVersion { get; set; }
    public int NumeratorCount { get; set; }
    public int DenominatorCount { get; set; }
    public decimal? Rate { get; set; }
}
