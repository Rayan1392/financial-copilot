using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/ai/monthly-sales-quality-rankings")]
[Route("api/ai/v1/monthly-sales-quality-rankings")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MonthlySalesQualityRankingsController(
    IMonthlySalesQualityRankingQueryUseCase queryUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MonthlySalesQualityRankingHttpResponse>> GetRanking(
        [FromQuery] int? reportYear,
        [FromQuery] byte? reportMonth,
        [FromQuery] Guid? industryId,
        [FromQuery] Guid? industryGroupId,
        [FromQuery] string[]? symbols,
        [FromQuery] string? scope,
        [FromQuery] string? direction,
        [FromQuery] int? limit = null,
        [FromQuery] decimal? minimumSalesAmount = null,
        [FromQuery] bool includeExplanation = true,
        [FromQuery] bool includeDimensionScores = true,
        [FromQuery] bool onlyEligibleRows = true,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 50)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 50.");
            return ValidationProblem(ModelState);
        }

        var query = new MonthlySalesQualityRankingQuery(
            ReportYear: reportYear,
            ReportMonth: reportMonth,
            IndustryId: industryId,
            IndustryGroupId: industryGroupId,
            Symbols: symbols,
            Scope: ParseScope(scope, industryId, industryGroupId, symbols),
            Direction: ParseDirection(direction),
            Limit: limit ?? 0,
            MinimumSalesAmount: minimumSalesAmount,
            IncludeExplanation: includeExplanation,
            IncludeDimensionScores: includeDimensionScores,
            OnlyEligibleRows: onlyEligibleRows);

        var result = await queryUseCase.ExecuteAsync(query, cancellationToken);
        return Ok(Map(result));
    }

    private static MonthlySalesQualityScope ParseScope(
        string? value,
        Guid? industryId,
        Guid? industryGroupId,
        IReadOnlyCollection<string>? symbols)
    {
        if (Enum.TryParse<MonthlySalesQualityScope>(value, ignoreCase: true, out var parsed))
            return parsed;
        if (symbols is { Count: > 0 }) return MonthlySalesQualityScope.Symbols;
        if (industryId.HasValue || industryGroupId.HasValue) return MonthlySalesQualityScope.Industry;
        return MonthlySalesQualityScope.Market;
    }

    private static MonthlySalesQualityDirection ParseDirection(string? value) =>
        Enum.TryParse<MonthlySalesQualityDirection>(value, ignoreCase: true, out var parsed)
            ? parsed
            : MonthlySalesQualityDirection.Top;

    private static MonthlySalesQualityRankingHttpResponse Map(MonthlySalesQualityRankingResponse response) => new(
        response.ReportYear,
        response.ReportMonth,
        response.Scope.ToString(),
        response.Direction.ToString(),
        response.TotalEligibleCompanies,
        response.GeneratedAtUtc,
        response.Items.Select(Map).ToList());

    private static MonthlySalesQualityRankingItemHttpResponse Map(MonthlySalesQualityRankingItem item) => new(
        item.Rank,
        item.Symbol,
        item.CompanyName,
        item.IndustryTitle,
        item.QualityScore,
        item.QualityLabel,
        item.ConfidenceScore,
        item.MonthlySalesAmount,
        item.Avg12MonthSalesAmount,
        item.SalesVsAvg12MPercent,
        item.SalesMonthOverMonthPercent,
        item.SalesYearOverYearPercent,
        item.DimensionScores is null ? null : new MonthlySalesQualityDimensionScoresHttpResponse(
            item.DimensionScores.SalesGrowthVs12M,
            item.DimensionScores.QuantityGrowthQuality,
            item.DimensionScores.RateGrowthQuality,
            item.DimensionScores.ProductMixStrength,
            item.DimensionScores.PersistenceTrend,
            item.DimensionScores.IndustryRelativeStrength),
        item.PositiveDrivers,
        item.NegativeDrivers,
        new MonthlySalesQualityDataCoverageHttpResponse(
            item.DataCoverage.HistoryMonths,
            item.DataCoverage.HasProductLineItems,
            item.DataCoverage.HasProductMix,
            item.DataCoverage.IndustryPeerCount),
        item.SourceProviderName,
        item.CalculatedAtUtc);
}

[ApiController]
[Route("api/v1/admin/monthly-sales-quality-rankings")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminMonthlySalesQualityRankingsController(
    IRecalculateMonthlySalesQualityRankingUseCase recalculateUseCase) : ControllerBase
{
    [HttpPost("recalculate")]
    public async Task<ActionResult<RecalculateMonthlySalesQualityRankingHttpResponse>> Recalculate(
        [FromBody] RecalculateMonthlySalesQualityRankingHttpRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await recalculateUseCase.ExecuteAsync(
            new RecalculateMonthlySalesQualityRankingRequest(request?.ReportYear, request?.ReportMonth),
            cancellationToken);

        return Ok(new RecalculateMonthlySalesQualityRankingHttpResponse(
            result.ReportYear,
            result.ReportMonth,
            result.EligibleCompanies,
            result.SkippedCompanies,
            result.CalculatedAtUtc));
    }
}
