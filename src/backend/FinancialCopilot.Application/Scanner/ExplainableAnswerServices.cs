using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

public sealed class ConfidenceScoreCalculator : IConfidenceScoreCalculator
{
    private const string PolicyVersion = "v1";
    private const double InterpretationWeight = 0.40;
    private const double EvidenceWeight = 0.35;
    private const double FreshnessWeight = 0.25;
    private const double WarningPenaltyPerWarning = 0.10;
    private const double MaxWarningPenalty = 0.30;

    public ConfidenceScoreResult Calculate(
        ScannerQueryPlan plan,
        ScannerTableResult? executionResult)
    {
        var interpretationCertainty = CalculateInterpretationCertainty(plan);
        var evidenceCompleteness = CalculateEvidenceCompleteness(plan, executionResult);
        var sourceFreshness = CalculateSourceFreshness(executionResult);
        var warningPenalty = CalculateWarningPenalty(plan, executionResult);

        var rawScore =
            interpretationCertainty * InterpretationWeight +
            evidenceCompleteness * EvidenceWeight +
            sourceFreshness * FreshnessWeight;

        var finalScore = Math.Round(rawScore * (1.0 - warningPenalty), 2);
        finalScore = Math.Clamp(finalScore, 0.0, 1.0);

        return new ConfidenceScoreResult(
            finalScore,
            new ConfidenceFactors(interpretationCertainty, evidenceCompleteness, sourceFreshness, warningPenalty),
            PolicyVersion);
    }

    private static double CalculateInterpretationCertainty(ScannerQueryPlan plan)
    {
        if (plan.ClarificationRequired) return 0.0;

        var inferredCount = plan.Conditions.Count(c => c.Origin == FilterOrigin.InferredDefault);
        return Math.Max(0.0, 1.0 - inferredCount * 0.10);
    }

    private static double CalculateEvidenceCompleteness(ScannerQueryPlan plan, ScannerTableResult? result)
    {
        if (result is null || result.Rows.Count == 0) return 0.0;

        var conditionCodes = plan.Conditions
            .Select(c => c.MetricReference.MetricCode.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (conditionCodes.Count == 0) return 1.0;

        var totalExpected = result.Rows.Count * conditionCodes.Count;
        if (totalExpected == 0) return 1.0;

        var nonMissingCount = result.Rows.Sum(row =>
            conditionCodes.Count(code =>
                row.Cells.TryGetValue(code, out var cell) &&
                cell.FreshnessStatus != CellFreshnessStatus.Missing));

        return (double)nonMissingCount / totalExpected;
    }

    private static double CalculateSourceFreshness(ScannerTableResult? result)
    {
        if (result is null || result.Rows.Count == 0) return 0.5;

        var liveOrFallback = 0;
        var total = 0;

        foreach (var row in result.Rows)
        {
            if (!row.Cells.TryGetValue("LATEST_PRICE", out var cell)) continue;
            total++;
            if (cell.FreshnessStatus is CellFreshnessStatus.Live or CellFreshnessStatus.PreviousTradingDay)
                liveOrFallback++;
        }

        return total == 0 ? 0.5 : (double)liveOrFallback / total;
    }

    private static double CalculateWarningPenalty(ScannerQueryPlan plan, ScannerTableResult? result)
    {
        var warnings = (result?.MissingDataWarnings?.Count ?? 0) + (plan.ColumnOverflowWarnings?.Count ?? 0);
        return Math.Min(MaxWarningPenalty, warnings * WarningPenaltyPerWarning);
    }
}

public sealed class ExplainableAnswerBuilder(
    IConfidenceScoreCalculator confidenceCalculator,
    IScannerExplanationGenerator explanationGenerator,
    IFinancialMetricRegistry metricRegistry,
    TimeProvider timeProvider) : IExplainableAnswerBuilder
{
    public async Task<ExplainableAnswer> BuildAsync(
        ExplainableAnswerRequest request,
        CancellationToken cancellationToken)
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        var plan = request.Plan;
        var result = request.ExecutionResult;

        var filterChips = BuildFilterChips(plan, asOf);
        var metricEvidence = BuildMetricEvidence(plan, result, asOf);
        var dataCitations = BuildDataCitations(result);
        var confidence = confidenceCalculator.Calculate(plan, result);

        ScannerExplanationOutput? explanationOutput = null;
        if (result is not null)
        {
            try
            {
                explanationOutput = await explanationGenerator.GenerateAsync(
                    new ScannerExplanationRequest(
                        plan.OriginalUserQuery,
                        result.Rows.Count,
                        result.Rows.Select(r => r.SymbolCode).ToList(),
                        filterChips,
                        request.TenantId,
                        request.CorrelationId),
                    cancellationToken);
            }
            catch
            {
                // Optional AI generation; deterministic evidence is preserved regardless
            }
        }

        return new ExplainableAnswer(
            filterChips,
            metricEvidence,
            dataCitations,
            confidence,
            explanationOutput?.SuggestedFollowUpQuestions ?? [],
            explanationOutput?.ExplanationText);
    }

