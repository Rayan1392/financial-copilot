using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class PersianReportingPeriodNormalizerTests
{
    [Theory]
    [InlineData("شهریور ۱۴۰۵", 1405, 6, "2026-08-23", "2026-09-22")]
    [InlineData("شهریور 1405", 1405, 6, "2026-08-23", "2026-09-22")]
    [InlineData("1405/06", 1405, 6, "2026-08-23", "2026-09-22")]
    [InlineData("۱۴۰۵-۰۶", 1405, 6, "2026-08-23", "2026-09-22")]
    [InlineData("مرداد ماه ۱۴۰۵", 1405, 5, "2026-07-23", "2026-08-22")]
    [InlineData("اسفند ۱۴۰۳", 1403, 12, "2025-02-19", "2025-03-20")]
    [InlineData("فروردین ۱۴۰۵", 1405, 1, "2026-03-21", "2026-04-20")]
    public void ParsesPersianMonthLabelsDeterministically(string label, int year, int month, string start, string end)
    {
        var period = PersianReportingPeriodNormalizer.Parse(label);
        Assert.Equal(PersianReportingPeriodStatus.Normalized, period.Status);
        Assert.Equal(year, period.PersianYear);
        Assert.Equal(month, period.PersianMonth);
        Assert.Equal(DateOnly.Parse(start), period.Start);
        Assert.Equal(DateOnly.Parse(end), period.End);
    }

    [Theory]
    [InlineData("سال گذشته")]
    [InlineData("1405")]
    [InlineData("ماه قبل")]
    public void RejectsLabelsWithoutYearAndMonth(string label)
    {
        var period = PersianReportingPeriodNormalizer.Parse(label);
        Assert.Equal(PersianReportingPeriodStatus.Unparseable, period.Status);
        Assert.Null(period.Start);
    }

    [Theory]
    [InlineData("1405/13")]
    [InlineData("1200/06")]
    public void RejectsOutOfRangeMonths(string label)
    {
        Assert.Equal(PersianReportingPeriodStatus.Unsupported, PersianReportingPeriodNormalizer.Parse(label).Status);
    }

    [Fact]
    public void OnlyExplicitMonthIntentsAreNormalized()
    {
        Assert.Equal(PersianReportingPeriodStatus.NotApplicable,
            PersianReportingPeriodNormalizer.Normalize(new(ReportingPeriodKind.LatestAvailable)).Status);
        var normalized = PersianReportingPeriodNormalizer.Normalize(
            new(ReportingPeriodKind.ExplicitMonth, ReportingPeriodGranularity.Month, UserPeriodLabel: "شهریور ۱۴۰۵"));
        Assert.Equal(new DateOnly(2026, 8, 23), normalized.Start);
        Assert.Equal(PersianReportingPeriodStatus.Unparseable,
            PersianReportingPeriodNormalizer.Normalize(new(ReportingPeriodKind.ExplicitMonth)).Status);
    }

    [Fact]
    public void ModelProvidedBoundsAreReplacedAndRecorded()
    {
        var requirements = new QuestionSchemaRequirements(1, "1", "hash", ["metric_monthly_production_quantity"], [], ["company", "product"],
            [], [], ["latest"], [], ["scalar"],
            StructuredShape: new StructuredPlanShape([], [], [], [], [], ReportingPeriodStart: new(2026, 8, 1), ReportingPeriodEnd: new(2026, 8, 31)));
        var period = PersianReportingPeriodNormalizer.Parse("شهریور ۱۴۰۵");

        var applied = ReportingPeriodRequirementsNormalizer.Apply(requirements, period);

        Assert.Equal(new DateOnly(2026, 8, 23), applied.StructuredShape!.ReportingPeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 22), applied.StructuredShape.ReportingPeriodEnd);
        Assert.Equal(["explicit-period"], applied.PeriodPolicies);
        Assert.Contains("persian_period_normalized:1405-06:2026-08-23..2026-09-22", applied.NormalizationDiagnostics);
        Assert.Contains("model_period_bounds_replaced:2026-08-01..2026-08-31", applied.NormalizationDiagnostics);
    }

    [Theory]
    [InlineData("فروردین", "شهریور ۱۴۰۵", "2026-03-21", "2026-09-22")]
    [InlineData("فروردین ۱۴۰۵", "شهریور ۱۴۰۵", "2026-03-21", "2026-09-22")]
    [InlineData("دی", "خرداد ۱۴۰۵", "2025-12-22", "2026-06-21")]
    [InlineData("1404/10", "1405/03", "2025-12-22", "2026-06-21")]
    public void MonthRangesAreNormalizedDeterministically(string startLabel, string endLabel, string start, string end)
    {
        var range = PersianReportingPeriodNormalizer.NormalizeRange(
            new(ReportingPeriodKind.DateRange, ReportingPeriodGranularity.Month, StartLabel: startLabel, EndLabel: endLabel));

        Assert.NotNull(range);
        Assert.Equal(DateOnly.Parse(start), range.Value.First.Start);
        Assert.Equal(DateOnly.Parse(end), range.Value.Last.End);
    }

    [Theory]
    [InlineData("شهریور ۱۴۰۵", "فروردین ۱۴۰۵")]
    [InlineData("ابتدای سال", "شهریور ۱۴۰۵")]
    [InlineData("فروردین", "اخیر")]
    public void InvertedOrUnparseableRangesAreNotNormalized(string startLabel, string endLabel)
    {
        Assert.Null(PersianReportingPeriodNormalizer.NormalizeRange(
            new(ReportingPeriodKind.DateRange, ReportingPeriodGranularity.Month, StartLabel: startLabel, EndLabel: endLabel)));
        Assert.Null(PersianReportingPeriodNormalizer.NormalizeRange(new(ReportingPeriodKind.ExplicitMonth, UserPeriodLabel: endLabel)));
    }

    [Fact]
    public void MonthRangeBoundsReplaceModelBoundsAndPeriodCount()
    {
        var requirements = new QuestionSchemaRequirements(1, "1", "hash", ["metric_monthly_production_quantity"], [], ["company", "product"],
            [], [], ["latest"], [], ["time-series"],
            StructuredShape: new StructuredPlanShape([], [], [], [], [], MaximumPeriods: 1));
        var range = PersianReportingPeriodNormalizer.NormalizeRange(
            new(ReportingPeriodKind.DateRange, ReportingPeriodGranularity.Month, StartLabel: "فروردین", EndLabel: "شهریور ۱۴۰۵"))!.Value;

        var applied = ReportingPeriodRequirementsNormalizer.Apply(requirements, range.First, range.Last);

        Assert.Equal(new DateOnly(2026, 3, 21), applied.StructuredShape!.ReportingPeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 22), applied.StructuredShape.ReportingPeriodEnd);
        Assert.Null(applied.StructuredShape.MaximumPeriods);
        Assert.Equal(["explicit-period"], applied.PeriodPolicies);
        Assert.Contains("persian_period_normalized:1405-01..1405-06:2026-03-21..2026-09-22", applied.NormalizationDiagnostics);
    }
}
