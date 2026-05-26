using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/billing")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class BillingController(
    ICurrentActorContext actorContext,
    IBillableAccountResolver accountResolver,
    IFinancialAccountingService financialAccounting,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("transactions")]
    public async Task<ActionResult<BillingTransactionsResponse>> GetTransactions(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var periodTo = to ?? timeProvider.GetUtcNow();
        var periodFrom = from ?? periodTo.AddDays(-30);

        if (periodFrom > periodTo)
        {
            ModelState.AddModelError(nameof(from), "Transaction period start must not be after its end.");
            return ValidationProblem(ModelState);
        }

        var actor = actorContext.Actor;
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(
                actor.ActorId,
                actor.TenantId,
                actor.UserId,
                actor.ApiClientId,
                ExternalUserId: null),
            cancellationToken);
        var transactions = await financialAccounting.QueryAsync(
            account.Id,
            periodFrom,
            periodTo,
            cancellationToken);

        return Ok(new BillingTransactionsResponse(
            periodFrom,
            periodTo,
            transactions
                .Select(transaction => new BillingTransactionResponse(
                    transaction.Type.ToString(),
                    transaction.Amount,
                    transaction.Currency,
                    transaction.OccurredAt))
                .ToArray()));
    }
}
