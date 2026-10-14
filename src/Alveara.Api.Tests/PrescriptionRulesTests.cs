using Alveara.Api.Architecture.Prescribing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-016: the structural rules for a prescription entry, with no database. A valid entry is normalized to the canonical spellings; every wrong field is named in one answer; one problem means nothing is
/// accepted ("when saved, the system prompts for correction"); a dose that is too large to be anything but a typing slip is refused with a message that says it is not a clinical limit; and nothing here claims to
/// know whether a dose is clinically right.
/// </summary>
public class PrescriptionRulesTests
{
    private static PrescriptionInput Valid(string? medication = "Amoxicillin", decimal? dose = 500m, string? unit = "mg", string? frequency = "Three times daily", string? note = null, string? key = "rx-1") =>
        new(medication, dose, unit, frequency, note, key);

    private static IReadOnlyList<string> Fields(PrescriptionCheck c) => c.Problems.Select(p => p.Field).ToList();

    // ---------- the entry that is accepted ----------

    [Fact]
    public void A_complete_entry_is_accepted_and_normalized()
    {
        var c = PrescriptionRules.Check(Valid("  Amoxicillin   500  ", 500m, " MG ", "three TIMES daily", "  Patient reports no reaction in 2019.  ", " rx-1 "));
        Assert.Empty(c.Problems);
        Assert.Equal(new NormalizedPrescription("Amoxicillin 500", 500m, "mg", "Three times daily", "Patient reports no reaction in 2019.", "rx-1"), c.Value);
    }

    [Theory]
    [InlineData("ml", "mL")] [InlineData("MCG", "mcg")] [InlineData("Tablet", "tablet")] [InlineData("CAPSULE", "capsule")] [InlineData("g", "g")] [InlineData("drop", "drop")] [InlineData("PUFF", "puff")] [InlineData("Unit", "unit")]
    public void A_unit_is_accepted_in_any_case_and_stored_in_its_canonical_spelling(string typed, string canonical) =>
        Assert.Equal(canonical, PrescriptionRules.Check(Valid(unit: typed, dose: 1m)).Value!.DoseUnit);

    [Theory]
    [InlineData("Once")] [InlineData("Once daily")] [InlineData("Twice daily")] [InlineData("Three times daily")] [InlineData("Four times daily")] [InlineData("Every 4 hours")]
    [InlineData("Every 6 hours")] [InlineData("Every 8 hours")] [InlineData("Every 12 hours")] [InlineData("At bedtime")] [InlineData("As needed")]
    public void Every_listed_frequency_is_accepted(string frequency) => Assert.Equal(frequency, PrescriptionRules.Check(Valid(frequency: frequency.ToLowerInvariant())).Value!.Frequency);

    [Fact]
    public void A_medication_may_use_the_punctuation_a_drug_name_uses()
    {
        Assert.Empty(PrescriptionRules.Check(Valid("Amoxicillin/clavulanate (Augmentin) 875/125 mg, 5% w/v + O'Brien-Test.")).Problems);
        Assert.Empty(PrescriptionRules.Check(Valid("Chlorhexidine 0.12%")).Problems);
    }

    // ---------- the incorrect entry: every problem named at once ----------

    [Fact]
    public void An_empty_entry_names_every_required_field_at_once_and_nothing_is_accepted()
    {
        var c = PrescriptionRules.Check(new(null, null, null, null, null, null));
        Assert.Null(c.Value);
        Assert.Equal(["medication", "doseUnit", "doseValue", "frequency", "idempotencyKey"], Fields(c));
        Assert.All(c.Problems, p => Assert.Equal("required", p.Code));
    }

    [Fact]
    public void A_missing_entry_is_a_single_clear_problem()
    {
        var c = PrescriptionRules.Check(null);
        Assert.Null(c.Value);
        Assert.Equal("prescription", Assert.Single(c.Problems).Field);
    }

    [Fact]
    public void Several_wrong_fields_are_reported_together()
    {
        var c = PrescriptionRules.Check(Valid("A", -5m, "stone", "whenever"));
        Assert.Equal(["medication", "doseUnit", "doseValue", "frequency"], Fields(c));
        Assert.Equal(["too_short", "unknown_unit", "not_positive", "unknown_frequency"], c.Problems.Select(p => p.Code));
    }

