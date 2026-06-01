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

    Task<IReadOnlyCollection<UsageLedgerEntry>> QueryAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UsageLedgerEntry>> QueryForApiClientAsync(
        Guid customerAccountId,
        Guid apiClientId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public interface IFinancialTransactionRepository
{
    Task<FinancialTransaction?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task AppendAsync(FinancialTransaction transaction, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FinancialTransaction>> QueryAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public interface IFinancialAccountingService
{
    Task RecordAsync(FinancialTransaction transaction, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<FinancialTransaction>> QueryAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record BillingOutboxMessage(
    Guid Id,
    string AggregateType,
    Guid AggregateId,
    string EventType,
    string IdempotencyKey,
    string Payload,
    DateTimeOffset OccurredAt,
    int AttemptCount);

public interface IBillingOutboxDispatcher
{
    Task DispatchAsync(BillingOutboxMessage message, CancellationToken cancellationToken);
}

public interface IBillingOutboxProcessor
{
    Task<int> ProcessPendingAsync(int maximumCount, CancellationToken cancellationToken);
}

public interface IUsageReservationRepository
{
    Task<UsageReservation?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UsageReservation>> FindExpiredReservedAsync(
        DateTimeOffset asOf,
        int maximumCount,
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

    Task<int> ExpireAbandonedAsync(
        int maximumCount,
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

public sealed record CreditAdjustmentCommand(
    Guid CustomerAccountId,
    Guid ActorId,
    Guid TenantId,
    decimal Credits,
    string Reason,
    string IdempotencyKey);

public sealed record CreditAdjustmentResult(
    UsageLedgerEntry LedgerEntry,
    WalletSnapshot Wallet,
    bool AlreadyApplied);

public interface ICreditAdjustmentService
{
    Task<CreditAdjustmentResult> ApplyAsync(
        CreditAdjustmentCommand command,
        CancellationToken cancellationToken);
}

public sealed record UsageRefundCommand(
    Guid CustomerAccountId,
    Guid ActorId,
    Guid TenantId,
    string OriginalChargeIdempotencyKey,
    decimal Credits,
    string Reason,
    string IdempotencyKey);

public sealed record UsageRefundResult(
    UsageLedgerEntry LedgerEntry,
    WalletSnapshot Wallet,
    bool AlreadyApplied);

public interface IUsageRefundService
{
    Task<UsageRefundResult> RefundAsync(
        UsageRefundCommand command,
        CancellationToken cancellationToken);
}

public sealed record UsageCommitCommand(
    Guid CustomerAccountId,
    Guid ActorId,
    Guid TenantId,
    Guid? ApiClientId,
    string? ExternalUserId,
    string ReservationIdempotencyKey,
    string LedgerIdempotencyKey,
    UsageChargeResult ActualCharge,
    string CompletionStatus = "Completed");

public sealed record UsageReleaseCommand(
    Guid CustomerAccountId,
    Guid TenantId,
    string ReservationIdempotencyKey,
    string Reason);

public sealed record UsageFinalizationResult(
    UsageReservation Reservation,
    WalletSnapshot Wallet,
    UsageLedgerEntry? LedgerEntry,
    bool AlreadyFinalized);

public interface IUsageFinalizationService
{
    Task<UsageFinalizationResult> CommitAsync(
        UsageCommitCommand command,
        CancellationToken cancellationToken);

    Task<UsageFinalizationResult> ReleaseAsync(
        UsageReleaseCommand command,
        CancellationToken cancellationToken);
}

public interface IEntitlementService
{
    Task ValidateCanExecuteAsync(
        CustomerAccount account,
        string operationCode,
        CancellationToken cancellationToken);
}

public interface IPlanCapabilityService
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

public sealed record CreditLineReservationAssessment(
    bool Approved,
    decimal AvailableSpendingCapacity,
    decimal RemainingSpendingCapacity,
    decimal CreditLineUsedAfterReservation,
    bool WarningThresholdReached);

public interface ICreditLinePolicyService
{
    CreditLineReservationAssessment AssessReservation(
        CustomerAccount account,
        WalletSnapshot wallet,
        decimal requestedCredits);

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

public interface IInvoiceAccountRepository
{
    Task<InvoiceAccount?> FindAsync(Guid customerAccountId, CancellationToken cancellationToken);
}

public interface ISubscriptionPlanRepository
{
    Task<SubscriptionPlan?> FindForCustomerAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken);
}

public interface IPartnerAccountService
{
    Task<CustomerAccount> GetOrganizationAccountAsync(
        Guid tenantId,
        CancellationToken cancellationToken);
}

public interface IBillingAdministrationService
{
    Task<CustomerAccount> GetTenantAccountAsync(
        Guid tenantId,
        Guid customerAccountId,
        CancellationToken cancellationToken);
}

public interface IApiUsageReportService
{
    Task<IReadOnlyCollection<UsageLedgerEntry>> QueryUsageAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UsageLedgerEntry>> QueryApiClientUsageAsync(
        Guid customerAccountId,
        Guid apiClientId,
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
