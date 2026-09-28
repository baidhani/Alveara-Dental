namespace Alveara.Api.Architecture.Money;

/// <summary>
/// Authoritative money type for the whole product, per ALV-N002: never use <c>double</c>/<c>float</c>
/// for financial amounts. Backed by <see cref="decimal"/>, fixed at USD/2-decimal-places for first
/// release (see <see cref="Currency"/>), using banker's rounding (round-half-to-even) — the standard
/// rounding policy for repeated financial aggregation, since it doesn't statistically bias sums
/// upward or downward the way round-half-away-from-zero does.
/// </summary>
public readonly record struct Money
{
    public const string Currency = "USD";
    private const int DecimalPlaces = 2;

    public decimal Amount { get; }

    private Money(decimal amount)
    {
        Amount = amount;
    }

    /// <summary>Creates a Money value, rounding to the currency's decimal places using banker's rounding.</summary>
    public static Money FromDecimal(decimal amount) =>
        new(Math.Round(amount, DecimalPlaces, MidpointRounding.ToEven));

    public static Money Zero => new(0m);

    public static Money operator +(Money a, Money b) => FromDecimal(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => FromDecimal(a.Amount - b.Amount);
    public static Money operator -(Money a) => FromDecimal(-a.Amount);

    /// <summary>
    /// Multiplies by a plain decimal factor (e.g. a quantity or a discount percentage), rounding
    /// the result — the only place rounding error can enter is here, and it always uses the same
    /// banker's-rounding policy as every other Money operation.
    /// </summary>
    public static Money operator *(Money a, decimal factor) => FromDecimal(a.Amount * factor);

    public override string ToString() => Amount.ToString("F2") + " " + Currency;
}
