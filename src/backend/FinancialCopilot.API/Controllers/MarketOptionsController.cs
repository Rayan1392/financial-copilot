using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

/// <summary>Feature 167: option chain of an underlying symbol and the call exercise ranking.</summary>
[ApiController]
[Route("api/v1/market/options")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MarketOptionsController(
    IOptionChainReader chainReader,
    CallExerciseRankingUseCase callExerciseRanking,
    IOptionComparisonUseCase comparisonUseCase,
    IOptionMarketSnapshotReader snapshotReader) : ControllerBase
{
    /// <summary>
    /// Feature 168: the option comparison of one underlying (calls, puts and covered calls with
    /// fees), the same payload the assistant answer carries. Not charged; reads stored data only.
    /// </summary>
    [HttpGet("comparison")]
    public async Task<ActionResult<OptionComparison>> GetComparison(
        [FromQuery] string? underlying,
        [FromQuery] string? view,
        [FromQuery] DateOnly? expiry,
        [FromQuery] int quantity = 1,
        CancellationToken cancellationToken = default)
    {
        var requestedView = OptionComparisonView.Call;
        if (string.IsNullOrWhiteSpace(underlying))
            ModelState.AddModelError(nameof(underlying), "Underlying symbol is required.");
        if (quantity is < 1 or > MaxQuantity)
            ModelState.AddModelError(nameof(quantity), $"Quantity must be between 1 and {MaxQuantity} contracts.");
        if (!string.IsNullOrWhiteSpace(view) && !Enum.TryParse(view.Replace("_", string.Empty), ignoreCase: true, out requestedView))
            ModelState.AddModelError(nameof(view), "View must be call, put or covered_call.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var outcome = await comparisonUseCase.ExecuteAsync(
            new OptionComparisonQuery(underlying!, requestedView, quantity, expiry), cancellationToken);
        return outcome.Failure switch
        {
            OptionComparisonFailure.None => Ok(outcome.Comparison),
            OptionComparisonFailure.NoUnderlyingPrice => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "No price is stored for the underlying symbol, so options cannot be compared."),
            _ => NotFound()
        };
    }

    /// <summary>Feature 168: symbols that have at least one unexpired listed option; used to show option entry points.</summary>
    [HttpGet("underlyings")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetUnderlyings(CancellationToken cancellationToken) =>
        Ok(await snapshotReader.ListUnderlyingSymbolsAsync(cancellationToken));

    private const int MaxQuantity = 100_000;

    /// <summary>Calls and puts per strike for one underlying and expiry (nearest unexpired expiry by default).</summary>
    [HttpGet("chain")]
    public async Task<ActionResult<OptionChain>> GetChain(
        [FromQuery] string? underlying,
        [FromQuery] DateOnly? expiry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(underlying))
        {
            ModelState.AddModelError(nameof(underlying), "Underlying symbol is required.");
            return ValidationProblem(ModelState);
        }

        var chain = await chainReader.GetChainAsync(underlying, expiry, cancellationToken);
        return chain is null ? NotFound() : Ok(chain);
    }

    /// <summary>Calls of one underlying ordered by the cost of buying the share through exercise at expiry.</summary>
    [HttpGet("call-exercise-ranking")]
    public async Task<ActionResult<CallExerciseRankingResult>> GetCallExerciseRanking(
        [FromQuery] string? underlying,
        [FromQuery] DateOnly? expiry,
        [FromQuery] int quantity = 1,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(underlying))
            ModelState.AddModelError(nameof(underlying), "Underlying symbol is required.");
        if (quantity is < 1 or > MaxQuantity)
            ModelState.AddModelError(nameof(quantity), $"Quantity must be between 1 and {MaxQuantity} contracts.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var (result, failure) = await callExerciseRanking.ExecuteAsync(underlying!, expiry, quantity, cancellationToken);
        return failure switch
        {
            CallExerciseRankingFailure.None => Ok(result),
            CallExerciseRankingFailure.NoUnderlyingPrice => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "No price is stored for the underlying symbol, so calls cannot be ranked."),
            _ => NotFound()
        };
    }
}
