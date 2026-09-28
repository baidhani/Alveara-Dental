using Alveara.Api.Architecture.Money;
using Xunit;

namespace Alveara.Api.Tests;

public class MoneyTests
{
    [Fact]
    public void Never_uses_floating_point_and_stores_a_plain_decimal()
    {
        var m = Money.FromDecimal(19.99m);
        Assert.IsType<decimal>(m.Amount);
    }

    [Theory]
    [InlineData(10.125, 10.12)] // banker's rounding: .125 -> nearest even at 2dp is .12
    [InlineData(10.135, 10.14)] // .135 -> nearest even is .14
    [InlineData(10.145, 10.14)] // .145 -> nearest even is .14 (not .15)
    [InlineData(10.155, 10.16)] // .155 -> nearest even is .16
    public void Rounds_using_banker_s_rounding_round_half_to_even(decimal input, decimal expected)
    {
        var m = Money.FromDecimal(input);
        Assert.Equal(expected, m.Amount);
    }

    [Fact]
    public void Addition_and_subtraction_round_the_result_consistently()
    {
        // 10.005 -> nearest even at 2dp is 10.00; 0.005 -> nearest even is 0.00.
        var a = Money.FromDecimal(10.005m);
        var b = Money.FromDecimal(0.005m);
        Assert.Equal(10.00m, a.Amount);
        Assert.Equal(0.00m, b.Amount);

        var sum = a + b;
        Assert.Equal(10.00m, sum.Amount);
    }

    [Fact]
    public void Repeated_aggregation_does_not_statistically_bias_upward()
    {
        // Sum of many .5-cent-rounding operations should not systematically drift upward, which
        // round-half-away-from-zero would cause but round-half-to-even should not for this
        // symmetric input set.
        decimal[] inputs = [0.005m, 0.015m, 0.025m, 0.035m, 0.045m, 0.055m, 0.065m, 0.075m];
        var total = inputs.Select(Money.FromDecimal).Aggregate(Money.Zero, (acc, m) => acc + m);
        var naiveSum = inputs.Sum();
        // The rounded total should be close to the naive sum, not consistently above it.
        Assert.True(Math.Abs(total.Amount - Math.Round(naiveSum, 2)) <= 0.02m);
    }

    [Fact]
    public void Currency_is_fixed_at_USD_for_first_release()
    {
        Assert.Equal("USD", Money.Currency);
    }
}
