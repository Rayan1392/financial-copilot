namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>Validated, channel-neutral input for the company disclosure listing.</summary>
public sealed record DisclosureListingQuery(
    IReadOnlyCollection<CompanyDisclosureType>? Types = null,
    string? SymbolOrCompany = null,
    IReadOnlyCollection<string>? ProviderNames = null,
    DateOnly? PublishedFrom = null,
    DateOnly? PublishedTo = null,
    DateTimeOffset? ReceivedFrom = null,
    DateTimeOffset? ReceivedTo = null,
    DisclosureConsolidationScope ConsolidationScope = DisclosureConsolidationScope.NonConsolidated,
    int Page = 1,
    int PageSize = 20,
    string Channel = "api");

public interface IDisclosureListingUseCase
{
    Task<DisclosureListingResult> ExecuteAsync(
        DisclosureListingQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class DisclosureListingValidationException(string message) : Exception(message);

public sealed record DisclosureListingAppliedFilters(
    IReadOnlyList<CompanyDisclosureType> Types,
    string? SymbolOrCompany,
    IReadOnlyList<string> ProviderNames,
    DateOnly? PublishedFrom,
    DateOnly? PublishedTo,
    DateTimeOffset? ReceivedFrom,
    DateTimeOffset? ReceivedTo,
    DisclosureConsolidationScope ConsolidationScope);

/// <summary>The sole listing result contract shared by HTTP, web, AI, and Telegram adapters.</summary>
public sealed record DisclosureListingResult(
    IReadOnlyList<CompanyDisclosureFeedItem> Items,
    DisclosureListingAppliedFilters AppliedFilters,
    int Page,
    int PageSize,
    bool HasPreviousPage,
    bool HasNextPage,
    int TotalCount,
    int TotalPages,
    DateTimeOffset AsOf,
    DisclosureCoverageStatus CoverageStatus,
    string FreshnessReasonCode);
