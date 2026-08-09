using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthAliasAndIntentTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 3);

    private static readonly MetricAliasResolver Resolver = new(
        new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []));

    [Theory]
    [InlineData("sales growth yoy", "MONTHLY_SALES_GROWTH_YOY", GrowthComparison.YearOverYear)]
    [InlineData("sales growth month over month", "MONTHLY_SALES_GROWTH_MOM", GrowthComparison.MonthOverMonth)]
    [InlineData("رشد فروش ماه مشابه سال قبل", "MONTHLY_SALES_GROWTH_YOY", GrowthComparison.YearOverYear)]
    [InlineData("رشد فروش نسبت به ماه قبل", "MONTHLY_SALES_GROWTH_MOM", GrowthComparison.MonthOverMonth)]
    public void GrowthAliases_ResolveToGovernedMetric(
        string expression,
        string expectedMetric,
        GrowthComparison expectedComparison)
    {
        var result = Resolver.ResolveAlias(
            expression,
            expression.Any(c => c > 127) ? "fa-IR" : "en-US",
            new MetricResolutionContext(),
            AsOf);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal(expectedMetric, candidate.Code.Value);
        var matchingAliases = candidate.Aliases
            .Where(alias => MetricAliasTextNormalizer.Normalize(alias.Expression) ==
                MetricAliasTextNormalizer.Normalize(expression))
            .ToArray();
        Assert.NotEmpty(matchingAliases);
        Assert.All(matchingAliases, alias => Assert.Equal(expectedComparison, alias.ComparisonQualifier));
    }

    [Fact]
    public void AliasNormalization_HandlesArabicLettersZwnjAndPersianDigits()
    {
        var normalized = MetricAliasTextNormalizer.Normalize("رشدِ فـروش\u200c ۱۲ ماهه٪");

        Assert.Equal("رشدِ فروش 12 ماهه%", normalized);
    }

    [Theory]
    [InlineData("لیست سهم‌هایی که رشد فروش بالای ۳۰ درصد نسبت به سال گذشته دارند")]
    [InlineData("list stocks with sales growth above 30% versus YoY")]
    [InlineData("نمادها با sales growth حداقل 2× نسبت به میانگین ۱۲ ماهه")]
    public void IntentRule_RecognizesPersianEnglishAndMixedDiscoveryQueries(string query)
    {
        Assert.True(SalesGrowthSymbolScannerIntentRules.LooksLikeSalesGrowthScannerQuery(query));
        Assert.True(SalesGrowthSymbolScannerIntentRules.ContainsComparisonPhrase(query));
    }

    [Fact]
    public void IntentRule_NormalizesDigitsPercentDecimalAndMultiplicationVariants()
    {
        var normalized = SalesGrowthSymbolScannerIntentRules.Normalize("رشد فروش ۲٫۵× یا ۳۰٪");

        Assert.Contains("2", normalized);
        Assert.Contains("30%", normalized);
        Assert.Contains("x", normalized);
    }

    [Fact]
    public void IntentRule_NormalizesPersianAndLatinDecimalDigitsWithoutChangingMagnitude()
    {
        var normalized = SalesGrowthSymbolScannerIntentRules.Normalize("sales growth بالای ۲٫۵ درصد و 1.25 برابر");

        Assert.Contains("2.5", normalized);
        Assert.Contains("1.25", normalized);
    }

    [Theory]
    [InlineData("فروش ماهانه شغدیر")]
    [InlineData("روند فروش ماهانه نمادها")]
    [InlineData("P/E زیر ۵ برای سهام")]
    public void IntentRule_DoesNotClassifyNonGrowthRequests(string query)
    {
        Assert.False(SalesGrowthSymbolScannerIntentRules.LooksLikeSalesGrowthScannerQuery(query));
    }
}
