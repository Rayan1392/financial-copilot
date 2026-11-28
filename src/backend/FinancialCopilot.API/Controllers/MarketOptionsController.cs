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
    CallExerciseRankingUseCase callExerciseRanking) : ControllerBase
{
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
