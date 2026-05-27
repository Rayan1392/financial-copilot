using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class EfCoreScannerExecutionService(
    FinancialIngestionDbContext dbContext,
    IScannerResultColumnPolicy columnPolicy,
    IMarketQuoteResolver quoteResolver,
    IScannerResultRanker ranker,
    TimeProvider timeProvider) : IScannerExecutionService
{
    public async Task<ScannerTableResult> ExecuteAsync(
        ScannerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = timeProvider.GetUtcNow();
        var plan = request.Plan;

        var columns = columnPolicy.BuildColumns(plan);

        var conditionCodes = plan.Conditions
            .Select(c => c.MetricReference.MetricCode.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var displayMetricCodes = columns
            .Where(col => col.ColumnType == ScannerColumnType.Metric && col.MetricCode is not null)
            .Select(col => col.MetricCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Always load MARKET_CAP for the MarketCap column
        displayMetricCodes.Add("MARKET_CAP");
        displayMetricCodes.Add("LATEST_PRICE");

        var allRequiredCodes = conditionCodes.Union(displayMetricCodes, StringComparer.OrdinalIgnoreCase).ToList();

        var symbolRows = await dbContext.Symbols.AsNoTracking().ToListAsync(cancellationToken);
        var totalSymbolCount = symbolRows.Count;

        if (totalSymbolCount == 0 || !plan.Conditions.Any())
        {
            return BuildEmptyResult(plan, columns, startTime, timeProvider.GetUtcNow(), totalSymbolCount);
        }

        var companyIds = symbolRows.Select(s => s.CompanyId).Distinct().ToList();
        var companyNameById = await dbContext.Companies.AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var symbolIds = symbolRows.Select(s => s.Id).ToList();
        var derivedRows = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(dm => symbolIds.Contains(dm.SymbolId) && allRequiredCodes.Contains(dm.MetricCode))
            .ToListAsync(cancellationToken);

        // Latest row per (SymbolId, MetricCode) — highest PeriodEnd wins
        var latestBySymbolMetric = derivedRows
            .GroupBy(dm => (dm.SymbolId, dm.MetricCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(dm => dm.PeriodEnd).First());

        // AND-filter: intersect symbols satisfying each condition
        var passingSymbolIds = new HashSet<Guid>(symbolIds);

        foreach (var condition in plan.Conditions)
        {
            var code = condition.MetricReference.MetricCode.Value;
            var passing = new HashSet<Guid>();

            foreach (var symbol in symbolRows)
            {
                if (!latestBySymbolMetric.TryGetValue((symbol.Id, code), out var row) || row.Value is null)
                    continue;

                if (PassesCondition(row.Value.Value, condition.Operator, condition.Threshold))
                    passing.Add(symbol.Id);
            }

            passingSymbolIds.IntersectWith(passing);
        }

        var matchingSymbols = symbolRows
            .Where(s => passingSymbolIds.Contains(s.Id))
            .OrderBy(s => s.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var symbolCodes = matchingSymbols
            .Select(s => new SymbolCode(s.SymbolCode))
            .ToList();

        var quoteResult = symbolCodes.Count > 0
            ? await quoteResolver.ResolveAsync(symbolCodes, cancellationToken)
            : new BatchMarketQuoteResult([], []);

        var quoteBySymbol = quoteResult.Observations
            .ToDictionary(q => q.SymbolCode.Value, StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();
        var conditionMetricCodes = plan.Conditions
            .Select(c => c.MetricReference.MetricCode.Value)
            .ToList();

        var rows = matchingSymbols.Select(symbol =>
        {
            quoteBySymbol.TryGetValue(symbol.SymbolCode, out var quote);
            var cells = BuildCells(columns, symbol, quote, latestBySymbolMetric);
            return new ScannerTableRow(
                symbol.SymbolCode,
                companyNameById.GetValueOrDefault(symbol.CompanyId),
                cells,
                Score: 0.0,
                conditionMetricCodes);
        }).ToList();

        foreach (var unavailable in quoteResult.UnavailableSymbols)
        {
            warnings.Add(
                $"Live market quote unavailable for {unavailable.Value}; price data may reflect last stored observation.");
        }

        var ranked = ranker.Rank(rows, plan);
        var limited = ranked.Take(request.MaxRows).ToList();

        var endTime = timeProvider.GetUtcNow();
        return new ScannerTableResult(
            plan.PlanId,
            columns,
            limited,
            new ScannerExecutionFacts(endTime, endTime - startTime, totalSymbolCount, matchingSymbols.Count, FromCache: false),
            warnings);
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

                ScannerColumnType.DailyChangePercent =>
                    BuildChangeCell(quote),

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

    private static ScannerTableCell BuildChangeCell(MarketQuoteObservation? quote)
    {
        if (quote is null)
            return new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null);

        var freshness = quote.Source == MarketQuoteSource.LiveQuote
            ? CellFreshnessStatus.Live
            : CellFreshnessStatus.PreviousTradingDay;

        return new ScannerTableCell(
            quote.PriceChangePercentage,
            $"{quote.PriceChangePercentage:+0.00;-0.00;0.00}%",
            freshness,
            quote.AsOf);
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

    private static bool PassesCondition(decimal value, ConditionOperator op, decimal threshold) =>
        op switch
        {
            ConditionOperator.LessThan => value < threshold,
            ConditionOperator.LessThanOrEqual => value <= threshold,
            ConditionOperator.GreaterThan => value > threshold,
            ConditionOperator.GreaterThanOrEqual => value >= threshold,
            ConditionOperator.Equal => value == threshold,
            ConditionOperator.NotEqual => value != threshold,
            _ => false
        };

    private static string FormatLargeNumber(decimal value) =>
        value switch
        {
            >= 1_000_000_000_000m => $"{value / 1_000_000_000_000m:N1}T",
            >= 1_000_000_000m => $"{value / 1_000_000_000m:N1}B",
            >= 1_000_000m => $"{value / 1_000_000m:N1}M",
            _ => value.ToString("N0")
        };

    private static ScannerTableResult BuildEmptyResult(
        ScannerQueryPlan plan,
        IReadOnlyCollection<ScannerTableColumn> columns,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int totalSymbols) =>
        new(
            plan.PlanId,
            columns,
            [],
            new ScannerExecutionFacts(endTime, endTime - startTime, totalSymbols, 0, FromCache: false),
            []);
}

public sealed class ProviderMarketQuoteResolver(IMarketDataProvider marketDataProvider) : IMarketQuoteResolver
{
    public Task<BatchMarketQuoteResult> ResolveAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken) =>
        marketDataProvider.GetLatestQuotesAsync(symbols, cancellationToken);
}
