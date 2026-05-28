using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminDataOperationsController(
    IDataSyncRequestPublisher publisher,
    IDataSyncRunReader runReader,
    IFinancialDataProviderHealthService providerHealth,
    ICyclicalWavesFullSyncService cyclicalWavesFullSync,
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

    private async Task<ActionResult<AdminDataSyncQueuedResponse>> QueueAsync(
        ProviderDataset dataset,
        AdminDataSyncRequest? request,
        bool requiresReference,
        CancellationToken cancellationToken)
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

        var now = timeProvider.GetUtcNow();
        var idempotencyKey = string.IsNullOrWhiteSpace(request?.IdempotencyKey)
            ? $"admin-data-sync:{dataset}:{externalReference ?? "all"}:{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();
        var message = new DataSyncRequest(
            Guid.NewGuid(),
            dataset,
            externalReference,
            now,
            idempotencyKey);

        await publisher.PublishAsync(message, cancellationToken);
        return Accepted(new AdminDataSyncQueuedResponse(
            message.RequestId,
            message.Dataset.ToString(),
            message.ExternalReference,
            message.RequestedAt,
            message.IdempotencyKey,
            DataSyncRunStatus.Queued.ToString()));
    }
}
