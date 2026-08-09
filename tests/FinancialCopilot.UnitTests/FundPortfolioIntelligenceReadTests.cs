using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioIntelligenceReadTests
{
    [Fact]
    public async Task UseCase_ReturnsPeriodSelectedSnapshotSignalsAndProvenanceMetadata()
    {
        var fundId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var period = new DateOnly(2026, 8, 1);
        var snapshot = new FundPortfolioAnalyticsSnapshot(
            Guid.NewGuid(), fundId, reportId, period, null, 50m, 20m, 10m, 5m, 40m, 60m, 0.2m,
            100m, 80m, 20m, 0.1m, 2, 1, FundPortfolioRiskPosture.Stable,
            FundPortfolioLiquidityRiskStatus.Partial, FundPortfolioValuationQualityStatus.Moderate,
            new(true, true, true, true, false, true), 0.72m, "signals-v1", "{\"source\":\"normalized\"}");
        var repository = new FakeAnalyticsRepository(new(snapshot, []));
        var status = new FakeReportStatus(new(reportId, fundId, FundPortfolioParseStatus.Parsed, 7, "Provider", "hash", new DateTimeOffset(2026, 8, 2, 1, 2, 3, TimeSpan.Zero)));

        var result = await new GetFundPortfolioIntelligenceUseCase(repository, status).ExecuteAsync(fundId, period, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(period, result!.Snapshot.PeriodEndDate);
        Assert.Equal(7, result.Report.SourceRevision);
        Assert.Equal(0.72m, result.Report.ConfidenceScore);
        Assert.Equal("signals-v1", result.Report.CalculationVersion);
        Assert.Equal(Enum.GetValues<FundPortfolioIntelligenceSection>(), result.AvailableSections);
        Assert.Equal(period, repository.LastQuery!.PeriodEndDate);
    }

    private sealed class FakeAnalyticsRepository(FundPortfolioAnalyticsResult result) : IFundPortfolioAnalyticsRepository
    {
        public FundPortfolioAnalyticsQuery? LastQuery { get; private set; }

        public Task<FundPortfolioAnalyticsResult?> GetAsync(FundPortfolioAnalyticsQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult<FundPortfolioAnalyticsResult?>(query.FundId == result.Snapshot.FundId ? result : null);
        }

        public Task StoreAsync(FundPortfolioAnalyticsSnapshot snapshot, IReadOnlyCollection<FundPortfolioSignal> signals, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeReportStatus(FundPortfolioReportStatusResult result) : IGetFundPortfolioReportStatusUseCase
    {
        public Task<FundPortfolioReportStatusResult?> ExecuteAsync(Guid reportId, CancellationToken cancellationToken) =>
            Task.FromResult<FundPortfolioReportStatusResult?>(reportId == result.ReportId ? result : null);
    }
}
