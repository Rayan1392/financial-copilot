using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlyActivityMetricInputSourceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-10T08:00:00Z");

    [Fact]
    public async Task SalesQuantity_SumsLinesThatCarryAQuantity()
    {
        await using var db = CreateDb();
        var reportId = await SeedReportAsync(db,
            Line(salesQuantity: 100m, salesAmount: 1_000m),
            Line(salesQuantity: 50m, salesAmount: 700m),
            Line(salesQuantity: null, salesAmount: 10m));

        var observations = await new MonthlySalesQuantityMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal(150m, observation.Value);
        Assert.Equal(new MetricCode("MONTHLY_SALES_QUANTITY"), observation.Code);
    }

    [Fact]
    public async Task ProductionQuantity_NullWhenNoProductLineCarriesProduction()
    {
        await using var db = CreateDb();
        await SeedReportAsync(db,
            Line(salesQuantity: 10m, salesAmount: 100m, productionQuantity: null),
            Line(salesQuantity: 5m, salesAmount: 60m, productionQuantity: null));

        var observations = await new MonthlyProductionQuantityMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        Assert.Null(Assert.Single(observations).Value);
    }

    [Fact]
    public async Task ProductionQuantity_SumsProductLinesOnly()
    {
        await using var db = CreateDb();
        await SeedReportAsync(db,
            Line(salesQuantity: 10m, salesAmount: 100m, productionQuantity: 120m),
            Line(salesQuantity: 5m, salesAmount: 60m, productionQuantity: 30m),
            Line(salesQuantity: 3m, salesAmount: 40m, productionQuantity: null)); // service line

        var observations = await new MonthlyProductionQuantityMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        Assert.Equal(150m, Assert.Single(observations).Value);
    }

    [Fact]
    public async Task SalesRate_IsQuantityWeightedAverageOverEligibleLines()
    {
        await using var db = CreateDb();
        // 100 units at 10 + 50 units at 20 → (1000 + 1000) / 150 = 13.33…
        var reportId = await SeedReportAsync(db,
            Line(salesQuantity: 100m, salesAmount: 1_000m),
            Line(salesQuantity: 50m, salesAmount: 1_000m),
            Line(salesQuantity: 0m, salesAmount: 99m),    // zero quantity — excluded
            Line(salesQuantity: null, salesAmount: 42m)); // no quantity — excluded

        var observations = await new MonthlySalesRateMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.NotNull(observation.Value);
        Assert.Equal((2_000m / 150m) * 1_000_000m, observation.Value!.Value, precision: 10);
    }

    [Fact]
    public async Task SalesRate_NullWhenNoEligibleLine()
    {
        await using var db = CreateDb();
        await SeedReportAsync(db, Line(salesQuantity: null, salesAmount: 42m));

        var observations = await new MonthlySalesRateMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        Assert.Null(Assert.Single(observations).Value);
    }

    [Fact]
    public async Task TwoReportsForSameCompany_ProduceTwoSeparateObservations()
    {
        await using var db = CreateDb();
        // A company may have historical reports across multiple months — each is a separate observation.
        await SeedReportAsync(db, [Line(salesQuantity: 100m, salesAmount: 1_000m)], periodOffset: 0);
        await SeedReportAsync(db, [Line(salesQuantity: 50m, salesAmount: 500m)], periodOffset: 1);

        var observations = await new MonthlySalesMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        Assert.Equal(2, observations.Count);
    }

    [Fact]
    public async Task SalesYtd_UsesOutputTypeOneRows()
    {
        await using var db = CreateDb();
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 999m)], 0, outputType: null);
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 100m)], 0, outputType: 0);
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 450m)], 0, outputType: 1);
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 300m)], 0, outputType: 4);

        var observations = await new MonthlySalesYtdMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal(new MetricCode("MONTHLY_SALES_YTD"), observation.Code);
        Assert.Equal(450_000_000m, observation.Value);
    }

    [Fact]
    public async Task SalesYtdPreviousMonth_UsesOutputTypeFourRows()
    {
        await using var db = CreateDb();
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 999m)], 0, outputType: null);
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 100m)], 0, outputType: 0);
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 450m)], 0, outputType: 1);
        await SeedReportAsync(db, [Line(salesQuantity: 1m, salesAmount: 300m)], 0, outputType: 4);

        var observations = await new MonthlySalesYtdPreviousMonthMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal(new MetricCode("MONTHLY_SALES_YTD_PREVIOUS_MONTH"), observation.Code);
        Assert.Equal(300_000_000m, observation.Value);
    }

    [Fact]
    public async Task SalesAmount_NoavaranMillionRials_NormalizesToRials()
    {
        await using var db = CreateDb();
        await SeedReportAsync(db,
            [Line(salesQuantity: 1m, salesAmount: 100m), Line(salesQuantity: 1m, salesAmount: 50m)],
            periodOffset: 0,
            outputType: 0);

        var observations = await new MonthlySalesMetricInputSource(db)
            .LoadAsync("13150", CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal(150_000_000m, observation.Value);
        var evidence = Assert.Single(observation.SourceEvidence);
        Assert.Equal("MillionRials", evidence.SourceUnit);
        Assert.Equal("Rials", evidence.CanonicalUnit);
        Assert.Equal("noavaran-million-rials-to-rials-v1", evidence.UnitNormalizationPolicy);
    }

    [Fact]
    public async Task SalesAmount_CyclicalWavesRials_RemainsUnchanged()
    {
        await using var db = CreateDb();
        await SeedReportAsync(
            db,
            [Line(salesQuantity: null, salesAmount: 90_879_722_000_000m)],
            periodOffset: 0,
            outputType: null,
            providerName: "CyclicalWaves",
            externalCompanyId: "cw-1",
            productCodePrefix: "REVENUE");

        var observations = await new MonthlySalesMetricInputSource(db)
            .LoadAsync("cw-1", CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal(90_879_722_000_000m, observation.Value);
        var evidence = Assert.Single(observation.SourceEvidence);
        Assert.Equal("Rials", evidence.SourceUnit);
        Assert.Equal("Rials", evidence.CanonicalUnit);
        Assert.Equal("cyclicalwaves-precomputed-rials-passthrough-v1", evidence.UnitNormalizationPolicy);
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed record LineSpec(decimal? SalesQuantity, decimal? SalesAmount, decimal? ProductionQuantity);

    private static LineSpec Line(
        decimal? salesQuantity,
        decimal? salesAmount,
        decimal? productionQuantity = null) =>
        new(salesQuantity, salesAmount, productionQuantity);

    private static async Task<Guid> SeedReportAsync(
        FinancialIngestionDbContext db,
        params LineSpec[] lines) =>
        await SeedReportAsync(db, lines, periodOffset: 0);

    private static async Task<Guid> SeedReportAsync(
        FinancialIngestionDbContext db,
        LineSpec[] lines,
        int periodOffset,
        int? outputType = null,
        string providerName = "NoavaranCurrentApi",
        string externalCompanyId = "13150",
        string productCodePrefix = "PRODUCT")
    {
        var periodStart = new DateOnly(2026, 4, 21).AddMonths(periodOffset);
        var periodEnd = new DateOnly(2026, 5, 21).AddMonths(periodOffset);
        var report = new NormalizedMonthlyReportRow
        {
            Id = Guid.NewGuid(),
            ProviderName = providerName,
            ExternalCompanyId = externalCompanyId,
            ExternalReportId = Guid.NewGuid().ToString(),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ReportType = "ProductSales",
            OutputType = outputType,
            SourcePayloadChecksum = "checksum",
            LastSynchronizedAt = Now
        };
        db.MonthlyReports.Add(report);
        var index = 0;
        foreach (var line in lines)
        {
            db.MonthlyReportLineItems.Add(new NormalizedMonthlyReportLineItemRow
            {
                Id = Guid.NewGuid(),
                MonthlyReportId = report.Id,
                ProductCode = productCodePrefix == "REVENUE" ? "REVENUE" : $"{productCodePrefix}:{index++}",
                SalesQuantity = line.SalesQuantity,
                SalesAmount = line.SalesAmount,
                ProductionQuantity = line.ProductionQuantity
            });
        }

        await db.SaveChangesAsync();
        return report.Id;
    }
}
