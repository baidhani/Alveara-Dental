using Alveara.Api.Architecture.Prescribing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-016-C01, the workflow rules on their own (no database): what a draft may hold, what finalizing needs on top, the typing-error ceilings of the quantity and the refills, the request hash, the status a
/// prescription has after its events, and the safety snapshot a prescriber's view is reduced to (so a changed allergy or medication list can be detected and described).
/// </summary>
public class PrescriptionWorkflowRulesTests
{
    private static WorkflowInput Complete(Func<WorkflowInput, WorkflowInput>? change = null)
    {
        var input = new WorkflowInput("Amoxicillin", 500m, "mg", "Three times daily", "Take one capsule with food until finished.", 21m, "capsule", 0, null, "key-1");
        return change?.Invoke(input) ?? input;
    }

    private static IReadOnlyList<(string Field, string Code)> Problems(WorkflowCheck c) => c.Problems.Select(p => (p.Field, p.Code)).ToList();

    // ---------- a draft ----------

    [Fact]
    public void A_draft_needs_only_the_core_entry_and_may_leave_the_instructions_quantity_and_refills_empty()
    {
        var c = PrescriptionWorkflowRules.CheckDraft(new("Amoxicillin", 500m, "mg", "Three times daily", null, null, null, null, null, "key-1"));
        Assert.Empty(c.Problems);
        Assert.Equal(("Amoxicillin", 500m, "mg", "Three times daily", null, null, null, null), (c.Value!.Medication, c.Value.DoseValue, c.Value.DoseUnit, c.Value.Frequency, c.Value.Instructions, c.Value.QuantityValue, c.Value.QuantityUnit, c.Value.Refills));
        Assert.False(PrescriptionWorkflowRules.IsComplete(c.Value));
    }

    [Fact]
    public void A_draft_is_normalized_the_same_way_as_a_prescription_and_the_instructions_are_trimmed_with_line_breaks_made_uniform()
    {
        var c = PrescriptionWorkflowRules.CheckDraft(Complete(i => i with { Medication = "  amoxicillin   ", DoseUnit = "MG", Frequency = "three times daily", Instructions = "  First line.\r\nSecond line.\r  ", QuantityUnit = "CAPSULE" }));
        Assert.Empty(c.Problems);
        Assert.Equal(("amoxicillin", "mg", "Three times daily", "First line.\nSecond line.", "capsule"), (c.Value!.Medication, c.Value.DoseUnit, c.Value.Frequency, c.Value.Instructions, c.Value.QuantityUnit));
    }

    [Fact]
    public void A_missing_entry_and_a_bad_core_field_are_refused_by_the_story_016_rules_unchanged()
    {
        Assert.Equal([("prescription", "required")], Problems(PrescriptionWorkflowRules.CheckDraft(null)));
        var c = PrescriptionWorkflowRules.CheckDraft(Complete(i => i with { DoseValue = 6000m, Frequency = "now and then" }));
        Assert.Equal([("doseValue", "above_typing_ceiling"), ("frequency", "unknown_frequency")], Problems(c));
        Assert.Null(c.Value);
    }

    [Fact]
    public void Every_problem_is_reported_together_the_core_ones_and_the_new_ones()
    {
        var c = PrescriptionWorkflowRules.CheckDraft(new("A", 0m, "stone", "whenever", new string('x', 501), -5m, "box", 99, new string('n', 501), " "));
        Assert.Equal(
            [("medication", "too_short"), ("doseUnit", "unknown_unit"), ("doseValue", "not_positive"), ("frequency", "unknown_frequency"), ("reviewNote", "too_long"), ("idempotencyKey", "required"),
             ("instructions", "too_long"), ("quantityUnit", "unknown_unit"), ("quantityValue", "not_positive"), ("refills", "above_typing_ceiling")], Problems(c));
        Assert.Null(c.Value);
    }

