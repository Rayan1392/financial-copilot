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

public sealed record AdminEligibleFundamentalIndexBulkSyncRequest(
    string? ProviderName = null,
    string? IdempotencyKey = null,
    int? MaxItems = null,
    bool DryRun = false);

public sealed record AdminEligibleFundamentalIndexBulkSyncItemResponse(
    string ExternalReference,
    string Status,
    string IdempotencyKey,
    string? Error);

public sealed record AdminEligibleFundamentalIndexBulkSyncResponse(
    Guid RequestId,
    string Dataset,
    string Source,
    DateTimeOffset RequestedAt,
    string IdempotencyKey,
    string Status,
    int EligibleCount,
    int QueuedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyCollection<AdminEligibleFundamentalIndexBulkSyncItemResponse> Items);

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

public sealed record AdminMonthlyActivitySingleMonthBackfillRequest(
    int ShamsiYear,
    int ShamsiMonth);

public sealed record AdminMonthlyActivitySingleCompanyMonthDirectRequest(
    int CompanyId,
    int ShamsiYear,
    int ShamsiMonth);

public sealed record AdminMonthlyActivitySingleCompanyMonthDirectResponse(
    Guid RequestId,
    int CompanyId,
    int ShamsiYear,
    int ShamsiMonth,
    string Status,
    bool AlreadyProcessed,
    int ProcessedRecords,
    int ErrorCount,
    string? ErrorMessage,
    DateTimeOffset? CompletedAt);

public sealed record AdminMonthlyActivityBackfillStartResponse(
    Guid? BatchId,
    string Outcome,
    int MonthsPlanned,
    int CompaniesPlanned,
    int RequestsEnqueued,
    AdminMonthlyActivityBackfillProgressResponse Progress);

public sealed record AdminMonthlyActivityBackfillBatchResponse(
    Guid BatchId,
    string Status,
    string RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishingStartedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? CompletedAt,
    int? TargetShamsiYear,
    int? TargetShamsiMonth,
    int PlannedCount,
    int PublishedCount,
    int ProcessedCount,
    int FailedCount,
    int RetryableCount,
    string? LastError);

public sealed record AdminMonthlyActivityBackfillMonthResponse(
    int ShamsiYear,
    int ShamsiMonth,
    int CompaniesPlanned,
    int CompaniesCompleted,
    int CompaniesNoDataYet,
    int CompaniesFailed,
    string Status);

public sealed record AdminMonthlyActivityBackfillProgressResponse(
    bool Started,
    bool IsCompleted,
    string Status,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastStartedAt,
    string? RequestedBy,
    IReadOnlyCollection<AdminMonthlyActivityBackfillMonthResponse> Months,
    IReadOnlyDictionary<int, int>? OutputTypeCounts = null);

// --- Spec 075: one-time product revenue mix backfill ---

public sealed record AdminProductRevenueMixBackfillResponse(
    string Outcome,
    string RequestedBy,
    int CompaniesConsidered,
    int CompanyMonthsDiscovered,
    int CompanyMonthsProcessed,
    int CompanyMonthsSkippedNoSalesLineItems,
    string Duration);

// --- Spec 076: company monthly activity trend snapshot backfill ---
// No request body — date range and forceRebuild are read from appsettings "TrendSnapshotBackfill";
// eligible companies are enumerated from NoavaranEligibleCompanies.

public sealed record AdminTrendSnapshotBackfillResponse(
    string Outcome,
    string RequestedBy,
    int CompaniesConsidered,
    int CompanyMonthsDiscovered,
    int CompanyMonthsProcessed,
    int CompanyMonthsSkipped,
    int CompanyMonthsFailed,
    string Duration);

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

// --- Single-company monthly re-ingestion (bug fix helper) ---

public sealed record AdminSingleCompanyMonthlyIngestionRequest(
    int ExternalCompanyId,
    int FromShamsiYear,
    int FromShamsiMonth,
    int ToShamsiYear,
    int ToShamsiMonth);

public sealed record AdminSingleCompanyMonthlyIngestionResponse(
    string Outcome,
    int ExternalCompanyId,
    int MonthsInRange,
    int RequestsEnqueued,
    string FirstMonth,
    string LastMonth,
    string RequestedBy);

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

// --- Spec 054 Phase 3: parallel validation ---

public sealed record AdminTsetmcValidationResponse(
    bool CanValidate,
    int InstrumentsCompared,
    int MismatchCount,
    string Duration);

public sealed record AdminTsetmcMismatchSummaryResponse(
    int RecentDays,
    IReadOnlyCollection<AdminTsetmcMismatchFieldSummary> ByField);

public sealed record AdminTsetmcMismatchFieldSummary(
    string Field,
    int MismatchCount,
    decimal AvgRelativeDiffPercent,
    decimal MaxRelativeDiffPercent,
    DateTimeOffset? LastComparedAt);

// --- Spec 054 Phase 4: cutover status ---

public sealed record AdminMarketQuoteSourceStatusResponse(
    string PrimarySourceName,
    bool BridgeEnabled,
    bool DirectFeedOperational,
    string Notes);

// --- Spec 065: CyclicalWaves ComprehensiveAnalysis sync ---

public sealed record AdminComprehensiveAnalysisFullSyncResponse(
    int PagesTotal,
    int ItemsSynced,
    string Duration);

public sealed record AdminComprehensiveAnalysisDailySyncResponse(
    int PagesTotal,
    int ItemsSynced,
    string Duration);

public sealed record AdminComprehensiveAnalysisSyncRunResponse(
    int Id,
    string JobName,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Status,
    int PagesTotal,
    int ItemsSynced,
    string? ErrorMessage);

public sealed record AdminComprehensiveAnalysisBackfillResponse(int RowsUpdated);

// --- Spec 067: CyclicalWaves CompanyId backfill ---
public sealed record AdminBackfillCyclicalWavesCompanyIdResponse(int Resolved, int Unresolved);
