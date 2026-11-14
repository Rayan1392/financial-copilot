using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>Preferences for versions of the same statement period.</summary>
public readonly record struct FinancialStatementVersionRank(bool IsAudited, bool IsRepresented, DateOnly? PublishedAt);

public sealed record FinancialStatementVersionPick<T>(T? Winner, bool IsConflict) where T : class;

/// <summary>
/// Orders statement revisions and detects conflicting equally ranked observations before applying stable ID ties.
/// Callers select the period and define the relevant observation signature.
/// </summary>
public static class FinancialStatementVersionPolicy
{
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> versions, Func<T, FinancialStatementVersionRank> rank) =>
        versions.OrderByDescending(item => rank(item).IsAudited)
            .ThenByDescending(item => rank(item).IsRepresented)
            .ThenByDescending(item => rank(item).PublishedAt.HasValue)
            .ThenByDescending(item => rank(item).PublishedAt)
            .ToArray();

    public static IReadOnlyList<T> TopTieGroup<T>(IReadOnlyList<T> ranked, Func<T, FinancialStatementVersionRank> rank)
    {
        if (ranked.Count == 0) return [];
        var top = rank(ranked[0]);
        return ranked.Where(item => rank(item) == top).ToArray();
    }

    public static FinancialStatementVersionPick<T> Pick<T>(
        IReadOnlyList<T> topGroup, Func<T, string> signature, Func<T, string> externalId, Func<T, Guid> id) where T : class
    {
        if (topGroup.Count == 0) return new(null, false);
        if (topGroup.Count > 1)
        {
            var first = signature(topGroup[0]);
            if (topGroup.Skip(1).Any(item => signature(item) != first)) return new(null, true);
        }
        return new(topGroup.OrderByDescending(externalId, ExternalIdComparer.Instance).ThenByDescending(id).First(), false);
    }

    private sealed class ExternalIdComparer : IComparer<string>
    {
        public static readonly ExternalIdComparer Instance = new();
        public int Compare(string? x, string? y) =>
            long.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var left) &&
            long.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var right)
                ? left.CompareTo(right)
                : string.CompareOrdinal(x, y);
    }
}