    [Theory]
    [InlineData("quantityZero", "quantityValue", "not_positive")]
    [InlineData("quantityNegative", "quantityValue", "not_positive")]
    [InlineData("quantityThreeDecimals", "quantityValue", "too_many_decimals")]
    [InlineData("quantityAboveCeiling", "quantityValue", "above_typing_ceiling")]
    [InlineData("quantityUnknownUnit", "quantityUnit", "unknown_unit")]
    [InlineData("quantityWithoutUnit", "quantityUnit", "required")]
    [InlineData("unitWithoutQuantity", "quantityValue", "required")]
    [InlineData("refillsNegative", "refills", "negative")]
    [InlineData("refillsAboveCeiling", "refills", "above_typing_ceiling")]
    [InlineData("instructionsTooLong", "instructions", "too_long")]
    [InlineData("instructionsControlCharacter", "instructions", "invalid_characters")]
    public void Each_new_field_is_refused_for_the_right_reason_and_nothing_is_accepted(string which, string field, string code)
    {
        var c = PrescriptionWorkflowRules.CheckDraft(Complete(i => which switch
        {
            "quantityZero" => i with { QuantityValue = 0m },
            "quantityNegative" => i with { QuantityValue = -1m },
            "quantityThreeDecimals" => i with { QuantityValue = 1.005m },
            "quantityAboveCeiling" => i with { QuantityValue = 1000.01m },
            "quantityUnknownUnit" => i with { QuantityUnit = "box" },
            "quantityWithoutUnit" => i with { QuantityUnit = null },
            "unitWithoutQuantity" => i with { QuantityValue = null },
            "refillsNegative" => i with { Refills = -1 },
            "refillsAboveCeiling" => i with { Refills = 13 },
            "instructionsTooLong" => i with { Instructions = new string('x', 501) },
            "instructionsControlCharacter" => i with { Instructions = "Take\u0007 one" },
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        }));
        Assert.Equal([(field, code)], Problems(c));
        Assert.Null(c.Value);
    }

    [Theory]
    [InlineData("quantityAtCeiling")] [InlineData("quantitySmallest")] [InlineData("refillsNone")] [InlineData("refillsAtCeiling")] [InlineData("instructionsAtLimit")]
    public void The_boundaries_themselves_are_accepted(string which)
    {
        var c = PrescriptionWorkflowRules.CheckDraft(Complete(i => which switch
        {
            "quantityAtCeiling" => i with { QuantityValue = 1000m },
            "quantitySmallest" => i with { QuantityValue = 0.01m },
            "refillsNone" => i with { Refills = 0 },
            "refillsAtCeiling" => i with { Refills = 12 },
            "instructionsAtLimit" => i with { Instructions = new string('x', 500) },
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        }));
        Assert.Empty(c.Problems);
        Assert.NotNull(c.Value);
    }

    [Fact]
    public void The_limits_are_described_as_typing_ceilings_and_not_as_clinical_or_legal_limits()
    {
        Assert.Contains("not a clinical or legal limit", PrescriptionWorkflowRules.CheckDraft(Complete(i => i with { QuantityValue = 5000m })).Problems.Single().Message);
        Assert.Contains("not a legal limit", PrescriptionWorkflowRules.CheckDraft(Complete(i => i with { Refills = 40 })).Problems.Single().Message);
        Assert.Contains("no drug-knowledge source", PrescriptionWorkflowRules.QuantityLimitation);
        Assert.Contains("controlled-substance", PrescriptionWorkflowRules.QuantityLimitation);
    }

    // ---------- finalizing ----------

    [Fact]
    public void A_complete_entry_can_be_finalized_and_is_complete()
    {
        var c = PrescriptionWorkflowRules.CheckFinal(Complete());
        Assert.Empty(c.Problems);
        Assert.True(PrescriptionWorkflowRules.IsComplete(c.Value!));
        Assert.Equal(("Amoxicillin", 500m, "mg", "Three times daily", "key-1"), (PrescriptionWorkflowRules.CoreOf(c.Value!).Medication, PrescriptionWorkflowRules.CoreOf(c.Value!).DoseValue, PrescriptionWorkflowRules.CoreOf(c.Value!).DoseUnit, PrescriptionWorkflowRules.CoreOf(c.Value!).Frequency, PrescriptionWorkflowRules.CoreOf(c.Value!).IdempotencyKey));
    }

    [Fact]
    public void Finalizing_names_every_missing_part_once()
    {
        var c = PrescriptionWorkflowRules.CheckFinal(Complete(i => i with { Instructions = null, QuantityValue = null, QuantityUnit = null, Refills = null }));
        Assert.Equal([("instructions", "required"), ("quantityValue", "required"), ("quantityUnit", "required"), ("refills", "required")], Problems(c));
        Assert.Null(c.Value);
    }

    [Theory]
    [InlineData("instructions")] [InlineData("quantity")] [InlineData("refills")]
    public void Finalizing_refuses_each_missing_part_on_its_own(string missing)
    {
        var c = PrescriptionWorkflowRules.CheckFinal(Complete(i => missing switch
        {
            "instructions" => i with { Instructions = "   " },
            "quantity" => i with { QuantityValue = null, QuantityUnit = null },
            "refills" => i with { Refills = null },
            _ => throw new ArgumentOutOfRangeException(nameof(missing)),
        }));
        Assert.NotEmpty(c.Problems);
        Assert.Null(c.Value);
        Assert.Equal(c.Problems.Count, c.Problems.Select(p => (p.Field, p.Code)).Distinct().Count());
    }

