using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class PsGaugeCalculatorTests
{
    [Fact]
    public void Shiraz_UsesEqualWidthBandsAndOuterProviderBoundaries()
    {
        var result = PsGaugeCalculator.Calculate(
            [219, 380, 692, 667, 146, 403],
            0.100m,
            0.1635637626811413m,
            0.740089530453877m,
            1.200m,
            0.4691581989639046m,
            2);

        Assert.Equal(6, result.Bands.Count);
        Assert.All(result.Bands, band => Assert.Equal(30m, band.EndAngleDegrees - band.StartAngleDegrees));
        Assert.Equal([8.74m, 15.16m, 27.60m, 26.61m, 5.82m, 16.07m], result.Bands.Select(x => x.DisplayPercentage));
        Assert.Equal(100m, result.Bands.Sum(x => x.DisplayPercentage));
        Assert.Equal(0.100m, result.Bands[0].LowerBoundary);
        Assert.Equal(0.1635637626811413m, result.Bands[0].UpperBoundary);
        Assert.Equal(0.740089530453877m, result.Bands[4].UpperBoundary);
        Assert.Equal(1.200m, result.Bands[5].UpperBoundary);
        Assert.InRange(result.Needle.AngleDegrees, 75m, 105m);
        Assert.False(result.Needle.IsClampedLow);
        Assert.False(result.Needle.IsClampedHigh);
    }

    [Fact]
    public void Ggolpa_CurrentBetweenStartAndMin_StaysInsideFirstBand()
    {
        var result = PsGaugeCalculator.Calculate(
            [168, 554, 550, 383, 168, 273],
            0.29868693299183074m,
            0.5107477311430351m,
            1.981287183325m,
            5.05668737m,
            0.4088225224044161m,
            2);

        Assert.InRange(result.Needle.AngleDegrees, 15m, 20m);
        Assert.Equal(0, result.Needle.BandOrder);
        Assert.False(result.Needle.IsClampedLow);
        Assert.Equal(100m, result.Bands.Sum(x => x.DisplayPercentage));
    }

    [Fact]
    public void InvalidGauge_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PsGaugeCalculator.Calculate([0, 0, 0, 0, 0, 0], 0m, 1m, 2m, 3m, 1.5m, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PsGaugeCalculator.Calculate([1, 1, 1, 1, 1, 1], 2m, 1m, 1.5m, 3m, 1.5m, 2));
    }
}
