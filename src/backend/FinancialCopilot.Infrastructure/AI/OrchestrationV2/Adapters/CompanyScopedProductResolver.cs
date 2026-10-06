using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

/// <summary>
/// Resolves a product only from the monthly product rows belonging to the
/// already-resolved company. It deliberately does not perform global product
/// or fuzzy identity matching.
/// </summary>
public sealed class CompanyScopedProductResolver(
    ICompanyResolverService companyResolver,
    IMonthlyProductComparisonReadRepository repository) : ICanonicalQueryProductResolver
{
    public async Task<ProductResolutionResult> ResolveAsync(
        string? companyMention,
        string? productMention,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyMention))
            return new ProductResolutionResult.Missing("CompanyOrSymbol");
        if (string.IsNullOrWhiteSpace(productMention))
            return new ProductResolutionResult.Missing("Product");

        var company = await companyResolver.ResolveBySymbolAsync(companyMention, cancellationToken);
        if (company is null)
            return new ProductResolutionResult.NotFound(MonthlyProductTrendCalculator.NormalizeProductText(productMention));

        var catalogRepository = repository as IMonthlyProductCatalogReadRepository;
        var observations = catalogRepository is not null
            ? await catalogRepository.GetProductCatalogAsync(company.ExternalCompanyId, cancellationToken)
            : await LoadPeriodFallbackAsync(company.ExternalCompanyId, cancellationToken);
        if (observations.Count == 0)
            return new ProductResolutionResult.NotFound(MonthlyProductTrendCalculator.NormalizeProductText(productMention));

        var products = new Dictionary<string, CanonicalQueryProduct>(StringComparer.Ordinal);
        foreach (var row in observations)
        {
                var key = row.ProductKey ?? MonthlyProductTrendCalculator.ProductKey(company.ExternalCompanyId, row);
                var title = string.IsNullOrWhiteSpace(row.Title) ? "بدون عنوان" : row.Title.Trim();
                products.TryAdd(key, new CanonicalQueryProduct(
                    company.Id,
                    company.ExternalCompanyId,
                    key,
                    title,
                    row.Unit,
                    row.ProviderProductCode,
                    row.ProviderProductId,
                    "feature-129-company-scoped-product-row"));
        }

        var requested = MonthlyProductTrendCalculator.NormalizeProductText(productMention);
        if (string.IsNullOrWhiteSpace(requested))
            return new ProductResolutionResult.NotFound(requested);

        var matches = ResolveMatches(products.Values, requested);
        if (matches.Count == 0)
            return new ProductResolutionResult.NotFound(requested);
        if (matches.Count > 1)
        {
            // The production catalog can contain the same title/unit once per
            // provider row. Treat those rows as one canonical product; retain
            // ambiguity when the company truly has distinct title/unit identities.
            if (catalogRepository is not null &&
                matches.Select(match => match.Product)
                    .GroupBy(product => MonthlyProductTrendCalculator.NormalizeProductText(product.DisplayTitle), StringComparer.OrdinalIgnoreCase)
                    .Count() == 1)
            {
                var canonical = matches[0];
                return new ProductResolutionResult.Resolved(
                    canonical.Product,
                    new ProductResolutionEvidence("exact_title_canonicalized", canonical.Confidence));
            }
            if (catalogRepository is not null)
            {
                var shortestTitleLength = matches.Min(match =>
                    MonthlyProductTrendCalculator.NormalizeProductText(match.Product.DisplayTitle).Length);
                var shortest = matches
                    .Where(match => MonthlyProductTrendCalculator.NormalizeProductText(match.Product.DisplayTitle).Length == shortestTitleLength)
                    .ToArray();
                if (shortest.Length == 1)
                {
                    var canonical = shortest[0];
                    return new ProductResolutionResult.Resolved(
                        canonical.Product,
                        new ProductResolutionEvidence("shortest_partial_title_canonicalized", canonical.Confidence));
                }
            }
            return new ProductResolutionResult.Ambiguous(matches
                .Take(8)
                .Select(match => new ProductResolutionCandidate(match.Product, match.Confidence, match.MatchKind))
                .ToArray());
        }

        var selected = matches[0];
        return new ProductResolutionResult.Resolved(
            selected.Product,
            new ProductResolutionEvidence(selected.MatchKind, selected.Confidence));
    }

    private async Task<IReadOnlyList<ProductSalesObservation>> LoadPeriodFallbackAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var periods = await repository.GetAvailablePeriodsAsync(externalCompanyId, cancellationToken);
        var observations = new List<ProductSalesObservation>();
        foreach (var period in periods)
        {
            var data = await repository.GetPeriodAsync(externalCompanyId, period, cancellationToken);
            if (data is not null) observations.AddRange(data.Observations);
        }
        return observations;
    }

    private static IReadOnlyList<MatchedProduct> ResolveMatches(
        IEnumerable<CanonicalQueryProduct> products,
        string requested)
    {
        var candidates = products.ToArray();
        var providerMatches = candidates
            .Where(product =>
                string.Equals(MonthlyProductTrendCalculator.NormalizeProductText(product.ProviderProductCode), requested, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(product.ProviderProductId is > 0 ? product.ProviderProductId.Value.ToString() : null, requested, StringComparison.OrdinalIgnoreCase))
            .Select(product => new MatchedProduct(product, "provider_identity", 1.00m))
            .ToArray();
        if (providerMatches.Length > 0)
            return providerMatches;

        var exactTitleMatches = candidates
            .Where(product => string.Equals(
                MonthlyProductTrendCalculator.NormalizeProductText(product.DisplayTitle), requested, StringComparison.OrdinalIgnoreCase))
            .Select(product => new MatchedProduct(product, "exact_title", 0.98m))
            .ToArray();
        if (exactTitleMatches.Length > 0)
            return exactTitleMatches;

        return candidates
            .Where(product => MonthlyProductTrendCalculator.NormalizeProductText(product.DisplayTitle)
                .Contains(requested, StringComparison.OrdinalIgnoreCase))
            .Select(product => new MatchedProduct(product, "partial_title", 0.90m))
            .ToArray();
    }

    private sealed record MatchedProduct(CanonicalQueryProduct Product, string MatchKind, decimal Confidence);
}