    [Fact]
    public void Finalizing_keeps_the_draft_problems_and_does_not_repeat_them()
    {
        var c = PrescriptionWorkflowRules.CheckFinal(Complete(i => i with { DoseValue = 0m, QuantityValue = null, QuantityUnit = null }));
        Assert.Equal([("doseValue", "not_positive"), ("quantityValue", "required"), ("quantityUnit", "required")], Problems(c));
    }

    [Fact]
    public void Finalizing_a_missing_entry_is_refused()
    {
        Assert.Equal([("prescription", "required")], Problems(PrescriptionWorkflowRules.CheckFinal(null)));
    }

    // ---------- the request hash ----------

    [Fact]
    public void The_hash_binds_every_field_except_the_key_and_ignores_the_case_of_the_medication()
    {
        string H(Func<WorkflowInput, WorkflowInput> change) => PrescriptionWorkflowRules.RequestHash(PrescriptionWorkflowRules.CheckDraft(Complete(change)).Value!);
        var baseline = H(i => i);
        Assert.Equal(baseline, H(i => i with { IdempotencyKey = "another-key", Medication = "AMOXICILLIN", Instructions = "  Take one capsule with food until finished.  " }));
        Assert.Matches("^[0-9a-f]{64}$", baseline);
        foreach (var changed in new Func<WorkflowInput, WorkflowInput>[]
        {
            i => i with { Medication = "Naproxen" }, i => i with { DoseValue = 250m }, i => i with { DoseUnit = "mcg" }, i => i with { Frequency = "Twice daily" }, i => i with { Instructions = "Other." },
            i => i with { QuantityValue = 14m }, i => i with { QuantityUnit = "tablet" }, i => i with { Refills = 1 }, i => i with { ReviewNote = "A note." },
        })
            Assert.NotEqual(baseline, H(changed));
    }

    [Fact]
    public void The_amendment_hash_names_the_prescription_being_amended_and_nothing_else()
    {
        var one = Guid.NewGuid();
        Assert.Matches("^[0-9a-f]{64}$", PrescriptionWorkflowRules.AmendHash(one));
        Assert.Equal(PrescriptionWorkflowRules.AmendHash(one), PrescriptionWorkflowRules.AmendHash(one));
        Assert.NotEqual(PrescriptionWorkflowRules.AmendHash(one), PrescriptionWorkflowRules.AmendHash(Guid.NewGuid()));
        Assert.NotEqual(PrescriptionWorkflowRules.AmendHash(one), PrescriptionWorkflowRules.RequestHash(PrescriptionWorkflowRules.CheckDraft(Complete()).Value!));   // an amendment key can never replay as a new draft, nor the reverse
    }

    // ---------- the status of a prescription ----------

    [Fact]
    public void A_prescription_with_no_ending_event_is_active_and_the_ending_event_decides_otherwise()
    {
        Assert.Equal(PrescriptionStatuses.Active, PrescriptionStatuses.From([]));                                                               // saved by STORY-016: no events
        Assert.Equal(PrescriptionStatuses.Active, PrescriptionStatuses.From([PrescriptionEventTypes.Finalized]));
        Assert.Equal(PrescriptionStatuses.Cancelled, PrescriptionStatuses.From([PrescriptionEventTypes.Finalized, PrescriptionEventTypes.Cancelled]));
        Assert.Equal(PrescriptionStatuses.Discontinued, PrescriptionStatuses.From([PrescriptionEventTypes.Finalized, PrescriptionEventTypes.Discontinued]));
        Assert.Equal(PrescriptionStatuses.Superseded, PrescriptionStatuses.From([PrescriptionEventTypes.Finalized, PrescriptionEventTypes.Superseded]));
    }

    [Fact]
    public void Only_an_active_prescription_can_end()
    {
        Assert.True(PrescriptionStatuses.CanEnd(PrescriptionStatuses.Active));
        Assert.All(new[] { PrescriptionStatuses.Cancelled, PrescriptionStatuses.Discontinued, PrescriptionStatuses.Superseded }, s => Assert.False(PrescriptionStatuses.CanEnd(s)));
    }

    [Theory]
    [InlineData(null, "required")] [InlineData("   ", "required")] [InlineData("Bad\u0007reason", "invalid_characters")]
    public void A_reason_is_required_and_must_be_plain_text(string? reason, string code)
    {
        var (value, problem) = PrescriptionWorkflowRules.CheckReason(reason);
        Assert.Null(value);
        Assert.Equal(("reason", code), (problem!.Field, problem.Code));
    }

    [Fact]
    public void A_reason_is_trimmed_and_limited_in_length()
    {
        var (reason, problem) = PrescriptionWorkflowRules.CheckReason("  Patient reacted to the first dose.  ");
        Assert.Equal("Patient reacted to the first dose.", reason);
        Assert.Null(problem);
        Assert.NotNull(PrescriptionWorkflowRules.CheckReason(new string('x', 500)).Reason);
        Assert.Equal("too_long", PrescriptionWorkflowRules.CheckReason(new string('x', 501)).Problem!.Code);
    }

