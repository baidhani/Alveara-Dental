using Alveara.Api.Architecture.Prescribing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-016: the allergy check, with no database. It compares the name of the medication with the names of the patient's active recorded allergies, and it is honest about what that means: a match is found by
/// words, never by pharmacology; "no recorded match" is not a clearance and says so; and a list nobody has reviewed is reported as not established, never as clear.
/// </summary>
public class AllergyCheckTests
{
    private static RecordedAllergy Allergy(string name, string? severity = "Severe", string? reaction = "Hives") => new(name, severity, reaction);
    private static AllergyAssessment Assess(string medication, string state = AllergyListStates.Established, params RecordedAllergy[] allergies) => AllergyCheck.Assess(medication, allergies, state);

    // ---------- finding a recorded allergy ----------

    [Theory]
    [InlineData("Penicillin", "Penicillin")]                          // identical
    [InlineData("penicillin", "PENICILLIN")]                          // case
    [InlineData("Penicillin V Potassium", "Penicillin")]              // the medication says more than the allergy
    [InlineData("Amoxicillin 500 mg", "amoxicillin")]
    [InlineData("Penicillin", "Penicillin V Potassium tablets")]      // the allergy says more than the medication
    [InlineData("Penicillin-VK", "penicillin vk")]                    // punctuation
    [InlineData("Latex (natural rubber)", "Latex")]
    public void A_medication_whose_words_include_an_allergys_words_or_the_other_way_round_matches(string medication, string allergy)
    {
        var a = Assess(medication, AllergyListStates.Established, Allergy(allergy));
        Assert.Equal(AllergyOutcomes.RecordedAllergyMatch, a.Outcome);
        Assert.Equal(allergy, Assert.Single(a.Matches).Name);
    }

    [Theory]
    [InlineData("Penicillin G", "Penicillin V")]
    [InlineData("Ibuprofen", "Penicillin")]
    [InlineData("Amoxicillin", "Penicillin")]                         // a drug family: the check does not know it, and says so
    [InlineData("Augmentin", "Amoxicillin")]                          // a brand name: likewise
    [InlineData("Penicil", "Penicillin")]                             // a prefix is not a word
    public void A_medication_that_shares_no_complete_name_with_any_allergy_has_no_recorded_match(string medication, string allergy)
    {
        var a = Assess(medication, AllergyListStates.Established, Allergy(allergy));
        Assert.Equal(AllergyOutcomes.NoRecordedMatch, a.Outcome);
        Assert.Empty(a.Matches);
    }

    [Fact]
    public void Every_matching_allergy_is_listed_in_a_stable_order_with_its_severity_and_reaction()
    {
        var a = Assess("Penicillin V", AllergyListStates.Established, Allergy("Penicillin V", "Severe", "Anaphylaxis"), Allergy("penicillin", null, null), Allergy("Latex"));
        Assert.Equal(["penicillin", "Penicillin V"], a.Matches.Select(m => m.Name));
        Assert.Contains("severe", a.Message);
        Assert.Contains("reaction: Anaphylaxis", a.Message);
        Assert.Contains("severity not recorded", a.Message);                                                      // a missing severity is said, never guessed
        Assert.DoesNotContain("Latex", a.Message);
    }

    [Fact]
    public void A_match_is_reported_even_when_the_list_is_not_established_because_what_is_known_is_never_hidden()
    {
        foreach (var state in new[] { AllergyListStates.Unconfirmed, AllergyListStates.NotEstablished })
            Assert.Equal(AllergyOutcomes.RecordedAllergyMatch, Assess("Penicillin", state, Allergy("Penicillin")).Outcome);
    }

    [Fact]
    public void A_blank_or_punctuation_only_name_never_matches_anything()
    {
        Assert.Equal(AllergyOutcomes.NoRecordedMatch, Assess("Ibuprofen", AllergyListStates.Established, Allergy("  "), Allergy("---")).Outcome);
        Assert.Equal(AllergyOutcomes.NoRecordedMatch, Assess("-", AllergyListStates.Established, Allergy("Penicillin")).Outcome);
    }

    // ---------- what the check says when it cannot say anything ----------

    [Theory]
    [InlineData("Reviewed", AllergyListStates.Established)]
    [InlineData("NoneKnown", AllergyListStates.Established)]
    [InlineData("NeedsReview", AllergyListStates.Unconfirmed)]
    [InlineData("Unknown", AllergyListStates.NotEstablished)]
    [InlineData("NotReviewed", AllergyListStates.NotEstablished)]
    [InlineData("SomethingNew", AllergyListStates.NotEstablished)]
    [InlineData(null, AllergyListStates.NotEstablished)]
    public void A_clinical_record_status_maps_to_how_far_the_list_can_be_relied_on(string? status, string expected) => Assert.Equal(expected, AllergyListStates.From(status));

    [Fact]
    public void An_established_list_with_nothing_matching_is_no_recorded_match_and_is_not_called_a_clearance()
    {
        var a = Assess("Ibuprofen", AllergyListStates.Established);
        Assert.Equal(AllergyOutcomes.NoRecordedMatch, a.Outcome);
        Assert.Contains("not a clearance", a.Message);
    }

    [Theory]
    [InlineData(AllergyListStates.NotEstablished, "have not been reviewed")]
    [InlineData(AllergyListStates.Unconfirmed, "changed since it was last confirmed")]
    public void A_list_that_is_not_established_is_never_reported_as_clear_and_says_why(string state, string reason)
    {
        var a = Assess("Ibuprofen", state);
        Assert.Equal(AllergyOutcomes.NotEstablished, a.Outcome);
        Assert.Contains(reason, a.Message);
        Assert.Contains("Nothing listed here does not mean none", a.Message);
    }

    [Fact]
    public void The_limitation_travels_with_every_result_and_says_what_the_check_does_not_know()
    {
        foreach (var a in new[]
        {
            Assess("Penicillin", AllergyListStates.Established, Allergy("Penicillin")), Assess("Ibuprofen"), Assess("Ibuprofen", AllergyListStates.NotEstablished), Assess("Ibuprofen", AllergyListStates.Unconfirmed),
        })
            Assert.Equal(AllergyCheck.Limitation, a.Limitation);
        Assert.Contains("does not know drug families", AllergyCheck.Limitation);
        Assert.Contains("amoxicillin is a penicillin", AllergyCheck.Limitation);
        Assert.Contains("not a clearance", AllergyCheck.Limitation);
    }

    // ---------- the words of a name ----------

    [Fact]
    public void The_words_of_a_name_ignore_case_punctuation_and_repeats()
    {
        Assert.Equal(["penicillin", "v", "potassium"], AllergyCheck.Words("Penicillin-V,  POTASSIUM (Penicillin)"));
        Assert.Empty(AllergyCheck.Words("  --  "));
    }
}
