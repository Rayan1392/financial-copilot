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
        Assert.Equal(2_000m / 150m, observation.Value!.Value, precision: 10);
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
        int periodOffset)
    {
        var periodStart = new DateOnly(2026, 4, 21).AddMonths(periodOffset);
        var periodEnd = new DateOnly(2026, 5, 21).AddMonths(periodOffset);
        var report = new NormalizedMonthlyReportRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "13150",
            ExternalReportId = Guid.NewGuid().ToString(),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            ReportType = "ProductSales",
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
                ProductCode = $"PRODUCT:{index++}",
                SalesQuantity = line.SalesQuantity,
                SalesAmount = line.SalesAmount,
                ProductionQuantity = line.ProductionQuantity
            });
        }

        await db.SaveChangesAsync();
        return report.Id;
    }
}
