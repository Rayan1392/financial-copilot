using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Financial.Reports;

namespace FinancialCopilot.Application.FinancialData.MarketReports;

public sealed record MarketReportEvidenceItem(
    string Id,
    string Kind,
    string Label,
    string Text,
    IReadOnlyList<string> NumericValues,
    string? Unit,
    string Source,
    DateTimeOffset? FreshnessUtc,
    decimal? Confidence);

public sealed record MarketReportEvidenceBundle(
    string SchemaVersion,
    DateOnly TradingDate,
    string WindowKey,
    bool IsPartial,
    bool IsFinal,
    IReadOnlyList<Guid> SnapshotIds,
    IReadOnlyList<Guid> InsightEventIds,
    IReadOnlyList<string> FollowedSymbols,
    IReadOnlyList<MarketReportEvidenceItem> Items,
    IReadOnlyList<string> Caveats,
    IReadOnlyList<string> ExcludedReasons,
    DateTimeOffset? SourceFreshnessUtc,
    decimal Confidence,
    DateTimeOffset AssembledAtUtc);

public sealed record MarketReportView(
    Guid Id,
    MarketReportScope Scope,
    MarketReportStatus Status,
    DateOnly TradingDate,
    string WindowKey,
    int Revision,
    Guid? SupersedesReportId,
    string ReportVersion,
    string EvidenceSchemaVersion,
    string PromptPolicyVersion,
    string RenderingPolicyVersion,
    string SafetyPolicyVersion,
    string EvidenceHash,
    MarketReportEvidenceBundle Evidence,
    string? Narrative,
    IReadOnlyList<string> Caveats,
    decimal Confidence,
    string? ProviderName,
    string? ModelName,
    string? FailureReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? GeneratedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    bool IsCurrent,
    string Disclaimer);

public sealed record MarketReportHistoryQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    MarketReportStatus? Status = null,
    int Page = 1,
    int PageSize = 20);

public sealed record MarketReportHistoryPage(
    IReadOnlyList<MarketReportView> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GeneratePersonalDigestCommand(
    CurrentActor Actor,
    string CorrelationId,
    bool PublishNotification = false);

public sealed record GeneratePublicMarketReportCommand(
    string Segment,
    string CorrelationId);

public interface IMarketReportService
{
    Task<MarketReportView?> GetLatestPublicAsync(CancellationToken cancellationToken);
    Task<MarketReportHistoryPage> GetPublicHistoryAsync(MarketReportHistoryQuery query, CancellationToken cancellationToken);
    Task<MarketReportView?> GetPublicVersionAsync(Guid reportId, CancellationToken cancellationToken);
    Task<MarketReportView> GeneratePublicAsync(GeneratePublicMarketReportCommand command, CancellationToken cancellationToken);
    Task<MarketReportView?> GetLatestPersonalAsync(CurrentActor actor, CancellationToken cancellationToken);
    Task<MarketReportHistoryPage> GetPersonalHistoryAsync(CurrentActor actor, MarketReportHistoryQuery query, CancellationToken cancellationToken);
    Task<MarketReportView?> GetPersonalVersionAsync(CurrentActor actor, Guid reportId, CancellationToken cancellationToken);
    Task<MarketReportView> GeneratePersonalAsync(GeneratePersonalDigestCommand command, CancellationToken cancellationToken);
}

public interface IMarketReportScheduler
{
    Task<int> GenerateDueAsync(CancellationToken cancellationToken);
}

public sealed class MarketReportValidationException(string message) : Exception(message);

public sealed class MarketReportAccessDeniedException(string message) : Exception(message);
