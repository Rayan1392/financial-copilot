using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.MissingAnswer;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class EfCoreScannerExecutionService(
    FinancialIngestionDbContext dbContext,
    IScannerResultColumnPolicy columnPolicy,
    IMarketQuoteResolver quoteResolver,
    IScannerResultRanker ranker,
    TimeProvider timeProvider,
    IFinancialMetricRegistry metricRegistry,
    IMissingAnswerFeedbackCollector feedbackCollector) : IScannerExecutionService
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

        // Derive display metric codes directly from the column list built by the column policy.
        // Quote columns (LATEST_PRICE, DAILY_CHANGE_PCT, MARKET_CAP) are loaded only when
        // the policy included them, which happens only when the user requested them explicitly
        // or they appear as a filter/sort condition.
        var displayMetricCodes = columns
            .Where(col => col.MetricCode is not null)
            .Select(col => col.MetricCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allRequiredCodes = conditionCodes.Union(displayMetricCodes, StringComparer.OrdinalIgnoreCase).ToList();

        // Use Companies as the universe — each company with an ExternalCompanyId is a candidate
        var companyQuery = dbContext.Companies.AsNoTracking()
            .Where(c => c.ExternalCompanyId != null && c.ExternalCompanyId != string.Empty);
        if (!string.IsNullOrWhiteSpace(request.Universe?.IndustryCode))
        {
            var industryCode = request.Universe.IndustryCode.Trim();
            companyQuery = companyQuery.Where(company => company.IndustryId.HasValue &&
                dbContext.Industries.Any(industry => industry.Id == company.IndustryId.Value &&
                    (industry.ExternalId == industryCode || industry.Name == industryCode)));
        }
        if (!string.IsNullOrWhiteSpace(request.Universe?.InstrumentClass))
        {
            var instrumentClass = request.Universe.InstrumentClass.Trim();
            companyQuery = companyQuery.Where(company => dbContext.TradingInstruments.Any(instrument =>
                instrument.NormalizedCompanyId == company.Id && instrument.IsActive &&
                instrument.InstrumentKind == instrumentClass));
        }
        var maximumSymbols = Math.Clamp(request.Universe?.MaximumSymbols ?? 5_000, 1, 5_000);
        var companyRows = await companyQuery
            .OrderBy(company => company.ExternalCompanyId)
            .Take(maximumSymbols)
            .ToListAsync(cancellationToken);
        var totalCompanyCount = companyRows.Count;

        if (totalCompanyCount == 0 || !plan.Conditions.Any())
        {
            await TryCollectMissingAnswerFeedbackAsync(
                request,
                plan,
                totalCompanyCount,
                matchedCompanyCount: 0,
                cancellationToken);
            return BuildEmptyResult(plan, columns, startTime, timeProvider.GetUtcNow(), totalCompanyCount);
        }

        var externalCompanyIds = companyRows.Select(c => c.ExternalCompanyId).ToList();
        var derivedRows = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(dm => externalCompanyIds.Contains(dm.ExternalCompanyId) && allRequiredCodes.Contains(dm.MetricCode))
            .ToListAsync(cancellationToken);

        // Latest row per (ExternalCompanyId, MetricCode) — highest PeriodEnd wins
        var latestByCompanyMetric = derivedRows
            .GroupBy(dm => (dm.ExternalCompanyId, dm.MetricCode), ExternalCompanyMetricKeyComparer.Instance)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(dm => dm.PeriodEnd).First(),
                ExternalCompanyMetricKeyComparer.Instance);

        // AND-filter: intersect companies satisfying each condition
        var passingExternalIds = new HashSet<string>(externalCompanyIds, StringComparer.OrdinalIgnoreCase);

        foreach (var condition in plan.Conditions)
        {
            var code = condition.MetricReference.MetricCode.Value;
            var isValuationRatio = IsValuationRatioMetric(condition.MetricReference.MetricCode, request.AsOf);
            var passing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var company in companyRows)
            {
                if (!latestByCompanyMetric.TryGetValue((company.ExternalCompanyId, code), out var row) || row.Value is null)
                    continue;

                if (PassesCondition(row.Value.Value, condition.Operator, condition.Threshold, isValuationRatio))
                    passing.Add(company.ExternalCompanyId);
            }

            passingExternalIds.IntersectWith(passing);
        }

        var matchingCompanies = companyRows
            .Where(c => passingExternalIds.Contains(c.ExternalCompanyId))
            .OrderBy(c => c.Ticker ?? c.TseSymbol ?? c.CompanySymbol ?? c.ExternalCompanyId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Use Ticker (Persian) as the primary symbol code for quote resolution
        var symbolCodes = matchingCompanies
            .Select(c => CompanyDisplayResolver.FirstNonBlank(c.Ticker, c.TseSymbol, c.CompanySymbol, c.ExternalCompanyId))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new SymbolCode(s!))
            .Distinct()
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

        var rows = matchingCompanies.Select(company =>
        {
            var displaySymbol = CompanyDisplayResolver.FirstNonBlank(
                company.Ticker, company.TseSymbol, company.CompanySymbol, company.ExternalCompanyId)
                ?? company.ExternalCompanyId;

            // Try known symbol identifiers in priority order for quote
            MarketQuoteObservation? quote = null;
            foreach (var candidate in new[] { company.Ticker, company.TseSymbol, company.CompanySymbol, company.ExternalCompanyId })
            {
                if (!string.IsNullOrWhiteSpace(candidate) && quoteBySymbol.TryGetValue(candidate!, out var q))
                {
                    quote = q;
                    break;
                }
            }

            var cells = BuildCells(columns, company.ExternalCompanyId, displaySymbol, company.Name, quote, latestByCompanyMetric);
            return new ScannerTableRow(
                displaySymbol,
                company.Name,
                cells,
                Score: 0.0,
                conditionMetricCodes,
                SourceProvider: company.ProviderName,
                ExternalCompanyId: company.ExternalCompanyId);
        }).ToList();

        foreach (var unavailable in quoteResult.UnavailableSymbols)
        {
            warnings.Add(
                $"Live market quote unavailable for {unavailable.Value}; price data may reflect last stored observation.");
        }

        var ranked = ranker.Rank(rows, plan);

        // Hard cap to avoid excessive memory use; pagination slices within this window.
        const int HardCap = 500;
        var allRanked = ranked.Take(HardCap).ToList();

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Max(1, Math.Min(request.PageSize, 100));
        var totalPages = allRanked.Count == 0 ? 1 : (int)Math.Ceiling(allRanked.Count / (double)pageSize);
        page = Math.Min(page, totalPages);

        var paginated = allRanked
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        await TryCollectMissingAnswerFeedbackAsync(
            request,
            plan,
            totalCompanyCount,
            matchingCompanies.Count,
            cancellationToken);

        var endTime = timeProvider.GetUtcNow();
        return new ScannerTableResult(
            plan.PlanId,
            columns,
            paginated,
            new ScannerExecutionFacts(endTime, endTime - startTime, totalCompanyCount, matchingCompanies.Count,
                FromCache: false, Page: page, PageSize: pageSize, TotalPages: totalPages),
            warnings);
    }

    private async Task TryCollectMissingAnswerFeedbackAsync(
        ScannerExecutionRequest request,
        ScannerQueryPlan plan,
        int totalCompanyCount,
        int matchedCompanyCount,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.ActorId is null || request.QueryText is null) return;
            if (plan.Conditions.Count == 0) return;

            var primary = plan.Conditions.First().MetricReference;
            var primaryCode = primary.MetricCode.Value;
            var asOf = request.AsOf;

            var registered = TryResolveDefinition(primary.MetricCode, asOf);
            var derivedRowCount = registered
                ? await dbContext.DerivedMetrics.AsNoTracking()
                    .CountAsync(row => row.MetricCode == primaryCode, cancellationToken)
                : 0;

            var classification = MissingAnswerFeedbackClassifier.Classify(
                new MissingAnswerClassificationContext(
                    PrimaryMetricCode: primaryCode,
                    MetricRegistered: registered,
                    DerivedMetricRowCountForMetric: derivedRowCount,
                    TotalSymbolCount: totalCompanyCount,
                    MatchedSymbolCount: matchedCompanyCount));
            if (classification is null) return;

            var context = JsonSerializer.Serialize(new
            {
                planId = plan.PlanId,
                primaryMetric = primaryCode,
                conditions = plan.Conditions.Select(c => new
                {
                    metric = c.MetricReference.MetricCode.Value,
                    op = c.Operator.ToString(),
                    threshold = c.Threshold
                }).ToArray()
            });

            await feedbackCollector.CollectAsync(
                new MissingAnswerFeedbackRequest(
                    ActorId: request.ActorId,
                    QueryText: request.QueryText,
                    Classification: classification.Value,
                    RequestedMetricCode: primaryCode,
                    AffectedDataCodeOrName: primary.OriginalUserTerminology,
                    SymbolCountTotal: totalCompanyCount,
                    SymbolCountMatched: matchedCompanyCount,
                    SubmittedAt: timeProvider.GetUtcNow(),
                    Context: context),
                cancellationToken);
        }
        catch
        {
            // Collection must never disturb the scanner response.
        }
    }

    private bool TryResolveDefinition(MetricCode code, DateOnly asOf)
    {
        try
        {
            _ = metricRegistry.ResolveDefinition(code, asOf);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

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
                    BuildChangeCell(quote),

                ScannerColumnType.MarketCap =>
                    BuildPersistedMetricCell(externalCompanyId, "MARKET_CAP", latestByCompanyMetric, FormatLargeNumber),

                ScannerColumnType.Metric when column.MetricCode is not null =>
                    BuildPersistedMetricCell(
                        externalCompanyId,
                        column.MetricCode,
                        latestByCompanyMetric,
                        v => FinancialNumberFormatter.Metric(column.MetricCode, v)),

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
                quote.AsOf);
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

    private static ScannerTableCell BuildChangeCell(MarketQuoteObservation? quote)
    {
        if (quote is null)
            return new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null);

        var freshness = quote.Source == MarketQuoteSource.LiveQuote
            ? CellFreshnessStatus.Live
            : CellFreshnessStatus.PreviousTradingDay;

        return new ScannerTableCell(
            quote.PriceChangePercentage,
            FinancialNumberFormatter.SignedPercent(quote.PriceChangePercentage),
            freshness,
            quote.AsOf);
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

    // A stored value of 0 for a valuation ratio (PE, PS, PB, …) means the denominator was
    // zero/negative or the metric was never computed — not a genuine ratio of zero. Treat it
    // as missing/invalid and exclude the row from LessThan/LessThanOrEqual screens so that
    // "PE < 5" does not match companies with no valid earnings.
    internal static bool PassesCondition(decimal value, ConditionOperator op, decimal threshold, bool isValuationRatio = false)
    {
        if (isValuationRatio && value == 0m &&
            op is ConditionOperator.LessThan or ConditionOperator.LessThanOrEqual)
        {
            return false;
        }

        return op switch
        {
            ConditionOperator.LessThan => value < threshold,
            ConditionOperator.LessThanOrEqual => value <= threshold,
            ConditionOperator.GreaterThan => value > threshold,
            ConditionOperator.GreaterThanOrEqual => value >= threshold,
            ConditionOperator.Equal => value == threshold,
            ConditionOperator.NotEqual => value != threshold,
            _ => false
        };
    }

    internal bool IsValuationRatioMetric(MetricCode code, DateOnly asOf)
    {
        try
        {
            var definition = metricRegistry.ResolveDefinition(code, asOf);
            return definition.Category == MetricCategory.Valuation;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static string FormatLargeNumber(decimal value) => FinancialNumberFormatter.LargeNumber(value);

    private static ScannerTableResult BuildEmptyResult(
        ScannerQueryPlan plan,
        IReadOnlyCollection<ScannerTableColumn> columns,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int totalCompanies) =>
        new(
            plan.PlanId,
            columns,
            [],
            new ScannerExecutionFacts(endTime, endTime - startTime, totalCompanies, 0,
                FromCache: false, Page: 1, PageSize: 20, TotalPages: 1),
            []);

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

public sealed class ProviderMarketQuoteResolver(IMarketDataProvider marketDataProvider) : IMarketQuoteResolver
{
    public Task<BatchMarketQuoteResult> ResolveAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken) =>
        marketDataProvider.GetLatestQuotesAsync(symbols, cancellationToken);
}
