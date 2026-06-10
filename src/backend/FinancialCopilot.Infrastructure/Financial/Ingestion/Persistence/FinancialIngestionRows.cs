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

    /// <summary>Provider-local company code (NADPCO <c>coCode</c>).</summary>
    public string? CompanyCode { get; set; }

    /// <summary>Trading symbol (CodalDB <c>CompanySymbol</c>).</summary>
    public string? CompanySymbol { get; set; }

    /// <summary>English trading symbol (NADPCO <c>coSymbolEnglish</c>).</summary>
    public string? CompanySymbolEnglish { get; set; }

    /// <summary>Pinglish trading symbol (NADPCO <c>coSymbolPinglish</c>).</summary>
    public string? CompanySymbolPinglish { get; set; }

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

    public int? PrecedencyRight { get; set; }

    public string? AcceptionDateJalali { get; set; }

    public string? AcceptionDateGregorian { get; set; }

    public string? EnlistedDateJalali { get; set; }

    public string? EnlistedDateGregorian { get; set; }

    public string? IpoDateJalali { get; set; }

    public string? IpoDateGregorian { get; set; }

    public int? FundTypeId { get; set; }

    public string? FundTypeTitle { get; set; }

    public string? NationalId { get; set; }

    public int? InExchange { get; set; }

    public string? EstablishmentDateJalali { get; set; }

    public string? EstablishmentDateGregorian { get; set; }

    public string? BusinessStartDateJalali { get; set; }

    public string? BusinessStartDateGregorian { get; set; }

    public string? RegistrationDateJalali { get; set; }

    public string? RegistrationDateGregorian { get; set; }

    public string? RegistrationNumber { get; set; }

    public string? RegistrationProvince { get; set; }

    public string? RegistrationCity { get; set; }

    public string? MarketBoard { get; set; }

    /// <summary>
    /// Source row last-modified timestamp (CodalDB <c>ModifiedDateTime</c>), used as the
    /// incremental-sync watermark by the scheduled orchestrator (spec 027).
    /// </summary>
    public DateTimeOffset? SourceModifiedAt { get; set; }

    // --- Source provenance (spec 051). ProviderName carries the physical source name; these record
    //     the logical vendor and import mode so an issuer ingested from the archive can be told apart
    //     from the current-API feed without re-deriving from ProviderName. Nullable for legacy rows. ---

    /// <summary>Logical vendor name (<c>LogicalVendor</c>) that owns this record.</summary>
    public string? LogicalVendor { get; set; }

    /// <summary>Import mode (<c>SourceMode</c>) the record was ingested under.</summary>
    public string? SourceMode { get; set; }
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

    /// <summary>Logical vendor name (<c>LogicalVendor</c>) that owns this statement (spec 051).</summary>
    public string? LogicalVendor { get; set; }

    /// <summary>Import mode (<c>SourceMode</c>) the statement was ingested under (spec 051).</summary>
    public string? SourceMode { get; set; }
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

    /// <summary>Logical vendor name (<c>LogicalVendor</c>) that owns this monthly report (spec 051).</summary>
    public string? LogicalVendor { get; set; }

    /// <summary>Import mode (<c>SourceMode</c>) the monthly report was ingested under (spec 051).</summary>
    public string? SourceMode { get; set; }
}

public sealed class NormalizedMonthlyReportLineItemRow
{
    public Guid Id { get; set; }

    public Guid MonthlyReportId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public decimal? ProductionQuantity { get; set; }

    public decimal? SalesQuantity { get; set; }

    public decimal? SalesAmount { get; set; }

    /// <summary>Vendor product/service title (spec 057 model audit: normalized, not evidence-only).</summary>
    public string? Title { get; set; }

    /// <summary>Vendor measurement unit for the quantities (spec 057 model audit).</summary>
    public string? Unit { get; set; }

