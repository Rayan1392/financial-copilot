namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

// Feature 148: independent CyclicalWaves v2 persistence in schema "cyclicalwaves".
// These rows reference only each other; nothing here points at a NoavaranAmin or legacy table.

public sealed class CyclicalWavesV2TickerRow
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string NormalizedTicker { get; set; } = string.Empty;
    public string? Enticker { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset LastSeenInCatalogAt { get; set; }
}

public sealed class CyclicalWavesV2MetricDefinitionRow
{
    public short Id { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameFa { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public short SortOrder { get; set; }
}

public sealed class CyclicalWavesV2FinancialValueRow
{
    public Guid TickerId { get; set; }
    public short MetricDefinitionId { get; set; }
    public decimal? NumericValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2ScreenerMetadataRow
{
    public Guid TickerId { get; set; }
    public DateOnly? LastMonthSaleDate { get; set; }
    public DateOnly? LastQuarterDate { get; set; }
    public DateTimeOffset? ProviderUpdatedAt { get; set; }
    public DateTimeOffset LastChangedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}

public sealed class CyclicalWavesV2IncomeStatementLineDefinitionRow
{
    public int Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameFa { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2IncomeStatementRow
{
    public Guid Id { get; set; }
    public Guid TickerId { get; set; }
    public DateOnly PeriodEndingDate { get; set; }
    public int FiscalYear { get; set; }
    public int FiscalMonth { get; set; }
    public bool? Audited { get; set; }
    public DateTimeOffset? PublishDate { get; set; }
    public string? CodalUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2IncomeStatementValueRow
{
    public Guid StatementId { get; set; }
    public int LineDefinitionId { get; set; }
    public int SortOrder { get; set; }
    public decimal? Quarter { get; set; }
    public decimal? YearToDate { get; set; }
    public decimal? TrailingYear { get; set; }
}

public sealed class CyclicalWavesV2AnalysisRow
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? PlainTextSummary { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset FirstSyncedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CyclicalWavesV2AnalysisCategoryRow
{
    public long AnalysisId { get; set; }
    public long ProviderCategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class CyclicalWavesV2AnalysisTagRow
{
    public long AnalysisId { get; set; }
    public long ProviderTagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
}

public sealed class CyclicalWavesV2SyncRunRow
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string? TickerFilter { get; set; }
    public string? AnalysisMode { get; set; }
    public string TriggerSource { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int UpdatedItems { get; set; }
    public int UnchangedItems { get; set; }
    public int FailedItems { get; set; }
    public string? ErrorSummary { get; set; }
    public string? LockOwner { get; set; }
    public DateTimeOffset? LockLeaseExpiresAt { get; set; }
    public string? RequestedBy { get; set; }
}
