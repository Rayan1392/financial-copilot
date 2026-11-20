using System.Text;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.AI.GroundedAnalysis;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>The outcome of the external evidence check of one answer.</summary>
/// <param name="Text">The answer with invalid list items removed; markers kept for the server-written source list.</param>
/// <param name="GroundingText">
/// What <see cref="NumericGroundingValidator"/> must still check: markers and verified external numbers removed, so
/// every other number is checked against the report.
/// </param>
public sealed record ExternalEvidenceResult(
    bool Passed,
    string Text,
    string GroundingText,
    IReadOnlyList<ExternalSource> Sources,
    int MarkersChecked,
    int ItemsRemoved,
    string? FailureReason = null);

/// <summary>
/// Change «sol-deep-analysis-web-search» (design-proposal §11, FR-10): checks the <c>[Wn]</c> markers of an analysis
/// answer. Pure; no I/O and no model. A marker is valid when its declared source is a page the hosted search actually
/// consulted (normalised URL), has a publication date, and — when it declares a value — that value is the number written
/// right before the marker. A list item with an invalid marker is removed; an invalid marker on any other line fails
/// the answer. It proves provenance, not truth (R4). An answer without markers is returned unchanged.
/// </summary>
public static partial class ExternalEvidenceValidator
{
    private const int MaximumTitleLength = 120;

    public static ExternalEvidenceResult Validate(
        string answer,
        IReadOnlyList<DeclaredExternalSource> declared,
        IReadOnlyCollection<string> consultedUrls)
    {
        if (!Marker().IsMatch(answer))
            return new ExternalEvidenceResult(true, answer, answer, [], 0, 0);

        var consulted = consultedUrls.Select(NormalizeUrl).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var valid = declared
            .Where(source => SourceId().IsMatch(source.Id) && source.PublishedDate.Trim().Length > 0 &&
                             NormalizeUrl(source.Url) is { } url && consulted.Contains(url))
            .GroupBy(source => source.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var lines = answer.Split('\n');
        var keptText = new List<string>(lines.Length);
        var keptGrounding = new List<string>(lines.Length);
        var cited = new List<DeclaredExternalSource>();
        var markersChecked = 0;
        var removed = 0;
        var firstContentLine = Array.FindIndex(lines, line => line.Trim().Length > 0);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!Marker().IsMatch(line))
            {
                keptText.Add(line);
                keptGrounding.Add(line);
                continue;
            }

            var check = CheckLine(line, valid);
            markersChecked += check.Markers;
            if (check.GroundingLine is not null)
            {
                keptText.Add(line);
                keptGrounding.Add(check.GroundingLine);
                cited.AddRange(check.Cited);
                continue;
            }

            if (index != firstContentLine && ListItem().IsMatch(line))
            {
                removed++;
                continue;
            }

            return new ExternalEvidenceResult(false, answer, answer, [], markersChecked, removed,
                index == firstContentLine ? "invalid_takeaway" : "invalid_paragraph");
        }

