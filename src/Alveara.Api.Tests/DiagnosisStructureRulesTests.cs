using Alveara.Api.Architecture.Clinical;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-013-C01: the structured parts of a diagnosis entry (coding, source, region), on top of STORY-013's rules. No database. A diagnosis needs no coding; when it names a system it must give the code
/// and the other way round; an unknown system, source or region is refused by name; a code is never checked against a code set; a region and a tooth exclude each other; every problem is reported
/// together; and STORY-013's own entries still come out exactly as they did.
/// </summary>
public class DiagnosisStructureRulesTests
{
    private static readonly Guid Enc = Guid.NewGuid();

    private static DiagnosisInput In(string? system = null, string? code = null, string? source = null, string? note = null, string? region = null, string? tooth = null, string? plan = null)
        => new(Enc, "Chronic periodontitis", tooth, null, plan, system, code, source, note, region);

    private static string[] Codes(DiagnosisInput? i) => DiagnosisRules.Check(i).Problems.Select(p => $"{p.Field}:{p.Code}").ToArray();

    // ---------- stored structurally with no coding configured ----------

    [Fact]
    public void A_diagnosis_with_no_coding_source_or_region_is_accepted_and_is_manual()
    {
        var c = DiagnosisRules.Check(In());
        Assert.Empty(c.Problems);
        Assert.Null(c.Value!.CodingSystem);
        Assert.Null(c.Value.Code);
        Assert.Null(c.Value.RegionKey);
        Assert.Null(c.Value.SourceNote);
        Assert.Equal(DiagnosisSources.Manual, c.Value.Source);
    }

    [Fact]
    public void Stories_013_entries_come_out_exactly_as_before()
    {
        var c = DiagnosisRules.Check(new DiagnosisInput(Enc, "  Caries ", "16", " note ", " plan-1 "));
        Assert.Equal(new NormalizedDiagnosis(Enc, "Caries", "16", "note", "plan-1"), c.Value);
    }

    // ---------- coding ----------

    [Theory]
    [InlineData("ICD-10-CM", "K05.311")] [InlineData("SNODENT", "1234567")] [InlineData("Local", "PERIO_2")] [InlineData("Local", "a-b.c_9")]
    public void A_supported_system_with_a_code_survives_normalization_unchanged(string system, string code)
    {
        var c = DiagnosisRules.Check(In(system, code));
        Assert.Empty(c.Problems);
        Assert.Equal((system, code), (c.Value!.CodingSystem, c.Value.Code));
    }

    [Fact]
    public void The_code_is_trimmed_and_may_be_exactly_30_characters_and_not_31()
    {
        Assert.Equal("K05.3", DiagnosisRules.Check(In("ICD-10-CM", "  K05.3  ")).Value!.Code);
        Assert.Empty(Codes(In("Local", new string('A', 30))));
        Assert.Equal(["code:too_long"], Codes(In("Local", new string('A', 31))));
    }

    [Theory]
    [InlineData("icd-10-cm")] [InlineData("ICD10")] [InlineData("SNOMED")] [InlineData("")] [InlineData(" ")] [InlineData("Local ")]
    public void An_unknown_or_differently_spelled_coding_system_is_refused_by_name(string system)
        => Assert.Contains("codingSystem:unsupported_system", Codes(In(system, "X1")));

    [Fact]
    public void A_system_needs_its_code_and_a_code_needs_its_system()
    {
        Assert.Equal(["code:coding_incomplete"], Codes(In("ICD-10-CM", null)));
        Assert.Equal(["codingSystem:coding_incomplete"], Codes(In(null, "K05")));
    }

    [Theory]
    [InlineData("")] [InlineData("   ")]
    public void A_blank_code_is_refused_rather_than_read_as_no_code(string code)
        => Assert.Contains("code:blank", Codes(In("Local", code)));

    [Theory]
    [InlineData("K05 3")] [InlineData("K05/3")] [InlineData("K05,3")] [InlineData("é1")] [InlineData("K05\n3")]
    public void A_code_with_anything_but_letters_digits_dots_hyphens_and_underscores_is_refused(string code)
        => Assert.Contains(Codes(In("Local", code)), p => p is "code:invalid_characters");

