using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

public sealed class LlmSymbolLookupParser(
    IAiModelExecutionService executionService,
    IMetricAliasResolver aliasResolver) : ISymbolLookupParser
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

        return BuildParseResult(request, llmOutput);
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
        LlmLookupParseOutput llmOutput)
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
            var resolution = aliasResolver.ResolveAlias(
                pair.MetricTerm,
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
            }
            else if (resolution.Status == MetricResolutionStatus.Ambiguous)
            {
                clarificationMessages.Add(
                    $"Metric term '{pair.MetricTerm}' is ambiguous: " +
                    string.Join(", ", resolution.Candidates.Select(c => c.Code.Value)));
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

    private static string NormalizeBcp47(string language) =>
        language.Trim().ToLowerInvariant() switch
        {
            "en" => "en-US",
            "fa" => "fa-IR",
            _ => language
        };
}
