namespace FinancialCopilot.Billing.Purchases;

public enum BillingPurchaseProductType
{
    CreditPack,
    Subscription
}

public enum BillingCheckoutStatus
{
    Pending,
    AwaitingPayment,
    ReceiptSubmitted,
    UnderReview,
    Approved,
    Rejected,
    Expired,
    Cancelled,
    Failed,
    RefundPending,
    Refunded,
    Fulfilled
}

public sealed class BillingPurchaseProduct
{
    public BillingPurchaseProduct(
        string code,
        BillingPurchaseProductType productType,
        string version,
        string displayName,
        decimal amount,
        string currency,
        decimal credits,
        string? planCode,
        int? durationDays,
        string channel,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Product code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Product version is required.", nameof(version));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name is required.", nameof(displayName));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Product amount must be positive.");
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("Channel is required.", nameof(channel));
        if (productType == BillingPurchaseProductType.CreditPack && credits <= 0)
            throw new ArgumentOutOfRangeException(nameof(credits), "Credit packs must grant credits.");
        if (productType == BillingPurchaseProductType.Subscription && string.IsNullOrWhiteSpace(planCode))
            throw new ArgumentException("Subscription products must reference a plan.", nameof(planCode));
        if (productType == BillingPurchaseProductType.Subscription && durationDays is null or <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationDays), "Subscription products must define duration.");

        Code = code.Trim();
        ProductType = productType;
        Version = version.Trim();
        DisplayName = displayName.Trim();
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        Credits = credits;
        PlanCode = string.IsNullOrWhiteSpace(planCode) ? null : planCode.Trim();
        DurationDays = durationDays;
        Channel = channel.Trim();
        IsActive = isActive;
    }

    public string Code { get; }
    public BillingPurchaseProductType ProductType { get; }
    public string Version { get; }
    public string DisplayName { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public decimal Credits { get; }
    public string? PlanCode { get; }
    public int? DurationDays { get; }
    public string Channel { get; }
    public bool IsActive { get; }
}

public sealed class BillingCheckoutIntent
{
    private static readonly IReadOnlyDictionary<BillingCheckoutStatus, BillingCheckoutStatus[]> AllowedTransitions =
        new Dictionary<BillingCheckoutStatus, BillingCheckoutStatus[]>
        {
            [BillingCheckoutStatus.Pending] =
            [
                BillingCheckoutStatus.AwaitingPayment,
                BillingCheckoutStatus.Cancelled,
                BillingCheckoutStatus.Expired,
                BillingCheckoutStatus.Failed
            ],
            [BillingCheckoutStatus.AwaitingPayment] =
            [
                BillingCheckoutStatus.ReceiptSubmitted,
                BillingCheckoutStatus.Cancelled,
                BillingCheckoutStatus.Expired,
                BillingCheckoutStatus.Failed
            ],
            [BillingCheckoutStatus.ReceiptSubmitted] =
            [
                BillingCheckoutStatus.UnderReview,
                BillingCheckoutStatus.Rejected,
                BillingCheckoutStatus.Cancelled,
                BillingCheckoutStatus.Expired,
                BillingCheckoutStatus.Failed
            ],
            [BillingCheckoutStatus.UnderReview] =
            [
                BillingCheckoutStatus.Approved,
                BillingCheckoutStatus.Rejected,
                BillingCheckoutStatus.Failed
            ],
            [BillingCheckoutStatus.Approved] =
            [
                BillingCheckoutStatus.Fulfilled,
                BillingCheckoutStatus.RefundPending,
                BillingCheckoutStatus.Failed
            ],
            [BillingCheckoutStatus.Fulfilled] =
            [
                BillingCheckoutStatus.RefundPending
            ],
            [BillingCheckoutStatus.RefundPending] =
            [
                BillingCheckoutStatus.Refunded,
                BillingCheckoutStatus.Failed
            ]
        };

    public BillingCheckoutIntent(
        Guid id,
        Guid tenantId,
        Guid actorId,
        Guid customerAccountId,
        BillingPurchaseProductType productType,
        string productCode,
        string productVersion,
        decimal amount,
        string currency,
        string paymentReference,
        BillingCheckoutStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        int version)
    {
        if (id == Guid.Empty) throw new ArgumentException("Checkout id is required.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));
        if (customerAccountId == Guid.Empty)
            throw new ArgumentException("Customer account id is required.", nameof(customerAccountId));
        if (string.IsNullOrWhiteSpace(productCode))
            throw new ArgumentException("Product code is required.", nameof(productCode));
        if (string.IsNullOrWhiteSpace(productVersion))
            throw new ArgumentException("Product version is required.", nameof(productVersion));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Checkout amount must be positive.");
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        if (string.IsNullOrWhiteSpace(paymentReference))
            throw new ArgumentException("Payment reference is required.", nameof(paymentReference));
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Checkout expiry must be after creation.", nameof(expiresAtUtc));

        Id = id;
        TenantId = tenantId;
        ActorId = actorId;
        CustomerAccountId = customerAccountId;
        ProductType = productType;
        ProductCode = productCode.Trim();
        ProductVersion = productVersion.Trim();
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        PaymentReference = paymentReference.Trim();
        Status = status;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Version = version;
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public Guid CustomerAccountId { get; }
    public BillingPurchaseProductType ProductType { get; }
    public string ProductCode { get; }
    public string ProductVersion { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public string PaymentReference { get; }
    public BillingCheckoutStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public int Version { get; private set; }

    public void EnsureNotExpired(DateTimeOffset now)
    {
        if (now > ExpiresAtUtc && Status is BillingCheckoutStatus.Pending or BillingCheckoutStatus.AwaitingPayment
                or BillingCheckoutStatus.ReceiptSubmitted)
        {
            TransitionTo(BillingCheckoutStatus.Expired);
        }
    }

    public void TransitionTo(BillingCheckoutStatus next)
    {
        if (Status == next)
        {
            return;
        }

        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(next))
        {
            throw new InvalidOperationException($"Checkout cannot move from {Status} to {next}.");
        }

        Status = next;
        Version++;
    }
}
