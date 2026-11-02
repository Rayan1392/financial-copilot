using FinancialCopilot.Billing.Accounts;

namespace FinancialCopilot.Billing.Usage;

public sealed class UsageReservation
{
    public UsageReservation(
        Guid id,
        Guid customerAccountId,
        string idempotencyKey,
        string operationCode,
        decimal reservedCredits,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CreditQuotaMode quotaMode = CreditQuotaMode.Finite,
        Guid? sourceSubscriptionId = null,
        DateTimeOffset? sourceSubscriptionExpiresAtUtc = null)
    {
        if (id == Guid.Empty || customerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Reservation and customer account ids are required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(operationCode))
        {
            throw new ArgumentException("Idempotency key and operation code are required.");
        }

        if (quotaMode == CreditQuotaMode.Finite && reservedCredits <= 0 ||
            quotaMode == CreditQuotaMode.Unlimited &&
            (reservedCredits != 0 || sourceSubscriptionId is null || sourceSubscriptionId == Guid.Empty || sourceSubscriptionExpiresAtUtc is null))
        {
            throw new ArgumentOutOfRangeException(nameof(reservedCredits));
        }

        if (!Enum.IsDefined(quotaMode)) throw new ArgumentOutOfRangeException(nameof(quotaMode));

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Reservation expiry must be after creation.", nameof(expiresAt));
        }

        Id = id;
        CustomerAccountId = customerAccountId;
        IdempotencyKey = idempotencyKey.Trim();
        OperationCode = operationCode.Trim();
        ReservedCredits = reservedCredits;
        QuotaMode = quotaMode;
        SourceSubscriptionId = sourceSubscriptionId;
        SourceSubscriptionExpiresAtUtc = sourceSubscriptionExpiresAtUtc;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = UsageReservationStatus.Reserved;
    }

    public Guid Id { get; }

    public Guid CustomerAccountId { get; }

    public string IdempotencyKey { get; }

    public string OperationCode { get; }

    public decimal ReservedCredits { get; }

    public CreditQuotaMode QuotaMode { get; }

    public Guid? SourceSubscriptionId { get; }

    public DateTimeOffset? SourceSubscriptionExpiresAtUtc { get; }

    public decimal? CommittedCredits { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public UsageReservationStatus Status { get; private set; }

    public string? FinalizationReason { get; private set; }

    public static UsageReservation Restore(
        Guid id,
        Guid customerAccountId,
        string idempotencyKey,
        string operationCode,
        decimal reservedCredits,
        decimal? committedCredits,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        UsageReservationStatus status,
        string? finalizationReason = null,
        CreditQuotaMode quotaMode = CreditQuotaMode.Finite,
        Guid? sourceSubscriptionId = null,
        DateTimeOffset? sourceSubscriptionExpiresAtUtc = null)
    {
        var reservation = new UsageReservation(
            id,
            customerAccountId,
            idempotencyKey,
            operationCode,
            reservedCredits,
            createdAt,
            expiresAt,
            quotaMode,
            sourceSubscriptionId,
            sourceSubscriptionExpiresAtUtc);

        switch (status)
        {
            case UsageReservationStatus.Reserved:
                break;
            case UsageReservationStatus.Committed:
                reservation.Commit(committedCredits ??
                    throw new ArgumentException("Committed credits are required for committed reservations."));
                break;
            case UsageReservationStatus.Released:
                reservation.Release(finalizationReason);
                break;
            case UsageReservationStatus.Expired:
                reservation.Expire(finalizationReason);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return reservation;
    }

    public void Commit(decimal actualCredits)
    {
        EnsureReserved();

        if (actualCredits < 0 || actualCredits > ReservedCredits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actualCredits),
                "Committed credits must be within the reserved capacity.");
        }

        CommittedCredits = actualCredits;
        Status = UsageReservationStatus.Committed;
    }

    public void Release(string? reason = null) => TransitionTo(UsageReservationStatus.Released, reason);

    public void Expire(string? reason = null) => TransitionTo(UsageReservationStatus.Expired, reason);

    private void TransitionTo(UsageReservationStatus status, string? reason)
    {
        EnsureReserved();
        Status = status;
        FinalizationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    private void EnsureReserved()
    {
        if (Status != UsageReservationStatus.Reserved)
        {
            throw new InvalidOperationException("A finalized reservation cannot be changed.");
        }
    }
}
