using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Converts cumulative Codal income observations (3/6/9/12-month) to discrete quarterly
/// <see cref="FiscalPeriodType.ThreeMonths"/> observations for use with QoQ calculators.
/// <para>
/// Codal financial statements are <em>cumulative</em>: a "9-month" row holds the sum of the
/// first three quarters. To compute QoQ growth, discrete quarter values are needed:
/// <list type="bullet">
///   <item>Q1 (3-month cumulative): discrete = cumulative (unchanged).</item>
///   <item>Q2 (6-month cumulative): discrete = cumulative(Q2) − cumulative(Q1).</item>
///   <item>Q3 (9-month cumulative): discrete = cumulative(Q3) − cumulative(Q2).</item>
///   <item>Q4 (12-month cumulative): discrete = cumulative(Q4) − cumulative(Q3).</item>
/// </list>
/// The derived period for Qn (n ≥ 2) is <c>ThreeMonths</c> with
/// <c>PeriodStart = prior.PeriodEnd + 1 day</c> and <c>PeriodEnd = current.PeriodEnd</c>.
/// </para>
/// <para>
/// Observations are grouped by <c>PeriodStart</c> (identifies the fiscal year; all cumulative
/// quarters within a fiscal year share the same start). YoY calculators consume the original
/// cumulative observations directly; only QoQ calculators need the output of this deriver.
/// </para>
/// </summary>
public static class CodalDiscreteQuarterDeriver
{
    /// <summary>
    /// Converts <paramref name="cumulativeObservations"/> to discrete quarterly observations.
    /// Input observations outside the cumulative pattern (non-ThreeMonths/Six/Nine/Twelve) are
    /// passed through unchanged.
    /// </summary>
    public static IReadOnlyList<MetricInputObservation> Derive(
        IReadOnlyCollection<MetricInputObservation> cumulativeObservations)
    {
        var results = new List<MetricInputObservation>(cumulativeObservations.Count);

        // Group by PeriodStart — all cumulative quarters in a fiscal year share the same start.
        var byFiscalYearStart = cumulativeObservations
            .Where(IsQuarterCumulative)
            .GroupBy(obs => obs.Period.StartDate!.Value);

        foreach (var group in byFiscalYearStart)
        {
            var sorted = group
                .OrderBy(obs => obs.Period.EndDate!.Value)
                .ToList();

            for (var i = 0; i < sorted.Count; i++)
            {
                var current = sorted[i];

                if (i == 0)
                {
                    // Q1 is already a 3-month discrete period — keep as-is.
                    results.Add(current);
                }
                else
                {
                    var prior = sorted[i - 1];
                    var discreteValue = current.Value is not null && prior.Value is not null
                        ? (decimal?)(current.Value.Value - prior.Value.Value)
                        : null;

                    var discretePeriodStart = prior.Period.EndDate!.Value.AddDays(1);
                    var discretePeriodEnd = current.Period.EndDate!.Value;
                    var discretePeriod = FiscalPeriod.Closed(
                        FiscalPeriodType.ThreeMonths,
                        discretePeriodStart,
                        discretePeriodEnd);

                    results.Add(current with
                    {
                        Period = discretePeriod,
                        Value = discreteValue
                    });
                }
            }
        }

        // Pass through any non-cumulative-quarterly observations unchanged.
        foreach (var obs in cumulativeObservations.Where(o => !IsQuarterCumulative(o)))
        {
            results.Add(obs);
        }

        return results;
    }

    private static bool IsQuarterCumulative(MetricInputObservation obs) =>
        obs.Period.StartDate.HasValue &&
        obs.Period.EndDate.HasValue &&
        obs.Period.Type is FiscalPeriodType.ThreeMonths
            or FiscalPeriodType.SixMonths
            or FiscalPeriodType.NineMonths
            or FiscalPeriodType.TwelveMonths;
}
