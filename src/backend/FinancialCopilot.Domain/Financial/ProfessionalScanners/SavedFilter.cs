namespace FinancialCopilot.Domain.Financial.ProfessionalScanners;

public sealed record SavedFilterActor
{
    public SavedFilterActor(Guid tenantId, Guid actorId, string actorType)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));
        if (string.IsNullOrWhiteSpace(actorType)) throw new ArgumentException("Actor type is required.", nameof(actorType));
        TenantId = tenantId;
        ActorId = actorId;
        ActorType = actorType.Trim();
    }

    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public string ActorType { get; }
}

public sealed class SavedFilter
{
    private SavedFilter(Guid id, SavedFilterActor actor, string name, string filterCode, string filterVersion,
        string parametersJson, int version, Guid concurrencyToken, DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc, DateTimeOffset? removedAtUtc)
    {
        Id = id;
        Actor = actor;
        Name = ValidateName(name);
        FilterCode = ValidateToken(filterCode, nameof(filterCode), 64).ToUpperInvariant();
        FilterVersion = ValidateToken(filterVersion, nameof(filterVersion), 32);
        ParametersJson = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson;
        Version = version;
        ConcurrencyToken = concurrencyToken;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RemovedAtUtc = removedAtUtc;
    }

    public Guid Id { get; }
    public SavedFilterActor Actor { get; }
    public string Name { get; private set; }
    public string FilterCode { get; private set; }
    public string FilterVersion { get; private set; }
    public string ParametersJson { get; private set; }
    public int Version { get; private set; }
    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? RemovedAtUtc { get; private set; }
    public bool IsRemoved => RemovedAtUtc.HasValue;

    public static SavedFilter Create(SavedFilterActor actor, string name, string filterCode, string filterVersion,
        string parametersJson, DateTimeOffset now) =>
        new(Guid.NewGuid(), actor, name, filterCode, filterVersion, parametersJson, 1, Guid.NewGuid(), now, now, null);

    public static SavedFilter Rehydrate(Guid id, SavedFilterActor actor, string name, string filterCode,
        string filterVersion, string parametersJson, int version, Guid concurrencyToken,
        DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc, DateTimeOffset? removedAtUtc) =>
        new(id, actor, name, filterCode, filterVersion, parametersJson, version, concurrencyToken,
            createdAtUtc, updatedAtUtc, removedAtUtc);

    public void Update(int expectedVersion, string name, string filterCode, string filterVersion,
        string parametersJson, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        Name = ValidateName(name);
        FilterCode = ValidateToken(filterCode, nameof(filterCode), 64).ToUpperInvariant();
        FilterVersion = ValidateToken(filterVersion, nameof(filterVersion), 32);
        ParametersJson = string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson;
        RemovedAtUtc = null;
        Touch(now);
    }

    public void Remove(int expectedVersion, DateTimeOffset now)
    {
        EnsureVersion(expectedVersion);
        RemovedAtUtc = now;
        Touch(now);
    }

    private void EnsureVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
            throw new SavedFilterValidationException("Saved filter was changed by another request.");
        if (IsRemoved) throw new SavedFilterValidationException("Saved filter has been removed.");
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = now;
    }

    private static string ValidateName(string value)
    {
        value = ValidateToken(value, nameof(value), 100);
        if (value.Any(char.IsControl)) throw new SavedFilterValidationException("Saved filter name is invalid.");
        return value;
    }

    private static string ValidateToken(string value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new SavedFilterValidationException($"{field} is required.");
        value = value.Trim();
        if (value.Length > maximumLength) throw new SavedFilterValidationException($"{field} exceeds {maximumLength} characters.");
        return value;
    }
}

public sealed class SavedFilterValidationException(string message) : InvalidOperationException(message);
