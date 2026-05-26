using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;

namespace FinancialCopilot.Billing.Services;

public sealed class BillableAccountResolver(ICustomerAccountRepository accounts) : IBillableAccountResolver
{
    public async Task<CustomerAccount> ResolveAsync(
        BillableActorContext actor,
        CancellationToken cancellationToken)
    {
        CustomerAccount? account;

        if (actor.ApiClientId.HasValue)
        {
            account = await accounts.FindOrganizationByTenantAsync(actor.TenantId, cancellationToken);
        }
        else if (actor.UserId.HasValue)
        {
            account = await accounts.FindIndividualByUserAsync(
                actor.TenantId,
                actor.UserId.Value,
                cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("A billable actor must represent a user or an API client.");
        }

        if (account is null)
        {
            throw new InvalidOperationException("No billable customer account is configured for this actor.");
        }

        if (account.TenantId != actor.TenantId)
        {
            throw new InvalidOperationException("The resolved customer account is outside the actor tenant.");
        }

        return account;
    }
}
