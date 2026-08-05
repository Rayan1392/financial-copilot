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
    IMissingAnswerFeedbackCollector feedbackCollector,
    ISalesGrowthComparisonCalculator? salesGrowthCalculator = null,
    ISalesGrowthCommonEvaluationPeriodSelector? commonPeriodSelector = null) : IScannerExecutionService
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

        if (plan.SalesGrowth is not null)
        {
            return await ExecuteSalesGrowthAsync(
                request,
                plan,
                columns,
                companyRows,
                startTime,
                cancellationToken);
        }

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

    private async Task<ScannerTableResult> ExecuteSalesGrowthAsync(
        ScannerExecutionRequest request,
        ScannerQueryPlan plan,
        IReadOnlyCollection<ScannerTableColumn> columns,
        IReadOnlyCollection<NormalizedCompanyRow> companyRows,
        DateTimeOffset startTime,
        CancellationToken cancellationToken)
    {
        var salesPlan = plan.SalesGrowth!;
        var comparisonCalculator = salesGrowthCalculator ?? new SalesGrowthComparisonCalculator();
        var periodSelector = commonPeriodSelector ?? new SalesGrowthCommonEvaluationPeriodSelector(new SalesGrowthScannerOptions());
        var externalCompanyIds = companyRows.Select(company => company.ExternalCompanyId).ToList();
        var snapshots = await dbContext.CompanyMonthlyActivityTrendSnapshots.AsNoTracking()
            .Where(snapshot => externalCompanyIds.Contains(snapshot.ExternalCompanyId))
            .ToListAsync(cancellationToken);

        var observations = snapshots
            .Select(snapshot => new SalesGrowthPeriodObservation(
                ToEvaluationPeriod(snapshot),
                snapshot.ExternalCompanyId,
                IsCompleteForBaseline(snapshot, salesPlan.Semantics.Baseline)))
            .ToArray();

        var selection = periodSelector.Select(observations, companyRows.Count);
        var targetPeriod = salesPlan.TargetCommonPeriod is not null
            ? new SalesGrowthEvaluationPeriod(
                salesPlan.TargetCommonPeriod.Value.Year,
                salesPlan.TargetCommonPeriod.Value.Month)
            : selection.TargetPeriod;

        var warnings = new List<string>();
        if (targetPeriod is null || selection.Status != SalesGrowthCommonPeriodSelectionStatus.Available &&
            salesPlan.TargetCommonPeriod is null)
        {
            warnings.Add($"Sales-growth scanner unavailable: {selection.Reason ?? "no common evaluation period was selected"}");
            return BuildEmptyResult(plan, columns, startTime, timeProvider.GetUtcNow(), companyRows.Count, warnings);
        }
        var resolvedTargetPeriod = targetPeriod.Value;

        var requiredOtherCodes = plan.Conditions
            .Select(condition => condition.MetricReference.MetricCode.Value)
            .Where(code => !IsSalesGrowthMetric(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var derivedRows = requiredOtherCodes.Length == 0
            ? []
            : await dbContext.DerivedMetrics.AsNoTracking()
                .Where(row => externalCompanyIds.Contains(row.ExternalCompanyId) && requiredOtherCodes.Contains(row.MetricCode))
                .ToListAsync(cancellationToken);
        var latestOtherMetrics = derivedRows
            .GroupBy(row => (row.ExternalCompanyId, row.MetricCode), ExternalCompanyMetricKeyComparer.Instance)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.PeriodEnd).First(),
                ExternalCompanyMetricKeyComparer.Instance);

        var matched = new List<(NormalizedCompanyRow Company, SalesGrowthComparisonCalculationResult Calculation)>();
        var evaluatedCount = 0;
        var excludedMissingCount = 0;
        var excludedThresholdCount = 0;
        var excludedOtherConditionCount = 0;
        foreach (var company in companyRows)
        {
            var companySnapshots = snapshots
                .Where(snapshot => string.Equals(snapshot.ExternalCompanyId, company.ExternalCompanyId, StringComparison.OrdinalIgnoreCase))
                .Select(snapshot => new SalesGrowthSalesObservation(
                    snapshot.ExternalCompanyId,
                    ToEvaluationPeriod(snapshot),
                    snapshot.MonthlySalesAmount,
                    snapshot.SourceProviderName,
                    snapshot.SourceReportId ?? snapshot.SourceRawPayloadId ?? snapshot.Id.ToString("N"),
                    snapshot.CalculatedAtUtc))
                .ToArray();
            var calculation = comparisonCalculator.Calculate(
                company.ExternalCompanyId,
                resolvedTargetPeriod,
                salesPlan.Semantics.Baseline,
                companySnapshots);
            evaluatedCount++;

            if (!PassesSalesGrowth(calculation, salesPlan.Semantics))
            {
                if (!calculation.IsUsable)
                {
                    excludedMissingCount++;
                }
                else
                {
                    excludedThresholdCount++;
                }
                continue;
            }

            var passesOtherConditions = plan.Conditions
                .Where(condition => !IsSalesGrowthMetric(condition.MetricReference.MetricCode.Value))
                .All(condition =>
                {
                    if (!latestOtherMetrics.TryGetValue(
                            (company.ExternalCompanyId, condition.MetricReference.MetricCode.Value),
                            out var row) || row.Value is null)
                    {
                        return false;
                    }

                    return PassesCondition(
                        row.Value.Value,
                        condition.Operator,
                        condition.Threshold,
                        IsValuationRatioMetric(condition.MetricReference.MetricCode, request.AsOf));
                });

            if (passesOtherConditions)
            {
                matched.Add((company, calculation));
            }
            else
            {
                excludedOtherConditionCount++;
            }
        }

        var ranked = matched
            .OrderByDescending(item => item.Calculation.GrowthPercent ?? decimal.MinValue)
            .ThenBy(item => CompanyDisplayResolver.FirstNonBlank(
                item.Company.Ticker,
                item.Company.TseSymbol,
                item.Company.CompanySymbol,
                item.Company.ExternalCompanyId),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = ranked.Select(item => BuildSalesGrowthRow(
            item.Company,
            item.Calculation,
            columns,
            latestOtherMetrics,
            plan.Conditions.Select(condition => condition.MetricReference.MetricCode.Value).ToArray(),
            salesPlan.Semantics)).ToArray();

        var pageSize = Math.Clamp(request.PageSize, 1, SalesGrowthScannerPlan.MaximumPageSize);
        var totalPages = rows.Length == 0 ? 1 : (int)Math.Ceiling(rows.Length / (double)pageSize);
        var page = Math.Clamp(request.Page, 1, totalPages);
        var pageRows = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var eligibleSymbolCount = targetPeriod is null
            ? 0
            : observations
                .Where(observation => observation.Period == resolvedTargetPeriod && observation.IsComplete)
                .Select(observation => observation.ExternalCompanyId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        var tableMetadata = new SalesGrowthTableMetadata(
            resolvedTargetPeriod.FirstDay,
            eligibleSymbolCount,
            companyRows.Count,
            companyRows.Count == 0 ? 0m : Math.Round(eligibleSymbolCount * 100m / companyRows.Count, 2),
            salesPlan.TargetCommonPeriod is null
                ? selection.Status
                : SalesGrowthCommonPeriodSelectionStatus.Available,
            selection.PolicyVersion,
            salesPlan.Semantics.Policies.Calculation,
            selection.MixedPeriodsAllowed,
            selection.Reason);

        await TryCollectMissingAnswerFeedbackAsync(
            request,
            plan,
            companyRows.Count,
            matched.Count,
            cancellationToken);

        var endTime = timeProvider.GetUtcNow();
        return new ScannerTableResult(
            plan.PlanId,
            columns,
            pageRows,
            new ScannerExecutionFacts(
                endTime,
                endTime - startTime,
                companyRows.Count,
                matched.Count,
                FromCache: false,
                Page: page,
                PageSize: pageSize,
                TotalPages: totalPages,
                EligibleSymbolCount: eligibleSymbolCount,
                EvaluatedSymbolCount: evaluatedCount,
                ExcludedByReason: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["missing_or_unusable_data"] = excludedMissingCount,
                    ["sales_growth_threshold"] = excludedThresholdCount,
                    ["other_conditions"] = excludedOtherConditionCount
                }),
            warnings,
            tableMetadata);
    }

    private ScannerTableRow BuildSalesGrowthRow(
        NormalizedCompanyRow company,
        SalesGrowthComparisonCalculationResult calculation,
        IReadOnlyCollection<ScannerTableColumn> columns,
        IReadOnlyDictionary<(string ExternalCompanyId, string MetricCode), DerivedMetricRow> latestOtherMetrics,
        IReadOnlyCollection<string> conditionCodes,
        SalesGrowthScannerSemantics semantics)
    {
        var displaySymbol = CompanyDisplayResolver.FirstNonBlank(
            company.Ticker,
            company.TseSymbol,
            company.CompanySymbol,
            company.ExternalCompanyId) ?? company.ExternalCompanyId;
        var cells = new Dictionary<string, ScannerTableCell>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (column.ColumnType == ScannerColumnType.Symbol)
            {
                cells[column.Identifier] = new ScannerTableCell(null, displaySymbol, CellFreshnessStatus.Persisted, null);
                continue;
            }

            if (column.ColumnType == ScannerColumnType.CompanyName)
            {
                cells[column.Identifier] = new ScannerTableCell(null, company.Name, CellFreshnessStatus.Persisted, null);
                continue;
            }

            decimal? value = column.MetricCode switch
            {
                "MONTHLY_SALES" => calculation.Current.Amount,
                "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH" or
                "MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR" or
                "MONTHLY_SALES_BASELINE_AVERAGE_PREVIOUS_12_MONTHS" => calculation.BaselineValue.Amount,
                "MONTHLY_SALES_GROWTH_MOM" or "MONTHLY_SALES_GROWTH_YOY" => calculation.GrowthPercent,
                "MONTHLY_SALES_GROWTH_PERCENT" => calculation.GrowthPercent,
                "MONTHLY_SALES_GROWTH_MULTIPLE" => calculation.GrowthMultiple,
                _ when column.MetricCode is not null && latestOtherMetrics.TryGetValue(
                    (company.ExternalCompanyId, column.MetricCode), out var row) => row.Value,
                _ => null
            };
            var timestamp = calculation.LatestObservedAtUtc;
            cells[column.Identifier] = new ScannerTableCell(
                value,
                value is null ? null : FinancialNumberFormatter.Metric(column.MetricCode ?? column.Identifier, value.Value),
                value is null ? CellFreshnessStatus.Missing : CellFreshnessStatus.Persisted,
                timestamp);
        }

        return new ScannerTableRow(
            displaySymbol,
            company.Name,
            cells,
            calculation.GrowthPercent is null ? 0d : (double)calculation.GrowthPercent.Value,
            conditionCodes,
            company.ProviderName,
            company.ExternalCompanyId,
            new SalesGrowthRowMetadata(
                calculation.Current.Period!.Value.FirstDay,
                calculation.BaselineValue.Period?.FirstDay,
                calculation.BaselineValue.WindowPeriods.Select(period => period.FirstDay).ToArray(),
                "currency",
                "raw",
                calculation.Evidence,
                calculation.LatestObservedAtUtc,
                calculation.FreshnessSource,
                semantics.ThresholdValue,
                semantics.ComparisonOperator,
                semantics.Origin,
                calculation.Policies,
                BuildSalesGrowthMatchReason(calculation, semantics)));
    }

    private static string BuildSalesGrowthMatchReason(
        SalesGrowthComparisonCalculationResult calculation,
        SalesGrowthScannerSemantics semantics) =>
        semantics.ThresholdKind switch
        {
            SalesGrowthThresholdKind.Positive =>
                $"current {calculation.Current.Amount} > baseline {calculation.BaselineValue.Amount}",
            SalesGrowthThresholdKind.Percent =>
                $"growth percent {calculation.GrowthPercent} {semantics.ComparisonOperator} {semantics.ThresholdValue}%",
            SalesGrowthThresholdKind.Multiple =>
                $"growth multiple {calculation.GrowthMultiple} {semantics.ComparisonOperator} {semantics.ThresholdValue}",
            _ => "sales-growth condition matched"
        };

    private static bool PassesSalesGrowth(
        SalesGrowthComparisonCalculationResult calculation,
        SalesGrowthScannerSemantics semantics)
    {
        if (!calculation.IsUsable)
        {
            return false;
        }

        var value = semantics.ThresholdKind switch
        {
            SalesGrowthThresholdKind.Positive =>
                calculation.Current.Amount!.Value > calculation.BaselineValue.Amount!.Value
                    ? 1m
                    : 0m,
            SalesGrowthThresholdKind.Percent => calculation.GrowthPercent!.Value,
            SalesGrowthThresholdKind.Multiple => calculation.GrowthMultiple!.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(semantics))
        };
        var threshold = semantics.ThresholdKind == SalesGrowthThresholdKind.Positive
            ? 0m
            : semantics.ThresholdValue!.Value;
        return PassesCondition(value, semantics.ComparisonOperator, threshold);
    }

    private static bool IsSalesGrowthMetric(string code) =>
        code.Equals("MONTHLY_SALES_GROWTH_MOM", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("MONTHLY_SALES_GROWTH_YOY", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("MONTHLY_SALES_GROWTH", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompleteForBaseline(
        CompanyMonthlyActivityTrendSnapshotRow snapshot,
        SalesGrowthComparisonBaseline baseline) =>
        snapshot.MonthlySalesAmount >= 0m && baseline switch
        {
            SalesGrowthComparisonBaseline.SameMonthPreviousYear => snapshot.IsComparablePreviousYearAvailable,
            SalesGrowthComparisonBaseline.AveragePrevious12Months => snapshot.IsAverage12MonthComplete,
            SalesGrowthComparisonBaseline.PreviousMonth => true,
            _ => false
        };

    private static SalesGrowthEvaluationPeriod ToEvaluationPeriod(CompanyMonthlyActivityTrendSnapshotRow snapshot)
    {
        var year = snapshot.CalendarYear ?? snapshot.ReportYear;
        var month = snapshot.CalendarMonth ?? snapshot.ReportMonth;
        return new SalesGrowthEvaluationPeriod(year, month);
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
        int totalCompanies,
        IReadOnlyCollection<string>? warnings = null) =>
        new(
            plan.PlanId,
            columns,
            [],
            new ScannerExecutionFacts(endTime, endTime - startTime, totalCompanies, 0,
                FromCache: false, Page: 1, PageSize: 20, TotalPages: 1),
            warnings ?? []);

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
