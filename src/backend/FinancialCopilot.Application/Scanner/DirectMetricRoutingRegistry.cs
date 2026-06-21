using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

[Flags]
public enum DirectMetricRoutingCapabilities
{
    None = 0,
    LookupEligible = 1 << 0,
    DirectQuestionEligible = 1 << 1,
    QuoteMetric = 1 << 2,
    QuoteContextMetric = 1 << 3,
    MonthlyActivityMetric = 1 << 4,
    ValuationMetric = 1 << 5,
    FundamentalMetric = 1 << 6,
    MarketStatisticMetric = 1 << 7,
    SuppressInMonthlyActivityResponses = 1 << 8
}

public sealed record DirectMetricRoutingMatch(
    string MatchedPhrase,
    MetricCode MetricCode,
    DirectMetricRoutingCapabilities Capabilities,
    SymbolLookupPeriodSelector? PeriodSelector,
    string DisplayLabel);

public interface IDirectMetricRoutingRegistry
{
    DirectMetricRoutingMatch? TryResolve(string userMessage, DateOnly asOf);

    bool ContainsDirectMetricTerm(string userMessage, DateOnly asOf);

    SymbolLookupPeriodSelector? ResolvePeriodSelector(string userMessage, MetricCode metricCode);

    string ResolveDisplayLabel(MetricCode metricCode, SymbolLookupPeriodSelector? selector);

    string StripResolvedPhrase(string userMessage, DirectMetricRoutingMatch match);
}

