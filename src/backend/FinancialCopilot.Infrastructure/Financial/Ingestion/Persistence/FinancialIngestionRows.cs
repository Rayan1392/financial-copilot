namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class NormalizedCompanyRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

public sealed class NormalizedSymbolRow
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalSymbolId { get; set; } = string.Empty;

    public string SymbolCode { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

public sealed class NormalizedFinancialStatementRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string ExternalStatementId { get; set; } = string.Empty;

    public string PeriodType { get; set; } = string.Empty;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string SourcePayloadChecksum { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

public sealed class NormalizedFinancialStatementLineItemRow
{
    public Guid Id { get; set; }

    public Guid FinancialStatementId { get; set; }

    public string MetricCode { get; set; } = string.Empty;

    public decimal? Value { get; set; }
}

public sealed class NormalizedMonthlyReportRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string ExternalReportId { get; set; } = string.Empty;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string SourcePayloadChecksum { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

public sealed class NormalizedMonthlyReportLineItemRow
{
    public Guid Id { get; set; }

    public Guid MonthlyReportId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public decimal? ProductionQuantity { get; set; }

    public decimal? SalesQuantity { get; set; }

    public decimal? SalesAmount { get; set; }
}

public sealed class DataSyncRunRow
{
    public Guid Id { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Dataset { get; set; } = string.Empty;

    public string? ExternalReference { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int ProcessedRecords { get; set; }

    public int ErrorCount { get; set; }

    public string? ErrorMessage { get; set; }

    public string? SourcePayloadChecksum { get; set; }
}

public sealed class MetricRecalculationRequestRow
{
    public Guid Id { get; set; }

    public string SourceDataset { get; set; } = string.Empty;

    public string? ExternalReference { get; set; }

    public string SourcePayloadChecksum { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }
}

public sealed class DerivedMetricRow
{
    public Guid Id { get; set; }

    public Guid SymbolId { get; set; }

    public string MetricCode { get; set; } = string.Empty;

    public string MetricVersion { get; set; } = string.Empty;

    public string CalculationPolicyVersion { get; set; } = string.Empty;

    public string PeriodType { get; set; } = string.Empty;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public decimal? Value { get; set; }

    public string Unit { get; set; } = string.Empty;

    public DateTimeOffset ObservedAt { get; set; }

    public DateTimeOffset LastSynchronizedAt { get; set; }

    public string WarningsJson { get; set; } = "[]";

    public string SourceEvidenceJson { get; set; } = "[]";

    public string DependencyEvidenceJson { get; set; } = "[]";
}
