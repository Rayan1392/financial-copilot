using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Domain.Financial.FollowedSymbols;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.FollowedSymbols;

public sealed class EfCoreFollowedSymbolRepository(
    FinancialIngestionDbContext dbContext) : IFollowedSymbolRepository
{
    public async Task<IReadOnlyCollection<FollowedSymbol>> GetAsync(
        FollowedSymbolActor actor,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.FollowedSymbols
            .AsNoTracking()
            .Where(row =>
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType)
            .OrderByDescending(row => row.FollowedAtUtc)
            .ThenBy(row => row.Symbol)
            .ToArrayAsync(cancellationToken);
        return rows.Select(ToDomain).ToArray();
    }

    public async Task<FollowedSymbol?> FindAsync(
        FollowedSymbolActor actor,
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.FollowedSymbols
            .AsNoTracking()
            .Where(row =>
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType &&
                row.ExternalCompanyId == externalCompanyId)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task SaveAsync(FollowedSymbol followedSymbol, CancellationToken cancellationToken)
    {
        var existing = await dbContext.FollowedSymbols
            .Where(row =>
                row.TenantId == followedSymbol.Actor.TenantId &&
                row.ActorId == followedSymbol.Actor.ActorId &&
                row.ActorType == followedSymbol.Actor.ActorType &&
                row.ExternalCompanyId == followedSymbol.ExternalCompanyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            dbContext.FollowedSymbols.Add(ToRow(followedSymbol));
        }
        else
        {
            existing.Symbol = followedSymbol.Symbol;
            existing.CompanyName = followedSymbol.CompanyName;
            existing.CompanyNameEnglish = followedSymbol.CompanyNameEnglish;
            existing.Source = followedSymbol.Source;
            existing.UpdatedAtUtc = followedSymbol.UpdatedAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceAsync(
        FollowedSymbolActor actor,
        IReadOnlyCollection<FollowedSymbol> followedSymbols,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.FollowedSymbols
            .Where(row =>
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType)
            .ToArrayAsync(cancellationToken);
        var desired = followedSymbols.ToDictionary(row => row.ExternalCompanyId, StringComparer.Ordinal);
        foreach (var row in existing)
        {
            if (!desired.TryGetValue(row.ExternalCompanyId, out var followed))
            {
                dbContext.FollowedSymbols.Remove(row);
                continue;
            }

            row.Symbol = followed.Symbol;
            row.CompanyName = followed.CompanyName;
            row.CompanyNameEnglish = followed.CompanyNameEnglish;
            row.Source = followed.Source;
            row.UpdatedAtUtc = followed.UpdatedAtUtc;
        }

        var existingIds = existing.Select(row => row.ExternalCompanyId).ToHashSet(StringComparer.Ordinal);
        dbContext.FollowedSymbols.AddRange(followedSymbols
            .Where(followed => !existingIds.Contains(followed.ExternalCompanyId))
            .Select(ToRow));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(
        FollowedSymbolActor actor,
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.FollowedSymbols
            .Where(row =>
                row.TenantId == actor.TenantId &&
                row.ActorId == actor.ActorId &&
                row.ActorType == actor.ActorType &&
                row.ExternalCompanyId == externalCompanyId)
            .ToArrayAsync(cancellationToken);
        dbContext.FollowedSymbols.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FollowedSymbol ToDomain(FollowedSymbolRow row) =>
        FollowedSymbol.Rehydrate(
            row.Id,
            new FollowedSymbolActor(row.TenantId, row.ActorId, row.ActorType),
            new CanonicalFollowedCompany(
                row.ExternalCompanyId,
                row.Symbol,
                row.CompanyName,
                row.CompanyNameEnglish),
            row.Source,
            row.FollowedAtUtc,
            row.UpdatedAtUtc);

    private static FollowedSymbolRow ToRow(FollowedSymbol followedSymbol) =>
        new()
        {
            Id = followedSymbol.Id,
            TenantId = followedSymbol.Actor.TenantId,
            ActorId = followedSymbol.Actor.ActorId,
            ActorType = followedSymbol.Actor.ActorType,
            ExternalCompanyId = followedSymbol.ExternalCompanyId,
            Symbol = followedSymbol.Symbol,
            CompanyName = followedSymbol.CompanyName,
            CompanyNameEnglish = followedSymbol.CompanyNameEnglish,
            Source = followedSymbol.Source,
            FollowedAtUtc = followedSymbol.FollowedAtUtc,
            UpdatedAtUtc = followedSymbol.UpdatedAtUtc
        };
}

public sealed class EfCoreFollowedCompanyResolver(
    FinancialIngestionDbContext dbContext) : IFollowedCompanyResolver
{
    public async Task<CanonicalCompanyResolution> ResolveReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        var normalized = reference.Trim();
        if (normalized.Length == 0) return new CanonicalCompanyResolution([], false);
        var rows = await dbContext.Companies
            .AsNoTracking()
            .Where(row => row.ExternalCompanyId == normalized ||
                          row.Ticker == normalized ||
                          row.TseSymbol == normalized ||
                          row.CompanySymbol == normalized ||
                          row.CompanyCode == normalized)
            .Select(row => new
            {
                row.ExternalCompanyId,
                row.Ticker,
                row.TseSymbol,
                row.CompanySymbol,
                row.CompanyCode,
                row.Name,
                row.NameEnglish,
                row.LastSynchronizedAt
            })
            .ToArrayAsync(cancellationToken);
        var candidates = rows
            .GroupBy(row => row.ExternalCompanyId, StringComparer.Ordinal)
            .Select(group =>
            {
                var row = group.OrderByDescending(item => item.LastSynchronizedAt).First();
                return new CanonicalFollowedCompany(
                    row.ExternalCompanyId,
                    FirstNonBlank(row.Ticker, row.TseSymbol, row.CompanySymbol, row.CompanyCode, row.ExternalCompanyId)
                        ?? row.ExternalCompanyId,
                    row.Name,
                    row.NameEnglish);
            })
            .OrderBy(item => item.Symbol, StringComparer.Ordinal)
            .ToArray();
        return new CanonicalCompanyResolution(candidates, candidates.Length > 1);
    }

    public async Task<CanonicalFollowedCompany?> ResolveAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var results = await ResolveManyAsync([externalCompanyId], cancellationToken);
        return results.TryGetValue(externalCompanyId, out var company) ? company : null;
    }

    public async Task<IReadOnlyDictionary<string, CanonicalFollowedCompany>> ResolveManyAsync(
        IReadOnlyCollection<string> externalCompanyIds,
        CancellationToken cancellationToken)
    {
        if (externalCompanyIds.Count == 0)
        {
            return new Dictionary<string, CanonicalFollowedCompany>(StringComparer.Ordinal);
        }

        var ids = externalCompanyIds.ToHashSet(StringComparer.Ordinal);
        var rows = await dbContext.Companies
            .AsNoTracking()
            .Where(row => ids.Contains(row.ExternalCompanyId))
            .Select(row => new
            {
                row.ExternalCompanyId,
                row.Ticker,
                row.TseSymbol,
                row.CompanySymbol,
                row.CompanyCode,
                row.Name,
                row.NameEnglish,
                row.LastSynchronizedAt
            })
            .ToArrayAsync(cancellationToken);
        return rows
            .GroupBy(row => row.ExternalCompanyId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group
                        .OrderByDescending(item => item.Ticker != null || item.TseSymbol != null || item.CompanySymbol != null)
                        .ThenByDescending(item => item.LastSynchronizedAt)
                        .First();
                    return new CanonicalFollowedCompany(
                        row.ExternalCompanyId,
                        FirstNonBlank(row.Ticker, row.TseSymbol, row.CompanySymbol, row.CompanyCode, row.ExternalCompanyId)
                            ?? row.ExternalCompanyId,
                        row.Name,
                        row.NameEnglish);
                },
                StringComparer.Ordinal);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