public sealed class DirectMetricRoutingRegistry(
    IMetricAliasResolver aliasResolver,
    IMetricAliasExpressionNormalizer normalizer)
    : IDirectMetricRoutingRegistry
{
    private static readonly IReadOnlyDictionary<string, DirectMetricRoutingCapabilities> CapabilityMap =
        new Dictionary<string, DirectMetricRoutingCapabilities>(StringComparer.OrdinalIgnoreCase)
        {
            ["LATEST_PRICE"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.QuoteMetric |
                DirectMetricRoutingCapabilities.MarketStatisticMetric,
            ["DAILY_CHANGE_PCT"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.QuoteMetric |
                DirectMetricRoutingCapabilities.MarketStatisticMetric,
            ["PE_TTM"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.ValuationMetric |
                DirectMetricRoutingCapabilities.QuoteContextMetric,
            ["PS_TTM"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.ValuationMetric |
                DirectMetricRoutingCapabilities.QuoteContextMetric,
            ["EPS"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.FundamentalMetric,
            ["ROE"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.FundamentalMetric,
            ["ROA"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.FundamentalMetric,
            ["CURRENT_RATIO"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.FundamentalMetric,
            ["MARKET_CAP"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MarketStatisticMetric,
            ["MONTHLY_SALES"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MonthlyActivityMetric |
                DirectMetricRoutingCapabilities.SuppressInMonthlyActivityResponses,
            ["AVG_12M_MONTHLY_SALES"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MonthlyActivityMetric |
                DirectMetricRoutingCapabilities.SuppressInMonthlyActivityResponses,
            ["MONTHLY_SALES_YTD"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MonthlyActivityMetric |
                DirectMetricRoutingCapabilities.SuppressInMonthlyActivityResponses,
            ["MONTHLY_SALES_YTD_PREVIOUS_MONTH"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MonthlyActivityMetric |
                DirectMetricRoutingCapabilities.SuppressInMonthlyActivityResponses,
            ["MONTHLY_SALES_QUANTITY"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MonthlyActivityMetric |
                DirectMetricRoutingCapabilities.SuppressInMonthlyActivityResponses,
            ["MONTHLY_PRODUCTION_QUANTITY"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MonthlyActivityMetric |
                DirectMetricRoutingCapabilities.SuppressInMonthlyActivityResponses,
            ["MONTHLY_SALES_RATE"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.MonthlyActivityMetric |
                DirectMetricRoutingCapabilities.SuppressInMonthlyActivityResponses,
            ["NET_PROFIT_MARGIN"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.FundamentalMetric,
            ["GROSS_PROFIT_MARGIN"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.FundamentalMetric,
            ["OPERATING_PROFIT_MARGIN"] =
                DirectMetricRoutingCapabilities.LookupEligible |
                DirectMetricRoutingCapabilities.DirectQuestionEligible |
                DirectMetricRoutingCapabilities.FundamentalMetric
        };

    private static readonly string[] NoiseTerms =
    [
        "چقدر است", "چقدر هست", "چقدر بوده", "چقدر بود", "چقدر", "است", "هست", "بوده", "بود",
        "نماد", "سهم", "شرکت", "برای", "را", "از", "میخوام", "می‌خوام", "لطفا", "لطفاً"
    ];

    public DirectMetricRoutingMatch? TryResolve(string userMessage, DateOnly asOf)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return null;
        }

        var normalizedMessage = NormalizeQuery(userMessage);
        var candidates = ExtractCandidatePhrases(normalizedMessage);
        DirectMetricRoutingMatch? best = null;

        foreach (var candidate in candidates)
        {
            var resolution = ResolveCandidate(candidate, asOf);
            if (resolution is null)
            {
                continue;
            }

            if (best is null ||
                candidate.Length > best.MatchedPhrase.Length)
            {
                best = resolution with
                {
                    PeriodSelector = ResolvePeriodSelector(userMessage, resolution.MetricCode),
                    DisplayLabel = ResolveDisplayLabel(
                        resolution.MetricCode,
                        ResolvePeriodSelector(userMessage, resolution.MetricCode))
                };
            }
        }

        return best;
    }

    public bool ContainsDirectMetricTerm(string userMessage, DateOnly asOf) =>
        TryResolve(userMessage, asOf) is not null;

    public SymbolLookupPeriodSelector? ResolvePeriodSelector(string userMessage, MetricCode metricCode)
    {
        var normalized = NormalizeQuery(userMessage);

        if (IsQuarterMetric(metricCode))
        {
            if (ContainsAny(normalized, "فصل مشابه سال قبل", "فصل سال قبل"))
            {
                return SymbolLookupPeriodSelector.SameQuarterLastYear;
            }

            if (ContainsAny(normalized, "فصل قبل", "فصل گذشته"))
            {
                return SymbolLookupPeriodSelector.PreviousQuarter;
            }

            if (ContainsAny(normalized, "آخرین فصل", "فصل اخیر"))
            {
                return SymbolLookupPeriodSelector.LatestQuarter;
            }

            return null;
        }

        if (string.Equals(metricCode.Value, "AVG_12M_MONTHLY_SALES", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(normalized, "سال قبل", "ماه مشابه سال قبل"))
            {
                return SymbolLookupPeriodSelector.LastYearAverage12Month;
            }

            return null;
        }

        if (string.Equals(metricCode.Value, "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(normalized, "ماه مشابه سال قبل"))
            {
                return SymbolLookupPeriodSelector.SameMonthLastYear;
            }

            if (ContainsAny(normalized, "ماه قبل", "ماه گذشته"))
            {
                return SymbolLookupPeriodSelector.PreviousMonth;
            }

            if (ContainsAny(normalized, "آخرین ماه", "ماه اخیر"))
            {
                return SymbolLookupPeriodSelector.LatestMonth;
            }
        }

        return null;
    }

    public string ResolveDisplayLabel(MetricCode metricCode, SymbolLookupPeriodSelector? selector) =>
        (metricCode.Value.ToUpperInvariant(), selector) switch
        {
            ("NET_PROFIT_MARGIN", SymbolLookupPeriodSelector.LatestQuarter) => "حاشیه سود خالص آخرین فصل",
            ("NET_PROFIT_MARGIN", SymbolLookupPeriodSelector.PreviousQuarter) => "حاشیه سود خالص فصل قبل",
            ("NET_PROFIT_MARGIN", SymbolLookupPeriodSelector.SameQuarterLastYear) => "حاشیه سود خالص فصل مشابه سال قبل",
            ("GROSS_PROFIT_MARGIN", SymbolLookupPeriodSelector.LatestQuarter) => "حاشیه سود ناخالص آخرین فصل",
            ("GROSS_PROFIT_MARGIN", SymbolLookupPeriodSelector.PreviousQuarter) => "حاشیه سود ناخالص فصل قبل",
            ("GROSS_PROFIT_MARGIN", SymbolLookupPeriodSelector.SameQuarterLastYear) => "حاشیه سود ناخالص فصل مشابه سال قبل",
            ("OPERATING_PROFIT_MARGIN", SymbolLookupPeriodSelector.LatestQuarter) => "حاشیه سود عملیاتی آخرین فصل",
            ("OPERATING_PROFIT_MARGIN", SymbolLookupPeriodSelector.PreviousQuarter) => "حاشیه سود عملیاتی فصل قبل",
            ("OPERATING_PROFIT_MARGIN", SymbolLookupPeriodSelector.SameQuarterLastYear) => "حاشیه سود عملیاتی فصل مشابه سال قبل",
            ("MONTHLY_SALES", SymbolLookupPeriodSelector.LatestMonth) => "فروش آخرین ماه",
            ("MONTHLY_SALES", SymbolLookupPeriodSelector.PreviousMonth) => "فروش ماه قبل",
            ("MONTHLY_SALES", SymbolLookupPeriodSelector.SameMonthLastYear) => "فروش ماه مشابه سال قبل",
            ("AVG_12M_MONTHLY_SALES", SymbolLookupPeriodSelector.LastYearAverage12Month) => "متوسط فروش ۱۲ ماهه سال قبل",
            ("AVG_12M_MONTHLY_SALES", _) => "متوسط فروش ۱۲ ماهه",
            ("MONTHLY_SALES", _) => "فروش ماهانه",
            ("PE_TTM", _) => "نسبت قیمت به سود",
            ("PS_TTM", _) => "نسبت قیمت به فروش",
            ("LATEST_PRICE", _) => "آخرین قیمت",
            ("DAILY_CHANGE_PCT", _) => "تغییر روزانه %",
            ("NET_PROFIT_MARGIN", _) => "حاشیه سود خالص",
            ("GROSS_PROFIT_MARGIN", _) => "حاشیه سود ناخالص",
            ("OPERATING_PROFIT_MARGIN", _) => "حاشیه سود عملیاتی",
            _ => metricCode.Value
        };

    public string StripResolvedPhrase(string userMessage, DirectMetricRoutingMatch match)
    {
        var normalized = NormalizeQuery(userMessage);
        var stripped = normalized.Replace(match.MatchedPhrase, " ", StringComparison.OrdinalIgnoreCase);
        stripped = RemovePeriodPhrases(stripped);

        foreach (var noise in NoiseTerms)
        {
            stripped = stripped.Replace(noise, " ", StringComparison.OrdinalIgnoreCase);
        }

        stripped = stripped.Replace("?", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("؟", " ", StringComparison.OrdinalIgnoreCase)
            .Replace(":", " ", StringComparison.OrdinalIgnoreCase);

        return CollapseWhitespace(stripped);
    }

    private DirectMetricRoutingMatch? ResolveCandidate(string candidate, DateOnly asOf)
    {
        foreach (var language in InferLanguages(candidate))
        {
            var resolution = aliasResolver.ResolveAlias(
                candidate,
                language,
                new MetricResolutionContext(),
                asOf);

            if (resolution.Status != MetricResolutionStatus.Resolved)
            {
                continue;
            }

            var metricCode = resolution.Candidates.Single().Code;
            if (!CapabilityMap.TryGetValue(metricCode.Value, out var capabilities) ||
                !capabilities.HasFlag(DirectMetricRoutingCapabilities.DirectQuestionEligible))
            {
                continue;
            }

            return new DirectMetricRoutingMatch(
                candidate,
                metricCode,
                capabilities,
                null,
                ResolveDisplayLabel(metricCode, null));
        }

        return null;
    }

    private static IEnumerable<string> ExtractCandidatePhrases(string normalizedMessage)
    {
        var tokenized = Regex.Replace(normalizedMessage, @"[^\p{L}\p{N}/%]+", " ");
        var tokens = tokenized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        const int maxWindow = 6;
        for (var length = Math.Min(maxWindow, tokens.Length); length >= 1; length--)
        {
            for (var start = 0; start <= tokens.Length - length; start++)
            {
                yield return string.Join(' ', tokens.Skip(start).Take(length));
            }
        }
    }

    private static IEnumerable<string> InferLanguages(string expression)
    {
        if (ContainsPersian(expression))
        {
            yield return "fa-IR";
            yield return "fa";
            yield break;
        }

        yield return "en-US";
        yield return "en";
        yield return "fa-IR";
    }

    private static bool IsQuarterMetric(MetricCode metricCode) =>
        string.Equals(metricCode.Value, "NET_PROFIT_MARGIN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode.Value, "GROSS_PROFIT_MARGIN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metricCode.Value, "OPERATING_PROFIT_MARGIN", StringComparison.OrdinalIgnoreCase);

    private string NormalizeQuery(string text)
    {
        var normalized = normalizer.Normalize(text, ContainsPersian(text) ? "fa-IR" : "en-US");
        return CollapseWhitespace(
            normalized
                .Replace('\u200c', ' ')
                .Replace('\u200d', ' ')
                .Replace('ي', 'ی')
                .Replace('ك', 'ک'));
    }

    private static string RemovePeriodPhrases(string text)
    {
        var phrases = new[]
        {
            "فصل مشابه سال قبل",
            "فصل سال قبل",
            "فصل قبل",
            "فصل گذشته",
            "آخرین فصل",
            "فصل اخیر",
            "ماه مشابه سال قبل",
            "ماه قبل",
            "ماه گذشته",
            "آخرین ماه",
            "ماه اخیر",
            "سال قبل"
        };

        var result = text;
        foreach (var phrase in phrases)
        {
            result = result.Replace(phrase, " ", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsPersian(string value) =>
        value.Any(ch => ch is >= '\u0600' and <= '\u06ff');

    private static string CollapseWhitespace(string value) =>
        string.Join(' ',
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
