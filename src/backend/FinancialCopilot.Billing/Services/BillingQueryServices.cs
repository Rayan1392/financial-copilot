using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.Billing.Services;

public sealed class PartnerAccountService(ICustomerAccountRepository accounts) : IPartnerAccountService
{
    public async Task<CustomerAccount> GetOrganizationAccountAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        await accounts.FindOrganizationByTenantAsync(tenantId, cancellationToken) ??
        throw new KeyNotFoundException("Organization billing account is not configured.");
}

public sealed class BillingAdministrationService(
    ICustomerAccountRepository accounts) : IBillingAdministrationService
{
    public async Task<CustomerAccount> GetTenantAccountAsync(
        Guid tenantId,
        Guid customerAccountId,
        CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(customerAccountId, cancellationToken);

        if (account is null || account.TenantId != tenantId)
        {
            throw new KeyNotFoundException("Billing account is not configured in this tenant.");
        }

        return account;
    }
}

public sealed class InvoiceService(IInvoiceAccountRepository accounts) : IInvoiceService
{
    public async Task<InvoiceAccount> GetInvoiceAccountAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken) =>
        await accounts.FindAsync(customerAccountId, cancellationToken) ??
        throw new KeyNotFoundException("Invoice account is not configured.");
}

public sealed class ApiUsageReportService(IUsageLedgerRepository ledger) : IApiUsageReportService
{
    public Task<IReadOnlyCollection<UsageLedgerEntry>> QueryUsageAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ArgumentException("Usage report end must be after its start.", nameof(to));
        }

        return ledger.QueryAsync(customerAccountId, from, to, cancellationToken);
    }

    public Task<IReadOnlyCollection<UsageLedgerEntry>> QueryApiClientUsageAsync(
        Guid customerAccountId,
        Guid apiClientId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (apiClientId == Guid.Empty)
        {
            throw new ArgumentException("API client id is required.", nameof(apiClientId));
        }

        if (to < from)
        {
            throw new ArgumentException("Usage report end must be after its start.", nameof(to));
        }

        return ledger.QueryForApiClientAsync(
            customerAccountId,
            apiClientId,
            from,
            to,
            cancellationToken);
    }
}

public sealed class SubscriptionService(ISubscriptionPlanRepository plans) : ISubscriptionService
{
    public async Task<SubscriptionPlan> GetActivePlanAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken) =>
        await plans.FindForCustomerAsync(customerAccountId, cancellationToken) ??
        throw new KeyNotFoundException("Active subscription plan is not configured.");
}
