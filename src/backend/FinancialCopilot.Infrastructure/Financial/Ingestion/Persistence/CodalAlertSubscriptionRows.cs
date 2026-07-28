namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CodalAlertSubscriptionRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ActorId { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string AnnouncementTypesJson { get; set; } = "[]";

    public string MinimumImportance { get; set; } = string.Empty;

    public bool RawAlertEnabled { get; set; }

    public bool AiSummaryEnabled { get; set; }

    public string State { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
