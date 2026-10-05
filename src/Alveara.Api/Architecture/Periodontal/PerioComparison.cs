namespace Alveara.Api.Architecture.Periodontal;

/// <summary>What is compared: a chart's readings and whole-tooth records, whether it is a saved chart or a draft being entered.</summary>
public sealed record PerioChartSnapshot(IReadOnlyList<PerioReadingView> Readings, IReadOnlyList<PerioToothView> Teeth)
{
    public static PerioChartSnapshot From(PerioExamView exam) => new(exam.Readings, exam.Teeth ?? []);
    public static PerioChartSnapshot From(PerioSessionView session) => new(session.Readings, session.Teeth);
}

/// <summary>
/// Visual cues for a reading (ALV-012-C01). They draw the eye to values worth a second look and are NOT a diagnosis and never a classification: the thresholds are plain numbers anyone can check, and a
/// reading with no cue is not thereby healthy. Presentation aids only.
/// </summary>
public static class PerioCues
{
    /// <summary>A probing depth of this many millimetres or more is marked as a deeper pocket.</summary>
    public const int DeepMm = 4;
    /// <summary>A probing depth of this many millimetres or more is marked as a very deep pocket (it is also a deeper pocket).</summary>
    public const int VeryDeepMm = 6;
    /// <summary>A change in probing depth of this many millimetres or more, either way, is counted as better or worse; a smaller one is within the error of measuring and counts as unchanged.</summary>
    public const int SignificantChangeMm = 2;

    public static IReadOnlyList<string> Of(PerioReadingView r)
    {
        var cues = new List<string>();
        if (r.ProbingDepthMm >= VeryDeepMm) cues.Add("very_deep");
        else if (r.ProbingDepthMm >= DeepMm) cues.Add("deep");
        if (r.RecessionMm > 0) cues.Add("recession");
        if (r.Bleeding) cues.Add("bleeding");
        if (r.Suppuration == true) cues.Add("suppuration");
        return cues;
    }
}

/// <summary>Whole-chart figures: plain arithmetic over the readings, so they can be checked by eye against the table.</summary>
public sealed record PerioFigures(int Sites, int Teeth, decimal MeanDepthMm, decimal MeanAttachmentLossMm, int BleedingPercent, int DeepSites, int VeryDeepSites, int SuppurationSites, int? PlaquePercent);

/// <summary>One site compared. Null on a side means that chart has no reading there. <see cref="Trend"/> is Improved, Worsened or Unchanged for a site in both (by probing depth, see <see cref="PerioCues.SignificantChangeMm"/>), or OnlyPrevious / OnlyCurrent.</summary>
public sealed record PerioSiteChange(
    string ToothKey, string Site, int? PreviousDepthMm, int? CurrentDepthMm, int? DepthChangeMm, int? PreviousAttachmentLossMm, int? CurrentAttachmentLossMm, int? AttachmentLossChangeMm,
    bool? PreviousBleeding, bool? CurrentBleeding, string Trend, IReadOnlyList<string> CurrentCues);

/// <summary>One tooth whose state or grades differ between the two charts. State is Charted (has readings), Excluded (marked not charted) or NotRecorded.</summary>
public sealed record PerioToothChange(string ToothKey, string PreviousState, string CurrentState, int? PreviousMobility, int? CurrentMobility, int? PreviousFurcation, int? CurrentFurcation);

/// <summary>Two charts compared. The counts cover sites in both charts; sites in only one are listed and counted separately, never dropped.</summary>
public sealed record PerioComparisonView(
    PerioFigures Previous, PerioFigures Current, int MatchedSites, int Improved, int Worsened, int Unchanged, int OnlyPrevious, int OnlyCurrent, decimal MeanDepthChangeMm,
    IReadOnlyList<PerioSiteChange> Sites, IReadOnlyList<PerioToothChange> Teeth);

/// <summary>ALV-012-C01: previous-versus-current comparison and trend summary. Pure, no database; every number is arithmetic on the two charts' readings.</summary>
public static class PerioComparison
{
    public static decimal Round1(double value) => Math.Round((decimal)value, 1, MidpointRounding.AwayFromZero);

