using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Domain.Financial.Radar;

namespace FinancialCopilot.Infrastructure.Financial.Radar;

public sealed class RadarEntitlementPolicy(
    IBillableAccountResolver accountResolver,
    IPlanCapabilityService planCapabilities) : IRadarEntitlementPolicy
{
    public const string CapabilityCode = "Radar.Symbols";
    private const int DefaultSymbolLimit = 5;

    public async Task ValidateManageAsync(
        CurrentActor actor,
        int followedSymbolCount,
        CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(
            new BillableActorContext(actor.ActorId, actor.TenantId, actor.UserId, actor.ApiClientId, null),
            cancellationToken);
        await ValidateAsync(account, followedSymbolCount, cancellationToken);
    }

    public async Task<bool> CanEvaluateAsync(
        RadarActor actor,
        int followedSymbolCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var isUser = actor.ActorType.Equals(ActorType.User.ToString(), StringComparison.OrdinalIgnoreCase);
            var account = await accountResolver.ResolveAsync(new BillableActorContext(
                actor.ActorId,
                actor.TenantId,
                isUser ? actor.ActorId : null,
                isUser ? null : actor.ActorId,
                null), cancellationToken);
            await ValidateAsync(account, followedSymbolCount, cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private async Task ValidateAsync(
        CustomerAccount account,
        int followedSymbolCount,
        CancellationToken cancellationToken)
    {
        await planCapabilities.ValidateCanExecuteAsync(account, CapabilityCode, cancellationToken);
        var limit = await planCapabilities.GetLimitAsync(account, CapabilityCode, cancellationToken)
            ?? DefaultSymbolLimit;
        if (followedSymbolCount > limit)
            throw new RadarValidationException(
                $"The active subscription plan allows radar evaluation for at most {limit} followed symbols.");
    }
}
