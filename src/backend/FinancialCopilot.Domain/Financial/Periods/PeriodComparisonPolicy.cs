namespace FinancialCopilot.Domain.Financial.Periods;

public enum GrowthComparison
{
    YearOverYear,
    MonthOverMonth
}

public sealed class PeriodComparisonPolicy
{
    public FiscalPeriod GetComparisonPeriod(
        FiscalPeriod currentPeriod,
        GrowthComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(currentPeriod);

        if (currentPeriod.IsLatestSelection)
        {
            throw new InvalidOperationException(
                "Resolve latest-period selections before applying a comparison policy.");
        }

        return comparison switch
        {
            GrowthComparison.YearOverYear => currentPeriod.ShiftMonths(-12),
            GrowthComparison.MonthOverMonth when currentPeriod.Type == FiscalPeriodType.Monthly =>
                currentPeriod.ShiftMonths(-1),
            GrowthComparison.MonthOverMonth => throw new InvalidOperationException(
                "Month-over-month comparison is supported only for monthly periods."),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };
    }
}
