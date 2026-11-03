using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Scanner.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// CyclicalWaves-mode basic screening (decision C3, design §10.5). The existing parser, column policy and
/// ranker are reused; only the data source and the deterministic evaluation are new. Every rejection is
/// decided before any data read, and all arithmetic is C#; the model never computes a number.
/// </summary>
public sealed class CyclicalWavesV2ScannerExecutionService(
    CyclicalWavesV2ReadModel readModel,
    IScannerResultColumnPolicy columnPolicy,
    IScannerResultRanker ranker,
    TimeProvider timeProvider) : IScannerExecutionService
{
    public const string UnitClarificationMessage = "واحد مبلغ (ریال/تومان، میلیون/میلیارد) را مشخص کنید.";
    public const string MissingUnitReasonCode = "monetary_unit_required";
    public const string MissingValueReason = "missing_value";
    public const string PeNotComputableReason = "pe_not_computable";

    public async Task<ScannerTableResult> ExecuteAsync(ScannerExecutionRequest request, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var plan = request.Plan;
        var columns = columnPolicy.BuildColumns(plan);

        if (!string.IsNullOrWhiteSpace(request.Universe?.IndustryCode))
        {
            return Rejected(plan, columns, started, ScannerExecutionIssueKind.Unavailable,
                ChatCapabilityAvailability.UnavailableForProviderReasonCode,
                "غربال بر اساس صنعت در منبع دادهٔ فعال (CyclicalWaves) در دسترس نیست؛ این منبع اطلاعات صنعت ندارد.");
        }

        var built = BuildConditions(plan);
        if (built.Issue is not null)
        {
            return Rejected(plan, columns, started, built.Issue.Kind, built.Issue.ReasonCode, built.Issue.Message);
        }

        var conditions = built.Conditions;
        var snapshots = await readModel.LoadSnapshotsAsync(null, cancellationToken);
        var excluded = new Dictionary<string, int>(StringComparer.Ordinal);
        var matched = new List<CyclicalWavesV2TickerSnapshot>();
        foreach (var snapshot in snapshots.Take(Math.Clamp(request.Universe?.MaximumSymbols ?? 5_000, 1, 5_000)))
        {
            var reason = Evaluate(snapshot, conditions);
            if (reason is null)
            {
                matched.Add(snapshot);
            }
            else if (reason.Length > 0)
            {
                excluded[reason] = excluded.GetValueOrDefault(reason) + 1;
            }
        }

        var rows = matched.Select(snapshot => BuildRow(snapshot, columns, conditions)).ToList();
        var rankingPlan = plan with
        {
            Conditions = conditions.Select(condition => condition.Source with { Threshold = condition.Threshold }).ToList()
        };
        var ranked = ranker.Rank(rows, rankingPlan);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(ranked.Count / (double)pageSize));
        var page = Math.Clamp(request.Page, 1, totalPages);
        var pageRows = ranked.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var warnings = new List<string>();
        if (excluded.GetValueOrDefault(MissingValueReason) > 0)
        {
            warnings.Add($"{excluded[MissingValueReason]} نماد به‌دلیل نبود مقدار در منبع CyclicalWaves بررسی نشد.");
        }

        if (matched.Select(snapshot => snapshot.LastQuarterDate).Distinct().Count() > 1 ||
            matched.Select(snapshot => snapshot.LastMonthSaleDate).Distinct().Count() > 1)
        {
            warnings.Add("دورهٔ آخرین گزارش همهٔ نمادها یکسان نیست؛ تاریخ دورهٔ هر مقدار در جدول آمده است.");
        }

        var finished = timeProvider.GetUtcNow();
        return new ScannerTableResult(
            plan.PlanId,
            columns,
            pageRows,
            new ScannerExecutionFacts(
                finished,
                finished - started,
                snapshots.Count,
                matched.Count,
                FromCache: false,
                Page: page,
                PageSize: pageSize,
                TotalPages: totalPages,
                EligibleSymbolCount: snapshots.Count,
                EvaluatedSymbolCount: snapshots.Count - excluded.GetValueOrDefault(MissingValueReason),
                ExcludedByReason: excluded),
            warnings);
    }

    /// <summary>One resolved condition: the operand binding, the threshold in the binding's unit, and the operator.</summary>
    internal sealed record ResolvedCondition(
        ScannerCondition Source,
        ScreenerMetricBinding Binding,
        ConditionOperator Operator,
        decimal Threshold,
        SalesGrowthThresholdKind? SalesGrowthKind = null);

    internal sealed record BuiltConditions(IReadOnlyList<ResolvedCondition> Conditions, ScannerExecutionIssue? Issue);

    internal static BuiltConditions BuildConditions(ScannerQueryPlan plan)
    {
        var conditions = new List<ResolvedCondition>();
        if (plan.SalesGrowth is { } salesGrowth)
        {
            var semantics = salesGrowth.Semantics;
            var code = semantics.Baseline switch
            {
                SalesGrowthComparisonBaseline.PreviousMonth => "MONTHLY_SALES_GROWTH_MOM",
                SalesGrowthComparisonBaseline.SameMonthPreviousYear => "MONTHLY_SALES_GROWTH_YOY",
                _ => null
            };
            if (code is null)
            {
                return Unavailable("رشد فروش نسبت به میانگین ۱۲ ماه گذشته");
            }

            var binding = ScreenerMetricMapping.ForScanner(new MetricCode(code));
            var source = plan.Conditions.FirstOrDefault() ?? new ScannerCondition(
                new ScannerMetricReference("رشد فروش", new MetricCode(code), new MetricVersion("v1"),
                    new CalculationPolicyVersion($"{code}_v1"), FiscalPeriodType.Monthly, null),
                semantics.ComparisonOperator, semantics.ThresholdValue ?? 0m, semantics.Origin);
            conditions.Add(new ResolvedCondition(source, binding, semantics.ComparisonOperator, semantics.ThresholdValue ?? 0m, semantics.ThresholdKind));
            return new BuiltConditions(conditions, null);
        }

        foreach (var condition in plan.Conditions)
        {
            var binding = ScreenerMetricMapping.ForScanner(condition.MetricReference.MetricCode, condition.MetricReference.Comparison);
            if (!binding.IsAvailable)
            {
                return Unavailable(condition.MetricReference.OriginalUserTerminology);
            }

            conditions.Add(new ResolvedCondition(condition, binding, condition.Operator, condition.Threshold));
        }

        var monetary = conditions.Where(condition => condition.Binding.ComparisonUnit == ScreenerMetricUnit.Irr).ToList();
        var resolutions = MonetaryThresholdResolver.Resolve(plan.OriginalUserQuery, monetary.Select(condition => condition.Threshold).ToList());
        for (var index = 0; index < monetary.Count; index++)
        {
            var resolution = resolutions[index];
            if (resolution.Status is MonetaryThresholdStatus.MissingUnit or MonetaryThresholdStatus.Ambiguous)
            {
                return new BuiltConditions([], new ScannerExecutionIssue(
                    ScannerExecutionIssueKind.ClarificationRequired, MissingUnitReasonCode, UnitClarificationMessage));
            }

            var position = conditions.IndexOf(monetary[index]);
            conditions[position] = monetary[index] with { Threshold = resolution.IrrValue!.Value };
        }

        return new BuiltConditions(conditions, null);
    }

    /// <returns><c>null</c> when every condition passes; otherwise the exclusion reason ("" for an ordinary non-match).</returns>
    internal static string? Evaluate(CyclicalWavesV2TickerSnapshot snapshot, IReadOnlyList<ResolvedCondition> conditions)
    {
        foreach (var condition in conditions)
        {
            var binding = condition.Binding;
            if (binding.Kind == ScreenerOperandKind.Direct &&
                ScreenerMetricCatalog.Get(binding.FieldKey!).ZeroMeansNotComputable &&
                snapshot.Values.GetValueOrDefault(binding.FieldKey!) == 0m)
            {
                return PeNotComputableReason;
            }

            var operand = condition.SalesGrowthKind switch
            {
                SalesGrowthThresholdKind.Multiple => Multiple(snapshot.Values.GetValueOrDefault(binding.FieldKey!), snapshot.Values.GetValueOrDefault(binding.BaseFieldKey!)),
                _ => binding.Evaluate(snapshot.Values)
            };
            if (operand is null)
            {
                return MissingValueReason;
            }

            var threshold = condition.SalesGrowthKind == SalesGrowthThresholdKind.Positive ? 0m : condition.Threshold;
            var op = condition.SalesGrowthKind == SalesGrowthThresholdKind.Positive ? ConditionOperator.GreaterThan : condition.Operator;
            if (!Passes(operand.Value, op, threshold))
            {
                return string.Empty;
            }
        }

        return null;
    }

    internal static bool Passes(decimal value, ConditionOperator op, decimal threshold) => op switch
    {
        ConditionOperator.GreaterThan => value > threshold,
        ConditionOperator.GreaterThanOrEqual => value >= threshold,
        ConditionOperator.LessThan => value < threshold,
        ConditionOperator.LessThanOrEqual => value <= threshold,
        ConditionOperator.Equal => value == threshold,
        ConditionOperator.NotEqual => value != threshold,
        _ => false
    };

    private static decimal? Multiple(decimal? current, decimal? baseValue) =>
        current is not null && baseValue is > 0m ? current.Value / baseValue.Value : null;

    private static ScannerTableRow BuildRow(
        CyclicalWavesV2TickerSnapshot snapshot,
        IReadOnlyCollection<ScannerTableColumn> columns,
        IReadOnlyList<ResolvedCondition> conditions)
    {
        var cells = new Dictionary<string, ScannerTableCell>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            cells[column.Identifier] = column.ColumnType switch
            {
                ScannerColumnType.Symbol or ScannerColumnType.CompanyName =>
                    new ScannerTableCell(null, snapshot.Ticker, CellFreshnessStatus.Persisted, null),
                ScannerColumnType.Metric when column.MetricCode == SalesGrowthMultipleColumn =>
                    SalesGrowthMultipleCell(snapshot, conditions),
                ScannerColumnType.Metric when column.MetricCode is not null =>
                    CyclicalWavesV2SymbolMetricLookupService.BuildCell(BindingFor(column.MetricCode, conditions), snapshot),
                _ => new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null)
            };
        }

        // The ranker reads cells by condition metric code; make sure each condition has one.
        foreach (var condition in conditions)
        {
            var code = condition.Source.MetricReference.MetricCode.Value;
            if (!cells.ContainsKey(code))
            {
                cells[code] = CyclicalWavesV2SymbolMetricLookupService.BuildCell(condition.Binding, snapshot);
            }
        }

        return new ScannerTableRow(
            snapshot.Ticker,
            snapshot.Ticker,
            cells,
            1.0,
            conditions.Select(condition => condition.Source.MetricReference.MetricCode.Value).Distinct().ToList(),
            CyclicalWavesV2ReadModel.SourceLabel,
            CyclicalWavesV2CanonicalEntityResolver.ExternalCompanyId(snapshot.TickerId));
    }

    private static ScreenerMetricBinding BindingFor(string metricCode, IReadOnlyList<ResolvedCondition> conditions) =>
        conditions.FirstOrDefault(condition => condition.Source.MetricReference.MetricCode.Value == metricCode)?.Binding
        ?? SalesGrowthColumnBinding(metricCode, conditions)
        ?? ScreenerMetricMapping.ForScanner(new MetricCode(metricCode));

    private const string SalesGrowthMultipleColumn = "MONTHLY_SALES_GROWTH_MULTIPLE";

    // Feature 116 sales-growth plans add baseline and growth columns (ScannerResultColumnPolicy); they are read from
    // the same screener fields the condition was evaluated on.
    private static ScreenerMetricBinding? SalesGrowthColumnBinding(string metricCode, IReadOnlyList<ResolvedCondition> conditions)
    {
        var growth = conditions.FirstOrDefault(condition => condition.SalesGrowthKind is not null)?.Binding;
        return metricCode switch
        {
            "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH" => Direct(metricCode, ScreenerMetricCatalog.PenultimateMonthSale),
            "MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR" => Direct(metricCode, ScreenerMetricCatalog.LastYearSameMonthSale),
            "MONTHLY_SALES_GROWTH_PERCENT" when growth is not null => growth with { MetricCode = metricCode },
            _ => null
        };
    }

    private static ScreenerMetricBinding Direct(string metricCode, string fieldKey) =>
        new(metricCode, ScreenerOperandKind.Direct, fieldKey, null, ScreenerMetricCatalog.Get(fieldKey).Unit);

    private static ScannerTableCell SalesGrowthMultipleCell(CyclicalWavesV2TickerSnapshot snapshot, IReadOnlyList<ResolvedCondition> conditions)
    {
        var growth = conditions.FirstOrDefault(condition => condition.SalesGrowthKind is not null)?.Binding;
        var multiple = growth is null
            ? null
            : Multiple(snapshot.Values.GetValueOrDefault(growth.FieldKey!), snapshot.Values.GetValueOrDefault(growth.BaseFieldKey!));
        return multiple is null
            ? new ScannerTableCell(null, null, CellFreshnessStatus.Missing, null)
            : new ScannerTableCell(multiple, CyclicalWavesV2Presentation.Format(ScreenerMetricUnit.Ratio, multiple.Value),
                CellFreshnessStatus.Persisted, snapshot.LastSyncedAt, snapshot.LastMonthSaleDate,
                snapshot.LastMonthSaleDate is null ? null : CyclicalWavesV2Presentation.PersianDate(snapshot.LastMonthSaleDate.Value),
                CyclicalWavesV2ReadModel.SourceLabel);
    }

    private static BuiltConditions Unavailable(string criterion) =>
        new([], new ScannerExecutionIssue(
            ScannerExecutionIssueKind.Unavailable,
            ChatCapabilityAvailability.UnavailableForProviderReasonCode,
            $"معیار «{criterion}» با دادهٔ منبع فعال (CyclicalWaves) قابل غربال نیست."));

    private ScannerTableResult Rejected(
        ScannerQueryPlan plan,
        IReadOnlyCollection<ScannerTableColumn> columns,
        DateTimeOffset started,
        ScannerExecutionIssueKind kind,
        string reasonCode,
        string message)
    {
        var finished = timeProvider.GetUtcNow();
        return new ScannerTableResult(
            plan.PlanId,
            columns,
            [],
            new ScannerExecutionFacts(finished, finished - started, 0, 0, FromCache: false),
            [message],
            Issue: new ScannerExecutionIssue(kind, reasonCode, message));
    }
}
