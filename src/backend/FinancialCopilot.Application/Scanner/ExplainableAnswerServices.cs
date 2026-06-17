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
                    kv.Value.FreshnessStatus.ToString(),
                    row.SourceProvider)))
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
            : threshold.ToString("N2", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
}

public sealed class ConfidenceScoringService(
    IConfidenceScoringAuditSink? auditSink = null) : IConfidenceScoringService
{
    private const string PolicyVersion = "v1";
    private const double SourceWeight = 0.45;
    private const double CompletenessWeight = 0.20;
    private const double SupportingSourcesWeight = 0.15;
    private const double FreshnessWeight = 0.10;
    private const double ConsistencyWeight = 0.10;
    private const double WarningPenaltyPerWarning = 0.10;
    private const double MaxWarningPenalty = 0.30;

    public ConfidenceScoreResult Calculate(ConfidenceScoringRequest request)
    {
        var tableCells = GetFinancialCells(request).ToList();
        var expectedCellCount = CountExpectedFinancialCells(request);
        var supportedCells = tableCells
            .Where(cell => cell.Value is not null && cell.FreshnessStatus != CellFreshnessStatus.Missing)
            .ToList();

        var sourceReliability = GetSourceReliability(request.SourceType);
        var completeness = expectedCellCount == 0 ? 0.0 : (double)supportedCells.Count / expectedCellCount;
        var supportingSources = CalculateSupportingSources(supportedCells.Count);
        var freshness = CalculateFreshness(supportedCells);
        var warningPenalty = CalculateWarningPenalty(request);
        var narrativeConsistency = CalculateNarrativeConsistency(request.AnswerText, supportedCells);

        var rawScore =
            sourceReliability * SourceWeight +
            completeness * CompletenessWeight +
            supportingSources * SupportingSourcesWeight +
            freshness * FreshnessWeight +
            narrativeConsistency * ConsistencyWeight;

        var finalScore = Math.Clamp(Math.Round(rawScore * (1.0 - warningPenalty), 2), 0.0, 1.0);

        if (request.SourceType == ConfidenceSourceType.PreCalculatedMetric &&
            supportedCells.Count > 0)
        {
            finalScore = Math.Max(finalScore, 0.95);
        }

        var result = new ConfidenceScoreResult(
            finalScore,
            new ConfidenceFactors(
                sourceReliability,
                completeness,
                freshness,
                warningPenalty),
            PolicyVersion);

        auditSink?.Record(new ConfidenceScoringAudit(
            request.CorrelationId,
            request.SourceType,
            supportedCells.Count,
            expectedCellCount,
            narrativeConsistency,
            warningPenalty,
            result));

        return result;
    }

    private static double GetSourceReliability(ConfidenceSourceType sourceType) =>
        sourceType switch
        {
            ConfidenceSourceType.PreCalculatedMetric => 0.97,
            ConfidenceSourceType.DerivedMetric => 0.88,
            ConfidenceSourceType.LlmInference => 0.65,
            ConfidenceSourceType.MissingDataFallback => 0.25,
            _ => 0.25
        };

    private static double CalculateSupportingSources(int supportedCellCount) =>
        supportedCellCount switch
        {
            <= 0 => 0.0,
            1 => 0.9,
            _ => 1.0
        };

    private static double CalculateFreshness(IReadOnlyCollection<ScannerTableCell> supportedCells)
    {
        if (supportedCells.Count == 0) return 0.0;

        return supportedCells.Average(cell => cell.FreshnessStatus switch
        {
            CellFreshnessStatus.Live => 1.0,
            CellFreshnessStatus.PreviousTradingDay => 1.0,
            CellFreshnessStatus.Persisted => 0.9,
            CellFreshnessStatus.Missing => 0.0,
            _ => 0.0
        });
    }

    private static double CalculateWarningPenalty(ConfidenceScoringRequest request)
    {
        var warnings =
            (request.ScannerTable?.MissingDataWarnings.Count ?? 0) +
            (request.SymbolLookupTable?.MissingDataWarnings.Count ?? 0) +
            (request.SymbolLookupTable?.UnresolvedSymbols.Count ?? 0);
        return Math.Min(MaxWarningPenalty, warnings * WarningPenaltyPerWarning);
    }

    private static double CalculateNarrativeConsistency(
        string? answerText,
        IReadOnlyCollection<ScannerTableCell> supportedCells)
    {
        if (supportedCells.Count == 0) return 0.0;
        if (string.IsNullOrWhiteSpace(answerText)) return 0.75;

        var narrativeNumbers = ExtractNumbers(answerText).ToList();
        if (narrativeNumbers.Count == 0) return 0.75;

        var supportedValues = supportedCells
            .Select(cell => cell.Value!.Value)
            .Concat(supportedCells.SelectMany(cell => ExtractNumbers(cell.FormattedValue)))
            .Distinct()
            .ToList();

        return narrativeNumbers.Any(n =>
            supportedValues.Any(v => ValuesMatch(n, v)))
            ? 1.0
            : 0.5;
    }

    private static bool ValuesMatch(decimal left, decimal right) =>
        Math.Abs(left - right) <= 0.005m;

    private static IEnumerable<decimal> ExtractNumbers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var normalized = NormalizeDigits(text);
        var chars = new List<char>();

        foreach (var ch in normalized)
        {
            if (char.IsDigit(ch) || ch is '.' or '-' or '+')
            {
                chars.Add(ch);
                continue;
            }

            if (chars.Count > 0)
            {
                if (TryParse(chars, out var value)) yield return value;
                chars.Clear();
            }
        }

        if (chars.Count > 0 && TryParse(chars, out var finalValue))
            yield return finalValue;
    }

    private static string NormalizeDigits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '۰' and <= '۹' => (char)('0' + chars[i] - '۰'),
                >= '٠' and <= '٩' => (char)('0' + chars[i] - '٠'),
                '٫' => '.',
                '٬' or ',' => '\0',
                _ => chars[i]
            };
        }

        return new string(chars.Where(c => c != '\0').ToArray());
    }

    private static bool TryParse(IReadOnlyCollection<char> chars, out decimal value)
    {
        var token = new string(chars.ToArray()).Trim('.', '+', '-');
        return decimal.TryParse(
            token,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    private static IEnumerable<ScannerTableCell> GetFinancialCells(ConfidenceScoringRequest request)
    {
        if (request.ScannerTable is not null)
        {
            foreach (var cell in GetFinancialCells(request.ScannerTable.Columns, request.ScannerTable.Rows))
                yield return cell;
        }

        if (request.SymbolLookupTable is not null)
        {
            foreach (var cell in GetFinancialCells(request.SymbolLookupTable.Columns, request.SymbolLookupTable.Rows))
                yield return cell;
        }
    }

    private static IEnumerable<ScannerTableCell> GetFinancialCells(
        IReadOnlyCollection<ScannerTableColumn> columns,
        IReadOnlyCollection<ScannerTableRow> rows)
    {
        var financialColumnIds = columns
            .Where(IsFinancialColumn)
            .Select(c => c.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            foreach (var columnId in financialColumnIds)
            {
                if (row.Cells.TryGetValue(columnId, out var cell))
                    yield return cell;
            }
        }
    }

    private static int CountExpectedFinancialCells(ConfidenceScoringRequest request)
    {
        var count = 0;
        if (request.ScannerTable is not null)
            count += CountExpectedFinancialCells(request.ScannerTable.Columns, request.ScannerTable.Rows);
        if (request.SymbolLookupTable is not null)
            count += CountExpectedFinancialCells(request.SymbolLookupTable.Columns, request.SymbolLookupTable.Rows);
        return count;
    }

    private static int CountExpectedFinancialCells(
        IReadOnlyCollection<ScannerTableColumn> columns,
        IReadOnlyCollection<ScannerTableRow> rows) =>
        columns.Count(IsFinancialColumn) * rows.Count;

    private static bool IsFinancialColumn(ScannerTableColumn column) =>
        column.ColumnType is ScannerColumnType.Metric
            or ScannerColumnType.LatestPrice
            or ScannerColumnType.DailyChangePercent
            or ScannerColumnType.MarketCap;
}
