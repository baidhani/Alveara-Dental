using Alveara.Api.Architecture.Odontogram;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-006: the tooth key and the three numbering systems. No database. The stored identity is the FDI string; Universal and Palmer are display only, so the properties that matter
/// are that every system covers all 52 teeth exactly once, that reading what was shown returns the same tooth, and that well-known reference teeth read as the published charts say.
/// </summary>
public class ToothNumberingTests
{
    public static IEnumerable<object[]> Systems() => Enum.GetValues<NumberingSystem>().Select(s => new object[] { s });

    [Fact]
    public void There_are_52_keys_32_permanent_and_20_primary_with_no_duplicates()
    {
        Assert.Equal(32, ToothKeys.Permanent.Count);
        Assert.Equal(20, ToothKeys.Primary.Count);
        Assert.Equal(52, ToothKeys.All.Count);
        Assert.Equal(52, ToothKeys.All.Distinct().Count());
    }

    [Theory]
    [InlineData("11", true)] [InlineData("48", true)] [InlineData("55", true)] [InlineData("85", true)]
    [InlineData("19", false)] [InlineData("10", false)] [InlineData("49", false)] [InlineData("56", false)] [InlineData("86", false)] [InlineData("90", false)]
    [InlineData("1", false)] [InlineData("111", false)] [InlineData(" 11", false)] [InlineData("", false)] [InlineData(null, false)] [InlineData("1A", false)]
    public void Only_the_52_fdi_strings_are_keys(string? key, bool valid) => Assert.Equal(valid, ToothKeys.IsValid(key));

    [Theory, MemberData(nameof(Systems))]
    public void Every_system_shows_each_of_the_52_teeth_once_and_reads_it_back(NumberingSystem system)
    {
        var shown = ToothKeys.All.Select(k => ToothNumbering.Display(k, system)).ToList();
        Assert.Equal(52, shown.Distinct(StringComparer.Ordinal).Count());
        foreach (var key in ToothKeys.All)
            Assert.Equal(key, ToothNumbering.Parse(ToothNumbering.Display(key, system), system));
    }

    [Theory]
    [InlineData("18", "1")] [InlineData("11", "8")] [InlineData("21", "9")] [InlineData("28", "16")]
    [InlineData("38", "17")] [InlineData("31", "24")] [InlineData("41", "25")] [InlineData("48", "32")]
    [InlineData("55", "A")] [InlineData("51", "E")] [InlineData("61", "F")] [InlineData("65", "J")]
    [InlineData("75", "K")] [InlineData("71", "O")] [InlineData("81", "P")] [InlineData("85", "T")]
    public void Universal_matches_the_published_chart(string key, string universal) => Assert.Equal(universal, ToothNumbering.Display(key, NumberingSystem.Universal));

    [Theory]
    [InlineData("11", "UR1")] [InlineData("18", "UR8")] [InlineData("21", "UL1")] [InlineData("28", "UL8")]
    [InlineData("31", "LL1")] [InlineData("38", "LL8")] [InlineData("41", "LR1")] [InlineData("48", "LR8")]
    [InlineData("51", "URA")] [InlineData("55", "URE")] [InlineData("61", "ULA")] [InlineData("75", "LLE")] [InlineData("81", "LRA")] [InlineData("85", "LRE")]
    public void Palmer_matches_the_published_chart(string key, string palmer) => Assert.Equal(palmer, ToothNumbering.Display(key, NumberingSystem.Palmer));

    [Fact]
    public void Fdi_display_is_the_key_itself() => Assert.All(ToothKeys.All, k => Assert.Equal(k, ToothNumbering.Display(k, NumberingSystem.Fdi)));

    [Fact]
    public void Universal_is_the_default_system() => Assert.Equal(NumberingSystem.Universal, default(NumberingSystem));

    [Fact]
    public void Universal_runs_1_to_32_and_A_to_T_without_gaps()
    {
        var permanent = ToothKeys.Permanent.Select(k => int.Parse(ToothNumbering.Display(k, NumberingSystem.Universal))).OrderBy(x => x);
        Assert.Equal(Enumerable.Range(1, 32), permanent);
        var primary = ToothKeys.Primary.Select(k => ToothNumbering.Display(k, NumberingSystem.Universal)[0]).OrderBy(x => x);
        Assert.Equal(Enumerable.Range('A', 20).Select(c => (char)c), primary);
    }

    [Theory]
    [InlineData("1", NumberingSystem.Universal, "18")] [InlineData(" 32 ", NumberingSystem.Universal, "48")] [InlineData("a", NumberingSystem.Universal, "55")]
    [InlineData("ur1", NumberingSystem.Palmer, "11")] [InlineData(" LLe ", NumberingSystem.Palmer, "75")]
    [InlineData("48", NumberingSystem.Fdi, "48")]
    public void Parse_reads_what_a_person_types(string text, NumberingSystem system, string key) => Assert.Equal(key, ToothNumbering.Parse(text, system));

    [Theory]
    [InlineData("0", NumberingSystem.Universal)] [InlineData("33", NumberingSystem.Universal)] [InlineData("U", NumberingSystem.Universal)] [InlineData("1a", NumberingSystem.Universal)]
    [InlineData("UR9", NumberingSystem.Palmer)] [InlineData("UR0", NumberingSystem.Palmer)] [InlineData("URF", NumberingSystem.Palmer)] [InlineData("XX1", NumberingSystem.Palmer)] [InlineData("UR", NumberingSystem.Palmer)]
    [InlineData("19", NumberingSystem.Fdi)] [InlineData("A", NumberingSystem.Fdi)]
    [InlineData("", NumberingSystem.Universal)] [InlineData(null, NumberingSystem.Fdi)]
    public void Parse_refuses_what_is_not_exactly_a_tooth_instead_of_guessing(string? text, NumberingSystem system) => Assert.Null(ToothNumbering.Parse(text, system));

    [Fact]
    public void Displaying_something_that_is_not_a_key_is_a_defect_not_a_blank()
    {
        Assert.Throws<ArgumentException>(() => ToothNumbering.Display("19", NumberingSystem.Universal));
        Assert.Throws<ArgumentException>(() => ToothNumbering.Display("", NumberingSystem.Palmer));
    }

    [Fact]
    public void Anterior_means_positions_1_to_3_and_decides_which_surfaces_exist()
    {
        Assert.True(ToothKeys.IsAnterior("11"));
        Assert.True(ToothKeys.IsAnterior("53"));
        Assert.False(ToothKeys.IsAnterior("14"));
        Assert.False(ToothKeys.IsAnterior("55"));
        Assert.Contains("I", ToothSurfaces.For("21"));
        Assert.DoesNotContain("O", ToothSurfaces.For("21"));
        Assert.Contains("O", ToothSurfaces.For("36"));
        Assert.DoesNotContain("F", ToothSurfaces.For("36"));
        Assert.True(ToothSurfaces.IsValid("16", "B"));
        Assert.False(ToothSurfaces.IsValid("11", "B"));
        Assert.False(ToothSurfaces.IsValid("99", "M"));
        Assert.False(ToothSurfaces.IsValid("11", null));
    }
}