    [Fact]
    public void A_code_is_never_checked_against_a_code_set()
    {
        // a well-formed code that names nothing is accepted: no terminology content is bundled or consulted
        Assert.Empty(Codes(In("ICD-10-CM", "ZZZ99.9")));
        Assert.Empty(Codes(In("SNODENT", "0")));
    }

    // ---------- source ----------

    [Theory]
    [InlineData("Manual")] [InlineData("Imported")] [InlineData("Mapped")]
    public void Every_known_source_is_accepted(string source) => Assert.Equal(source, DiagnosisRules.Check(In(source: source)).Value!.Source);

    [Theory]
    [InlineData("manual")] [InlineData("Unknown")] [InlineData("")] [InlineData(" ")]
    public void An_unknown_source_is_refused_and_a_blank_source_is_not_manual(string source) => Assert.Equal(["source:unsupported_source"], Codes(In(source: source)));

    [Fact]
    public void A_source_note_belongs_to_an_imported_or_mapped_diagnosis_only_and_is_normalized_and_bounded()
    {
        Assert.Equal("From the old chart", DiagnosisRules.Check(In(source: "Imported", note: "  From   the old chart ")).Value!.SourceNote);
        Assert.Empty(Codes(In(source: "Mapped", note: new string('n', 200))));
        Assert.Equal(["sourceNote:too_long"], Codes(In(source: "Mapped", note: new string('n', 201))));
        Assert.Equal(["sourceNote:source_required"], Codes(In(note: "from somewhere")));
        Assert.Equal(["sourceNote:source_required"], Codes(In(source: "Manual", note: "from somewhere")));
        Assert.Equal(["sourceNote:blank"], Codes(In(source: "Imported", note: "   ")));
        Assert.Equal(["sourceNote:invalid_characters"], Codes(In(source: "Imported", note: "two\nlines")));
    }

    // ---------- region ----------

    [Theory]
    [InlineData("FullMouth")] [InlineData("UpperArch")] [InlineData("LowerArch")] [InlineData("UpperRight")] [InlineData("UpperLeft")]
    [InlineData("LowerRight")] [InlineData("LowerLeft")] [InlineData("SoftTissue")] [InlineData("Tmj")]
    public void Every_known_region_is_accepted_without_a_tooth(string region) => Assert.Equal(region, DiagnosisRules.Check(In(region: region)).Value!.RegionKey);

    [Theory]
    [InlineData("fullmouth")] [InlineData("Mouth")] [InlineData("")] [InlineData(" ")]
    public void An_unknown_region_is_refused_and_a_blank_region_is_not_none(string region) => Assert.Equal(["regionKey:unknown_region"], Codes(In(region: region)));

    [Fact]
    public void A_diagnosis_is_about_a_tooth_or_a_region_not_both()
    {
        Assert.Equal(["regionKey:conflicts_with_tooth"], Codes(In(region: "UpperArch", tooth: "16")));
        Assert.Empty(Codes(In(tooth: "16")));
    }

    // ---------- the whole entry ----------

    [Fact]
    public void Every_structural_problem_is_reported_together_with_the_old_ones_and_nothing_is_accepted()
    {
        var c = DiagnosisRules.Check(new DiagnosisInput(Guid.Empty, "", "99", null, " ", "Nope", null, "Wrong", "x", "Nowhere"));
        Assert.Null(c.Value);
        var found = c.Problems.Select(p => $"{p.Field}:{p.Code}").ToArray();
        foreach (var expected in new[] { "encounterId:required", "label:required", "toothKey:unknown_tooth", "treatmentPlanReference:blank", "codingSystem:unsupported_system", "code:coding_incomplete", "source:unsupported_source", "regionKey:unknown_region" })
            Assert.Contains(expected, found);
    }

    [Fact]
    public void The_treatment_plan_reference_is_unaffected_by_the_structure_and_still_never_resolved()
    {
        var c = DiagnosisRules.Check(In("Local", "A1", "Mapped", "from import", "UpperArch", plan: "  plan-9  "));
        Assert.Empty(c.Problems);
        Assert.Equal("plan-9", c.Value!.TreatmentPlanReference);
    }
}
