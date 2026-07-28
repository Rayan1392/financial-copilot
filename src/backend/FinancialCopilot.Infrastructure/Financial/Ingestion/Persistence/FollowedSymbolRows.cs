namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class FollowedSymbolRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ActorId { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string? CompanyNameEnglish { get; set; }

    public string? Source { get; set; }

    public DateTimeOffset FollowedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
