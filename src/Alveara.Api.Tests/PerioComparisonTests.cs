using Alveara.Api.Architecture.Periodontal;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-012-C01: the previous-versus-current comparison and its trend figures. No database. Every expected number is worked out by hand in the fixtures, not by calling the code under test.</summary>
public class PerioComparisonTests
{
    private static PerioReadingView V(string tooth, string site, int pd, int rec = 0, bool bleed = false, bool? pus = null, bool? plaque = null) => new(tooth, site, pd, rec, pd + rec, bleed, pus, plaque);
    private static PerioToothView Th(string tooth, int? mobility = null, int? furcation = null, bool excluded = false) => new(tooth, mobility, furcation, excluded);
    private static PerioChartSnapshot Chart(PerioReadingView[] readings, PerioToothView[]? teeth = null) => new(readings, teeth ?? []);

    // previous: depths 5,3,7,2,3  attachment 6,3,9,2,3  2 bleeding  2 deeper (5,7)  1 very deep (7)  1 pus  plaque 1 of 1
    private static readonly PerioChartSnapshot Previous = Chart(
        [V("16", "B", 5, 1, true), V("16", "MB", 3), V("26", "DL", 7, 2, true, true), V("26", "L", 2, 0, false, null, true), V("11", "B", 3)], [Th("16", mobility: 1), Th("26", furcation: 1)]);
    // current: depths 3,4,9,2,4  attachment 4,4,11,2,4  2 bleeding  3 deeper (4,9,4)  1 very deep (9)  0 pus  plaque 0 of 1
    private static readonly PerioChartSnapshot Current = Chart(
        [V("16", "B", 3, 1), V("16", "MB", 4), V("26", "DL", 9, 2, true), V("26", "L", 2, 0, false, null, false), V("47", "B", 4, 0, true)], [Th("16", mobility: 2), Th("26", furcation: 1), Th("38", excluded: true)]);

    [Fact]
    public void Whole_chart_figures_are_the_arithmetic_a_person_would_do_by_hand()
    {
        Assert.Equal(new PerioFigures(5, 3, 4.0m, 4.6m, 40, 2, 1, 1, 100), PerioComparison.Figures(Previous));
        Assert.Equal(new PerioFigures(5, 3, 4.4m, 5.0m, 40, 3, 1, 0, 0), PerioComparison.Figures(Current));
    }

    [Fact]
    public void An_empty_chart_has_zero_figures_and_no_plaque_percentage_because_nothing_was_assessed()
        => Assert.Equal(new PerioFigures(0, 0, 0, 0, 0, 0, 0, 0, null), PerioComparison.Figures(Chart([])));

    [Fact]
    public void Sites_in_both_charts_are_matched_and_counted_as_improved_worsened_or_unchanged_by_the_depth_change()
    {
        var c = PerioComparison.Compare(Previous, Current);
        Assert.Equal((4, 1, 1, 2), (c.MatchedSites, c.Improved, c.Worsened, c.Unchanged));              // 16B -2, 16MB +1, 26DL +2, 26L 0
        Assert.Equal(0.3m, c.MeanDepthChangeMm);                                                       // (-2 + 1 + 2 + 0) / 4 = 0.25, rounded up from the half
        var byKey = c.Sites.ToDictionary(s => $"{s.ToothKey}{s.Site}");
        Assert.Equal(("Improved", -2, -2), (byKey["16B"].Trend, byKey["16B"].DepthChangeMm, byKey["16B"].AttachmentLossChangeMm));
        Assert.Equal(("Unchanged", 1, 1), (byKey["16MB"].Trend, byKey["16MB"].DepthChangeMm, byKey["16MB"].AttachmentLossChangeMm));
        Assert.Equal(("Worsened", 2, 2), (byKey["26DL"].Trend, byKey["26DL"].DepthChangeMm, byKey["26DL"].AttachmentLossChangeMm));
        Assert.Equal(("Unchanged", 0), (byKey["26L"].Trend, byKey["26L"].DepthChangeMm));
        Assert.Equal((true, true), (byKey["26DL"].PreviousBleeding, byKey["26DL"].CurrentBleeding));
        Assert.Equal((true, false), (byKey["16B"].PreviousBleeding, byKey["16B"].CurrentBleeding));
    }

    [Fact]
    public void Sites_in_only_one_chart_are_listed_and_counted_separately_and_never_dropped_or_averaged_in()
    {
        var c = PerioComparison.Compare(Previous, Current);
        Assert.Equal((1, 1), (c.OnlyPrevious, c.OnlyCurrent));
        var gone = c.Sites.Single(s => s is { ToothKey: "11", Site: "B" });
        Assert.Equal(("OnlyPrevious", 3, null), (gone.Trend, gone.PreviousDepthMm, gone.CurrentDepthMm));
        Assert.Null(gone.DepthChangeMm);
        var added = c.Sites.Single(s => s is { ToothKey: "47", Site: "B" });
        Assert.Equal(("OnlyCurrent", 4, null), (added.Trend, added.CurrentDepthMm, added.PreviousDepthMm));
        Assert.Equal(6, c.Sites.Count);                                                               // 4 matched + 1 + 1
    }

