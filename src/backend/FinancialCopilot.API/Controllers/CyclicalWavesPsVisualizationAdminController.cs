using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.API.Security;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.API.Controllers;

/// <summary>Bounded, payload-free operations for the persisted CyclicalWaves P/S visualization feed.</summary>
[ApiController]
[Route("api/v1/admin/cyclicalwaves/ps-visualization")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class CyclicalWavesPsVisualizationAdminController(
    IPsEligibleCompanyScopeReader scopeReader,
    ICyclicalWavesPsVisualizationSyncService syncService,
    ICompanyPsVisualizationReader reader,
    IOptions<CyclicalWavesPsSyncOptions> options) : ControllerBase
{
    [HttpPost("scope/dry-run")]
    public async Task<ActionResult<PsScopeResponse>> DryRun([FromBody] PsSyncCommand? command, CancellationToken cancellationToken)
    {
        var scope = await scopeReader.ReadAsync(command?.MaxCompanies, cancellationToken);
        return Ok(new PsScopeResponse(scope.EligibleRowsRead, scope.DuplicateRowsRemoved, scope.SkippedMissingOrInvalidIsins,
            scope.Companies.Take(50).Select(x => new PsCompanyPreview(x.CompanyId, x.CompanyIsin)).ToArray(),
            scope.Issues.Take(50).Select(x => new PsIssueResponse(x.CompanyId, x.CompanyIsin, x.Code)).ToArray()));
    }

    [HttpPost("sync")]
    public Task<ActionResult<PsSyncResponse>> Sync([FromBody] PsSyncCommand? command, CancellationToken cancellationToken) => ExecuteAsync(command, false, false, cancellationToken);
    [HttpPost("snapshot")]
    public Task<ActionResult<PsSyncResponse>> Snapshot([FromBody] PsSyncCommand? command, CancellationToken cancellationToken) => ExecuteAsync(command, true, false, cancellationToken);
    [HttpPost("history")]
    public Task<ActionResult<PsSyncResponse>> History([FromBody] PsSyncCommand? command, CancellationToken cancellationToken) => ExecuteAsync(command, false, true, cancellationToken);

    [HttpGet("companies/{companyId:guid}")]
    public async Task<ActionResult<PsReadResponse>> Get(Guid companyId, CancellationToken cancellationToken)
    {
        var model = await reader.GetAsync(companyId, cancellationToken);
        return model is null ? NotFound() : Ok(new PsReadResponse(model.CompanyId, model.CompletenessStatus.ToString(), model.GaugeRenderabilityStatus.ToString(), model.SnapshotObservationDate, model.LastSnapshotSyncAtUtc, model.LastHistorySyncAtUtc, model.WarningCodes, model.HistoryPoints.Count));
    }

    private async Task<ActionResult<PsSyncResponse>> ExecuteAsync(PsSyncCommand? command, bool snapshotOnly, bool historyOnly, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled && !settings.AllowManualSyncWhenWorkerDisabled)
            return Conflict(new { code = "ManualSyncDisabled" });
        var result = await syncService.SyncAsync(new PsVisualizationSyncRequest(false, command?.MaxCompanies, command?.CompanyId, snapshotOnly, historyOnly, command?.CorrelationId), cancellationToken);
        return Ok(new PsSyncResponse(result.CorrelationId, result.CompaniesConsidered, result.SnapshotSucceeded, result.HistorySucceeded, result.Failed, result.Unchanged, result.ScopeIssues.Take(50).Select(x => new PsIssueResponse(x.CompanyId, x.CompanyIsin, x.Code)).ToArray()));
    }
}

public sealed record PsSyncCommand(int? MaxCompanies = null, Guid? CompanyId = null, string? CorrelationId = null);
public sealed record PsCompanyPreview(Guid CompanyId, string CompanyIsin);
public sealed record PsIssueResponse(Guid? CompanyId, string? CompanyIsin, string Code);
public sealed record PsScopeResponse(int EligibleRowsRead, int DuplicateRowsRemoved, int SkippedMissingOrInvalidIsins, IReadOnlyList<PsCompanyPreview> Preview, IReadOnlyList<PsIssueResponse> Issues);
public sealed record PsSyncResponse(string CorrelationId, int CompaniesConsidered, int SnapshotSucceeded, int HistorySucceeded, int Failed, int Unchanged, IReadOnlyList<PsIssueResponse> Issues);
public sealed record PsReadResponse(Guid CompanyId, string CompletenessStatus, string GaugeRenderabilityStatus, DateOnly? SnapshotObservationDate, DateTimeOffset? LastSnapshotSyncAtUtc, DateTimeOffset? LastHistorySyncAtUtc, IReadOnlyList<string> WarningCodes, int ActiveHistoryPointCount);
