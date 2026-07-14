using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;

namespace FinancialCopilot.Infrastructure.Financial.ConditionalTrackers;

public sealed class ConditionalTrackerEntitlementPolicy(
    IBillableAccountResolver accountResolver,
    IPlanCapabilityService planCapabilities) : IConditionalTrackerEntitlementPolicy
{
    public const string CapabilityCode = "Tracker.Rules";
    private const int DefaultRuleLimit = 5;

    public async Task ValidateCreateAsync(
        CurrentActor actor,
        int currentLiveRuleCount,
        CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(ToBillingActor(actor), cancellationToken);
        await planCapabilities.ValidateCanExecuteAsync(account, CapabilityCode, cancellationToken);
        var limit = await planCapabilities.GetLimitAsync(account, CapabilityCode, cancellationToken)
            ?? DefaultRuleLimit;
        if (currentLiveRuleCount >= limit)
            throw new AlertRuleValidationException($"The active subscription plan allows at most {limit} live tracker rules.");
    }

    public async Task<bool> CanEvaluateAsync(AlertRuleActor actor, CancellationToken cancellationToken)
    {
        try
        {
            var account = await accountResolver.ResolveAsync(
                new BillableActorContext(
                    actor.ActorId,
                    actor.TenantId,
                    IsWebUser(actor.ActorType) ? actor.ActorId : null,
                    IsWebUser(actor.ActorType) ? null : actor.ActorId,
                    ExternalUserId: null),
                cancellationToken);
            await planCapabilities.ValidateCanExecuteAsync(account, CapabilityCode, cancellationToken);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static BillableActorContext ToBillingActor(CurrentActor actor) =>
        new(actor.ActorId, actor.TenantId, actor.UserId, actor.ApiClientId, ExternalUserId: null);

    private static bool IsWebUser(string actorType) =>
        actorType.Equals(ActorType.User.ToString(), StringComparison.OrdinalIgnoreCase);
}
