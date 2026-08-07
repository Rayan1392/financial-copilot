using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Application.Scanner;

public sealed class LlmScannerQueryParser(
    IAiModelExecutionService executionService,
    IMetricAliasResolver aliasResolver,
    IScannerQueryPlanValidator validator,
    TimeProvider timeProvider,
    IMetricAliasLearningSignalCollector? learningSignalCollector = null) : IScannerQueryParser
{
    private const string PolicyVersion = "v1";
    private const string SchemaName = "ScannerParseOutput";

    private static readonly HashSet<string> StandardColumnTerms = new(
        [
            "symbol",
            "ticker",
            "company",
            "companyname",
            "latestprice",
            "price",
            "latestpricechangepercent",
            "dailychangepct",
            "dailychangepercent",
            "changepercent",
            "percentchange",
            "marketcap",
            "marketcapitalization",
            "نماد",
            "نامنماد",
            "شرکت",
            "نامشرکت",
            "قیمت",
            "آخرینقیمت",
            "درصدتغییر",
            "تغییرقیمت",
            "درصدتغییرآخرینقیمت",
            "ارزشبازار"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly AiStructuredOutputContract ParseContract = new(
        SchemaName,
        ["detectedLanguage", "conditions", "clarificationRequired"]);

    private const string SystemPrompt =
        "You are a financial stock screening parser. " +
        "Parse the user's screening request and return structured JSON.\n\n" +
        "Instructions:\n" +
        "- Extract screening conditions: what metric, what comparison operator, and what threshold.\n" +
        "- For each condition, return the user's ORIGINAL terminology exactly as written — do not translate or resolve metric names.\n" +
        "- Detect the language of the query (e.g. 'en', 'fa').\n" +
        "- Extract any explicit column requests the user made.\n" +
        "- Set clarificationRequired=true ONLY when a metric term in the query is genuinely ambiguous (e.g. the user wrote a term that matches multiple unrelated metrics).\n" +
        "- NEVER set clarificationRequired=true because market scope, exchange, or universe is not specified. Omitted scope means the default full universe — do NOT ask which market.\n" +
        "- NEVER set clarificationRequired=true for a query that contains only clear metric conditions (e.g. PE < 5, PS < 2) even if no market is named.\n" +
        "- NEVER mention phrases not present in the user's message in the clarificationMessage.\n" +
        "- NEVER add conditions the user did not explicitly request.\n" +
        "- NEVER produce SQL.\n\n" +
        "Operators: GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal, NotEqual\n" +
        "GrowthComparison: YearOverYear, QuarterOverQuarter, MonthOverMonth, or null\n" +
        "PeriodHint: Monthly, Quarterly, TTM, LatestQuarter, LatestMonth, or null\n\n" +
        "Examples of queries that must NOT trigger clarification:\n" +
        "  'لیست نمادهای با pe کمتر از 5 و ps کمتر از 2' → clarificationRequired=false\n" +
        "  'نمادهای با پی به ای زیر 4 و پی به اس زیر 1' → clarificationRequired=false\n" +
        "  'symbols with PE below 5' → clarificationRequired=false\n\n" +
        "Schema: {\"detectedLanguage\":\"en\",\"conditions\":[{\"userTerminology\":\"P/E\",\"language\":\"en\"," +
        "\"operator\":\"LessThan\",\"threshold\":6.0,\"periodHint\":null,\"growthComparison\":null," +
        "\"inferredDefault\":false,\"inferredReason\":null}],\"requestedColumns\":[]," +
        "\"clarificationRequired\":false,\"clarificationMessage\":null}";

    public async Task<ScannerParseResult> ParseAsync(
        ScannerParseRequest request,
        CancellationToken cancellationToken)
    {
        if (TryBuildFeature116SalesGrowthPlan(request, out var salesGrowthResult))
        {
            return salesGrowthResult;
        }

        var llmOutput = await InvokeLlmAsync(request, cancellationToken);
        return BuildPlan(request, llmOutput, cancellationToken);
    }

    private bool TryBuildFeature116SalesGrowthPlan(
        ScannerParseRequest request,
        out ScannerParseResult result)
    {
        result = default!;
        var normalized = NormalizeSalesGrowthText(request.UserQuery);
        var hasGenericSalesGrowth = Feature116ContainsAny(
                normalized,
                "\u0631\u0634\u062f \u0641\u0631\u0648\u0634",
                "\u0627\u0641\u0632\u0627\u06cc\u0634 \u0641\u0631\u0648\u0634",
                "sales growth") ||
            (Feature116ContainsAny(normalized, "\u0641\u0631\u0648\u0634", "sales") &&
             Feature116ContainsAny(
                 normalized,
                 "\u0631\u0634\u062f",
                 "\u0627\u0641\u0632\u0627\u06cc\u0634",
                 "\u0631\u0634\u062f \u06a9\u0631\u062f\u0647",
                 "growth",
                 "increase",
                 "increased",
                 "grew"));
        var hasAverage12MonthMultiple =
            Feature116ContainsAny(normalized, "\u0641\u0631\u0648\u0634 \u0645\u0627\u0647\u0627\u0646\u0647", "monthly sales") &&
            Feature116ContainsAny(normalized, "\u0645\u06cc\u0627\u0646\u06af\u06cc\u0646", "\u0645\u062a\u0648\u0633\u0637", "average") &&
            Feature116ContainsAny(normalized, "12 \u0645\u0627\u0647", "12-month", "12 month") &&
            Feature116ContainsAny(normalized, "\u0628\u0631\u0627\u0628\u0631", " times", " multiple");
        var hasComparativeSalesMultiple =
            Feature116ContainsAny(normalized, "\u0641\u0631\u0648\u0634", "sales") &&
            Feature116ContainsAny(normalized, "\u0628\u0631\u0627\u0628\u0631", " times", " multiple") &&
            Feature116ContainsAny(
                normalized,
                "\u0645\u0627\u0647 \u0642\u0628\u0644",
                "\u0645\u0627\u0647 \u0645\u0634\u0627\u0628\u0647",
                "\u0633\u0627\u0644 \u0642\u0628\u0644",
                "\u0633\u0627\u0644 \u06af\u0630\u0634\u062a\u0647",
                "\u067e\u0627\u0631\u0633\u0627\u0644",
                "previous month",
                "same month",
                "previous year",
                "last year",
                "mom",
                "yoy");

        if (!hasGenericSalesGrowth && !hasAverage12MonthMultiple && !hasComparativeSalesMultiple)
        {
            return false;
        }

        var baseline = ResolveFeature116Baseline(normalized, out var baselineOrigin);
        var thresholdKind = Feature116ContainsAny(normalized, "\u0628\u0631\u0627\u0628\u0631", " times", " multiple")
            ? SalesGrowthThresholdKind.Multiple
            : SalesGrowthThresholdKind.Percent;
        var thresholdMatch = MatchFeature116Threshold(normalized, thresholdKind);

        decimal? threshold = null;
        if (thresholdMatch.Success &&
            decimal.TryParse(
                thresholdMatch.Groups["value"].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsedThreshold))
        {
            threshold = parsedThreshold;
        }
        else if (thresholdKind == SalesGrowthThresholdKind.Percent)
        {
            thresholdKind = SalesGrowthThresholdKind.Positive;
        }
        else
        {
            return false;
        }

        var comparisonOperator = ResolveFeature116Operator(normalized, thresholdKind);
        var metricCode = (baseline, thresholdKind) switch
        {
            (SalesGrowthComparisonBaseline.PreviousMonth, _) => "MONTHLY_SALES_GROWTH_MOM",
            (SalesGrowthComparisonBaseline.SameMonthPreviousYear, _) => "MONTHLY_SALES_GROWTH_YOY",
            (_, SalesGrowthThresholdKind.Multiple) => "MONTHLY_SALES_GROWTH_MULTIPLE",
            _ => "MONTHLY_SALES_GROWTH_PERCENT"
        };
        var growthComparison = baseline switch
        {
            SalesGrowthComparisonBaseline.PreviousMonth => GrowthComparison.MonthOverMonth,
            SalesGrowthComparisonBaseline.SameMonthPreviousYear => GrowthComparison.YearOverYear,
            _ => (GrowthComparison?)null
        };
        var thresholdOrigin = threshold.HasValue ? FilterOrigin.Explicit : FilterOrigin.InferredDefault;

        var condition = new ScannerCondition(
            new ScannerMetricReference(
                "\u0631\u0634\u062f \u0641\u0631\u0648\u0634",
                new MetricCode(metricCode),
                new MetricVersion("v1"),
                new CalculationPolicyVersion($"{metricCode}_v1"),
                FiscalPeriodType.Monthly,
                growthComparison),
            comparisonOperator,
            threshold ?? 0m,
            thresholdOrigin);

        var salesPlan = new SalesGrowthScannerPlan(
            new SalesGrowthScannerSemantics(
                baseline,
                thresholdKind,
                comparisonOperator,
                threshold,
                baselineOrigin == FilterOrigin.Explicit && thresholdOrigin == FilterOrigin.Explicit
                    ? FilterOrigin.Explicit
                    : FilterOrigin.InferredDefault,
                SalesGrowthPolicyVersions.V1,
                baselineOrigin,
                thresholdOrigin));

        var language = normalized.Any(character => character is >= '\u0600' and <= '\u06ff')
            ? "fa"
            : request.Language;
        var plan = new ScannerQueryPlan(
            Guid.NewGuid(),
            request.UserQuery,
            language,
            [condition],
            [],
            false,
            null,
            [],
            [],
            timeProvider.GetUtcNow(),
            PolicyVersion,
            salesPlan);

        var validationError = validator.Validate(plan);
        result = validationError is null
            ? new ScannerParseResult(plan, Succeeded: true)
            : new ScannerParseResult(plan, Succeeded: false, validationError);
        return true;
    }

    private static SalesGrowthComparisonBaseline ResolveFeature116Baseline(
        string normalized,
        out FilterOrigin origin)
    {
        origin = FilterOrigin.Explicit;
        if (Feature116ContainsAny(normalized, "\u0645\u06cc\u0627\u0646\u06af\u06cc\u0646", "\u0645\u062a\u0648\u0633\u0637", "average"))
        {
            return SalesGrowthComparisonBaseline.AveragePrevious12Months;
        }

        if (Feature116ContainsAny(normalized, "\u0645\u0627\u0647 \u0642\u0628\u0644", "\u062f\u0648\u0631\u0647 \u0642\u0628\u0644", "previous month", "mom"))
        {
            return SalesGrowthComparisonBaseline.PreviousMonth;
        }

        if (Feature116ContainsAny(
                normalized,
                "\u0633\u0627\u0644 \u06af\u0630\u0634\u062a\u0647",
                "\u0633\u0627\u0644 \u0642\u0628\u0644",
                "\u067e\u0627\u0631\u0633\u0627\u0644",
                "\u0645\u0627\u0647 \u0645\u0634\u0627\u0628\u0647",
                "\u062f\u0648\u0631\u0647 \u0645\u0634\u0627\u0628\u0647",
                "previous year",
                "last year",
                "yoy"))
        {
            return SalesGrowthComparisonBaseline.SameMonthPreviousYear;
        }

        origin = FilterOrigin.InferredDefault;
        return SalesGrowthComparisonBaseline.SameMonthPreviousYear;
    }

    private static System.Text.RegularExpressions.Match MatchFeature116Threshold(
        string normalized,
        SalesGrowthThresholdKind thresholdKind)
    {
        var suffix = thresholdKind == SalesGrowthThresholdKind.Multiple
            ? "(?:\\u0628\\u0631\\u0627\\u0628\\u0631|times?|multiple)"
            : "(?:%|\\u062f\\u0631\\u0635\\u062f|percent)";

        return System.Text.RegularExpressions.Regex.Match(
            normalized,
            $@"(?<value>\d+(?:\.\d+)?)\s*{suffix}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static ConditionOperator ResolveFeature116Operator(
        string normalized,
        SalesGrowthThresholdKind thresholdKind)
    {
        if (thresholdKind == SalesGrowthThresholdKind.Positive)
        {
            return ConditionOperator.GreaterThan;
        }

        if (Feature116ContainsAny(normalized, "\u062d\u062f\u0627\u0642\u0644", "\u06a9\u0645\u062a\u0631 \u0646\u0628\u0627\u0634\u062f", "at least", "no less than"))
        {
            return ConditionOperator.GreaterThanOrEqual;
        }

        if (Feature116ContainsAny(normalized, "\u0628\u06cc\u0634 \u0627\u0632", "\u0628\u06cc\u0634\u062a\u0631 \u0627\u0632", "\u0628\u0627\u0644\u0627\u06cc", "over", "more than", "above"))
        {
            return ConditionOperator.GreaterThan;
        }

        // Feature 116 defines unqualified multiple expressions such as "2 times" as
        // at-least/effectively that multiple, avoiding a brittle exact-decimal match.
        return thresholdKind == SalesGrowthThresholdKind.Multiple
            ? ConditionOperator.GreaterThanOrEqual
            : ConditionOperator.GreaterThan;
    }

    private static bool Feature116ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeSalesGrowthText(string value)
    {
        var normalized = value.Trim()
            .Replace('٫', '.')
            .Replace('٬', ',');

        for (var digit = 0; digit <= 9; digit++)
        {
            normalized = normalized
                .Replace((char)('\u06F0' + digit), (char)('0' + digit))
                .Replace((char)('\u0660' + digit), (char)('0' + digit));
        }

        return normalized;
    }

    private async Task<LlmScannerParseOutput> InvokeLlmAsync(
        ScannerParseRequest request,
        CancellationToken cancellationToken)
    {
        var selection = new AiModelSelectionRequest(
            request.TenantId,
            AiWorkloadKind.ScannerParsing,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            request.CorrelationId);

        var aiRequest = new AiModelRequest(
            request.CorrelationId,
            request.TenantId,
            AiWorkloadKind.ScannerParsing,
            [
                new AiConversationMessage(AiMessageRole.System, SystemPrompt),
                new AiConversationMessage(AiMessageRole.User, request.UserQuery)
            ],
            StructuredOutput: ParseContract);

        var result = await executionService.ExecuteAsync(selection, aiRequest, cancellationToken);
        return ParseLlmOutput(result.StructuredJson);
    }

    private static LlmScannerParseOutput ParseLlmOutput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LlmScannerParseOutput("en", [], [], true, "AI model returned empty output.");
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var language = root.TryGetProperty("detectedLanguage", out var langProp)
                ? langProp.GetString() ?? "en"
                : "en";

            var clarificationRequired = root.TryGetProperty("clarificationRequired", out var clarProp) &&
                clarProp.ValueKind == JsonValueKind.True;

            var clarificationMessage = root.TryGetProperty("clarificationMessage", out var clarMsgProp)
                ? clarMsgProp.GetString()
                : null;

            var conditions = new List<LlmConditionCandidate>();
            if (root.TryGetProperty("conditions", out var condsProp) &&
                condsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var cond in condsProp.EnumerateArray())
                {
                    var terminology = GetString(cond, "userTerminology") ?? string.Empty;
                    var condLanguage = GetString(cond, "language") ?? language;
                    var operatorStr = GetString(cond, "operator") ?? "LessThan";
                    var threshold = cond.TryGetProperty("threshold", out var threshProp) &&
                        threshProp.TryGetDecimal(out var threshVal) ? threshVal : 0;
                    var periodHint = GetString(cond, "periodHint");
                    var growthComparison = GetString(cond, "growthComparison");
                    var inferredDefault = cond.TryGetProperty("inferredDefault", out var infProp) &&
                        infProp.ValueKind == JsonValueKind.True;
                    var inferredReason = GetString(cond, "inferredReason");

                    if (!string.IsNullOrWhiteSpace(terminology))
                    {
                        conditions.Add(new LlmConditionCandidate(
                            terminology, condLanguage, operatorStr, threshold,
                            periodHint, growthComparison, inferredDefault, inferredReason));
                    }
                }
            }

            var requestedColumns = new List<string>();
            if (root.TryGetProperty("requestedColumns", out var colsProp) &&
                colsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var col in colsProp.EnumerateArray())
                {
                    var colStr = col.GetString();
                    if (!string.IsNullOrWhiteSpace(colStr))
                    {
                        requestedColumns.Add(colStr);
                    }
                }
            }

            return new LlmScannerParseOutput(language, conditions, requestedColumns, clarificationRequired, clarificationMessage);
        }
        catch (JsonException)
        {
            return new LlmScannerParseOutput("en", [], [], true, "AI model output was not valid JSON.");
        }
    }

    private ScannerParseResult BuildPlan(ScannerParseRequest request, LlmScannerParseOutput llmOutput, CancellationToken cancellationToken = default)
    {
        var conditions = new List<ScannerCondition>();
        var clarificationItems = new List<ScannerClarificationItem>();
        var clarificationRequired = llmOutput.ClarificationRequired;
        var clarificationMessage = llmOutput.ClarificationMessage;

        foreach (var candidate in llmOutput.Conditions)
        {
            if (string.IsNullOrWhiteSpace(candidate.UserTerminology))
            {
                continue;
            }

            var context = new MetricResolutionContext(
                PeriodType: MapPeriodHint(candidate.PeriodHint),
                Comparison: MapGrowthComparison(candidate.GrowthComparison));

            var resolution = aliasResolver.ResolveAlias(
                candidate.UserTerminology,
                NormalizeBcp47(candidate.Language),
                context,
                request.AsOf);

            switch (resolution.Status)
            {
                case MetricResolutionStatus.Resolved:
                    var definition = resolution.Candidates.Single();
                    var metricRef = new ScannerMetricReference(
                        candidate.UserTerminology,
                        definition.Code,
                        definition.Version,
                        ResolvePolicy(definition.Code),
                        ResolvePeriod(definition, candidate.PeriodHint),
                        MapGrowthComparison(candidate.GrowthComparison));

                    conditions.Add(new ScannerCondition(
                        metricRef,
                        MapOperator(candidate.Operator),
                        candidate.Threshold,
                        candidate.InferredDefault ? FilterOrigin.InferredDefault : FilterOrigin.Explicit,
                        candidate.InferredReason));
                    break;

                case MetricResolutionStatus.Ambiguous:
                    clarificationRequired = true;
                    clarificationItems.Add(new ScannerClarificationItem(
                        candidate.UserTerminology,
                        resolution.ClarificationMessage ?? "Ambiguous metric expression.",
                        resolution.Candidates.Select(d => d.Code.Value).ToArray()));
                    EmitLearningSignal(candidate.UserTerminology, NormalizeBcp47(candidate.Language),
                        MetricResolutionStatus.Ambiguous, null, request.CorrelationId, cancellationToken);
                    break;

                case MetricResolutionStatus.NotFound:
                    clarificationRequired = true;
                    clarificationItems.Add(new ScannerClarificationItem(
                        candidate.UserTerminology,
                        "Metric term is not recognized in the supported catalog.",
                        []));
                    EmitLearningSignal(candidate.UserTerminology, NormalizeBcp47(candidate.Language),
                        MetricResolutionStatus.NotFound, null, request.CorrelationId, cancellationToken);
                    break;
            }
        }

        // If every metric condition resolved cleanly and there are no unresolved terms,
        // the LLM must not block the query with a clarification. LLM-originated clarifications
        // for reasons unrelated to metric resolution (e.g. asking for market scope that was
        // never mentioned by the user) are suppressed here as a deterministic backstop.
        if (clarificationRequired && clarificationItems.Count == 0 && conditions.Count > 0)
        {
            clarificationRequired = false;
            clarificationMessage = null;
        }

        if (clarificationRequired && clarificationItems.Count > 0 && clarificationMessage is null)
        {
            var unresolved = string.Join(", ", clarificationItems.Select(i => $"'{i.UserTerminology}'"));
            clarificationMessage = $"The following metric terms could not be uniquely resolved: {unresolved}.";
        }

        // Build requested column list. Standard columns and condition metrics are added
        // deterministically by the backend, so LLM requestedColumns only carry extra metrics.
        var columnWarnings = new List<string>();
        var requestedColumns = new List<ScannerColumnRequest>();
        var seenColumnIdentifiers = conditions
            .Select(condition => condition.MetricReference.MetricCode.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userColumns = new List<string>();

        foreach (var column in llmOutput.RequestedColumns)
        {
            if (string.IsNullOrWhiteSpace(column) || IsStandardColumnTerm(column))
            {
                continue;
            }

            var identifier = ResolveRequestedColumnIdentifier(
                column,
                NormalizeBcp47(llmOutput.DetectedLanguage),
                request.AsOf);

            if (seenColumnIdentifiers.Add(identifier))
            {
                userColumns.Add(identifier);
            }
        }

        if (userColumns.Count > ScannerQueryPlan.MaxDisplayColumns)
        {
            columnWarnings.Add(
                $"Column request exceeded the {ScannerQueryPlan.MaxDisplayColumns}-column limit. " +
                $"Only the first {ScannerQueryPlan.MaxDisplayColumns} columns were accepted.");
            userColumns = userColumns.Take(ScannerQueryPlan.MaxDisplayColumns).ToList();
        }

        foreach (var col in userColumns)
        {
            requestedColumns.Add(new ScannerColumnRequest(col, IsUserRequested: true));
        }

        var plan = new ScannerQueryPlan(
            Guid.NewGuid(),
            request.UserQuery,
            llmOutput.DetectedLanguage,
            conditions,
            requestedColumns,
            clarificationRequired,
            clarificationMessage,
            clarificationItems,
            columnWarnings,
            timeProvider.GetUtcNow(),
            PolicyVersion);

        var validationError = validator.Validate(plan);
        if (validationError is not null)
        {
            return new ScannerParseResult(plan, Succeeded: false, validationError);
        }

        return new ScannerParseResult(plan, Succeeded: true);
    }

    private static CalculationPolicyVersion ResolvePolicy(MetricCode metricCode) =>
        new($"{metricCode.Value}_v1");

    private static FiscalPeriodType ResolvePeriod(FinancialMetricDefinition definition, string? periodHint)
    {
        if (!string.IsNullOrWhiteSpace(periodHint))
        {
            var mapped = MapPeriodHint(periodHint);
            if (mapped.HasValue && definition.SupportedPeriodTypes.Contains(mapped.Value))
            {
                return mapped.Value;
            }
        }

        return definition.SupportedPeriodTypes.FirstOrDefault();
    }

    private static ConditionOperator MapOperator(string operatorStr) =>
        operatorStr.Trim() switch
        {
            "GreaterThan" => ConditionOperator.GreaterThan,
            "GreaterThanOrEqual" => ConditionOperator.GreaterThanOrEqual,
            "LessThanOrEqual" => ConditionOperator.LessThanOrEqual,
            "Equal" => ConditionOperator.Equal,
            "NotEqual" => ConditionOperator.NotEqual,
            _ => ConditionOperator.LessThan
        };

    private static FiscalPeriodType? MapPeriodHint(string? hint) =>
        hint?.Trim() switch
        {
            "Monthly" => FiscalPeriodType.Monthly,
            "Quarterly" => FiscalPeriodType.ThreeMonths,
            "TTM" => FiscalPeriodType.TrailingTwelveMonths,
            "LatestQuarter" => FiscalPeriodType.LatestQuarter,
            "LatestMonth" => FiscalPeriodType.LatestMonth,
            _ => null
        };

    private static GrowthComparison? MapGrowthComparison(string? comparison) =>
        comparison?.Trim() switch
        {
            "YearOverYear" => GrowthComparison.YearOverYear,
            "QuarterOverQuarter" => GrowthComparison.QuarterOverQuarter,
            "MonthOverMonth" => GrowthComparison.MonthOverMonth,
            _ => null
        };

    private string ResolveRequestedColumnIdentifier(string column, string language, DateOnly asOf)
    {
        var trimmed = column.Trim();
        var metric = aliasResolver.ResolveAlias(
            trimmed,
            language,
            new MetricResolutionContext(),
            asOf);

        if (metric.Status == MetricResolutionStatus.Resolved)
        {
            return metric.Candidates.Single().Code.Value;
        }

        return TryResolveCatalogMetricCode(trimmed, language, asOf) ?? trimmed;
    }

    private static string? TryResolveCatalogMetricCode(string identifier, string language, DateOnly asOf)
    {
        var normalizedIdentifier = NormalizeColumnTerm(identifier);
        foreach (var definition in PhaseOneFinancialSemanticCatalog.Definitions
            .Where(definition =>
                definition.EffectiveFrom <= asOf &&
                (definition.EffectiveTo is null || definition.EffectiveTo >= asOf)))
        {
            if (string.Equals(NormalizeColumnTerm(definition.Code.Value), normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                definition.Aliases.Any(alias =>
                    string.Equals(alias.Language, language, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NormalizeColumnTerm(alias.Expression), normalizedIdentifier, StringComparison.OrdinalIgnoreCase)))
            {
                return definition.Code.Value;
            }
        }

        return null;
    }

    private static bool IsStandardColumnTerm(string column) =>
        StandardColumnTerms.Contains(NormalizeColumnTerm(column));

    private static string NormalizeColumnTerm(string term)
    {
        var chars = term.Trim().ToLowerInvariant()
            .Where(ch => !char.IsWhiteSpace(ch) && ch is not '_' and not '-' and not '/' and not '%' and not '.')
            .ToArray();
        return new string(chars);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    // Expands short ISO language codes from LLM output to BCP-47 tags used in the catalog.
    private static string NormalizeBcp47(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "en" => "en-US",
            "fa" => "fa-IR",
            _ => language
        };

    private void EmitLearningSignal(
        string userExpression, string language, MetricResolutionStatus failureKind,
        string? actorId, string? correlationId, CancellationToken cancellationToken)
    {
        if (learningSignalCollector is null) return;
        var signal = new MetricAliasLearningSignal(
            UserExpression: userExpression,
            NormalizedExpression: userExpression,
            Language: language,
            FailureKind: failureKind,
            ActorId: actorId,
            CorrelationId: correlationId,
            OccurredAt: timeProvider.GetUtcNow());
        _ = learningSignalCollector.CollectAsync(signal, CancellationToken.None);
    }
}

public sealed class ScannerQueryPlanValidator : IScannerQueryPlanValidator
{
    public string? Validate(ScannerQueryPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.OriginalUserQuery))
        {
            return "Scanner plan must retain the original user query.";
        }

        if (plan.Conditions.Count == 0 && plan.SalesGrowth is null && !plan.ClarificationRequired)
        {
            return "Scanner plan must contain at least one condition or require clarification.";
        }

        foreach (var condition in plan.Conditions)
        {
            if (string.IsNullOrWhiteSpace(condition.MetricReference.MetricCode.Value))
            {
                return "All conditions must resolve to a canonical MetricCode.";
            }
        }

        if (plan.RequestedColumns.Count > ScannerQueryPlan.MaxDisplayColumns)
        {
            return $"Requested columns exceed the {ScannerQueryPlan.MaxDisplayColumns}-column maximum.";
        }

        if (plan.SalesGrowth is not null)
        {
            var salesGrowth = plan.SalesGrowth;
            if (salesGrowth.CurrentObservationSelector !=
                SalesGrowthCurrentObservationSelector.LatestEligibleCompleteMonthlySales)
            {
                return "Sales-growth scanner requires the latest eligible complete monthly-sales observation selector.";
            }

            if (salesGrowth.Semantics.ThresholdKind == SalesGrowthThresholdKind.Positive &&
                salesGrowth.Semantics.ComparisonOperator != ConditionOperator.GreaterThan)
            {
                return "Positive sales growth must use the strict GreaterThan operator.";
            }

            if (salesGrowth.Semantics.ThresholdKind is SalesGrowthThresholdKind.Percent or SalesGrowthThresholdKind.Multiple &&
                salesGrowth.Semantics.ThresholdValue is null)
            {
                return "Percent and multiple sales-growth thresholds require a numeric value.";
            }

            if (salesGrowth.Semantics.ThresholdKind == SalesGrowthThresholdKind.Multiple &&
                salesGrowth.Semantics.ThresholdValue <= 0)
            {
                return "Sales-growth multiple thresholds must be greater than zero.";
            }

            if (salesGrowth.Page < 1 || salesGrowth.PageSize < 1 ||
                salesGrowth.PageSize > SalesGrowthScannerPlan.MaximumPageSize)
            {
                return $"Sales-growth pagination must use page >= 1 and page size between 1 and {SalesGrowthScannerPlan.MaximumPageSize}.";
            }

            var universe = salesGrowth.EffectiveMarketUniverse;
            if (universe.MaximumSymbols < 1 || universe.MaximumSymbols > SalesGrowthScannerPlan.MaximumSymbols)
            {
                return $"Sales-growth market universe must contain between 1 and {SalesGrowthScannerPlan.MaximumSymbols} symbols.";
            }

            var sort = salesGrowth.EffectiveSort;
            if (sort.Key != SalesGrowthSortKey.GrowthPercent ||
                sort.Direction is not (SalesGrowthSortDirection.Descending or SalesGrowthSortDirection.Ascending))
            {
                return "Sales-growth sorting supports only GrowthPercent with an explicit direction.";
            }

            if (salesGrowth.EffectiveRequestedDisplayColumns.Count > ScannerQueryPlan.MaxDisplayColumns)
            {
                return $"Sales-growth display columns exceed the {ScannerQueryPlan.MaxDisplayColumns}-column maximum.";
            }
        }

        return null;
    }
}
