using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

public sealed class CanonicalQueryEntityResolver(
    FinancialIngestionDbContext dbContext,
    IOptions<CanonicalEntityResolutionOptions> options) : ICanonicalQueryEntityResolver
{
    public async Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeIdentity(mention);
        if (string.IsNullOrWhiteSpace(normalized))
            return new EntityResolutionResult.Missing("CompanyOrSymbol");
        if (QueryNormalization.IsPresentationWord(normalized))
            return new EntityResolutionResult.Missing("CompanyOrSymbol");

        var companies = await dbContext.Companies.AsNoTracking().ToArrayAsync(cancellationToken);
        var exactTicker = Match(companies, normalized, row => row.Ticker, "exact_ticker");
        var exactCompanyName = exactTicker.Length == 0
            ? Match(companies, normalized, row => row.Name, "exact_company_name")
            : [];
        var approvedAlias = exactTicker.Length == 0 && exactCompanyName.Length == 0
            ? Match(companies, normalized, row => row.TseSymbol, "approved_alias")
                .Concat(Match(companies, normalized, row => row.CompanySymbol, "approved_alias"))
                .Concat(Match(companies, normalized, row => row.EnTicker, "approved_alias"))
                .Concat(Match(companies, normalized, row => row.CompanySymbolEnglish, "approved_alias"))
                .Concat(Match(companies, normalized, row => row.CompanySymbolPinglish, "approved_alias"))
                .GroupBy(item => item.Row.Id)
                .Select(group => group.First())
                .ToArray()
            : [];
        var exact = exactTicker.Length > 0 ? exactTicker
            : exactCompanyName.Length > 0 ? exactCompanyName
            : approvedAlias;

        if (exact.Length == 1)
            return ToResolved(exact[0]);
        if (exact.Length > 1)
            return ToAmbiguous(exact, options.Value.MaxCandidates);

        var fuzzy = companies
            .Select(row => new { Row = row, Score = BestSimilarity(normalized, CandidateValues(row)) })
            .Where(item => item.Score >= options.Value.FuzzyCandidateThreshold)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => DisplaySymbol(item.Row), StringComparer.Ordinal)
            .Take(Math.Clamp(options.Value.MaxCandidates, 1, 10))
            .ToArray();
        return fuzzy.Length > 0
            ? new EntityResolutionResult.Ambiguous(fuzzy.Select(item => new EntityResolutionCandidate(
                ToEntity(item.Row, "fuzzy_candidate"), item.Score, "fuzzy_candidate")).ToArray())
            : new EntityResolutionResult.NotFound(normalized);
    }

    public async Task<EntityResolutionResult> ResolveFromInterpretationAsync(
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default)
    {
        foreach (var mention in interpretation.EntityMentions
                     .Where(mention => !QueryNormalization.IsPresentationWord(mention.Text))
                     .OrderByDescending(mention => mention.Length))
        {
            var result = await ResolveMentionAsync(mention.Text, cancellationToken);
            if (result is EntityResolutionResult.Resolved or EntityResolutionResult.Ambiguous)
                return result;
        }

        return interpretation.EntityMentions.Count == 0
            ? new EntityResolutionResult.Missing("CompanyOrSymbol")
            : new EntityResolutionResult.NotFound(NormalizeIdentity(interpretation.EntityMentions[0].Text));
    }

    private static MatchResult[] Match(
        IEnumerable<NormalizedCompanyRow> rows,
        string normalized,
        Func<NormalizedCompanyRow, string?> selector,
        string kind) =>
        rows.Where(row => selector(row) is { } value &&
                          string.Equals(NormalizeIdentity(value), normalized, StringComparison.OrdinalIgnoreCase))
            .Select(row => new MatchResult(row, kind))
            .ToArray();

    private static EntityResolutionResult.Resolved ToResolved(MatchResult match) =>
        new(ToEntity(match.Row, match.Kind), new EntityResolutionEvidence(match.Kind, 1m));

    private static EntityResolutionResult.Ambiguous ToAmbiguous(IEnumerable<MatchResult> matches, int limit) =>
        new(matches.OrderBy(item => DisplaySymbol(item.Row), StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 10))
            .Select(item => new EntityResolutionCandidate(ToEntity(item.Row, item.Kind), 1m, item.Kind))
            .ToArray());

    private static CanonicalQueryEntity ToEntity(NormalizedCompanyRow row, string provenance) =>
        new(row.Id, DisplaySymbol(row), row.Name, "Company", provenance);

    private static string DisplaySymbol(NormalizedCompanyRow row) =>
        FirstNonBlank(row.Ticker, row.TseSymbol, row.CompanySymbol, row.EnTicker, row.ExternalCompanyId);

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";

    private static IEnumerable<string?> CandidateValues(NormalizedCompanyRow row) =>
    [
        row.Ticker, row.Name, row.NameEnglish, row.TseSymbol, row.CompanySymbol,
        row.EnTicker, row.CompanySymbolEnglish, row.CompanySymbolPinglish
    ];

    private static decimal BestSimilarity(string mention, IEnumerable<string?> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Similarity(mention, NormalizeIdentity(value)))
            .DefaultIfEmpty(0m)
            .Max();

    private static decimal Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0m;
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return 1m;
        var distance = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++) distance[i, 0] = i;
        for (var j = 0; j <= right.Length; j++) distance[0, j] = j;
        for (var i = 1; i <= left.Length; i++)
        for (var j = 1; j <= right.Length; j++)
            distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                distance[i - 1, j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
        return 1m - (decimal)distance[left.Length, right.Length] / Math.Max(left.Length, right.Length);
    }

    private static string NormalizeIdentity(string? value) =>
        QueryNormalization.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    private sealed record MatchResult(NormalizedCompanyRow Row, string Kind);
}