    /// <summary>Vendor per-line sales rate (price per unit) when supplied (spec 057 model audit).</summary>
    public decimal? SalesRate { get; set; }
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

    // --- Batch-level source provenance (spec 051 AC #7). Nullable for runs predating the model;
    //     populated from the resolved ProviderSources descriptor and the request. ---

    /// <summary>Logical vendor name (<c>LogicalVendor</c>), e.g. <c>NoavaranAmin</c>.</summary>
    public string? LogicalVendor { get; set; }

    /// <summary>Physical source name (<c>PhysicalSource</c>), e.g. <c>NoavaranArchiveSql</c>.</summary>
    public string? PhysicalSource { get; set; }

    /// <summary>Import mode (<c>SourceMode</c>), e.g. <c>ArchiveOneTime</c> / <c>CurrentIncremental</c>.</summary>
    public string? SourceMode { get; set; }

    /// <summary>Shamsi source date range covered by this run (inclusive start), if known.</summary>
    public string? SourceDateRangeStartJalali { get; set; }

    /// <summary>Shamsi source date range covered by this run (inclusive end), if known.</summary>
    public string? SourceDateRangeEndJalali { get; set; }
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

/// <summary>
/// Per logical NADPCO endpoint sync progress. NADPCO HTTP endpoints currently lack a reliable
/// modified-since cursor, so incremental orchestration records the overlap window used for
/// reconciliation rather than pretending the vendor has source watermarks.
/// </summary>
public sealed class NadpcoApiSyncStateRow
{
    public string Dataset { get; set; } = string.Empty;

    public DateTimeOffset? LastSuccessfulSyncAt { get; set; }

    public DateTimeOffset? LastOverlapFrom { get; set; }

    public DateTimeOffset? LastRunStartedAt { get; set; }

    public DateTimeOffset? LastRunCompletedAt { get; set; }

    public int LastCompaniesConsidered { get; set; }

    public int LastCompaniesEnqueued { get; set; }

    public int LastFailedCompanies { get; set; }

    public string? LastRunMode { get; set; }

    public string? LastError { get; set; }
}

public sealed class NadpcoScheduledSyncRunRow
{
    public Guid Id { get; set; }

    public string TriggerSource { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? LastSuccessfulExecutionAt { get; set; }

    public int ProcessedBatches { get; set; }

    public int FailedBatches { get; set; }

    public int RetryAttempts { get; set; }

    public string? Diagnostics { get; set; }

    public string ScheduleSnapshotJson { get; set; } = "{}";

    public string DatasetSelectionJson { get; set; } = "[]";

    public string? LockOwner { get; set; }

    public DateTimeOffset? LockLeaseExpiresAt { get; set; }

    public bool AlertEmitted { get; set; }

    public string? ManualReason { get; set; }
}

/// <summary>
/// One-time Noavaran archive import run history (spec 052). Records each DataAdmin archive action
/// (dry-run/import/validate/freeze/re-import) with counts, conflicts, failures, the reason, and a
/// running lease so a second concurrent import is rejected.
/// </summary>
public sealed class ArchiveImportRunRow
{
    public Guid Id { get; set; }

    /// <summary>Stringified <c>ArchiveImportAction</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Stringified <c>ArchiveImportRunStatus</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>JSON array of selected <c>ArchiveImportDataset</c> names (empty array = all datasets).</summary>
    public string DatasetSelectionJson { get; set; } = "[]";

    public string? Reason { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int CompaniesConsidered { get; set; }

    public int RequestsEnqueued { get; set; }

    public int SkippedCount { get; set; }

    public int ConflictCount { get; set; }

    public int FailedCount { get; set; }

    /// <summary>Whether this run left the archive source frozen.</summary>
    public bool Frozen { get; set; }

    public string? Diagnostics { get; set; }

    /// <summary>Running-lease owner; null when the run is finished.</summary>
    public string? LockOwner { get; set; }

    public DateTimeOffset? LockLeaseExpiresAt { get; set; }
}

/// <summary>
/// Single-row authoritative freeze marker for the Noavaran archive source (spec 052 AC #3/#5).
/// Keyed by the source name so it is unambiguous and never duplicated.
/// </summary>
public sealed class ArchiveFreezeStateRow
{
    public string SourceName { get; set; } = string.Empty;

