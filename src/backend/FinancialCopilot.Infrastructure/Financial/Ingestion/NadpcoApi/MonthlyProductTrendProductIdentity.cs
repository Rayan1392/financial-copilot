using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>Shared Feature 136 product identity and title resolution semantics.</summary>
internal static class MonthlyProductTrendProductIdentity
{
    internal static MonthlyProductTrendCandidate[] BuildCandidates(
        string companyId,
        IReadOnlyCollection<ProductSalesObservation> rows) => rows
        .Select(row => ToCandidate(companyId, row))
        .GroupBy(candidate => candidate.ProductKey, StringComparer.Ordinal)
        .Select(group => group
            .OrderBy(candidate => candidate.DisplayTitle, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Unit, StringComparer.Ordinal)
            .First())
        .OrderBy(candidate => candidate.DisplayTitle, StringComparer.Ordinal)
        .ThenBy(candidate => candidate.ProductKey, StringComparer.Ordinal)
        .ToArray();

    internal static MonthlyProductTrendCandidate[] ResolveMatches(
        IReadOnlyCollection<MonthlyProductTrendCandidate> candidates,
        string requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return [];

        var providerIdentityMatches = candidates
            .Where(candidate =>
                string.Equals(MonthlyProductTrendCalculator.NormalizeProductText(candidate.ProviderProductCode), requested, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.ProviderProductId is > 0 ? candidate.ProviderProductId.Value.ToString() : null, requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (providerIdentityMatches.Length > 0) return providerIdentityMatches;

        var exactTitleMatches = candidates
            .Where(candidate => string.Equals(
                MonthlyProductTrendCalculator.NormalizeProductText(candidate.DisplayTitle), requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactTitleMatches.Length > 0) return exactTitleMatches;

        return candidates
            .Where(candidate => MonthlyProductTrendCalculator.NormalizeProductText(candidate.DisplayTitle)
                .Contains(requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal static string ProductKey(string companyId, ProductSalesObservation row) =>
        row.ProductKey ?? MonthlyProductTrendCalculator.ProductKey(companyId, row);

    internal static MonthlyProductTrendCandidate ToCandidate(string companyId, ProductSalesObservation row) =>
        new(row.Title?.Trim() is { Length: > 0 } title ? title : "بدون عنوان",
            row.Unit, ProductKey(companyId, row), row.ProviderProductCode, row.ProviderProductId);
}
