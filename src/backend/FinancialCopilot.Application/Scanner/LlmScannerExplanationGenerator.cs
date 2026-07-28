using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.Scanner;

public sealed class LlmScannerExplanationGenerator(
    IAiModelExecutionService executionService) : IScannerExplanationGenerator
{
    private const string SchemaName = "ScannerExplanationOutput";

    private static readonly AiStructuredOutputContract ExplanationContract = new(
        SchemaName,
        ["suggestedFollowUpQuestions"]);

    private const string SystemPrompt =
        "You are a financial assistant summarizing stock screening results.\n" +
        "Return a brief factual explanation (1-2 sentences) of what was found, " +
        "and 2-3 follow-up screening questions the user might find useful.\n" +
        "Rules:\n" +
        "- Do not make buy/sell recommendations.\n" +
        "- Do not invent metrics, definitions, or data not in the provided context.\n" +
        "- Explanation must describe results factually without altering or estimating numeric values.\n" +
        "- Respond in the same language as the user's original query.\n" +
        "- Keep follow-up questions within supported screening capabilities (metric filters only).\n" +
        "- The provided symbol list is the ONLY source of truth for symbol names. " +
        "Do NOT name any symbol not present in the provided list. " +
        "Do NOT draw on general market knowledge to add more symbols.\n" +
        "Schema: {\"explanationText\":\"...\",\"suggestedFollowUpQuestions\":[\"...\",\"...\",\"...\"]}";

    public async Task<ScannerExplanationOutput> GenerateAsync(
        ScannerExplanationRequest request,
        CancellationToken cancellationToken)
    {
        var userContent = BuildUserContent(request);

        var selection = new AiModelSelectionRequest(
            request.TenantId,
            AiWorkloadKind.ExplanationGeneration,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            request.CorrelationId);

        var aiRequest = new AiModelRequest(
            request.CorrelationId,
            request.TenantId,
            AiWorkloadKind.ExplanationGeneration,
            [
                new AiConversationMessage(AiMessageRole.System, SystemPrompt),
                new AiConversationMessage(AiMessageRole.User, userContent)
            ],
            StructuredOutput: ExplanationContract);

        var result = await executionService.ExecuteAsync(selection, aiRequest, cancellationToken);
        return ParseOutput(result.StructuredJson);
    }

    internal static string BuildUserContent(ScannerExplanationRequest request)
    {
        var filters = request.FilterChips
            .Select(chip => $"{chip.MetricDisplayName} {chip.OperatorLabel} {chip.ThresholdFormatted}");
        var filterList = string.Join(", ", filters);

        string symbolContext;
        if (request.MatchedSymbols.Count == 0)
        {
            symbolContext = $"Found {request.MatchedSymbolCount} symbol(s). No symbols on this page.";
        }
        else if (request.MatchedSymbolCount > request.MatchedSymbols.Count)
        {
            // Total exceeds this page — frame explicitly as a sample to prevent hallucination.
            var pageSymbols = string.Join(", ", request.MatchedSymbols);
            symbolContext =
                $"Found {request.MatchedSymbolCount} symbol(s) in total. " +
                $"This page contains {request.MatchedSymbols.Count} symbol(s): {pageSymbols}. " +
                $"Only describe the symbols listed above — do not name any others.";
        }
        else
        {
            var pageSymbols = string.Join(", ", request.MatchedSymbols);
            symbolContext = $"Found {request.MatchedSymbolCount} symbol(s): {pageSymbols}.";
        }

        return $"Query: \"{request.OriginalQuery}\"\n" +
               $"Filters: {filterList}\n" +
               symbolContext;
    }

    private static ScannerExplanationOutput ParseOutput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ScannerExplanationOutput(null, []);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var text = root.TryGetProperty("explanationText", out var textProp)
                       && textProp.ValueKind == JsonValueKind.String
                ? textProp.GetString()
                : null;

            var questions = root.TryGetProperty("suggestedFollowUpQuestions", out var qProp)
                            && qProp.ValueKind == JsonValueKind.Array
                ? qProp.EnumerateArray()
                    .Where(q => q.ValueKind == JsonValueKind.String)
                    .Select(q => q.GetString()!)
                    .Where(q => !string.IsNullOrWhiteSpace(q))
                    .ToList()
                : (IReadOnlyCollection<string>)[];

            return new ScannerExplanationOutput(text, questions);
        }
        catch
        {
            return new ScannerExplanationOutput(null, []);
        }
    }
}
