using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class EfCoreSymbolMetricLookupService(
    FinancialIngestionDbContext dbContext,
    ICompanyResolverService companyResolver,
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
        var resolvedByName = new Dictionary<string, ResolvedCompany>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in uniqueSymbolNames)
        {
            var resolved = await companyResolver.ResolveBySymbolAsync(name, cancellationToken);
            if (resolved is not null)
            {
                resolvedByName[name] = resolved;
                LogPeLookupResolution(request.QueryText, name, resolved.ExternalCompanyId, uniqueMetricCodes);
            }
            else
            {
                unresolvedSymbols.Add(name);
                LogPeLookupMissing(
                    request.QueryText,
                    name,
                    null,
                    "SymbolResolutionFailed",
                    uniqueMetricCodes);
            }
        }

        if (resolvedByName.Count == 0)
        {
            return BuildEmptyResult(lookupId, uniqueMetricCodes, startTime, unresolvedSymbols, uniqueSymbolNames);
        }

        var externalCompanyIds = resolvedByName.Values
            .Select(r => r.ExternalCompanyId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lookupMetricCodes = uniqueMetricCodes
            .Concat(["LATEST_PRICE", "DAILY_CHANGE_PCT"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var derivedRows = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(dm => externalCompanyIds.Contains(dm.ExternalCompanyId) && lookupMetricCodes.Contains(dm.MetricCode))
            .ToListAsync(cancellationToken);

        LogPeLookupQueryScope(
            request.QueryText,
            uniqueMetricCodes,
            externalCompanyIds,
            derivedRows);

        var latestByCompanyMetric = derivedRows
            .GroupBy(dm => (dm.ExternalCompanyId, dm.MetricCode), ExternalCompanyMetricKeyComparer.Instance)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(dm => dm.PeriodEnd).First(),
                ExternalCompanyMetricKeyComparer.Instance);

        var companyRows = await dbContext.Companies.AsNoTracking()
            .Where(c => externalCompanyIds.Contains(c.ExternalCompanyId))
            .ToListAsync(cancellationToken);

        var companyRowByExternalId = companyRows
            .ToDictionary(c => c.ExternalCompanyId, StringComparer.OrdinalIgnoreCase);

        var quoteBySymbol = new Dictionary<string, MarketQuoteObservation>(StringComparer.OrdinalIgnoreCase);
        if (companyRows.Count > 0)
        {
            try
            {
                // Use Ticker (Persian) for quote resolution; fall back to TseSymbol/CompanySymbol.
                var symbolCodes = companyRows
                    .Select(c => CompanyDisplayResolver.FirstNonBlank(c.Ticker, c.TseSymbol, c.CompanySymbol, c.ExternalCompanyId))
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(s => new Domain.Financial.ValueObjects.SymbolCode(s!))
                    .ToList();
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
            if (!resolvedByName.TryGetValue(name, out var resolved)) continue;

            companyRowByExternalId.TryGetValue(resolved.ExternalCompanyId, out var companyRow);
            var displaySymbol = CompanyDisplayResolver.FirstNonBlank(
                companyRow?.TseSymbol,
                resolved.Ticker,
                companyRow?.CompanySymbol,
                resolved.ExternalCompanyId) ?? resolved.ExternalCompanyId;
            var companyName = companyRow?.Name;

            var quote = ResolveQuoteForCompany(resolved, companyRow, quoteBySymbol);
            var cells = BuildCells(
                columns,
                resolved.ExternalCompanyId,
                displaySymbol,
                companyName,
                quote,
                latestByCompanyMetric);

            LogPeLookupResult(
                request.QueryText,
                name,
                resolved.ExternalCompanyId,
                latestByCompanyMetric,
                cells);

            rows.Add(new ScannerTableRow(
                displaySymbol,
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
            BuildMissingDataWarnings(rows, uniqueMetricCodes),
            unresolvedSymbols);
    }

    private static IReadOnlyCollection<ScannerTableColumn> BuildLookupColumns(
        IEnumerable<string> metricCodes)
    {
        var columns = new List<ScannerTableColumn>
        {
            new("SYMBOL", "نماد", ScannerColumnType.Symbol),
            new("COMPANY_NAME", "شرکت", ScannerColumnType.CompanyName),
            new("LATEST_PRICE", "آخرین قیمت", ScannerColumnType.LatestPrice),
            new("DAILY_CHANGE_PCT", "تغییر روزانه %", ScannerColumnType.DailyChangePercent)
        };

        var seen = columns
            .Select(c => c.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var code in metricCodes)
        {
            if (!seen.Add(code))
                continue;

            if (string.Equals(code, "MARKET_CAP", StringComparison.OrdinalIgnoreCase))
                columns.Add(new ScannerTableColumn("MARKET_CAP", "ارزش بازار", ScannerColumnType.MarketCap));
            else
                columns.Add(new ScannerTableColumn(code, FormatPersianMetricDisplayName(code), ScannerColumnType.Metric, code));
        }

        return columns;
    }

    private static string FormatPersianMetricDisplayName(string metricCode) =>
        metricCode.Replace("_", " ").ToUpperInvariant() switch
        {
            "PE TTM" => "P/E دوازده‌ماهه",
            "PS TTM" => "P/S دوازده‌ماهه",
            "NET PROFIT GROWTH YOY" => "رشد سالانه سود خالص",
            "NET PROFIT GROWTH QOQ" => "رشد فصلی سود خالص",
            "MONTHLY SALES GROWTH YOY" => "رشد سالانه فروش",
            "MONTHLY SALES GROWTH MOM" => "رشد ماهانه فروش",
            "TTM EARNINGS" => "سود دوازده‌ماهه",
            "TTM SALES" => "فروش دوازده‌ماهه",
            "TTM EPS" => "EPS دوازده‌ماهه",
            "LATEST PRICE" => "آخرین قیمت",
            "NET PROFIT" => "سود خالص",
            "NET PROFIT MARGIN" => "حاشیه سود خالص",
            "GROSS PROFIT MARGIN" => "حاشیه سود ناخالص",
            "OPERATING PROFIT MARGIN" => "حاشیه سود عملیاتی",
            "MONTHLY SALES" => "فروش ماهانه",
            "MONTHLY PRODUCTION QUANTITY" => "تولید ماهانه",
            "EPS" => "EPS",
            "ROE" => "ROE",
            "ROA" => "ROA",
            _ => metricCode
        };

    private static IReadOnlyDictionary<string, ScannerTableCell> BuildCells(
        IReadOnlyCollection<ScannerTableColumn> columns,
        string externalCompanyId,
        string displaySymbol,
        string? companyName,
        MarketQuoteObservation? quote,
        IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric)
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
                    BuildPriceCell(externalCompanyId, quote, latestByCompanyMetric),

                ScannerColumnType.DailyChangePercent =>
                    BuildChangeCell(externalCompanyId, quote, latestByCompanyMetric),

                ScannerColumnType.MarketCap =>
                    BuildPersistedMetricCell(externalCompanyId, "MARKET_CAP", latestByCompanyMetric, FormatLargeNumber),

                ScannerColumnType.Metric when column.MetricCode is not null =>
                    BuildPersistedMetricCell(externalCompanyId, column.MetricCode, latestByCompanyMetric, v => v.ToString("N2")),

                _ => new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null)
            };
        }

        return cells;
    }

    private static ScannerTableCell BuildPriceCell(
        string externalCompanyId,
        MarketQuoteObservation? quote,
        IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric)
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

        if (latestByCompanyMetric.TryGetValue((externalCompanyId, "LATEST_PRICE"), out var row) && row.Value is not null)
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
        string externalCompanyId,
        MarketQuoteObservation? quote,
        IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric)
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

        if (latestByCompanyMetric.TryGetValue((externalCompanyId, "DAILY_CHANGE_PCT"), out var row) && row.Value is not null)
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
        string externalCompanyId,
        string metricCode,
        IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric,
        Func<decimal, string> formatter)
    {
        if (!latestByCompanyMetric.TryGetValue((externalCompanyId, metricCode), out var row) || row.Value is null)
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
        ResolvedCompany resolved,
        NormalizedCompanyRow? companyRow,
        IReadOnlyDictionary<string, MarketQuoteObservation> quoteBySymbol)
    {
        // Try Persian ticker first, then TseSymbol, CompanySymbol, ExternalCompanyId
        var candidates = new[]
        {
            resolved.Ticker,
            companyRow?.TseSymbol,
            companyRow?.CompanySymbol,
            resolved.ExternalCompanyId
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && quoteBySymbol.TryGetValue(candidate!, out var quote))
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
        string externalCompanyId,
        IReadOnlyCollection<string> metricCodes)
    {
        if (!ContainsPeMetric(metricCodes)) return;

        logger.LogInformation(
            "PE lookup symbol resolved. Query={OriginalUserQuery}; DetectedSymbol={DetectedSymbol}; ResolvedExternalCompanyId={ResolvedExternalCompanyId}; RequestedMetric=PE_TTM",
            queryText,
            detectedSymbol,
            externalCompanyId);
    }

    private void LogPeLookupResult(
        string? queryText,
        string detectedSymbol,
        string externalCompanyId,
        IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric,
        IReadOnlyDictionary<string, ScannerTableCell> cells)
    {
        if (!cells.TryGetValue("PE_TTM", out var cell))
            return;

        latestByCompanyMetric.TryGetValue((externalCompanyId, "PE_TTM"), out var rawRow);

        if (cell.Value is not null && cell.FreshnessStatus != CellFreshnessStatus.Missing)
        {
            logger.LogInformation(
                "PE lookup value retrieved. Query={OriginalUserQuery}; NormalizedMetric=PE_TTM; DetectedSymbol={DetectedSymbol}; ResolvedExternalCompanyId={ResolvedExternalCompanyId}; SqlSource={SqlSource}; RawPeTtmValue={RawPeTtmValue}; Freshness={Freshness}; ConfidenceDecisionReason={ConfidenceDecisionReason}",
                queryText,
                detectedSymbol,
                externalCompanyId,
                "DerivedMetrics by ExternalCompanyId",
                rawRow?.Value ?? cell.Value,
                cell.FreshnessStatus,
                "PreCalculatedMetric because PE_TTM has a persisted non-missing value.");
            return;
        }

        var missingReason = rawRow is null
            ? "NoDerivedMetricRowForResolvedExternalCompanyId"
            : "DerivedMetricValueNull";

        LogPeLookupMissing(
            queryText,
            detectedSymbol,
            externalCompanyId,
            missingReason,
            ["PE_TTM"]);
    }

    private void LogPeLookupMissing(
        string? queryText,
        string detectedSymbol,
        string? externalCompanyId,
        string reason,
        IReadOnlyCollection<string> metricCodes)
    {
        if (!ContainsPeMetric(metricCodes)) return;

        logger.LogWarning(
            "PE lookup value missing. Query={OriginalUserQuery}; NormalizedMetric=PE_TTM; DetectedSymbol={DetectedSymbol}; ResolvedExternalCompanyId={ResolvedExternalCompanyId}; SqlSource={SqlSource}; MissingReason={MissingReason}; ConfidenceDecisionReason={ConfidenceDecisionReason}",
            queryText,
            detectedSymbol,
            externalCompanyId,
            "DerivedMetrics by ExternalCompanyId",
            reason,
            "MissingDataFallback because PE_TTM has no persisted non-missing value.");
    }

    private void LogPeLookupQueryScope(
        string? queryText,
        IReadOnlyCollection<string> metricCodes,
        IReadOnlyCollection<string> externalCompanyIds,
        IReadOnlyCollection<DerivedMetricRow> derivedRows)
    {
        if (!ContainsPeMetric(metricCodes)) return;

        logger.LogInformation(
            "PE lookup query scope. Query={OriginalUserQuery}; NormalizedMetric=PE_TTM; ResolvedExternalCompanyIds={ResolvedExternalCompanyIds}; SqlSource={SqlSource}; DerivedMetricRowsRead={DerivedMetricRowsRead}; NonNullPeTtmRowsRead={NonNullPeTtmRowsRead}",
            queryText,
            string.Join(",", externalCompanyIds),
            "DerivedMetrics by ExternalCompanyId",
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

    private sealed class ExternalCompanyMetricKeyComparer : IEqualityComparer<(string ExternalCompanyId, string MetricCode)>
    {
        public static readonly ExternalCompanyMetricKeyComparer Instance = new();

        public bool Equals((string ExternalCompanyId, string MetricCode) x, (string ExternalCompanyId, string MetricCode) y) =>
            string.Equals(x.ExternalCompanyId, y.ExternalCompanyId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.MetricCode, y.MetricCode, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ExternalCompanyId, string MetricCode) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ExternalCompanyId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MetricCode));
    }
}
