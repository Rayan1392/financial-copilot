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

    /// <summary>
    /// Feature 162 change «max-plan-full-analysis-text»: the whole text for an entitled reader, without a repeated title.
    /// </summary>
    public static string FullText(string title, string? text) => WithoutRepeatedTitle(title, (text ?? string.Empty).Trim());

    /// <summary>
    /// <paramref name="text"/> when it fits in <paramref name="maxLength"/>; otherwise cut at the last sentence end (or
    /// word boundary) so that the result with <see cref="Ellipsis"/> fits.
    /// </summary>
    public static string FitTo(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var limit = Math.Max(0, maxLength - Ellipsis.Length);
        if (limit == 0)
        {
            return string.Empty;
        }

        var end = LastSentenceEnd(text, limit);
        if (end < limit * 2 / 5)
        {
            var space = text.LastIndexOf(' ', limit - 1, limit);
            end = space > 0 ? space : limit;
        }

        return text[..end].TrimEnd() + Ellipsis;
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
    /// <param name="fullText">
    /// Feature 162 change «max-plan-full-analysis-text»: the items carry the whole text (entitled reader), so the card
    /// shows it with its paragraphs, leaves out the read-more sentence and links to the analyses section.
    /// </param>
    public static string ComposeMarkdown(
        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items,
        IReadOnlyList<string> requestedSymbols,
        string analysesUrl,
        IReadOnlyList<string>? technicalAnalysisNotFoundSymbols = null,
        bool fullText = false) =>
        WithTechnicalNotFoundLine(technicalAnalysisNotFoundSymbols,
            Compose(items, requestedSymbols, title => $"### {title}", text => $"[{text}]({analysesUrl})", "\n\n",
                fullText, fullText ? AsMarkdownParagraphs : null, null), "\n\n");

    /// <summary>Plain text for Telegram: one part per line and the bare URL, which Telegram links.</summary>
    /// <param name="maxLength">
    /// Telegram's message limit. Only a whole-text card can pass it; its text is then cut (sentence end, else word
    /// boundary, then « …») so that the whole card fits, and the link line is kept.
    /// </param>
    public static string ComposePlainText(
        IReadOnlyList<ComprehensiveAnalysisSummaryItem> items,
        IReadOnlyList<string> requestedSymbols,
        string analysesUrl,
        IReadOnlyList<string>? technicalAnalysisNotFoundSymbols = null,
        bool fullText = false,
        int? maxLength = null)
    {
        string Card(int? textLimit) => WithTechnicalNotFoundLine(technicalAnalysisNotFoundSymbols,
            Compose(items, requestedSymbols, title => title, text => $"{text}: {analysesUrl}", "\n", fullText, null, textLimit),
            "\n");

        var card = Card(null);
        if (!fullText || maxLength is not { } limit || card.Length <= limit || items.Count == 0)
        {
            return card;
        }

        var text = items[0].PlainTextSummary.Trim();
        return Card(Math.Max(0, text.Length - (card.Length - limit)));
    }

    /// <summary>Markdown joins lines split by one line break; every line of the whole text becomes its own paragraph.</summary>
    private static string AsMarkdownParagraphs(string text) =>
        string.Join("\n\n", text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>BUG-020 (owner, 2026-11-11): no technical analysis → say so, then the latest analysis card.</summary>
    public static string TechnicalAnalysisNotFoundSentence(IReadOnlyList<string> symbols) =>
        $"برای نماد {string.Join("، ", symbols)} تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد.";

    private static string WithTechnicalNotFoundLine(IReadOnlyList<string>? symbols, string card, string separator)
    {
        var names = (symbols ?? []).Where(symbol => !string.IsNullOrWhiteSpace(symbol)).Select(symbol => symbol.Trim()).ToList();
        return names.Count == 0 ? card : TechnicalAnalysisNotFoundSentence(names) + separator + card;
    }

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
        string partSeparator,
        bool fullText = false,
        Func<string, string>? textLayout = null,
        int? textLimit = null)
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

        var text = latest.PlainTextSummary.Trim();
        if (textLimit is { } limit)
        {
            text = ComprehensiveAnalysisTeaserPolicy.FitTo(text, limit);
        }

        if (text.Length > 0)
        {
            parts.Add(textLayout?.Invoke(text) ?? text);
        }

        var builder = new StringBuilder(string.Join(partSeparator, parts));
        // Whole text (entitled reader): no read-more sentence, and the link goes to the analyses section.
        builder.Append("\n\n");
        if (!fullText)
        {
            builder.Append(ReadMoreFa).Append(partSeparator);
        }

        builder.Append(linkLine(fullText ? AllAnalysesLinkTextFa : LinkTextFa));
        return builder.ToString();
    }
}
