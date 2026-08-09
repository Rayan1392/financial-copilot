using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioAnalyticsRecalculationTests
{
    [Fact]
    public async Task Coordinator_SchedulesOnlyEligibleTerminalReportsAndDeduplicatesStableRequests()
    {
        var fundId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var reports = new FakeReports(new(reportId, fundId, FundPortfolioParseStatus.Parsed, 4, "provider", "hash", DateTimeOffset.UtcNow));
        var scheduler = new FakeScheduler();
        var coordinator = new FundPortfolioAnalyticsRecalculationCoordinatorForTest(reports, scheduler);
        var request = new FundPortfolioAnalyticsRecalculationRequest(fundId, reportId, new DateOnly(2026, 8, 1), FundPortfolioAnalyticsRecalculationReason.NormalizedSectionsCompleted, "fingerprint-a", "fund-portfolio-analytics-v1");

        var first = await coordinator.RequestAsync(request);
        var second = await coordinator.RequestAsync(request);

        Assert.True(first.Scheduled);
        Assert.True(second.Scheduled);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(first.Job!.Id, second.Job!.Id);
        Assert.Equal(1, scheduler.PublishedCount);
    }

    [Fact]
    public async Task Coordinator_AllowsMappingMarketDataAndVersionChangesThroughDistinctFingerprints()
    {
        var fundId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var reports = new FakeReports(new(reportId, fundId, FundPortfolioParseStatus.PartiallyParsed, 4, "provider", "hash", DateTimeOffset.UtcNow));
        var scheduler = new FakeScheduler();
        var coordinator = new FundPortfolioAnalyticsRecalculationCoordinatorForTest(reports, scheduler);

        var mapping = await coordinator.RequestAsync(new(fundId, reportId, new DateOnly(2026, 8, 1), FundPortfolioAnalyticsRecalculationReason.MappingChanged, "mapping-v2", "fund-portfolio-analytics-v1"));
        var market = await coordinator.RequestAsync(new(fundId, reportId, new DateOnly(2026, 8, 1), FundPortfolioAnalyticsRecalculationReason.MarketDataChanged, "market-v3", "fund-portfolio-analytics-v1"));
        var version = await coordinator.RequestAsync(new(fundId, reportId, new DateOnly(2026, 8, 1), FundPortfolioAnalyticsRecalculationReason.CalculationVersionChanged, "mapping-v2", "fund-portfolio-analytics-v2"));

        Assert.NotEqual(mapping.IdempotencyKey, market.IdempotencyKey);
        Assert.NotEqual(market.IdempotencyKey, version.IdempotencyKey);
        Assert.Equal(3, scheduler.PublishedCount);
    }

    [Fact]
    public async Task Coordinator_SkipsNonTerminalReportsWithoutPublishing()
    {
        var fundId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var reports = new FakeReports(new(reportId, fundId, FundPortfolioParseStatus.Parsing, 1, "provider", "hash", DateTimeOffset.UtcNow));
        var scheduler = new FakeScheduler();
        var result = await new FundPortfolioAnalyticsRecalculationCoordinatorForTest(reports, scheduler)
            .RequestAsync(new(fundId, reportId, new DateOnly(2026, 8, 1), FundPortfolioAnalyticsRecalculationReason.Manual, "manual", "v1"));

        Assert.False(result.Scheduled);
        Assert.Equal(0, scheduler.PublishedCount);
        Assert.Contains("terminal", result.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeScheduler : IFeatureRecalculationScheduler
    {
        private readonly Dictionary<string, FeatureComputationJob> jobs = new(StringComparer.Ordinal);
        public int PublishedCount { get; private set; }

        public Task<FeatureComputationJob> ScheduleAsync(FeatureRecalculationRequested request, CancellationToken cancellationToken)
        {
            if (jobs.TryGetValue(request.IdempotencyKey, out var existing)) return Task.FromResult(existing);
            var job = new FeatureComputationJob(request.JobId, request.FeatureCode, request.FeatureVersion, request.ExternalCompanyId, request.Period.ToFiscalPeriod(), request.IdempotencyKey, FeatureComputationStatus.Requested, request.RequestedAt);
            jobs[request.IdempotencyKey] = job;
            PublishedCount++;
            return Task.FromResult(job);
        }
    }

    private sealed class FakeReports(FundPortfolioReportStatusResult status) : IFundPortfolioReportRepository
    {
        public Task<FundPortfolioReportStatusResult?> FindStatusAsync(Guid reportId, CancellationToken cancellationToken) => Task.FromResult<FundPortfolioReportStatusResult?>(reportId == status.ReportId ? status : null);
        public Task<FundPortfolioReportIssuePage> FindIssuesAsync(Guid reportId, int page, int pageSize, FundExtractionIssueSeverity? severity, string? issueCode, CancellationToken cancellationToken) => Task.FromResult(new FundPortfolioReportIssuePage([], page, pageSize, 0));
        public Task<(Guid ReportId, int SourceRevision)?> FindByHashAsync(string providerName, string fileSha256, CancellationToken cancellationToken) => Task.FromResult<(Guid, int)?>(null);
        public Task<int> GetNextRevisionAsync(Guid fundId, string providerName, DateOnly? periodEndDate, CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<bool> SaveParsedReportAsync(InvestmentFund fund, IngestFundPortfolioWorkbookRequest request, FundPortfolioStoredFile storedFile, FundPortfolioWorkbookEnvelope envelope, int sourceRevision, Guid? supersedesReportId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Guid?> FindLatestReportIdAsync(Guid fundId, string providerName, DateOnly? periodEndDate, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
    }

    private sealed class FundPortfolioAnalyticsRecalculationCoordinatorForTest(
        IFundPortfolioReportRepository reports,
        IFeatureRecalculationScheduler scheduler)
    {
        private readonly FundPortfolioAnalyticsRecalculationCoordinator coordinator = new(reports, scheduler, TimeProvider.System);
        public Task<FundPortfolioAnalyticsRecalculationResult> RequestAsync(FundPortfolioAnalyticsRecalculationRequest request) => coordinator.RequestAsync(request, CancellationToken.None);
    }
}
