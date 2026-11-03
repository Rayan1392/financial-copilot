namespace FinancialCopilot.Application.AI.Orchestration;

public sealed record CanonicalQueryProduct(
    Guid CompanyId,
    string ExternalCompanyId,
    string ProductKey,
    string DisplayTitle,
    string? Unit,
    string? ProviderProductCode,
    long? ProviderProductId,
    string IdentityProvenance);

public sealed record ProductResolutionEvidence(
    string MatchKind,
    decimal Confidence,
    QueryValueProvenance Provenance = QueryValueProvenance.UserExplicit);

public sealed record ProductResolutionCandidate(
    CanonicalQueryProduct Product,
    decimal Confidence,
    string MatchKind);

public abstract record ProductResolutionResult
{
    public sealed record Resolved(CanonicalQueryProduct Product, ProductResolutionEvidence Evidence) : ProductResolutionResult;
    public sealed record Ambiguous(IReadOnlyList<ProductResolutionCandidate> Candidates) : ProductResolutionResult;
    public sealed record NotFound(string NormalizedMention) : ProductResolutionResult;
    public sealed record Missing(string EntityType) : ProductResolutionResult;
}

public interface ICanonicalQueryProductResolver
{
    Task<ProductResolutionResult> ResolveAsync(
        string? companyMention,
        string? productMention,
        CancellationToken cancellationToken = default);
}
