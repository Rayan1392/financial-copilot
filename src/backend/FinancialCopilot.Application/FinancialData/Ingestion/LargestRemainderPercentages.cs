namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Largest-remainder rounding of non-negative counts to percentages that total exactly 100 at the given precision.
/// Extracted unchanged from <see cref="PsGaugeCalculator"/>; ties resolve to the earlier bucket.
/// </summary>
public static class LargestRemainderPercentages
{
    /// <summary>Returns display percentages; throws when the total is not positive.</summary>
    public static decimal[] Calculate(IReadOnlyList<long> counts, int decimals)
    {
        if (counts.Any(count => count < 0)) throw new ArgumentException("Counts must be non-negative.", nameof(counts));
        var total = counts.Sum();
        if (total <= 0) throw new ArgumentOutOfRangeException(nameof(counts), "Bucket total must be positive.");
        if (decimals is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(decimals));

        var scale = Pow10(decimals);
        var targetUnits = checked(100L * scale);
        var exactUnits = counts.Select((count, order) => new
        {
            Order = order,
            Exact = (decimal)count * targetUnits / total
        }).ToArray();
        var displayUnits = exactUnits.Select(x => (long)decimal.Floor(x.Exact)).ToArray();
        var residual = checked((int)(targetUnits - displayUnits.Sum()));
        foreach (var item in exactUnits.OrderByDescending(x => x.Exact - decimal.Floor(x.Exact)).ThenBy(x => x.Order).Take(residual))
            displayUnits[item.Order]++;
        return displayUnits.Select(units => (decimal)units / scale).ToArray();
    }

    private static long Pow10(int value)
    {
        long result = 1;
        for (var index = 0; index < value; index++) result *= 10;
        return result;
    }
}
