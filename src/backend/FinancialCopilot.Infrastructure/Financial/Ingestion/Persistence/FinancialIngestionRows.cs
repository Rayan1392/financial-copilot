namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class NormalizedCompanyRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }

    // --- Enriched company master-data attributes (nullable; populated by richer providers such
    //     as CodalDb. Existing providers leave them null.) ---

    /// <summary>English company name (CodalDB <c>CoNameEnglish</c>).</summary>
    public string? NameEnglish { get; set; }

    /// <summary>Trading symbol (CodalDB <c>CompanySymbol</c>).</summary>
    public string? CompanySymbol { get; set; }

    /// <summary>TSE symbol (CodalDB <c>CoTSESymbol</c>).</summary>
    public string? TseSymbol { get; set; }

    /// <summary>TSETMC instrument code (CodalDB <c>InstCode</c>).</summary>
    public string? InstrumentCode { get; set; }

    /// <summary>Company ISIN (CodalDB <c>TseCIsinCode</c>).</summary>
    public string? CompanyIsin { get; set; }

    /// <summary>Symbol/share ISIN (CodalDB <c>TseSIsinCode</c>).</summary>
    public string? SymbolIsin { get; set; }

    /// <summary>
    /// CodalDB <c>InstrumentRef</c> retained verbatim for provenance. NON-IDENTIFYING: it is a
    /// single constant GUID shared by all rows, so it must never be indexed or used as a join or
    /// symbol-resolution key. Use <see cref="InstrumentCode"/>, ISINs, or <see cref="TseSymbol"/>.
    /// </summary>
    public string? InstrumentRefPlaceholder { get; set; }

    /// <summary>FK to <see cref="NormalizedIndustryRow"/> (nullable).</summary>
    public Guid? IndustryId { get; set; }

    /// <summary>FK to <see cref="NormalizedIndustryGroupRow"/> (nullable).</summary>
    public Guid? GroupId { get; set; }

    /// <summary>FK to <see cref="NormalizedMarketRow"/> (nullable).</summary>
    public Guid? MarketId { get; set; }

    /// <summary>
    /// Source row last-modified timestamp (CodalDB <c>ModifiedDateTime</c>), used as the
    /// incremental-sync watermark by the scheduled orchestrator (spec 027).
    /// </summary>
    public DateTimeOffset? SourceModifiedAt { get; set; }
}

public sealed class NormalizedSymbolRow
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalSymbolId { get; set; } = string.Empty;

    public string SymbolCode { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }

    /// <summary>
    /// Which identifier produced <see cref="SymbolCode"/> (e.g. <c>SymbolIsin</c>), recorded so
    /// the canonical value is reproducible/auditable and cross-provider alignment is explainable.
    /// </summary>
    public string? LinkageBasis { get; set; }
}

/// <summary>Industry classification dimension (provider-scoped; ready for future hierarchy).</summary>
public sealed class NormalizedIndustryRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional parent industry (future hierarchy expansion).</summary>
    public Guid? ParentId { get; set; }

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

/// <summary>Super-sector group classification dimension (provider-scoped).</summary>
public sealed class NormalizedIndustryGroupRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

/// <summary>Market/board classification dimension (provider-scoped).</summary>
public sealed class NormalizedMarketRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

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

    public string WarningsJson { get; set; } = "[]";
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

    public string WarningsJson { get; set; } = "[]";
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

    /// <summary>Optional provider this run targeted; null means the configured primary provider.</summary>
    public string? ProviderName { get; set; }

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

public sealed class FeatureDefinitionRow
{
    public Guid Id { get; set; }

    public string FeatureCode { get; set; } = string.Empty;

    public string FeatureVersion { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PolicyVersion { get; set; } = string.Empty;

    public int RequiredObservationWindow { get; set; }

    public string Unit { get; set; } = string.Empty;

    public decimal? MinimumValue { get; set; }

    public decimal? MaximumValue { get; set; }

    public string StrategyKey { get; set; } = string.Empty;

    public string AlgorithmVersion { get; set; } = string.Empty;

    public string InputSchemaVersion { get; set; } = string.Empty;

    public string DependenciesJson { get; set; } = "[]";
}

public sealed class FeatureSnapshotRow
{
    public Guid Id { get; set; }

    public Guid SymbolId { get; set; }

    public string FeatureCode { get; set; } = string.Empty;

    public string FeatureVersion { get; set; } = string.Empty;

    public string PolicyVersion { get; set; } = string.Empty;

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

    public string InputFingerprint { get; set; } = string.Empty;
}

public sealed class FeatureComputationJobRow
{
    public Guid Id { get; set; }

    public string FeatureCode { get; set; } = string.Empty;

    public string FeatureVersion { get; set; } = string.Empty;

    public Guid? SymbolId { get; set; }

    public string PeriodType { get; set; } = string.Empty;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }
}
