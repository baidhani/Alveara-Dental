using Alveara.Api.Architecture.Periodontal;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-012-C01: the rules for a complete chart - the new measures (suppuration, plaque, mobility, furcation), excluded and missing teeth, and the attachment-loss rule. No database. Every STORY-012 chart
/// must still pass unchanged.
/// </summary>
public class PerioChartValidatorTests
{
    private static PerioReadingInput R(string tooth = "16", string site = "B", int pd = 3, int rec = 1, bool bleed = false, bool? pus = null, bool? plaque = null) => new(tooth, site, pd, rec, bleed, pus, plaque);
    private static PerioToothInput T(string tooth = "16", int? mobility = null, int? furcation = null, bool excluded = false) => new(tooth, mobility, furcation, excluded);
    private static PerioChartInput Chart(IReadOnlyList<PerioReadingInput>? readings = null, IReadOnlyList<PerioToothInput>? teeth = null) => new(readings ?? [R()], teeth);
    private static string[] Codes(PerioChartInput c, params string[] absent) => PerioChartValidator.Validate(c, absent.ToHashSet()).Select(p => p.Code).ToArray();

    // ---------- compatibility ----------

    [Fact]
    public void A_story_012_chart_with_no_new_measures_and_no_tooth_records_is_still_valid()
        => Assert.Empty(PerioChartValidator.Validate(new PerioChartInput([R("16", "B", 3, 1, true), R("47", "DL", 6, 2, false)], null)));

    [Fact]
    public void The_site_rules_of_story_012_still_apply_unchanged()
        => Assert.Equal(["out_of_range", "unknown_tooth", "unknown_site", "primary_tooth"], Codes(Chart([R(pd: 99), R("19"), R(site: "Q"), R("55")])));

    // ---------- the new measures ----------

    [Fact]
    public void Suppuration_and_plaque_may_be_true_false_or_not_assessed_and_all_three_are_valid()
        => Assert.Empty(PerioChartValidator.Validate(Chart([R(site: "DB", pus: true, plaque: true), R(site: "B", pus: false, plaque: false), R(site: "MB")])));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Mobility_and_furcation_grades_0_to_3_are_valid(int grade) => Assert.Empty(PerioChartValidator.Validate(Chart(teeth: [T("16", grade, grade)])));

    [Theory]
    [InlineData(-1)] [InlineData(4)] [InlineData(100)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void Mobility_and_furcation_outside_0_to_3_are_refused_naming_the_tooth_and_the_range(int grade)
    {
        var problems = PerioChartValidator.Validate(Chart(teeth: [T("16", grade, grade)]));
        Assert.Equal(["mobility", "furcation"], problems.Select(p => p.Field).ToArray());
        Assert.All(problems, p => { Assert.Equal(("out_of_range", "16"), (p.Code, p.ToothKey)); Assert.Contains("0 to 3", p.Message); });
    }

    [Fact]
    public void A_furcation_grade_on_a_single_rooted_tooth_is_refused_but_mobility_there_is_fine()
    {
        Assert.Equal(["furcation_not_applicable"], Codes(Chart(teeth: [T("11", furcation: 1)])));
        Assert.Empty(PerioChartValidator.Validate(Chart(teeth: [T("11", mobility: 2)])));
        Assert.Empty(PerioChartValidator.Validate(Chart(teeth: [T("14", furcation: 1), T("26", furcation: 3)])));       // a first upper premolar and a molar have more than one root
    }

    [Fact]
    public void A_tooth_record_must_name_a_permanent_tooth_once()
    {
        Assert.Equal(["unknown_tooth"], Codes(Chart(teeth: [T("19", 1)])));
        Assert.Equal(["primary_tooth"], Codes(Chart(teeth: [T("55", 1)])));
        Assert.Equal(["duplicate_tooth"], Codes(Chart(teeth: [T("16", 1), T("16", 2)])));
    }

    // ---------- excluded and missing teeth ----------

    [Fact]
    public void An_excluded_tooth_with_no_readings_is_valid_and_does_not_disturb_the_rest_of_the_chart()
        => Assert.Empty(PerioChartValidator.Validate(Chart([R("17"), R("18")], [T("16", excluded: true)])));

    [Fact]
    public void An_excluded_tooth_cannot_also_carry_readings_or_grades()
    {
        Assert.Equal(["excluded_tooth"], Codes(Chart([R("16", "B")], [T("16", excluded: true)])));
        Assert.Equal(["excluded_tooth", "excluded_tooth"], Codes(Chart([R("16", "B"), R("16", "MB")], [T("16", excluded: true)])));
        Assert.Equal(["excluded_tooth"], Codes(Chart([R("17")], [T("16", mobility: 1, excluded: true)])));
    }

    [Fact]
    public void A_tooth_the_odontogram_records_as_missing_cannot_be_charted_unless_it_is_marked_not_charted()
    {
        var found = PerioChartValidator.Validate(Chart([R("16", "B"), R("17")]), new HashSet<string> { "16" });
        var p = Assert.Single(found);
        Assert.Equal(("tooth_absent", "16", "B"), (p.Code, p.ToothKey, p.Site));
        Assert.Contains("recorded as missing in the odontogram", p.Message);
        Assert.Contains("Mark it as not charted", p.Message);
        Assert.Empty(PerioChartValidator.Validate(Chart([R("17")], [T("16", excluded: true)]), new HashSet<string> { "16" }));    // the way out
        Assert.Empty(PerioChartValidator.Validate(Chart([R("17")]), new HashSet<string> { "16" }));                               // a missing tooth with no readings is simply not charted
    }

    [Fact]
    public void Everything_wrong_is_reported_together_so_nothing_valid_is_judged_in_isolation()
        => Assert.Equal(["out_of_range", "out_of_range", "furcation_not_applicable", "excluded_tooth", "tooth_absent"],
            Codes(Chart([R("17", pd: 40), R("26", "B"), R("18")], [T("17", mobility: 9), T("11", furcation: 1), T("26", excluded: true)]), "18"));

    [Fact]
    public void An_empty_or_missing_chart_is_still_refused() => Assert.Equal(["required"], PerioChartValidator.Validate(null).Select(p => p.Code).ToArray());

    // ---------- the attachment-loss rule ----------

    [Theory]
    [InlineData(0, 0, 0)] [InlineData(3, 0, 3)] [InlineData(3, 2, 5)] [InlineData(15, 15, 30)] [InlineData(0, 4, 4)]
    public void Attachment_loss_is_probing_depth_plus_recession_and_never_less_than_the_depth(int pd, int rec, int cal)
    {
        var r = R(pd: pd, rec: rec);
        Assert.Equal(cal, PerioRules.AttachmentLossMm(r));
        Assert.True(PerioRules.AttachmentLossMm(r) >= r.ProbingDepthMm);
    }

    [Fact]
    public void Attachment_loss_is_derived_and_there_is_no_field_to_store_it_in()
        => Assert.DoesNotContain(typeof(PerioReadingInput).GetProperties().Select(p => p.Name), n => n.Contains("Attachment") || n == "Cal");
}
