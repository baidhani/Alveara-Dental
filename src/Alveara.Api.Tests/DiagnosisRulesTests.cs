using Alveara.Api.Architecture.Clinical;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-013: the rules for a valid structured diagnosis entry, including the treatment-plan forward reference. No database. Happy path, every refusal, the boundaries, and that normalizing twice changes nothing.</summary>
public class DiagnosisRulesTests
{
    private static readonly Guid Enc = Guid.NewGuid();
    private static DiagnosisInput In(string? label = "Chronic periodontitis", string? tooth = null, string? notes = null, string? plan = null, Guid? encounter = null) => new(encounter ?? Enc, label, tooth, notes, plan);
    private static string[] Codes(DiagnosisInput? i) => DiagnosisRules.Check(i).Problems.Select(p => $"{p.Field}:{p.Code}").ToArray();

    // ---------- the happy path ----------

    [Fact]
    public void A_complete_entry_is_accepted_and_comes_back_exactly_as_it_would_be_stored()
    {
        var c = DiagnosisRules.Check(In("  Caries   on the occlusal surface ", "16", "Sensitive to cold.\r\nReview in 2 weeks.  ", "  plan-2026-07  "));
        Assert.Empty(c.Problems);
        Assert.Equal(new NormalizedDiagnosis(Enc, "Caries on the occlusal surface", "16", "Sensitive to cold.\nReview in 2 weeks.", "plan-2026-07"), c.Value);
    }

    [Fact]
    public void Only_the_label_and_the_encounter_are_required_and_everything_else_may_be_left_out()
    {
        var c = DiagnosisRules.Check(In());
        Assert.Empty(c.Problems);
        Assert.Equal(new NormalizedDiagnosis(Enc, "Chronic periodontitis", null, null, null), c.Value);
    }

    [Theory]
    [InlineData("11")] [InlineData("48")] [InlineData("55")] [InlineData("85")]
    public void A_tooth_can_be_any_of_the_52_fdi_keys_permanent_or_primary(string tooth) => Assert.Empty(Codes(In(tooth: tooth)));