        var sources = cited
            .DistinctBy(source => source.Id, StringComparer.Ordinal)
            .Select(source => new ExternalSource(source.Id, Title(source), source.Url.Trim(), source.PublishedDate.Trim()))
            .ToArray();
        return new ExternalEvidenceResult(true, string.Join('\n', keptText), string.Join('\n', keptGrounding), sources,
            markersChecked, removed);
    }

    /// <summary>The server-written «منابع» block, appended after <see cref="GroundedAnswerLayout.Normalize"/>.</summary>
    public static string AppendSourcesBlock(string text, IReadOnlyList<ExternalSource> sources)
    {
        if (sources.Count == 0) return text;
        var builder = new StringBuilder(text.TrimEnd()).Append("\n\n**منابع**\n");
        foreach (var source in sources)
            builder.Append("- [").Append(source.Id).Append("] ").Append(source.Title)
                .Append(" (").Append(source.PublishedAt).Append("): ").Append(source.Url).Append('\n');
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Host without «www.», path without a trailing slash, query without tracking parameters (OpenAI adds
    /// <c>utm_source</c>), no fragment. Null for anything that is not an absolute http(s) URL.
    /// </summary>
    internal static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return null;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        var query = string.Join('&', uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !pair.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)));
        return query.Length == 0 ? host + path : $"{host}{path}?{query}";
    }

    private readonly record struct LineCheck(int Markers, string? GroundingLine, IReadOnlyList<DeclaredExternalSource> Cited);

    /// <summary>
    /// Every marker of the line must be valid. The number a marker covers is the one in its segment (the text since the
    /// previous marker or sentence end) equal to the declared value; numbers of the source's date are also covered.
    /// </summary>
    private static LineCheck CheckLine(string line, IReadOnlyDictionary<string, DeclaredExternalSource> valid)
    {
        var covered = new List<(int Index, int Length)>();
        var cited = new List<DeclaredExternalSource>();
        var markers = Marker().Matches(line);
        var numbers = NumericGroundingValidator.ExtractWithPositions(line).ToArray();
        var segmentStart = 0;
        foreach (Match marker in markers)
        {
            var id = "W" + AsciiDigits(marker.Groups[1].Value);
            var start = LastSentenceEnd(line, segmentStart, marker.Index);
            if (!valid.TryGetValue(id, out var source))
                return new LineCheck(markers.Count, null, []);

            var inSegment = numbers.Where(number => number.Index >= start && number.Index + number.Length <= marker.Index).ToArray();
            if (source.Value is { } value)
            {
                var magnitude = Math.Abs(value);
                var match = inSegment.LastOrDefault(number =>
                    Math.Abs(number.Number.Value - magnitude) <= 0.5m * (decimal)Math.Pow(10, -number.Number.Decimals));
                if (match.Length == 0) return new LineCheck(markers.Count, null, []);
                covered.Add((match.Index, match.Length));
            }

            var dateNumbers = NumericGroundingValidator.Extract(source.PublishedDate).Select(number => number.Value).ToHashSet();
            covered.AddRange(inSegment.Where(number => dateNumbers.Contains(number.Number.Value))
                .Select(number => (number.Index, number.Length)));
            cited.Add(source);
            segmentStart = marker.Index + marker.Length;
        }

        var grounding = line.ToCharArray();
        foreach (var (index, length) in covered.Concat(markers.Select(marker => (marker.Index, marker.Length))))
            for (var position = index; position < index + length; position++) grounding[position] = ' ';
        return new LineCheck(markers.Count, new string(grounding), cited);
    }

    private static int LastSentenceEnd(string line, int from, int to)
    {
        var segment = line[from..to];
        var last = SentenceEnd().Matches(segment).LastOrDefault();
        return last is null ? from : from + last.Index + last.Length;
    }

    private static string Title(DeclaredExternalSource source)
    {
        var title = TitleNoise().Replace(source.Title.ReplaceLineEndings(" "), " ").Trim();
        if (title.Length == 0) title = Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ? uri.Host : source.Id;
        return title.Length > MaximumTitleLength ? title[..MaximumTitleLength] + "…" : title;
    }

    private static string AsciiDigits(string digits) =>
        string.Concat(digits.Select(character => character switch
        {
            >= '۰' and <= '۹' => (char)('0' + (character - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
            _ => character
        }));

    [GeneratedRegex(@"\[W([0-9۰-۹٠-٩]{1,2})\]", RegexOptions.CultureInvariant)]
    private static partial Regex Marker();

    [GeneratedRegex(@"^W[0-9]{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceId();

    [GeneratedRegex(@"^\s*(?:-\s|\d+[.)]\s)", RegexOptions.CultureInvariant)]
    private static partial Regex ListItem();

    /// <summary>A sentence end: «.», «!», «?», «؟», «؛» or «;» followed by a space or the end (so «12.5» is not one).</summary>
    [GeneratedRegex(@"[.!?؟؛;](?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[*\[\]<>|`#]+", RegexOptions.CultureInvariant)]
    private static partial Regex TitleNoise();
}
