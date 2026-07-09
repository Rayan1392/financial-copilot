using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Domain.Financial.Insights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/insights")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MarketInsightsController(
    IGetMarketInsightFeedUseCase feedUseCase) : ControllerBase
{
    [HttpGet("market")]
    public Task<ActionResult<InsightFeedHttpResponse>> GetMarketFeed(
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] bool includeExpired = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default) =>
        QueryAsync(null, null, type, severity, dateFrom, dateTo, includeExpired, skip, take, cancellationToken);

    [HttpGet("symbol/{symbol}")]
    public Task<ActionResult<InsightFeedHttpResponse>> GetSymbolFeed(
        string symbol,
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] bool includeExpired = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default) =>
        QueryAsync(symbol, null, type, severity, dateFrom, dateTo, includeExpired, skip, take, cancellationToken);

    [HttpGet("industries/{industryCode}")]
    public Task<ActionResult<InsightFeedHttpResponse>> GetIndustryFeed(
        string industryCode,
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] bool includeExpired = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default) =>
        QueryAsync(null, industryCode, type, severity, dateFrom, dateTo, includeExpired, skip, take, cancellationToken);

    private async Task<ActionResult<InsightFeedHttpResponse>> QueryAsync(
        string? symbol,
        string? industryCode,
        string? type,
        string? severity,
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        bool includeExpired,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(take), "Take must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        if (skip < 0)
        {
            ModelState.AddModelError(nameof(skip), "Skip must be zero or greater.");
            return ValidationProblem(ModelState);
        }

        if (!TryParseEnum<InsightType>(type, nameof(type), out var parsedType)) return ValidationProblem(ModelState);
        if (!TryParseEnum<InsightSeverity>(severity, nameof(severity), out var parsedSeverity)) return ValidationProblem(ModelState);

        var response = await feedUseCase.ExecuteAsync(
            new InsightFeedQuery(
                Symbol: symbol,
                IndustryCode: industryCode,
                InsightType: parsedType,
                Severity: parsedSeverity,
                DateFrom: dateFrom,
                DateTo: dateTo,
                IncludeExpired: includeExpired,
                Skip: skip,
                Take: take),
            cancellationToken);

        return Ok(Map(response));
    }

    private bool TryParseEnum<T>(string? value, string parameterName, out T? parsed)
        where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
        {
            parsed = result;
            return true;
        }

        ModelState.AddModelError(parameterName, $"Unknown {parameterName} '{value}'. Valid: {string.Join(", ", Enum.GetNames<T>())}.");
        return false;
    }

    internal static InsightFeedHttpResponse Map(InsightFeedResponse response) => new(
        response.TotalCount,
        response.GeneratedAtUtc,
        response.Items.Select(item => new InsightFeedItemHttpResponse(
            item.Id,
            item.ExternalCompanyId,
            item.Symbol,
            item.IndustryCode,
            item.InsightType.ToString(),
            item.Severity.ToString(),
            item.ImportanceScore,
            item.ConfidenceScore,
            item.Title,
            item.Summary,
            item.Reason,
            item.Evidence.Select(e => new InsightEvidenceItemHttpResponse(
                e.Label,
                e.Value,
                e.SourceProvider,
                e.SourcePeriod,
                e.LastSyncedAtUtc)).ToArray(),
            item.SourceProviderName,
            item.SourceEntityType.ToString(),
            item.SourceEntityId,
            item.SourcePeriod,
            item.DetectedAtUtc,
            item.ExpiresAtUtc,
            item.SuggestedActions.Select(action => action.ToString()).ToArray())).ToArray());
}

[ApiController]
[Route("api/v1/admin/insights")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminMarketInsightsController(
    IGenerateMarketInsightsUseCase generateUseCase) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult<GenerateMarketInsightsHttpResponse>> Generate(
        [FromBody] GenerateMarketInsightsHttpRequest? request,
        CancellationToken cancellationToken)
    {
        var lookbackDays = request?.LookbackDays ?? 7;
        if (lookbackDays is < 1 or > 90)
        {
            ModelState.AddModelError(nameof(GenerateMarketInsightsHttpRequest.LookbackDays), "LookbackDays must be between 1 and 90.");
            return ValidationProblem(ModelState);
        }

        var result = await generateUseCase.ExecuteAsync(
            new GenerateMarketInsightsRequest(lookbackDays),
            cancellationToken);

        return Ok(new GenerateMarketInsightsHttpResponse(
            result.DetectorsRun,
            result.EventsDetected,
            result.EventsPersisted,
            result.GeneratedAtUtc));
    }
}
