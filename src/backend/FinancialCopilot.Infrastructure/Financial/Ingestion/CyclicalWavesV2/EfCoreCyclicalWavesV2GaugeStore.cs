using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

public sealed class EfCoreCyclicalWavesV2GaugeStore(FinancialIngestionDbContext db) : ICyclicalWavesV2GaugeStore
{
    public async Task<CyclicalWavesV2GaugeCatalogApplyResult> ApplyGaugeCatalogAsync(IReadOnlyCollection<CyclicalWavesV2GaugeCatalogItem> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tickers = await db.CyclicalWavesV2Tickers.ToListAsync(cancellationToken);
        var matched = new Dictionary<Guid, CyclicalWavesV2GaugeCatalogItem>();
        var unmatched = 0;
        foreach (var item in items)
        {
            var ticker = MatchTicker(tickers, item.Enticker, item.Ticker);
            if (ticker is null || matched.ContainsKey(ticker.Id)) { unmatched++; continue; }
            matched[ticker.Id] = item;
        }

        var availability = await db.CyclicalWavesV2GaugeAvailability.ToDictionaryAsync(x => x.TickerId, cancellationToken);
        var updated = 0;
        var unchanged = 0;
        foreach (var ticker in tickers)
        {
            if (!availability.TryGetValue(ticker.Id, out var row))
            {
                row = new CyclicalWavesV2GaugeAvailabilityRow { TickerId = ticker.Id };
                db.CyclicalWavesV2GaugeAvailability.Add(row);
                availability[ticker.Id] = row;
            }
            var item = matched.GetValueOrDefault(ticker.Id);
            var hasPe = item?.HasPe ?? false;
            var hasPs = item?.HasPs ?? false;
            if (row.HasPe != hasPe || row.HasPs != hasPs) updated++; else unchanged++;
            row.HasPe = hasPe; row.HasPs = hasPs; row.LastFetchedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new CyclicalWavesV2GaugeCatalogApplyResult(matched.Count, unmatched, updated, unchanged);
    }

    public async Task<CyclicalWavesV2GaugeCatalogApplyResult> ApplyEquilibriumCatalogAsync(IReadOnlyCollection<CyclicalWavesV2EquilibriumCatalogItem> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tickers = await db.CyclicalWavesV2Tickers.ToListAsync(cancellationToken);
        var matched = new HashSet<Guid>(); var unmatched = 0;
        foreach (var item in items)
        {
            var ticker = MatchTicker(tickers, item.Enticker, item.Ticker);
            if (ticker is null || !matched.Add(ticker.Id)) unmatched++;
        }
        var rows = await db.CyclicalWavesV2EquilibriumRanges.ToDictionaryAsync(x => x.TickerId, cancellationToken);
        var updated = 0; var unchanged = 0;
        foreach (var ticker in tickers)
        {
            if (!rows.TryGetValue(ticker.Id, out var row))
            {
                row = new CyclicalWavesV2EquilibriumRangeRow { TickerId = ticker.Id };
                db.CyclicalWavesV2EquilibriumRanges.Add(row); rows[ticker.Id] = row;
            }
            var hasRange = matched.Contains(ticker.Id);
            if (row.HasRange != hasRange) updated++; else unchanged++;
            row.HasRange = hasRange; row.FetchedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new CyclicalWavesV2GaugeCatalogApplyResult(matched.Count, unmatched, updated, unchanged);
    }

    public async Task<IReadOnlyList<CyclicalWavesV2GaugeTarget>> GetGaugeTargetsAsync(CancellationToken cancellationToken) =>
        await (from ticker in db.CyclicalWavesV2Tickers.AsNoTracking()
               join availability in db.CyclicalWavesV2GaugeAvailability.AsNoTracking() on ticker.Id equals availability.TickerId
               where availability.HasPe || availability.HasPs
               orderby ticker.Ticker
               select new CyclicalWavesV2GaugeTarget(ticker.Id, ticker.Ticker, ticker.Enticker, availability.HasPe, availability.HasPs, false))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CyclicalWavesV2GaugeTarget>> GetEquilibriumTargetsAsync(CancellationToken cancellationToken) =>
        await (from ticker in db.CyclicalWavesV2Tickers.AsNoTracking()
               join range in db.CyclicalWavesV2EquilibriumRanges.AsNoTracking() on ticker.Id equals range.TickerId
               where range.HasRange orderby ticker.Ticker
               select new CyclicalWavesV2GaugeTarget(ticker.Id, ticker.Ticker, ticker.Enticker, false, false, true))
            .ToListAsync(cancellationToken);

    public async Task<CyclicalWavesV2ApplyOutcome> ApplyGaugeDetailsAsync(CyclicalWavesV2GaugeDetails details, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ticker = await FindMatchAsync(details.Enticker, details.Ticker, cancellationToken);
        if (ticker is null) return CyclicalWavesV2ApplyOutcome.NotInCatalog;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var changed = false;
        changed |= await UpsertGaugeAsync(ticker.Id, "PE", details.Pe, now, cancellationToken);
        changed |= await UpsertGaugeAsync(ticker.Id, "PS", details.Ps, now, cancellationToken);
        var availability = await db.CyclicalWavesV2GaugeAvailability.SingleOrDefaultAsync(x => x.TickerId == ticker.Id, cancellationToken);
        if (availability is not null)
        {
            changed |= availability.HasPe != (details.Pe is not null) || availability.HasPs != (details.Ps is not null);
            availability.HasPe = details.Pe is not null;
            availability.HasPs = details.Ps is not null;
            availability.LastFetchedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); db.ChangeTracker.Clear();
        return changed ? CyclicalWavesV2ApplyOutcome.Updated : CyclicalWavesV2ApplyOutcome.Unchanged;
    }

    public async Task<CyclicalWavesV2ApplyOutcome> ApplyEquilibriumRangeAsync(string identifier, CyclicalWavesV2EquilibriumRange range, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ticker = await FindMatchAsync(identifier, identifier, cancellationToken);
        if (ticker is null) return CyclicalWavesV2ApplyOutcome.NotInCatalog;
        var row = await db.CyclicalWavesV2EquilibriumRanges.SingleOrDefaultAsync(x => x.TickerId == ticker.Id, cancellationToken);
        var changed = row is null || !row.HasRange || row.Min != range.Min || row.Avg != range.Avg || row.Max != range.Max;
        if (!changed)
        {
            db.ChangeTracker.Clear();
            return CyclicalWavesV2ApplyOutcome.Unchanged;
        }
        row ??= new CyclicalWavesV2EquilibriumRangeRow { TickerId = ticker.Id };
        row.HasRange = true; row.Min = range.Min; row.Avg = range.Avg; row.Max = range.Max; row.FetchedAtUtc = now;
        if (db.Entry(row).State == EntityState.Detached) db.CyclicalWavesV2EquilibriumRanges.Add(row);
        await db.SaveChangesAsync(cancellationToken); db.ChangeTracker.Clear();
        return changed ? CyclicalWavesV2ApplyOutcome.Updated : CyclicalWavesV2ApplyOutcome.Unchanged;
    }

    public async Task MarkGaugeUnavailableAsync(string identifier, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ticker = await FindMatchAsync(identifier, identifier, cancellationToken); if (ticker is null) return;
        var row = await db.CyclicalWavesV2GaugeAvailability.SingleOrDefaultAsync(x => x.TickerId == ticker.Id, cancellationToken);
        if (row is not null) { row.HasPe = false; row.HasPs = false; row.LastFetchedAtUtc = now; await db.SaveChangesAsync(cancellationToken); }
    }

    public async Task MarkEquilibriumUnavailableAsync(string identifier, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ticker = await FindMatchAsync(identifier, identifier, cancellationToken); if (ticker is null) return;
        var row = await db.CyclicalWavesV2EquilibriumRanges.SingleOrDefaultAsync(x => x.TickerId == ticker.Id, cancellationToken);
        if (row is not null) { row.HasRange = false; row.FetchedAtUtc = now; await db.SaveChangesAsync(cancellationToken); }
    }

    private async Task<bool> UpsertGaugeAsync(Guid tickerId, string type, CyclicalWavesV2Gauge? gauge, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.CyclicalWavesV2GaugeDetails.SingleOrDefaultAsync(x => x.TickerId == tickerId && x.GaugeType == type, cancellationToken);
        if (gauge is null)
        {
            if (row is null) return false;
            db.CyclicalWavesV2GaugeDetails.Remove(row);
            return true;
        }
        var changed = row is null || row.A != gauge.A || row.B != gauge.B || row.C != gauge.C || row.D != gauge.D || row.E != gauge.E || row.F != gauge.F ||
                      row.Close != gauge.Close || row.Start != gauge.Start || row.End != gauge.End || row.Min != gauge.Min || row.Avg != gauge.Avg || row.Max != gauge.Max || row.Status != gauge.Status;
        if (!changed)
            return false;
        row ??= new CyclicalWavesV2GaugeDetailRow { TickerId = tickerId, GaugeType = type };
        row.A = gauge.A; row.B = gauge.B; row.C = gauge.C; row.D = gauge.D; row.E = gauge.E; row.F = gauge.F;
        row.Close = gauge.Close; row.Start = gauge.Start; row.End = gauge.End; row.Min = gauge.Min; row.Avg = gauge.Avg; row.Max = gauge.Max; row.Status = gauge.Status; row.FetchedAtUtc = now;
        if (db.Entry(row).State == EntityState.Detached) db.CyclicalWavesV2GaugeDetails.Add(row);
        return changed;
    }

    private async Task<CyclicalWavesV2TickerRow?> FindMatchAsync(string? enticker, string ticker, CancellationToken cancellationToken) =>
        MatchTicker(await db.CyclicalWavesV2Tickers.AsNoTracking().ToListAsync(cancellationToken), enticker, ticker);

    private static CyclicalWavesV2TickerRow? MatchTicker(IReadOnlyCollection<CyclicalWavesV2TickerRow> tickers, string? enticker, string ticker)
    {
        var isin = tickers.Where(x => !string.IsNullOrWhiteSpace(enticker) && string.Equals(x.Enticker?.Trim(), enticker.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (isin.Count == 1) return isin[0];
        if (isin.Count > 1) return null;
        var normalized = PersianSymbolNormalizer.Normalize(ticker);
        var matches = tickers.Where(x => x.NormalizedTicker == normalized).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
