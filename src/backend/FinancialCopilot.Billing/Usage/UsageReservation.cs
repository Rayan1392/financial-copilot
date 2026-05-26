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
        DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty || customerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Reservation and customer account ids are required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(operationCode))
        {
            throw new ArgumentException("Idempotency key and operation code are required.");
        }

        if (reservedCredits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedCredits));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("Reservation expiry must be after creation.", nameof(expiresAt));
        }

        Id = id;
        CustomerAccountId = customerAccountId;
        IdempotencyKey = idempotencyKey.Trim();
        OperationCode = operationCode.Trim();
        ReservedCredits = reservedCredits;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = UsageReservationStatus.Reserved;
    }

    public Guid Id { get; }

    public Guid CustomerAccountId { get; }

    public string IdempotencyKey { get; }

    public string OperationCode { get; }

    public decimal ReservedCredits { get; }

    public decimal? CommittedCredits { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public UsageReservationStatus Status { get; private set; }

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

    public void Release() => TransitionTo(UsageReservationStatus.Released);

    public void Expire() => TransitionTo(UsageReservationStatus.Expired);

    private void TransitionTo(UsageReservationStatus status)
    {
        EnsureReserved();
        Status = status;
    }

    private void EnsureReserved()
    {
        if (Status != UsageReservationStatus.Reserved)
        {
            throw new InvalidOperationException("A finalized reservation cannot be changed.");
        }
    }
}
