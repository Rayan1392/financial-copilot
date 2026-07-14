namespace FinancialCopilot.Domain.Financial.Insights;

public enum InsightType
{
    MonthlyReportPublished,
    MonthlySalesAnomaly,
    MonthlyQualityRankingChange,
    PriceMovement,
    ComprehensiveAnalysisPublished,
    FinancialStatementPublished,
    CodalAnnouncementMatched,
    DataFreshnessWarning,
    LargeTradeDetected,
    OrderQueueChanged,
    BuyerSellerPowerChanged,
    RealMoneyFlowChanged,
    TradingVolumeAnomaly,
    TradingValueAnomaly
}

public enum InsightSeverity
{
    Informational,
    Notice,
    Important,
    Critical
}

public enum InsightSourceEntityType
{
    MonthlyReport,
    MonthlyActivityTrendSnapshot,
    MonthlySalesQualityRankingSnapshot,
    MarketQuote,
    ComprehensiveAnalysis,
    FinancialStatement,
    SyncState,
    MarketMicrostructureObservation
}

public enum InsightAction
{
    OpenSymbol,
    AskAiAboutThis,
    OpenSourceReport
}

public sealed record InsightUserActor
{
    public InsightUserActor(Guid tenantId, Guid actorId, string actorType)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));
        if (string.IsNullOrWhiteSpace(actorType)) throw new ArgumentException("Actor type is required.", nameof(actorType));

        TenantId = tenantId;
        ActorId = actorId;
        ActorType = actorType.Trim();
    }

    public Guid TenantId { get; }

    public Guid ActorId { get; }

    public string ActorType { get; }
}

public sealed class UserInsightState
{
    private UserInsightState(
        Guid id,
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset? seenAtUtc,
        DateTimeOffset? dismissedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("State id is required.", nameof(id));
        if (insightEventId == Guid.Empty) throw new ArgumentException("Insight event id is required.", nameof(insightEventId));

        Id = id;
        Actor = actor;
        InsightEventId = insightEventId;
        SeenAtUtc = seenAtUtc;
        DismissedAtUtc = dismissedAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public InsightUserActor Actor { get; }

    public Guid InsightEventId { get; }

    public DateTimeOffset? SeenAtUtc { get; private set; }

    public DateTimeOffset? DismissedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool Seen => SeenAtUtc.HasValue;

    public bool Dismissed => DismissedAtUtc.HasValue;

    public static UserInsightState Create(
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), actor, insightEventId, null, null, now, now);

    public static UserInsightState Rehydrate(
        Guid id,
        InsightUserActor actor,
        Guid insightEventId,
        DateTimeOffset? seenAtUtc,
        DateTimeOffset? dismissedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc) =>
        new(id, actor, insightEventId, seenAtUtc, dismissedAtUtc, createdAtUtc, updatedAtUtc);

    public void MarkSeen(DateTimeOffset seenAtUtc)
    {
        SeenAtUtc ??= seenAtUtc;
        UpdatedAtUtc = seenAtUtc;
    }

    public void Dismiss(DateTimeOffset dismissedAtUtc)
    {
        SeenAtUtc ??= dismissedAtUtc;
        DismissedAtUtc ??= dismissedAtUtc;
        UpdatedAtUtc = dismissedAtUtc;
    }
}

public sealed record InsightEvidenceItem(
    string Label,
    string Value,
    string SourceProvider,
    string? SourcePeriod = null,
    DateTimeOffset? LastSyncedAtUtc = null);

public sealed record InsightScore(
    InsightSeverity Severity,
    decimal ImportanceScore,
    decimal ConfidenceScore);

public sealed record InsightScoringInput(
    decimal Magnitude,
    decimal SourceConfidence,
    decimal EvidenceCompleteness,
    decimal FreshnessScore,
    decimal RarityScore);

