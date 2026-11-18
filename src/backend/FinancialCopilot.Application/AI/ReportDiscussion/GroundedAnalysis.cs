using System.Text.Json;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// Change «analytical-answer-and-report-score» (owner, 2026-11-18): the analysis style, amount-unit rule and report
/// score shared by the Report Discussion (162) and symbol-grounded fallback (163) prompts. The model decides whether a
/// question is an overall analysis and returns only the score number; the server writes the score line (D3, OQ-105).
/// </summary>
public static class GroundedAnalysis
{
    public const int MaximumScore = 100;

    /// <summary>Amounts arrive already converted by <see cref="ReportDisplayUnits"/>; the model only copies them.</summary>
    public const string UnitRules =
        "Amounts in <report> are already given in همت or میلیارد تومان. Write each amount exactly as given, with that " +
        "unit. Never write an amount in ریال or میلیون ریال, and never convert, add up or round amounts. Per-share values " +
        "keep their own unit.\n";

    public const string StyleRules =
        "Analysis style:\n" +
        "- Interpret, do not recite. Explain what the figures mean: the drivers of revenue and profit, the direction of " +
        "margins, the quality of earnings (operating versus non-operating and one-off items), cost pressure, " +
        "sustainability and the risks to watch.\n" +
        "- Do not walk through the report line by line and do not list figures one after another. Use a figure only " +
        "where it supports a point, at most one or two per list item.\n" +
        "- When the question asks for an overall analysis, interpretation or evaluation of the report or of the " +
        "company's performance, analyse the newest period (the first one in \"periods (newest first)\", which is the " +
        "last column of the table) and use earlier periods only as context. Name that period once in the first line, " +
        "with its label exactly as given, and give the important points as the list items.\n";

    /// <summary>Report Discussion (162): the report was shown to the user.</summary>
    public const string ShownReportRule =
        "The user can see this report as a table, so the numbers are already in front of them.\n";

    /// <summary>Symbol-grounded fallback (163): the statement was not shown (OQ-106).</summary>
    public const string HiddenReportRule =
        "The user has not seen this statement: cite the two or three key figures (for example revenue, net profit and " +
        "a margin) that support the conclusion.\n";

    /// <summary>Asked only for a quarterly income statement (OQ-104).</summary>
    public const string ScoreRules =
        "reportScore: only when the question asks for an overall analysis, interpretation or evaluation of the report or " +
        "of the company's performance, score the newest period's report from 0 to 100 with this rubric: revenue growth " +
        "and its quality up to 25, profitability and the direction of margins up to 30, quality of earnings (operating " +
        "versus non-operating and one-off items) up to 25, consistency with earlier periods up to 20. Return the total as " +
        "an integer. Do not write the score in the answer text; it is added below the answer. Use null for a narrow " +
        "question about specific items and for any question about price, valuation, or buying or selling the share.\n";

    public const string NoScoreRule = "reportScore: always null for this report.\n";

    public static string ScoreRulesFor(ReportFamily family) =>
        family == ReportFamily.QuarterlyIncomeStatement ? ScoreRules : NoScoreRule;

    /// <summary>The JSON schema property of the score, shared by both structured-output contracts.</summary>
    public const string ScoreSchemaProperty =
        "\"reportScore\": { \"type\": [\"integer\", \"null\"], \"minimum\": 0, \"maximum\": 100 }";

    /// <summary>An integer from 0 to 100, or null for anything else (an invalid score never fails the answer).</summary>
    public static int? ParseScore(JsonElement root) =>
        root.TryGetProperty("reportScore", out var property) && property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var score) && score is >= 0 and <= MaximumScore
            ? score
            : null;

    /// <summary>The score shown only for a quarterly income statement (OQ-104).</summary>
    public static int? ShownScore(ReportFamily family, int? score) =>
        family == ReportFamily.QuarterlyIncomeStatement ? score : null;

    /// <summary>The server-written score paragraph and disclaimer (OQ-105).</summary>
    public static string ScoreBlock(int score, string period) =>
        $"**امتیاز گزارش {PersianDigits(period)}: {PersianDigits(score.ToString(System.Globalization.CultureInfo.InvariantCulture))} از ۱۰۰**\n\n" +
        "این امتیاز ارزیابی عملکرد مالی همین گزارش است و توصیهٔ خرید یا فروش نیست.";

    /// <summary>Numbers the grounding check accepts because the server wrote them: the score and 100.</summary>
    public static IReadOnlyList<decimal> ScoreNumbers(int? score) =>
        score is { } value ? [value, MaximumScore] : [];

    private static string PersianDigits(string text) =>
        string.Concat(text.Select(character => character is >= '0' and <= '9' ? "۰۱۲۳۴۵۶۷۸۹"[character - '0'] : character));
}
