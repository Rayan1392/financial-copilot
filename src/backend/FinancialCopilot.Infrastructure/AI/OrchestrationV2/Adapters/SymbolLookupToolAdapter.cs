using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

// Narrow adapter wrapping ISymbolLookupParser + ISymbolMetricLookupService.
// Only the raw user query and security context reach this adapter from the LLM closure;
// symbol resolution, metric code mapping, and data retrieval are fully deterministic.
internal sealed class SymbolLookupToolAdapter(
    ISymbolLookupParser parser,
    ISymbolMetricLookupService lookupService,
    TimeProvider timeProvider)
{
    private static readonly string[] ConversationHistoryMarkers =
    [
        "[Recent conversation]",
        "[Stored context]",
        "User:",
        "User ",
        "Assistant:",
        "Assistant "
    ];

    internal async Task<SymbolLookupToolResult> LookupAsync(
        string userQuery,
        string correlationId,
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken,
        string? queryTextForLookup = null,
        string? parserContextMessage = null)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return SymbolLookupToolResult.Clarification(
                "Please specify the symbol name and the metric you want to look up.");

        var latestUserMessage = ExtractLatestUserMessage(userQuery);
        var parserInput = BuildParserInput(latestUserMessage, parserContextMessage ?? queryTextForLookup);
        if (string.IsNullOrWhiteSpace(parserInput))
        {
            return SymbolLookupToolResult.Clarification(
                ContainsPersianText(userQuery)
                    ? "لطفاً نام نماد یا شرکت و معیار مالی موردنظر را فقط در پیام آخر ارسال کنید."
                    : "Please provide only the latest symbol/company name and metric in your last message.");
        }

        var now = timeProvider.GetUtcNow();
        var parseRequest = new SymbolLookupParseRequest(
            parserInput,
            "fa",
            correlationId,
            tenantId,
            DateOnly.FromDateTime(now.DateTime));

        var parseResult = SanitizeParseResult(
            await parser.ParseAsync(parseRequest, cancellationToken),
            latestUserMessage);

        if (parseResult.Status == LookupParseStatus.ClarificationRequired)
        {
            var msg = parseResult.ClarificationMessage ??
                (ContainsPersianText(latestUserMessage)
                    ? "لطفاً نام نماد و معیار مالی موردنظر را مشخص کنید."
                    : "Please specify the symbol name and the metric you want to look up.");
            return SymbolLookupToolResult.Clarification(msg);
        }

        var lookupPairs = parseResult.Pairs
            .Where(p => p.ResolvedMetricCode is not null)
            .Select(p => (p.RawSymbolName, p.ResolvedMetricCode!))
            .ToList();

        var lookupRequest = new SymbolLookupRequest(
            lookupPairs,
            DateOnly.FromDateTime(now.DateTime),
            ActorId: actorId.ToString(),
            QueryText: queryTextForLookup ?? userQuery);

        var table = await lookupService.LookupAsync(lookupRequest, cancellationToken);

        return SymbolLookupToolResult.Success(table);
    }

    private static SymbolLookupParseResult SanitizeParseResult(
        SymbolLookupParseResult parseResult,
        string latestUserMessage)
    {
        var sanitizedPairs = new List<SymbolLookupParsedPair>();
        foreach (var pair in parseResult.Pairs)
        {
            var sanitizedEntity = SanitizeEntity(pair.RawSymbolName);
            if (sanitizedEntity is null && LooksLikeEntityOnlyReply(latestUserMessage))
            {
                sanitizedEntity = CollapseWhitespace(latestUserMessage);
            }

            if (sanitizedEntity is null)
            {
                return new SymbolLookupParseResult(
                    [],
                    LookupParseStatus.ClarificationRequired,
                    ContainsPersianText(latestUserMessage)
                        ? "نام نماد یا شرکت از پیام‌های قبلی استخراج شد و معتبر نیست. لطفاً فقط نام نماد یا شرکت را در پیام آخر ارسال کنید."
                        : "The extracted symbol/company name was contaminated by prior conversation context. Please resend only the latest symbol or company name.");
            }

            sanitizedPairs.Add(pair with { RawSymbolName = sanitizedEntity });
        }

        return parseResult with { Pairs = sanitizedPairs };
    }

    private static string BuildParserInput(string latestUserMessage, string? queryTextForLookup)
    {
        var cleanLatest = CollapseWhitespace(latestUserMessage);
        if (cleanLatest.Length == 0)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(queryTextForLookup))
            return cleanLatest;

        var userTurns = ExtractUserTurns(queryTextForLookup);
        var priorUserTurn = userTurns
            .Reverse<string>()
            .FirstOrDefault(turn => !string.Equals(turn, cleanLatest, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(priorUserTurn))
            return cleanLatest;

        return LooksLikeEntityOnlyReply(cleanLatest) || !HasEntityCandidate(cleanLatest)
            ? CollapseWhitespace($"{priorUserTurn} {cleanLatest}")
            : cleanLatest;
    }

    private static bool HasEntityCandidate(string text)
    {
        var candidate = text;
        foreach (var phrase in new[]
                 {
                     "نسبت قیمت به سود", "نسبت پی به ای", "پی به ای", "P/E", "PE",
                     "فروش YTD تا ماه قبل", "فروش YTD تا ماه گذشته", "فروش YTD",
                     "متوسط فروش 12 ماهه", "متوسط فروش ۱۲ ماهه",
                     "میانگین فروش 12 ماهه", "میانگین فروش ۱۲ ماهه",
                     "آخرین فروش ماهانه", "فروش ماهانه", "فروش ماهیانه",
                     "فروش آخرین ماه", "فروش این ماه", "آخرین فروش", "فروش"
                 })
        {
            candidate = candidate.Replace(phrase, " ", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var noise in new[]
                 {
                     "چقدر است", "چقدر هست", "چقدر بوده", "چقدر", "است", "هست", "بوده",
                     "برابر است", "برابر", "نماد", "سهم", "شرکت", "برای", "را", "?", "؟", ":"
                 })
        {
            candidate = candidate.Replace(noise, " ", StringComparison.OrdinalIgnoreCase);
        }

        return CollapseWhitespace(candidate).Length > 0;
    }

    private static string ExtractLatestUserMessage(string message)
    {
        var turns = ExtractUserTurns(message);
        return turns.Count > 0
            ? turns[^1]
            : CollapseWhitespace(message);
    }

    private static List<string> ExtractUserTurns(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var turns = new List<string>();
        var inStoredContext = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (string.Equals(line, "[Stored context]", StringComparison.OrdinalIgnoreCase))
            {
                inStoredContext = true;
                continue;
            }

            if (string.Equals(line, "[Recent conversation]", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                inStoredContext = false;
                continue;
            }

            if (inStoredContext || line.StartsWith("- ", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("Assistant:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Assistant ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("User:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("User ", StringComparison.OrdinalIgnoreCase))
            {
                var userText = StripSpeakerPrefix(line, "User");
                if (userText.Length > 0)
                    AddIfDistinct(turns, userText);
                continue;
            }

            if (!line.StartsWith("[", StringComparison.Ordinal))
                AddIfDistinct(turns, line);
        }

        return turns;
    }

    private static string StripSpeakerPrefix(string line, string speaker)
    {
        var content = line[speaker.Length..].TrimStart();
        content = content.TrimStart(':', '-', ' ');
        return CollapseWhitespace(content);
    }

    private static void AddIfDistinct(List<string> turns, string value)
    {
        var collapsed = CollapseWhitespace(value);
        if (collapsed.Length == 0)
            return;

        if (turns.Count == 0 || !string.Equals(turns[^1], collapsed, StringComparison.OrdinalIgnoreCase))
            turns.Add(collapsed);
    }

    private static string? SanitizeEntity(string rawEntity)
    {
        var candidate = CollapseWhitespace(ContainsConversationHistoryMarkers(rawEntity)
            ? ExtractLatestUserMessage(rawEntity)
            : rawEntity);

        if (candidate.Length == 0)
            return null;

        if (ContainsConversationHistoryMarkers(candidate) ||
            candidate.Contains('|', StringComparison.Ordinal) ||
            candidate.Contains("```", StringComparison.Ordinal) ||
            candidate.Length > 120)
        {
            return null;
        }

        return candidate;
    }

    private static bool ContainsConversationHistoryMarkers(string text) =>
        ConversationHistoryMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeEntityOnlyReply(string text)
    {
        var trimmed = CollapseWhitespace(text);
        if (trimmed.Length is < 2 or > 64)
            return false;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 6 &&
               !trimmed.Contains("P/E", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Contains("PE", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Contains("فروش", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Contains("پی به ای", StringComparison.OrdinalIgnoreCase) &&
               !trimmed.Contains("نسبت قیمت به سود", StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsPersianText(string text) =>
        text.Any(c => c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ');
}

internal sealed record SymbolLookupToolResult
{
    public bool Succeeded { get; init; }
    public bool ClarificationRequired { get; init; }
    public string? ClarificationMessage { get; init; }
    public SymbolLookupTableResult? Table { get; init; }
    public string CompletionStatus { get; init; } = "Completed";

    public string AgentSummary => ClarificationRequired
        ? $"Clarification needed: {ClarificationMessage}"
        : $"Found metric data for {Table?.ExecutionFacts.MatchingSymbolCount ?? 0} symbol(s)." +
          $"{(Table?.UnresolvedSymbols.Count > 0 ? $" {Table.UnresolvedSymbols.Count} unresolved." : string.Empty)}";

    public static SymbolLookupToolResult Success(SymbolLookupTableResult table) =>
        new() { Succeeded = true, Table = table };

    public static SymbolLookupToolResult Clarification(string message) =>
        new()
        {
            Succeeded = false,
            ClarificationRequired = true,
            ClarificationMessage = message,
            CompletionStatus = "ClarificationRequired"
        };
}
