using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class EfCoreSymbolMetricLookupService(
    FinancialIngestionDbContext dbContext,
    ISymbolNameResolver symbolNameResolver,
    IMarketQuoteResolver quoteResolver,
    TimeProvider timeProvider) : ISymbolMetricLookupService
{
    public async Task<SymbolLookupTableResult> LookupAsync(
        SymbolLookupRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = timeProvider.GetUtcNow();
        var lookupId = Guid.NewGuid();

        // Deduplicate symbol names and metric codes from the request pairs.
        var uniqueSymbolNames = request.Pairs
            .Select(p => p.SymbolName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var uniqueMetricCodes = request.Pairs
            .Select(p => p.MetricCode.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueSymbolNames.Count == 0 || uniqueMetricCodes.Count == 0)
        {
            return BuildEmptyResult(lookupId, uniqueMetricCodes, startTime, [], uniqueSymbolNames);
        }

        // Resolve each raw symbol name to a SymbolCode.
        var unresolvedSymbols = new List<string>();
        var symbolCodeByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in uniqueSymbolNames)
        {
            var resolved = await symbolNameResolver.ResolveAsync(name, cancellationToken);
            if (resolved is not null)
                symbolCodeByName[name] = resolved.Value;
            else
                unresolvedSymbols.Add(name);
        }

        if (symbolCodeByName.Count == 0)
        {
            return BuildEmptyResult(lookupId, uniqueMetricCodes, startTime, unresolvedSymbols, uniqueSymbolNames);
        }

        // Load symbol rows for resolved codes.
        var resolvedCodes = symbolCodeByName.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var symbolRows = await dbContext.Symbols.AsNoTracking()
            .Where(s => resolvedCodes.Contains(s.SymbolCode))
            .ToListAsync(cancellationToken);

        // Load company names.
        var companyIds = symbolRows.Select(s => s.CompanyId).Distinct().ToList();
        var companyNameById = await dbContext.Companies.AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Load DerivedMetrics for resolved symbols and requested metric codes.
        var symbolIds = symbolRows.Select(s => s.Id).ToList();
        var derivedRows = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(dm => symbolIds.Contains(dm.SymbolId) && uniqueMetricCodes.Contains(dm.MetricCode))
            .ToListAsync(cancellationToken);

        // Latest row per (SymbolId, MetricCode) — highest PeriodEnd wins.
        var latestBySymbolMetric = derivedRows
            .GroupBy(dm => (dm.SymbolId, dm.MetricCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(dm => dm.PeriodEnd).First());

        // Resolve market quotes for LATEST_PRICE if requested.
        var needsQuotes = uniqueMetricCodes.Any(m =>
            string.Equals(m, "LATEST_PRICE", StringComparison.OrdinalIgnoreCase));

        var quoteBySymbol = new Dictionary<string, MarketQuoteObservation>(StringComparer.OrdinalIgnoreCase);
        if (needsQuotes && symbolRows.Count > 0)
        {
            var symbolCodes = symbolRows.Select(s => new SymbolCode(s.SymbolCode)).ToList();
            var quoteResult = await quoteResolver.ResolveAsync(symbolCodes, cancellationToken);
            foreach (var obs in quoteResult.Observations)
                quoteBySymbol[obs.SymbolCode.Value] = obs;
        }

        // Build columns: SYMBOL, COMPANY_NAME, then one column per metric.
        var columns = BuildLookupColumns(uniqueMetricCodes);

        // Build rows — one per resolved symbol, in order of the original request.
        var rows = new List<ScannerTableRow>();
        foreach (var name in uniqueSymbolNames)
        {
            if (!symbolCodeByName.TryGetValue(name, out var symbolCode)) continue;

            var symbolRow = symbolRows.FirstOrDefault(s =>
                string.Equals(s.SymbolCode, symbolCode, StringComparison.OrdinalIgnoreCase));
            if (symbolRow is null) continue;

            quoteBySymbol.TryGetValue(symbolCode, out var quote);
            var cells = BuildCells(columns, symbolRow, quote, latestBySymbolMetric);
            var companyName = companyNameById.GetValueOrDefault(symbolRow.CompanyId);

            rows.Add(new ScannerTableRow(
                symbolRow.SymbolCode,
                companyName,
                cells,
                Score: 1.0,
                []));
        }

        var matchingSymbolCount = rows.Count(r =>
            r.Cells.Values.Any(c => c.FreshnessStatus != CellFreshnessStatus.Missing));

        var endTime = timeProvider.GetUtcNow();
        return new SymbolLookupTableResult(
            lookupId,
            columns,
            rows,
            new ScannerExecutionFacts(
                endTime,
                endTime - startTime,
                TotalSymbolsEvaluated: uniqueSymbolNames.Count,
                MatchingSymbolCount: matchingSymbolCount,
                FromCache: false,
                Page: 1,
                PageSize: Math.Max(1, rows.Count),
                TotalPages: 1),
            [],
            unresolvedSymbols);
    }

    private static IReadOnlyCollection<ScannerTableColumn> BuildLookupColumns(
        IEnumerable<string> metricCodes)
    {
        var columns = new List<ScannerTableColumn>
        {
            new("SYMBOL", "Symbol", ScannerColumnType.Symbol),
            new("COMPANY_NAME", "Company", ScannerColumnType.CompanyName)
        };

        foreach (var code in metricCodes)
        {
            if (string.Equals(code, "LATEST_PRICE", StringComparison.OrdinalIgnoreCase))
                columns.Add(new ScannerTableColumn("LATEST_PRICE", "Latest Price", ScannerColumnType.LatestPrice));
            else if (string.Equals(code, "MARKET_CAP", StringComparison.OrdinalIgnoreCase))
                columns.Add(new ScannerTableColumn("MARKET_CAP", "Market Cap", ScannerColumnType.MarketCap));
            else
                columns.Add(new ScannerTableColumn(code, code, ScannerColumnType.Metric, code));
        }

        return columns;
    }

    private static IReadOnlyDictionary<string, ScannerTableCell> BuildCells(
        IReadOnlyCollection<ScannerTableColumn> columns,
        NormalizedSymbolRow symbol,
        MarketQuoteObservation? quote,
        Dictionary<(Guid SymbolId, string MetricCode), DerivedMetricRow> latestBySymbolMetric)
    {
        var cells = new Dictionary<string, ScannerTableCell>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            cells[column.Identifier] = column.ColumnType switch
            {
                ScannerColumnType.Symbol =>
                    new ScannerTableCell(null, symbol.SymbolCode, CellFreshnessStatus.Persisted, null),

                ScannerColumnType.CompanyName =>
                    new ScannerTableCell(null, null, CellFreshnessStatus.Persisted, null),

                ScannerColumnType.LatestPrice =>
                    BuildPriceCell(symbol, quote, latestBySymbolMetric),

                ScannerColumnType.MarketCap =>
                    BuildPersistedMetricCell(symbol, "MARKET_CAP", latestBySymbolMetric, FormatLargeNumber),

                ScannerColumnType.Metric when column.MetricCode is not null =>
                    BuildPersistedMetricCell(symbol, column.MetricCode, latestBySymbolMetric, v => v.ToString("N2")),

                _ => new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null)
            };
        }

        return cells;
    }

    private static ScannerTableCell BuildPriceCell(
        NormalizedSymbolRow symbol,
        MarketQuoteObservation? quote,
        Dictionary<(Guid SymbolId, string MetricCode), DerivedMetricRow> latestBySymbolMetric)
    {
        if (quote is not null)
        {
            var freshness = quote.Source == MarketQuoteSource.LiveQuote
                ? CellFreshnessStatus.Live
                : CellFreshnessStatus.PreviousTradingDay;
            return new ScannerTableCell(
                quote.LatestPrice,
                quote.LatestPrice.ToString("N2"),
                freshness,
                quote.AsOf);
        }

        if (latestBySymbolMetric.TryGetValue((symbol.Id, "LATEST_PRICE"), out var row) && row.Value is not null)
        {
            return new ScannerTableCell(
                row.Value,
                row.Value.Value.ToString("N2"),
                CellFreshnessStatus.Persisted,
                row.ObservedAt);
        }

        return new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null);
    }

    private static ScannerTableCell BuildPersistedMetricCell(
        NormalizedSymbolRow symbol,
        string metricCode,
        Dictionary<(Guid SymbolId, string MetricCode), DerivedMetricRow> latestBySymbolMetric,
        Func<decimal, string> formatter)
    {
        if (!latestBySymbolMetric.TryGetValue((symbol.Id, metricCode), out var row) || row.Value is null)
            return new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null);

        return new ScannerTableCell(
            row.Value,
            formatter(row.Value.Value),
            CellFreshnessStatus.Persisted,
            row.ObservedAt);
    }

    private SymbolLookupTableResult BuildEmptyResult(
        Guid lookupId,
        IEnumerable<string> metricCodes,
        DateTimeOffset startTime,
        IReadOnlyCollection<string> unresolvedSymbols,
        IReadOnlyCollection<string> requestedSymbolNames)
    {
        var endTime = timeProvider.GetUtcNow();
        return new SymbolLookupTableResult(
            lookupId,
            BuildLookupColumns(metricCodes),
            [],
            new ScannerExecutionFacts(
                endTime,
                endTime - startTime,
                TotalSymbolsEvaluated: requestedSymbolNames.Count,
                MatchingSymbolCount: 0,
                FromCache: false,
                Page: 1,
                PageSize: 1,
                TotalPages: 1),
            [],
            unresolvedSymbols);
    }

    private static string FormatLargeNumber(decimal value) =>
        value switch
        {
            >= 1_000_000_000_000m => $"{value / 1_000_000_000_000m:N1}T",
            >= 1_000_000_000m => $"{value / 1_000_000_000m:N1}B",
            >= 1_000_000m => $"{value / 1_000_000m:N1}M",
            _ => value.ToString("N0")
        };
}
