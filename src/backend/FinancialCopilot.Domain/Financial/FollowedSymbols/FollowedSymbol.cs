namespace FinancialCopilot.Domain.Financial.FollowedSymbols;

public sealed record FollowedSymbolActor
{
    public FollowedSymbolActor(Guid tenantId, Guid actorId, string actorType)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (actorId == Guid.Empty) throw new ArgumentException("Actor id is required.", nameof(actorId));

        TenantId = tenantId;
        ActorId = actorId;
        ActorType = NormalizeActorType(actorType);
    }

    public Guid TenantId { get; }

    public Guid ActorId { get; }

    public string ActorType { get; }

    private static string NormalizeActorType(string actorType)
    {
        if (string.IsNullOrWhiteSpace(actorType))
        {
            throw new ArgumentException("Actor type is required.", nameof(actorType));
        }

        return actorType.Trim();
    }
}

public sealed record CanonicalFollowedCompany
{
    public CanonicalFollowedCompany(
        string externalCompanyId,
        string symbol,
        string companyName,
        string? companyNameEnglish)
    {
        ExternalCompanyId = NormalizeRequired(externalCompanyId, nameof(externalCompanyId), 64);
        Symbol = NormalizeRequired(symbol, nameof(symbol), 64);
        CompanyName = NormalizeRequired(companyName, nameof(companyName), 512);
        CompanyNameEnglish = NormalizeOptional(companyNameEnglish, 512);
    }

    public string ExternalCompanyId { get; }

    public string Symbol { get; }

    public string CompanyName { get; }

    public string? CompanyNameEnglish { get; }

    private static string NormalizeRequired(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} must not exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentException($"Value must not exceed {maxLength} characters.");
    }
}

public sealed class FollowedSymbol
{
    private FollowedSymbol(
        Guid id,
        FollowedSymbolActor actor,
        CanonicalFollowedCompany company,
        string? source,
        DateTimeOffset followedAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Followed symbol id is required.", nameof(id));

        Id = id;
        Actor = actor;
        ExternalCompanyId = company.ExternalCompanyId;
        Symbol = company.Symbol;
        CompanyName = company.CompanyName;
        CompanyNameEnglish = company.CompanyNameEnglish;
        Source = NormalizeSource(source);
        FollowedAtUtc = followedAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public FollowedSymbolActor Actor { get; }

    public string ExternalCompanyId { get; private set; }

    public string Symbol { get; private set; }

    public string CompanyName { get; private set; }

    public string? CompanyNameEnglish { get; private set; }

    public string? Source { get; private set; }

    public DateTimeOffset FollowedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static FollowedSymbol Follow(
        FollowedSymbolActor actor,
        CanonicalFollowedCompany company,
        DateTimeOffset followedAtUtc,
        string? source = null) =>
        new(Guid.NewGuid(), actor, company, source, followedAtUtc, followedAtUtc);

    public static FollowedSymbol Rehydrate(
        Guid id,
        FollowedSymbolActor actor,
        CanonicalFollowedCompany company,
        string? source,
        DateTimeOffset followedAtUtc,
        DateTimeOffset updatedAtUtc) =>
        new(id, actor, company, source, followedAtUtc, updatedAtUtc);

    public void RefreshCompanySnapshot(CanonicalFollowedCompany company, DateTimeOffset updatedAtUtc)
    {
        if (!string.Equals(ExternalCompanyId, company.ExternalCompanyId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A followed-symbol snapshot cannot switch company identity.");
        }

        Symbol = company.Symbol;
        CompanyName = company.CompanyName;
        CompanyNameEnglish = company.CompanyNameEnglish;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string? NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var normalized = source.Trim();
        return normalized.Length <= 64
            ? normalized
            : throw new ArgumentException("Followed-symbol source must not exceed 64 characters.", nameof(source));
    }
}
