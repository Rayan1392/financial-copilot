using System.Text;
using System.Text.RegularExpressions;
using AnalysisDepth = FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisDepth;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// Feature 163 change «answer-readability» (owner, 2026-11-17; also Report Discussion by OQ-101): the one owner of the
/// layout of a grounded report answer. <see cref="PromptRules"/> asks the model for the layout; <see cref="Normalize"/>
/// is the deterministic safety net applied after grounding. It never changes a value: it only adds thousands separators
/// (which <see cref="NumericGroundingValidator"/> accepts) and keeps the answer inside a small Markdown subset that the
/// web and Telegram renderers support (paragraphs, <c>**bold**</c>, <c>- </c> lists).
/// </summary>
public static partial class GroundedAnswerLayout
{
    /// <summary>The layout section shared by the Feature 163 and Report Discussion system prompts.</summary>
    public const string PromptRules =
        "Answer layout (Markdown subset, written for quick reading in a chat):\n" +
        "- Line 1: one sentence with the main takeaway; put the takeaway itself in **bold**. When every value compares " +
        "the same periods, name those periods once in this line instead of repeating them in every sentence.\n" +
        "- Then a blank line and a short list of 2 to 5 items, one topic per item, each written as " +
        "\"- **label:** text\" with at most two short sentences. For a narrow question about one item, use 1 or 2 items.\n" +
        "- Bold only the item labels and the one or two key figures or changes of each item; never bold a whole sentence.\n" +
        "- Write every number of five or more digits with thousands separators. Every value keeps its unit.\n" +
        "- Do not use headings (#), tables, links, emojis or code.\n" +
        "- A required sentence that this is not a valuation or an investment recommendation goes alone on the last line, " +
        "not bold.\n" +
        "- Keep the answer within about 180 words.\n";

    /// <summary>
    /// Change «sol-deep-analysis-web-search» (§10, §12): the layout of an analysis answer per depth. Length is steered here,
    /// never by cutting text; <see cref="Normalize"/> is unchanged.
    /// </summary>
    public static string AnalysisPromptRules(AnalysisDepth depth) =>
        "Answer layout (Markdown subset, written for reading in a chat):\n" +
        "- Line 1: one sentence with the main takeaway; put the takeaway itself in **bold**. When every value compares " +
        "the same periods, name those periods once in this line instead of repeating them in every sentence.\n" +
        (depth == AnalysisDepth.Simple
            ? "- Then a blank line and a list of 3 to 6 items, one topic per item, each written as \"- **label:** text\" " +
              "with at most three short sentences. For a narrow question about one item, use 1 to 3 items.\n" +
              "- Keep the answer within about 300 to 500 words; shorter is better when the question is narrow.\n"
            : "- Then a blank line and a list of 5 to 8 items, one topic per item, each written as \"- **label:** text\" " +
              "with at most five sentences. Include the main risks and qualitative scenarios, and any unconfirmed " +
              "hypothesis worded as a possibility. For a narrow question about one item, use 2 to 4 items.\n" +
              "- Keep an overall analysis within about 600 to 1,000 words; shorter is better when the question is " +
              "narrow.\n") +
        "- Bold only the item labels and the one or two key figures or changes of each item; never bold a whole sentence.\n" +
        "- Write every number of five or more digits with thousands separators. Every value keeps its unit.\n" +
        "- Do not use headings (#), tables, links, emojis or code.\n" +
        "- A required sentence that this is not a valuation or an investment recommendation goes alone on the last line, " +
        "not bold.\n";

    /// <summary>
    /// Applies the layout safety net. Idempotent; never adds, removes or changes a number's value in the answer text.
    /// A web citation the model wrote as a link (BUG-036) is removed whole, digits of its domain included: the cited
    /// source is shown through its <c>[Wn]</c> marker and the server-written source list instead.
    /// </summary>
    public static string Normalize(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return string.Empty;

        var lines = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var output = new List<string>(lines.Length + 4);
        var previousWasListItem = false;
        foreach (var raw in lines)
        {
            if (TableSeparatorRow().IsMatch(raw)) continue;

            var line = raw.TrimEnd();
            line = HeadingMarker().Replace(line, string.Empty);
            line = Image().Replace(line, "$1");
            line = RemoveWebCitations(WebCitation(), line);
            line = RemoveWebCitations(WebLinkBeforeMarker(), line);
            line = Link().Replace(line, "$1");
            line = TableRow().IsMatch(line) ? "- " + string.Join(" · ", SplitTableRow(line)) : line;
            line = BulletMarker().Replace(line, "- ");
            line = BoldInnerSpace().Replace(line, "**$1**");
            line = SpaceAfterClosingBold(line);
            line = GroupThousands(line);

            var isListItem = ListItem().IsMatch(line);
            // A paragraph line right after a list item would be read as that item's continuation (CommonMark lazy line).
            if (previousWasListItem && !isListItem && line.Length > 0 && !char.IsWhiteSpace(line[0]))
                output.Add(string.Empty);
            output.Add(line);
            previousWasListItem = isListItem;
        }

        return ExtraBlankLines().Replace(string.Join('\n', output), "\n\n").Trim();
    }

