namespace FinancialCopilot.Domain.Financial.CyclicalWavesV2;

public enum MonetaryCurrency
{
    Rial,
    Toman
}

/// <summary>Persian amount scales accepted in user questions. همت is ten to the twelfth toman.</summary>
public enum MonetaryScale
{
    One,
    Thousand,
    Million,
    Billion,
    ThousandBillion,
    Hemat
}

/// <summary>Deterministic conversion of a user-stated amount to IRR. One toman is ten rials.</summary>
public static class MonetaryAmountUnits
{
    public const decimal RialsPerToman = 10m;

    public static decimal ScaleFactor(MonetaryScale scale) => scale switch
    {
        MonetaryScale.One => 1m,
        MonetaryScale.Thousand => 1_000m,
        MonetaryScale.Million => 1_000_000m,
        MonetaryScale.Billion => 1_000_000_000m,
        MonetaryScale.ThousandBillion => 1_000_000_000_000m,
        MonetaryScale.Hemat => 1_000_000_000_000m,
        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, null)
    };

    /// <summary>همت always means toman, so the currency argument is ignored for that scale.</summary>
    public static decimal ToIrr(decimal amount, MonetaryScale scale, MonetaryCurrency currency)
    {
        var effectiveCurrency = scale == MonetaryScale.Hemat ? MonetaryCurrency.Toman : currency;
        var currencyFactor = effectiveCurrency == MonetaryCurrency.Toman ? RialsPerToman : 1m;
        return amount * ScaleFactor(scale) * currencyFactor;
    }
}

/// <summary>
/// Percentage growth with the same formula and non-positive-base policy as the existing Domain
/// <c>PercentageGrowthMetricCalculator</c>: (current − base) ÷ |base| × 100; a missing operand or a zero
/// base gives no value. A negative base is allowed and divides by its absolute value.
/// </summary>
public static class ScreenerGrowth
{
    public static decimal? Percent(decimal? current, decimal? baseValue) =>
        current is not null && baseValue is not null && baseValue.Value != 0m
            ? (current.Value - baseValue.Value) / Math.Abs(baseValue.Value) * 100m
            : null;
}