    // ---------- the dose: incorrect dosage is not accepted ----------

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(-0.001)]
    public void A_dose_that_is_not_more_than_zero_is_refused(double dose)
    {
        var c = PrescriptionRules.Check(Valid(dose: (decimal)dose));
        Assert.Equal("not_positive", Assert.Single(c.Problems).Code);
        Assert.Null(c.Value);
    }

    [Fact]
    public void A_dose_with_more_than_three_decimal_places_is_refused_and_exactly_three_is_accepted()
    {
        Assert.Equal("too_many_decimals", Assert.Single(PrescriptionRules.Check(Valid(dose: 0.0001m, unit: "mg")).Problems).Code);
        Assert.Equal("too_many_decimals", Assert.Single(PrescriptionRules.Check(Valid(dose: 1.2345m)).Problems).Code);
        Assert.Empty(PrescriptionRules.Check(Valid(dose: 0.001m)).Problems);
        Assert.Empty(PrescriptionRules.Check(Valid(dose: 12.345m)).Problems);
    }

    [Theory]
    [InlineData("mg", 5000, 5001)] [InlineData("mcg", 5000, 5001)] [InlineData("g", 10, 10.001)] [InlineData("mL", 100, 100.5)] [InlineData("tablet", 10, 11)]
    [InlineData("capsule", 10, 11)] [InlineData("drop", 20, 21)] [InlineData("puff", 20, 21)] [InlineData("unit", 1000, 1001)]
    public void A_dose_above_the_typing_ceiling_for_its_unit_is_refused_and_one_at_the_ceiling_is_accepted(string unit, double ceiling, double above)
    {
        Assert.Empty(PrescriptionRules.Check(Valid(dose: (decimal)ceiling, unit: unit)).Problems);
        var refused = PrescriptionRules.Check(Valid(dose: (decimal)above, unit: unit));
        var problem = Assert.Single(refused.Problems);
        Assert.Equal(("doseValue", "above_typing_ceiling"), (problem.Field, problem.Code));
        Assert.Contains("not a clinical dose limit", problem.Message);                                         // the refusal says what the ceiling is and is not
        Assert.Null(refused.Value);
    }

    [Fact]
    public void Every_unit_has_a_ceiling_and_the_ceilings_are_the_documented_ones()
    {
        Assert.Equal(["mg", "mcg", "g", "mL", "tablet", "capsule", "drop", "puff", "unit"], PrescriptionRules.DoseUnits);
        Assert.Equal([5000m, 5000m, 10m, 100m, 10m, 10m, 20m, 20m, 1000m], PrescriptionRules.DoseUnits.Select(u => PrescriptionRules.TypingCeilings[u]));
    }

    [Fact]
    public void A_wrong_unit_does_not_hide_a_wrong_number_and_a_wrong_number_is_still_judged_when_the_unit_is_unknown()
    {
        Assert.Equal(["doseUnit"], Fields(PrescriptionRules.Check(Valid(unit: "stone", dose: 99999m))));          // no ceiling can be applied without a unit; the unit is the problem to fix first
        Assert.Equal(["doseUnit", "doseValue"], Fields(PrescriptionRules.Check(Valid(unit: "stone", dose: 0m))));
    }

    [Fact]
    public void The_dose_check_says_in_words_that_it_is_not_a_clinical_check()
    {
        Assert.Contains("not checked against any clinical dose range", PrescriptionRules.DoseCheckLimitation);
        Assert.Contains("no drug-knowledge source", PrescriptionRules.DoseCheckLimitation);
    }

    // ---------- the medication, the frequency, the note and the key ----------

    [Theory]
    [InlineData("   ")] [InlineData("")] [InlineData(null)]
    public void A_blank_medication_is_required(string? medication) => Assert.Equal("required", Assert.Single(PrescriptionRules.Check(Valid(medication)).Problems).Code);

    [Fact]
    public void A_medication_must_be_one_line_of_letters_digits_and_drug_punctuation_and_bounded()
    {
        Assert.Equal("invalid_characters", Assert.Single(PrescriptionRules.Check(Valid("Amox\nicillin")).Problems).Code);
        Assert.Equal("invalid_characters", Assert.Single(PrescriptionRules.Check(Valid("Amox\u0007icillin")).Problems).Code);
        Assert.Equal("invalid_characters", Assert.Single(PrescriptionRules.Check(Valid("Drop; DROP TABLE x")).Problems).Code);
        Assert.Equal("invalid_characters", Assert.Single(PrescriptionRules.Check(Valid("<script>")).Problems).Code);
        Assert.Equal("too_short", Assert.Single(PrescriptionRules.Check(Valid("A")).Problems).Code);
        Assert.Empty(PrescriptionRules.Check(Valid("Ab")).Problems);
        Assert.Empty(PrescriptionRules.Check(Valid(new string('a', 200))).Problems);
        Assert.Equal("too_long", Assert.Single(PrescriptionRules.Check(Valid(new string('a', 201))).Problems).Code);
    }

    [Fact]
    public void An_unknown_frequency_is_refused_by_naming_the_ones_that_exist()
    {
        var p = Assert.Single(PrescriptionRules.Check(Valid(frequency: "now and then")).Problems);
        Assert.Equal(("frequency", "unknown_frequency"), (p.Field, p.Code));
        Assert.Contains("Every 8 hours", p.Message);
    }

    [Fact]
    public void A_review_note_is_optional_trimmed_bounded_and_may_span_lines()
    {
        Assert.Null(PrescriptionRules.Check(Valid(note: "   ")).Value!.ReviewNote);
        Assert.Equal("a\nb", PrescriptionRules.Check(Valid(note: " a\r\nb ")).Value!.ReviewNote);
        Assert.Empty(PrescriptionRules.Check(Valid(note: new string('n', 500))).Problems);
        Assert.Equal("too_long", Assert.Single(PrescriptionRules.Check(Valid(note: new string('n', 501))).Problems).Code);
        Assert.Equal("invalid_characters", Assert.Single(PrescriptionRules.Check(Valid(note: "a\u0007b")).Problems).Code);
    }

    [Fact]
    public void The_save_key_is_required_bounded_and_free_of_control_characters()
    {
        Assert.Equal("required", Assert.Single(PrescriptionRules.Check(Valid(key: "  ")).Problems).Code);
        Assert.Empty(PrescriptionRules.Check(Valid(key: new string('k', 64))).Problems);
        Assert.Equal("invalid", Assert.Single(PrescriptionRules.Check(Valid(key: new string('k', 65))).Problems).Code);
        Assert.Equal("invalid", Assert.Single(PrescriptionRules.Check(Valid(key: "k\u0001")).Problems).Code);
    }

    // ---------- the request identity ----------

    [Fact]
    public void The_same_entry_has_the_same_hash_whatever_its_spelling_case_or_spacing_and_whatever_its_key()
    {
        var a = PrescriptionRules.RequestHash(PrescriptionRules.Check(Valid("Amoxicillin 500", key: "one")).Value!);
        var b = PrescriptionRules.RequestHash(PrescriptionRules.Check(Valid("  amoxicillin    500 ", unit: "MG", frequency: "THREE TIMES DAILY", key: "two")).Value!);
        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Theory]
    [InlineData("medication")] [InlineData("dose")] [InlineData("unit")] [InlineData("frequency")] [InlineData("note")]
    public void Changing_anything_that_makes_the_request_different_changes_the_hash(string changed)
    {
        var baseline = PrescriptionRules.RequestHash(PrescriptionRules.Check(Valid()).Value!);
        var other = changed switch
        {
            "medication" => Valid("Ibuprofen"),
            "dose" => Valid(dose: 250m),
            "unit" => Valid(dose: 500m, unit: "mcg"),
            "frequency" => Valid(frequency: "Twice daily"),
            _ => Valid(note: "Reviewed with the patient."),
        };
        Assert.NotEqual(baseline, PrescriptionRules.RequestHash(PrescriptionRules.Check(other).Value!));
    }

    [Fact]
    public void A_dose_hashes_the_same_whatever_trailing_zeros_it_was_typed_with() =>
        Assert.Equal(PrescriptionRules.RequestHash(PrescriptionRules.Check(Valid(dose: 500m)).Value!), PrescriptionRules.RequestHash(PrescriptionRules.Check(Valid(dose: 500.000m)).Value!));

    // ---------- the review the allergy check can require ----------

    private static AllergyAssessment Assessment(string outcome) =>
        new(outcome, [], outcome == AllergyOutcomes.NoRecordedMatch ? AllergyListStates.Established : AllergyListStates.NotEstablished, "m", AllergyCheck.Limitation);

    [Fact]
    public void No_review_is_asked_for_when_there_is_no_recorded_match_and_the_list_is_established() =>
        Assert.Null(PrescriptionRules.ReviewProblem(Assessment(AllergyOutcomes.NoRecordedMatch), null));

    [Theory]
    [InlineData(AllergyOutcomes.RecordedAllergyMatch, "allergy_review_required")]
    [InlineData(AllergyOutcomes.NotEstablished, "allergy_status_review_required")]
    public void A_match_or_an_unestablished_list_needs_the_prescribers_own_words_and_only_then_proceeds(string outcome, string code)
    {
        var problem = PrescriptionRules.ReviewProblem(Assessment(outcome), null)!;
        Assert.Equal(("reviewNote", code), (problem.Field, problem.Code));
        Assert.Null(PrescriptionRules.ReviewProblem(Assessment(outcome), "Allergy was a childhood rash; patient tolerated it since; I discussed it."));
    }
}