    /// <summary>Thousands separators for digit runs of five or more integer digits that have none.</summary>
    internal static string GroupThousands(string text) =>
        UngroupedNumber().Replace(text, match =>
        {
            var digits = match.Value;
            var separator = digits[0] is >= '0' and <= '9' ? ',' : '٬';
            var builder = new StringBuilder(digits.Length + digits.Length / 3);
            var head = digits.Length % 3;
            if (head > 0) builder.Append(digits, 0, head);
            for (var index = head; index < digits.Length; index += 3)
            {
                if (builder.Length > 0) builder.Append(separator);
                builder.Append(digits, index, 3);
            }
            return builder.ToString();
        });

    /// <summary>
    /// «**فروش:**متن»: in CommonMark a closing «**» after punctuation closes the bold only when whitespace or punctuation
    /// follows, so a space is added there. Only closing markers (the 2nd, 4th, … on the line) are touched.
    /// </summary>
    internal static string SpaceAfterClosingBold(string line)
    {
        var builder = new StringBuilder(line.Length + 4);
        var markers = 0;
        for (var index = 0; index < line.Length; index++)
        {
            if (index + 1 < line.Length && line[index] == '*' && line[index + 1] == '*')
            {
                builder.Append("**");
                var closing = markers++ % 2 == 1;
                var next = index + 2 < line.Length ? line[index + 2] : ' ';
                if (closing && index > 0 && char.IsPunctuation(line[index - 1]) && char.IsLetterOrDigit(next))
                    builder.Append(' ');
                index++;
                continue;
            }
            builder.Append(line[index]);
        }
        return builder.ToString();
    }

    private static IEnumerable<string> SplitTableRow(string line) =>
        line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).Where(cell => cell.Length > 0);

    // Not preceded by a digit, a decimal mark or a separator (so a fraction or an already grouped number is untouched);
    // one script of digits per run.
    [GeneratedRegex(@"(?<![0-9۰-۹٠-٩.,٫٬])(?:[0-9]{5,}|[۰-۹]{5,}|[٠-٩]{5,})(?![0-9۰-۹٠-٩٬,])", RegexOptions.CultureInvariant)]
    private static partial Regex UngroupedNumber();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingMarker();

    /// <summary>
    /// Removes each citation with the spaces before it, but keeps one space when the citation sat between two words
    /// or before a marker («است ([a](u))[W1]» → «است [W1]»).
    /// </summary>
    private static string RemoveWebCitations(Regex citation, string line) =>
        citation.Replace(line, match =>
        {
            var next = match.Index + match.Length;
            var spaced = match.Value.Length > 0 && match.Value[0] is ' ' or '	';
            return spaced && next < line.Length && !char.IsWhiteSpace(line[next]) && !".,،;؛:!?؟)".Contains(line[next])
                ? " "
                : string.Empty;
        });

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    /// <summary>
    /// OpenAI's inline web citation: one or more http(s) links inside parentheses, «([irrena.ir](https://…))» or
    /// «([a](u1), [b](u2))» (BUG-036). A plain link in running text is not a citation and keeps its text.
    /// </summary>
    [GeneratedRegex(@"[ \t]*\(\s*\[(?!W[0-9۰-۹٠-٩]{1,2}\])[^\]\n]*\]\(https?://[^)\s]+\)(?:\s*[,،;؛]\s*\[(?!W[0-9۰-۹٠-٩]{1,2}\])[^\]\n]*\]\(https?://[^)\s]+\))*\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex WebCitation();

    /// <summary>An http(s) link written right before a <c>[Wn]</c> marker: the marker already names that source (BUG-036).</summary>
    [GeneratedRegex(@"[ \t]*\[(?!W[0-9۰-۹٠-٩]{1,2}\])[^\]\n]*\]\(https?://[^)\s]+\)(?=\s*\[W[0-9۰-۹٠-٩]{1,2}\])", RegexOptions.CultureInvariant)]
    private static partial Regex WebLinkBeforeMarker();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableSeparatorRow();

    [GeneratedRegex(@"^\s*\|.*\|\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableRow();

    /// <summary>«* », «• », «+ » or «– » list markers (a single star followed by a space, so «**bold**» is untouched).</summary>
    [GeneratedRegex(@"^\s*[*•+–]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex BulletMarker();

    [GeneratedRegex(@"^\s*(?:-\s|\d+[.)]\s)", RegexOptions.CultureInvariant)]
    private static partial Regex ListItem();

    /// <summary>«** متن **» does not render as bold in CommonMark; the spaces inside the markers are removed.</summary>
    [GeneratedRegex(@"\*\*[ \t]*([^*\n]*?[^*\s])[ \t]*\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex BoldInnerSpace();


    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExtraBlankLines();
}
