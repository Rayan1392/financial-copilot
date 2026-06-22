using System.Collections.Concurrent;
using System.Text;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace FinancialCopilot.Infrastructure.Financial.Semantics;

// ---------------------------------------------------------------------------
// Expression normalizer
// ---------------------------------------------------------------------------

public sealed class DefaultMetricAliasExpressionNormalizer : IMetricAliasExpressionNormalizer
{
    public string Normalize(string expression, string language)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return string.Empty;

        var s = expression.Trim();

        // Remove zero-width characters
        s = s.Replace("‌", "").Replace("‍", "").Replace("﻿", "");

        // Arabic/Persian letter normalization
        s = s
            .Replace('ك', 'ک') // Arabic Kaf → Persian Kaf
            .Replace('ي', 'ی') // Arabic Yeh → Persian Yeh
            .Replace('ى', 'ی') // Alef Maqsura → Persian Yeh
            .Replace('گ', 'گ') // Gaf stays
            .Replace('ئ', 'ی'); // Yeh with Hamza → Yeh

        // Eastern-Arabic digits → ASCII
        for (var i = 0; i <= 9; i++)
            s = s.Replace((char)(0x06F0 + i), (char)('0' + i));
        for (var i = 0; i <= 9; i++)
            s = s.Replace((char)(0x0660 + i), (char)('0' + i));

        // For Latin/English content lowercase everything
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            s = s.ToLowerInvariant();

        // Collapse multiple spaces
        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }
}

// ---------------------------------------------------------------------------
// EF Core repositories
// ---------------------------------------------------------------------------

