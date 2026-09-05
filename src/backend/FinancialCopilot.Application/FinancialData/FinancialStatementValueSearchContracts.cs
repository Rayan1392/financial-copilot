using FinancialCopilot.Domain.Financial.Entities;

namespace FinancialCopilot.Application.FinancialData;

public interface IFinancialStatementValueSearchService
{
    Task<FinancialStatementValueSearchResult> SearchAsync(
        FinancialStatementValueSearchRequest request,
        CancellationToken cancellationToken = default);
}

public interface IFinancialStatementValueSearchProvider
{
    string ProviderName { get; }
}

public sealed record FinancialStatementValueSearchRequest(
    string ProviderName,
    FinancialStatementType StatementType,
    IReadOnlyCollection<FinancialStatementValueClue> Clues);

public sealed record FinancialStatementValueClue(
    decimal Value,
    string? MetricCode = null,
    string? SourceTitle = null,
    string? GovernedAlias = null);

public enum FinancialStatementValueSearchOutcome
{
    NoMatch,
    MatchesFound
}

public enum FinancialStatementCompanyResolutionStatus
{
    LocalCompanyId,
    ProviderExternalMapping,
    Unresolved
}

public sealed record FinancialStatementValueSearchResult(
    FinancialStatementValueSearchOutcome Outcome,
    IReadOnlyCollection<FinancialStatementValueSearchMatch> Matches);

public sealed record FinancialStatementValueSearchMatch(
    string? Symbol,
    string? CompanyName,
    FinancialStatementCompanyResolutionStatus ResolutionStatus,
    FinancialStatementType StatementType,
    string PeriodType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? PublishedAt,
    DateTimeOffset LastSynchronizedAt,
    string ProviderName,
    string ExternalCompanyId,
    string ExternalStatementId,
    IReadOnlyCollection<FinancialStatementValueEvidence> Items);

public sealed record FinancialStatementValueEvidence(
    FinancialStatementValueClue RequestedClue,
    decimal Value,
    string? MetricCode,
    string? SourceTitle,
    Guid LineItemId,
    Guid? SourceItemCatalogId,
    IReadOnlyCollection<Guid> DuplicateLineItemIds);
