using System.Text;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Feature 162: how a comprehensive-analysis answer links to and previews TahlilApp. TahlilApp has one fixed page for
/// its comprehensive analyses (owner answer to OQ-063, 2026-11-10); there is no per-article link. The analysis text is
/// paid TahlilApp content, so only a partial preview is shown (change «latest-analysis-teaser», 2026-11-10).
/// </summary>
public sealed class ComprehensiveAnalysisPresentationOptions
{
    public const string SectionName = "ComprehensiveAnalysis";
    public const string DefaultTahlilAppAnalysesUrl = "https://tahlilapp.com/app/analysis";

    public string TahlilAppAnalysesUrl { get; set; } = DefaultTahlilAppAnalysesUrl;

    /// <summary>Largest share of the analysis text shown as a preview, in percent.</summary>
    public int TeaserPercent { get; set; } = 30;

    /// <summary>Upper bound of the preview length in characters.</summary>
    public int TeaserMaxCharacters { get; set; } = 300;

    /// <summary>Texts shorter than this (after removing a repeated title) get no preview at all.</summary>
    public int TeaserMinimumSourceCharacters { get; set; } = 150;

    public bool IsValid(out string? error)
    {
        error = !(Uri.TryCreate(TahlilAppAnalysesUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            ? "ComprehensiveAnalysis:TahlilAppAnalysesUrl must be an absolute https URL."
            : TeaserPercent is < 1 or > 50
                ? "ComprehensiveAnalysis:TeaserPercent must be between 1 and 50."
                : TeaserMaxCharacters is < 20 or > 1_000
                    ? "ComprehensiveAnalysis:TeaserMaxCharacters must be between 20 and 1000."
                    : TeaserMinimumSourceCharacters is < 0 or > 2_000
                        ? "ComprehensiveAnalysis:TeaserMinimumSourceCharacters must be between 0 and 2000."
                        : null;
        return error is null;
    }
}

/// <summary>
/// The partial preview of a paid analysis: at most <see cref="ComprehensiveAnalysisPresentationOptions.TeaserPercent"/>
/// of the text and at most <see cref="ComprehensiveAnalysisPresentationOptions.TeaserMaxCharacters"/> characters, cut at a
/// sentence end (never inside a number such as 0.88) or a word boundary, ending with «…». The title is not repeated.
/// </summary>
public static class ComprehensiveAnalysisTeaserPolicy
{
    public const string Ellipsis = " …";
    private static readonly char[] SentenceEnds = ['.', '!', '?', '؟'];

    public static string Teaser(string title, string? text, ComprehensiveAnalysisPresentationOptions options)
    {
        var body = WithoutRepeatedTitle(title, (text ?? string.Empty).Trim());
        if (body.Length == 0 || body.Length < options.TeaserMinimumSourceCharacters)
        {
            return string.Empty;
        }

        var limit = Math.Min(options.TeaserMaxCharacters, body.Length * options.TeaserPercent / 100);
        if (limit <= 0)
        {
            return string.Empty;
        }

        var end = LastSentenceEnd(body, limit);
        if (end < limit * 2 / 5)
        {
            var space = body.LastIndexOf(' ', limit - 1, limit);
            end = space > 0 ? space : limit;
        }

        return body[..end].TrimEnd() + Ellipsis;
    }

    /// <summary>Exclusive end of the last sentence that finishes within the first <paramref name="limit"/> characters.</summary>
    private static int LastSentenceEnd(string body, int limit)
    {
        for (var index = limit - 1; index >= 0; index--)
        {
            if (body[index] == '\n')
            {
                return index;
            }

            // A full stop counts only when white space follows it, so «0.88» is never a sentence end.
            if (Array.IndexOf(SentenceEnds, body[index]) >= 0 && index + 1 < body.Length && char.IsWhiteSpace(body[index + 1]))
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static string WithoutRepeatedTitle(string title, string text)
    {
        var trimmedTitle = title.Trim();
        return trimmedTitle.Length > 0 && text.StartsWith(trimmedTitle, StringComparison.Ordinal)
            ? text[trimmedTitle.Length..].TrimStart(' ', '\n', '\r', '\t', '-', ':', '،', '؛')
            : text;
    }
}

/// <summary>
/// Feature 162 answer card for the latest analysis of a ticker: title, publish date, partial preview, a read-more line
/// and the fixed TahlilApp link. No LLM writes this text.
/// </summary>
public static class ComprehensiveAnalysisCardComposer
{
    public const string ReadMoreFa = "متن کامل این تحلیل را در بخش تحلیل جامع، تحلیل اپ بخوانید";
    public const string LinkTextFa = "ادامهٔ این تحلیل در تحلیل‌اپ";
    public const string AllAnalysesLinkTextFa = "تحلیل‌های جامع تحلیل‌اپ";
    public const string PublishedAtLabelFa = "تاریخ انتشار";

    /// <summary>
    /// Markdown for the web chat. Every part is its own paragraph: markdown joins lines separated by a single line
    /// break, which put the date, the preview, the read-more line and the link on one line.
    /// </summary>
    public static string ComposeMarkdown(
        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items,
        IReadOnlyList<string> requestedSymbols,
        string analysesUrl) =>
        Compose(items, requestedSymbols, title => $"### {title}", text => $"[{text}]({analysesUrl})", "\n\n");

    /// <summary>Plain text for Telegram: one part per line and the bare URL, which Telegram links.</summary>
    public static string ComposePlainText(
        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items,
        IReadOnlyList<string> requestedSymbols,
        string analysesUrl) =>
        Compose(items, requestedSymbols, title => title, text => $"{text}: {analysesUrl}", "\n");

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
        Func<string, string> linkLine,
        string partSeparator)
    {
        // The items arrive newest first; only the latest analysis is shown.
        var latest = items.Count > 0 ? items[0] : null;
        if (latest is null)
        {
            return NoResultSentence(requestedSymbols) + "\n\n" + linkLine(AllAnalysesLinkTextFa);
        }

        var parts = new List<string> { titleLine(latest.Title.Trim()) };
        if (!string.IsNullOrWhiteSpace(latest.PersianCreatedAt))
        {
            parts.Add($"{PublishedAtLabelFa}: {latest.PersianCreatedAt}");
        }

        var preview = latest.PlainTextSummary.Trim();
        if (preview.Length > 0)
        {
            parts.Add(preview);
        }

        var builder = new StringBuilder(string.Join(partSeparator, parts));
        builder.Append("\n\n").Append(ReadMoreFa).Append(partSeparator).Append(linkLine(LinkTextFa));
        return builder.ToString();
    }
}
