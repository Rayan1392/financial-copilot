using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Maps a CodalDB statement's Gregorian date columns to a canonical <see cref="FiscalPeriod"/>-compatible
/// record. No date estimation is needed: CodalDB provides absolute Gregorian dates for both period end
/// and fiscal-year end, so <c>PeriodStart</c> is computed deterministically as the day after the end
/// of the previous fiscal year (i.e. <c>FiscalYearEnd − 1 year + 1 day</c>).
/// Both Jalali strings are retained as source evidence for explainability.
/// </summary>
public sealed record CodalDbMappedPeriod(
    FiscalPeriodType FiscalPeriodType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? PeriodEndJalali,
    string? FiscalYearEndJalali);

public static class CodalDbFiscalPeriodMapper
{
    /// <summary>
    /// Maps CodalDB statement date columns to a <see cref="CodalDbMappedPeriod"/>.
    /// <paramref name="codalPeriodType"/> is the cumulative period length in months (3/6/9/12).
    /// </summary>
    public static CodalDbMappedPeriod Map(
        DateTimeOffset fiscalYearEnd,
        DateTimeOffset periodEnd,
        byte codalPeriodType,
        string? periodEndJalali = null,
        string? fiscalYearEndJalali = null)
    {
        // Fiscal year start = the day after the last day of the previous fiscal year.
        var fyEndDate = DateOnly.FromDateTime(fiscalYearEnd.Date);
        var fyStartDate = fyEndDate.AddYears(-1).AddDays(1);

        var fiscalPeriodType = codalPeriodType switch
        {
            3  => FiscalPeriodType.ThreeMonths,
            6  => FiscalPeriodType.SixMonths,
            9  => FiscalPeriodType.NineMonths,
            12 => FiscalPeriodType.TwelveMonths,
            _  => FiscalPeriodType.TwelveMonths  // unexpected value: safe fallback
        };

        return new CodalDbMappedPeriod(
            fiscalPeriodType,
            fyStartDate,
            DateOnly.FromDateTime(periodEnd.Date),
            periodEndJalali,
            fiscalYearEndJalali);
    }
}
