namespace FinancialCopilot.API.Contracts;

public sealed record AdminDataSyncRequest(
    string? ExternalReference = null,
    string? IdempotencyKey = null,
    string? ProviderName = null);

public sealed record AdminDataSyncQueuedResponse(
    Guid RequestId,
    string Dataset,
    string? ExternalReference,
    DateTimeOffset RequestedAt,
    string IdempotencyKey,
    string Status);

public sealed record AdminDataSyncRunResponse(
    Guid RunId,
    string Dataset,
    string? ExternalReference,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int ProcessedRecords,
    int ErrorCount,
    string? ErrorMessage,
    string? SourcePayloadChecksum);

public sealed record AdminProviderHealthResponse(
    string ProviderName,
    string Status,
    DateTimeOffset CheckedAt,
    string? Detail);

/// <summary>
/// Per-source ingestion freshness (spec 051). Archive sources report <c>IsFrozenArchive=true</c> once
/// their one-time import completes and are not flagged stale by absence of recent runs; current
/// sources report freshness against their last successful run.
/// </summary>
public sealed record AdminSourceFreshnessResponse(
    string LogicalVendor,
    string PhysicalSource,
    string SourceMode,
    string SourceName,
    bool IsFrozenArchive,
    DateTimeOffset? LastSuccessfulRunAt,
    int RecentSuccessfulRuns,
    int RecentFailedRuns);

// --- Spec 052: one-time Noavaran archive import ---

public sealed record AdminArchiveImportRequest(
    string[]? Datasets = null,
    string? Reason = null);

public sealed record AdminArchiveImportRunResponse(
    Guid RunId,
    string Action,
    string Status,
    string RequestedBy,
    IReadOnlyCollection<string> Datasets,
    string? Reason,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int CompaniesConsidered,
    int RequestsEnqueued,
    int SkippedCount,
    int ConflictCount,
    int FailedCount,
    bool Frozen,
    string? Diagnostics);

public sealed record AdminArchiveFreezeStateResponse(
    bool IsFrozen,
    DateTimeOffset? FrozenAt,
    Guid? FrozenByRunId,
    string? Reason);

public sealed record AdminArchiveCoverageResponse(
    string SourceName,
    int CompanyCount,
    IReadOnlyDictionary<string, int> RowCountByDataset,
    IReadOnlyDictionary<int, int> RowCountByFiscalYear);

public sealed record AdminArchiveImportValidationResponse(
    bool CompanyMappingValid,
    int CompaniesWithoutCanonicalSymbol,
    IReadOnlyCollection<string> UnmappedExternalCompanyIds,
    AdminArchiveCoverageResponse Coverage);

// --- Spec 050: NADPCO all-index fundamental-index catch-up coverage ---

public sealed record AdminFundamentalIndexCatchUpRequest(
    int FromShamsiYear = 1403,
    int ToShamsiYear = 1405);

public sealed record AdminFundamentalIndexCatchUpRunResponse(
    Guid RunId,
    string Status,
    string RequestedBy,
    int FromShamsiYear,
    int ToShamsiYear,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int CompaniesConsidered,
    int RequestsEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    string? Diagnostics);

// --- Spec 053: Noavaran current-API ingestion ---

public sealed record AdminCurrentApiBackfillRequest(
    int? FromShamsiYear = null);

public sealed record AdminCurrentApiBackfillResponse(
    bool FullReload,
    int? AppliedFromShamsiYear,
    int CompaniesConsidered,
    int RequestsEnqueued,
    int FailedCompanies,
    string Duration);

public sealed record AdminCurrentApiHealthResponse(
    string SourceName,
    string ProviderHealthStatus,
    string? ProviderHealthDetail,
    bool ScheduledSyncEnabled,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? NextDueAt,
    DateTimeOffset CheckedAt);

// --- Spec 057: NADPCO monthly-activity reverse-chronological backfill ---

public sealed record AdminMonthlyActivityBackfillStartResponse(
    string Outcome,
    int MonthsPlanned,
    int CompaniesPlanned,
    int RequestsEnqueued,
    AdminMonthlyActivityBackfillProgressResponse Progress);

public sealed record AdminMonthlyActivityBackfillMonthResponse(
    int ShamsiYear,
    int ShamsiMonth,
    int CompaniesPlanned,
    int CompaniesCompleted,
    int CompaniesFailed,
    string Status);

public sealed record AdminMonthlyActivityBackfillProgressResponse(
    bool Started,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastStartedAt,
    string? RequestedBy,
    IReadOnlyCollection<AdminMonthlyActivityBackfillMonthResponse> Months,
    IReadOnlyDictionary<int, int>? OutputTypeCounts = null);