    [Fact]
    public void Sites_come_out_in_the_charts_row_order_not_the_order_they_were_entered()
        => Assert.Equal(new[] { "16B", "16MB", "11B", "26DL", "26L", "47B" }, PerioComparison.Compare(Previous, Current).Sites.Select(s => $"{s.ToothKey}{s.Site}"));

    [Fact]
    public void Teeth_whose_state_or_grades_differ_are_listed_in_arch_order_and_unchanged_teeth_are_not()
    {
        var teeth = PerioComparison.Compare(Previous, Current).Teeth;
        Assert.Equal(new[] { "16", "11", "47", "38" }, teeth.Select(t => t.ToothKey));
        Assert.Equal(new PerioToothChange("16", "Charted", "Charted", 1, 2, null, null), teeth[0]);       // mobility 1 to 2
        Assert.Equal(new PerioToothChange("11", "Charted", "NotRecorded", null, null, null, null), teeth[1]);
        Assert.Equal(new PerioToothChange("47", "NotRecorded", "Charted", null, null, null, null), teeth[2]);
        Assert.Equal(new PerioToothChange("38", "NotRecorded", "Excluded", null, null, null, null), teeth[3]);
        Assert.DoesNotContain(teeth, t => t.ToothKey == "26");                                         // same furcation grade, still charted
    }

    [Fact]
    public void Comparing_a_chart_with_itself_changes_nothing()
    {
        var c = PerioComparison.Compare(Previous, Previous);
        Assert.Equal((5, 0, 0, 5, 0, 0, 0m), (c.MatchedSites, c.Improved, c.Worsened, c.Unchanged, c.OnlyPrevious, c.OnlyCurrent, c.MeanDepthChangeMm));
        Assert.Empty(c.Teeth);
        Assert.All(c.Sites, s => Assert.Equal(("Unchanged", 0, 0), (s.Trend, s.DepthChangeMm, s.AttachmentLossChangeMm)));
    }

    [Fact]
    public void Comparing_with_an_empty_chart_in_either_direction_reports_every_site_as_only_in_one()
    {
        var added = PerioComparison.Compare(Chart([]), Current);
        Assert.Equal((0, 0, 5), (added.MatchedSites, added.OnlyPrevious, added.OnlyCurrent));
        Assert.Equal(0m, added.MeanDepthChangeMm);
        var removed = PerioComparison.Compare(Previous, Chart([]));
        Assert.Equal((0, 5, 0), (removed.MatchedSites, removed.OnlyPrevious, removed.OnlyCurrent));
        Assert.All(removed.Sites, s => Assert.Empty(s.CurrentCues));
    }

    [Theory]
    [InlineData(-15, "Improved")] [InlineData(-3, "Improved")] [InlineData(-2, "Improved")] [InlineData(-1, "Unchanged")] [InlineData(0, "Unchanged")]
    [InlineData(1, "Unchanged")] [InlineData(2, "Worsened")] [InlineData(3, "Worsened")] [InlineData(15, "Worsened")]
    public void A_change_of_two_millimetres_or_more_counts_and_a_smaller_one_is_within_the_error_of_measuring(int change, string trend)
        => Assert.Equal(trend, PerioComparison.TrendOf(change));

    [Theory]
    [InlineData(0.25, 0.3)] [InlineData(0.24, 0.2)] [InlineData(-0.25, -0.3)] [InlineData(4.45, 4.5)] [InlineData(0, 0)]
    public void Averages_are_rounded_to_one_decimal_with_halves_going_away_from_zero(double value, double expected) => Assert.Equal((decimal)expected, PerioComparison.Round1(value));

    [Theory]
    [InlineData(3, 0, false, null, "")]
    [InlineData(4, 0, false, null, "deep")]
    [InlineData(5, 0, false, null, "deep")]
    [InlineData(6, 0, false, null, "very_deep")]
    [InlineData(3, 2, false, null, "recession")]
    [InlineData(3, 0, true, null, "bleeding")]
    [InlineData(3, 0, false, true, "suppuration")]
    [InlineData(3, 0, false, false, "")]
    [InlineData(7, 3, true, true, "very_deep,recession,bleeding,suppuration")]
    public void Cues_mark_values_worth_a_second_look_by_plain_thresholds_and_say_nothing_about_a_diagnosis(int pd, int rec, bool bleed, bool? pus, string expected)
        => Assert.Equal(expected, string.Join(",", PerioCues.Of(V("16", "B", pd, rec, bleed, pus))));

    [Fact]
    public void The_cues_of_a_comparison_are_those_of_the_current_reading()
    {
        var sites = PerioComparison.Compare(Previous, Current).Sites.ToDictionary(s => $"{s.ToothKey}{s.Site}");
        Assert.Equal(new[] { "recession" }, sites["16B"].CurrentCues);
        Assert.Equal(new[] { "very_deep", "recession", "bleeding" }, sites["26DL"].CurrentCues);
        Assert.Equal(new[] { "deep", "bleeding" }, sites["47B"].CurrentCues);
        Assert.Empty(sites["26L"].CurrentCues);
    }
}
