namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>Screenshot-verified six-bin histogram projection shared by interactive and export renderers.</summary>
public static class PsGaugeCalculator
{
    public static (IReadOnlyList<PsGaugeBand> Bands, PsGaugeNeedle Needle) Calculate(
        IReadOnlyList<long> counts,
        decimal axisMin,
        decimal axisMax,
        decimal currentTtmPs,
        int percentageDecimals)
    {
        if (counts.Count != 6 || counts.Any(x => x < 0)) throw new ArgumentException("Exactly six non-negative bucket counts are required.", nameof(counts));
        var total = counts.Sum();
        if (total <= 0) throw new ArgumentOutOfRangeException(nameof(counts), "Bucket total must be positive.");
        if (axisMax <= axisMin) throw new ArgumentOutOfRangeException(nameof(axisMax), "Gauge maximum must exceed minimum.");
        if (percentageDecimals is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(percentageDecimals));

        var scale = Pow10(percentageDecimals);
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

        var span = axisMax - axisMin;
        var roles = Enum.GetValues<PsGaugeBandRole>();
        var bands = Enumerable.Range(0, 6).Select(order => new PsGaugeBand(
            order,
            roles[order],
            counts[order],
            (decimal)counts[order] * 100m / total,
            (decimal)displayUnits[order] / scale,
            axisMin + span * order / 6m,
            axisMin + span * (order + 1) / 6m,
            order * 30m,
            (order + 1) * 30m)).ToArray();

        var unclamped = (currentTtmPs - axisMin) / span;
        var normalized = Math.Clamp(unclamped, 0m, 1m);
        var bandOrder = normalized >= 1m ? 5 : Math.Clamp((int)decimal.Floor(normalized * 6m), 0, 5);
        return (bands, new PsGaugeNeedle(currentTtmPs, normalized, normalized * 180m, bandOrder, unclamped < 0m, unclamped > 1m));
    }

    private static long Pow10(int value)
    {
        long result = 1;
        for (var index = 0; index < value; index++) result *= 10;
        return result;
    }
}