    // ---------- the safety snapshot ----------

    private static readonly SnapshotItem Penicillin = new("Allergy", "Penicillin", "High", "Hives", "Active");
    private static readonly SnapshotItem Latex = new("Allergy", "Latex", "Low", "Rash", "Active");
    private static readonly SnapshotItem Warfarin = new("Medication", "Warfarin", null, "5 mg daily", "Active");

    [Fact]
    public void The_snapshot_is_the_same_whatever_order_the_items_arrive_in_and_has_one_line_per_item_and_per_list()
    {
        var a = SafetySnapshot.Lines([Penicillin, Latex, Warfarin], "Reviewed", "Reviewed");
        var b = SafetySnapshot.Lines([Warfarin, Latex, Penicillin], "Reviewed", "Reviewed");
        Assert.Equal(a, b);
        Assert.Equal(5, a.Count);
        Assert.Equal(SafetySnapshot.Fingerprint(a), SafetySnapshot.Fingerprint(b));
        Assert.Matches("^[0-9a-f]{64}$", SafetySnapshot.Fingerprint(a));
    }

    [Theory]
    [InlineData("anAllergyIsAdded")] [InlineData("anAllergyIsRemoved")] [InlineData("aSeverityChanges")] [InlineData("aReactionChanges")] [InlineData("aMedicationChanges")]
    [InlineData("theAllergyListNeedsReview")] [InlineData("theMedicationListNeedsReview")]
    public void Any_change_to_an_allergy_a_medication_or_how_far_a_list_can_be_relied_on_changes_the_fingerprint(string which)
    {
        var seen = SafetySnapshot.Fingerprint(SafetySnapshot.Lines([Penicillin, Warfarin], "Reviewed", "Reviewed"));
        var now = which switch
        {
            "anAllergyIsAdded" => SafetySnapshot.Lines([Penicillin, Latex, Warfarin], "Reviewed", "Reviewed"),
            "anAllergyIsRemoved" => SafetySnapshot.Lines([Warfarin], "Reviewed", "Reviewed"),
            "aSeverityChanges" => SafetySnapshot.Lines([Penicillin with { Severity = "Low" }, Warfarin], "Reviewed", "Reviewed"),
            "aReactionChanges" => SafetySnapshot.Lines([Penicillin with { Detail = "Anaphylaxis" }, Warfarin], "Reviewed", "Reviewed"),
            "aMedicationChanges" => SafetySnapshot.Lines([Penicillin, Warfarin with { Detail = "7.5 mg daily" }], "Reviewed", "Reviewed"),
            "theAllergyListNeedsReview" => SafetySnapshot.Lines([Penicillin, Warfarin], "NeedsReview", "Reviewed"),
            "theMedicationListNeedsReview" => SafetySnapshot.Lines([Penicillin, Warfarin], "Reviewed", "NotReviewed"),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        Assert.NotEqual(seen, SafetySnapshot.Fingerprint(now));
    }

    [Fact]
    public void An_unchanged_view_has_the_same_fingerprint_and_the_difference_describes_what_changed_in_words()
    {
        var seen = SafetySnapshot.Lines([Penicillin, Warfarin], "Reviewed", "Reviewed");
        Assert.Equal(SafetySnapshot.Fingerprint(seen), SafetySnapshot.Fingerprint(SafetySnapshot.Lines([Warfarin, Penicillin], "Reviewed", "Reviewed")));
        var (added, removed) = SafetySnapshot.Diff(seen, SafetySnapshot.Lines([Penicillin, Latex], "NeedsReview", "Reviewed"));
        Assert.Equal(["Allergy list: changed since it was last confirmed, so it needs review", "Allergy: Latex (low), Rash"], added.OrderByDescending(x => x.StartsWith("Allergy list")));
        Assert.Equal(["Allergy list: reviewed", "Medication: Warfarin, 5 mg daily"], removed.OrderByDescending(x => x.StartsWith("Allergy list")));
        var same = SafetySnapshot.Diff(seen, seen);
        Assert.Empty(same.Added);
        Assert.Empty(same.Removed);
    }

    [Fact]
    public void The_snapshot_survives_being_stored_as_text_and_an_empty_one_is_still_two_lines_for_the_lists()
    {
        var lines = SafetySnapshot.Lines([Penicillin, Warfarin], "Reviewed", "Reviewed");
        Assert.Equal(lines, SafetySnapshot.Split(SafetySnapshot.Join(lines)));
        Assert.Empty(SafetySnapshot.Split(""));
        Assert.Equal(["AllergyList|NotReviewed", "MedicationList|NotReviewed"], SafetySnapshot.Lines([], "NotReviewed", "NotReviewed"));
    }
}
