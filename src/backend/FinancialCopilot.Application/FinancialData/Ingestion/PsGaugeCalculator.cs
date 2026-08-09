namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>Screenshot-verified six-bin histogram projection shared by interactive and export renderers.</summary>
public static class PsGaugeCalculator
{
    public static (IReadOnlyList<PsGaugeBand> Bands, PsGaugeNeedle Needle) Calculate(
        IReadOnlyList<long> counts,
        decimal boundaryStart,
        decimal gaugeMin,
        decimal gaugeMax,
        decimal boundaryEnd,
        decimal currentTtmPs,
        int percentageDecimals)
    {
        if (counts.Count != 6 || counts.Any(x => x < 0)) throw new ArgumentException("Exactly six non-negative bucket counts are required.", nameof(counts));
        var total = counts.Sum();
        if (total <= 0) throw new ArgumentOutOfRangeException(nameof(counts), "Bucket total must be positive.");
        if (boundaryStart >= gaugeMin || gaugeMin >= gaugeMax || gaugeMax >= boundaryEnd)
            throw new ArgumentOutOfRangeException(nameof(gaugeMax), "Gauge boundaries must satisfy start < min < max < end.");
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

        var middleStep = (gaugeMax - gaugeMin) / 4m;
        var lowerBoundaries = new[]
        {
            boundaryStart,
            gaugeMin,
            gaugeMin + middleStep,
            gaugeMin + middleStep * 2m,
            gaugeMin + middleStep * 3m,
            gaugeMax
        };
        var upperBoundaries = new[]
        {
            gaugeMin,
            gaugeMin + middleStep,
            gaugeMin + middleStep * 2m,
            gaugeMin + middleStep * 3m,
            gaugeMax,
            boundaryEnd
        };
        var roles = Enum.GetValues<PsGaugeBandRole>();
        var bands = Enumerable.Range(0, 6).Select(order => new PsGaugeBand(
            order,
            roles[order],
            counts[order],
            (decimal)counts[order] * 100m / total,
            (decimal)displayUnits[order] / scale,
            lowerBoundaries[order],
            upperBoundaries[order],
            order * 30m,
            (order + 1) * 30m)).ToArray();

        var (normalized, bandOrder) = MapValue(currentTtmPs, bands);
        return (bands, new PsGaugeNeedle(
            currentTtmPs,
            normalized,
            normalized * 180m,
            bandOrder,
            currentTtmPs < boundaryStart,
            currentTtmPs > boundaryEnd));
    }

    private static (decimal Normalized, int BandOrder) MapValue(
        decimal value,
        IReadOnlyList<PsGaugeBand> bands)
    {
        if (value <= bands[0].LowerBoundary) return (0m, 0);
        if (value >= bands[^1].UpperBoundary) return (1m, 5);

        var band = bands.First(x => value <= x.UpperBoundary);
        var fraction = (value - band.LowerBoundary) / (band.UpperBoundary - band.LowerBoundary);
        var normalized = (band.StartAngleDegrees + fraction * (band.EndAngleDegrees - band.StartAngleDegrees)) / 180m;
        return (normalized, band.Order);
    }

    private static long Pow10(int value)
    {
        long result = 1;
        for (var index = 0; index < value; index++) result *= 10;
        return result;
    }
}
