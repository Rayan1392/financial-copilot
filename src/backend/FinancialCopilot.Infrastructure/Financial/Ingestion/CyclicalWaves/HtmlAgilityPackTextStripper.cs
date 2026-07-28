using System.Net;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Ingestion;
using HtmlAgilityPack;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class HtmlAgilityPackTextStripper : IHtmlTextStripper
{
    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.Compiled);

    public string Strip(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var text = doc.DocumentNode.InnerText;
        text = WebUtility.HtmlDecode(text);
        text = CollapseWhitespace.Replace(text, " ");
        return text.Trim();
    }
}
