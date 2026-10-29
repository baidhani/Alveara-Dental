using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Data;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>Why a metric could or could not be compared with the baseline. Anything but <see cref="Comparable"/> shows no change at all.</summary>
public static class ComparisonStatus
{
    public const string Comparable = "comparable";
    public const string NoBaseline = "no_baseline";
    public const string BaselineNotBefore = "baseline_not_before";
    public const string DefinitionChanged = "definition_changed";
    public const string BaselineSampleTooSmall = "baseline_sample_too_small";
    public const string CurrentSampleTooSmall = "current_sample_too_small";

    public static string Message(string status) => status switch
    {
        Comparable => "The two periods can be compared.",
        NoBaseline => "There is no baseline for this metric, so there is nothing to compare with.",
        BaselineNotBefore => "The baseline period does not end before this period starts, so the two overlap or are the wrong way round.",
        DefinitionChanged => "The metric is defined differently now than when the baseline was taken, so the two numbers do not measure the same thing.",
        BaselineSampleTooSmall => "The baseline has too few records to compare with.",
        CurrentSampleTooSmall => "This period has too few records to compare.",
        _ => status,
    };
}

/// <summary>One side of a comparison: the definition version it was measured under, the numerator, the denominator (the sample size) and the rate.</summary>
public sealed record MetricSide(int DefinitionVersion, int NumeratorCount, int DenominatorCount, decimal? Rate);

/// <summary>
/// One metric compared. <see cref="AbsoluteChange"/> (the current rate minus the baseline rate), <see cref="RelativeChange"/> (that, as a fraction of the baseline rate; absent when the baseline rate is zero) and
/// <see cref="Movement"/> (Lower, Higher or Same) are present ONLY when <see cref="Status"/> is <c>comparable</c>; otherwise they are all null and <see cref="Message"/> says why.
/// </summary>
public sealed record MetricComparison(string Key, string Name, bool LowerIsBetter, string Status, string Message, MetricSide? Baseline, MetricSide Current, decimal? AbsoluteChange, decimal? RelativeChange, string? Movement);

public sealed record ComparisonData(BaselineView? Baseline, int MinimumSample, IReadOnlyList<MetricComparison> Comparisons, IReadOnlyList<NotMeasurable> NotMeasurable, string Note);

/// <summary>
/// ALV-N006 slice 5b: compares a period with a stored baseline without claiming more than the numbers show. The baseline is the one asked for or, by default, the latest. A metric is compared only if the baseline period ended
/// before this period starts, the metric was measured under the same definition version, and BOTH denominators reach the minimum sample (<see cref="ReportOptions.MinimumComparisonSample"/>). Otherwise it shows its own value
/// and the reason, and no change. Even a valid comparison says Lower or Higher, never "improved", and carries the sentence that a difference is not a cause. Failure modes handled: no baseline at all (every metric says so), an
/// unknown baseline id (404), a provider or location filter (<c>filter_not_supported</c>), an audit entry that cannot be saved (503).
/// </summary>
public class MeasurementComparisonService(AlveraDbContext db, MetricsService metrics, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<MeasurementComparisonService> log)
{
    public const int DefinitionVersion = 1;

    public async Task<ReportResult<ComparisonData>> RunAsync(ReportFilter current, Guid? baselineId, Guid actor, CancellationToken ct, DateTimeOffset? asOfUtc = null)
    {
        if (current.ProviderIds.Count > 0 || current.LocationId is not null) throw new ReportException("filter_not_supported", "Measurement is practice-wide and cannot be filtered by provider or location.", 400);
        var minimum = options.Value.MinimumComparisonSample;
        var asOf = asOfUtc ?? clock.UtcNow;
        string consistency;
        ComparisonData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var baseline = baselineId is { } id
                ? await db.ReportBaselines.AsNoTracking().Include(b => b.Metrics).SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new ReportException("baseline_not_found", "There is no baseline with that id.", 404)
                : await db.ReportBaselines.AsNoTracking().Include(b => b.Metrics).OrderByDescending(b => b.SetAtUtc).ThenBy(b => b.Id).FirstOrDefaultAsync(ct);
            var values = await metrics.ComputeAsync(current.StartUtc, current.EndUtc, asOf, ct);
            var comparisons = values.Select(v => Compare(v, baseline, current, minimum)).ToList();
            data = new ComparisonData(baseline is null ? null : MeasurementBaselineService.View(baseline), minimum, comparisons, MetricDefinitions.Unmeasurable, MetricDefinitions.NoCausationNote);
        }

        var records = data.Comparisons.Sum(c => c.Current.DenominatorCount);
        await ReportSupport.AuditAsync(db, log, ReportIds.MeasurementComparison, false, current, records, actor, ct);
        return new ReportResult<ComparisonData>(ReportIds.MeasurementComparison, DefinitionVersion, ReportSupport.View(current), clock.UtcNow, consistency, records, records == 0, data);
    }

    private static MetricComparison Compare(MetricValue now, ReportBaseline? baseline, ReportFilter current, int minimum)
    {
        var currentSide = new MetricSide(now.DefinitionVersion, now.NumeratorCount, now.DenominatorCount, now.Rate);
        var stored = baseline?.Metrics.SingleOrDefault(m => m.MetricKey == now.Key);
        MetricComparison Without(string status, MetricSide? side) => new(now.Key, now.Name, now.LowerIsBetter, status, ComparisonStatus.Message(status), side, currentSide, null, null, null);

        if (baseline is null || stored is null) return Without(ComparisonStatus.NoBaseline, null);
        var baselineSide = new MetricSide(stored.DefinitionVersion, stored.NumeratorCount, stored.DenominatorCount, stored.Rate);
        if (baseline.PeriodEndUtc > current.StartUtc) return Without(ComparisonStatus.BaselineNotBefore, baselineSide);
        if (stored.DefinitionVersion != now.DefinitionVersion) return Without(ComparisonStatus.DefinitionChanged, baselineSide);
        if (stored.DenominatorCount < minimum || stored.Rate is null) return Without(ComparisonStatus.BaselineSampleTooSmall, baselineSide);
        if (now.DenominatorCount < minimum || now.Rate is null) return Without(ComparisonStatus.CurrentSampleTooSmall, baselineSide);

        var absolute = Math.Round(now.Rate.Value - stored.Rate.Value, 4);
        decimal? relative = stored.Rate.Value == 0 ? null : Math.Round(absolute / stored.Rate.Value, 4);
        var movement = absolute < 0 ? "Lower" : absolute > 0 ? "Higher" : "Same";
        return new MetricComparison(now.Key, now.Name, now.LowerIsBetter, ComparisonStatus.Comparable, ComparisonStatus.Message(ComparisonStatus.Comparable), baselineSide, currentSide, absolute, relative, movement);
    }
}
