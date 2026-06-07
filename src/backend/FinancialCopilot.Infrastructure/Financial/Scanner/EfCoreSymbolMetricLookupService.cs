using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class EfCoreSymbolMetricLookupService(
    FinancialIngestionDbContext dbContext,
    ISymbolNameResolver symbolNameResolver,
    IMarketQuoteResolver quoteResolver,
    TimeProvider timeProvider,
    ILogger<EfCoreSymbolMetricLookupService> logger) : ISymbolMetricLookupService
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
            {
                symbolCodeByName[name] = resolved.Value;
                LogPeLookupResolution(request.QueryText, name, resolved.Value, uniqueMetricCodes);
            }
            else
            {
                unresolvedSymbols.Add(name);
                LogPeLookupMissing(
                    request.QueryText,
                    name,
                    null,
                    null,
                    null,
                    null,
                    "SymbolResolutionFailed",
                    uniqueMetricCodes);
            }
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

        var lookupMetricCodes = uniqueMetricCodes
            .Concat(["LATEST_PRICE", "DAILY_CHANGE_PCT"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var symbolIds = allCompanySymbolRows.Select(s => s.Id).ToList();
        var derivedRows = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(dm => symbolIds.Contains(dm.SymbolId) && lookupMetricCodes.Contains(dm.MetricCode))
            .ToListAsync(cancellationToken);

        LogPeLookupQueryScope(
            request.QueryText,
            uniqueMetricCodes,
            resolvedCodes,
            companyIds,
            symbolIds,
            derivedRows);

        var companyIdBySymbolId = allCompanySymbolRows.ToDictionary(s => s.Id, s => s.CompanyId);
        var latestByCompanyMetric = derivedRows
            .Where(dm => companyIdBySymbolId.ContainsKey(dm.SymbolId))
            .GroupBy(dm => (CompanyId: companyIdBySymbolId[dm.SymbolId], dm.MetricCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(dm => dm.PeriodEnd).First());

        var quoteBySymbol = new Dictionary<string, MarketQuoteObservation>(StringComparer.OrdinalIgnoreCase);
        if (allCompanySymbolRows.Count > 0)
        {
            try
            {
                var symbolCodes = allCompanySymbolRows.Select(s => new SymbolCode(s.SymbolCode)).ToList();
                var quoteResult = await quoteResolver.ResolveAsync(symbolCodes, cancellationToken);
                foreach (var obs in quoteResult.Observations)
                    quoteBySymbol[obs.SymbolCode.Value] = obs;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Symbol lookup quote resolution failed; continuing with persisted metric fallbacks. Query={OriginalUserQuery}; RequestedMetrics={RequestedMetrics}",
                    request.QueryText,
                    string.Join(",", uniqueMetricCodes));
            }
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

            LogPeLookupResult(
                request.QueryText,
                name,
                symbolCode,
                symbolRow.Id,
                symbolRow.CompanyId,
                allCompanySymbolRows,
                latestByCompanyMetric,
                cells);

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
            BuildMissingDataWarnings(rows, uniqueMetricCodes),
            unresolvedSymbols);
    }

    private static IReadOnlyCollection<ScannerTableColumn> BuildLookupColumns(
        IEnumerable<string> metricCodes)
    {
        var columns = new List<ScannerTableColumn>
        {
            new("SYMBOL", "Symbol", ScannerColumnType.Symbol),
            new("COMPANY_NAME", "Company", ScannerColumnType.CompanyName),
            new("LATEST_PRICE", "Latest Price", ScannerColumnType.LatestPrice),
            new("DAILY_CHANGE_PCT", "Change %", ScannerColumnType.DailyChangePercent)
        };

        var seen = columns
            .Select(c => c.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var code in metricCodes)
        {
            if (!seen.Add(code))
                continue;

            if (string.Equals(code, "MARKET_CAP", StringComparison.OrdinalIgnoreCase))
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

                ScannerColumnType.DailyChangePercent =>
                    BuildChangeCell(companyId, quote, latestByCompanyMetric),

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

    private static ScannerTableCell BuildChangeCell(
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
                quote.PriceChangePercentage,
                $"{quote.PriceChangePercentage:+0.00;-0.00;0.00}%",
                freshness,
                quote.AsOf);
        }

        if (latestByCompanyMetric.TryGetValue((companyId, "DAILY_CHANGE_PCT"), out var row) && row.Value is not null)
        {
            return new ScannerTableCell(
                row.Value,
                $"{row.Value:+0.00;-0.00;0.00}%",
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

    private void LogPeLookupResolution(
        string? queryText,
        string detectedSymbol,
        string resolvedSymbol,
        IReadOnlyCollection<string> metricCodes)
    {
        if (!ContainsPeMetric(metricCodes)) return;

        logger.LogInformation(
            "PE lookup symbol resolved. Query={OriginalUserQuery}; DetectedSymbol={DetectedSymbol}; ResolvedSymbol={ResolvedSymbol}; RequestedMetric=PE_TTM",
            queryText,
            detectedSymbol,
            resolvedSymbol);
    }

    private void LogPeLookupResult(
        string? queryText,
        string detectedSymbol,
        string resolvedSymbol,
        Guid resolvedSymbolId,
        Guid companyId,
        IReadOnlyCollection<NormalizedSymbolRow> candidateSymbols,
        IReadOnlyDictionary<(Guid CompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric,
        IReadOnlyDictionary<string, ScannerTableCell> cells)
    {
        if (!cells.TryGetValue("PE_TTM", out var cell))
            return;

        latestByCompanyMetric.TryGetValue((companyId, "PE_TTM"), out var rawRow);
        var candidateSymbolIds = string.Join(
            ",",
            candidateSymbols
                .Where(s => s.CompanyId == companyId)
                .Select(s => s.Id)
                .Distinct());

        if (cell.Value is not null && cell.FreshnessStatus != CellFreshnessStatus.Missing)
        {
            logger.LogInformation(
                "PE lookup value retrieved. Query={OriginalUserQuery}; NormalizedMetric=PE_TTM; DetectedSymbol={DetectedSymbol}; NormalizedSymbol={ResolvedSymbol}; ResolvedSymbolId={ResolvedSymbolId}; ResolvedCompanyId={ResolvedCompanyId}; CandidateSymbolIds={CandidateSymbolIds}; SqlSource={SqlSource}; RawPeTtmValue={RawPeTtmValue}; Freshness={Freshness}; ConfidenceDecisionReason={ConfidenceDecisionReason}",
                queryText,
                detectedSymbol,
                resolvedSymbol,
                resolvedSymbolId,
                companyId,
                candidateSymbolIds,
                "DerivedMetrics grouped by Symbols.CompanyId",
                rawRow?.Value ?? cell.Value,
                cell.FreshnessStatus,
                "PreCalculatedMetric because PE_TTM has a persisted non-missing value.");
            return;
        }

        var missingReason = rawRow is null
            ? "NoDerivedMetricRowForResolvedCompanySymbols"
            : "DerivedMetricValueNull";

        LogPeLookupMissing(
            queryText,
            detectedSymbol,
            resolvedSymbol,
            resolvedSymbolId,
            companyId,
            candidateSymbolIds,
            missingReason,
            ["PE_TTM"]);
    }

    private void LogPeLookupMissing(
        string? queryText,
        string detectedSymbol,
        string? resolvedSymbol,
        Guid? resolvedSymbolId,
        Guid? companyId,
        string? candidateSymbolIds,
        string reason,
        IReadOnlyCollection<string> metricCodes)
    {
        if (!ContainsPeMetric(metricCodes)) return;

        logger.LogWarning(
            "PE lookup value missing. Query={OriginalUserQuery}; NormalizedMetric=PE_TTM; DetectedSymbol={DetectedSymbol}; NormalizedSymbol={ResolvedSymbol}; ResolvedSymbolId={ResolvedSymbolId}; ResolvedCompanyId={ResolvedCompanyId}; CandidateSymbolIds={CandidateSymbolIds}; SqlSource={SqlSource}; RawPeTtmValue={RawPeTtmValue}; MissingReason={MissingReason}; ConfidenceDecisionReason={ConfidenceDecisionReason}",
            queryText,
            detectedSymbol,
            resolvedSymbol,
            resolvedSymbolId,
            companyId,
            candidateSymbolIds,
            "DerivedMetrics grouped by Symbols.CompanyId",
            null,
            reason,
            "MissingDataFallback because PE_TTM has no persisted non-missing value.");
    }

    private void LogPeLookupQueryScope(
        string? queryText,
        IReadOnlyCollection<string> metricCodes,
        IReadOnlyCollection<string> resolvedCodes,
        IReadOnlyCollection<Guid> companyIds,
        IReadOnlyCollection<Guid> symbolIds,
        IReadOnlyCollection<DerivedMetricRow> derivedRows)
    {
        if (!ContainsPeMetric(metricCodes)) return;

        logger.LogInformation(
            "PE lookup query scope. Query={OriginalUserQuery}; NormalizedMetric=PE_TTM; NormalizedSymbols={ResolvedSymbols}; ResolvedCompanyIds={ResolvedCompanyIds}; CandidateSymbolIds={CandidateSymbolIds}; SqlSource={SqlSource}; DerivedMetricRowsRead={DerivedMetricRowsRead}; NonNullPeTtmRowsRead={NonNullPeTtmRowsRead}",
            queryText,
            string.Join(",", resolvedCodes),
            string.Join(",", companyIds),
            string.Join(",", symbolIds),
            "DerivedMetrics joined by SymbolId from Symbols for matched company ids",
            derivedRows.Count(row => string.Equals(row.MetricCode, "PE_TTM", StringComparison.OrdinalIgnoreCase)),
            derivedRows.Count(row =>
                string.Equals(row.MetricCode, "PE_TTM", StringComparison.OrdinalIgnoreCase) &&
                row.Value is not null));
    }

    private static bool ContainsPeMetric(IReadOnlyCollection<string> metricCodes) =>
        metricCodes.Any(code => string.Equals(code, "PE_TTM", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyCollection<string> BuildMissingDataWarnings(
        IReadOnlyCollection<ScannerTableRow> rows,
        IReadOnlyCollection<string> metricCodes)
    {
        if (!ContainsPeMetric(metricCodes)) return [];

        return rows
            .Where(row =>
                row.Cells.TryGetValue("PE_TTM", out var cell) &&
                (cell.Value is null || cell.FreshnessStatus == CellFreshnessStatus.Missing))
            .Select(row => $"PE_TTM is missing for symbol '{row.SymbolCode}'.")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
