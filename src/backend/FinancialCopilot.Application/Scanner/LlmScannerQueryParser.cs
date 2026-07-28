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
        var llmOutput = await InvokeLlmAsync(request, cancellationToken);
        return BuildPlan(request, llmOutput, cancellationToken);
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

        if (plan.Conditions.Count == 0 && !plan.ClarificationRequired)
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

        return null;
    }
}
