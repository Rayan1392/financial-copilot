using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminDataOperationsController(
    IDataSyncRequestPublisher publisher,
    IDataSyncRunReader runReader,
    IFinancialDataProviderHealthService providerHealth,
    ISourceFreshnessReader sourceFreshnessReader,
    ICyclicalWavesFullSyncService cyclicalWavesFullSync,
    ICodalDbScheduledSyncService codalDbScheduledSync,
    INadpcoApiScheduledSyncService nadpcoApiScheduledSync,
    INadpcoApiSyncStateReader nadpcoApiSyncStateReader,
    INadpcoScheduledSyncCoordinator nadpcoScheduledSyncCoordinator,
    INadpcoScheduledSyncRunReader nadpcoScheduledSyncRunReader,
    IStockMarketDbSyncService stockMarketDbSync,
    IStockMarketDbSyncStateReader stockMarketDbSyncStateReader,
    ITsetmcDirectFeedSyncService tsetmcDirectFeed,
    ITsetmcValidationService tsetmcValidation,
    IMarketQuoteMismatchReader mismatchReader,
    IMarketQuoteSourcePriority marketQuoteSourcePriority,
    IArchiveImportCoordinator archiveImportCoordinator,
    IArchiveImportRunReader archiveImportRunReader,
    ICurrentApiBackfillCoordinator currentApiBackfillCoordinator,
    ICurrentApiGapReader currentApiGapReader,
    IMonthlyActivityBackfillCoordinator monthlyActivityBackfillCoordinator,
    IProductRevenueMixBackfillService productRevenueMixBackfillService,
    ICompanyMonthlyActivityTrendSnapshotBackfillService trendSnapshotBackfillService,
    IEligibleFundamentalIndexBulkSyncService eligibleFundamentalIndexBulkSyncService,
    ISingleCompanyMonthlyIngestionService singleCompanyIngestion,
    IFundamentalIndexCatchUpCoordinator fundamentalIndexCatchUpCoordinator,
    IFundamentalIndexCatchUpRunReader fundamentalIndexCatchUpRunReader,
    ICurrentActorContext currentActor,
    IMissingAnswerFeedbackRepository missingAnswerFeedback,
    IDataSyncActivityMonitor activityMonitor,
    IComprehensiveAnalysisFullSyncService comprehensiveAnalysisFullSync,
    IComprehensiveAnalysisDailySyncService comprehensiveAnalysisDailySync,
    IComprehensiveAnalysisSyncRunReader comprehensiveAnalysisSyncRunReader,
    IComprehensiveAnalysisPlainTextBackfillService comprehensiveAnalysisBackfill,
    IBackfillCyclicalWavesCompanyIdService cyclicalWavesCompanyIdBackfill,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("data-sync/symbols")]
    public Task<ActionResult<AdminDataSyncQueuedResponse>> QueueSymbolSync(
        [FromBody] AdminDataSyncRequest? request,
        CancellationToken cancellationToken) =>
        QueueAsync(ProviderDataset.Symbols, request, requiresReference: false, cancellationToken);

    [HttpPost("data-sync/financial-statements")]
    public Task<ActionResult<AdminDataSyncQueuedResponse>> QueueFinancialStatementSync(
        [FromBody] AdminDataSyncRequest? request,
        CancellationToken cancellationToken) =>
        QueueAsync(ProviderDataset.FinancialStatements, request, requiresReference: true, cancellationToken);

    [HttpPost("data-sync/monthly-reports")]
    public Task<ActionResult<AdminDataSyncQueuedResponse>> QueueMonthlyReportSync(
        [FromBody] AdminDataSyncRequest? request,
        CancellationToken cancellationToken) =>
        QueueAsync(ProviderDataset.MonthlyProductionSales, request, requiresReference: true, cancellationToken);

    [HttpPost("data-sync/financial-ratios")]
    public Task<ActionResult<AdminDataSyncQueuedResponse>> QueueFinancialRatiosSync(
        [FromBody] AdminDataSyncRequest? request,
        CancellationToken cancellationToken) =>
        QueueAsync(ProviderDataset.FinancialRatios, request, requiresReference: true, cancellationToken,
            providerName: ProviderSources.NoavaranArchiveSqlName);

    [HttpPost("data-sync/fundamental-indexes")]
    public Task<ActionResult<AdminDataSyncQueuedResponse>> QueueFundamentalIndexesSync(
        [FromBody] AdminDataSyncRequest? request,
        CancellationToken cancellationToken) =>
        QueueAsync(ProviderDataset.FundamentalIndexes, request, requiresReference: true, cancellationToken,
            providerName: ProviderSources.NoavaranCurrentApiName);

    [HttpPost("data-sync/fundamental-indexes/eligible-companies")]
    public async Task<ActionResult<AdminEligibleFundamentalIndexBulkSyncResponse>> QueueEligibleFundamentalIndexesSync(
        [FromBody] AdminEligibleFundamentalIndexBulkSyncRequest? request,
        CancellationToken cancellationToken)
    {
        var providerName = ResolveCuratedFundamentalIndexProviderName(request?.ProviderName);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (request?.MaxItems is <= 0)
        {
            ModelState.AddModelError(
                nameof(AdminEligibleFundamentalIndexBulkSyncRequest.MaxItems),
                "MaxItems must be greater than zero when provided.");
            return ValidationProblem(ModelState);
        }

        var actor = currentActor.Actor;
        var result = await eligibleFundamentalIndexBulkSyncService.RunAsync(
            new EligibleFundamentalIndexBulkSyncRequest(
                $"{actor.ActorType}:{actor.ActorId}",
                providerName,
                request?.IdempotencyKey,
                request?.MaxItems,
                request?.DryRun ?? false),
            cancellationToken);

        return Ok(new AdminEligibleFundamentalIndexBulkSyncResponse(
            result.RequestId,
            result.Dataset.ToString(),
            result.Source,
            result.RequestedAt,
            result.IdempotencyKey,
            result.Status,
            result.EligibleCount,
            result.QueuedCount,
            result.SkippedCount,
            result.FailedCount,
            result.Items.Select(item => new AdminEligibleFundamentalIndexBulkSyncItemResponse(
                item.ExternalReference,
                item.Status,
                item.IdempotencyKey,
                item.Error)).ToArray()));
    }

    [HttpGet("data-sync/runs")]
    public async Task<ActionResult<IReadOnlyCollection<AdminDataSyncRunResponse>>> GetRuns(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var runs = await runReader.QueryRecentAsync(limit, cancellationToken);
        return Ok(runs.Select(run => new AdminDataSyncRunResponse(
            run.Id,
            run.Dataset.ToString(),
            run.ExternalReference,
            run.Status.ToString(),
            run.RequestedAt,
            run.StartedAt,
            run.CompletedAt,
            run.ProcessedRecords,
            run.ErrorCount,
            run.ErrorMessage,
            run.SourcePayloadChecksum)).ToArray());
    }

    [HttpPost("cyclicalwaves/full-sync")]
    public async Task<ActionResult<AdminCyclicalWavesFullSyncResponse>> RunCyclicalWavesFullSync(
        CancellationToken cancellationToken)
    {
        var result = await cyclicalWavesFullSync.ExecuteAsync(cancellationToken);
        return Ok(new AdminCyclicalWavesFullSyncResponse(
            result.SymbolsSynced,
            result.TickersSynced,
            result.TickersFailed,
            result.FailedTickers,
            result.Duration.ToString("g")));
    }

    // --- Spec 065: CyclicalWaves ComprehensiveAnalysis sync ---

    [HttpPost("comprehensive-analysis/full-sync")]
    public async Task<ActionResult<AdminComprehensiveAnalysisFullSyncResponse>> RunComprehensiveAnalysisFullSync(
        CancellationToken cancellationToken)
    {
        var result = await comprehensiveAnalysisFullSync.ExecuteAsync(cancellationToken);
        return Ok(new AdminComprehensiveAnalysisFullSyncResponse(
            result.PagesTotal,
            result.ItemsSynced,
            result.Duration.ToString("g")));
    }

    [HttpPost("comprehensive-analysis/daily-sync")]
    public async Task<ActionResult<AdminComprehensiveAnalysisDailySyncResponse>> RunComprehensiveAnalysisDailySync(
        CancellationToken cancellationToken)
    {
        var result = await comprehensiveAnalysisDailySync.ExecuteAsync(cancellationToken);
        return Ok(new AdminComprehensiveAnalysisDailySyncResponse(
            result.PagesTotal,
            result.ItemsSynced,
            result.Duration.ToString("g")));
    }

    [HttpPost("comprehensive-analysis/backfill-plain-text")]
    public async Task<ActionResult<AdminComprehensiveAnalysisBackfillResponse>> BackfillComprehensiveAnalysisPlainText(
        CancellationToken cancellationToken)
    {
        var result = await comprehensiveAnalysisBackfill.ExecuteAsync(cancellationToken);
        return Ok(new AdminComprehensiveAnalysisBackfillResponse(result.RowsUpdated));
    }

    // --- Spec 067: CyclicalWaves CompanyId backfill (DataAdmin only) ---
    // Backfills CompanyId FK on historical CyclicalWaves FinancialStatements and MonthlyReports
    // rows ingested before the spec 067 normalizer wiring was in place. Safe to re-invoke.

    [HttpPost("cyclicalwaves/backfill-company-id")]
    public async Task<ActionResult<AdminBackfillCyclicalWavesCompanyIdResponse>> BackfillCyclicalWavesCompanyId(
        CancellationToken cancellationToken)
    {
        var result = await cyclicalWavesCompanyIdBackfill.RunAsync(cancellationToken);
        return Ok(new AdminBackfillCyclicalWavesCompanyIdResponse(result.Resolved, result.Unresolved));
    }

    [HttpGet("comprehensive-analysis/sync-runs")]
    public async Task<ActionResult<IReadOnlyCollection<AdminComprehensiveAnalysisSyncRunResponse>>> GetComprehensiveAnalysisSyncRuns(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var runs = await comprehensiveAnalysisSyncRunReader.QueryRecentAsync(limit, cancellationToken);
        return Ok(runs.Select(r => new AdminComprehensiveAnalysisSyncRunResponse(
            r.Id,
            r.JobName,
            r.StartedAt,
            r.FinishedAt,
            r.Status,
            r.PagesTotal,
            r.ItemsSynced,
            r.ErrorMessage)).ToArray());
    }

    [HttpPost("codaldb/full-sync")]
    public Task<ActionResult<AdminCodalDbSyncResponse>> RunCodalDbFullSync(CancellationToken cancellationToken) =>
        RunCodalDbSyncAsync(fullReload: true, cancellationToken);

    [HttpPost("codaldb/incremental-sync")]
    public Task<ActionResult<AdminCodalDbSyncResponse>> RunCodalDbIncrementalSync(CancellationToken cancellationToken) =>
        RunCodalDbSyncAsync(fullReload: false, cancellationToken);

    private async Task<ActionResult<AdminCodalDbSyncResponse>> RunCodalDbSyncAsync(
        bool fullReload,
        CancellationToken cancellationToken)
    {
        var result = await codalDbScheduledSync.ExecuteAsync(fullReload, cancellationToken);
        return Ok(new AdminCodalDbSyncResponse(
            result.FullReload,
            result.CompaniesConsidered,
            result.CompaniesEnqueued,
            result.FailedCompanies,
            result.FailedCompanyIds,
            result.AdvancedWatermark,
            result.Duration.ToString("g")));
    }

    // --- Spec 052: one-time Noavaran archive import (DataAdmin only) ---

    [HttpPost("noavaran-archive/dry-run")]
    public Task<ActionResult<AdminArchiveImportRunResponse>> RunArchiveDryRun(
        [FromBody] AdminArchiveImportRequest? request,
        CancellationToken cancellationToken) =>
        RunArchiveActionAsync(ArchiveImportAction.DryRun, request, cancellationToken);

    [HttpPost("noavaran-archive/import")]
    public Task<ActionResult<AdminArchiveImportRunResponse>> RunArchiveImport(
        [FromBody] AdminArchiveImportRequest? request,
        CancellationToken cancellationToken) =>
        RunArchiveActionAsync(ArchiveImportAction.Import, request, cancellationToken);

    [HttpPost("noavaran-archive/re-import")]
    public Task<ActionResult<AdminArchiveImportRunResponse>> RunArchiveReImport(
        [FromBody] AdminArchiveImportRequest? request,
        CancellationToken cancellationToken) =>
        RunArchiveActionAsync(ArchiveImportAction.ReImport, request, cancellationToken);

    [HttpPost("noavaran-archive/validate")]
    public Task<ActionResult<AdminArchiveImportRunResponse>> RunArchiveValidate(
        [FromBody] AdminArchiveImportRequest? request,
        CancellationToken cancellationToken) =>
        RunArchiveActionAsync(ArchiveImportAction.Validate, request, cancellationToken);

    [HttpPost("noavaran-archive/freeze")]
    public Task<ActionResult<AdminArchiveImportRunResponse>> RunArchiveFreeze(
        [FromBody] AdminArchiveImportRequest? request,
        CancellationToken cancellationToken) =>
        RunArchiveActionAsync(ArchiveImportAction.Freeze, request, cancellationToken);

    [HttpGet("noavaran-archive/freeze-state")]
    public async Task<ActionResult<AdminArchiveFreezeStateResponse>> GetArchiveFreezeState(
        CancellationToken cancellationToken)
    {
        var state = await archiveImportCoordinator.GetFreezeStateAsync(cancellationToken);
        return Ok(new AdminArchiveFreezeStateResponse(
            state.IsFrozen, state.FrozenAt, state.FrozenByRunId, state.Reason));
    }

    [HttpGet("noavaran-archive/runs")]
    public async Task<ActionResult<IReadOnlyCollection<AdminArchiveImportRunResponse>>> GetArchiveRuns(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var runs = await archiveImportRunReader.QueryRecentAsync(limit, cancellationToken);
        return Ok(runs.Select(ToArchiveRunResponse).ToArray());
    }

    [HttpGet("noavaran-archive/coverage")]
    public async Task<ActionResult<AdminArchiveImportValidationResponse>> GetArchiveCoverage(
        CancellationToken cancellationToken)
    {
        var validation = await archiveImportCoordinator.ValidateAsync(cancellationToken);
        return Ok(new AdminArchiveImportValidationResponse(
            validation.CompanyMappingValid,
            validation.CompaniesWithoutCanonicalSymbol,
            validation.UnmappedExternalCompanyIds,
            ToCoverageResponse(validation.Coverage)));
    }

    private async Task<ActionResult<AdminArchiveImportRunResponse>> RunArchiveActionAsync(
        ArchiveImportAction action,
        AdminArchiveImportRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryParseArchiveDatasets(request?.Datasets, out var datasets, out var invalidDataset))
        {
            ModelState.AddModelError(nameof(request.Datasets), $"Unknown dataset '{invalidDataset}'.");
            return ValidationProblem(ModelState);
        }

        var actor = currentActor.Actor;
        var requestedBy = $"{actor.ActorType}:{actor.ActorId}";
        var run = await archiveImportCoordinator.RunAsync(
            new ArchiveImportRequest(action, requestedBy, datasets, request?.Reason),
            cancellationToken);
        return Ok(ToArchiveRunResponse(run));
    }

    private static bool TryParseArchiveDatasets(
        string[]? requested,
        out IReadOnlyCollection<ArchiveImportDataset> datasets,
        out string? invalidDataset)
    {
        invalidDataset = null;
        if (requested is null || requested.Length == 0)
        {
            datasets = [];
            return true;
        }

        var parsed = new List<ArchiveImportDataset>(requested.Length);
        foreach (var name in requested)
        {
            if (!Enum.TryParse<ArchiveImportDataset>(name, ignoreCase: true, out var dataset))
            {
                invalidDataset = name;
                datasets = [];
                return false;
            }

            parsed.Add(dataset);
        }

        datasets = parsed;
        return true;
    }

    private static AdminArchiveImportRunResponse ToArchiveRunResponse(ArchiveImportRun run) =>
        new(
            run.RunId,
            run.Action.ToString(),
            run.Status.ToString(),
            run.RequestedBy,
            run.Datasets.Select(d => d.ToString()).ToArray(),
            run.Reason,
            run.StartedAt,
            run.FinishedAt,
            run.CompaniesConsidered,
            run.RequestsEnqueued,
            run.SkippedCount,
            run.ConflictCount,
            run.FailedCount,
            run.Frozen,
            run.Diagnostics);

    private static AdminArchiveCoverageResponse ToCoverageResponse(ArchiveCoverageSummary coverage) =>
        new(
            coverage.SourceName,
            coverage.CompanyCount,
            coverage.RowCountByDataset,
            coverage.RowCountByFiscalYear);

    // --- Spec 053: Noavaran current-API ingestion (DataAdmin only) ---

    [HttpGet("noavaran-current/health")]
    public async Task<ActionResult<AdminCurrentApiHealthResponse>> GetCurrentApiHealth(
        CancellationToken cancellationToken)
    {
        var health = await currentApiBackfillCoordinator.GetHealthAsync(cancellationToken);
        return Ok(new AdminCurrentApiHealthResponse(
            health.SourceName,
            health.ProviderHealthStatus,
            health.ProviderHealthDetail,
            health.ScheduledSyncEnabled,
            health.LastSuccessfulSyncAt,
            health.NextDueAt,
            health.CheckedAt));
    }

    [HttpGet("noavaran-current/gaps")]
    public async Task<ActionResult<AdminCurrentApiGapResponse>> GetCurrentApiGaps(
        CancellationToken cancellationToken)
    {
        var report = await currentApiGapReader.ReportAsync(cancellationToken);
        return Ok(new AdminCurrentApiGapResponse(
            report.CurrentApiBoundaryShamsiYear,
            report.TotalGapRows,
            report.Gaps.Select(gap => new AdminCurrentApiGapItem(
                gap.Dataset,
                gap.ExternalCompanyId,
                gap.FiscalYear,
                gap.CurrentApiRowCount,
                gap.ArchiveRowCount)).ToArray()));
    }

    // Backfill / gap-fill: a full current-API sync, optionally lowering the Shamsi start boundary for
    // this run only (monthly activity stays clamped to the vendor-permitted 1404 boundary).
    [HttpPost("noavaran-current/backfill")]
    public async Task<ActionResult<AdminCurrentApiBackfillResponse>> RunCurrentApiBackfill(
        [FromBody] AdminCurrentApiBackfillRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.FromShamsiYear is { } year && year is < 1380 or > 1500)
        {
            ModelState.AddModelError(nameof(request.FromShamsiYear), "FromShamsiYear must be a plausible Shamsi year (1380-1500).");
            return ValidationProblem(ModelState);
        }

        var actor = currentActor.Actor;
        var result = await currentApiBackfillCoordinator.BackfillAsync(
            new CurrentApiBackfillRequest($"{actor.ActorType}:{actor.ActorId}", request?.FromShamsiYear),
            cancellationToken);
        return Ok(new AdminCurrentApiBackfillResponse(
            result.FullReload,
            result.AppliedFromShamsiYear,
            result.CompaniesConsidered,
            result.RequestsEnqueued,
            result.FailedCompanies,
            result.Duration));
    }

    // --- Spec 057: NADPCO monthly-activity reverse-chronological backfill (DataAdmin only) ---
    // Walks Shamsi months newest-first (e.g. 1405/02 â†’ â€¦ â†’ 1404/01), one bounded company-month
    // request per eligible company per month. Manual only â€” never scheduler-invoked. Re-invoking
    // resumes: completed company-months are skipped, failed ones retried.

    [HttpPost("noavaran-current/monthly-backfill")]
    public async Task<ActionResult<AdminMonthlyActivityBackfillStartResponse>> StartMonthlyActivityBackfill(
        CancellationToken cancellationToken)
    {
        var actor = currentActor.Actor;
        var result = await monthlyActivityBackfillCoordinator.StartAsync(
            new MonthlyActivityBackfillRequest($"{actor.ActorType}:{actor.ActorId}"),
            cancellationToken);
        var response = new AdminMonthlyActivityBackfillStartResponse(
            result.BatchId,
            result.Outcome,
            result.MonthsPlanned,
            result.CompaniesPlanned,
            result.RequestsEnqueued,
            ToMonthlyBackfillProgressResponse(result.Progress));
        return result.Outcome == "Started" ? Accepted(response) : Ok(response);
    }

    [HttpPost("noavaran-current/monthly-backfill/single-month")]
    public async Task<ActionResult<AdminMonthlyActivityBackfillStartResponse>> StartSingleMonthActivityBackfill(
        [FromBody] AdminMonthlyActivitySingleMonthBackfillRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ShamsiYear is < 1380 or > 1500)
        {
            ModelState.AddModelError(
                nameof(request.ShamsiYear),
                "ShamsiYear must be a plausible Shamsi year (1380-1500).");
        }

        if (request.ShamsiMonth is < 1 or > 12)
        {
            ModelState.AddModelError(nameof(request.ShamsiMonth), "ShamsiMonth must be between 1 and 12.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var actor = currentActor.Actor;
        var result = await monthlyActivityBackfillCoordinator.StartAsync(
            new MonthlyActivityBackfillRequest(
                $"{actor.ActorType}:{actor.ActorId}",
                new ShamsiMonth(request.ShamsiYear, request.ShamsiMonth)),
            cancellationToken);
        var response = new AdminMonthlyActivityBackfillStartResponse(
            result.BatchId,
            result.Outcome,
            result.MonthsPlanned,
            result.CompaniesPlanned,
            result.RequestsEnqueued,
            ToMonthlyBackfillProgressResponse(result.Progress));
        return result.Outcome == "Started" ? Accepted(response) : Ok(response);
    }

    [HttpGet("noavaran-current/monthly-backfill")]
    public async Task<ActionResult<AdminMonthlyActivityBackfillProgressResponse>> GetMonthlyActivityBackfillProgress(
        CancellationToken cancellationToken)
    {
        var progress = await monthlyActivityBackfillCoordinator.GetProgressAsync(cancellationToken);
        return Ok(ToMonthlyBackfillProgressResponse(progress));
    }

    [HttpGet("noavaran-current/monthly-backfill/batches/{batchId:guid}")]
    public async Task<ActionResult<AdminMonthlyActivityBackfillBatchResponse>> GetMonthlyActivityBackfillBatch(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        var batch = await monthlyActivityBackfillCoordinator.GetBatchAsync(batchId, cancellationToken);
        return batch is null ? NotFound() : Ok(ToMonthlyBackfillBatchResponse(batch));
    }

    [HttpGet("noavaran-current/monthly-backfill/batches")]
    public async Task<ActionResult<IReadOnlyCollection<AdminMonthlyActivityBackfillBatchResponse>>> ListMonthlyActivityBackfillBatches(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            return BadRequest("limit must be between 1 and 100.");
        }

        var batches = await monthlyActivityBackfillCoordinator.ListBatchesAsync(limit, cancellationToken);
        return Ok(batches.Select(ToMonthlyBackfillBatchResponse).ToArray());
    }

    // --- Spec 075: one-time backfill of persisted company product revenue mix rows (DataAdmin only) ---
    // Reuses the existing calculator over already-normalized Noavaran ProductSales rows so
    // historical periods are populated without re-ingesting source payloads.

    [HttpPost("noavaran-current/product-revenue-mix-backfill")]
    public async Task<ActionResult<AdminProductRevenueMixBackfillResponse>> RunProductRevenueMixBackfill(
        CancellationToken cancellationToken)
    {
        var actor = currentActor.Actor;
        var result = await productRevenueMixBackfillService.RunAsync(
            new ProductRevenueMixBackfillRequest($"{actor.ActorType}:{actor.ActorId}"),
            cancellationToken);
        return Ok(new AdminProductRevenueMixBackfillResponse(
            result.Outcome,
            result.RequestedBy,
            result.CompaniesConsidered,
            result.CompanyMonthsDiscovered,
            result.CompanyMonthsProcessed,
            result.CompanyMonthsSkippedNoSalesLineItems,
            result.Duration));
    }

    // Spec 076 â€” rebuild CompanyMonthlyActivityTrendSnapshots from already-persisted Noavaran
    // monthly activity data. Accepts an optional company filter and a Jalali date range.

    // Date range and forceRebuild are read from appsettings "TrendSnapshotBackfill".
    // Eligible companies are enumerated from NoavaranEligibleCompanies â€” no body required.
    [HttpPost("noavaran-current/trend-snapshot-backfill")]
    public async Task<ActionResult<AdminTrendSnapshotBackfillResponse>> RunTrendSnapshotBackfill(
        CancellationToken cancellationToken)
    {
        var actor = currentActor.Actor;
        var result = await trendSnapshotBackfillService.RunAsync(
            new CompanyMonthlyActivityTrendSnapshotBackfillRequest(
                RequestedBy: $"{actor.ActorType}:{actor.ActorId}"),
            cancellationToken);

        return Ok(new AdminTrendSnapshotBackfillResponse(
            result.Outcome,
            result.RequestedBy,
            result.CompaniesConsidered,
            result.CompanyMonthsDiscovered,
            result.CompanyMonthsProcessed,
            result.CompanyMonthsSkipped,
            result.CompanyMonthsFailed,
            result.Duration));
    }


    [HttpPost("noavaran-current/single-company-monthly-ingestion")]
    public async Task<ActionResult<AdminSingleCompanyMonthlyIngestionResponse>> RunSingleCompanyMonthlyIngestion(
        [FromBody] AdminSingleCompanyMonthlyIngestionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExternalCompanyId <= 0)
        {
            ModelState.AddModelError(nameof(request.ExternalCompanyId), "ExternalCompanyId must be a positive integer.");
            return ValidationProblem(ModelState);
        }
        if (request.FromShamsiMonth is < 1 or > 12 || request.ToShamsiMonth is < 1 or > 12)
        {
            ModelState.AddModelError("ShamsiMonth", "Shamsi month must be between 1 and 12.");
            return ValidationProblem(ModelState);
        }
        var from = new ShamsiMonth(request.FromShamsiYear, (byte)request.FromShamsiMonth);
        var to = new ShamsiMonth(request.ToShamsiYear, (byte)request.ToShamsiMonth);
        if (from > to)
        {
            ModelState.AddModelError("DateRange", "FromShamsi must not be later than ToShamsi.");
            return ValidationProblem(ModelState);
        }

        var actor = currentActor.Actor;
        var result = await singleCompanyIngestion.EnqueueAsync(
            new SingleCompanyMonthlyIngestionRequest(
                request.ExternalCompanyId,
                request.FromShamsiYear,
                request.FromShamsiMonth,
                request.ToShamsiYear,
                request.ToShamsiMonth,
                $"{actor.ActorType}:{actor.ActorId}"),
            cancellationToken);

        return Ok(new AdminSingleCompanyMonthlyIngestionResponse(
            result.Outcome,
            result.ExternalCompanyId,
            result.MonthsInRange,
            result.RequestsEnqueued,
            result.FirstMonth,
            result.LastMonth,
            result.RequestedBy));
    }
    private static AdminMonthlyActivityBackfillProgressResponse ToMonthlyBackfillProgressResponse(
        MonthlyActivityBackfillProgress progress) =>
        new(
            progress.Started,
            progress.IsCompleted,
            progress.Status,
            progress.CompletedAt,
            progress.LastStartedAt,
            progress.RequestedBy,
            progress.Months.Select(month => new AdminMonthlyActivityBackfillMonthResponse(
                month.ShamsiYear,
                month.ShamsiMonth,
                month.CompaniesPlanned,
                month.CompaniesCompleted,
                month.CompaniesNoDataYet,
                month.CompaniesFailed,
                month.Status)).ToArray(),
            progress.OutputTypeCounts);

    private static AdminMonthlyActivityBackfillBatchResponse ToMonthlyBackfillBatchResponse(
        MonthlyActivityBackfillBatch batch) =>
        new(
            batch.BatchId,
            batch.Status,
            batch.RequestedBy,
            batch.CreatedAt,
            batch.PublishingStartedAt,
            batch.PublishedAt,
            batch.CompletedAt,
            batch.TargetShamsiYear,
            batch.TargetShamsiMonth,
            batch.PlannedCount,
            batch.PublishedCount,
            batch.ProcessedCount,
            batch.FailedCount,
            batch.RetryableCount,
            batch.LastError);

    // --- Spec 050: NADPCO all-index fundamental-index catch-up coverage (DataAdmin only) ---
    // Distinct from the curated 041 fundamental-index sync: this fetches EVERY vendor index
    // (empty companyIndexIds) for all local companies into the non-scannable coverage table.

    [HttpPost("nadpcoapi/fundamental-index-catch-up")]
    public async Task<ActionResult<AdminFundamentalIndexCatchUpRunResponse>> RunFundamentalIndexCatchUp(
        [FromBody] AdminFundamentalIndexCatchUpRequest? request,
        CancellationToken cancellationToken)
    {
        var fromYear = request?.FromShamsiYear ?? 1403;
        var toYear = request?.ToShamsiYear ?? 1405;
        if (fromYear is < 1380 or > 1500 || toYear is < 1380 or > 1500 || fromYear > toYear)
        {
            ModelState.AddModelError(
                nameof(AdminFundamentalIndexCatchUpRequest.FromShamsiYear),
                "FromShamsiYear/ToShamsiYear must be plausible Shamsi years (1380-1500) with From <= To.");
            return ValidationProblem(ModelState);
        }

        var actor = currentActor.Actor;
        var run = await fundamentalIndexCatchUpCoordinator.RunAsync(
            new FundamentalIndexCatchUpRequest($"{actor.ActorType}:{actor.ActorId}", fromYear, toYear),
            cancellationToken);
        return Ok(ToCatchUpRunResponse(run));
    }

    [HttpGet("nadpcoapi/fundamental-index-catch-up/runs")]
    public async Task<ActionResult<IReadOnlyCollection<AdminFundamentalIndexCatchUpRunResponse>>> GetFundamentalIndexCatchUpRuns(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var runs = await fundamentalIndexCatchUpRunReader.QueryRecentAsync(limit, cancellationToken);
        return Ok(runs.Select(ToCatchUpRunResponse).ToArray());
    }

    private static AdminFundamentalIndexCatchUpRunResponse ToCatchUpRunResponse(FundamentalIndexCatchUpRun run) =>
        new(
            run.RunId,
            run.Status.ToString(),
            run.RequestedBy,
            run.FromShamsiYear,
            run.ToShamsiYear,
            run.StartedAt,
            run.FinishedAt,
            run.CompaniesConsidered,
            run.RequestsEnqueued,
            run.FailedCompanies,
            run.FailedCompanyIds,
            run.Diagnostics);

    [HttpPost("nadpcoapi/full-sync")]
    public Task<ActionResult<AdminNadpcoApiSyncResponse>> RunNadpcoApiFullSync(CancellationToken cancellationToken) =>
        RunNadpcoApiSyncAsync(fullReload: true, cancellationToken);

    [HttpPost("nadpcoapi/incremental-sync")]
    public Task<ActionResult<AdminNadpcoApiSyncResponse>> RunNadpcoApiIncrementalSync(CancellationToken cancellationToken) =>
        RunNadpcoApiSyncAsync(fullReload: false, cancellationToken);

    [HttpPost("nadpcoapi/company-catalog/clean-slate")]
    public Task<ActionResult<AdminNadpcoApiSyncResponse>> RunNadpcoApiCompanyCatalogCleanSlate(
        CancellationToken cancellationToken) =>
        RunNadpcoApiCompanyCatalogAsync(cleanSlate: true, cancellationToken);

    [HttpPost("nadpcoapi/company-catalog/refresh")]
    public Task<ActionResult<AdminNadpcoApiSyncResponse>> RunNadpcoApiCompanyCatalogRefresh(
        CancellationToken cancellationToken) =>
        RunNadpcoApiCompanyCatalogAsync(cleanSlate: false, cancellationToken);

    private async Task<ActionResult<AdminNadpcoApiSyncResponse>> RunNadpcoApiSyncAsync(
        bool fullReload,
        CancellationToken cancellationToken)
    {
        var result = await nadpcoApiScheduledSync.ExecuteAsync(fullReload, cancellationToken);
        return ToNadpcoApiSyncResponse(result);
    }

    private async Task<ActionResult<AdminNadpcoApiSyncResponse>> RunNadpcoApiCompanyCatalogAsync(
        bool cleanSlate,
        CancellationToken cancellationToken)
    {
        var result = await nadpcoApiScheduledSync.ExecuteCompanyCatalogAsync(cleanSlate, cancellationToken);
        return ToNadpcoApiSyncResponse(result);
    }

    private ActionResult<AdminNadpcoApiSyncResponse> ToNadpcoApiSyncResponse(NadpcoApiSyncResult result)
    {
        return Ok(new AdminNadpcoApiSyncResponse(
            result.RunMode.ToString(),
            result.FullReload,
            result.CompaniesConsidered,
            result.CompaniesEnqueued,
            result.FailedCompanies,
            result.FailedCompanyIds,
            result.RequestsEnqueued,
            result.OverlapFrom,
            result.AdvancedWatermark,
            result.Duration.ToString("g"),
            result.CleanSlate is null
                ? null
                : new AdminNadpcoCompanyCatalogCleanSlateResponse(
                    result.CleanSlate.MetricRecalculationRequestsDeleted,
                    result.CleanSlate.FeatureComputationJobsDeleted,
                    result.CleanSlate.FeatureSnapshotsDeleted,
                    result.CleanSlate.DerivedMetricsDeleted,
                    result.CleanSlate.SymbolsDeleted,
                    result.CleanSlate.TradingInstrumentLinksCleared,
                    result.CleanSlate.CompaniesDeleted)));
    }

    [HttpGet("nadpcoapi/sync-state")]
    public async Task<ActionResult<IReadOnlyCollection<AdminNadpcoApiSyncStateResponse>>> GetNadpcoApiSyncState(
        CancellationToken cancellationToken)
    {
        var states = await nadpcoApiSyncStateReader.QueryAsync(cancellationToken);
        return Ok(states.Select(state => new AdminNadpcoApiSyncStateResponse(
            state.Dataset,
            state.LastSuccessfulSyncAt,
            state.LastOverlapFrom,
            state.LastRunStartedAt,
            state.LastRunCompletedAt,
            state.LastCompaniesConsidered,
            state.LastCompaniesEnqueued,
            state.LastFailedCompanies,
            state.LastRunMode,
            state.LastError)).ToArray());
    }

    [HttpPost("nadpcoapi/scheduled-sync/run")]
    public async Task<ActionResult<AdminNadpcoScheduledSyncRunResponse>> RunNadpcoScheduledSync(
        [FromBody] AdminNadpcoScheduledSyncManualRunRequest? request,
        CancellationToken cancellationToken)
    {
        var run = await nadpcoScheduledSyncCoordinator.RunAsync(
            new NadpcoScheduledSyncRunRequest(
                NadpcoScheduledSyncTriggerSource.Manual,
                request?.Reason,
                Force: true),
            cancellationToken);
        return Ok(ToScheduledSyncRunResponse(run));
    }

    [HttpGet("nadpcoapi/scheduled-sync/status")]
    public async Task<ActionResult<AdminNadpcoScheduledSyncStatusResponse>> GetNadpcoScheduledSyncStatus(
        [FromQuery] int recentRunLimit = 10,
        CancellationToken cancellationToken = default)
    {
        if (recentRunLimit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(recentRunLimit), "Recent run limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var status = await nadpcoScheduledSyncCoordinator.GetStatusAsync(recentRunLimit, cancellationToken);
        return Ok(new AdminNadpcoScheduledSyncStatusResponse(
            status.Enabled,
            status.Ready,
            status.NextDueAt,
            status.LastSuccessfulExecutionAt,
            status.ActiveRun is null ? null : ToScheduledSyncRunResponse(status.ActiveRun),
            status.RecentRuns.Select(ToScheduledSyncRunResponse).ToArray()));
    }

    [HttpGet("nadpcoapi/scheduled-sync/runs")]
    public async Task<ActionResult<IReadOnlyCollection<AdminNadpcoScheduledSyncRunResponse>>> GetNadpcoScheduledSyncRuns(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var runs = await nadpcoScheduledSyncRunReader.QueryRecentAsync(limit, cancellationToken);
        return Ok(runs.Select(ToScheduledSyncRunResponse).ToArray());
    }

    [HttpPost("stockmarketdb/{dataset}/sync")]
    public async Task<ActionResult<AdminStockMarketSyncResponse>> RunStockMarketDbSync(
        string dataset,
        [FromQuery] bool fullReload = false,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<StockMarketDataset>(dataset, ignoreCase: true, out var parsed))
        {
            ModelState.AddModelError(
                nameof(dataset),
                $"Unknown dataset '{dataset}'. Valid: {string.Join(", ", Enum.GetNames<StockMarketDataset>())}.");
            return ValidationProblem(ModelState);
        }

        var result = await stockMarketDbSync.SynchronizeAsync(parsed, fullReload, cancellationToken);
        return Ok(new AdminStockMarketSyncResponse(
            result.Dataset.ToString(),
            result.RowsRead,
            result.RowsPersisted,
            result.AdvancedWatermark,
            result.Duration.ToString("g")));
    }

    [HttpGet("stockmarketdb/sync-state")]
    public async Task<ActionResult<IReadOnlyCollection<AdminStockMarketSyncStateResponse>>> GetStockMarketDbSyncState(
        CancellationToken cancellationToken)
    {
        var states = await stockMarketDbSyncStateReader.QueryAsync(cancellationToken);
        return Ok(states.Select(state => new AdminStockMarketSyncStateResponse(
            state.Dataset.ToString(),
            state.Watermark,
            state.LastRunStartedAt,
            state.LastRunCompletedAt,
            state.LogicalVendor,
            state.PhysicalSource,
            state.SourceMode)).ToArray());
    }

    [HttpGet("tsetmc/status")]
    public ActionResult<AdminTsetmcDirectFeedStatusResponse> GetTsetmcDirectFeedStatus() =>
        Ok(new AdminTsetmcDirectFeedStatusResponse(
            IsOperational: tsetmcDirectFeed.IsOperational,
            PhysicalSource: "TsetmcWebService",
            Notes: tsetmcDirectFeed.IsOperational
                ? "Direct TSETMC feed is configured and operational."
                : "Direct TSETMC feed is not operational. Set TsetmcWebService:Enabled=true and provide credentials."));

    [HttpPost("tsetmc/{dataset}/sync")]
    public async Task<ActionResult<AdminTsetmcSyncResponse>> RunTsetmcDirectFeedSync(
        string dataset,
        CancellationToken cancellationToken)
    {
        if (!tsetmcDirectFeed.IsOperational)
            return Conflict(new { error = "TsetmcWebService is not operational. Enable it and configure credentials." });

        TsetmcSyncResult result;
        try
        {
            result = dataset.ToLowerInvariant() switch
            {
                "instruments" => await tsetmcDirectFeed.SynchronizeInstrumentsAsync(cancellationToken),
                "intradaytrades" => await tsetmcDirectFeed.SynchronizeIntradayTradesAsync(cancellationToken),
                "dailytrades" => await tsetmcDirectFeed.SynchronizeDailyTradesAsync(cancellationToken),
                "dailyindices" => await tsetmcDirectFeed.SynchronizeDailyIndicesAsync(cancellationToken),
                "intradayindices" => await tsetmcDirectFeed.SynchronizeIntradayIndicesAsync(cancellationToken),
                _ => throw new ArgumentException($"Unknown dataset '{dataset}'. Valid: instruments, intradaytrades, dailytrades, dailyindices, intradayindices.")
            };
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(dataset), ex.Message);
            return ValidationProblem(ModelState);
        }

        return Ok(new AdminTsetmcSyncResponse(
            result.Dataset,
            result.RowsFetched,
            result.RowsPersisted,
            result.Duration.ToString("g")));
    }

    [HttpPost("tsetmc/validate")]
    public async Task<ActionResult<AdminTsetmcValidationResponse>> RunTsetmcValidation(
        CancellationToken cancellationToken)
    {
        if (!tsetmcValidation.CanValidate)
            return Conflict(new { error = "Cannot validate: both StockMarketDb (UsePersistedMarketQuotes=true) and TsetmcWebService (Enabled=true with credentials) must be configured." });

        var result = await tsetmcValidation.ValidateLatestQuotesAsync(cancellationToken);
        return Ok(new AdminTsetmcValidationResponse(
            tsetmcValidation.CanValidate,
            result.InstrumentsCompared,
            result.MismatchCount,
            result.Duration.ToString("g")));
    }

    [HttpGet("tsetmc/mismatches")]
    public async Task<ActionResult<AdminTsetmcMismatchSummaryResponse>> GetTsetmcMismatchSummary(
        [FromQuery] int recentDays = 7,
        CancellationToken cancellationToken = default)
    {
        var summaries = await mismatchReader.GetSummaryAsync(recentDays, cancellationToken);
        return Ok(new AdminTsetmcMismatchSummaryResponse(
            recentDays,
            summaries.Select(s => new AdminTsetmcMismatchFieldSummary(
                s.Field, s.MismatchCount, s.AvgRelativeDiffPercent,
                s.MaxRelativeDiffPercent, s.LastComparedAt)).ToArray()));
    }

    [HttpGet("tsetmc/source-priority")]
    public ActionResult<AdminMarketQuoteSourceStatusResponse> GetMarketQuoteSourcePriority() =>
        Ok(new AdminMarketQuoteSourceStatusResponse(
            PrimarySourceName: marketQuoteSourcePriority.PrimarySourceName,
            BridgeEnabled: true,
            DirectFeedOperational: tsetmcDirectFeed.IsOperational,
            Notes: marketQuoteSourcePriority.PrimarySourceName == "TsetmcWebService"
                ? "Phase 4 cutover active: live quotes served from TsetmcWebService direct feed."
                : "Bridge phase active: live quotes served from StockMarketDb."));

    [HttpGet("missing-answer-feedback")]
    public async Task<ActionResult<IReadOnlyCollection<AdminMissingAnswerFeedbackItem>>> GetMissingAnswerFeedback(
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] string? classification,
        [FromQuery] string? metricCode,
        [FromQuery] string? actorId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        MissingAnswerFeedbackClassification? classificationFilter = null;
        if (!string.IsNullOrWhiteSpace(classification))
        {
            if (!Enum.TryParse<MissingAnswerFeedbackClassification>(classification, ignoreCase: true, out var parsed))
            {
                ModelState.AddModelError(nameof(classification),
                    $"Unknown classification '{classification}'. Valid: {string.Join(", ", Enum.GetNames<MissingAnswerFeedbackClassification>())}.");
                return ValidationProblem(ModelState);
            }
            classificationFilter = parsed;
        }

        if (take is < 1 or > 1000)
        {
            ModelState.AddModelError(nameof(take), "Take must be between 1 and 1000.");
            return ValidationProblem(ModelState);
        }

        var rows = await missingAnswerFeedback.QueryAsync(
            new MissingAnswerFeedbackQuery(
                DateFrom: dateFrom,
                DateTo: dateTo,
                Classification: classificationFilter,
                RequestedMetricCode: metricCode,
                ActorId: actorId,
                Skip: skip,
                Take: take),
            cancellationToken);

        return Ok(rows.Select(item => new AdminMissingAnswerFeedbackItem(
            item.Id,
            item.ActorId,
            item.QueryText,
            item.Classification.ToString(),
            item.RequestedMetricCode,
            item.AffectedDataCodeOrName,
            item.SymbolCountTotal,
            item.SymbolCountMatched,
            item.SubmittedAt,
            item.FrequencyCount,
            item.ResolvedAt)).ToArray());
    }

    [HttpGet("missing-answer-feedback/summary")]
    public async Task<ActionResult<AdminMissingAnswerFeedbackSummary>> GetMissingAnswerFeedbackSummary(
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        CancellationToken cancellationToken = default)
    {
        var counts = await missingAnswerFeedback.GetCountByClassificationAsync(dateFrom, dateTo, cancellationToken);
        var byCode = counts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
        return Ok(new AdminMissingAnswerFeedbackSummary(
            dateFrom,
            dateTo,
            byCode,
            counts.Values.Sum()));
    }

    [HttpGet("provider-health")]
    public async Task<ActionResult<AdminProviderHealthResponse>> GetProviderHealth(
        CancellationToken cancellationToken)
    {
        var health = await providerHealth.CheckAsync(cancellationToken);
        return Ok(new AdminProviderHealthResponse(
            health.ProviderName,
            health.Status.ToString(),
            health.CheckedAt,
            health.Detail));
    }

    // Spec 051: per-source freshness, reporting frozen-archive sources distinctly from current sources.
    [HttpGet("source-freshness")]
    public async Task<ActionResult<IReadOnlyCollection<AdminSourceFreshnessResponse>>> GetSourceFreshness(
        [FromQuery] int recentRunSampleSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (recentRunSampleSize is < 1 or > 500)
        {
            ModelState.AddModelError(nameof(recentRunSampleSize), "Recent run sample size must be between 1 and 500.");
            return ValidationProblem(ModelState);
        }

        var freshness = await sourceFreshnessReader.QueryAsync(recentRunSampleSize, cancellationToken);
        return Ok(freshness.Select(source => new AdminSourceFreshnessResponse(
            source.Vendor.ToString(),
            source.Source.ToString(),
            source.Mode.ToString(),
            source.SourceName,
            source.IsFrozenArchive,
            source.LastSuccessfulRunAt,
            source.RecentSuccessfulRuns,
            source.RecentFailedRuns)).ToArray());
    }

    // --- Spec 058: live data sync monitor ---

    [HttpGet("data-sync/activity")]
    public async Task<ActionResult<AdminDataSyncActivitySnapshotResponse>> GetDataSyncActivity(
        [FromQuery] int recentPerProvider = 5,
        CancellationToken cancellationToken = default)
    {
        if (recentPerProvider is < 1 or > 20)
        {
            ModelState.AddModelError(nameof(recentPerProvider), "recentPerProvider must be between 1 and 20.");
            return ValidationProblem(ModelState);
        }

        var snapshot = await activityMonitor.GetSnapshotAsync(recentPerProvider, cancellationToken);
        return Ok(ToActivitySnapshotResponse(snapshot));
    }

    [HttpGet("data-sync/activity/stream")]
    public async Task StreamDataSyncActivity(CancellationToken cancellationToken)
    {
        if (activityMonitor.ActiveConnections >= 10)
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.Body.FlushAsync(cancellationToken);

        var channel = Channel.CreateBounded<DataSyncActivityEvent>(
            new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });

        var subscribeTask = activityMonitor.SubscribeAsync(channel.Writer, cancellationToken);

        try
        {
            await foreach (var @event in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await WriteSseEventAsync(@event, cancellationToken);

                if (@event.Kind == DataSyncActivityEventKind.Close)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected â€” normal exit.
        }
        finally
        {
            channel.Writer.TryComplete();
            await subscribeTask.ConfigureAwait(false);
        }
    }

    private async Task WriteSseEventAsync(DataSyncActivityEvent @event, CancellationToken cancellationToken)
    {
        var eventName = @event.Kind switch
        {
            DataSyncActivityEventKind.Snapshot => "snapshot",
            DataSyncActivityEventKind.Update => "update",
            DataSyncActivityEventKind.Heartbeat => "heartbeat",
            DataSyncActivityEventKind.Close => "close",
            _ => "update"
        };

        object? payload = @event.Kind switch
        {
            DataSyncActivityEventKind.Snapshot when @event.Snapshot is not null =>
                ToActivitySnapshotResponse(@event.Snapshot),
            DataSyncActivityEventKind.Update when @event.UpdatedItems is not null =>
                @event.UpdatedItems.Select(ToActivityItemResponse).ToArray(),
            DataSyncActivityEventKind.Heartbeat =>
                new { at = @event.HeartbeatAt?.ToString("O") },
            DataSyncActivityEventKind.Close =>
                new { reason = @event.CloseReason },
            _ => null
        };

        var data = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var line = $"event: {eventName}\ndata: {data}\n\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        await Response.Body.WriteAsync(bytes, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static AdminDataSyncActivitySnapshotResponse ToActivitySnapshotResponse(
        DataSyncActivitySnapshot snapshot) =>
        new(
            snapshot.ActiveRuns.Select(ToActivityItemResponse).ToArray(),
            snapshot.RecentRuns.Select(ToActivityItemResponse).ToArray());

    private static AdminDataSyncActivityItemResponse ToActivityItemResponse(DataSyncActivityItem item) =>
        new(
            item.RunId,
            item.Provider,
            item.Dataset,
            item.Status,
            item.StartedAt,
            item.CompletedAt,
            item.DurationMs,
            item.ProcessedRecords,
            item.ErrorCount,
            item.ErrorMessage,
            item.TriggerSource,
            item.RequestedShamsiMonth,
            item.LogicalVendor,
            item.PhysicalSource,
            item.SourceMode);

    private async Task<ActionResult<AdminDataSyncQueuedResponse>> QueueAsync(
        ProviderDataset dataset,
        AdminDataSyncRequest? request,
        bool requiresReference,
        CancellationToken cancellationToken,
        string? providerName = null)
    {
        var externalReference = string.IsNullOrWhiteSpace(request?.ExternalReference)
            ? null
            : request.ExternalReference.Trim();
        if (requiresReference && externalReference is null)
        {
            ModelState.AddModelError(
                nameof(AdminDataSyncRequest.ExternalReference),
                "External reference is required for this synchronization dataset.");
            return ValidationProblem(ModelState);
        }

        var selectedProviderName = string.IsNullOrWhiteSpace(providerName)
            ? request?.ProviderName?.Trim()
            : providerName;
        var now = timeProvider.GetUtcNow();
        var idempotencyKey = string.IsNullOrWhiteSpace(request?.IdempotencyKey)
            ? $"admin-data-sync:{dataset}:{externalReference ?? "all"}:{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();
        var message = new DataSyncRequest(
            Guid.NewGuid(),
            dataset,
            externalReference,
            now,
            idempotencyKey,
            ProviderName: selectedProviderName);

        await publisher.PublishAsync(message, cancellationToken);
        return Accepted(new AdminDataSyncQueuedResponse(
            message.RequestId,
            message.Dataset.ToString(),
            message.ExternalReference,
            message.RequestedAt,
            message.IdempotencyKey,
            DataSyncRunStatus.Queued.ToString()));
    }

    private string ResolveCuratedFundamentalIndexProviderName(string? providerName)
    {
        var normalized = string.IsNullOrWhiteSpace(providerName)
            ? ProviderSources.NoavaranCurrentApiName
            : ProviderSources.NormalizeName(providerName.Trim());

        if (!string.Equals(normalized, ProviderSources.NoavaranCurrentApiName, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(AdminEligibleFundamentalIndexBulkSyncRequest.ProviderName),
                $"ProviderName must be '{ProviderSources.NoavaranCurrentApiName}' for curated fundamental-index sync.");
        }

        return ProviderSources.NoavaranCurrentApiName;
    }

    private static AdminNadpcoScheduledSyncRunResponse ToScheduledSyncRunResponse(
        NadpcoScheduledSyncRun run) =>
        new(
            run.RunId,
            run.TriggerSource.ToString(),
            run.Status.ToString(),
            run.StartedAt,
            run.CompletedAt,
            run.LastSuccessfulExecutionAt,
            run.ProcessedBatches,
            run.FailedBatches,
            run.RetryAttempts,
            run.Diagnostics,
            run.ScheduleSnapshotJson,
            run.DatasetSelectionJson,
            run.LockOwner,
            run.LockLeaseExpiresAt,
            run.AlertEmitted,
            run.ManualReason);
}
