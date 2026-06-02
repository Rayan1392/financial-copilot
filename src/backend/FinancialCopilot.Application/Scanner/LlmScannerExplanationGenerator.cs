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

    private static string BuildUserContent(ScannerExplanationRequest request)
    {
        var filters = request.FilterChips
            .Select(chip => $"{chip.MetricDisplayName} {chip.OperatorLabel} {chip.ThresholdFormatted}");
        var filterList = string.Join(", ", filters);
        var symbolList = request.MatchedSymbols.Count > 0
            ? string.Join(", ", request.MatchedSymbols.Take(5))
            : "no symbols";

        return $"Query: \"{request.OriginalQuery}\"\n" +
               $"Filters: {filterList}\n" +
               $"Found {request.MatchedSymbolCount} symbol(s): {symbolList}";
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
