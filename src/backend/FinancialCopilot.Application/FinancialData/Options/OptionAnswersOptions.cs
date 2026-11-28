using System.Text.Json.Serialization;

namespace FinancialCopilot.Application.FinancialData.Options;

/// <summary>
/// Settings of the option comparison answers (Feature 168). The fee rates come from the source
/// the owner named (OQ-172) and are configuration so a correction needs no release.
/// </summary>
public sealed class OptionAnswersOptions
{
    public const string SectionName = "OptionAnswers";

    /// <summary>Master switch of the assistant capability and the discount alert. Off by default.</summary>
    public bool Enabled { get; init; }

    /// <summary>Fee on buying or selling an option, as a fraction of the premium.</summary>
    public decimal TradeFeeRate { get; init; } = 0.001m;

    /// <summary>Fee on exercising, as a fraction of the strike; both sides of an exercised contract.</summary>
    public decimal ExerciseFeeRate { get; init; } = 0.0005m;

    /// <summary>Transfer tax on the strike, paid by the side that delivers a share; none for fund units.</summary>
    public decimal ShareTransferTaxRate { get; init; } = 0.005m;

    /// <summary>Annualized figures are shown only with at least this many days to expiry (OQ-174).</summary>
    public int AnnualizationFloorDays { get; init; } = 3;

    /// <summary>Expiries per answer when the question names none.</summary>
    public int MaxExpiries { get; init; } = 3;

    /// <summary>
    /// Effective annual risk-free rate for the theoretical price (OQ-178: the compounded yearly
    /// yield of fixed-income funds, 0.415 on 2026-11-28). Null leaves the theoretical columns out.
    /// </summary>
    public decimal? RiskFreeAnnualRate { get; init; }

    /// <summary>Trading days of daily closes used for historical volatility.</summary>
    public int VolatilityWindowDays { get; init; } = 90;

    /// <summary>Trading days per year used to annualize daily volatility.</summary>
    public int TradingDaysPerYear { get; init; } = 240;

    public OptionFeeRates Fees => new(TradeFeeRate, ExerciseFeeRate, ShareTransferTaxRate);

    public bool IsValid(out string error)
    {
        error = string.Empty;
        static bool Rate(decimal value) => value is >= 0m and <= 0.05m;
        if (!Rate(TradeFeeRate) || !Rate(ExerciseFeeRate) || !Rate(ShareTransferTaxRate))
            error = "fee rates must be between 0 and 0.05";
        else if (AnnualizationFloorDays is < 0 or > 365)
            error = "AnnualizationFloorDays must be between 0 and 365";
        else if (MaxExpiries is < 1 or > 12)
            error = "MaxExpiries must be between 1 and 12";
        else if (RiskFreeAnnualRate is < 0m or > 5m)
            error = "RiskFreeAnnualRate must be between 0 and 5";
        else if (VolatilityWindowDays is < 20 or > 500)
            error = "VolatilityWindowDays must be between 20 and 500";
        else if (TradingDaysPerYear is < 200 or > 366)
            error = "TradingDaysPerYear must be between 200 and 366";
        return error.Length == 0;
    }
}

/// <summary>The three fee rates an answer was computed with; stated in every answer.</summary>
public sealed record OptionFeeRates(decimal Trade, decimal Exercise, decimal ShareTransferTax)
{
    /// <summary>Tax on delivering the underlying at exercise: shares only. An unknown class is taxed like a share.</summary>
    public decimal TransferTaxFor(OptionUnderlyingClass underlyingClass) =>
        underlyingClass == OptionUnderlyingClass.FundUnit ? 0m : ShareTransferTax;
}

/// <summary>What an option is written on. Decides whether the transfer tax applies at exercise.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OptionUnderlyingClass>))]
public enum OptionUnderlyingClass
{
    Share,
    FundUnit,

    /// <summary>Could not be told; computed like a share (the costlier case) and flagged in the answer.</summary>
    Unknown
}

public static class OptionUnderlyingClassifier
{
    /// <summary>
    /// From the underlying's ISIN (OQ-177): <c>IRT…</c> is a tradable fund unit, <c>IRO…</c> a
    /// share. Checked on 2026-11-28 against all 31 underlyings of listed calls and puts.
    /// </summary>
    public static OptionUnderlyingClass FromIsin(string? isin)
    {
        var value = isin?.Trim().ToUpperInvariant() ?? string.Empty;
        if (value.StartsWith("IRT", StringComparison.Ordinal)) return OptionUnderlyingClass.FundUnit;
        if (value.StartsWith("IRO", StringComparison.Ordinal)) return OptionUnderlyingClass.Share;
        return OptionUnderlyingClass.Unknown;
    }
}
