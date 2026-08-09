using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

public sealed class LlmSymbolLookupParser(
    IAiModelExecutionService executionService,
    IMetricAliasResolver aliasResolver,
    IDirectMetricRoutingRegistry routingRegistry,
    TimeProvider? timeProvider = null,
    IMetricAliasLearningSignalCollector? learningSignalCollector = null) : ISymbolLookupParser
{
    private static readonly string[] DirectPeTerms =
    [
        "نسبت پی به ای",
        "پی به ای",
        "پی ای",
        "پی‌ای",
        "پی بر ای",
        "نسبت قیمت به سود",
        "قیمت به سود",
        "price-to-earnings",
        "price to earnings",
        "p/e"
    ];

    private static readonly string[] DirectMonthlySalesTerms =
    [
        "فروش YTD تا ماه قبل",
        "فروش YTD تا ماه گذشته",
        "متوسط فروش 12 ماهه",
        "متوسط فروش ۱۲ ماهه",
        "میانگین فروش 12 ماهه",
        "میانگین فروش ۱۲ ماهه",
        "آخرین فروش ماهانه",
        "فروش ماهانه",
        "فروش ماهیانه",
        "فروش آخرین ماه",
        "فروش این ماه",
        "مبلغ فروش",
        "آخرین فروش",
        "فروش YTD",
        "فروش ماه",
        "فروش"
    ];

    private static readonly string[] DirectDailyChangeTerms =
    [
        "درصد تغییر قیمت",
        "درصد تغییر روزانه",
        "تغییر روزانه درصدی",
        "تغییر قیمت",
        "تغییر روزانه"
    ];

    private static readonly string[] DirectPriceTerms =
    [
        "آخرین قیمت",
        "قیمت امروز",
        "قیمت پایانی",
        "قیمت"
    ];

    private static readonly string[] DirectLookupNoiseTerms =
    [
        "؟",
        "?",
        "چقدر است",
        "چقدر هست",
        "چقدر بوده",
        "چقدر",
        "است",
        "هست",
        "بوده",
        "برابر است",
        "برابر",
        "نماد",
        "سهم",
        "شرکت",
        "برای",
        "را",
        ":"
    ];

    private const string SchemaName = "SymbolLookupParseOutput";
    private static readonly AiStructuredOutputContract LookupContract = new(
        SchemaName,
        ["pairs", "detectedLanguage"]);

    private const string SystemPrompt =
        "You are a financial symbol lookup parser. " +
        "The user wants to retrieve specific financial metrics for one or more named companies or stock symbols.\n\n" +
        "Instructions:\n" +
        "- Extract each (symbol name, metric term) pair from the user message.\n" +
        "- Return the symbol name EXACTLY as the user wrote it — do not translate or resolve it.\n" +
        "- Return the metric term EXACTLY as the user wrote it — do not translate or resolve it.\n" +
        "- Detect the language of the query (e.g. 'en', 'fa').\n" +
        "- Set clarificationRequired=true only if no valid (symbol, metric) pairs can be extracted.\n" +
        "- NEVER produce SQL or code.\n\n" +
        "Example JSON: {\"detectedLanguage\":\"fa\",\"pairs\":[{\"symbolName\":\"حفاری\",\"metricTerm\":\"P/E\"}]," +
        "\"clarificationRequired\":false,\"clarificationMessage\":null}";

    public async Task<SymbolLookupParseResult> ParseAsync(
        SymbolLookupParseRequest request,
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
                new AiConversationMessage(AiMessageRole.User, request.Message)
            ],
            StructuredOutput: LookupContract);

        var result = await executionService.ExecuteAsync(selection, aiRequest, cancellationToken);
        var llmOutput = ParseLlmOutput(result.StructuredJson);

        return BuildParseResult(request, llmOutput, cancellationToken);
    }

    private static LlmLookupParseOutput ParseLlmOutput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LlmLookupParseOutput("fa", [], true, "AI model returned empty output.");
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var language = root.TryGetProperty("detectedLanguage", out var langProp)
                ? langProp.GetString() ?? "fa"
                : "fa";

            var clarificationRequired = root.TryGetProperty("clarificationRequired", out var clarProp) &&
                clarProp.ValueKind == JsonValueKind.True;

            var clarificationMessage = root.TryGetProperty("clarificationMessage", out var clarMsgProp)
                ? clarMsgProp.GetString()
                : null;

            var pairs = new List<LlmLookupPairOutput>();
            if (root.TryGetProperty("pairs", out var pairsProp) &&
                pairsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var pair in pairsProp.EnumerateArray())
                {
                    var symbolName = pair.TryGetProperty("symbolName", out var symProp) &&
                        symProp.ValueKind == JsonValueKind.String
                        ? symProp.GetString()
                        : null;
                    var metricTerm = pair.TryGetProperty("metricTerm", out var metProp) &&
                        metProp.ValueKind == JsonValueKind.String
                        ? metProp.GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(symbolName) && !string.IsNullOrWhiteSpace(metricTerm))
                    {
                        pairs.Add(new LlmLookupPairOutput(symbolName, metricTerm));
                    }
                }
            }

            return new LlmLookupParseOutput(language, pairs, clarificationRequired, clarificationMessage);
        }
        catch (JsonException)
        {
            return new LlmLookupParseOutput("fa", [], true, "AI model output was not valid JSON.");
        }
    }

    private SymbolLookupParseResult BuildParseResult(
        SymbolLookupParseRequest request,
        LlmLookupParseOutput llmOutput,
        CancellationToken cancellationToken = default)
    {
        var directRouting = routingRegistry.TryResolve(request.Message, request.AsOf);
        var deterministicPair = TryParseDirectLookup(request.Message, request.AsOf, directRouting);
        if (llmOutput.ClarificationRequired || llmOutput.Pairs.Count == 0)
        {
            if (deterministicPair is not null)
            {
                return new SymbolLookupParseResult([deterministicPair], LookupParseStatus.Parsed);
            }

            return new SymbolLookupParseResult(
                [],
                LookupParseStatus.ClarificationRequired,
                llmOutput.ClarificationMessage);
        }

        var resolvedPairs = new List<SymbolLookupParsedPair>();
        var clarificationMessages = new List<string>();

        foreach (var pair in llmOutput.Pairs)
        {
            var language = NormalizeBcp47(llmOutput.DetectedLanguage);
            var metricTerm = SelectResolvableMetricTerm(
                directRouting is not null &&
                !string.Equals(directRouting.MetricCode.Value, "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase) &&
                directRouting.MatchedPhrase.Length > pair.MetricTerm.Length
                    ? directRouting.MatchedPhrase
                    : pair.MetricTerm,
                request.Message,
                language,
                request.AsOf);
            var resolution = aliasResolver.ResolveAlias(
                metricTerm,
                language,
                new MetricResolutionContext(PeriodType: null, Comparison: null),
                request.AsOf);

            MetricCode? resolvedCode = resolution.Status == MetricResolutionStatus.Resolved
                ? resolution.Candidates.Single().Code
                : null;

            if (resolution.Status == MetricResolutionStatus.NotFound)
            {
                clarificationMessages.Add(
                    $"Metric term '{pair.MetricTerm}' is not recognized in the supported catalog.");
                EmitLearningSignal(pair.MetricTerm, language, MetricResolutionStatus.NotFound,
                    request.CorrelationId, cancellationToken);
            }
            else if (resolution.Status == MetricResolutionStatus.Ambiguous)
            {
                clarificationMessages.Add(
                    $"Metric term '{pair.MetricTerm}' is ambiguous: " +
                    string.Join(", ", resolution.Candidates.Select(c => c.Code.Value)));
                EmitLearningSignal(pair.MetricTerm, language, MetricResolutionStatus.Ambiguous,
                    request.CorrelationId, cancellationToken);
            }

            var periodSelector = resolvedCode is null
                ? null
                : routingRegistry.ResolvePeriodSelector(request.Message, resolvedCode);

            resolvedPairs.Add(new SymbolLookupParsedPair(
                pair.SymbolName,
                resolvedCode,
                pair.MetricTerm,
                periodSelector));
        }

        // If the model missed a direct PE/P-E company-name lookup, recover from the
        // original user message. This keeps common Persian PE questions off the
        // unresolved-symbol path when the LLM extracts no usable pair.
        if (deterministicPair is not null && resolvedPairs.All(p => p.ResolvedMetricCode is null))
        {
            return new SymbolLookupParseResult([deterministicPair], LookupParseStatus.Parsed);
        }

        // If no pairs produced a resolvable metric, require clarification.
        var validPairs = resolvedPairs.Where(p => p.ResolvedMetricCode is not null).ToList();
        if (validPairs.Count == 0)
        {
            var message = clarificationMessages.Count > 0
                ? string.Join(" ", clarificationMessages)
                : "None of the requested metric terms could be resolved.";
            return new SymbolLookupParseResult(resolvedPairs, LookupParseStatus.ClarificationRequired, message);
        }

        return new SymbolLookupParseResult(resolvedPairs, LookupParseStatus.Parsed);
    }

    private SymbolLookupParsedPair? TryParseDirectLookup(
        string userMessage,
        DateOnly asOf,
        DirectMetricRoutingMatch? directRouting = null)
    {
        var match = directRouting ?? routingRegistry.TryResolve(userMessage, asOf);
        if (match is null)
        {
            return null;
        }

        var symbolName = routingRegistry.StripResolvedPhrase(userMessage, match);
        return symbolName.Length == 0
            ? null
            : new SymbolLookupParsedPair(
                symbolName,
                match.MetricCode,
                match.MatchedPhrase,
                match.PeriodSelector);
    }

    private void EmitLearningSignal(
        string userExpression, string language, MetricResolutionStatus failureKind,
        string? correlationId, CancellationToken cancellationToken)
    {
        if (learningSignalCollector is null) return;
        var signal = new MetricAliasLearningSignal(
            UserExpression: userExpression,
            NormalizedExpression: userExpression,
            Language: language,
            FailureKind: failureKind,
            ActorId: null,
            CorrelationId: correlationId,
            OccurredAt: (timeProvider ?? TimeProvider.System).GetUtcNow());
        _ = learningSignalCollector.CollectAsync(signal, CancellationToken.None);
    }

    private string SelectResolvableMetricTerm(
        string metricTerm,
        string userMessage,
        string language,
        DateOnly asOf)
    {
        var explicitMonthlyCompanionTerm = SelectExplicitMonthlyActivityMetricTerm(userMessage);
        if (explicitMonthlyCompanionTerm is not null)
        {
            return explicitMonthlyCompanionTerm;
        }

        if (ShouldForceMonthlySalesSnapshot(metricTerm, userMessage))
        {
            return "آخرین فروش";
        }

        var direct = aliasResolver.ResolveAlias(
            metricTerm,
            language,
            new MetricResolutionContext(PeriodType: null, Comparison: null),
            asOf);

        if (direct.Status != MetricResolutionStatus.NotFound)
        {
            return metricTerm;
        }

        var segments = metricTerm
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (segments.Length <= 1)
        {
            return metricTerm;
        }

        foreach (var segment in segments.Where(segment =>
                     userMessage.Contains(segment, StringComparison.OrdinalIgnoreCase)))
        {
            var resolution = aliasResolver.ResolveAlias(
                segment,
                language,
                new MetricResolutionContext(PeriodType: null, Comparison: null),
                asOf);

            if (resolution.Status == MetricResolutionStatus.Resolved)
            {
                return segment;
            }
        }

        foreach (var segment in segments)
        {
            var resolution = aliasResolver.ResolveAlias(
                segment,
                language,
                new MetricResolutionContext(PeriodType: null, Comparison: null),
                asOf);

            if (resolution.Status == MetricResolutionStatus.Resolved)
            {
                return segment;
            }
        }

        return metricTerm;
    }

    private static string? SelectExplicitMonthlyActivityMetricTerm(string userMessage)
    {
        var normalizedMessage = NormalizePersianText(userMessage);

        if (normalizedMessage.Contains("نسبت فروش به تولید", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("نسبت مقدار فروش به تولید", StringComparison.OrdinalIgnoreCase))
        {
            return "نسبت فروش به تولید";
        }

        if (normalizedMessage.Contains("میزان رشد تولید", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("رشد تولید نسبت به سال قبل", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("رشد تولید ماهانه سالانه", StringComparison.OrdinalIgnoreCase))
        {
            return "میزان رشد تولید";
        }

        if (normalizedMessage.Contains("میزان رشد فروش", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("رشد فروش نسبت به سال قبل", StringComparison.OrdinalIgnoreCase))
        {
            return "میزان رشد فروش";
        }

        if (normalizedMessage.Contains("رشد فروش", StringComparison.OrdinalIgnoreCase) &&
            !normalizedMessage.Contains("ماه قبل", StringComparison.OrdinalIgnoreCase) &&
            !normalizedMessage.Contains("فصلی", StringComparison.OrdinalIgnoreCase) &&
            !normalizedMessage.Contains("درآمد", StringComparison.OrdinalIgnoreCase))
        {
            return "میزان رشد فروش";
        }

        if (normalizedMessage.Contains("فروش YTD تا ماه قبل", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("فروش YTD تا ماه گذشته", StringComparison.OrdinalIgnoreCase))
        {
            return "فروش YTD تا ماه قبل";
        }

        if (normalizedMessage.Contains("فروش YTD", StringComparison.OrdinalIgnoreCase))
        {
            return "فروش YTD";
        }

        if (normalizedMessage.Contains("متوسط فروش 12 ماهه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("متوسط فروش ۱۲ ماهه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("میانگین فروش 12 ماهه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("میانگین فروش ۱۲ ماهه", StringComparison.OrdinalIgnoreCase))
        {
            return "متوسط فروش 12 ماهه";
        }

        return null;
    }

    private static bool ShouldForceMonthlySalesSnapshot(string metricTerm, string userMessage)
    {
        var normalizedTerm = NormalizePersianText(metricTerm).Trim();
        if (!IsSalesSnapshotQuery(userMessage) || !IsSalesRelatedLookupTerm(normalizedTerm))
        {
            return false;
        }

        return true;
    }

    private static bool IsSalesRelatedLookupTerm(string normalizedTerm) =>
        string.Equals(normalizedTerm, "فروش", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("فروش ماه", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("آخرین فروش", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("متوسط فروش", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("میانگین فروش", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("مبلغ فروش", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("فروش آخرین ماه", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("YTD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalizedTerm, "sales", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("sale", StringComparison.OrdinalIgnoreCase) ||
        normalizedTerm.Contains("revenue", StringComparison.OrdinalIgnoreCase);

    private static bool IsSalesSnapshotQuery(string userMessage)
    {
        var normalizedMessage = NormalizePersianText(userMessage);
        return normalizedMessage.Contains("آخرین فروش", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("فروش ماه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("فروش ماهانه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("فروش ماهیانه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("فروش این ماه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("مبلغ فروش", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("فروش آخرین ماه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("فروش YTD", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("متوسط فروش 12 ماهه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("متوسط فروش ۱۲ ماهه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("میانگین فروش 12 ماهه", StringComparison.OrdinalIgnoreCase) ||
            normalizedMessage.Contains("میانگین فروش ۱۲ ماهه", StringComparison.OrdinalIgnoreCase) ||
            IsShortSymbolSalesQuestion(normalizedMessage);
    }

    private static bool IsShortSymbolSalesQuestion(string normalizedMessage)
    {
        var compact = normalizedMessage
            .Replace("؟", " ", StringComparison.Ordinal)
            .Replace("?", " ", StringComparison.Ordinal)
            .Trim();

        return compact.StartsWith("فروش ", StringComparison.OrdinalIgnoreCase) &&
            !compact.Contains("فصلی", StringComparison.OrdinalIgnoreCase) &&
            !compact.Contains("درآمد", StringComparison.OrdinalIgnoreCase) &&
            !compact.Contains("خالص", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePersianText(string text) =>
        text.Replace('ي', 'ی').Replace('ك', 'ک');

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeBcp47(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "en" => "en-US",
            "fa" => "fa-IR",
            _ => language
        };
}
