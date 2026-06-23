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
        "- ProductRevenueMix: the user asks which product contributes the most revenue, asks for product revenue mix, " +
        "product composition, dominant product, most important product, or similar product-level monthly sales questions. " +
        "These are not metric lookups and must NOT fall through to SymbolLookup.\n" +
        "- ComprehensiveAnalysis: the user is asking about a stock in a general or analytical way, OR asking about " +
        "analysis posts, reports, or market commentary. " +
        "Triggers include: تحلیل, بررسی, بررسی کن, وضعیت, ارزیابی, نظرت چیه, چطوره, گزارش, " +
        "رصد معاملات عمده, تحلیل تکنیکال, تحلیل بنیادی, قیمت تعادلی, " +
        "نمودار P/E, نمودار P/S, تحلیل جامع, 'comprehensive analysis', 'market report', " +
        "'analyze', 'review', 'what do you think about'. " +
        "When a specific symbol name is mentioned alongside any of these words, always use ComprehensiveAnalysis. " +
        "Does NOT trigger when the user asks for a specific metric value only (use SymbolLookup) " +
        "or asks for stocks matching a condition (use Scanner).\n" +
        "- Unknown: the intent is not related to stock screening, metric lookup, product revenue mix, or analysis posts.\n" +
        "- Clarification: the message is too vague to classify AND no stock symbol is mentioned.\n" +
        "Key distinction: Scanner requires an operator+threshold (filter many); " +
        "SymbolLookup asks for a specific metric value for named symbol(s) with no threshold; " +
        "ProductRevenueMix asks for the dominant product / product mix of a specific company; " +
        "ComprehensiveAnalysis = any general question about a named stock, or request for analysis/reports.\n" +
        "Respond ONLY with JSON matching this schema: " +
        "{\"intent\":\"Scanner|SymbolLookup|ProductRevenueMix|ComprehensiveAnalysis|Unknown|Clarification\",\"confidence\":0.0}";

    public async Task<IntentDetectionResult> DetectAsync(
        IntentDetectionInput input,
        CancellationToken cancellationToken)
    {
        if (ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery(input.UserQuery))
        {
            return new IntentDetectionResult(
                DetectedIntent.ProductRevenueMix,
                0.98,
                "Deterministic product revenue mix phrase rule.");
        }

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

    private static readonly string[] ProductRevenuePhrases =
    [
        "مهم‌ترین محصول", "مهم ترین محصول",
        "پرفروش‌ترین محصول", "پرفروش ترین محصول", "پرفروشترین محصول",
        "پرفروش‌ترین محصولات", "پرفروش ترین محصولات", "پرفروشترین محصولات",
        "محصول پرفروش", "محصولات پرفروش",
        "محصول اصلی", "محصولات اصلی",
        "بیشترین فروش محصول", "بیشترین فروش محصولات",
        "بیشترین درآمد از چه محصول", "بیشترین درآمد از چه محصولی",
        "بیشتر از چه محصول", "بیشتر از چه محصولی",
        "بیشتر از چه محصول درآمد دارد", "بیشتر از چه محصولی درآمد دارد",
        "ترکیب فروش محصول", "ترکیب فروش محصولات",
        "ترکیب درآمد محصول", "ترکیب درآمد محصولات",
        "سهم فروش محصول", "سهم فروش محصولات",
        "سهم درآمد محصول", "سهم درآمد محصولات",
        "کدام محصول بیشترین فروش", "کدام محصول بیشترین درآمد",
        "بالاترین فروش محصول", "بالاترین درآمد محصول",
        "revenue mix", "product revenue",
        "most important product", "top products",
        "product composition", "product concentration"
    ];

    // Pre-normalized so the Contains check compares normalized-to-normalized on both sides.
    private static readonly string[] NormalizedProductRevenuePhrases =
        ProductRevenuePhrases.Select(NormalizeLookupText).ToArray();

    private static bool LooksLikeProductRevenueMixQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var normalized = NormalizeLookupText(query);
        return NormalizedProductRevenuePhrases.Any(phrase =>
            normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase));
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
                "ProductRevenueMix" => DetectedIntent.ProductRevenueMix,
                "ComprehensiveAnalysis" => DetectedIntent.ComprehensiveAnalysis,
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
