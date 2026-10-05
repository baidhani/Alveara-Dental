using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-012: the rules for valid periodontal charting. No database. Happy path, every failure path the story names (incorrect data), and the boundaries.</summary>
public class PerioRulesTests
{
    private static PerioReadingInput R(string tooth = "16", string site = "B", int pd = 3, int rec = 0, bool bleed = false) => new(tooth, site, pd, rec, bleed);

    [Fact]
    public void A_well_formed_chart_has_no_problems()
        => Assert.Empty(PerioRules.Validate([R("16", "B", 3, 1, true), R("16", "MB", 4, 0, false), R("47", "DL", 6, 2, true)]));

    [Fact]
    public void A_full_mouth_of_192_readings_is_accepted_and_one_more_is_not()
    {
        var full = ToothKeys.Permanent.SelectMany(t => PerioRules.Sites.Select(s => R(t, s))).ToList();
        Assert.Equal(192, PerioRules.MaxReadings);
        Assert.Empty(PerioRules.Validate(full));
        var tooMany = PerioRules.Validate([.. full, R("11", "B")]);
        Assert.Contains(tooMany, p => p.Code == "too_many");
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(15)]
    public void The_edges_of_the_millimetre_range_are_accepted(int mm) => Assert.Empty(PerioRules.Validate([R(pd: mm, rec: mm)]));

    [Theory]
    [InlineData(-1)] [InlineData(16)] [InlineData(100)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void Depth_and_recession_outside_0_to_15_are_refused_naming_the_tooth_site_and_range(int mm)
    {
        var problems = PerioRules.Validate([R("36", "ML", pd: mm, rec: mm)]);
        Assert.Equal(["probingDepthMm", "recessionMm"], problems.Select(p => p.Field).ToArray());
        Assert.All(problems, p =>
        {
            Assert.Equal("out_of_range", p.Code);
            Assert.Equal(("36", "ML"), (p.ToothKey, p.Site));
            Assert.Contains("0 to 15 mm", p.Message);
            Assert.Contains("36", p.Message);
        });
    }

    [Theory]
    [InlineData("19")] [InlineData("")] [InlineData(" 16")] [InlineData("1")] [InlineData("abc")] [InlineData(null)]
    public void A_value_that_is_not_a_tooth_is_refused(string? tooth)
        => Assert.Contains(PerioRules.Validate([R(tooth!)]), p => p.Code == "unknown_tooth" && p.Field == "toothKey");

    [Fact]
    public void A_primary_tooth_is_refused_with_its_own_message() // 55 is a real FDI key, so "not a tooth" would be the wrong thing to say
    {
        var p = Assert.Single(PerioRules.Validate([R("55")]));
        Assert.Equal("primary_tooth", p.Code);
        Assert.Contains("permanent teeth only", p.Message);
    }

    [Theory]
    [InlineData("X")] [InlineData("b")] [InlineData("")] [InlineData("DB ")] [InlineData(null)]
    public void A_site_must_be_one_of_the_six_exactly(string? site)
        => Assert.Contains(PerioRules.Validate([R(site: site!)]), p => p.Code == "unknown_site" && p.Field == "site");

    [Fact]
    public void The_same_tooth_and_site_twice_is_refused_but_the_same_site_on_two_teeth_is_fine()
    {
        Assert.Contains(PerioRules.Validate([R("16", "B", 3), R("16", "B", 5)]), p => p.Code == "duplicate_site");
        Assert.Empty(PerioRules.Validate([R("16", "B"), R("17", "B")]));
    }

    [Fact]
    public void An_empty_or_missing_chart_is_refused()
    {
        Assert.Equal("required", Assert.Single(PerioRules.Validate(null)).Code);
        Assert.Equal("required", Assert.Single(PerioRules.Validate([])).Code);
    }

    [Fact]
    public void Every_problem_in_a_chart_is_reported_at_once_so_the_person_corrects_it_in_one_pass()
    {
        var problems = PerioRules.Validate([R("16", "B", 99, -1), R("19", "B"), R("17", "Q"), R("55", "B")]);
        Assert.Equal(["out_of_range", "out_of_range", "unknown_tooth", "unknown_site", "primary_tooth"], problems.Select(p => p.Code).ToArray());
    }

    [Fact]
    public void Attachment_loss_is_depth_plus_recession_and_is_not_a_stored_field()
    {
        Assert.Equal(7, PerioRules.AttachmentLossMm(R(pd: 5, rec: 2)));
        Assert.DoesNotContain(typeof(PerioReadingInput).GetProperties(), p => p.Name.Contains("Attachment"));
    }

    [Fact]
    public void The_six_sites_are_distinct_and_cover_both_sides_of_a_tooth()
    {
        Assert.Equal(6, PerioRules.Sites.Distinct().Count());
        Assert.Equal(["DB", "B", "MB", "DL", "L", "ML"], PerioRules.Sites.ToArray());
    }
}
