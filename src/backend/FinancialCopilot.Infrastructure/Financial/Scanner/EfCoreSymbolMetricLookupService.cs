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

        var resolvedCodes = symbolCodeByName.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resolvedSymbolRows = await dbContext.Symbols.AsNoTracking()
            .Where(s => resolvedCodes.Contains(s.SymbolCode))
            .ToListAsync(cancellationToken);

        var companyIds = resolvedSymbolRows.Select(s => s.CompanyId).Distinct().ToList();

        var allCompanySymbolRows = await dbContext.Symbols.AsNoTracking()
            .Where(s => companyIds.Contains(s.CompanyId))
            .ToListAsync(cancellationToken);

        var companyLookup = await CompanyDisplayResolver.BuildLookupAsync(
            dbContext,
            allCompanySymbolRows.Count == 0 ? resolvedSymbolRows : allCompanySymbolRows,
            cancellationToken);

        var symbolIds = allCompanySymbolRows.Select(s => s.Id).ToList();
        var derivedRows = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(dm => symbolIds.Contains(dm.SymbolId) && uniqueMetricCodes.Contains(dm.MetricCode))
            .ToListAsync(cancellationToken);

        var companyIdBySymbolId = allCompanySymbolRows.ToDictionary(s => s.Id, s => s.CompanyId);
        var latestByCompanyMetric = derivedRows
            .Where(dm => companyIdBySymbolId.ContainsKey(dm.SymbolId))
            .GroupBy(dm => (CompanyId: companyIdBySymbolId[dm.SymbolId], dm.MetricCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(dm => dm.PeriodEnd).First());

        var needsQuotes = uniqueMetricCodes.Any(m =>
            string.Equals(m, "LATEST_PRICE", StringComparison.OrdinalIgnoreCase));

        var quoteBySymbol = new Dictionary<string, MarketQuoteObservation>(StringComparer.OrdinalIgnoreCase);
        if (needsQuotes && allCompanySymbolRows.Count > 0)
        {
            var symbolCodes = allCompanySymbolRows.Select(s => new SymbolCode(s.SymbolCode)).ToList();
            var quoteResult = await quoteResolver.ResolveAsync(symbolCodes, cancellationToken);
            foreach (var obs in quoteResult.Observations)
                quoteBySymbol[obs.SymbolCode.Value] = obs;
        }

        var columns = BuildLookupColumns(uniqueMetricCodes);
        var rows = new List<ScannerTableRow>();
        foreach (var name in uniqueSymbolNames)
        {
            if (!symbolCodeByName.TryGetValue(name, out var symbolCode)) continue;

            var symbolRow = resolvedSymbolRows.FirstOrDefault(s =>
                string.Equals(s.SymbolCode, symbolCode, StringComparison.OrdinalIgnoreCase));
            if (symbolRow is null) continue;

            var quote = ResolveQuoteForCompany(symbolRow.CompanyId, allCompanySymbolRows, quoteBySymbol);
            var company = CompanyDisplayResolver.ResolveCompany(symbolRow, companyLookup);
            var displaySymbol = CompanyDisplayResolver.GetDisplaySymbol(company, symbolRow);
            var cells = BuildCells(
                columns,
                symbolRow.CompanyId,
                displaySymbol,
                company?.Name,
                quote,
                latestByCompanyMetric);

            rows.Add(new ScannerTableRow(
                displaySymbol,
                company?.Name,
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
        Guid companyId,
        string displaySymbol,
        string? companyName,
        MarketQuoteObservation? quote,
        IReadOnlyDictionary<(Guid CompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric)
    {
        var cells = new Dictionary<string, ScannerTableCell>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            cells[column.Identifier] = column.ColumnType switch
            {
                ScannerColumnType.Symbol =>
                    new ScannerTableCell(null, displaySymbol, CellFreshnessStatus.Persisted, null),

                ScannerColumnType.CompanyName =>
                    new ScannerTableCell(null, companyName, CellFreshnessStatus.Persisted, null),

                ScannerColumnType.LatestPrice =>
                    BuildPriceCell(companyId, quote, latestByCompanyMetric),

                ScannerColumnType.MarketCap =>
                    BuildPersistedMetricCell(companyId, "MARKET_CAP", latestByCompanyMetric, FormatLargeNumber),

                ScannerColumnType.Metric when column.MetricCode is not null =>
                    BuildPersistedMetricCell(companyId, column.MetricCode, latestByCompanyMetric, v => v.ToString("N2")),

                _ => new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null)
            };
        }

        return cells;
    }

    private static ScannerTableCell BuildPriceCell(
        Guid companyId,
        MarketQuoteObservation? quote,
        IReadOnlyDictionary<(Guid CompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric)
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

        if (latestByCompanyMetric.TryGetValue((companyId, "LATEST_PRICE"), out var row) && row.Value is not null)
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
        Guid companyId,
        string metricCode,
        IReadOnlyDictionary<(Guid CompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric,
        Func<decimal, string> formatter)
    {
        if (!latestByCompanyMetric.TryGetValue((companyId, metricCode), out var row) || row.Value is null)
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

    private static MarketQuoteObservation? ResolveQuoteForCompany(
        Guid companyId,
        IReadOnlyCollection<NormalizedSymbolRow> symbols,
        IReadOnlyDictionary<string, MarketQuoteObservation> quoteBySymbol)
    {
        foreach (var symbol in symbols.Where(s => s.CompanyId == companyId))
        {
            if (quoteBySymbol.TryGetValue(symbol.SymbolCode, out var quote))
                return quote;
        }

        return null;
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
