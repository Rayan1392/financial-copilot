using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;
using Microsoft.AspNetCore.Authorization;
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
    IMissingAnswerFeedbackRepository missingAnswerFeedback,
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
            state.LastRunCompletedAt)).ToArray());
    }

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
