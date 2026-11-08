using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.API.Controllers;

/// <summary>
/// Feature 148 CyclicalWaves v2 data operations. Catalog and single-ticker syncs run inline (short);
/// screener, income statements, analysis and Full are queued for the Worker so no HTTP request runs a full import.
/// </summary>
[ApiController]
[Route("api/v1/admin/cyclicalwaves-v2")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class CyclicalWavesV2AdminController(
    ICyclicalWavesV2SyncCoordinator coordinator,
    ICyclicalWavesV2SyncRunStore runStore,
    IOptions<CyclicalWavesV2Options> options,
    IOptions<FinancialDataOptions> financialDataOptions,
    ICurrentActorContext currentActor,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("catalog/sync")]
    public Task<ActionResult<CyclicalWavesV2InlineRunResponse>> SyncCatalog(CancellationToken cancellationToken) =>
        RunInlineAsync(CyclicalWavesV2SyncScope.Catalog, null, cancellationToken);

    [HttpPost("tickers/{ticker}/sync")]
    public Task<ActionResult<CyclicalWavesV2InlineRunResponse>> SyncTicker(string ticker, CancellationToken cancellationToken) =>
        RunInlineAsync(CyclicalWavesV2SyncScope.Ticker, ticker, cancellationToken);

    [HttpPost("screener/sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueScreener(CancellationToken cancellationToken) =>
        QueueAsync(CyclicalWavesV2SyncScope.Screener, null, cancellationToken);

    [HttpPost("income-statements/sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueIncomeStatements(CancellationToken cancellationToken) =>
        QueueAsync(CyclicalWavesV2SyncScope.IncomeStatements, null, cancellationToken);

    [HttpPost("gauge-catalog/sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueGaugeCatalog(CancellationToken cancellationToken) =>
        QueueAsync(CyclicalWavesV2SyncScope.GaugeCatalog, null, cancellationToken);

    [HttpPost("gauges/sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueGaugeDetails(CancellationToken cancellationToken) =>
        QueueAsync(CyclicalWavesV2SyncScope.GaugeDetails, null, cancellationToken);

    [HttpPost("equilibrium-catalog/sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueEquilibriumCatalog(CancellationToken cancellationToken) =>
        QueueAsync(CyclicalWavesV2SyncScope.EquilibriumCatalog, null, cancellationToken);

    [HttpPost("equilibrium-ranges/sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueEquilibriumDetails(CancellationToken cancellationToken) =>
        QueueAsync(CyclicalWavesV2SyncScope.EquilibriumDetails, null, cancellationToken);

    [HttpPost("tickers/{ticker}/gauge-equilibrium/sync")]
    public Task<ActionResult<CyclicalWavesV2InlineRunResponse>> SyncTickerGaugeEquilibrium(string ticker, CancellationToken cancellationToken) =>
        RunInlineAsync(CyclicalWavesV2SyncScope.TickerGaugeEquilibrium, ticker, cancellationToken);

    [HttpPost("analysis/sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueAnalysis(
        [FromQuery] string? mode,
        CancellationToken cancellationToken)
    {
        if (!TryParseMode(mode, out var analysisMode))
        {
            ModelState.AddModelError(nameof(mode), "Mode must be 'incremental' or 'full'.");
            return Task.FromResult<ActionResult<CyclicalWavesV2QueuedRunResponse>>(ValidationProblem(ModelState));
        }

        return QueueAsync(CyclicalWavesV2SyncScope.Analysis, analysisMode, cancellationToken);
    }

    [HttpPost("sync")]
    public Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueFull(CancellationToken cancellationToken) =>
        QueueAsync(CyclicalWavesV2SyncScope.Full, null, cancellationToken);

    [HttpGet("status")]
    public async Task<ActionResult<CyclicalWavesV2StatusResponse>> GetStatus(
        [FromQuery] int recent = 10,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var runs = await runStore.GetRecentRunsAsync(Math.Clamp(recent, 1, 50), cancellationToken);
        var history = await runStore.GetRecentRunsAsync(100, cancellationToken);
        var active = runs.Concat(history).FirstOrDefault(run =>
            run.Status is CyclicalWavesV2SyncRunStatus.Running or CyclicalWavesV2SyncRunStatus.Requested);
        var lastSuccess = history
            .Where(run => run.Status is CyclicalWavesV2SyncRunStatus.Succeeded or CyclicalWavesV2SyncRunStatus.CompletedWithErrors &&
                          run.CompletedAt is not null)
            .GroupBy(run => run.Scope)
            .Select(group => new CyclicalWavesV2LastSuccessResponse(group.Key.ToString(), group.Max(run => run.CompletedAt!.Value)))
            .OrderBy(item => item.Scope, StringComparer.Ordinal)
            .ToList();

        return Ok(new CyclicalWavesV2StatusResponse(
            settings.IngestionEnabled,
            settings.DailySyncEnabled,
            settings.DailySyncCronUtc,
            settings.IngestionEnabled && settings.DailySyncEnabled
                ? CyclicalWavesUtcCronSchedule.Parse(settings.DailySyncCronUtc).GetNextOccurrence(timeProvider.GetUtcNow())
                : null,
            !string.IsNullOrWhiteSpace(settings.Identifier) && !string.IsNullOrWhiteSpace(settings.Password),
            financialDataOptions.Value.ChatProvider.ToString(),
            active is null ? null : ToResponse(active),
            lastSuccess,
            runs.Select(ToResponse).ToList()));
    }

    private async Task<ActionResult<CyclicalWavesV2InlineRunResponse>> RunInlineAsync(
        CyclicalWavesV2SyncScope scope,
        string? ticker,
        CancellationToken cancellationToken)
    {
        if (!options.Value.IngestionEnabled)
            return IngestionDisabled();

        var outcome = await coordinator.RunAsync(
            new CyclicalWavesV2SyncRequest(scope, ticker?.Trim(), null, CyclicalWavesV2SyncTrigger.Admin, RequestedBy()),
            $"admin:{RequestedBy()}",
            cancellationToken);
        if (outcome.Status == CyclicalWavesV2SyncRunStatus.SkippedAlreadyRunning)
        {
            var active = (await runStore.GetRecentRunsAsync(20, cancellationToken))
                .FirstOrDefault(run => run.Status == CyclicalWavesV2SyncRunStatus.Running);
            return Conflict(new CyclicalWavesV2ConflictResponse("SyncAlreadyRunning", active?.RunId ?? Guid.Empty));
        }

        return Ok(new CyclicalWavesV2InlineRunResponse(outcome.RunId, outcome.Status.ToString(), ToResponse(outcome.Counts), outcome.ErrorSummary));
    }

    private async Task<ActionResult<CyclicalWavesV2QueuedRunResponse>> QueueAsync(
        CyclicalWavesV2SyncScope scope,
        CyclicalWavesV2AnalysisMode? analysisMode,
        CancellationToken cancellationToken)
    {
        if (!options.Value.IngestionEnabled)
            return IngestionDisabled();

        var result = await runStore.RequestAsync(
            new CyclicalWavesV2SyncRequest(scope, null, analysisMode, CyclicalWavesV2SyncTrigger.Admin, RequestedBy()),
            timeProvider.GetUtcNow(),
            cancellationToken);
        return result.Accepted
            ? Accepted(new CyclicalWavesV2QueuedRunResponse(result.RunId, CyclicalWavesV2SyncRunStatus.Requested.ToString()))
            : Conflict(new CyclicalWavesV2ConflictResponse("SyncAlreadyActive", result.RunId));
    }

    private ObjectResult IngestionDisabled() =>
        Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "IngestionDisabled",
            detail: "CyclicalWaves v2 ingestion is disabled (CyclicalWavesV2:IngestionEnabled=false).",
            type: "IngestionDisabled");

    private string RequestedBy()
    {
        var actor = currentActor.Actor;
        return $"{actor.ActorType}:{actor.ActorId}";
    }

    private static bool TryParseMode(string? mode, out CyclicalWavesV2AnalysisMode analysisMode)
    {
        analysisMode = CyclicalWavesV2AnalysisMode.Incremental;
        if (string.IsNullOrWhiteSpace(mode))
            return true;
        return Enum.TryParse(mode.Trim(), ignoreCase: true, out analysisMode) && Enum.IsDefined(analysisMode);
    }

    private static CyclicalWavesV2RunCountsResponse ToResponse(CyclicalWavesV2RunCounts counts) =>
        new(counts.TotalItems, counts.ProcessedItems, counts.UpdatedItems, counts.UnchangedItems, counts.FailedItems);

    private static CyclicalWavesV2RunResponse ToResponse(CyclicalWavesV2SyncRunSummary run) =>
        new(run.RunId,
            run.Scope.ToString(),
            run.Ticker,
            run.AnalysisMode?.ToString(),
            run.Trigger.ToString(),
            run.Status.ToString(),
            run.RequestedAt,
            run.StartedAt,
            run.CompletedAt,
            ToResponse(run.Counts),
            run.ErrorSummary,
            run.RequestedBy);
}
