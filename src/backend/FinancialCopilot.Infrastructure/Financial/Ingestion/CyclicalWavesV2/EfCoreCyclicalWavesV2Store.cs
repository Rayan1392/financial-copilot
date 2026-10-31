using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// Latest-state screener upserts and period upserts for income statements. Unchanged values are never
/// rewritten, statement periods are never deleted, and every write for one ticker is one transaction.
/// </summary>
public sealed class EfCoreCyclicalWavesV2Store(
    FinancialIngestionDbContext db,
    ILogger<EfCoreCyclicalWavesV2Store> logger) : ICyclicalWavesV2Store
{
    public async Task<CyclicalWavesV2CatalogResult> UpsertCatalogAsync(
        IReadOnlyCollection<string> tickers,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var incoming = tickers
            .Where(ticker => !string.IsNullOrWhiteSpace(ticker))
            .Select(ticker => ticker.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.CyclicalWavesV2Tickers.ToListAsync(cancellationToken);
        var byTicker = existing.ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        int added = 0, deactivated = 0, reactivated = 0, unchanged = 0;

        foreach (var ticker in incoming)
        {
            if (byTicker.TryGetValue(ticker, out var row))
            {
                if (!row.IsActive)
                {
                    row.IsActive = true;
                    row.UpdatedAt = now;
                    reactivated++;
                }
                else
                {
                    unchanged++;
                }

                row.LastSeenInCatalogAt = now;
                continue;
            }

            db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
            {
                Id = Guid.NewGuid(),
                Ticker = ticker,
                NormalizedTicker = PersianSymbolNormalizer.Normalize(ticker),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                LastSeenInCatalogAt = now
            });
            added++;
        }

        foreach (var row in existing.Where(row => row.IsActive && !incoming.Contains(row.Ticker)))
        {
            row.IsActive = false;
            row.UpdatedAt = now;
            deactivated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new CyclicalWavesV2CatalogResult(added, deactivated, reactivated, unchanged);
    }

    public async Task<IReadOnlyList<string>> GetActiveTickersAsync(CancellationToken cancellationToken) =>
        await db.CyclicalWavesV2Tickers
            .AsNoTracking()
            .Where(row => row.IsActive)
            .OrderBy(row => row.Ticker)
            .Select(row => row.Ticker)
            .ToListAsync(cancellationToken);

    public async Task<string?> FindCatalogTickerAsync(string ticker, CancellationToken cancellationToken) =>
        (await FindTickerRowAsync(ticker, tracked: false, cancellationToken))?.Ticker;

    public async Task<CyclicalWavesV2ScreenerApplyResult> ApplyScreenerAsync(
        CyclicalWavesV2ScreenerSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ticker = await FindTickerRowAsync(snapshot.Ticker, tracked: true, cancellationToken);
        if (ticker is null)
        {
            return new CyclicalWavesV2ScreenerApplyResult(CyclicalWavesV2ApplyOutcome.NotInCatalog, false);
        }

        var stored = await db.CyclicalWavesV2FinancialValues
            .Where(row => row.TickerId == ticker.Id)
            .ToDictionaryAsync(row => row.MetricDefinitionId, cancellationToken);
        var changed = false;

        foreach (var definition in ScreenerMetricCatalog.All)
        {
            snapshot.Values.TryGetValue(definition.FieldKey, out var incoming);
            if (!stored.TryGetValue(definition.Id, out var row))
            {
                db.CyclicalWavesV2FinancialValues.Add(new CyclicalWavesV2FinancialValueRow
                {
                    TickerId = ticker.Id,
                    MetricDefinitionId = definition.Id,
                    NumericValue = incoming,
                    UpdatedAt = now
                });
                changed = true;
            }
            else if (!ScreenerValueComparer.AreEqual(row.NumericValue, incoming))
            {
                row.NumericValue = incoming;
                row.UpdatedAt = now;
                changed = true;
            }
        }

        var entickerConflict = await ApplyEntickerAsync(ticker, snapshot.Enticker, now, cancellationToken);

        var metadata = await db.CyclicalWavesV2ScreenerMetadata
            .SingleOrDefaultAsync(row => row.TickerId == ticker.Id, cancellationToken);
        if (metadata is null)
        {
            metadata = new CyclicalWavesV2ScreenerMetadataRow { TickerId = ticker.Id, LastChangedAt = now };
            db.CyclicalWavesV2ScreenerMetadata.Add(metadata);
            changed = true;
        }

        if (metadata.LastMonthSaleDate != snapshot.LastMonthSaleDate ||
            metadata.LastQuarterDate != snapshot.LastQuarterDate)
        {
            metadata.LastMonthSaleDate = snapshot.LastMonthSaleDate;
            metadata.LastQuarterDate = snapshot.LastQuarterDate;
            changed = true;
        }

        metadata.ProviderUpdatedAt = snapshot.ProviderUpdatedAt;
        metadata.LastSyncedAt = now;
        if (changed)
        {
            metadata.LastChangedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new CyclicalWavesV2ScreenerApplyResult(
            changed ? CyclicalWavesV2ApplyOutcome.Updated : CyclicalWavesV2ApplyOutcome.Unchanged,
            entickerConflict);
    }

    public async Task<int> UpsertLineDefinitionsAsync(
        IReadOnlyCollection<CyclicalWavesV2LineDefinition> definitions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var incoming = definitions
            .GroupBy(definition => definition.Key)
            .Select(group => group.Last())
            .ToArray();
        var stored = await db.CyclicalWavesV2IncomeStatementLineDefinitions
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var changed = 0;

        foreach (var definition in incoming)
        {
            if (!stored.TryGetValue(definition.Key, out var row))
            {
                db.CyclicalWavesV2IncomeStatementLineDefinitions.Add(new CyclicalWavesV2IncomeStatementLineDefinitionRow
                {
                    Id = definition.Key,
                    NameEn = definition.NameEn,
                    NameFa = definition.NameFa,
                    UpdatedAt = now
                });
                changed++;
            }
            else if (row.NameEn != definition.NameEn || row.NameFa != definition.NameFa)
            {
                row.NameEn = definition.NameEn;
                row.NameFa = definition.NameFa;
                row.UpdatedAt = now;
                changed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return changed;
    }

    public async Task<CyclicalWavesV2StatementApplyResult> ApplyIncomeStatementsAsync(
        CyclicalWavesV2IncomeStatementSet statements,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ticker = await FindTickerRowAsync(statements.Ticker, tracked: false, cancellationToken);
        if (ticker is null)
        {
            return new CyclicalWavesV2StatementApplyResult(CyclicalWavesV2ApplyOutcome.NotInCatalog, 0, 0, 0);
        }

        // A restated period can appear twice in one response; the latest publication wins.
        var periods = statements.Statements
            .GroupBy(statement => (statement.PeriodEndingDate, statement.FiscalMonth))
            .Select(group => group.OrderByDescending(statement => statement.PublishDate ?? DateTimeOffset.MinValue).First())
            .ToArray();
        var duplicates = statements.Statements.Count - periods.Length;
        if (duplicates > 0)
        {
            logger.LogWarning(
                "CyclicalWaves v2 statements for {Ticker} repeated {Duplicates} period(s); the latest publication was kept.",
                ticker.Ticker,
                duplicates);
        }

        await EnsureLineDefinitionsAsync(periods.SelectMany(statement => statement.Lines), now, cancellationToken);

        var existing = await db.CyclicalWavesV2IncomeStatements
            .Where(row => row.TickerId == ticker.Id)
            .ToListAsync(cancellationToken);
        var existingByPeriod = existing.ToDictionary(row => (row.PeriodEndingDate, row.FiscalMonth));
        var existingIds = existing.Select(row => row.Id).ToArray();
        var existingValues = (await db.CyclicalWavesV2IncomeStatementValues
                .Where(row => existingIds.Contains(row.StatementId))
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.StatementId);
        int inserted = 0, updated = 0;

        foreach (var statement in periods)
        {
            if (!existingByPeriod.TryGetValue((statement.PeriodEndingDate, statement.FiscalMonth), out var header))
            {
                header = new CyclicalWavesV2IncomeStatementRow
                {
                    Id = Guid.NewGuid(),
                    TickerId = ticker.Id,
                    PeriodEndingDate = statement.PeriodEndingDate,
                    FiscalMonth = statement.FiscalMonth,
                    CreatedAt = now
                };
                ApplyHeader(header, statement, now);
                db.CyclicalWavesV2IncomeStatements.Add(header);
                AddValues(header.Id, statement.Lines, new Dictionary<int, CyclicalWavesV2IncomeStatementValueRow>());
                inserted++;
                continue;
            }

            var headerChanged = header.FiscalYear != statement.FiscalYear ||
                                header.Audited != statement.Audited ||
                                header.PublishDate != statement.PublishDate ||
                                header.CodalUrl != statement.CodalUrl;
            if (headerChanged)
            {
                ApplyHeader(header, statement, now);
            }

            var values = existingValues[header.Id].ToDictionary(row => row.LineDefinitionId);
            var valuesChanged = AddValues(header.Id, statement.Lines, values);
            if (headerChanged || valuesChanged)
            {
                header.UpdatedAt = now;
                updated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new CyclicalWavesV2StatementApplyResult(
            inserted + updated > 0 ? CyclicalWavesV2ApplyOutcome.Updated : CyclicalWavesV2ApplyOutcome.Unchanged,
            inserted,
            updated,
            duplicates);
    }

    private bool AddValues(
        Guid statementId,
        IReadOnlyList<CyclicalWavesV2StatementLine> lines,
        Dictionary<int, CyclicalWavesV2IncomeStatementValueRow> stored)
    {
        var changed = false;
        var seen = new HashSet<int>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!seen.Add(line.Key))
            {
                continue;
            }

            if (!stored.TryGetValue(line.Key, out var row))
            {
                db.CyclicalWavesV2IncomeStatementValues.Add(new CyclicalWavesV2IncomeStatementValueRow
                {
                    StatementId = statementId,
                    LineDefinitionId = line.Key,
                    SortOrder = index,
                    Quarter = line.Quarter,
                    YearToDate = line.YearToDate,
                    TrailingYear = line.TrailingYear
                });
                changed = true;
            }
            else if (row.SortOrder != index ||
                     !ScreenerValueComparer.AreEqual(row.Quarter, line.Quarter) ||
                     !ScreenerValueComparer.AreEqual(row.YearToDate, line.YearToDate) ||
                     !ScreenerValueComparer.AreEqual(row.TrailingYear, line.TrailingYear))
            {
                row.SortOrder = index;
                row.Quarter = line.Quarter;
                row.YearToDate = line.YearToDate;
                row.TrailingYear = line.TrailingYear;
                changed = true;
            }
        }

        return changed;
    }

    private static void ApplyHeader(
        CyclicalWavesV2IncomeStatementRow header,
        CyclicalWavesV2IncomeStatement statement,
        DateTimeOffset now)
    {
        header.FiscalYear = statement.FiscalYear;
        header.Audited = statement.Audited;
        header.PublishDate = statement.PublishDate;
        header.CodalUrl = statement.CodalUrl;
        header.UpdatedAt = now;
    }

    private async Task EnsureLineDefinitionsAsync(
        IEnumerable<CyclicalWavesV2StatementLine> lines,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var byKey = lines.GroupBy(line => line.Key).ToDictionary(group => group.Key, group => group.First());
        var keys = byKey.Keys.ToArray();
        var known = await db.CyclicalWavesV2IncomeStatementLineDefinitions
            .Where(row => keys.Contains(row.Id))
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        foreach (var key in keys.Except(known))
        {
            var line = byKey[key];
            db.CyclicalWavesV2IncomeStatementLineDefinitions.Add(new CyclicalWavesV2IncomeStatementLineDefinitionRow
            {
                Id = key,
                NameEn = line.NameEn,
                NameFa = line.NameFa,
                UpdatedAt = now
            });
        }
    }

    private async Task<bool> ApplyEntickerAsync(
        CyclicalWavesV2TickerRow ticker,
        string? enticker,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(enticker) || ticker.Enticker == enticker)
        {
            return false;
        }

        if (ticker.Enticker is not null)
        {
            logger.LogWarning(
                "CyclicalWaves v2 ISIN conflict for {Ticker}: stored value kept, provider value ignored.",
                ticker.Ticker);
            return true;
        }

        var takenByOther = await db.CyclicalWavesV2Tickers
            .AnyAsync(row => row.Enticker == enticker && row.Id != ticker.Id, cancellationToken);
        if (takenByOther)
        {
            logger.LogWarning(
                "CyclicalWaves v2 ISIN for {Ticker} is already assigned to another ticker; it was not stored.",
                ticker.Ticker);
            return true;
        }

        ticker.Enticker = enticker.Trim();
        ticker.UpdatedAt = now;
        return false;
    }

    private async Task<CyclicalWavesV2TickerRow?> FindTickerRowAsync(
        string ticker,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var query = tracked ? db.CyclicalWavesV2Tickers : db.CyclicalWavesV2Tickers.AsNoTracking();
        var trimmed = ticker.Trim();
        var exact = await query.SingleOrDefaultAsync(row => row.Ticker == trimmed, cancellationToken);
        if (exact is not null)
        {
            return exact;
        }

        var normalized = PersianSymbolNormalizer.Normalize(trimmed);
        if (normalized.Length == 0)
        {
            return null;
        }

        var matches = await query
            .Where(row => row.NormalizedTicker == normalized)
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }
}
