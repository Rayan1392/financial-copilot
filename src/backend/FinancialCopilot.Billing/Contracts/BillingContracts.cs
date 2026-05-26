using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.Billing.Contracts;

public sealed record BillableActorContext(
    Guid ActorId,
    Guid TenantId,
    Guid? UserId,
    Guid? ApiClientId,
    string? ExternalUserId);

public interface IBillableAccountResolver
{
    Task<CustomerAccount> ResolveAsync(
        BillableActorContext actor,
        CancellationToken cancellationToken);
}

public interface IWalletService
{
    Task<WalletSnapshot> GetSnapshotAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken);
}

public interface ICustomerAccountRepository
{
    Task<CustomerAccount?> FindAsync(Guid customerAccountId, CancellationToken cancellationToken);

    Task<CustomerAccount?> FindOrganizationByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<CustomerAccount?> FindIndividualByUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken);
}

public interface IWalletProjectionRepository : IWalletService
{
    Task SaveAsync(WalletSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IUsageLedgerRepository
{
    Task<UsageLedgerEntry?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task AppendAsync(UsageLedgerEntry entry, CancellationToken cancellationToken);
}

public interface IFinancialTransactionRepository
{
    Task AppendAsync(FinancialTransaction transaction, CancellationToken cancellationToken);
}

public interface IUsageReservationRepository
{
    Task<UsageReservation?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task SaveAsync(UsageReservation reservation, CancellationToken cancellationToken);
}

public interface ICreditReservationService
{
    Task<UsageReservation> ReserveAsync(
        CustomerAccount account,
        WalletSnapshot wallet,
        string operationCode,
        decimal maximumCredits,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task CommitAsync(
        UsageReservation reservation,
        UsageChargeResult actualCharge,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        UsageReservation reservation,
        string reason,
        CancellationToken cancellationToken);
}

public interface IPricingPolicyProvider
{
    PricingPolicy GetPolicy(string policyVersion);
}

public interface IUsageChargeCalculator
{
    UsageChargeResult Calculate(UsageChargeRequest request);
}

public interface IUsageAccountingService
{
    Task AppendAsync(UsageLedgerEntry entry, CancellationToken cancellationToken);
}

public interface IEntitlementService
{
    Task ValidateCanExecuteAsync(
        CustomerAccount account,
        string operationCode,
        CancellationToken cancellationToken);
}

public interface IWalletProjectionBuilder
{
    WalletSnapshot Rebuild(
        Guid customerAccountId,
        decimal openingBalance,
        decimal reservedAmount,
        IReadOnlyCollection<UsageLedgerEntry> usageEntries,
        DateTimeOffset asOf);
}

public interface ICreditLinePolicyService
{
    bool CanReserve(
        CustomerAccount account,
        WalletSnapshot wallet,
        decimal requestedCredits);
}

public interface IInvoiceService
{
    Task<InvoiceAccount> GetInvoiceAccountAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken);
}

public interface IPartnerAccountService
{
    Task<CustomerAccount> GetOrganizationAccountAsync(
        Guid tenantId,
        CancellationToken cancellationToken);
}

public interface IApiUsageReportService
{
    Task<IReadOnlyCollection<UsageLedgerEntry>> QueryUsageAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public interface ISubscriptionService
{
    Task<SubscriptionPlan> GetActivePlanAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken);
}

public interface ITopUpService
{
    Task RequestTopUpAsync(
        Guid customerAccountId,
        decimal amount,
        CancellationToken cancellationToken);
}

public interface IPaymentGatewayService
{
    Task<string> CreatePaymentRequestAsync(
        Guid customerAccountId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public interface IPaymentReconciliationService
{
    Task ReconcileAsync(string callbackId, CancellationToken cancellationToken);
}
