using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class FundPortfolioAnalyticsPersistenceTests
{
    [Fact]
    public async Task Repository_PersistsTwoConsecutivePeriodsAndReturnsDeterministicLatestSnapshot()
    {
        await using var db = new FinancialProviderDbContext(
            new DbContextOptionsBuilder<FinancialProviderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var repository = new EfCoreFundPortfolioAnalyticsRepository(db);
        var fundId = Guid.NewGuid();
        var first = Snapshot(fundId, new DateOnly(2026, 6, 30), Guid.NewGuid(), 0.42m);
        var second = Snapshot(fundId, new DateOnly(2026, 7, 31), Guid.NewGuid(), 0.78m);
        var firstSignal = Signal(first, "A", FundPortfolioSignalType.TopPurchase);
        var secondSignal = Signal(second, "B", FundPortfolioSignalType.TopSale);

        await repository.StoreAsync(first, [firstSignal], CancellationToken.None);
        await repository.StoreAsync(second, [secondSignal], CancellationToken.None);

        var latest = await repository.GetAsync(new(fundId), CancellationToken.None);
        var exact = await repository.GetAsync(new(fundId, first.PeriodEndDate), CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(second.PeriodEndDate, latest!.Snapshot.PeriodEndDate);
        Assert.Equal(second.ConfidenceScore, latest.Snapshot.ConfidenceScore);
        Assert.Equal(secondSignal.DeduplicationKey, latest.Signals.Single().DeduplicationKey);
        Assert.NotNull(exact);
        Assert.Equal(first.ReportId, exact!.Snapshot.ReportId);
        Assert.Equal(firstSignal.DeduplicationKey, exact.Signals.Single().DeduplicationKey);
    }

    [Fact]
    public async Task Repository_UpsertReplacesSignalsWithoutCreatingDuplicateSnapshotRows()
    {
        await using var db = new FinancialProviderDbContext(
            new DbContextOptionsBuilder<FinancialProviderDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var repository = new EfCoreFundPortfolioAnalyticsRepository(db);
        var snapshot = Snapshot(Guid.NewGuid(), new DateOnly(2026, 7, 31), Guid.NewGuid(), 0.55m);
        var original = Signal(snapshot, "A", FundPortfolioSignalType.TopPurchase);
        var replacement = Signal(snapshot, "C", FundPortfolioSignalType.TopSale);

        await repository.StoreAsync(snapshot, [original], CancellationToken.None);
        await repository.StoreAsync(snapshot with { ConfidenceScore = 0.31m }, [replacement], CancellationToken.None);

        Assert.Equal(1, await db.FundPortfolioAnalyticsSnapshots.CountAsync());
        Assert.Equal(1, await db.FundPortfolioSignals.CountAsync());
        var result = await repository.GetAsync(new(snapshot.FundId, snapshot.PeriodEndDate), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(0.31m, result!.Snapshot.ConfidenceScore);
        Assert.Equal(replacement.DeduplicationKey, result.Signals.Single().DeduplicationKey);
    }

    private static FundPortfolioAnalyticsSnapshot Snapshot(Guid fundId, DateOnly periodEndDate, Guid reportId, decimal confidence) =>
        new(
            Guid.NewGuid(), fundId, reportId, periodEndDate, null,
            70m, 20m, 5m, 5m, 55m, 75m, 0.32m,
            100m, 30m, 70m, 0.26m, 1, 0,
            FundPortfolioRiskPosture.Stable,
            FundPortfolioLiquidityRiskStatus.Partial,
            FundPortfolioValuationQualityStatus.Limited,
            new FundPortfolioInputCompleteness(true, true, true, false, true, false),
            confidence, FundPortfolioAnalyticsCalculationPolicy.CalculationVersion,
            "{\"fixture\":true}");

    private static FundPortfolioSignal Signal(
        FundPortfolioAnalyticsSnapshot snapshot,
        string company,
        FundPortfolioSignalType type) =>
        new(
            Guid.NewGuid(), snapshot.Id, type, company, null, 10m, 0.5m, snapshot.ConfidenceScore,
            type.ToString(), "disclosed activity evidence", "{\"source\":\"fixture\"}",
            FundPortfolioAnalyticsCalculationPolicy.SignalDeduplicationKey(snapshot.FundId, snapshot.ReportId, type, company));
}
