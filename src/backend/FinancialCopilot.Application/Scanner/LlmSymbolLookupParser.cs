using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

public sealed class LlmSymbolLookupParser(
    IAiModelExecutionService executionService,
    IMetricAliasResolver aliasResolver,
    TimeProvider? timeProvider = null,
    IMetricAliasLearningSignalCollector? learningSignalCollector = null) : ISymbolLookupParser
{
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
        if (llmOutput.ClarificationRequired || llmOutput.Pairs.Count == 0)
        {
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
            var metricTerm = SelectResolvableMetricTerm(pair.MetricTerm, request.Message, language, request.AsOf);
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

            resolvedPairs.Add(new SymbolLookupParsedPair(pair.SymbolName, resolvedCode, pair.MetricTerm));
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

    private static string NormalizeBcp47(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "en" => "en-US",
            "fa" => "fa-IR",
            _ => language
        };
}
