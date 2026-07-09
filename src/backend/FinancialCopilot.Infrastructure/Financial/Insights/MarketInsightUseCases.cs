using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Domain.Financial.Insights;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Insights;

internal sealed class InsightDeduplicationPolicy : IInsightDeduplicationPolicy
{
    public string BuildKey(
        InsightType insightType,
        string externalCompanyId,
        string sourceProviderName,
        InsightSourceEntityType sourceEntityType,
        string? sourceEntityId,
        string? sourcePeriod)
    {
        return string.Join(
            ':',
            insightType,
            Normalize(externalCompanyId),
            Normalize(sourceProviderName),
            sourceEntityType,
            Normalize(sourceEntityId),
            Normalize(sourcePeriod));
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToUpperInvariant();
}

internal sealed class GenerateMarketInsightsUseCase(
    IEnumerable<IInsightDetector> detectors,
    IInsightEventRepository repository,
    TimeProvider timeProvider,
    ILogger<GenerateMarketInsightsUseCase> logger) : IGenerateMarketInsightsUseCase
{
    public async Task<GenerateMarketInsightsResult> ExecuteAsync(
        GenerateMarketInsightsRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var lookbackDays = Math.Clamp(request.LookbackDays <= 0 ? 7 : request.LookbackDays, 1, 90);
        var context = new InsightDetectionContext(now, now.AddDays(-lookbackDays));
        var detected = new List<InsightEvent>();
        var detectorCount = 0;

        foreach (var detector in detectors)
        {
            detectorCount++;
            try
            {
                var events = await detector.DetectAsync(context, cancellationToken);
                detected.AddRange(events);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Insight detector {DetectorName} failed.", detector.DetectorName);
            }
        }

        var distinct = detected
            .GroupBy(e => e.DeduplicationKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(e => e.ImportanceScore).First())
            .ToList();

        var persisted = await repository.UpsertAsync(distinct, cancellationToken);
        return new GenerateMarketInsightsResult(detectorCount, distinct.Count, persisted, now);
    }
}

internal sealed class GetMarketInsightFeedUseCase(
    IInsightEventRepository repository) : IGetMarketInsightFeedUseCase
{
    public Task<InsightFeedResponse> ExecuteAsync(
        InsightFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query with
        {
            Skip = Math.Max(0, query.Skip),
            Take = Math.Clamp(query.Take <= 0 ? 20 : query.Take, 1, 100),
            Symbol = string.IsNullOrWhiteSpace(query.Symbol) ? null : query.Symbol.Trim(),
            IndustryCode = string.IsNullOrWhiteSpace(query.IndustryCode) ? null : query.IndustryCode.Trim()
        };

        return repository.QueryAsync(normalized, cancellationToken);
    }
}
