using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.AI.Orchestration;

public sealed class LlmAiIntentDetector(IAiModelExecutionService executionService) : IAiIntentDetector
{
    private const string IntentSchemaName = "IntentDetectionOutput";
    private static readonly AiStructuredOutputContract IntentContract = new(
        IntentSchemaName,
        ["intent", "confidence"]);

    private const string SystemPrompt =
        "You are an AI intent classifier for a financial platform. " +
        "Classify the user message into one of these intents:\n" +
        "- Scanner: the user wants to screen or filter stocks by financial metrics with a condition or threshold " +
        "(e.g. 'find companies where P/E < 10', 'سهام با رشد بالا').\n" +
        "- SymbolLookup: the user names one or more specific symbols or companies AND asks for the value of a " +
        "metric — with no threshold or filter (e.g. 'PE حفاری چقدر است؟', 'نسبت بدهی فملی را نشان بده', " +
        "'what is the ROE of AAPL?').\n" +
        "- Unknown: the intent is not related to stock screening or metric lookup.\n" +
        "- Clarification: the message is too vague to classify.\n" +
        "Key distinction: Scanner requires an operator+threshold (filter many); " +
        "SymbolLookup asks for a value for named symbol(s) with no threshold.\n" +
        "Respond ONLY with JSON matching this schema: " +
        "{\"intent\":\"Scanner|SymbolLookup|Unknown|Clarification\",\"confidence\":0.0}";

    public async Task<IntentDetectionResult> DetectAsync(
        IntentDetectionInput input,
        CancellationToken cancellationToken)
    {
        if (LooksLikePePointLookup(input.UserQuery))
        {
            return new IntentDetectionResult(
                DetectedIntent.SymbolLookup,
                0.98,
                "Deterministic PE/P/E point lookup rule.");
        }

        var selection = new AiModelSelectionRequest(
            input.TenantId,
            AiWorkloadKind.ScannerParsing,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            input.CorrelationId);

        var request = new AiModelRequest(
            input.CorrelationId,
            input.TenantId,
            AiWorkloadKind.ScannerParsing,
            [
                new AiConversationMessage(AiMessageRole.System, SystemPrompt),
                new AiConversationMessage(AiMessageRole.User, input.UserQuery)
            ],
            StructuredOutput: IntentContract);

        var result = await executionService.ExecuteAsync(selection, request, cancellationToken);
        return ParseIntentOutput(result.StructuredJson);
    }

    private static bool LooksLikePePointLookup(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;

        var normalized = NormalizeLookupText(query);
        if (!ContainsPeTerm(normalized)) return false;
        if (ContainsComparisonOrThreshold(normalized)) return false;

        return true;
    }

    private static bool ContainsPeTerm(string normalized) =>
        normalized.Contains("pe", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("p/e", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("price to earnings", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("price-to-earnings", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("پی به ای", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("پی ای", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("پی‌ای", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("نسبت قیمت به سود", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("قیمت به سود", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsComparisonOrThreshold(string normalized) =>
        normalized.Contains('<') ||
        normalized.Contains('>') ||
        normalized.Contains(" less ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains(" below ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains(" greater ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains(" above ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains(" کمتر ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains(" زیر ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains(" بالای ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains(" بیشتر ", StringComparison.OrdinalIgnoreCase) ||
        normalized.Any(char.IsDigit);

    private static string NormalizeLookupText(string text) =>
        text.Trim()
            .Replace('ك', 'ک')
            .Replace('ي', 'ی')
            .Replace('‌', ' ');

    private static IntentDetectionResult ParseIntentOutput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new IntentDetectionResult(DetectedIntent.Unknown, 0.0, "LLM returned empty intent output.");
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var intentStr = root.TryGetProperty("intent", out var intentProp)
                ? intentProp.GetString() ?? string.Empty
                : string.Empty;

            var confidence = root.TryGetProperty("confidence", out var confProp) && confProp.TryGetDouble(out var confVal)
                ? confVal
                : 0.0;

            var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                ? reasonProp.GetString()
                : null;

            var intent = intentStr.Trim() switch
            {
                "Scanner" => DetectedIntent.Scanner,
                "SymbolLookup" => DetectedIntent.SymbolLookup,
                "Clarification" => DetectedIntent.Clarification,
                _ => DetectedIntent.Unknown
            };

            return new IntentDetectionResult(intent, confidence, reasoning);
        }
        catch (JsonException)
        {
            return new IntentDetectionResult(DetectedIntent.Unknown, 0.0, "Intent detection output was not valid JSON.");
        }
    }
}