public sealed class InsightEvent
{
    public InsightEvent(
        Guid id,
        string externalCompanyId,
        string symbol,
        string? industryCode,
        InsightType insightType,
        InsightSeverity severity,
        decimal importanceScore,
        decimal confidenceScore,
        string title,
        string summary,
        string reason,
        IReadOnlyCollection<InsightEvidenceItem> evidence,
        string sourceProviderName,
        InsightSourceEntityType sourceEntityType,
        string? sourceEntityId,
        string? sourcePeriod,
        DateTimeOffset detectedAtUtc,
        DateTimeOffset? expiresAtUtc,
        string deduplicationKey,
        IReadOnlyCollection<InsightAction>? suggestedActions = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Insight event id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(externalCompanyId)) throw new ArgumentException("External company id is required.", nameof(externalCompanyId));
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Symbol is required.", nameof(symbol));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("Summary is required.", nameof(summary));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required.", nameof(reason));
        if (string.IsNullOrWhiteSpace(sourceProviderName)) throw new ArgumentException("Source provider name is required.", nameof(sourceProviderName));
        if (string.IsNullOrWhiteSpace(deduplicationKey)) throw new ArgumentException("Deduplication key is required.", nameof(deduplicationKey));

        Id = id;
        ExternalCompanyId = externalCompanyId.Trim();
        Symbol = symbol.Trim();
        IndustryCode = string.IsNullOrWhiteSpace(industryCode) ? null : industryCode.Trim();
        InsightType = insightType;
        Severity = severity;
        ImportanceScore = ClampScore(importanceScore);
        ConfidenceScore = ClampScore(confidenceScore);
        Title = title.Trim();
        Summary = summary.Trim();
        Reason = reason.Trim();
        Evidence = evidence?.ToArray() ?? [];
        SourceProviderName = sourceProviderName.Trim();
        SourceEntityType = sourceEntityType;
        SourceEntityId = string.IsNullOrWhiteSpace(sourceEntityId) ? null : sourceEntityId.Trim();
        SourcePeriod = string.IsNullOrWhiteSpace(sourcePeriod) ? null : sourcePeriod.Trim();
        DetectedAtUtc = detectedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        DeduplicationKey = deduplicationKey.Trim();
        SuggestedActions = suggestedActions?.ToArray() ?? DefaultActions(sourceEntityId);
    }

    public Guid Id { get; }

    public string ExternalCompanyId { get; }

    public string Symbol { get; }

    public string? IndustryCode { get; }

    public InsightType InsightType { get; }

    public InsightSeverity Severity { get; }

    public decimal ImportanceScore { get; }

    public decimal ConfidenceScore { get; }

    public string Title { get; }

    public string Summary { get; }

    public string Reason { get; }

    public IReadOnlyCollection<InsightEvidenceItem> Evidence { get; }

    public string SourceProviderName { get; }

    public InsightSourceEntityType SourceEntityType { get; }

    public string? SourceEntityId { get; }

    public string? SourcePeriod { get; }

    public DateTimeOffset DetectedAtUtc { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }

    public string DeduplicationKey { get; }

    public IReadOnlyCollection<InsightAction> SuggestedActions { get; }

    private static decimal ClampScore(decimal value) => Math.Clamp(value, 0m, 100m);

    private static IReadOnlyCollection<InsightAction> DefaultActions(string? sourceEntityId) =>
        string.IsNullOrWhiteSpace(sourceEntityId)
            ? [InsightAction.OpenSymbol, InsightAction.AskAiAboutThis]
            : [InsightAction.OpenSymbol, InsightAction.AskAiAboutThis, InsightAction.OpenSourceReport];
}

public sealed class DeterministicInsightScoringService : IInsightScoringService
{
    public InsightScore Score(InsightScoringInput input)
    {
        var importance = Clamp(
            input.Magnitude * 0.45m +
            input.FreshnessScore * 0.25m +
            input.RarityScore * 0.20m +
            input.SourceConfidence * 0.10m);

        var confidence = Clamp(
            input.SourceConfidence * 0.45m +
            input.EvidenceCompleteness * 0.35m +
            input.FreshnessScore * 0.20m);

        var severity = importance switch
        {
            >= 85m => InsightSeverity.Critical,
            >= 65m => InsightSeverity.Important,
            >= 40m => InsightSeverity.Notice,
            _ => InsightSeverity.Informational
        };

        return new InsightScore(severity, Math.Round(importance, 2), Math.Round(confidence, 2));
    }

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 100m);
}

public interface IInsightScoringService
{
    InsightScore Score(InsightScoringInput input);
}
