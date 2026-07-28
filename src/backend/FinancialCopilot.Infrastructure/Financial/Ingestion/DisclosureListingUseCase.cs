using FinancialCopilot.Application.FinancialData.Ingestion;
using System.Diagnostics;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

internal sealed class DisclosureListingUseCase(ICompanyDisclosureFeedRepository repository)
    : IDisclosureListingUseCase
{
    public async Task<DisclosureListingResult> ExecuteAsync(
        DisclosureListingQuery query,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        DisclosureListingResult? result = null;
        try
        {
        if (query.Page < 1)
            throw new DisclosureListingValidationException("Page must be at least 1.");
        if (query.PageSize is < 1 or > 100)
            throw new DisclosureListingValidationException("PageSize must be between 1 and 100.");
        if (query.PublishedFrom > query.PublishedTo || query.ReceivedFrom > query.ReceivedTo)
            throw new DisclosureListingValidationException("The start of a date range cannot be after its end.");
        if (query.SymbolOrCompany?.Length > 128)
            throw new DisclosureListingValidationException("SymbolOrCompany cannot exceed 128 characters.");
        if (query.ProviderNames?.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 64) == true)
            throw new DisclosureListingValidationException("Each provider name must contain 1 to 64 characters.");

        var normalizedTypes = query.Types?.Distinct().ToArray();
        var providers = query.ProviderNames?.Select(name => name.Trim())
            .Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var page = await repository.QueryAsync(new CompanyDisclosureFeedQuery(
            normalizedTypes,
            string.IsNullOrWhiteSpace(query.SymbolOrCompany) ? null : query.SymbolOrCompany.Trim(),
            providers,
            query.PublishedFrom,
            query.PublishedTo,
            query.ReceivedFrom?.ToUniversalTime(),
            query.ReceivedTo?.ToUniversalTime(),
            query.ConsolidationScope,
            query.Page,
            query.PageSize), cancellationToken);
        var types = normalizedTypes is { Length: > 0 }
            ? normalizedTypes
            : Enum.GetValues<CompanyDisclosureType>();
        var totalPages = page.TotalCount == 0 ? 0 : (int)Math.Ceiling(page.TotalCount / (double)page.PageSize);
        result = new DisclosureListingResult(
            page.Items,
            new DisclosureListingAppliedFilters(types, string.IsNullOrWhiteSpace(query.SymbolOrCompany) ? null : query.SymbolOrCompany.Trim(),
                providers ?? [], query.PublishedFrom, query.PublishedTo, query.ReceivedFrom, query.ReceivedTo, query.ConsolidationScope),
            page.Page, page.PageSize, page.Page > 1, page.Page < totalPages, page.TotalCount, totalPages,
            page.AsOf, page.CoverageStatus, "PersistedNormalizedData");
        return result;
        }
        finally
        {
            DisclosureListingTelemetry.Record(query, result, stopwatch.Elapsed.TotalMilliseconds, result is null ? "failed" : "completed");
        }
    }
}
