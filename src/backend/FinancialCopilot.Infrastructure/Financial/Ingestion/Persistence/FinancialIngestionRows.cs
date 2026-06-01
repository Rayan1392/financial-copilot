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

    /// <summary>
    /// Stringified <c>FinancialCopilot.Domain.Financial.Entities.FinancialStatementType</c> value
    /// — <c>IncomeStatement</c>, <c>BalanceSheet</c>, or <c>CashFlow</c>. Distinguishes the kind
    /// of statement (spec 029); the period duration lives in <see cref="PeriodType"/>.
    /// </summary>
    public string StatementType { get; set; } = string.Empty;

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

    /// <summary>Set by <c>MetricRecalculationProcessor</c> once the row has been processed.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>Truncated to 1000 chars; null after a successful attempt.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Per-dataset incremental-sync watermark for the CodalDB scheduled orchestrator (spec 027). One row
/// per <see cref="Dataset"/>, holding the maximum source <c>ModifiedDateTime</c> we have successfully
/// enqueued — the next incremental run only considers rows newer than this.
/// </summary>
public sealed class CodalDbSyncStateRow
{
    public string Dataset { get; set; } = string.Empty;

    public DateTimeOffset? LastSyncedModifiedDateTime { get; set; }

    public DateTimeOffset? LastRunStartedAt { get; set; }

    public DateTimeOffset? LastRunCompletedAt { get; set; }
}

public sealed class TradingInstrumentRow
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public Guid ExternalInstrumentId { get; set; }
    public long InstrumentCode { get; set; }
    public string InstrumentIsin { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MarketCode { get; set; } = string.Empty;
    public string InstrumentKind { get; set; } = string.Empty;
    public Guid? NormalizedCompanyId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset SourceChangedAt { get; set; }
    public DateTimeOffset LastSynchronizedAt { get; set; }
}

public sealed class IntradayTradeSnapshotRow
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public Guid ExternalSnapshotId { get; set; }
    public Guid TradingInstrumentId { get; set; }
    public DateOnly TradingDate { get; set; }
    public TimeOnly? TradingTime { get; set; }
    public decimal ClosingPrice { get; set; }
    public decimal LastTradedPrice { get; set; }
    public decimal PriceChange { get; set; }
    public decimal PriceYesterday { get; set; }
    public decimal TotalTransactions { get; set; }
    public decimal Volume { get; set; }
    public decimal TotalCapital { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class DailyInstrumentTradeRow
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public long ExternalTradeId { get; set; }
    public Guid TradingInstrumentId { get; set; }
    public DateOnly TradingDate { get; set; }
    public decimal ClosingPrice { get; set; }
    public decimal LastTradedPrice { get; set; }
    public decimal PriceChange { get; set; }
    public decimal PriceYesterday { get; set; }
    public decimal TotalTransactions { get; set; }
    public decimal Volume { get; set; }
    public decimal TotalCapital { get; set; }
    public decimal MarketValue { get; set; }
    public DateTimeOffset SourceInsertedAt { get; set; }
}

public sealed class IntradayIndexSnapshotRow
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public Guid ExternalSnapshotId { get; set; }
    public Guid TradingInstrumentId { get; set; }
    public DateOnly TradingDate { get; set; }
    public TimeOnly? TradingTime { get; set; }
    public decimal Value { get; set; }
    public decimal? ChangePercent { get; set; }
    public DateTimeOffset SourceChangedAt { get; set; }
}

public sealed class DailyIndexSnapshotRow
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public Guid TradingInstrumentId { get; set; }
    public DateOnly TradingDate { get; set; }
    public decimal? Value { get; set; }
    public decimal? High { get; set; }
    public decimal? Low { get; set; }
    public decimal? ChangePercent { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class LatestMarketQuoteRow
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public Guid TradingInstrumentId { get; set; }
    public decimal LatestPrice { get; set; }
    public decimal PriceChangePercentage { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public DateTimeOffset AsOf { get; set; }
}

public sealed class StockMarketSyncStateRow
{
    public string Dataset { get; set; } = string.Empty;
    public DateTimeOffset? Watermark { get; set; }
    public DateTimeOffset? ContinuationWatermark { get; set; }
    public string? ContinuationExternalId { get; set; }
    public DateTimeOffset? LastRunStartedAt { get; set; }
    public DateTimeOffset? LastRunCompletedAt { get; set; }
}

/// <summary>
/// Persisted form of <c>MissingAnswerFeedback</c> (spec 028). Coalesced on
/// <c>(ActorId, QueryHashSha256, Classification, DateBucket)</c>.
/// </summary>
public sealed class MissingAnswerFeedbackRow
{
    public Guid Id { get; set; }

    public string ActorId { get; set; } = string.Empty;

    public string QueryText { get; set; } = string.Empty;

    public string QueryHashSha256 { get; set; } = string.Empty;

    public string Classification { get; set; } = string.Empty;

    public string? RequestedMetricCode { get; set; }

    public string? AffectedDataCodeOrName { get; set; }

    public int SymbolCountTotal { get; set; }

    public int SymbolCountMatched { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public DateOnly DateBucket { get; set; }

    public string? Context { get; set; }

    public int FrequencyCount { get; set; } = 1;

    public DateTimeOffset? ResolvedAt { get; set; }
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
