using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public interface ISnapshotMonthlyActivitySymbolMetricLookupService
{
    Task<SymbolLookupTableResult> LookupAsync(
        SymbolLookupRequest request,
        CancellationToken cancellationToken);
}

public sealed class SnapshotMonthlyActivitySymbolMetricLookupService(
    ICompanyMonthlyActivityTrendSnapshotRepository repository,
    ICompanyResolverService companyResolver,
    IDirectMetricRoutingRegistry directMetricRoutingRegistry,
    TimeProvider timeProvider)
    : ISnapshotMonthlyActivitySymbolMetricLookupService
{
    private const string MonthlySales = "MONTHLY_SALES";
    private const string Average12MonthMonthlySales = "AVG_12M_MONTHLY_SALES";
    private const string MonthlySalesYtd = "MONTHLY_SALES_YTD";
    private const string MonthlySalesYtdPreviousMonth = "MONTHLY_SALES_YTD_PREVIOUS_MONTH";
    private const string MonthlySalesPriorFiscalYearSameMonth = "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH";
    private const string MonthlySalesQuantity = "MONTHLY_SALES_QUANTITY";
    private const string MonthlyProductionQuantity = "MONTHLY_PRODUCTION_QUANTITY";
    private const string MonthlySalesRate = "MONTHLY_SALES_RATE";
    private const string MonthlySalesGrowthYoy = "MONTHLY_SALES_GROWTH_YOY";
    private const string MonthlySalesGrowthMom = "MONTHLY_SALES_GROWTH_MOM";
    private const string MonthlyProductionGrowthYoy = "MONTHLY_PRODUCTION_GROWTH_YOY";
    private const string MonthlySalesQuantityGrowthYoy = "MONTHLY_SALES_QUANTITY_GROWTH_YOY";
    private const string MonthlySalesToProductionRatio = "MONTHLY_SALES_TO_PRODUCTION_RATIO";

    private static readonly HashSet<string> SnapshotSupportedMetricCodes =
    [
        MonthlySales,
        Average12MonthMonthlySales,
        MonthlySalesYtd,
        MonthlySalesYtdPreviousMonth,
        MonthlySalesPriorFiscalYearSameMonth,
        MonthlySalesQuantity,
        MonthlyProductionQuantity,
        MonthlySalesRate,
        MonthlySalesGrowthYoy,
        MonthlySalesGrowthMom,
        MonthlyProductionGrowthYoy,
        MonthlySalesQuantityGrowthYoy,
        MonthlySalesToProductionRatio
    ];

    public static bool Supports(SymbolLookupRequest request)
    {
        if (request.Pairs.Count == 0)
        {
            return false;
        }

        foreach (var pair in request.Pairs)
        {
            if (!SnapshotSupportedMetricCodes.Contains(pair.MetricCode.Value))
            {
                return false;
            }

            if (pair.PeriodSelector is SymbolLookupPeriodSelector.LastYearAverage12Month)
            {
                return false;
            }
        }

        return true;
    }

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
        var requestedMetricCodes = requestedPairs
            .Select(p => p.MetricCode.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueSymbolNames.Count == 0 || requestedMetricCodes.Count == 0)
        {
            return BuildEmptyResult(lookupId, requestedMetricCodes, startTime, [], uniqueSymbolNames);
        }

        var resolvedByName = new Dictionary<string, ResolvedCompany>(StringComparer.OrdinalIgnoreCase);
        var unresolvedSymbols = new List<string>();

        foreach (var symbolName in uniqueSymbolNames)
        {
            var resolved = await companyResolver.ResolveBySymbolAsync(symbolName, cancellationToken);
            if (resolved is null)
            {
                unresolvedSymbols.Add(symbolName);
                continue;
            }

            resolvedByName[symbolName] = resolved;
        }

        if (resolvedByName.Count == 0)
        {
            return BuildEmptyResult(lookupId, requestedMetricCodes, startTime, unresolvedSymbols, uniqueSymbolNames);
        }

        var snapshotsByExternalId = new Dictionary<string, IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>>(StringComparer.OrdinalIgnoreCase);
        foreach (var externalCompanyId in resolvedByName.Values
                     .Select(v => v.ExternalCompanyId)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var snapshots = await repository.GetLatestAvailablePeriodsAsync(externalCompanyId, 2, cancellationToken);
            snapshotsByExternalId[externalCompanyId] = snapshots;
        }

        var displayMetricCodes = ExpandDisplayMetricCodes(requestedPairs, requestedMetricCodes);
        var displayNameOverrides = BuildDisplayNameOverrides(requestedPairs);
        var columns = BuildColumns(displayMetricCodes, displayNameOverrides);
        var rows = new List<ScannerTableRow>();

        foreach (var symbolName in uniqueSymbolNames)
        {
            if (!resolvedByName.TryGetValue(symbolName, out var resolved))
            {
                continue;
            }

            snapshotsByExternalId.TryGetValue(resolved.ExternalCompanyId, out var companySnapshots);
            companySnapshots ??= [];

            var latest = companySnapshots.FirstOrDefault();
            var previous = companySnapshots.Skip(1).FirstOrDefault();
            var displaySymbol = latest?.CompanySymbol ?? resolved.Ticker ?? resolved.ExternalCompanyId;
            var companyName = latest?.CompanyName;

            rows.Add(new ScannerTableRow(
                displaySymbol,
                companyName,
                BuildCells(columns, latest, previous, requestedPairs, request.QueryText, displaySymbol, companyName),
                Score: 1.0,
                []));
        }

        var endTime = timeProvider.GetUtcNow();
        var matchingSymbolCount = rows.Count(r =>
            r.Cells.Values.Any(c => c.FreshnessStatus != CellFreshnessStatus.Missing));

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
            BuildMissingDataWarnings(rows, columns),
            unresolvedSymbols,
            requestedMetricCodes);
    }

    private IReadOnlyDictionary<string, string> BuildDisplayNameOverrides(
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in requestPairs)
        {
            if (pair.DisplayLabel is not null)
            {
                overrides[pair.MetricCode.Value] = pair.DisplayLabel;
                continue;
            }

            overrides[pair.MetricCode.Value] =
                directMetricRoutingRegistry.ResolveDisplayLabel(pair.MetricCode, pair.PeriodSelector);
        }

        return overrides;
    }

    private static IReadOnlyCollection<string> ExpandDisplayMetricCodes(
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs,
        IReadOnlyCollection<string> metricCodes)
    {
        var expanded = new List<string>();
        foreach (var code in metricCodes)
        {
            if (!string.Equals(code, MonthlySales, StringComparison.OrdinalIgnoreCase))
            {
                AddIfMissing(expanded, code);
                continue;
            }

            var explicitSelector = requestPairs.Any(p =>
                string.Equals(p.MetricCode.Value, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
                p.PeriodSelector is not null);

            if (explicitSelector)
            {
                AddIfMissing(expanded, MonthlySales);
                continue;
            }

            AddIfMissing(expanded, MonthlySales);
            AddIfMissing(expanded, MonthlySalesPriorFiscalYearSameMonth);
            AddIfMissing(expanded, MonthlySalesYtd);
            AddIfMissing(expanded, MonthlySalesYtdPreviousMonth);
        }

        return expanded;
    }

    private static IReadOnlyCollection<ScannerTableColumn> BuildColumns(
        IEnumerable<string> metricCodes,
        IReadOnlyDictionary<string, string> displayNameOverrides)
    {
        var columns = new List<ScannerTableColumn>
        {
            new("SYMBOL", "نماد", ScannerColumnType.Symbol),
            new("COMPANY_NAME", "شرکت", ScannerColumnType.CompanyName)
        };

        foreach (var metricCode in metricCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            columns.Add(new ScannerTableColumn(
                metricCode,
                displayNameOverrides.TryGetValue(metricCode, out var displayName)
                    ? displayName
                    : FormatPersianMetricDisplayName(metricCode),
                ScannerColumnType.Metric,
                metricCode));
        }

        return columns;
    }

    private static IReadOnlyDictionary<string, ScannerTableCell> BuildCells(
        IReadOnlyCollection<ScannerTableColumn> columns,
        CompanyMonthlyActivityTrendSnapshot? latest,
        CompanyMonthlyActivityTrendSnapshot? previous,
        IReadOnlyCollection<SymbolLookupRequestPair> requestPairs,
        string? queryText,
        string displaySymbol,
        string? companyName)
    {
        var cells = new Dictionary<string, ScannerTableCell>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (column.ColumnType == ScannerColumnType.Symbol)
            {
                cells[column.Identifier] = new ScannerTableCell(null, displaySymbol, CellFreshnessStatus.Persisted, latest?.CalculatedAtUtc);
                continue;
            }

            if (column.ColumnType == ScannerColumnType.CompanyName)
            {
                cells[column.Identifier] = new ScannerTableCell(null, companyName, CellFreshnessStatus.Persisted, latest?.CalculatedAtUtc);
                continue;
            }

            var metricCode = column.MetricCode ?? column.Identifier;
            var requestPair = requestPairs.FirstOrDefault(p =>
                string.Equals(p.MetricCode.Value, metricCode, StringComparison.OrdinalIgnoreCase)) ??
                requestPairs.FirstOrDefault(p =>
                    string.Equals(p.MetricCode.Value, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(metricCode, MonthlySalesPriorFiscalYearSameMonth, StringComparison.OrdinalIgnoreCase));

            cells[column.Identifier] = BuildMetricCell(
                metricCode,
                latest,
                previous,
                requestPair?.PeriodSelector,
                queryText);
        }

        return cells;
    }

    private static ScannerTableCell BuildMetricCell(
        string metricCode,
        CompanyMonthlyActivityTrendSnapshot? latest,
        CompanyMonthlyActivityTrendSnapshot? previous,
        SymbolLookupPeriodSelector? selector,
        string? queryText)
    {
        if (latest is null)
        {
            return MissingCell();
        }

        var effectiveSelector = selector;
        if (effectiveSelector is null &&
            string.Equals(metricCode, MonthlySales, StringComparison.OrdinalIgnoreCase) &&
            LooksLikeSameMonthPreviousYearQuery(queryText))
        {
            effectiveSelector = SymbolLookupPeriodSelector.SameMonthLastYear;
        }

        var value = (metricCode.ToUpperInvariant(), effectiveSelector) switch
        {
            (MonthlySales, SymbolLookupPeriodSelector.PreviousMonth) => previous?.MonthlySalesAmount,
            (MonthlySales, SymbolLookupPeriodSelector.SameMonthLastYear) => latest.SameMonthPreviousYearSalesAmount,
            (MonthlySales, _) => latest.MonthlySalesAmount,
            (MonthlySalesPriorFiscalYearSameMonth, _) => latest.SameMonthPreviousYearSalesAmount,
            (Average12MonthMonthlySales, _) => latest.Average12MonthSalesAmount,
            (MonthlySalesYtd, _) => latest.YtdSalesAmount,
            (MonthlySalesYtdPreviousMonth, _) => latest.YtdPreviousMonthSalesAmount,
            (MonthlySalesQuantity, _) => latest.MonthlySalesQuantity,
            (MonthlyProductionQuantity, _) => latest.MonthlyProductionQuantity,
            (MonthlySalesRate, _) => latest.MonthlyAverageSalesRate,
            (MonthlySalesGrowthYoy, _) => latest.SalesAmountYoYGrowthPercent,
            (MonthlySalesGrowthMom, _) => latest.SalesAmountMomGrowthPercent,
            (MonthlyProductionGrowthYoy, _) => latest.ProductionQuantityYoYGrowthPercent,
            (MonthlySalesQuantityGrowthYoy, _) => latest.SalesQuantityYoYGrowthPercent,
            (MonthlySalesToProductionRatio, _) => BuildSalesToProductionRatio(latest),
            _ => null
        };

        if (value is null)
        {
            return MissingCell();
        }

        return new ScannerTableCell(
            value,
            IsMonthlySalesMonetaryMetric(metricCode)
                ? FormatMillionRials(value.Value)
                : FinancialNumberFormatter.Metric(metricCode, value.Value),
            CellFreshnessStatus.Persisted,
            latest.CalculatedAtUtc,
            null,
            null,
            latest.SourceProviderName);
    }

    private static decimal? BuildSalesToProductionRatio(CompanyMonthlyActivityTrendSnapshot latest)
    {
        if (!latest.MonthlySalesQuantity.HasValue ||
            !latest.MonthlyProductionQuantity.HasValue ||
            latest.MonthlyProductionQuantity.Value == 0)
        {
            return null;
        }

        return latest.MonthlySalesQuantity.Value / latest.MonthlyProductionQuantity.Value;
    }

    private static bool LooksLikeSameMonthPreviousYearQuery(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return false;
        }

        return queryText.Contains("ماه مشابه", StringComparison.OrdinalIgnoreCase) &&
               (queryText.Contains("سال قبل", StringComparison.OrdinalIgnoreCase) ||
                queryText.Contains("دوره قبل", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMonthlySalesMonetaryMetric(string metricCode) =>
        string.Equals(metricCode, MonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesPriorFiscalYearSameMonth, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, Average12MonthMonthlySales, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesYtd, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode, MonthlySalesYtdPreviousMonth, StringComparison.OrdinalIgnoreCase);

    private static string FormatMillionRials(decimal value) =>
        value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private static ScannerTableCell MissingCell() => new(null, null, CellFreshnessStatus.Missing, null);

    private static IReadOnlyCollection<string> BuildMissingDataWarnings(
        IReadOnlyCollection<ScannerTableRow> rows,
        IReadOnlyCollection<ScannerTableColumn> columns)
    {
        var warnings = new List<string>();
        foreach (var column in columns.Where(c => c.ColumnType == ScannerColumnType.Metric))
        {
            var allMissing = rows.All(row =>
                !row.Cells.TryGetValue(column.Identifier, out var cell) ||
                cell.FreshnessStatus == CellFreshnessStatus.Missing ||
                string.IsNullOrWhiteSpace(cell.FormattedValue));

            if (allMissing)
            {
                warnings.Add($"No data available for {column.DisplayName}.");
            }
        }

        return warnings;
    }

    private static SymbolLookupTableResult BuildEmptyResult(
        Guid lookupId,
        IReadOnlyCollection<string> requestedMetricCodes,
        DateTimeOffset startTime,
        IReadOnlyCollection<string> unresolvedSymbols,
        IReadOnlyCollection<string> uniqueSymbolNames)
    {
        var endTime = startTime;
        return new SymbolLookupTableResult(
            lookupId,
            [
                new ScannerTableColumn("SYMBOL", "نماد", ScannerColumnType.Symbol),
                new ScannerTableColumn("COMPANY_NAME", "شرکت", ScannerColumnType.CompanyName)
            ],
            [],
            new ScannerExecutionFacts(
                endTime,
                TimeSpan.Zero,
                TotalSymbolsEvaluated: uniqueSymbolNames.Count,
                MatchingSymbolCount: 0,
                FromCache: false,
                Page: 1,
                PageSize: 1,
                TotalPages: 1),
            [],
            unresolvedSymbols,
            requestedMetricCodes);
    }

    private static void AddIfMissing(List<string> metricCodes, string metricCode)
    {
        if (!metricCodes.Contains(metricCode, StringComparer.OrdinalIgnoreCase))
        {
            metricCodes.Add(metricCode);
        }
    }

    private static string FormatPersianMetricDisplayName(string metricCode) =>
        metricCode.ToUpperInvariant() switch
        {
            MonthlySales => "فروش ماهانه",
            Average12MonthMonthlySales => "متوسط فروش ۱۲ ماهه",
            MonthlySalesYtd => "فروش YTD",
            MonthlySalesYtdPreviousMonth => "فروش YTD تا ماه قبل",
            MonthlySalesPriorFiscalYearSameMonth => "فروش ماه مشابه سال قبل",
            MonthlySalesQuantity => "مقدار فروش ماهانه",
            MonthlyProductionQuantity => "تولید ماهانه",
            MonthlySalesRate => "نرخ فروش ماهانه",
            MonthlySalesGrowthYoy => "رشد سالانه فروش",
            MonthlySalesGrowthMom => "رشد ماهانه فروش",
            MonthlyProductionGrowthYoy => "رشد سالانه تولید",
            MonthlySalesQuantityGrowthYoy => "رشد سالانه مقدار فروش",
            MonthlySalesToProductionRatio => "نسبت فروش به تولید",
            _ => metricCode
        };
}
