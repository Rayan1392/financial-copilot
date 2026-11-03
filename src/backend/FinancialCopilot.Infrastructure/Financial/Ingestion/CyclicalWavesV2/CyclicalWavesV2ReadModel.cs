using System.Globalization;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>One ticker's latest screener state, read only from schema <c>cyclicalwaves</c>.</summary>
public sealed record CyclicalWavesV2TickerSnapshot(
    Guid TickerId,
    string Ticker,
    IReadOnlyDictionary<string, decimal?> Values,
    DateOnly? LastQuarterDate,
    DateOnly? LastMonthSaleDate,
    DateTimeOffset? LastSyncedAt);

/// <summary>Read side shared by the CyclicalWaves-mode Chat adapters. It never touches a NoavaranAmin or legacy table.</summary>
public sealed class CyclicalWavesV2ReadModel(FinancialIngestionDbContext db)
{
    public const string SourceLabel = "CyclicalWaves";

    public async Task<CyclicalWavesV2TickerRow?> FindTickerAsync(string? symbol, CancellationToken cancellationToken)
    {
        var normalized = PersianSymbolNormalizer.Normalize(symbol);
        if (normalized.Length == 0)
        {
            return null;
        }

        var isin = symbol!.Trim().ToUpperInvariant();
        var matches = await db.CyclicalWavesV2Tickers.AsNoTracking()
            .Where(row => row.IsActive && (row.NormalizedTicker == normalized || row.Enticker == isin))
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<CyclicalWavesV2TickerSnapshot>> LoadSnapshotsAsync(
        IReadOnlyCollection<Guid>? tickerIds,
        CancellationToken cancellationToken)
    {
        var tickers = db.CyclicalWavesV2Tickers.AsNoTracking().Where(row => row.IsActive);
        if (tickerIds is not null)
        {
            tickers = tickers.Where(row => tickerIds.Contains(row.Id));
        }

        var rows = await (
                from ticker in tickers
                join metadata in db.CyclicalWavesV2ScreenerMetadata.AsNoTracking() on ticker.Id equals metadata.TickerId
                select new { ticker.Id, ticker.Ticker, metadata.LastQuarterDate, metadata.LastMonthSaleDate, metadata.LastSyncedAt })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToArray();
        var values = await db.CyclicalWavesV2FinancialValues.AsNoTracking()
            .Where(value => ids.Contains(value.TickerId))
            .Select(value => new { value.TickerId, value.MetricDefinitionId, value.NumericValue })
            .ToListAsync(cancellationToken);
        var keysById = ScreenerMetricCatalog.All.ToDictionary(definition => definition.Id, definition => definition.FieldKey);
        var valuesByTicker = values
            .GroupBy(value => value.TickerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, decimal?>)group
                    .Where(value => keysById.ContainsKey(value.MetricDefinitionId))
                    .ToDictionary(value => keysById[value.MetricDefinitionId], value => value.NumericValue, StringComparer.Ordinal));

        return rows
            .OrderBy(row => row.Ticker, StringComparer.Ordinal)
            .Select(row => new CyclicalWavesV2TickerSnapshot(
                row.Id,
                row.Ticker,
                valuesByTicker.GetValueOrDefault(row.Id) ?? new Dictionary<string, decimal?>(StringComparer.Ordinal),
                row.LastQuarterDate,
                row.LastMonthSaleDate,
                row.LastSyncedAt))
            .ToList();
    }

    public async Task<(DateOnly PeriodEnd, decimal? Quarter, decimal? TrailingYear)?> GetLatestStatementLineAsync(
        Guid tickerId,
        int lineKey,
        CancellationToken cancellationToken)
    {
        var latest = await db.CyclicalWavesV2IncomeStatements.AsNoTracking()
            .Where(statement => statement.TickerId == tickerId)
            .OrderByDescending(statement => statement.PeriodEndingDate)
            .Select(statement => new { statement.Id, statement.PeriodEndingDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return null;
        }

        var value = await db.CyclicalWavesV2IncomeStatementValues.AsNoTracking()
            .Where(row => row.StatementId == latest.Id && row.LineDefinitionId == lineKey)
            .Select(row => new { row.Quarter, row.TrailingYear })
            .FirstOrDefaultAsync(cancellationToken);
        return (latest.PeriodEndingDate, value?.Quarter, value?.TrailingYear);
    }
}

/// <summary>Presentation conventions: money in million IRR (converted only here), margins in percent, ratios plain.</summary>
public static class CyclicalWavesV2Presentation
{
    public const string NotComputable = "قابل محاسبه نیست";
    public const string MillionRials = "میلیون ریال";

    public static string Format(ScreenerMetricUnit unit, decimal value) => unit switch
    {
        ScreenerMetricUnit.Irr => FinancialNumberFormatter.Whole(value / 1_000_000m),
        ScreenerMetricUnit.Percent => FinancialNumberFormatter.Ratio(value) + "%",
        _ => FinancialNumberFormatter.Ratio(value)
    };

    public static string UnitSuffix(ScreenerMetricUnit unit) => unit switch
    {
        ScreenerMetricUnit.Irr => MillionRials,
        ScreenerMetricUnit.Percent => "درصد",
        _ => string.Empty
    };

    /// <summary>The period a field describes: quarter fields follow <c>lastQuarterDate</c>, month fields <c>lastMonthSaleDate</c>.</summary>
    public static DateOnly? PeriodDate(ScreenerMetricDefinition definition, CyclicalWavesV2TickerSnapshot snapshot) =>
        definition.Category switch
        {
            ScreenerMetricCategory.MonthlySales => snapshot.LastMonthSaleDate,
            ScreenerMetricCategory.Valuation => null,
            _ => snapshot.LastQuarterDate
        };

    public static string PersianDate(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var value = date.ToDateTime(TimeOnly.MinValue);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}");
    }
}
