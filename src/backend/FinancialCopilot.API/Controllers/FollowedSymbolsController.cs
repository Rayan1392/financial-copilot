using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/followed-symbols")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class FollowedSymbolsController(
    ICurrentActorContext actorContext,
    IGetMyFollowedSymbolsUseCase getMyFollowedSymbols,
    IFollowSymbolUseCase followSymbol,
    IUnfollowSymbolUseCase unfollowSymbol,
    IReplaceMyFollowedSymbolsUseCase replaceMyFollowedSymbols) : ControllerBase
{
    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistReadSelf)]
    public async Task<ActionResult<FollowedSymbolsResponse>> GetMine(CancellationToken cancellationToken)
    {
        var result = await getMyFollowedSymbols.ExecuteAsync(
            new GetMyFollowedSymbolsQuery(actorContext.Actor),
            cancellationToken);
        return Ok(Map(result));
    }

    [HttpPost("me/{externalCompanyId}")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<ActionResult<FollowedSymbolResponse>> Follow(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await followSymbol.ExecuteAsync(
                new FollowSymbolCommand(actorContext.Actor, externalCompanyId, Source: "Api"),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (FollowedSymbolValidationException exception)
        {
            ModelState.AddModelError(nameof(externalCompanyId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpDelete("me/{externalCompanyId}")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<ActionResult<FollowedSymbolsResponse>> Unfollow(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await unfollowSymbol.ExecuteAsync(
                new UnfollowSymbolCommand(actorContext.Actor, externalCompanyId),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (FollowedSymbolValidationException exception)
        {
            ModelState.AddModelError(nameof(externalCompanyId), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPut("me")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<ActionResult<FollowedSymbolsResponse>> ReplaceMine(
        ReplaceFollowedSymbolsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await replaceMyFollowedSymbols.ExecuteAsync(
                new ReplaceMyFollowedSymbolsCommand(
                    actorContext.Actor,
                    request.ExternalCompanyIds ?? [],
                    request.Source ?? "Api"),
                cancellationToken);
            return Ok(Map(result));
        }
        catch (FollowedSymbolValidationException exception)
        {
            ModelState.AddModelError(nameof(request.ExternalCompanyIds), exception.Message);
            return ValidationProblem(ModelState);
        }
    }

    private static FollowedSymbolsResponse Map(IReadOnlyCollection<FollowedSymbolDto> symbols) =>
        new(symbols.Select(Map).ToArray());

    private static FollowedSymbolResponse Map(FollowedSymbolDto symbol) =>
        new(
            symbol.ExternalCompanyId,
            symbol.Symbol,
            symbol.CompanyName,
            symbol.CompanyNameEnglish,
            symbol.FollowedAtUtc,
            symbol.Source);
}
