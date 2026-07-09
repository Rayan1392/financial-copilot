using FinancialCopilot.Domain.Financial.Insights;

namespace FinancialCopilot.Application.FinancialData.Insights;

public sealed record InsightDetectionContext(
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset SinceUtc);

public sealed record InsightFeedQuery(
    string? Symbol = null,
    string? IndustryCode = null,
    InsightType? InsightType = null,
    InsightSeverity? Severity = null,
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    bool IncludeExpired = false,
    int Skip = 0,
    int Take = 20);

public sealed record InsightFeedResponse(
    int TotalCount,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<InsightFeedItem> Items);

public sealed record InsightFeedItem(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string? IndustryCode,
    InsightType InsightType,
    InsightSeverity Severity,
    decimal ImportanceScore,
    decimal ConfidenceScore,
    string Title,
    string Summary,
    string Reason,
    IReadOnlyList<InsightEvidenceItem> Evidence,
    string SourceProviderName,
    InsightSourceEntityType SourceEntityType,
    string? SourceEntityId,
    string? SourcePeriod,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string DeduplicationKey,
    IReadOnlyList<InsightAction> SuggestedActions);

public sealed record GenerateMarketInsightsRequest(int LookbackDays = 7);

public sealed record GenerateMarketInsightsResult(
    int DetectorsRun,
    int EventsDetected,
    int EventsPersisted,
    DateTimeOffset GeneratedAtUtc);

public interface IInsightEventRepository
{
    Task<int> UpsertAsync(IReadOnlyCollection<InsightEvent> events, CancellationToken cancellationToken = default);

    Task<InsightFeedResponse> QueryAsync(InsightFeedQuery query, CancellationToken cancellationToken = default);
}

public interface IInsightDetector
{
    string DetectorName { get; }

    Task<IReadOnlyCollection<InsightEvent>> DetectAsync(
        InsightDetectionContext context,
        CancellationToken cancellationToken = default);
}

public interface IInsightDeduplicationPolicy
{
    string BuildKey(
        InsightType insightType,
        string externalCompanyId,
        string sourceProviderName,
        InsightSourceEntityType sourceEntityType,
        string? sourceEntityId,
        string? sourcePeriod);
}

public interface IGenerateMarketInsightsUseCase
{
    Task<GenerateMarketInsightsResult> ExecuteAsync(
        GenerateMarketInsightsRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGetMarketInsightFeedUseCase
{
    Task<InsightFeedResponse> ExecuteAsync(
        InsightFeedQuery query,
        CancellationToken cancellationToken = default);
}