public sealed record AdminCurrentApiGapResponse(
    int CurrentApiBoundaryShamsiYear,
    int TotalGapRows,
    IReadOnlyCollection<AdminCurrentApiGapItem> Gaps);

public sealed record AdminCurrentApiGapItem(
    string Dataset,
    string ExternalCompanyId,
    int FiscalYear,
    int CurrentApiRowCount,
    int ArchiveRowCount);

public sealed record AdminCyclicalWavesFullSyncResponse(
    int SymbolsSynced,
    int TickersSynced,
    int TickersFailed,
    IReadOnlyCollection<string> FailedTickers,
    string Duration);

public sealed record AdminCodalDbSyncResponse(
    bool FullReload,
    int CompaniesConsidered,
    int CompaniesEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    DateTimeOffset? AdvancedWatermark,
    string Duration);

public sealed record AdminNadpcoApiSyncResponse(
    string RunMode,
    bool FullReload,
    int CompaniesConsidered,
    int CompaniesEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    int RequestsEnqueued,
    DateTimeOffset? OverlapFrom,
    DateTimeOffset? AdvancedWatermark,
    string Duration,
    AdminNadpcoCompanyCatalogCleanSlateResponse? CleanSlate);

public sealed record AdminNadpcoCompanyCatalogCleanSlateResponse(
    int MetricRecalculationRequestsDeleted,
    int FeatureComputationJobsDeleted,
    int FeatureSnapshotsDeleted,
    int DerivedMetricsDeleted,
    int SymbolsDeleted,
    int TradingInstrumentLinksCleared,
    int CompaniesDeleted);

public sealed record AdminNadpcoApiSyncStateResponse(
    string Dataset,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? LastOverlapFrom,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    int LastCompaniesConsidered,
    int LastCompaniesEnqueued,
    int LastFailedCompanies,
    string? LastRunMode,
    string? LastError);

public sealed record AdminNadpcoScheduledSyncManualRunRequest(
    string? Reason = null);

public sealed record AdminNadpcoScheduledSyncRunResponse(
    Guid RunId,
    string TriggerSource,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastSuccessfulExecutionAt,
    int ProcessedBatches,
    int FailedBatches,
    int RetryAttempts,
    string? Diagnostics,
    string ScheduleSnapshotJson,
    string DatasetSelectionJson,
    string? LockOwner,
    DateTimeOffset? LockLeaseExpiresAt,
    bool AlertEmitted,
    string? ManualReason);

public sealed record AdminNadpcoScheduledSyncStatusResponse(
    bool Enabled,
    bool Ready,
    DateTimeOffset? NextDueAt,
    DateTimeOffset? LastSuccessfulExecutionAt,
    AdminNadpcoScheduledSyncRunResponse? ActiveRun,
    IReadOnlyCollection<AdminNadpcoScheduledSyncRunResponse> RecentRuns);

public sealed record AdminStockMarketSyncResponse(
    string Dataset,
    int RowsRead,
    int RowsPersisted,
    DateTimeOffset? AdvancedWatermark,
    string Duration);

public sealed record AdminStockMarketSyncStateResponse(
    string Dataset,
    DateTimeOffset? Watermark,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    string? LogicalVendor,
    string? PhysicalSource,
    string? SourceMode);

public sealed record AdminMissingAnswerFeedbackItem(
    Guid Id,
    string ActorId,
    string QueryText,
    string Classification,
    string? RequestedMetricCode,
    string? AffectedDataCodeOrName,
    int SymbolCountTotal,
    int SymbolCountMatched,
    DateTimeOffset SubmittedAt,
    int FrequencyCount,
    DateTimeOffset? ResolvedAt);

// --- Spec 058: live data sync monitor ---

public sealed record AdminDataSyncActivityItemResponse(
    string RunId,
    string Provider,
    string Dataset,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    int ProcessedRecords,
    int ErrorCount,
    string? ErrorMessage,
    string TriggerSource,
    string? RequestedShamsiMonth,
    string? LogicalVendor,
    string? PhysicalSource,
    string? SourceMode);

public sealed record AdminDataSyncActivitySnapshotResponse(
    IReadOnlyCollection<AdminDataSyncActivityItemResponse> ActiveRuns,
    IReadOnlyCollection<AdminDataSyncActivityItemResponse> RecentRuns);

public sealed record AdminMissingAnswerFeedbackSummary(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyDictionary<string, int> CountsByClassification,
    int TotalCount);

public sealed record AdminTsetmcDirectFeedStatusResponse(
    bool IsOperational,
    string PhysicalSource,
    string Notes);

public sealed record AdminTsetmcSyncResponse(
    string Dataset,
    int RowsFetched,
    int RowsPersisted,
    string Duration);