public sealed class EfCoreDynamicMetricAliasRepository(
    SemanticCatalogDbContext dbContext) : IDynamicMetricAliasRepository
{
    public async Task<IReadOnlyList<DynamicMetricAlias>> GetActiveByLanguageAsync(
        string language, CancellationToken cancellationToken)
    {
        var rows = await dbContext.DynamicMetricAliases
            .AsNoTracking()
            .Where(r => r.Language == language && r.Status == "Active")
            .ToListAsync(cancellationToken);

        return rows.Select(MapRow).ToArray();
    }

    public async Task<DynamicMetricAlias?> FindActiveAsync(
        string normalizedExpression, string language, CancellationToken cancellationToken)
    {
        var row = await dbContext.DynamicMetricAliases
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.NormalizedExpression == normalizedExpression &&
                     r.Language == language &&
                     r.Status == "Active",
                cancellationToken);

        return row is null ? null : MapRow(row);
    }

    public async Task AddAsync(DynamicMetricAlias alias, CancellationToken cancellationToken)
    {
        dbContext.DynamicMetricAliases.Add(new DynamicMetricAliasRow
        {
            Id = alias.Id,
            Expression = alias.Expression,
            NormalizedExpression = alias.NormalizedExpression,
            Language = alias.Language,
            MetricCode = alias.MetricCode.Value,
            MetricVersion = alias.MetricVersion,
            Source = alias.Source.ToString(),
            Status = alias.Status.ToString(),
            ConfidenceScore = alias.ConfidenceScore,
            FrequencyCount = alias.FrequencyCount,
            CreatedAt = alias.CreatedAt,
            CreatedBy = alias.CreatedBy,
            ApprovedAt = alias.ApprovedAt,
            ApprovedBy = alias.ApprovedBy,
            DisabledAt = alias.DisabledAt,
            DisabledBy = alias.DisabledBy,
            DisableReason = alias.DisableReason,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableAsync(Guid id, string disabledBy, string reason, CancellationToken cancellationToken)
    {
        var row = await dbContext.DynamicMetricAliases.FindAsync([id], cancellationToken);
        if (row is null) return;

        row.Status = "Disabled";
        row.DisabledAt = DateTimeOffset.UtcNow;
        row.DisabledBy = disabledBy;
        row.DisableReason = reason;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static DynamicMetricAlias MapRow(DynamicMetricAliasRow row) =>
        new(row.Id, row.Expression, row.NormalizedExpression, row.Language,
            new MetricCode(row.MetricCode), row.MetricVersion,
            Enum.Parse<MetricAliasSource>(row.Source),
            Enum.Parse<MetricAliasStatus>(row.Status),
            row.ConfidenceScore, row.FrequencyCount,
            row.CreatedAt, row.CreatedBy,
            row.ApprovedAt, row.ApprovedBy,
            row.DisabledAt, row.DisabledBy, row.DisableReason);
}

public sealed class EfCoreMetricAliasCandidateRepository(
    SemanticCatalogDbContext dbContext) : IMetricAliasCandidateRepository
{
    public async Task<IReadOnlyList<MetricAliasCandidate>> GetPendingAsync(
        int take, CancellationToken cancellationToken)
    {
        var pending = "Pending";
        var rows = await dbContext.MetricAliasCandidates
            .AsNoTracking()
            .Where(r => r.Status == pending)
            .OrderByDescending(r => r.FrequencyCount)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyList<MetricAliasCandidate>> QueryAsync(
        MetricAliasCandidateQuery query, CancellationToken cancellationToken)
    {
        var queryable = dbContext.MetricAliasCandidates.AsNoTracking().AsQueryable();

        if (query.Status is not null)
        {
            var statusStr = query.Status.Value.ToString();
            queryable = queryable.Where(r => r.Status == statusStr);
        }
        if (!string.IsNullOrWhiteSpace(query.Language))
            queryable = queryable.Where(r => r.Language == query.Language);
        if (!string.IsNullOrWhiteSpace(query.MetricCode))
            queryable = queryable.Where(r => r.SuggestedMetricCode == query.MetricCode);

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 200);

        var rows = await queryable
            .OrderByDescending(r => r.LastSeenAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(MapRow).ToArray();
    }

    public async Task UpsertAsync(MetricAliasCandidate candidate, CancellationToken cancellationToken)
    {
        var existing = await dbContext.MetricAliasCandidates
            .FirstOrDefaultAsync(
                r => r.NormalizedExpression == candidate.NormalizedExpression &&
                     r.Language == candidate.Language &&
                     r.SuggestedMetricCode == candidate.SuggestedMetricCode.Value,
                cancellationToken);

        if (existing is not null)
        {
            existing.FrequencyCount += 1;
            existing.LastSeenAt = candidate.LastSeenAt;
            if (candidate.DistinctActorCount > existing.DistinctActorCount)
                existing.DistinctActorCount = candidate.DistinctActorCount;
            if (candidate.EvidenceExamplesJson is not null)
                existing.EvidenceExamplesJson = candidate.EvidenceExamplesJson;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        dbContext.MetricAliasCandidates.Add(new MetricAliasCandidateRow
        {
            Id = candidate.Id,
            Expression = candidate.Expression,
            NormalizedExpression = candidate.NormalizedExpression,
            Language = candidate.Language,
            SuggestedMetricCode = candidate.SuggestedMetricCode.Value,
            SuggestedMetricVersion = candidate.SuggestedMetricVersion,
            Status = candidate.Status.ToString(),
            ConfidenceScore = candidate.ConfidenceScore,
            FrequencyCount = candidate.FrequencyCount,
            DistinctActorCount = candidate.DistinctActorCount,
            FirstSeenAt = candidate.FirstSeenAt,
            LastSeenAt = candidate.LastSeenAt,
            EvidenceExamplesJson = candidate.EvidenceExamplesJson,
            RejectionReason = candidate.RejectionReason,
            PromotedAliasId = candidate.PromotedAliasId,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MetricAliasCandidate?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await dbContext.MetricAliasCandidates
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return row is null ? null : MapRow(row);
    }

    public async Task ApproveAsync(
        Guid id, string approvedBy, Guid promotedAliasId, CancellationToken cancellationToken)
    {
        var row = await dbContext.MetricAliasCandidates.FindAsync([id], cancellationToken);
        if (row is null) return;

        row.Status = MetricAliasCandidateStatus.Approved.ToString();
        row.PromotedAliasId = promotedAliasId;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var row = await dbContext.MetricAliasCandidates.FindAsync([id], cancellationToken);
        if (row is null) return;

        row.Status = MetricAliasCandidateStatus.Rejected.ToString();
        row.RejectionReason = reason;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MetricAliasCandidate MapRow(MetricAliasCandidateRow row) =>
        new(row.Id, row.Expression, row.NormalizedExpression, row.Language,
            new MetricCode(row.SuggestedMetricCode), row.SuggestedMetricVersion,
            Enum.Parse<MetricAliasCandidateStatus>(row.Status),
            row.ConfidenceScore, row.FrequencyCount, row.DistinctActorCount,
            row.FirstSeenAt, row.LastSeenAt,
            row.EvidenceExamplesJson, row.RejectionReason, row.PromotedAliasId);
}

// ---------------------------------------------------------------------------
// Composite alias resolver (singleton + per-language cache)
// ---------------------------------------------------------------------------

public sealed class CompositeMetricAliasResolver(
    IServiceScopeFactory scopeFactory,
    MetricAliasResolver staticResolver,
    IMetricAliasExpressionNormalizer normalizer,
    ILogger<CompositeMetricAliasResolver> logger)
    : IMetricAliasResolver, IMetricAliasCacheInvalidator
{
    // Key = language, Value = active aliases for that language (immutable snapshot)
    private readonly ConcurrentDictionary<string, IReadOnlyList<DynamicMetricAlias>> _cache = new();

    public MetricResolutionResult ResolveAlias(
        string userExpression,
        string language,
        MetricResolutionContext context,
        DateOnly asOf)
    {
        var normalized = normalizer.Normalize(userExpression, language);

        // 1. Exact active dynamic alias
        var langAliases = GetOrLoadLanguageAliases(language);
        var exact = langAliases
            .Where(a => string.Equals(a.NormalizedExpression, normalized, StringComparison.Ordinal))
            .ToArray();

        if (exact.Length == 1)
        {
            // Promote to static resolver so context (period type, comparison) filtering applies
            return staticResolver.ResolveAlias(exact[0].MetricCode.Value, language, context, asOf);
        }

        // 2. Static resolver (original behaviour on the original expression)
        var staticResult = staticResolver.ResolveAlias(userExpression, language, context, asOf);
        if (staticResult.Status == MetricResolutionStatus.Resolved)
            return staticResult;

        // 3. Fuzzy dynamic alias (case-insensitive)
        var fuzzy = langAliases
            .Where(a => string.Equals(a.NormalizedExpression, normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (fuzzy.Length == 1)
            return staticResolver.ResolveAlias(fuzzy[0].MetricCode.Value, language, context, asOf);

        // Return original static result (NotFound or Ambiguous)
        return staticResult;
    }

    public void InvalidateLanguage(string language) =>
        _cache.TryRemove(language, out _);

    private IReadOnlyList<DynamicMetricAlias> GetOrLoadLanguageAliases(string language)
    {
        if (_cache.TryGetValue(language, out var cached))
            return cached;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDynamicMetricAliasRepository>();
            var aliases = repo.GetActiveByLanguageAsync(language, CancellationToken.None).GetAwaiter().GetResult();
            _cache[language] = aliases;
            return aliases;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to load dynamic metric aliases for language {Language}; falling back to empty list.",
                language);
            return Array.Empty<DynamicMetricAlias>();
        }
    }
}

// ---------------------------------------------------------------------------
// Period alias resolver (singleton + per-language cache)
// ---------------------------------------------------------------------------

public sealed class EfCoreMetricPeriodAliasResolver(
    IServiceScopeFactory scopeFactory,
    IMetricAliasExpressionNormalizer normalizer,
    ILogger<EfCoreMetricPeriodAliasResolver> logger)
    : IMetricPeriodAliasResolver
{
    // Key = language, Value = (normalizedAlias -> ResolvedPeriodAlias) sorted by descending priority
    private readonly ConcurrentDictionary<string, IReadOnlyList<(string Normalized, ResolvedPeriodAlias Resolved)>>
        _cache = new();

    public ResolvedPeriodAlias? ResolvePhrase(string normalizedPhrase, string language)
    {
        var langAliases = GetOrLoad(language);

        // Longest-match first: iterate by priority order (already sorted desc)
        foreach (var (norm, resolved) in langAliases)
        {
            if (string.Equals(norm, normalizedPhrase, StringComparison.Ordinal))
                return resolved;
        }

        // Case-insensitive fuzzy fallback
        foreach (var (norm, resolved) in langAliases)
        {
            if (string.Equals(norm, normalizedPhrase, StringComparison.OrdinalIgnoreCase))
                return resolved;
        }

        return null;
    }

    public void InvalidateCache() => _cache.Clear();

    private IReadOnlyList<(string, ResolvedPeriodAlias)> GetOrLoad(string language)
    {
        if (_cache.TryGetValue(language, out var cached))
            return cached;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Persistence.SemanticCatalogDbContext>();
            var rows = db.MetricPeriodAliases
                .AsNoTracking()
                .Where(r => r.Language == language && r.Status == "Active")
                .OrderByDescending(r => r.Priority)
                .ToList();

            var entries = rows
                .Select(r => (
                    normalizer.Normalize(r.AliasText, language),
                    new ResolvedPeriodAlias(r.PeriodType, r.PeriodSelector, r.AliasText, r.Priority)))
                .ToList()
                .AsReadOnly();

            _cache[language] = entries;
            return entries;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to load period aliases for language {Language}; returning empty list.",
                language);
            return Array.Empty<(string, ResolvedPeriodAlias)>();
        }
    }
}

// ---------------------------------------------------------------------------
// Metric definition capability reader (singleton + cache)
// ---------------------------------------------------------------------------

public sealed class EfCoreMetricDefinitionCapabilityReader(
    IServiceScopeFactory scopeFactory,
    ILogger<EfCoreMetricDefinitionCapabilityReader> logger)
    : IMetricDefinitionCapabilityReader
{
    private volatile IReadOnlyDictionary<string, MetricDefinitionCapabilities>? _cache;

    public MetricDefinitionCapabilities? GetCapabilities(string metricCode) =>
        EnsureLoaded().TryGetValue(metricCode, out var caps) ? caps : null;

    public IReadOnlyList<MetricDefinitionCapabilities> GetAll() =>
        EnsureLoaded().Values.ToList().AsReadOnly();

    public void InvalidateCache() => _cache = null;

    private IReadOnlyDictionary<string, MetricDefinitionCapabilities> EnsureLoaded()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider
                .GetRequiredService<Persistence.SemanticCatalogDbContext>();

            // Latest version per MetricCode
            var rows = db.MetricDefinitions
                .AsNoTracking()
                .Where(r => r.EffectiveTo == null)
                .ToList()
                .GroupBy(r => r.MetricCode)
                .Select(g => g.OrderByDescending(r => r.EffectiveFrom).First());

            _cache = rows.ToDictionary(
                r => r.MetricCode,
                r => new MetricDefinitionCapabilities(
                    r.MetricCode,
                    r.PersianTitle,
                    r.DisplayName,
                    r.Category,
                    r.LookupEligible,
                    r.ScannerEligible,
                    r.IsMonthlyActivityMetric,
                    r.IsValuationMetric,
                    r.IsGrowthMetric,
                    r.IsMarginMetric,
                    r.IsFundamentalMetric,
                    r.SuppressQuoteContext));

            return _cache;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load metric definition capabilities; returning empty.");
            return new Dictionary<string, MetricDefinitionCapabilities>();
        }
    }
}
