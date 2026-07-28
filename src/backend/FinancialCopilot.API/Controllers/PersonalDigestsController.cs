using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Domain.Financial.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/digests/me")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class PersonalDigestsController(
    ICurrentActorContext actorContext,
    IMarketReportService reports) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<ActionResult<MarketReportView>> GetLatest(CancellationToken cancellationToken)
    {
        var report = await reports.GetLatestPersonalAsync(actorContext.Actor, cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("history")]
    public async Task<ActionResult<MarketReportHistoryPage>> GetHistory(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] MarketReportStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(() => reports.GetPersonalHistoryAsync(
            actorContext.Actor,
            new MarketReportHistoryQuery(from, to, status, page, pageSize),
            cancellationToken));

    [HttpGet("{reportId:guid}")]
    public async Task<ActionResult<MarketReportView>> GetVersion(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var report = await reports.GetPersonalVersionAsync(actorContext.Actor, reportId, cancellationToken);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpPost("generate")]
    public Task<ActionResult<MarketReportView>> Generate(
        GeneratePersonalDigestRequest? request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => reports.GeneratePersonalAsync(
            new GeneratePersonalDigestCommand(
                actorContext.Actor,
                HttpContext.TraceIdentifier,
                request?.PublishNotification ?? false),
            cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (MarketReportValidationException exception)
        {
            ModelState.AddModelError("digest", exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (MarketReportAccessDeniedException)
        {
            return Forbid();
        }
    }
}

public sealed record GeneratePersonalDigestRequest(bool PublishNotification = false);
