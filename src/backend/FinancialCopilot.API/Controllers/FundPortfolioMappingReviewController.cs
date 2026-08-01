using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/fund-portfolio-mapping-reviews")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class FundPortfolioMappingReviewController(
    IFundPortfolioMappingReviewRepository reviews,
    IResolveFundPortfolioMappingReviewUseCase resolve,
    IFundPortfolioAuditSink audit) : ControllerBase
{
    [HttpGet]
    public Task<FundPortfolioMappingReviewPage> List(FundPortfolioMappingReviewStatus? status, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default) => reviews.ListPageAsync(status, page, pageSize, cancellationToken);

    [HttpPost("{reviewId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid reviewId, [FromBody] ResolveFundPortfolioMappingReviewBody body, CancellationToken cancellationToken)
    {
        var changed = await resolve.ExecuteAsync(new(reviewId, body.ExpectedVersion, body.Approve, body.ResolutionJson, body.ResolvedByActorId), cancellationToken);
        if (changed.Changed) await audit.WriteAsync(new(body.Approve ? "mapping-approved" : "mapping-rejected", body.ResolvedByActorId, null, null, reviewId, Guid.NewGuid().ToString("N"), $"Fund portfolio mapping review resolved. AffectedReportCount={changed.AffectedReportCount} PreviousResolution={changed.PreviousResolutionJson ?? "none"}"), cancellationToken);
        return changed.Changed ? NoContent() : Conflict(new { code = "REVIEW_VERSION_CONFLICT", message = "The review was changed or resolved by another administrator." });
    }
}

public sealed record ResolveFundPortfolioMappingReviewBody(int ExpectedVersion, bool Approve, string ResolutionJson, string ResolvedByActorId);
