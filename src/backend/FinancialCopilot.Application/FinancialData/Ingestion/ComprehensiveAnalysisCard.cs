using System.Text;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Feature 162: how a comprehensive-analysis answer links to TahlilApp. TahlilApp has one fixed page for its
/// comprehensive analyses (owner answer to OQ-063, 2026-11-10); there is no per-article link.
/// </summary>
public sealed class ComprehensiveAnalysisPresentationOptions
{
    public const string SectionName = "ComprehensiveAnalysis";
    public const string DefaultTahlilAppAnalysesUrl = "https://tahlilapp.com/app/analysis";

    public string TahlilAppAnalysesUrl { get; set; } = DefaultTahlilAppAnalysesUrl;

    public bool IsValid(out string? error)
    {
        error = Uri.TryCreate(TahlilAppAnalysesUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? null
            : "ComprehensiveAnalysis:TahlilAppAnalysesUrl must be an absolute https URL.";
        return error is null;
    }
}

/// <summary>
/// Feature 162 answer card (owner answers to OQ-064/065, 2026-11-10): for each analysis only its title and its whole
/// published summary, verbatim, then one fixed TahlilApp link at the end. No LLM writes this text.
/// </summary>
public static class ComprehensiveAnalysisCardComposer
{
    public const string LinkTextFa = "ادامهٔ تحلیل‌ها در تحلیل‌اپ";

    /// <summary>Markdown for the web chat: <c>### title</c>, summary, and a markdown link line.</summary>
    public static string ComposeMarkdown(
        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items,
        IReadOnlyList<string> requestedSymbols,
        string analysesUrl) =>
        Compose(items, requestedSymbols, title => $"### {title}", $"[{LinkTextFa}]({analysesUrl})");

    /// <summary>Plain text for Telegram: title line, summary, and the bare URL (Telegram links it).</summary>
    public static string ComposePlainText(
        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items,
        IReadOnlyList<string> requestedSymbols,
        string analysesUrl) =>
        Compose(items, requestedSymbols, title => title, $"{LinkTextFa}: {analysesUrl}");

    public static string NoResultSentence(IReadOnlyList<string> requestedSymbols)
    {
        var symbols = requestedSymbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol)).Select(symbol => symbol.Trim()).ToList();
        return symbols.Count == 0
            ? "تحلیلی در تحلیل‌اپ پیدا نشد."
            : $"برای نماد {string.Join("، ", symbols)} تحلیلی در تحلیل‌اپ پیدا نشد.";
    }

    private static string Compose(
        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items,
        IReadOnlyList<string> requestedSymbols,
        Func<string, string> titleLine,
        string linkLine)
    {
        var builder = new StringBuilder();
        if (items.Count == 0)
        {
            builder.Append(NoResultSentence(requestedSymbols)).Append("\n\n");
        }

        foreach (var item in items)
        {
            builder.Append(titleLine(item.Title.Trim())).Append('\n');
            var summary = item.PlainTextSummary.Trim();
            if (summary.Length > 0)
            {
                builder.Append(summary).Append('\n');
            }

            builder.Append('\n');
        }

        builder.Append(linkLine);
        return builder.ToString();
    }
}
