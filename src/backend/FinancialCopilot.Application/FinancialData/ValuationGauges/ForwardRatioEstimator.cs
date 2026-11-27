namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

/// <summary>
/// Sapio's forward P/E and P/S estimate (method <see cref="MethodVersion"/>), pure and deterministic:
/// <code>
/// forecast sales = Σ last 12 monthly sales × (Σ last 4 months ÷ Σ the same 4 months one year earlier)
/// forward margin = median of the last 8 quarterly net margins (net profit ÷ revenue, discrete quarters)
/// forward P/S    = current P/S × Σ revenue of the last 4 quarters ÷ forecast sales
/// forward P/E    = current P/E × Σ net profit of the last 4 quarters ÷ (forecast sales × forward margin)
/// </code>
/// Each ratio uses its own gauge's current value (user-story assumption A1). Missing data is never replaced by a guess:
/// any failed guard gives no value with a reason code. The reference implementation of the same rule is
/// <c>estimate()</c> in <c>tools/research/forward-pe-reverse-engineering/export_estimator_fixtures.py</c>; the unit
/// tests pin this class to that script's fixtures.
/// </summary>
public static class ForwardRatioEstimator
{
    public const string MethodVersion = "sapio-1";

    private const int SalesMonths = 12;
    private const int GrowthWindowMonths = 4;
    private const int RequiredMonths = SalesMonths + GrowthWindowMonths;
    private const int RequiredQuarters = 8;
    private const int TrailingQuarters = 4;

    /// <summary>Largest distance in days between consecutive month ends that still counts as consecutive.</summary>
    private const int MaxMonthGapDays = 35;

    /// <summary>Largest distance in days between consecutive quarter ends that still counts as consecutive.</summary>
    private const int MaxQuarterGapDays = 100;

    public static ForwardRatioEstimate Estimate(
        ForwardRatioInputs inputs,
        decimal? currentPe,
        decimal? currentPs,
        ForwardRatioThresholds thresholds,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (inputs.IndustryExternalId is { } industry && thresholds.ExcludedIndustryExternalIds.Contains(industry))
            return ForwardRatioEstimate.None(ForwardRatioReasonCodes.ExcludedIndustry);

        var months = ConsecutiveTail(inputs.MonthlySales, RequiredMonths, month => month.MonthEnd, MaxMonthGapDays);
        if (months is null)
            return ForwardRatioEstimate.None(ForwardRatioReasonCodes.InsufficientMonthlySales);
        var latestMonth = months[^1].MonthEnd;
        if (today.DayNumber - latestMonth.DayNumber > thresholds.MaxMonthlyReportAgeDays)
            return ForwardRatioEstimate.None(ForwardRatioReasonCodes.StaleMonthlySales);

        var comparison = months.Take(GrowthWindowMonths).Sum(month => month.Sales);
        if (comparison <= 0m)
            return ForwardRatioEstimate.None(ForwardRatioReasonCodes.NoComparisonSales);
        var growth = months.Skip(RequiredMonths - GrowthWindowMonths).Sum(month => month.Sales) / comparison;
        if (growth < thresholds.MinGrowthFactor || growth > thresholds.MaxGrowthFactor)
            return ForwardRatioEstimate.None(ForwardRatioReasonCodes.GrowthOutOfRange) with { GrowthFactor = growth };

        // The 8 quarters must be consecutive, all usable, and end at the latest stored statement.
        var usable = inputs.Quarters.Where(quarter => quarter is { Revenue: > 0m, NetProfit: not null }).ToArray();
        var quarters = ConsecutiveTail(usable, RequiredQuarters, quarter => quarter.PeriodEnd, MaxQuarterGapDays);
        if (quarters is null || inputs.Quarters.Count == 0 || quarters[^1].PeriodEnd != inputs.Quarters[^1].PeriodEnd)
            return ForwardRatioEstimate.None(ForwardRatioReasonCodes.InsufficientQuarters) with { GrowthFactor = growth };

        var forecast = months.Skip(RequiredMonths - SalesMonths).Sum(month => month.Sales) * growth;
        var trailing = quarters.Skip(RequiredQuarters - TrailingQuarters).ToArray();
        var revenue4 = trailing.Sum(quarter => quarter.Revenue!.Value);
        var profit4 = trailing.Sum(quarter => quarter.NetProfit!.Value);
        var margin = Median(quarters.Select(quarter => quarter.NetProfit!.Value / quarter.Revenue!.Value).ToArray());

        decimal? forwardPs = null;
        var psReason = ForwardRatioReasonCodes.NoCurrentValue;
        if (currentPs is > 0m)
        {
            forwardPs = currentPs.Value * revenue4 / forecast;
            psReason = ForwardRatioReasonCodes.None;
        }

        decimal? forwardPe = null;
        string peReason;
        if (currentPe is not > 0m || profit4 <= 0m)
            peReason = ForwardRatioReasonCodes.NoPositiveEarnings;
        else if (margin <= 0m)
            peReason = ForwardRatioReasonCodes.NonPositiveMargin;
        else
        {
            forwardPe = currentPe.Value * profit4 / (forecast * margin);
            peReason = ForwardRatioReasonCodes.None;
        }

        return new ForwardRatioEstimate(forwardPs, psReason, forwardPe, peReason, growth, forecast, margin, latestMonth, quarters[^1].PeriodEnd);
    }

    /// <summary>The last <paramref name="count"/> items when each gap between their period ends is at most <paramref name="maxGapDays"/>.</summary>
    private static T[]? ConsecutiveTail<T>(IReadOnlyList<T> items, int count, Func<T, DateOnly> periodEnd, int maxGapDays)
    {
        if (items.Count < count) return null;
        var tail = items.Skip(items.Count - count).ToArray();
        for (var index = 1; index < tail.Length; index++)
        {
            if (periodEnd(tail[index]).DayNumber - periodEnd(tail[index - 1]).DayNumber > maxGapDays)
                return null;
        }

        return tail;
    }

    private static decimal Median(decimal[] values)
    {
        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2m;
    }
}
