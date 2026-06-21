using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class EfCoreSymbolMetricLookupService(
    FinancialIngestionDbContext dbContext,
    ICompanyResolverService companyResolver,
    IMarketQuoteResolver quoteResolver,
    IDirectMetricRoutingRegistry directMetricRoutingRegistry,
    TimeProvider timeProvider,
    ILogger<EfCoreSymbolMetricLookupService> logger) : ISymbolMetricLookupService
{
    private const string MonthlySales = "MONTHLY_SALES";
    private const string Average12MonthMonthlySales = "AVG_12M_MONTHLY_SALES";
    private const string MonthlySalesYtd = "MONTHLY_SALES_YTD";
    private const string MonthlySalesYtdPreviousMonth = "MONTHLY_SALES_YTD_PREVIOUS_MONTH";
    private const string MonthlySalesPriorFiscalYearSameMonth = "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH";

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

        var requestedPairs = request.Pairs.ToList();

        var uniqueMetricCodes = requestedPairs
            .Select(p => p.MetricCode.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueSymbolNames.Count == 0 || uniqueMetricCodes.Count == 0)
        {
            return BuildEmptyResult(lookupId, uniqueMetricCodes, startTime, [], uniqueSymbolNames);
        }

        var includeSameMonthPreviousYearSales = ShouldIncludeSameMonthPreviousYearSales(
            requestedPairs,
            uniqueMetricCodes,
            request.QueryText);
        var lookupMetricCodes = ExpandPersistedMetricCodes(ExpandLookupMetricCodes(requestedPairs, uniqueMetricCodes))
            .Concat(ShouldIncludeMarketContext(uniqueMetricCodes) ? ["LATEST_PRICE", "DAILY_CHANGE_PCT"] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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

        var derivedRows = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(dm => externalCompanyIds.Contains(dm.ExternalCompanyId) && lookupMetricCodes.Contains(dm.MetricCode))
            .ToListAsync(cancellationToken);

        LogPeLookupQueryScope(
            request.QueryText,
            uniqueMetricCodes,
            externalCompanyIds,
            derivedRows);

        var selectedByCompanyMetric = BuildSelectedMetricRows(
            derivedRows,
            externalCompanyIds,
            lookupMetricCodes,
            requestedPairs);

        var companyRows = await dbContext.Companies.AsNoTracking()
            .Where(c => externalCompanyIds.Contains(c.ExternalCompanyId))
            .ToListAsync(cancellationToken);

        var companyRowByExternalId = companyRows
            .ToDictionary(c => c.ExternalCompanyId, StringComparer.OrdinalIgnoreCase);

        var useCyclicalWavesAverageLayout = ShouldUseCyclicalWavesAverageLayout(
            uniqueMetricCodes,
            includeSameMonthPreviousYearSales,
            externalCompanyIds,
            derivedRows);
        var displayMetricCodes = ExpandDisplayMetricCodes(
            requestedPairs,
            uniqueMetricCodes,
            useCyclicalWavesAverageLayout,
            includeSameMonthPreviousYearSales);
        var includeMarketContext = ShouldIncludeMarketContext(displayMetricCodes);
        var displayNameOverrides = BuildDisplayNameOverrides(requestedPairs);

        var quoteBySymbol = new Dictionary<string, MarketQuoteObservation>(StringComparer.OrdinalIgnoreCase);
        if (includeMarketContext && companyRows.Count > 0)
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

        var columns = BuildLookupColumns(displayMetricCodes, includeMarketContext, displayNameOverrides);
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
                selectedByCompanyMetric,
                derivedRows);

            LogPeLookupResult(
                request.QueryText,
                name,
                resolved.ExternalCompanyId,
                selectedByCompanyMetric,
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
            BuildMissingDataWarnings(rows, displayMetricCodes),
            unresolvedSymbols,
            uniqueMetricCodes);
    }

    private static IReadOnlyCollection<string> ExpandDisplayMetricCodes(
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs,
        IReadOnlyCollection<string> metricCodes,
        bool useAverage12MonthSales,
        bool includeSameMonthPreviousYearSales)
    {
        var expanded = new List<string>();
        foreach (var code in metricCodes)
        {
            if (string.Equals(code, MonthlySales, StringComparison.OrdinalIgnoreCase))
            {
                var explicitSelector = requestPairs.Any(p =>
                    string.Equals(p.MetricCode.Value, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
                    p.PeriodSelector is not null);

                if (explicitSelector)
                {
                    AddIfMissing(expanded, MonthlySales);
                    continue;
                }

                AddIfMissing(expanded, MonthlySales);
                AddIfMissing(expanded, useAverage12MonthSales && !includeSameMonthPreviousYearSales
                    ? Average12MonthMonthlySales
                    : MonthlySalesPriorFiscalYearSameMonth);
                AddIfMissing(expanded, MonthlySalesYtd);
                AddIfMissing(expanded, MonthlySalesYtdPreviousMonth);
                continue;
            }

            AddIfMissing(expanded, code);
        }

        return expanded;
    }

    private static IReadOnlyCollection<string> ExpandLookupMetricCodes(
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs,
        IReadOnlyCollection<string> metricCodes)
    {
        var expanded = new List<string>();
        foreach (var code in metricCodes)
        {
            if (string.Equals(code, MonthlySales, StringComparison.OrdinalIgnoreCase))
            {
                var explicitSelector = requestPairs.Any(p =>
                    string.Equals(p.MetricCode.Value, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
                    p.PeriodSelector is not null);

                if (explicitSelector)
                {
                    AddIfMissing(expanded, MonthlySales);
                    continue;
                }

                AddIfMissing(expanded, MonthlySales);
                AddIfMissing(expanded, Average12MonthMonthlySales);
                AddIfMissing(expanded, MonthlySalesYtd);
                AddIfMissing(expanded, MonthlySalesYtdPreviousMonth);
                continue;
            }

            AddIfMissing(expanded, code);
        }

        return expanded;
    }

    private static bool ShouldUseCyclicalWavesAverageLayout(
        IReadOnlyCollection<string> metricCodes,
        bool includeSameMonthPreviousYearSales,
        IReadOnlyCollection<string> externalCompanyIds,
        IReadOnlyCollection<DerivedMetricRow> derivedRows)
    {
        if (includeSameMonthPreviousYearSales ||
            !metricCodes.Contains(MonthlySales, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var latestMonthlyRows = derivedRows
            .Where(row =>
                externalCompanyIds.Contains(row.ExternalCompanyId, StringComparer.OrdinalIgnoreCase) &&
                string.Equals(row.MetricCode, MonthlySales, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.ExternalCompanyId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.PeriodEnd).First())
            .ToList();

        return latestMonthlyRows.Count > 0 && latestMonthlyRows.All(IsCyclicalWavesMetricRow);
    }

    private static bool IsCyclicalWavesMetricRow(DerivedMetricRow row) =>
        ContainsCyclicalWavesMarker(row.SourceEvidenceJson) ||
        ContainsCyclicalWavesMarker(row.DependencyEvidenceJson) ||
        ContainsCyclicalWavesMarker(row.CalculationPolicyVersion);

    private static bool ContainsCyclicalWavesMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("CyclicalWaves", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> BuildSelectedMetricRows(
        IReadOnlyCollection<DerivedMetricRow> derivedRows,
        IReadOnlyCollection<string> externalCompanyIds,
        IReadOnlyCollection<string> lookupMetricCodes,
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs)
    {
        var selected = new Dictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow>(
            ExternalCompanyMetricKeyComparer.Instance);

        foreach (var externalCompanyId in externalCompanyIds)
        {
            foreach (var metricCode in lookupMetricCodes)
            {
                var selector = requestPairs
                    .FirstOrDefault(pair => string.Equals(pair.MetricCode.Value, metricCode, StringComparison.OrdinalIgnoreCase))
                    ?.PeriodSelector;

                var row = SelectMetricRow(derivedRows, externalCompanyId, metricCode, selector);
                if (row is not null)
                {
                    selected[(externalCompanyId, metricCode)] = row;
                }
            }
        }

        return selected;
    }

    private static DerivedMetricRow? SelectMetricRow(
        IReadOnlyCollection<DerivedMetricRow> derivedRows,
        string externalCompanyId,
        string metricCode,
        SymbolLookupPeriodSelector? selector)
    {
        var candidates = derivedRows
            .Where(row =>
                string.Equals(row.ExternalCompanyId, externalCompanyId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.MetricCode, metricCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.PeriodEnd)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var cyclicalCandidates = candidates
            .Where(IsCyclicalWavesMetricRow)
            .OrderByDescending(row => row.PeriodEnd)
            .ToList();

        if (selector is not null)
        {
            var selectedRows = cyclicalCandidates.Count > 0 ? cyclicalCandidates : candidates;
            var index = selector switch
            {
                SymbolLookupPeriodSelector.LatestQuarter => 0,
                SymbolLookupPeriodSelector.LatestMonth => 0,
                SymbolLookupPeriodSelector.PreviousQuarter => 1,
                SymbolLookupPeriodSelector.PreviousMonth => 1,
                SymbolLookupPeriodSelector.SameQuarterLastYear => 2,
                SymbolLookupPeriodSelector.SameMonthLastYear => 2,
                SymbolLookupPeriodSelector.LastYearAverage12Month => 1,
                _ => 0
            };

            return index < selectedRows.Count ? selectedRows[index] : null;
        }

        if (IsCyclicalWavesPeriodAwareMetric(metricCode) && cyclicalCandidates.Count > 0)
        {
            return cyclicalCandidates[0];
        }

        return candidates[0];
    }

    private static bool IsCyclicalWavesPeriodAwareMetric(string metricCode) =>
        string.Equals(metricCode, MonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, Average12MonthMonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "NET_PROFIT_MARGIN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "GROSS_PROFIT_MARGIN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "OPERATING_PROFIT_MARGIN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "PE_TTM", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "PS_TTM", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string> BuildDisplayNameOverrides(
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in requestPairs)
        {
            if (pair.PeriodSelector is null &&
                !string.Equals(pair.MetricCode.Value, Average12MonthMonthlySales, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pair.MetricCode.Value, "PE_TTM", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(pair.MetricCode.Value, "PS_TTM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            overrides[pair.MetricCode.Value] = pair.DisplayLabel ??
                directMetricRoutingRegistry.ResolveDisplayLabel(pair.MetricCode, pair.PeriodSelector);
        }

        return overrides;
    }

    private static bool ShouldIncludeSameMonthPreviousYearSales(
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs,
        IReadOnlyCollection<string> metricCodes,
        string? queryText)
    {
        if (metricCodes.Contains(MonthlySalesPriorFiscalYearSameMonth, StringComparer.OrdinalIgnoreCase))
            return true;

        if (requestPairs.Any(p =>
                string.Equals(p.MetricCode.Value, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
                p.PeriodSelector == SymbolLookupPeriodSelector.SameMonthLastYear))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(queryText))
            return false;

        var normalized = NormalizePersianText(queryText);
        return normalized.Contains("مشابه", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Contains("دوره قبل", StringComparison.OrdinalIgnoreCase) ||
             normalized.Contains("سال قبل", StringComparison.OrdinalIgnoreCase) ||
             normalized.Contains("مدت مشابه", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePersianText(string text) =>
        text.Replace('ي', 'ی').Replace('ك', 'ک');

    private static IReadOnlyCollection<string> ExpandPersistedMetricCodes(IReadOnlyCollection<string> metricCodes)
    {
        var expanded = new List<string>();
        foreach (var code in metricCodes)
        {
            AddIfMissing(expanded,
                string.Equals(code, MonthlySalesPriorFiscalYearSameMonth, StringComparison.OrdinalIgnoreCase)
                    ? MonthlySales
                    : code);
        }

        return expanded;
    }

    private static bool ShouldIncludeMarketContext(IReadOnlyCollection<string> metricCodes) =>
        !metricCodes.All(IsMonthlyActivityMetric);

    private static bool IsMonthlyActivityMetric(string metricCode) =>
        string.Equals(metricCode, MonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, Average12MonthMonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesYtd, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesYtdPreviousMonth, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesPriorFiscalYearSameMonth, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "MONTHLY_SALES_QUANTITY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "MONTHLY_PRODUCTION_QUANTITY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "MONTHLY_SALES_RATE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "MONTHLY_SALES_GROWTH_YOY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "MONTHLY_SALES_GROWTH_MOM", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, "TTM_SALES", StringComparison.OrdinalIgnoreCase);

    private static void AddIfMissing(List<string> metricCodes, string metricCode)
    {
        if (!metricCodes.Contains(metricCode, StringComparer.OrdinalIgnoreCase))
        {
            metricCodes.Add(metricCode);
        }
    }

    private static IReadOnlyCollection<ScannerTableColumn> BuildLookupColumns(
        IEnumerable<string> metricCodes,
        bool includeMarketContext,
        IReadOnlyDictionary<string, string> displayNameOverrides)
    {
        var columns = new List<ScannerTableColumn>
        {
            new("SYMBOL", "نماد", ScannerColumnType.Symbol),
            new("COMPANY_NAME", "شرکت", ScannerColumnType.CompanyName)
        };

        if (includeMarketContext)
        {
            columns.Add(new("LATEST_PRICE", "آخرین قیمت", ScannerColumnType.LatestPrice));
            columns.Add(new("DAILY_CHANGE_PCT", "تغییر روزانه %", ScannerColumnType.DailyChangePercent));
        }

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
                columns.Add(new ScannerTableColumn(
                    code,
                    displayNameOverrides.TryGetValue(code, out var displayName)
                        ? displayName
                        : FormatPersianMetricDisplayName(code),
                    ScannerColumnType.Metric,
                    code));
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
            "MONTHLY SALES PRIOR FISCAL YEAR SAME MONTH" => "فروش ماه مشابه دوره قبل",
            "AVG 12M MONTHLY SALES" => "متوسط فروش ۱۲ ماهه",
            "MONTHLY SALES YTD" => "فروش YTD",
            "MONTHLY SALES YTD PREVIOUS MONTH" => "فروش YTD تا ماه قبل",
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
        IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> latestByCompanyMetric,
        IReadOnlyCollection<DerivedMetricRow> derivedRows)
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

                ScannerColumnType.Metric when string.Equals(column.MetricCode, MonthlySalesPriorFiscalYearSameMonth, StringComparison.OrdinalIgnoreCase) =>
                    BuildPriorFiscalYearSameMonthCell(externalCompanyId, derivedRows, FormatMillionRials),

                ScannerColumnType.Metric when column.MetricCode is not null =>
                    BuildPersistedMetricCell(
                        externalCompanyId,
                        column.MetricCode,
                        latestByCompanyMetric,
                        IsMonthlySalesMonetaryMetric(column.MetricCode)
                            ? FormatMillionRials
                            : v => FinancialNumberFormatter.Metric(column.MetricCode, v)),

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
                FinancialNumberFormatter.Whole(quote.LatestPrice),
                freshness,
                quote.AsOf,
                quote.TradingDate,
                FormatPersianDate(quote.TradingDate),
                quote.SourceLabel);
        }

        if (latestByCompanyMetric.TryGetValue((externalCompanyId, "LATEST_PRICE"), out var row) && row.Value is not null)
        {
            return new ScannerTableCell(
                row.Value,
                FinancialNumberFormatter.Whole(row.Value.Value),
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
                FinancialNumberFormatter.SignedPercent(quote.PriceChangePercentage),
                freshness,
                quote.AsOf,
                quote.TradingDate,
                FormatPersianDate(quote.TradingDate),
                quote.SourceLabel);
        }

        if (latestByCompanyMetric.TryGetValue((externalCompanyId, "DAILY_CHANGE_PCT"), out var row) && row.Value is not null)
        {
            return new ScannerTableCell(
                row.Value,
                FinancialNumberFormatter.SignedPercent(row.Value.Value),
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

    private static ScannerTableCell BuildPriorFiscalYearSameMonthCell(
        string externalCompanyId,
        IReadOnlyCollection<DerivedMetricRow> derivedRows,
        Func<decimal, string> formatter)
    {
        var current = derivedRows
            .Where(row =>
                string.Equals(row.ExternalCompanyId, externalCompanyId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.MetricCode, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
                row.Value is not null)
            .OrderByDescending(row => row.PeriodEnd)
            .FirstOrDefault();

        if (current is null)
        {
            return new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null);
        }

        var calendar = new PersianCalendar();
        var currentDate = current.PeriodEnd.ToDateTime(TimeOnly.MinValue);
        var targetPersianYear = calendar.GetYear(currentDate) - 1;
        var targetPersianMonth = calendar.GetMonth(currentDate);

        var prior = derivedRows
            .Where(row =>
                string.Equals(row.ExternalCompanyId, externalCompanyId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.MetricCode, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
                row.Value is not null &&
                IsPersianYearMonth(row.PeriodEnd, targetPersianYear, targetPersianMonth))
            .OrderByDescending(row => row.PeriodEnd)
            .FirstOrDefault();

        if (prior?.Value is null)
        {
            return new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null);
        }

        return new ScannerTableCell(
            prior.Value,
            formatter(prior.Value.Value),
            CellFreshnessStatus.Persisted,
            prior.ObservedAt);
    }

    private static bool IsPersianYearMonth(DateOnly date, int persianYear, int persianMonth)
    {
        var calendar = new PersianCalendar();
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return calendar.GetYear(dateTime) == persianYear &&
            calendar.GetMonth(dateTime) == persianMonth;
    }

    private static bool IsMonthlySalesMonetaryMetric(string metricCode) =>
        string.Equals(metricCode, MonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, Average12MonthMonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesYtd, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesYtdPreviousMonth, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesPriorFiscalYearSameMonth, StringComparison.OrdinalIgnoreCase);

    private static string FormatMillionRials(decimal value) =>
        FinancialNumberFormatter.Whole(value / 1_000_000m);

    private static string FormatPersianDate(DateOnly date)
    {
        var calendar = new PersianCalendar();
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return $"{calendar.GetYear(dateTime):0000}/{calendar.GetMonth(dateTime):00}/{calendar.GetDayOfMonth(dateTime):00}";
    }

    private SymbolLookupTableResult BuildEmptyResult(
        Guid lookupId,
        IEnumerable<string> metricCodes,
        DateTimeOffset startTime,
        IReadOnlyCollection<string> unresolvedSymbols,
        IReadOnlyCollection<string> requestedSymbolNames)
    {
        var endTime = timeProvider.GetUtcNow();
        var displayMetricCodes = ExpandDisplayMetricCodes(
            [],
            metricCodes.ToList(),
            useAverage12MonthSales: false,
            includeSameMonthPreviousYearSales: false);
        return new SymbolLookupTableResult(
            lookupId,
            BuildLookupColumns(
                displayMetricCodes,
                ShouldIncludeMarketContext(displayMetricCodes),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
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
            unresolvedSymbols,
            metricCodes.ToList());
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

    private static string FormatLargeNumber(decimal value) => FinancialNumberFormatter.LargeNumber(value);

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
        var warnings = new List<string>();
        foreach (var metricCode in metricCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            warnings.AddRange(rows
                .Where(row =>
                    row.Cells.TryGetValue(metricCode, out var cell) &&
                    (cell.Value is null || cell.FreshnessStatus == CellFreshnessStatus.Missing))
                .Select(row => $"{metricCode} is missing for symbol '{row.SymbolCode}'."));
        }

        return warnings
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
