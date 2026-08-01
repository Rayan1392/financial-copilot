using System.Diagnostics.Metrics;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioOperationalTelemetry(ILogger<FundPortfolioOperationalTelemetry> logger) : IFundPortfolioOperationalTelemetry, IDisposable
{
    private static readonly Meter Meter = new("FinancialCopilot.FundPortfolio", "1.0");
    private readonly Counter<long> discovery = Meter.CreateCounter<long>("fund_portfolio.discovery.count");
    private readonly Counter<long> uploads = Meter.CreateCounter<long>("fund_portfolio.upload.bytes");
    private readonly Counter<long> retries = Meter.CreateCounter<long>("fund_portfolio.retry.count");
    private readonly Counter<long> reviews = Meter.CreateCounter<long>("fund_portfolio.review.count");
    private readonly Counter<long> finalStatuses = Meter.CreateCounter<long>("fund_portfolio.run.final_status");
    private readonly Histogram<double> downloadLatency = Meter.CreateHistogram<double>("fund_portfolio.download.latency_ms");
    private readonly Histogram<double> queueLag = Meter.CreateHistogram<double>("fund_portfolio.queue.lag_ms");
    private readonly Counter<long> outcomes = Meter.CreateCounter<long>("fund_portfolio.item.outcome");

    public void RecordDiscovery(int count) { discovery.Add(count); logger.LogInformation("Fund portfolio discovery count={Count}", count); }
    public void RecordUpload(long bytes) { uploads.Add(bytes); logger.LogInformation("Fund portfolio upload size bytes={Bytes}", bytes); }
    public void RecordDownload(long bytes, double latencyMilliseconds) { downloadLatency.Record(latencyMilliseconds); logger.LogInformation("Fund portfolio download bytes={Bytes} latencyMs={LatencyMs}", bytes, latencyMilliseconds); }
    public void RecordRetry() { retries.Add(1); logger.LogInformation("Fund portfolio retry scheduled."); }
    public void RecordReview(int count) { reviews.Add(count); logger.LogInformation("Fund portfolio review count={Count}", count); }
    public void RecordFinalStatus(FundPortfolioImportRunStatus status) { finalStatuses.Add(1, new KeyValuePair<string, object?>("status", status.ToString())); logger.LogInformation("Fund portfolio run final status={Status}", status); }
    public void RecordQueueLag(TimeSpan lag) { queueLag.Record(lag.TotalMilliseconds); }
    public void RecordOutcome(FundPortfolioImportItemStatus status) { outcomes.Add(1, new KeyValuePair<string, object?>("status", status.ToString())); logger.LogInformation("Fund portfolio item outcome={Status}", status); }
    public void Dispose() { }
}
