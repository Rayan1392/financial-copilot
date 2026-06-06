using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/usage")]
[Authorize(Policy = AuthorizationPolicies.UsageReadSelf)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class UsageController(
    ICurrentActorContext actorContext,
    IBillableAccountResolver accountResolver,
    IWalletService wallets,
    IApiUsageReportService usageReports,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UsageSummaryResponse>> GetMyUsage(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var periodTo = to ?? timeProvider.GetUtcNow();
        var periodFrom = from ?? periodTo.AddDays(-30);

        if (periodFrom > periodTo)
        {
            ModelState.AddModelError(nameof(from), "Usage period start must not be after its end.");
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
        var wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
        var entries = await usageReports.QueryUsageAsync(
            account.Id,
            periodFrom,
            periodTo,
            cancellationToken);

        return Ok(MapResponse(account, wallet, periodFrom, periodTo, entries));
    }

    [HttpGet("api-client/{clientId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ApiClientOnly)]
    public async Task<ActionResult<UsageSummaryResponse>> GetApiClientUsage(
        Guid clientId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Actor;

        if (actor.ApiClientId != clientId)
        {
            return Forbid();
        }

        var periodTo = to ?? timeProvider.GetUtcNow();
        var periodFrom = from ?? periodTo.AddDays(-30);

        if (periodFrom > periodTo)
        {
            ModelState.AddModelError(nameof(from), "Usage period start must not be after its end.");
            return ValidationProblem(ModelState);
        }

        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(
                actor.ActorId,
                actor.TenantId,
                actor.UserId,
                actor.ApiClientId,
                ExternalUserId: null),
            cancellationToken);
        var wallet = await wallets.GetSnapshotAsync(account.Id, cancellationToken);
        var entries = await usageReports.QueryApiClientUsageAsync(
            account.Id,
            clientId,
            periodFrom,
            periodTo,
            cancellationToken);

        return Ok(MapResponse(account, wallet, periodFrom, periodTo, entries));
    }

    private static UsageSummaryResponse MapResponse(
        FinancialCopilot.Billing.Accounts.CustomerAccount account,
        FinancialCopilot.Billing.Accounts.WalletSnapshot wallet,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo,
        IReadOnlyCollection<FinancialCopilot.Billing.Usage.UsageLedgerEntry> entries) =>
        new(
            account.AccountType.ToString(),
            account.BillingMode.ToString(),
            wallet.Balance,
            wallet.ReservedAmount,
            account.GetAvailableSpendingCapacity(wallet),
            wallet.UpdatedAt,
            periodFrom,
            periodTo,
            entries
                .Select(entry => new UsageEntryResponse(
                    entry.OperationCode,
                    entry.EntryType.ToString(),
                    entry.CreditsCharged,
                    entry.PricingPolicyVersion,
                    entry.OccurredAt,
                    entry.ExternalUserId,
                    entry.CompletionStatus,
                    entry.ProviderName,
                    entry.ModelName,
                    entry.PromptTokens,
                    entry.CompletionTokens,
                    entry.TotalTokens,
                    entry.EstimatedCost))
                .ToArray());
}
