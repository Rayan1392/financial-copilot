using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// CyclicalWaves-mode entity resolution over <c>cyclicalwaves."Tickers"</c> only: exact ticker, then ISIN,
/// then fuzzy candidates (never auto-selected). The provider publishes no company names or industries,
/// so the display name is the ticker and every industry mention is NotFound.
/// </summary>
public sealed class CyclicalWavesV2CanonicalEntityResolver(
    FinancialIngestionDbContext dbContext,
    IOptions<CanonicalEntityResolutionOptions> options) : ICanonicalQueryEntityResolver, ICanonicalQueryIndustryResolver
{
    public const string ExternalIdPrefix = "cw2:";

    public Task<IndustryResolutionResult> ResolveIndustryMentionAsync(string? mention, CancellationToken cancellationToken = default) =>
        Task.FromResult<IndustryResolutionResult>(string.IsNullOrWhiteSpace(mention)
            ? new IndustryResolutionResult.Missing("Industry")
            : new IndustryResolutionResult.NotFound(NormalizeIdentity(mention)));

    public async Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeIdentity(mention);
        if (string.IsNullOrWhiteSpace(normalized) || QueryNormalization.IsPresentationWord(normalized))
            return new EntityResolutionResult.Missing("CompanyOrSymbol");

        var tickers = await LoadTickersAsync(cancellationToken);
        var kind = "exact_ticker";
        var exact = tickers.Where(row => NormalizeIdentity(row.Ticker) == normalized).ToArray();
        if (exact.Length == 0)
        {
            kind = "exact_isin";
            exact = tickers.Where(row => row.Enticker is not null &&
                                         string.Equals(NormalizeIdentity(row.Enticker), normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (exact.Length == 1)
            return Resolved(exact[0], kind);
        if (exact.Length > 1)
            return new EntityResolutionResult.Ambiguous(exact
                .OrderBy(row => row.Ticker, StringComparer.Ordinal)
                .Take(Math.Clamp(options.Value.MaxCandidates, 1, 10))
                .Select(row => new EntityResolutionCandidate(ToEntity(row, kind), 1m, kind))
                .ToArray());

        var fuzzy = tickers
            .Select(row => new { Row = row, Score = Similarity(normalized, NormalizeIdentity(row.Ticker)) })
            .Where(item => item.Score >= options.Value.FuzzyCandidateThreshold)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Row.Ticker, StringComparer.Ordinal)
            .Take(Math.Clamp(options.Value.MaxCandidates, 1, 10))
            .ToArray();
        return fuzzy.Length > 0
            ? new EntityResolutionResult.Ambiguous(fuzzy
                .Select(item => new EntityResolutionCandidate(ToEntity(item.Row, "fuzzy_candidate"), item.Score, "fuzzy_candidate"))
                .ToArray())
            : new EntityResolutionResult.NotFound(normalized);
    }

    public async Task<EntityResolutionResult> ResolveFromInterpretationAsync(
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default)
    {
        var mentions = CompanyMentions(interpretation, QueryNormalization.IsPresentationWord);
        EntityResolutionResult.Ambiguous? bestAmbiguity = null;
        foreach (var text in mentions.OrderByDescending(mention => mention.Length).Select(mention => mention.Text))
        {
            var result = await ResolveMentionAsync(text, cancellationToken);
            if (result is EntityResolutionResult.Resolved)
                return result;
            bestAmbiguity ??= result as EntityResolutionResult.Ambiguous;
        }

        if (bestAmbiguity is not null)
            return bestAmbiguity;

        return interpretation.EntityMentions.Count == 0
            ? new EntityResolutionResult.Missing("CompanyOrSymbol")
            : new EntityResolutionResult.NotFound(NormalizeIdentity(interpretation.EntityMentions[0].Text));
    }

    public async Task<EntityResolutionResult> ResolveExactTickerFromTextAsync(
        string? text,
        CancellationToken cancellationToken = default)
    {
        var tokens = QueryNormalization.Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return new EntityResolutionResult.Missing("CompanyOrSymbol");

        var tickers = await LoadTickersAsync(cancellationToken);
        foreach (var token in tokens)
        {
            var normalized = NormalizeIdentity(token);
            var exact = tickers.Where(row => NormalizeIdentity(row.Ticker) == normalized).ToArray();
            if (exact.Length == 1)
                return Resolved(exact[0], "exact_ticker");
        }

        return new EntityResolutionResult.Missing("CompanyOrSymbol");
    }

    public async Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default)
    {
        var resolved = new List<(int Position, EntityResolutionResult.Resolved Result)>();
        foreach (var mention in CompanyMentions(interpretation, QueryNormalization.IsEntityDistractor))
        {
            if (await ResolveMentionAsync(mention.Text, cancellationToken) is EntityResolutionResult.Resolved match)
                resolved.Add((mention.Start, match));
        }

        return resolved
            .OrderBy(item => item.Position)
            .GroupBy(item => item.Result.Entity.CanonicalId)
            .Select(group => group.First().Result)
            .Take(10)
            .ToArray();
    }

    public static string ExternalCompanyId(Guid tickerId) => ExternalIdPrefix + tickerId.ToString("D");

    private static EntityMention[] CompanyMentions(QueryInterpretation interpretation, Func<string, bool> isDistractor) =>
        interpretation.EntityMentions
            .Where(mention => !isDistractor(mention.Text) &&
                              !string.Equals(mention.EntityType, "product", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(mention.Scope, "product", StringComparison.OrdinalIgnoreCase))
            .OrderBy(mention => mention.Start)
            .ToArray();

    private async Task<CyclicalWavesV2TickerRow[]> LoadTickersAsync(CancellationToken cancellationToken) =>
        await dbContext.CyclicalWavesV2Tickers.AsNoTracking().Where(row => row.IsActive).ToArrayAsync(cancellationToken);

    private static EntityResolutionResult.Resolved Resolved(CyclicalWavesV2TickerRow row, string kind) =>
        new(ToEntity(row, kind), new EntityResolutionEvidence(kind, 1m));

    private static CanonicalQueryEntity ToEntity(CyclicalWavesV2TickerRow row, string provenance) =>
        new(row.Id, row.Ticker, row.Ticker, "Company", provenance, ExternalCompanyId(row.Id));

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
}
