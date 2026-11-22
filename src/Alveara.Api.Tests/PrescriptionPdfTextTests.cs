using Xunit;
using Alveara.Api.Architecture.Prescribing;

namespace Alveara.Api.Tests;

/// <summary>ALV-N026 S6: the display rules of a p2 prescription (no database): F-165 plural units from a fixed table, Q-5am's single-dose label, and the boundary statement's fixed claims.</summary>
public class PrescriptionPdfTextTests
{
    [Theory]
    [InlineData(21, "capsule", "21 capsules")] [InlineData(1, "capsule", "1 capsule")] [InlineData(30, "tablet", "30 tablets")] [InlineData(2, "tube", "2 tubes")]
    [InlineData(100, "mL", "100 mL")] [InlineData(5, "g", "5 g")] [InlineData(0.5, "tablet", "0.5 tablets")]
    public void A_quantity_uses_the_plural_from_the_fixed_table_and_leaves_other_units_unchanged(decimal value, string unit, string expected) =>
        Assert.Equal(expected, PrescriptionPdf.Quantity(value, unit));

    [Fact]
    public void A_single_dose_reads_as_such_and_every_stored_frequency_stays_as_it_is()
    {
        Assert.Equal("Once only (single dose)", PrescriptionPdf.FrequencyLabel("Once"));
        foreach (var f in PrescriptionRules.Frequencies.Where(f => f != "Once")) Assert.Equal(f, PrescriptionPdf.FrequencyLabel(f));
        Assert.Contains("Once", PrescriptionRules.Frequencies);                                                         // the stored value itself is unchanged
    }

    [Fact]
    public void The_boundary_states_what_the_layout_is_not_and_claims_no_compliance()
    {
        Assert.Contains("not a federal or state compliance profile", PrescriptionPdf.Boundary);
        Assert.Contains("not designed or validated as a controlled-substance prescription form", PrescriptionPdf.Boundary);
        Assert.DoesNotContain("DEA", PrescriptionPdf.Boundary);
    }
}
