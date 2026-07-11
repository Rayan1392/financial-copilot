namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class UserInsightStateRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ActorId { get; set; }

    public string ActorType { get; set; } = string.Empty;

    public Guid InsightEventId { get; set; }

    public DateTimeOffset? SeenAtUtc { get; set; }

    public DateTimeOffset? DismissedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