    public static PerioFigures Figures(PerioChartSnapshot chart)
    {
        var r = chart.Readings;
        var plaqueAssessed = r.Where(x => x.Plaque is not null).ToList();
        return new PerioFigures(
            r.Count, r.Select(x => x.ToothKey).Distinct().Count(),
            r.Count == 0 ? 0 : Round1(r.Average(x => x.ProbingDepthMm)), r.Count == 0 ? 0 : Round1(r.Average(x => x.AttachmentLossMm)),
            r.Count == 0 ? 0 : (int)Math.Round(100.0 * r.Count(x => x.Bleeding) / r.Count, MidpointRounding.AwayFromZero),
            r.Count(x => x.ProbingDepthMm >= PerioCues.DeepMm), r.Count(x => x.ProbingDepthMm >= PerioCues.VeryDeepMm), r.Count(x => x.Suppuration == true),
            plaqueAssessed.Count == 0 ? null : (int)Math.Round(100.0 * plaqueAssessed.Count(x => x.Plaque == true) / plaqueAssessed.Count, MidpointRounding.AwayFromZero));
    }

    public static string TrendOf(int depthChangeMm) =>
        depthChangeMm <= -PerioCues.SignificantChangeMm ? "Improved" : depthChangeMm >= PerioCues.SignificantChangeMm ? "Worsened" : "Unchanged";

    public static PerioComparisonView Compare(PerioChartSnapshot previous, PerioChartSnapshot current)
    {
        var before = previous.Readings.ToDictionary(r => (r.ToothKey, r.Site));
        var now = current.Readings.ToDictionary(r => (r.ToothKey, r.Site));
        var sites = new List<PerioSiteChange>();
        foreach (var (tooth, site) in PerioSiteModel.ArchOrder.SelectMany(t => PerioRules.Sites.Select(site => (t, site))))      // the chart's row order
        {
            before.TryGetValue((tooth, site), out var p);
            now.TryGetValue((tooth, site), out var c);
            if (p is null && c is null) continue;
            var change = p is not null && c is not null ? c.ProbingDepthMm - p.ProbingDepthMm : (int?)null;
            sites.Add(new PerioSiteChange(
                tooth, site, p?.ProbingDepthMm, c?.ProbingDepthMm, change, p?.AttachmentLossMm, c?.AttachmentLossMm, p is not null && c is not null ? c.AttachmentLossMm - p.AttachmentLossMm : null,
                p?.Bleeding, c?.Bleeding, change is { } d ? TrendOf(d) : p is null ? "OnlyCurrent" : "OnlyPrevious", c is null ? [] : PerioCues.Of(c)));
        }
        var matched = sites.Where(s => s.DepthChangeMm is not null).ToList();
        return new PerioComparisonView(
            Figures(previous), Figures(current), matched.Count, matched.Count(s => s.Trend == "Improved"), matched.Count(s => s.Trend == "Worsened"), matched.Count(s => s.Trend == "Unchanged"),
            sites.Count(s => s.Trend == "OnlyPrevious"), sites.Count(s => s.Trend == "OnlyCurrent"),
            matched.Count == 0 ? 0 : Round1(matched.Average(s => s.DepthChangeMm!.Value)), sites, ToothChanges(previous, current));
    }

    private static string StateOf(PerioChartSnapshot chart, string tooth) =>
        chart.Teeth.FirstOrDefault(t => t.ToothKey == tooth)?.Excluded == true ? "Excluded" : chart.Readings.Any(r => r.ToothKey == tooth) ? "Charted" : "NotRecorded";

    private static List<PerioToothChange> ToothChanges(PerioChartSnapshot previous, PerioChartSnapshot current)
    {
        var changes = new List<PerioToothChange>();
        foreach (var tooth in PerioSiteModel.ArchOrder)
        {
            var (p, c) = (previous.Teeth.FirstOrDefault(t => t.ToothKey == tooth), current.Teeth.FirstOrDefault(t => t.ToothKey == tooth));
            var (ps, cs) = (StateOf(previous, tooth), StateOf(current, tooth));
            if (ps == cs && p?.Mobility == c?.Mobility && p?.Furcation == c?.Furcation) continue;
            changes.Add(new PerioToothChange(tooth, ps, cs, p?.Mobility, c?.Mobility, p?.Furcation, c?.Furcation));
        }
        return changes;
    }
}
