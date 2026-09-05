using FinancialCopilot.Application.FinancialData;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class FinancialStatementValueSearchService(
    FinancialIngestionDbContext dbContext,
    IMetricAliasResolver aliasResolver,
    IFinancialMetricRegistry metricRegistry) : IFinancialStatementValueSearchService
{
    internal const int MaximumClues = 20;
    private const int MaximumMatches = 100;

    public async Task<FinancialStatementValueSearchResult> SearchAsync(
        FinancialStatementValueSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var clues = request.Clues
            .GroupBy(c => new { c.Value, Metric = c.MetricCode?.Trim(), Title = c.SourceTitle?.Trim(), Alias = c.GovernedAlias?.Trim() })
            .Select(g => g.First())
            .ToArray();
        var identities = await ResolveCluesAsync(request, clues, cancellationToken);
        if (identities is null)
            return new(FinancialStatementValueSearchOutcome.NoMatch, []);

        var statements = await dbContext.FinancialStatements.AsNoTracking()
            .Where(s => s.ProviderName == request.ProviderName.Trim() && s.StatementType == request.StatementType.ToString())
            .ToListAsync(cancellationToken);
        var companies = await dbContext.Companies.AsNoTracking().ToListAsync(cancellationToken);
        var eligible = await dbContext.NoavaranEligibleCompanies.AsNoTracking()
            .Where(c => c.ProviderName == request.ProviderName.Trim())
            .ToListAsync(cancellationToken);

        var selected = statements
            .GroupBy(s => ResolveIdentityKey(s, companies, eligible), StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(s => s.PeriodEnd)
                .ThenByDescending(s => s.PublishedAt.HasValue)
                .ThenByDescending(s => s.PublishedAt)
                .ThenByDescending(s => s.LastSynchronizedAt)
                .ThenByDescending(s => s.ExternalStatementId, StringComparer.Ordinal)
                .ThenByDescending(s => s.Id)
                .First())
            .ToArray();
        if (selected.Length == 0)
            return new(FinancialStatementValueSearchOutcome.NoMatch, []);

        var statementIds = selected.Select(s => s.Id).ToArray();
        var lines = await dbContext.FinancialStatementLineItems.AsNoTracking()
            .Where(i => statementIds.Contains(i.FinancialStatementId) && i.Value != null)
            .ToListAsync(cancellationToken);
        var sourceItems = await dbContext.FinancialStatementSourceItems.AsNoTracking().ToListAsync(cancellationToken);
        var matches = new List<FinancialStatementValueSearchMatch>();
        foreach (var statement in selected)
        {
            var statementLines = lines.Where(i => i.FinancialStatementId == statement.Id).ToArray();
            var matchedItems = new List<FinancialStatementValueEvidence>();
            for (var clueIndex = 0; clueIndex < clues.Length; clueIndex++)
            {
                var identity = identities[clueIndex];
                var candidates = statementLines.Where(line => line.Value == clues[clueIndex].Value && MatchesIdentity(line, identity)).ToArray();
                if (candidates.Length == 0) { matchedItems.Clear(); break; }
                // Normalization and provider-code ingestion can represent the same requested
                // line with different source-item links. Canonicalize by the persisted metric
                // identity first, then prefer the richer source-linked row deterministically.
                IEnumerable<IEnumerable<NormalizedFinancialStatementLineItemRow>> groups =
                    identity.Codes.Count == 0 && identity.SourceIds.Count == 0
                        ? candidates.GroupBy(_ => true).Select(group => (IEnumerable<NormalizedFinancialStatementLineItemRow>)group)
                        : candidates.GroupBy(line => line.MetricCode, StringComparer.OrdinalIgnoreCase)
                            .Select(group => (IEnumerable<NormalizedFinancialStatementLineItemRow>)group);
                foreach (var group in groups)
                {
                    var canonical = group.OrderByDescending(i => !string.IsNullOrWhiteSpace(i.MetricCode))
                        .ThenByDescending(i => i.SourceItemCatalogId.HasValue).ThenBy(i => i.Id).First();
                    var sourceTitle = sourceItems.FirstOrDefault(s => s.Id == canonical.SourceItemCatalogId)?.TitleFa;
                    matchedItems.Add(new(clues[clueIndex], canonical.Value!.Value, canonical.MetricCode, sourceTitle,
                        canonical.Id, canonical.SourceItemCatalogId, group.Skip(1).Select(i => i.Id).ToArray()));
                }
            }
            if (matchedItems.Count == 0 || matchedItems.Select(i => i.RequestedClue).Distinct().Count() != clues.Length) continue;
            var company = statement.CompanyId is Guid localId ? companies.FirstOrDefault(c => c.Id == localId) : null;
            var mapped = company is null ? eligible.FirstOrDefault(c => c.ProviderName == statement.ProviderName && c.ExternalCompanyId == statement.ExternalCompanyId) : null;
            var status = company is not null ? FinancialStatementCompanyResolutionStatus.LocalCompanyId :
                mapped is not null ? FinancialStatementCompanyResolutionStatus.ProviderExternalMapping : FinancialStatementCompanyResolutionStatus.Unresolved;
            matches.Add(new(
                company is null ? mapped?.CompanySymbol ?? mapped?.TseSymbol : company.Ticker ?? company.TseSymbol ?? company.CompanySymbol,
                company?.Name ?? mapped?.Name,
                status, request.StatementType, statement.PeriodType, statement.PeriodStart, statement.PeriodEnd,
                statement.PublishedAt, statement.LastSynchronizedAt, statement.ProviderName, statement.ExternalCompanyId,
                statement.ExternalStatementId, matchedItems));
        }
        return new(matches.Count == 0 ? FinancialStatementValueSearchOutcome.NoMatch : FinancialStatementValueSearchOutcome.MatchesFound,
            matches.Take(MaximumMatches).ToArray());
    }

    private async Task<ResolvedClue[]?> ResolveCluesAsync(FinancialStatementValueSearchRequest request, FinancialStatementValueClue[] clues, CancellationToken cancellationToken)
    {
        var sourceItems = await dbContext.FinancialStatementSourceItems.AsNoTracking()
            .Where(s => s.ProviderName == request.ProviderName.Trim() && s.StatementType == request.StatementType.ToString()).ToListAsync(cancellationToken);
        var mappings = await dbContext.FinancialStatementSourceItemMetricMappings.AsNoTracking().ToListAsync(cancellationToken);
        var resolved = new ResolvedClue[clues.Length];
        for (var i = 0; i < clues.Length; i++)
        {
            var clue = clues[i];
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceIds = new HashSet<Guid>();
            if (!string.IsNullOrWhiteSpace(clue.MetricCode))
            {
                var code = clue.MetricCode.Trim().ToUpperInvariant();
                try { metricRegistry.ResolveDefinition(new MetricCode(code), DateOnly.FromDateTime(DateTime.UtcNow)); }
                catch (KeyNotFoundException) { return null; }
                codes.Add(code);
            }
            foreach (var expression in new[] { clue.GovernedAlias }.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var result = aliasResolver.ResolveAlias(expression!, "fa-IR", new MetricResolutionContext(), DateOnly.FromDateTime(DateTime.UtcNow));
                if (result.Status != MetricResolutionStatus.Resolved) return null;
                codes.Add(result.ResolvedDefinition!.Code.Value);
            }
            if (!string.IsNullOrWhiteSpace(clue.SourceTitle))
            {
                var title = clue.SourceTitle.Trim();
                foreach (var item in sourceItems.Where(s => string.Equals(s.TitleFa, title, StringComparison.Ordinal)))
                {
                    sourceIds.Add(item.Id);
                    foreach (var mapping in mappings.Where(m => m.SourceItemCatalogId == item.Id)) codes.Add(mapping.MetricCode);
                }
                if (sourceIds.Count == 0) return null;
            }
            resolved[i] = new(codes, sourceIds,
                !string.IsNullOrWhiteSpace(clue.SourceTitle) &&
                (!string.IsNullOrWhiteSpace(clue.MetricCode) || !string.IsNullOrWhiteSpace(clue.GovernedAlias)));
        }
        return resolved;
    }

    private static bool MatchesIdentity(NormalizedFinancialStatementLineItemRow line, ResolvedClue identity)
    {
        var codeMatch = identity.Codes.Count > 0 && identity.Codes.Contains(line.MetricCode ?? string.Empty);
        var sourceMatch = identity.SourceIds.Count > 0 && line.SourceItemCatalogId is Guid id && identity.SourceIds.Contains(id);
        return identity.RequireMetricAndSource
            ? codeMatch && sourceMatch
            : (identity.Codes.Count == 0 && identity.SourceIds.Count == 0) || codeMatch || sourceMatch;
    }

    private static string ResolveIdentityKey(NormalizedFinancialStatementRow statement, IReadOnlyCollection<NormalizedCompanyRow> companies, IReadOnlyCollection<NoavaranEligibleCompanyRow> eligible)
    {
        if (statement.CompanyId is Guid id && companies.Any(c => c.Id == id)) return $"local:{id}";
        var mapped = eligible.Any(c => c.ProviderName == statement.ProviderName && c.ExternalCompanyId == statement.ExternalCompanyId);
        return mapped ? $"mapped:{statement.ProviderName}:{statement.ExternalCompanyId}" : $"unresolved:{statement.ProviderName}:{statement.ExternalCompanyId}";
    }

    private static void Validate(FinancialStatementValueSearchRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ProviderName)) throw new ArgumentException("Provider name is required.", nameof(request));
        if (request.StatementType != FinancialStatementType.IncomeStatement) throw new ArgumentException("Only IncomeStatement is supported.", nameof(request));
        if (request.Clues is null || request.Clues.Count == 0) throw new ArgumentException("At least one clue is required.", nameof(request));
        if (request.Clues.Count > MaximumClues) throw new ArgumentException($"At most {MaximumClues} clues are supported.", nameof(request));
    }

    private sealed record ResolvedClue(HashSet<string> Codes, HashSet<Guid> SourceIds, bool RequireMetricAndSource);
}
