using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-012-C01: the six-site model - which site is which on every tooth, how it reads, the one full-mouth entry order, and which teeth can have a furcation. No database.</summary>
public class PerioSiteModelTests
{
    [Fact]
    public void Every_tooth_has_six_distinct_sites_three_on_each_side_and_the_codes_are_the_stored_identity()
    {
        Assert.Equal(["DB", "B", "MB", "DL", "L", "ML"], PerioRules.Sites.ToArray());
        Assert.Equal(["DB", "B", "MB"], PerioRules.Sites.Where(PerioSiteModel.IsCheekSide).ToArray());
        Assert.Equal(["DL", "L", "ML"], PerioRules.Sites.Where(s => !PerioSiteModel.IsCheekSide(s)).ToArray());
        foreach (var tooth in ToothKeys.Permanent)
            Assert.Equal(6, PerioRules.Sites.Select(s => PerioSiteModel.Describe(tooth, s)).Distinct().Count());   // all six distinguishable on every tooth
    }

    [Theory]
    [InlineData("16", "DB", "distal buccal")] [InlineData("16", "B", "mid buccal")] [InlineData("16", "MB", "mesial buccal")]
    [InlineData("16", "DL", "distal palatal")] [InlineData("16", "L", "mid palatal")] [InlineData("16", "ML", "mesial palatal")]
    [InlineData("46", "DL", "distal lingual")] [InlineData("46", "ML", "mesial lingual")]
    [InlineData("11", "B", "mid facial")] [InlineData("11", "MB", "mesial facial")] [InlineData("11", "DL", "distal palatal")]
    [InlineData("33", "DB", "distal facial")] [InlineData("33", "L", "mid lingual")]
    public void A_site_reads_as_facial_or_buccal_and_palatal_or_lingual_by_the_tooth(string tooth, string site, string text)
        => Assert.Equal(text, PerioSiteModel.Describe(tooth, site));

    [Theory]
    [InlineData("55", "B")] [InlineData("16", "X")] [InlineData("", "B")]
    public void Describing_something_that_is_not_a_site_of_a_tooth_throws(string tooth, string site)
        => Assert.Throws<ArgumentException>(() => PerioSiteModel.Describe(tooth, site));

    [Fact]
    public void The_sweep_visits_every_site_of_every_tooth_exactly_once()
    {
        var sweep = PerioSiteModel.Sweep();
        Assert.Equal(192, sweep.Count);
        Assert.Equal(192, sweep.Distinct().Count());
        Assert.Equal(ToothKeys.Permanent.SelectMany(t => PerioRules.Sites.Select(s => (t, s))).OrderBy(x => x).ToArray(), sweep.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void The_sweep_starts_at_the_upper_right_distal_cheek_side_and_snakes_back_along_the_tongue_side_before_the_lower_arch()
    {
        var sweep = PerioSiteModel.Sweep();
        Assert.Equal(("18", "DB"), sweep[0]);
        Assert.Equal(("11", "MB"), sweep[23]);                                   // right side, travelling toward the middle: distal, mid, mesial
        Assert.Equal(("21", "MB"), sweep[24]);                                   // left side: mesial, mid, distal
        Assert.Equal(("28", "DB"), sweep[47]);                                   // end of the cheek side of the upper arch
        Assert.Equal(("28", "DL"), sweep[48]);                                   // turn around: the tongue side, left to right
        Assert.Equal(("18", "DL"), sweep[95]);                                   // end of the upper arch
        Assert.Equal(("48", "DB"), sweep[96]);                                   // the lower arch starts the same way
        Assert.Equal(("18", "DL"), sweep[95]);
        Assert.Equal(("38", "DL"), sweep[144]);
        Assert.Equal(("48", "DL"), sweep[191]);
    }

    [Fact]
    public void The_sweep_is_the_same_order_as_the_clients_checksum_of_the_whole_sequence()
    {
        // the client (perioSiteModel.test.ts) asserts the same SHA-256, so the entry order on screen cannot drift from the order the server uses to say what comes next
        var text = string.Join(",", PerioSiteModel.Sweep().Select(x => $"{x.Tooth}{x.Site}"));
        Assert.Equal("c237982f1354edf763319b77e502ec67405691ab0d74f927c7f23f1a2a7d23bd", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant());
    }

    [Fact]
    public void Consecutive_sites_are_never_more_than_one_tooth_apart_so_the_hand_never_jumps_within_an_arch()
    {
        var order = PerioSiteModel.ArchOrder.ToList();
        var sweep = PerioSiteModel.Sweep();
        for (var i = 1; i < sweep.Count; i++)
        {
            var (a, b) = (order.IndexOf(sweep[i - 1].Tooth), order.IndexOf(sweep[i].Tooth));
            if (a / 16 != b / 16) continue;                                      // the single move from the upper arch to the lower
            Assert.True(Math.Abs(a - b) <= 1, $"{sweep[i - 1]} then {sweep[i]} skips a tooth");
        }
    }

    [Fact]
    public void Excluded_teeth_are_skipped_without_disturbing_the_order_of_the_rest()
    {
        var all = PerioSiteModel.Sweep();
        var skipped = PerioSiteModel.Sweep(new HashSet<string> { "16", "46" });
        Assert.Equal(180, skipped.Count);
        Assert.DoesNotContain(skipped, x => x.Tooth is "16" or "46");
        Assert.Equal(all.Where(x => x.Tooth is not ("16" or "46")).ToArray(), skipped.ToArray());      // the same sequence minus those teeth
        Assert.Empty(PerioSiteModel.Sweep(new HashSet<string>(ToothKeys.Permanent)));
    }

    [Fact]
    public void Arch_order_is_the_charts_row_order_upper_18_to_28_then_lower_48_to_38()
    {
        Assert.Equal(32, PerioSiteModel.ArchOrder.Count);
        Assert.Equal("18", PerioSiteModel.ArchOrder[0]);
        Assert.Equal("28", PerioSiteModel.ArchOrder[15]);
        Assert.Equal("48", PerioSiteModel.ArchOrder[16]);
        Assert.Equal("38", PerioSiteModel.ArchOrder[31]);
    }

    [Theory]
    [InlineData("16", true)] [InlineData("17", true)] [InlineData("18", true)] [InlineData("26", true)] [InlineData("36", true)] [InlineData("48", true)]
    [InlineData("14", true)] [InlineData("24", true)]
    [InlineData("15", false)] [InlineData("34", false)] [InlineData("44", false)] [InlineData("11", false)] [InlineData("13", false)] [InlineData("55", false)] [InlineData("99", false)]
    public void Only_molars_and_the_first_upper_premolars_can_have_a_furcation(string tooth, bool multiRooted) => Assert.Equal(multiRooted, PerioSiteModel.IsMultiRooted(tooth));
}
