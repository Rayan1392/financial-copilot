using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed class LlmComprehensiveAnalysisQueryParser(
    IAiModelExecutionService executionService,
    TimeProvider timeProvider) : IComprehensiveAnalysisQueryParser
{
    private static readonly HashSet<string> AllowedTopicSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "تحلیل_تکنیکال",
        "قیمت_تعادلی",
        "رصد_معاملات_عمده",
        "گزارش_فصلی",
        "گزارش_ماهانه",
        "نمودار_P_S",
        "نمودار_P_E",
    };

    private const string SchemaName = "ComprehensiveAnalysisParseOutput";
    private static readonly AiStructuredOutputContract ParseContract = new(
        SchemaName,
        ["symbolNames", "topicTags", "fromDateHint", "limit"]);

    private const string SystemPrompt =
        "You are a financial analysis query parser for the Iranian stock market.\n" +
        "Extract the following from the user message:\n" +
        "- symbolNames: list of Persian stock symbol names or codes mentioned (e.g. شغدیر, کرازی). Return [] if none.\n" +
        "- topicTags: list of analysis-type slug identifiers. Only use slugs from this allowed list:\n" +
        "  تحلیل_تکنیکال, قیمت_تعادلی, رصد_معاملات_عمده, گزارش_فصلی, گزارش_ماهانه, نمودار_P_S, نمودار_P_E\n" +
        "  Return [] if no matching topic tags are mentioned.\n" +
        "- fromDateHint: temporal expression as one of: 'yesterday', 'this_week', 'last_week', 'this_month', " +
        "  'last_month', ISO 8601 date string (e.g. '2025-01-15'), or null if no date mentioned.\n" +
        "- limit: number of results requested (1–5). Default 3 if not specified.\n\n" +
        "Respond ONLY with JSON matching this schema:\n" +
        "{\"symbolNames\":[],\"topicTags\":[],\"fromDateHint\":null,\"limit\":3}";

    public async Task<ComprehensiveAnalysisParseResult> ParseAsync(
        string userMessage,
        CancellationToken cancellationToken)
    {
        var tenantId = Guid.Empty;
        var correlationId = Guid.NewGuid().ToString("N");

        var selection = new AiModelSelectionRequest(
            tenantId,
            AiWorkloadKind.ScannerParsing,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            correlationId);

        var request = new AiModelRequest(
            correlationId,
            tenantId,
            AiWorkloadKind.ScannerParsing,
            [
                new AiConversationMessage(AiMessageRole.System, SystemPrompt),
                new AiConversationMessage(AiMessageRole.User, userMessage)
            ],
            StructuredOutput: ParseContract);

        var result = await executionService.ExecuteAsync(selection, request, cancellationToken);
        return ParseOutput(result.StructuredJson, userMessage);
    }

    private ComprehensiveAnalysisParseResult ParseOutput(string? json, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Clarification();

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var symbolNames = ReadStringArray(root, "symbolNames");
            var rawTopicTags = ReadStringArray(root, "topicTags");
            var topicTags = rawTopicTags.Where(t => AllowedTopicSlugs.Contains(t)).ToList();
            var fromDateHint = root.TryGetProperty("fromDateHint", out var dateProp)
                ? dateProp.GetString()
                : null;
            var limit = root.TryGetProperty("limit", out var limitProp) && limitProp.TryGetInt32(out var l)
                ? Math.Clamp(l, 1, 5)
                : 3;

            var fromDate = ResolveDate(fromDateHint);

            if (symbolNames.Count == 0 && topicTags.Count == 0 && fromDate is null)
                return Clarification();

            return new ComprehensiveAnalysisParseResult(
                ComprehensiveAnalysisParseStatus.Parsed,
                symbolNames,
                topicTags,
                fromDate,
                limit);
        }
        catch (JsonException)
        {
            return Clarification();
        }
    }

    private DateTimeOffset? ResolveDate(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return null;

        var now = timeProvider.GetUtcNow();

        return hint.ToLowerInvariant() switch
        {
            "yesterday" => now.AddDays(-1).Date == now.Date ? now.AddDays(-1) : now.AddDays(-1),
            "this_week" => now.AddDays(-(int)now.DayOfWeek),
            "last_week" => now.AddDays(-(int)now.DayOfWeek - 7),
            "this_month" => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset),
            "last_month" => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(-1),
            _ => DateTimeOffset.TryParse(hint, out var parsed) ? parsed : null
        };
    }

    private static List<string> ReadStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return [];

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static ComprehensiveAnalysisParseResult Clarification() =>
        new(
            ComprehensiveAnalysisParseStatus.ClarificationRequired,
            [],
            [],
            null,
            3,
            "لطفاً نماد سهم، نوع تحلیل (مثلاً تحلیل تکنیکال، رصد معاملات)، یا بازه زمانی مورد نظر را مشخص کنید.");
}