    public bool IsFrozen { get; set; }

    public DateTimeOffset? FrozenAt { get; set; }

    public Guid? FrozenByRunId { get; set; }

    public string? Reason { get; set; }
}

/// <summary>
/// Single-row durable state for the manual Noavaran monthly-activity backfill (spec 057 Phase A).
/// <see cref="IsCompleted"/> is the backfill-complete marker that switches the scheduled
/// monthly-activity refresh to previous-Shamsi-month-only mode (Phase B). Per-company-month
/// progress is owned by <c>ProviderSyncRuns</c> via deterministic idempotency keys; this row holds
/// the planned scope and lifecycle facts.
/// </summary>
public sealed class MonthlyActivityBackfillStateRow
{
    public string SourceName { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? LastStartedAt { get; set; }

    public string? RequestedBy { get; set; }

    /// <summary>JSON array of planned months: [{"y":1405,"m":2,"companies":700}, …] newest first.</summary>
    public string PlannedMonthsJson { get; set; } = "[]";
}

/// <summary>
/// All-index fundamental-index coverage observation (spec 050). One row per canonical
/// (provider, company, index, period type, period end) vendor index value. This is a NON-SCANNABLE
/// staging/coverage model: the scanner never reads it, and only the curated 041 path promotes
/// reviewed indexes into <see cref="DerivedMetricRow"/>. Idempotent upsert on the unique key.
/// </summary>
public sealed class NadpcoFundamentalIndexObservationRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string ExternalCompanyId { get; set; } = string.Empty;

    public string? CompanyTitle { get; set; }

    public long ExternalStatementId { get; set; }

    public int CompanyIndexId { get; set; }

    public string? CompanyIndexTitle { get; set; }

    public int? CompanyIndexGroupId { get; set; }

    public string? CompanyIndexGroupTitle { get; set; }

    public decimal? CompanyIndexValue { get; set; }

    public string? CompanyIndexUnit { get; set; }

    /// <summary>Vendor period type code (3/6/9/12 months).</summary>
    public int PeriodType { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public string? JalaliFiscalYearEnd { get; set; }

    public string? JalaliPeriodEnd { get; set; }

    public string? JalaliAnnouncementDate { get; set; }

    public bool IsAudited { get; set; }

    public bool IsRepresented { get; set; }

    public bool IsComposing { get; set; }

    /// <summary>True when the curated 041 allowlist maps this index id to a governed metric.</summary>
    public bool IsGovernedCandidate { get; set; }

    /// <summary>SHA-256 of the source raw payload this observation came from (provenance).</summary>
    public string SourcePayloadChecksum { get; set; } = string.Empty;

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

/// <summary>
/// Run history for the DataAdmin all-index fundamental-index coverage catch-up (spec 050 AC #9).
/// Mirrors the spec-052 archive-import run pattern (lease + hung recovery).
/// </summary>
public sealed class FundamentalIndexCatchUpRunRow
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public int FromShamsiYear { get; set; }

    public int ToShamsiYear { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int CompaniesConsidered { get; set; }

    public int RequestsEnqueued { get; set; }

    public int FailedCompanies { get; set; }

    /// <summary>JSON array of failed company ids.</summary>
    public string FailedCompanyIdsJson { get; set; } = "[]";

    public string? Diagnostics { get; set; }

    public string? LockOwner { get; set; }

    public DateTimeOffset? LockLeaseExpiresAt { get; set; }
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
    public Guid ExternalTradeId { get; set; }
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
    public DateOnly TradingDate { get; set; }
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

public sealed class WatchlistSymbolRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
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
