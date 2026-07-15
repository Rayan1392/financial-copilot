using FinancialCopilot.Billing.Purchases;

namespace FinancialCopilot.API.Contracts;

public sealed record BillingCatalogResponse(
    IReadOnlyCollection<BillingCatalogProductResponse> Products,
    DateTimeOffset GeneratedAtUtc);

public sealed record BillingCatalogProductResponse(
    string Code,
    string ProductType,
    string Version,
    string DisplayName,
    decimal Amount,
    string Currency,
    decimal Credits,
    string? PlanCode,
    int? DurationDays,
    string Channel);

public sealed record CreateBillingCheckoutRequest(
    string ProductCode,
    string? IdempotencyKey);

public sealed record SubmitBillingReceiptRequest(
    int ExpectedVersion,
    string AttachmentKind,
    string AttachmentReference,
    string? ProviderReference,
    string? IdempotencyKey);

public sealed record CancelBillingCheckoutRequest(
    int ExpectedVersion,
    string Reason);

public sealed record BillingCheckoutResponse(
    Guid Id,
    Guid CustomerAccountId,
    string ProductType,
    string ProductCode,
    string ProductVersion,
    string ProductDisplayName,
    decimal Amount,
    string Currency,
    string PaymentReference,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ReceiptSubmittedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    DateTimeOffset? FulfilledAtUtc,
    string? ReceiptAttachmentKind,
    string? ReceiptAttachmentReference,
    string? ReviewReason,
    int Version,
    bool AlreadyApplied,
    IReadOnlyCollection<string> NextActions);

public sealed record BillingCheckoutPageResponse(
    IReadOnlyCollection<BillingCheckoutResponse> Items,
    int Offset,
    int PageSize,
    bool HasMore);

public sealed record ReviewBillingReceiptRequest(
    int ExpectedVersion,
    bool Approved,
    string Reason,
    string? IdempotencyKey);

public sealed record PaymentCallbackRequest(
    string ProviderReference,
    string PaymentReference,
    decimal Amount,
    string Currency,
    string Signature,
    DateTimeOffset TimestampUtc,
    string Nonce);

public sealed record PaymentCallbackResponse(
    string Status,
    Guid? CheckoutId,
    bool Fulfilled,
    string Reason);

public sealed record BillingReconciliationResponse(
    int AwaitingPayment,
    int UnderReview,
    int Fulfilled,
    int Rejected,
    int Expired,
    decimal FulfilledAmount,
    decimal RefundedAmount,
    DateTimeOffset GeneratedAtUtc);