    private IReadOnlyCollection<ConditionFilterChip> BuildFilterChips(ScannerQueryPlan plan, DateOnly asOf) =>
        plan.Conditions.Select(condition =>
        {
            var code = condition.MetricReference.MetricCode.Value;
            var displayName = TryGetDisplayName(condition.MetricReference.MetricCode, asOf) ?? code;
            var (symbol, label) = GetOperatorDisplay(condition.Operator);

            return new ConditionFilterChip(
                code,
                displayName,
                symbol,
                label,
                condition.Threshold,
                FormatThreshold(condition.Threshold),
                condition.Origin.ToString(),
                condition.Origin == FilterOrigin.InferredDefault,
                condition.OriginReason);
        }).ToList();

    private IReadOnlyCollection<MetricEvidenceSummary> BuildMetricEvidence(
        ScannerQueryPlan plan,
        ScannerTableResult? result,
        DateOnly asOf) =>
        plan.Conditions.Select(condition =>
        {
            var code = condition.MetricReference.MetricCode.Value;
            var displayName = TryGetDisplayName(condition.MetricReference.MetricCode, asOf) ?? code;
            var definition = TryGetDefinition(condition.MetricReference.MetricCode, asOf);

            var representativeCell = result?.Rows
                .Select(r => r.Cells.TryGetValue(code, out var c) ? c : null)
                .FirstOrDefault(c => c is not null && c.FreshnessStatus != CellFreshnessStatus.Missing);

            return new MetricEvidenceSummary(
                code,
                condition.MetricReference.MetricVersion.Value,
                condition.MetricReference.CalculationPolicyVersion.Value,
                displayName,
                definition?.Unit.DisplayLabel ?? "Unknown",
                representativeCell?.Value,
                representativeCell?.FormattedValue,
                condition.MetricReference.PeriodType.ToString(),
                representativeCell?.SourceTimestamp);
        }).ToList();

    private static IReadOnlyCollection<DataCitation> BuildDataCitations(ScannerTableResult? result)
    {
        if (result is null) return [];

        return result.Rows
            .SelectMany(row => row.Cells
                .Where(kv => kv.Value.FreshnessStatus != CellFreshnessStatus.Missing
                             && kv.Value.SourceTimestamp is not null)
                .Select(kv => new DataCitation(
                    row.SymbolCode,
                    kv.Key,
                    kv.Value.SourceTimestamp,
                    kv.Value.FreshnessStatus.ToString())))
            .ToList();
    }

    private string? TryGetDisplayName(MetricCode code, DateOnly asOf)
    {
        try { return metricRegistry.ResolveDefinition(code, asOf).DisplayName; }
        catch { return null; }
    }

    private FinancialMetricDefinition? TryGetDefinition(MetricCode code, DateOnly asOf)
    {
        try { return metricRegistry.ResolveDefinition(code, asOf); }
        catch { return null; }
    }

    private static (string Symbol, string Label) GetOperatorDisplay(ConditionOperator op) =>
        op switch
        {
            ConditionOperator.LessThan => ("<", "below"),
            ConditionOperator.LessThanOrEqual => ("≤", "at most"),
            ConditionOperator.GreaterThan => (">", "above"),
            ConditionOperator.GreaterThanOrEqual => ("≥", "at least"),
            ConditionOperator.Equal => ("=", "equal to"),
            ConditionOperator.NotEqual => ("≠", "not equal to"),
            _ => ("?", "unknown")
        };

    private static string FormatThreshold(decimal threshold) =>
        threshold == Math.Floor(threshold)
            ? ((long)threshold).ToString()
            : threshold.ToString("N2");
}
