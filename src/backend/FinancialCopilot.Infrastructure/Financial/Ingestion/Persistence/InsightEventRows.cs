namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class InsightEventRow
{
    public Guid Id { get; set; }

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string? IndustryCode { get; set; }

    public string InsightType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public decimal ImportanceScore { get; set; }

    public decimal ConfidenceScore { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string EvidenceJson { get; set; } = "[]";

    public string SourceProviderName { get; set; } = string.Empty;

    public string SourceEntityType { get; set; } = string.Empty;

    public string? SourceEntityId { get; set; }

    public string? SourcePeriod { get; set; }

    public DateTimeOffset DetectedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public string DeduplicationKey { get; set; } = string.Empty;

    public string SuggestedActionsJson { get; set; } = "[]";
}
