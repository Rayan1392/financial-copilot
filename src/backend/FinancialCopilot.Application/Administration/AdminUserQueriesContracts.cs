namespace FinancialCopilot.Application.Administration;

public sealed record AdminUserDirectoryQuery(
    int PageSize = 50,
    string? Cursor = null,
    string? Search = null,
    string Status = "any",
    string? AcquisitionSource = null,
    string? LinkedProvider = null,
    string? ExternalProvider = null,
    string? ExternalUserId = null,
    DateTimeOffset? RegisteredFromUtc = null,
    DateTimeOffset? RegisteredToUtc = null,
    DateTimeOffset? LastLoginFromUtc = null,
    DateTimeOffset? LastLoginToUtc = null,
    string Sort = "registeredAtUtc-desc,userId-asc");

public sealed record AdminUserDirectoryPage(
    IReadOnlyCollection<AdminUserDirectoryItem> Items,
    string? NextCursor,
    bool HasMore,
    DateTimeOffset AsOfUtc);

public sealed record AdminUserDirectoryItem(
    Guid UserId,
    string? Email,
    string? DisplayName,
    bool IsEnabled,
    bool IsLockedOut,
    string Status,
    string AcquisitionSourceCode,
    DateTimeOffset? RegisteredAtUtc,
    DateTimeOffset? LastSuccessfulLoginAtUtc,
    string? LastLoginProviderCode,
    IReadOnlyCollection<string> LinkedProviderCodes,
    AdminOptionalSection<AdminUserSubscriptionSummary> Subscription,
    AdminOptionalSection<AdminUserCreditSummary> Credits,
    Guid? CustomerAccountId = null);

public sealed record AdminOptionalSection<T>(string Status, T? Data, string? Reason = null);

public sealed record AdminUserSubscriptionSummary(
    Guid CustomerAccountId,
    string AccountType,
    string? PlanCode,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    long Revision,
    bool IsActive,
    IReadOnlyCollection<string> CapabilityCodes,
    string SourceSystem,
    DateTimeOffset AsOfUtc);

public sealed record AdminUserCreditSummary(
    decimal? SpendableCredits,
    decimal? ReservedCredits,
    decimal? ApprovedCreditLine,
    decimal ActiveSubscriptionCredits,
    DateTimeOffset? NextExpiryUtc,
    string SourceSystem,
    DateTimeOffset AsOfUtc);

public sealed record AdminProviderLinkView(
    string ProviderCode,
    string Status,
    string MaskedExternalId,
    string? DisplayMetadata,
    DateTimeOffset LinkedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? ProfileSyncedAtUtc);

public sealed record AdminCustomer360Permissions(
    bool CanReadSubscription,
    bool CanReadCredits,
    bool CanReadPurchases);

public sealed record AdminCustomer360View(
    Guid UserId,
    Guid TenantId,
    bool IsEnabled,
    bool IsLockedOut,
    string AcquisitionSourceCode,
    DateTimeOffset? RegisteredAtUtc,
    DateTimeOffset? LastSuccessfulLoginAtUtc,
    string? LastLoginProviderCode,
    bool HasNativeCredential,
    bool EmailConfirmed,
    int ActiveRefreshSessions,
    IReadOnlyCollection<Guid> TenantMemberships,
    IReadOnlyCollection<AdminProviderLinkView> LinkedProviders,
    AdminOptionalSection<AdminCustomerSubscriptionView> Subscription,
    AdminOptionalSection<AdminCustomerCreditView> Credits,
    AdminOptionalSection<AdminCustomerPurchaseSummary> Purchases,
    DateTimeOffset AsOfUtc);

public sealed record AdminCustomerSubscriptionView(
    Guid CustomerAccountId,
    string AccountType,
    string? PlanCode,
    string? PlanName,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    long Revision,
    bool IsActive,
    IReadOnlyCollection<AdminCustomerPlanCapabilityView> Capabilities,
    string? ExternalSyncStatus,
    DateTimeOffset? ExternalLastSyncedAtUtc,
    string SourceSystem,
    DateTimeOffset AsOfUtc);

public sealed record AdminCustomerPlanCapabilityView(string Code, bool IsEnabled, decimal? Limit, string PolicyVersion);

public sealed record AdminCustomerCreditView(
    decimal? SpendableCredits,
    decimal? ReservedCredits,
    decimal? ApprovedCreditLine,
    decimal ActiveSubscriptionCredits,
    DateTimeOffset? NextExpiryUtc,
    IReadOnlyCollection<AdminCreditPeriodView> ActivePeriods,
    string SourceSystem,
    DateTimeOffset AsOfUtc);

public sealed record AdminCreditPeriodView(
    Guid Id,
    string PlanCode,
    decimal GrantedCredits,
    decimal RemainingCredits,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status);

public sealed record AdminCustomerPurchaseSummary(
    int? CompletedLocalPurchaseCount,
    IReadOnlyCollection<AdminPurchaseHistoryItem> RecentLocalPurchases,
    string ExternalEntitlementsStatus,
    string SourceSystem,
    DateTimeOffset AsOfUtc);

public sealed record AdminCreditHistoryPage(
    IReadOnlyCollection<AdminCreditHistoryItem> Items,
    string? NextCursor,
    bool HasMore,
    DateTimeOffset AsOfUtc);

public sealed record AdminCreditHistoryItem(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string RecordType,
    string? EntryType,
    string? OperationCode,
    decimal Credits,
    string? AllocationSource,
    DateTimeOffset? ExpiresAtUtc,
    Guid? RelatedEntryId,
    string Status,
    Guid CustomerAccountId,
    decimal? RemainingCredits,
    Guid? GrantLedgerEntryId,
    string? AuditDescription = null);

public sealed record AdminPurchaseHistoryPage(
    IReadOnlyCollection<AdminPurchaseHistoryItem> Items,
    string? NextCursor,
    bool HasMore,
    DateTimeOffset AsOfUtc);

public sealed record AdminPurchaseHistoryItem(
    Guid CheckoutIntentId,
    DateTimeOffset CreatedAtUtc,
    string ProductType,
    string ProductCode,
    string ProductDisplayName,
    string Status,
    decimal Amount,
    string Currency,
    string Channel,
    string? ProviderName,
    string PaymentReference,
    DateTimeOffset? ReceiptSubmittedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset? FulfilledAtUtc,
    Guid? FinancialTransactionId,
    string? FinancialTransactionType,
    DateTimeOffset? FinancialTransactionOccurredAtUtc,
    string? ReconciliationState);

public interface IAdminUserQueries
{
    Task<AdminUserDirectoryPage> GetDirectoryAsync(
        Guid tenantId,
        AdminUserDirectoryQuery query,
        bool canReadSubscription,
        bool canReadCredits,
        bool canResolveBillingAccount,
        CancellationToken cancellationToken);

    Task<AdminCustomer360View> GetCustomer360Async(
        Guid tenantId,
        Guid userId,
        AdminCustomer360Permissions permissions,
        CancellationToken cancellationToken);

    Task<AdminCreditHistoryPage> GetCreditHistoryAsync(
        Guid tenantId,
        Guid userId,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken);

    Task<AdminPurchaseHistoryPage> GetPurchaseHistoryAsync(
        Guid tenantId,
        Guid userId,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken);
}