    // ---------- the label ----------

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData(" ")]
    public void A_missing_or_blank_label_is_refused(string? label) => Assert.Equal(["label:required"], Codes(In(label)));

    [Fact]
    public void The_label_may_be_exactly_200_characters_and_not_201()
    {
        Assert.Empty(Codes(In(new string('a', 200))));
        Assert.Equal(["label:too_long"], Codes(In(new string('a', 201))));
        Assert.Empty(Codes(In("  " + new string('a', 200) + "  ")));                     // the padding is trimmed before it is measured
        Assert.Empty(Codes(In("a" + new string(' ', 300) + "b")));                         // the length is measured after runs of spaces collapse, so this is "a b"
        Assert.Equal(["label:too_long"], Codes(In(string.Join(" ", Enumerable.Repeat("ab", 100)))));   // 299 characters with single spaces cannot collapse further
    }

    [Theory]
    [InlineData("line one\nline two")] [InlineData("tab\there")] [InlineData("bell\a")] [InlineData("null\0char")]
    public void A_label_must_be_one_line_without_control_characters(string label) => Assert.Equal(["label:invalid_characters"], Codes(In(label)));

    // ---------- the tooth ----------

    [Theory]
    [InlineData("")] [InlineData(" ")] [InlineData("19")] [InlineData("1")] [InlineData("111")] [InlineData(" 16")] [InlineData("abc")] [InlineData("56")] [InlineData("90")]
    public void A_tooth_that_is_not_an_fdi_key_is_refused_and_a_blank_tooth_is_not_the_same_as_none(string tooth) => Assert.Equal(["toothKey:unknown_tooth"], Codes(In(tooth: tooth)));

    // ---------- the notes ----------

    [Fact]
    public void Notes_may_span_lines_may_be_exactly_1000_characters_and_blank_notes_are_simply_no_notes()
    {
        Assert.Equal("a\nb\nc", DiagnosisRules.Check(In(notes: "a\r\nb\rc")).Value!.Notes);
        Assert.Empty(Codes(In(notes: new string('n', 1000))));
        Assert.Equal(["notes:too_long"], Codes(In(notes: new string('n', 1001))));
        Assert.Null(DiagnosisRules.Check(In(notes: "  \n  ")).Value!.Notes);
    }

    [Theory]
    [InlineData("bell\a")] [InlineData("null\0char")] [InlineData("escape\u001b")]
    public void Notes_refuse_control_characters_other_than_line_breaks_and_tabs(string notes) => Assert.Equal(["notes:invalid_characters"], Codes(In(notes: notes)));

    // ---------- the treatment-plan forward reference ----------

    [Fact]
    public void No_reference_is_fine_and_means_no_reference()
    {
        Assert.Null(DiagnosisRules.Check(In()).Value!.TreatmentPlanReference);
        Assert.Null(DiagnosisRules.Check(In(plan: null)).Value!.TreatmentPlanReference);
    }

    [Theory]
    [InlineData("plan-7", "plan-7")] [InlineData("  plan-7  ", "plan-7")] [InlineData("Plan   7", "Plan 7")] [InlineData("plan 7", "plan 7")] [InlineData("PLAN-7", "PLAN-7")]
    public void A_reference_is_trimmed_runs_of_spaces_are_collapsed_and_case_is_kept(string given, string stored)
        => Assert.Equal(stored, DiagnosisRules.Check(In(plan: given)).Value!.TreatmentPlanReference);

    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("  ")]
    public void A_reference_that_is_present_but_blank_is_refused_not_silently_dropped(string plan) => Assert.Equal(["treatmentPlanReference:blank"], Codes(In(plan: plan)));

    [Fact]
    public void A_reference_may_be_exactly_100_characters_and_not_101()
    {
        Assert.Empty(Codes(In(plan: new string('p', 100))));
        Assert.Equal(["treatmentPlanReference:too_long"], Codes(In(plan: new string('p', 101))));
        Assert.Empty(Codes(In(plan: "   " + new string('p', 100) + "   ")));
    }

    [Theory]
    [InlineData("plan\n7")] [InlineData("plan\t7")] [InlineData("plan\r7")] [InlineData("plan\07")]
    public void A_reference_refuses_line_breaks_and_control_characters(string plan) => Assert.Equal(["treatmentPlanReference:invalid_characters"], Codes(In(plan: plan)));

    [Fact]
    public void A_reference_is_never_treated_as_proof_of_anything_whatever_it_says()
    {
        // it is opaque text: a value that looks like an identifier, a url or a sentence is stored as given (normalized), with no lookup and no judgement
        foreach (var text in new[] { "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "https://example.test/plans/1", "the plan Dr Okafor discussed", "ABC-123" })
            Assert.Equal(DiagnosisRules.NormalizeLine(text), DiagnosisRules.Check(In(plan: text)).Value!.TreatmentPlanReference);
    }

    // ---------- the encounter, and everything together ----------

    [Fact]
    public void An_entry_with_no_encounter_or_no_entry_at_all_is_refused()
    {
        Assert.Equal(["encounterId:required"], Codes(In(encounter: Guid.Empty)));
        Assert.Equal(["diagnosis:required"], Codes(null));
    }

    [Fact]
    public void Every_problem_is_reported_together_so_nothing_is_corrected_one_at_a_time()
    {
        var c = DiagnosisRules.Check(In(label: "", tooth: "19", notes: "bell\a", plan: "   ", encounter: Guid.Empty));
        Assert.Equal(["encounterId:required", "label:required", "toothKey:unknown_tooth", "notes:invalid_characters", "treatmentPlanReference:blank"], c.Problems.Select(p => $"{p.Field}:{p.Code}").ToArray());
        Assert.Null(c.Value);                                                                        // nothing partial is ever handed back
    }

    [Fact]
    public void Each_problem_says_what_to_correct()
        => Assert.All(DiagnosisRules.Check(In(label: new string('a', 201), tooth: "19", plan: new string('p', 101))).Problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Message)));

    // ---------- normalizing twice changes nothing ----------

    [Theory]
    [InlineData("  a   b  ")] [InlineData("plan  7")] [InlineData("x")] [InlineData("a b")]
    public void Normalizing_a_line_twice_gives_the_same_result_so_a_saved_value_can_be_saved_again_unchanged(string text)
    {
        var once = DiagnosisRules.NormalizeLine(text);
        Assert.Equal(once, DiagnosisRules.NormalizeLine(once));
    }

    [Fact]
    public void A_checked_entry_checked_again_is_accepted_and_unchanged()
    {
        var first = DiagnosisRules.Check(In("  Gingivitis ", "36", " note \r\n more ", "  plan   9 ")).Value!;
        var again = DiagnosisRules.Check(new DiagnosisInput(first.EncounterId, first.Label, first.ToothKey, first.Notes, first.TreatmentPlanReference)).Value!;
        Assert.Equal(first, again);
    }
}
