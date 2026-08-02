using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioAnalyticsRecalculationCoordinator(
    IFundPortfolioReportRepository reports,
    IFeatureRecalculationScheduler scheduler,
    TimeProvider timeProvider) : IFundPortfolioAnalyticsRecalculationCoordinator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Leases = new(StringComparer.Ordinal);

    public async Task<FundPortfolioAnalyticsRecalculationResult> RequestAsync(
        FundPortfolioAnalyticsRecalculationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FundId == Guid.Empty || request.ReportId == Guid.Empty) throw new ArgumentException("Fund and report identities are required.");
        if (string.IsNullOrWhiteSpace(request.InputFingerprint)) throw new ArgumentException("An input fingerprint is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CalculationVersion)) throw new ArgumentException("A calculation version is required.", nameof(request));

        var status = await reports.FindStatusAsync(request.ReportId, cancellationToken);
        if (status is null) return new(false, string.Empty, null, "Report was not found.");
        if (status.FundId != request.FundId) return new(false, string.Empty, null, "Report does not belong to the requested fund.");
        if (status.ParseStatus is not (FundPortfolioParseStatus.Parsed or FundPortfolioParseStatus.PartiallyParsed))
            return new(false, string.Empty, null, "Required normalized sections are not in a terminal eligible state.");

        var idempotencyKey = $"fund-portfolio-analytics|{request.FundId:N}|{request.ReportId:N}|{request.PeriodEndDate:yyyy-MM-dd}|{request.CalculationVersion.Trim()}|{request.InputFingerprint.Trim()}";
        var lease = Leases.GetOrAdd(idempotencyKey, _ => new SemaphoreSlim(1, 1));
        await lease.WaitAsync(cancellationToken);
        try
        {
            var periodStart = request.PeriodEndDate.AddDays(1 - request.PeriodEndDate.Day);
            var featureRequest = new FeatureRecalculationRequested(
                StableGuid(idempotencyKey),
                new FeatureCode(FundPortfolioAnalyticsCalculationPolicy.FeatureCode),
                new FeatureVersion("v1"),
                request.FundId.ToString("N"),
                FeatureComputationPeriod.From(FiscalPeriod.Closed(FiscalPeriodType.Monthly, periodStart, request.PeriodEndDate)),
                idempotencyKey,
                timeProvider.GetUtcNow());
            var job = await scheduler.ScheduleAsync(featureRequest, cancellationToken);
            return new(true, idempotencyKey, job);
        }
        finally
        {
            lease.Release();
        }
    }

    private static Guid StableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
