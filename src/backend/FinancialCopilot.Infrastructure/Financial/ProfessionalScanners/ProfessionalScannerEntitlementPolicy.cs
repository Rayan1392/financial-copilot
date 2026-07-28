using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using FinancialCopilot.Billing.Contracts;

namespace FinancialCopilot.Infrastructure.Financial.ProfessionalScanners;

public sealed class ProfessionalScannerEntitlementPolicy(
    IBillableAccountResolver accountResolver,
    IPlanCapabilityService planCapabilities) : IProfessionalScannerEntitlementPolicy
{
    public async Task<ProfessionalAccessMode> ValidateExecuteAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(ToBillingActor(actor), cancellationToken);
        await planCapabilities.ValidateCanExecuteAsync(account, GovernedProfessionalFilterCatalog.EntitlementCode, cancellationToken);
        var meteredLimit = await planCapabilities.GetLimitAsync(account, GovernedProfessionalFilterCatalog.EntitlementCode, cancellationToken);
        return meteredLimit.HasValue ? ProfessionalAccessMode.Metered : ProfessionalAccessMode.Unlimited;
    }

    public async Task ValidateSaveAsync(CurrentActor actor, int currentSavedCount, CancellationToken cancellationToken)
    {
        var account = await accountResolver.ResolveAsync(ToBillingActor(actor), cancellationToken);
        await planCapabilities.ValidateCanExecuteAsync(account, GovernedProfessionalFilterCatalog.EntitlementCode, cancellationToken);
        var limit = await planCapabilities.GetLimitAsync(account, GovernedProfessionalFilterCatalog.EntitlementCode, cancellationToken);
        if (limit.HasValue && currentSavedCount >= Math.Max(1, (int)limit.Value))
            throw new ProfessionalScannerValidationException($"The active plan allows at most {(int)limit.Value} saved filters.");
    }

    private static BillableActorContext ToBillingActor(CurrentActor actor) =>
        new(actor.ActorId, actor.TenantId, actor.UserId, actor.ApiClientId, ExternalUserId: null);
}
