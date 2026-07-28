namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class SavedFilterRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string FilterCode { get; set; } = string.Empty;
    public string FilterVersion { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
    public int Version { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
}
