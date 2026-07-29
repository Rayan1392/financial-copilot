using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class PsGaugeCalculatorTests
{
    [Fact]
    public void Shiraz_UsesEqualWidthBandsAndMinMaxNeedleScale()
    {
        var result = PsGaugeCalculator.Calculate(
            [219, 380, 692, 667, 146, 403],
            0.1635637626811413m,
            0.740089530453877m,
            0.4691581989639046m,
            2);

        Assert.Equal(6, result.Bands.Count);
        Assert.All(result.Bands, band => Assert.Equal(30m, band.EndAngleDegrees - band.StartAngleDegrees));
        Assert.Equal([8.74m, 15.16m, 27.60m, 26.61m, 5.82m, 16.07m], result.Bands.Select(x => x.DisplayPercentage));
        Assert.Equal(100m, result.Bands.Sum(x => x.DisplayPercentage));
        Assert.InRange(result.Needle.NormalizedPosition, 0.52m, 0.54m);
        Assert.False(result.Needle.IsClampedLow);
        Assert.False(result.Needle.IsClampedHigh);
    }

    [Fact]
    public void Ggolpa_CurrentBelowMin_ClampsNeedleToLowEdge()
    {
        var result = PsGaugeCalculator.Calculate(
            [168, 554, 550, 383, 168, 273],
            0.514889286014075m,
            1.9899426667336684m,
            0.4088225224044161m,
            2);

        Assert.Equal(0m, result.Needle.NormalizedPosition);
        Assert.Equal(0m, result.Needle.AngleDegrees);
        Assert.True(result.Needle.IsClampedLow);
        Assert.Equal(100m, result.Bands.Sum(x => x.DisplayPercentage));
    }

    [Fact]
    public void InvalidGauge_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PsGaugeCalculator.Calculate([0, 0, 0, 0, 0, 0], 1m, 2m, 1.5m, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PsGaugeCalculator.Calculate([1, 1, 1, 1, 1, 1], 2m, 1m, 1.5m, 2));
    }
}
