using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Domain.Financial.Reports;

namespace FinancialCopilot.Infrastructure.Financial.MarketReports;

internal sealed partial class MarketReportNarrativePolicy
{
    public const string PromptPolicyVersion = "market-report-prompt-v1";
    public const string RenderingPolicyVersion = "market-report-rendering-v1";
    public const string SafetyPolicyVersion = "market-report-safety-v1";

    private static readonly string[] ProhibitedPhrases =
    [
        "سیگنال خرید", "سیگنال فروش", "حتماً بخرید", "حتماً بفروشید", "تضمینی",
        "قیمت هدف", "سبد شما", "سود شما", "زیان شما", "buy now", "sell now",
        "guaranteed return", "price target", "caused by", "باعث شد", "به دلیل قطعی"
    ];

    public string BuildSystemPrompt(MarketReportScope scope) =>
        $"""
        You render a Persian {scope} market report only from the supplied persisted evidence.
        Every factual sentence must end with one or more evidence markers in the exact form [e:EVIDENCE_ID].
        Every numeric value must occur in the cited evidence item's numericValues collection.
        Do not calculate new values. Do not state price targets, buy/sell instructions, portfolio exposure,
        profit/loss, suitability, or unsupported causality. Use qualified language for possible drivers.
        State partial, stale, excluded, or unavailable evidence explicitly. Keep the output informational.
        """;

    public bool TryValidate(
        string? narrative,
        MarketReportEvidenceBundle evidence,
        out string failureReason)
    {
        if (string.IsNullOrWhiteSpace(narrative))
        {
            failureReason = "The AI provider returned an empty narrative.";
            return false;
        }

        var normalized = narrative.Trim();
        var prohibited = ProhibitedPhrases.FirstOrDefault(phrase =>
            normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        if (prohibited is not null)
        {
            failureReason = $"The narrative violated safety policy with prohibited phrase '{prohibited}'.";
            return false;
        }

        var byId = evidence.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var citedAny = false;
        foreach (var line in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var citations = CitationRegex().Matches(line).Select(match => match.Groups[1].Value).Distinct().ToArray();
            if (citations.Length > 0) citedAny = true;
            if (citations.Any(id => !byId.ContainsKey(id)))
            {
                failureReason = "The narrative cited an evidence id that is not in the persisted bundle.";
                return false;
            }

            var textWithoutCitations = CitationRegex().Replace(line, string.Empty);
            var numbers = NumberRegex().Matches(MarketReportEvidenceAssembler.NormalizeDigits(textWithoutCitations))
                .Select(match => MarketReportEvidenceAssembler.Canonical(match.Value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (numbers.Length == 0) continue;
            if (citations.Length == 0)
            {
                failureReason = "A numeric sentence did not cite persisted evidence.";
                return false;
            }

            var allowed = citations.SelectMany(id => byId[id].NumericValues)
                .ToHashSet(StringComparer.Ordinal);
            if (numbers.Any(number => !allowed.Contains(number)))
            {
                failureReason = "A numeric claim was not present in the cited evidence item.";
                return false;
            }
        }

        if (!citedAny)
        {
            failureReason = "The narrative contained no evidence citations.";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    public string BuildFallback(MarketReportScope scope, MarketReportEvidenceBundle evidence)
    {
        var builder = new StringBuilder();
        var meta = evidence.Items.First(item => item.Kind == "PulseMetadata");
        builder.AppendLine(scope == MarketReportScope.PersonalDigest ? "خلاصه شخصی پایان روز" :
            evidence.IsFinal ? "گزارش نهایی بازار" : "گزارش درون‌روزی بازار");
        builder.AppendLine($"تاریخ معاملاتی: {evidence.TradingDate:yyyy-MM-dd} [e:{meta.Id}]");

        if (evidence.FollowedSymbols.Count > 0)
            builder.AppendLine($"نمادهای دنبال‌شده: {string.Join("، ", evidence.FollowedSymbols)}");

        foreach (var item in evidence.Items.Where(item => item.Kind is "PulseFact" or "Breadth").Take(5))
            builder.AppendLine($"- {item.Text} [e:{item.Id}]");

        foreach (var item in evidence.Items.Where(item => item.Kind is "LeadingIndustry" or "LaggingIndustry").Take(4))
            builder.AppendLine($"- {item.Label}: {item.Text} [e:{item.Id}]");

        var events = evidence.Items.Where(item => item.Kind == "InsightEvent").ToArray();
        if (events.Length > 0)
        {
            builder.AppendLine("رویدادهای منتخب:");
            foreach (var item in events.Take(8))
                builder.AppendLine($"- {item.Text} [e:{item.Id}]");
            builder.AppendLine("موارد قابل پیگیری در روز معاملاتی بعد:");
            foreach (var item in events.Take(3))
                builder.AppendLine($"- پیگیری به‌روزرسانی شواهد «{item.Label}» بدون استنباط توصیه معاملاتی. [e:{item.Id}]");
        }

        if (evidence.Caveats.Count > 0)
        {
            builder.AppendLine("ملاحظات:");
            foreach (var caveat in evidence.Caveats) builder.AppendLine($"- {caveat}");
        }
        if (evidence.ExcludedReasons.Count > 0)
        {
            builder.AppendLine("پوشش داده:");
            foreach (var reason in evidence.ExcludedReasons) builder.AppendLine($"- {reason}");
        }
        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"\[e:([^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
