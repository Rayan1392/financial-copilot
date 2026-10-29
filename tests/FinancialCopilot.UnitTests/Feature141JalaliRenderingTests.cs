using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 141 answers state reporting periods in the Jalali calendar only; values, units, company labels and the Gregorian
/// ISO dates kept in evidence and table rows are unchanged.
/// </summary>
public sealed class Feature141JalaliRenderingTests
{
    private static readonly Guid Kgol = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Kgehr = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Fameli = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    // ---------- formatter ----------

    [Theory]
    [InlineData("ThreeMonths", "2026-03-21", "2026-06-21", "دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵")]
    [InlineData("SixMonths", "2026-03-21", "2026-09-22", "دوره شش‌ماهه منتهی به ۳۱ شهریور ۱۴۰۵")]
    [InlineData("NineMonths", "2026-03-21", "2026-12-21", "دوره نه‌ماهه منتهی به ۳۰ آذر ۱۴۰۵")]
    [InlineData("TwelveMonths", "2025-03-21", "2026-03-20", "سال مالی منتهی به ۲۹ اسفند ۱۴۰۴")]
    // 1403 is a Jalali leap year: Esfand has 30 days (2025-03-20).
    [InlineData("TwelveMonths", "2024-03-20", "2025-03-20", "سال مالی منتهی به ۳۰ اسفند ۱۴۰۳")]
    // A fiscal year that does not start in Farvardin (1 Mehr 1404 .. 31 Shahrivar 1405).
    [InlineData("TwelveMonths", "2025-09-23", "2026-09-22", "سال مالی منتهی به ۳۱ شهریور ۱۴۰۵")]
    public void StatementPeriodsAreNamedFromTheirReportingMetadata(string periodType, string start, string end, string expected) =>
        Assert.Equal(expected, PersianReportingPeriodText.StatementPeriod(periodType, DateOnly.Parse(start), DateOnly.Parse(end)));

    [Theory]
    [InlineData("2026-08-23", "2026-09-22", "شهریور ۱۴۰۵")]
    [InlineData("2025-02-19", "2025-03-20", "اسفند ۱۴۰۳")] // leap Esfand, 30 days
    [InlineData("2026-02-20", "2026-03-20", "اسفند ۱۴۰۴")] // common Esfand, 29 days
    [InlineData("2026-08-23", "2026-09-10", "بازهٔ ۱ شهریور ۱۴۰۵ تا ۱۹ شهریور ۱۴۰۵")]
    public void MonthlyWindowsAreTheirJalaliMonthOrBothBoundaries(string start, string end, string expected) =>
        Assert.Equal(expected, PersianReportingPeriodText.Window(DateOnly.Parse(start), DateOnly.Parse(end)));

    [Fact]
    public void LeapYearBoundaryDatesConvertExactly()
    {
        Assert.Equal("۳۰ اسفند ۱۴۰۳", PersianReportingPeriodText.Date(new DateOnly(2025, 3, 20)));
        Assert.Equal("۱ فروردین ۱۴۰۴", PersianReportingPeriodText.Date(new DateOnly(2025, 3, 21)));
        Assert.Equal("۲۹ اسفند ۱۴۰۴", PersianReportingPeriodText.Date(new DateOnly(2026, 3, 20)));
    }

    [Fact]
    public void MissingOrInconsistentMetadataStatesWhatIsKnownWithoutGuessing()
    {
        // A three-month type whose start is not a whole-month boundary keeps both boundaries.
        Assert.Equal("دوره سه‌ماهه از ۳۰ آذر ۱۴۰۴ تا ۲۹ اسفند ۱۴۰۴",
            PersianReportingPeriodText.StatementPeriod("ThreeMonths", new DateOnly(2025, 12, 21), new DateOnly(2026, 3, 20)));
        Assert.Equal("دوره از ۱ فروردین ۱۴۰۵ تا ۳۱ خرداد ۱۴۰۵",
            PersianReportingPeriodText.StatementPeriod("Unexpected", new DateOnly(2026, 3, 21), new DateOnly(2026, 6, 21)));
        Assert.Equal("دوره منتهی به ۳۱ خرداد ۱۴۰۵", PersianReportingPeriodText.StatementPeriod(null, null, new DateOnly(2026, 6, 21)));
        Assert.Equal("دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵", PersianReportingPeriodText.StatementPeriod("ThreeMonths", null, new DateOnly(2026, 6, 21)));
        Assert.Equal("دوره گزارش نامشخص", PersianReportingPeriodText.StatementPeriod("ThreeMonths", new DateOnly(2026, 3, 21), null));
        Assert.Null(PersianReportingPeriodText.Parse("21/06/2026"));
        Assert.Null(PersianReportingPeriodText.Parse(null));
        Assert.Equal(new DateOnly(2026, 6, 21), PersianReportingPeriodText.Parse("2026-06-21"));
    }

    // ---------- rendered answers ----------

    [Fact]
    public void SingleCompanyStatementAnswerStatesTheJalaliPeriodAndKeepsIsoEvidence()
    {
        var plan = Plan([Company("کگل", Kgol)], Statement(), PlanPresentation.Scalar);
        var answer = Generate(plan, [StatementRow(Kgol, 1_200m)]);
        Assert.Equal(StructuredAnswerStatus.Answered, answer.Status);
        Assert.Contains("کگل در دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵", answer.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("۱٬۲۰۰", answer.TextAnswer, StringComparison.Ordinal);
        AssertNoGregorian(answer.TextAnswer);
        Assert.Equal("2026-03-21", answer.Evidence[0].PeriodStart);
        Assert.Equal("2026-06-21", answer.Evidence[0].PeriodEnd);
    }

    [Theory]
    [InlineData("metric_monthly_production_quantity", "مقدار تولید ماهانه")]
    [InlineData("metric_monthly_sales", "مبلغ فروش ماهانه")]
    public void MonthlyAnswersStateTheJalaliMonth(string metric, string measure)
    {
        var plan = Plan([Company("کگل", Kgol), new PlanEntityReference("Product", "گندله")], Monthly(metric), PlanPresentation.Scalar,
            start: new DateOnly(2026, 8, 23), end: new DateOnly(2026, 9, 22));
        var answer = Generate(plan, [MonthlyRow(Kgol, 25m)]);
        Assert.Equal(StructuredAnswerStatus.Answered, answer.Status);
        Assert.Contains("در شهریور ۱۴۰۵", answer.TextAnswer, StringComparison.Ordinal);
        Assert.Contains(measure, answer.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("۲۵", answer.TextAnswer, StringComparison.Ordinal);
        AssertNoGregorian(answer.TextAnswer);
        Assert.Equal("2026-09-22", answer.Evidence[0].PeriodEnd);
    }

    [Fact]
    public void ComparisonRankingAndFilterAnswersStateTheSharedJalaliPeriod()
    {
        var rows = new[] { StatementRow(Kgol, 1_200m), StatementRow(Kgehr, 900m), StatementRow(Fameli, 1_600m) };
        var labels = new Dictionary<Guid, string> { [Kgol] = "کگل", [Kgehr] = "کگهر", [Fameli] = "فملی" };
        var comparison = Comparison(rows, labels, ranked: false);
        var compared = Generate(Plan([], Statement(), PlanPresentation.Table, comparison: true), comparison.Rows, comparison.Warnings);
        Assert.Contains("در آخرین دورهٔ گزارش مشترک (دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵)", compared.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("کگل: ۱٬۲۰۰ میلیون ریال", compared.TextAnswer, StringComparison.Ordinal);
        AssertNoGregorian(compared.TextAnswer);

        var ranking = Comparison(rows, labels, ranked: true);
        var ranked = Generate(Plan([], Statement(), PlanPresentation.RankedTable, comparison: true), ranking.Rows, ranking.Warnings);
        Assert.Contains("۱. فملی: ۱٬۶۰۰ میلیون ریال", ranked.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵", ranked.TextAnswer, StringComparison.Ordinal);
        AssertNoGregorian(ranked.TextAnswer);

        var filterRows = rows.Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(row)
            { ["FilterValue0"] = row["Value"], ["FilterMatched0"] = (decimal)row["Value"]! > 1_000m }).ToArray();
        var filter = CrossCompanyStatementComparisonPolicy.Resolve(filterRows, [Kgol, Kgehr, Fameli],
            new StatementComparisonSpecification(PlanSortDirection.Descending, null, false, null, labels, false,
                Filters: [new(0, "source_item_net_profit", "سود خالص", PlanFilterOperator.GreaterThan, 1_000m, "MillionIRR")]));
        var filtered = Generate(Plan([], Statement(), PlanPresentation.Table, comparison: true), filter.Rows, filter.Warnings);
        Assert.Contains("با شرط «سود خالص بیشتر از ۱٬۰۰۰ میلیون ریال»، در آخرین دورهٔ گزارش مشترک (دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵)",
            filtered.TextAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("کگهر", filtered.TextAnswer, StringComparison.Ordinal);
        AssertNoGregorian(filtered.TextAnswer);
        // Table rows keep the Gregorian ISO period for structured consumers.
        Assert.Equal("2026-06-21", filtered.TableRows[0]["PeriodEnd"]);
    }

    [Fact]
    public void DifferentLatestPeriodsKeepTheCommonPeriodAndDiscloseTheNewerOneInJalali()
    {
        IReadOnlyDictionary<string, object?> Prior(Guid company, decimal value, DateOnly latest) => new Dictionary<string, object?>(StatementRow(company, value))
        {
            ["PeriodStart"] = new DateOnly(2025, 12, 22), ["PeriodEnd"] = new DateOnly(2026, 3, 20), ["CompanyLatestPeriodEnd"] = latest
        };
        var labels = new Dictionary<Guid, string> { [Kgol] = "کگل", [Kgehr] = "کگهر" };
        var outcome = Comparison([Prior(Kgol, 1_100m, new DateOnly(2026, 6, 21)), Prior(Kgehr, 800m, new DateOnly(2026, 3, 20))], labels, ranked: false);
        var answer = Generate(Plan([], Statement(), PlanPresentation.Table, comparison: true), outcome.Rows, outcome.Warnings);
        // 2025-12-22 is 1 Dey 1404: Dey-Bahman-Esfand is a whole Jalali quarter.
        Assert.Contains("دوره سه‌ماهه منتهی به ۲۹ اسفند ۱۴۰۴", answer.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("برای کگل دورهٔ جدیدتری تا ۳۱ خرداد ۱۴۰۵ گزارش شده", answer.TextAnswer, StringComparison.Ordinal);
        AssertNoGregorian(answer.TextAnswer);
    }

    [Fact]
    public void DiscoveryAnswerStatesTheJalaliMonth()
    {
        var plan = Plan([new PlanEntityReference("Product", "گندله")], Monthly("metric_monthly_production_quantity"), PlanPresentation.Table,
            start: new DateOnly(2026, 8, 23), end: new DateOnly(2026, 9, 22)) with { PopulationCoveragePolicyId = "population-discovery" };
        var row = new Dictionary<string, object?>(MonthlyRow(Kgol, 25m)) { ["CompanySymbol"] = "کگل", ["CompanyName"] = "Fixture کگل" };
        var answer = new PersianStructuredQueryResponseGenerator().Generate(new StructuredQueryAnswerRequest(plan,
            Execution(StructuredQueryExecutionStatus.Incomplete, [row], [], "population_discovery_product_alias_coverage_incomplete")));
        Assert.Contains("در گزارش‌های ماهانهٔ واجد شرایط شهریور ۱۴۰۵", answer.TextAnswer, StringComparison.Ordinal);
        Assert.Contains("۲۵", answer.TextAnswer, StringComparison.Ordinal);
        AssertNoGregorian(answer.TextAnswer);
    }

    // ---------- helpers ----------

    private static void AssertNoGregorian(string text)
    {
        foreach (var marker in new[] { "۲۰۲۵", "۲۰۲۶", "2025", "2026", "میلادی" })
            Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
    }

    private static PlanEntityReference Company(string label, Guid id) => new("Company", label, id, null, "unit", true);

    private static PlanSourceReference Statement() => new(FinancialValueReferenceType.DirectSourceItem, "source_item_net_profit", Guid.NewGuid(),
        "NoavaranCurrentApi", "IncomeStatement", 143, null, "MillionIRR", "approved", "verified", "approved", true, "سود خالص");

    private static PlanSourceReference Monthly(string metric) => new(FinancialValueReferenceType.MonthlyMeasure, metric, null,
        "NoavaranCurrentApi", null, null, metric, null, "approved", "verified", "approved", true);

    private static StructuredQueryPlan Plan(IReadOnlyList<PlanEntityReference> entities, PlanSourceReference source, PlanPresentation presentation,
        DateOnly? start = null, DateOnly? end = null, bool comparison = false) => new(1, "v", "h", Guid.NewGuid(), entities, [source], [],
        [new(source.CatalogId, PlanProjectionRole.Value)], [],
        new PlanPeriodSelection(start is null ? ["latest"] : [], [], start is null, false, start, end),
        new PlanStatementSelection([], ["NoavaranCurrentApi"], [], [], false, true, true, true, true, true), [],
        presentation == PlanPresentation.RankedTable ? [new PlanSort(source.CatalogId, PlanSortDirection.Descending)] : [],
        new PlanPagination(0, 100), presentation,
        new PlanFinancialOperation(FinancialValueOperation.DirectValueRetrieval, PlanFinancialOperationKind.DirectValue,
            comparison ? PlanComparisonMode.MultiCompanyComparison : PlanComparisonMode.None, [], false),
        true, true, true, true, []);

    private static IReadOnlyDictionary<string, object?> StatementRow(Guid company, decimal value) => new Dictionary<string, object?>
    {
        ["CompanyId"] = company, ["Value"] = value, ["PeriodStart"] = new DateOnly(2026, 3, 21), ["PeriodEnd"] = new DateOnly(2026, 6, 21),
        ["PeriodType"] = "ThreeMonths", ["IsComposing"] = false, ["CompanyBasisCount"] = 1L, ["SourceUnit"] = "N/A",
        ["CompanyLatestPeriodEnd"] = new DateOnly(2026, 6, 21), ["StatementId"] = Guid.NewGuid(), ["ProviderName"] = "NoavaranCurrentApi"
    };

    private static IReadOnlyDictionary<string, object?> MonthlyRow(Guid company, decimal value) => new Dictionary<string, object?>
    {
        ["CompanyId"] = company, ["Value"] = value, ["PeriodStart"] = new DateOnly(2026, 8, 23), ["PeriodEnd"] = new DateOnly(2026, 9, 22),
        ["SourceUnit"] = "ton", ["ProductTitle"] = "گندله", ["ProviderName"] = "NoavaranCurrentApi", ["ExternalReportId"] = "kgol-1405-06"
    };

    private static StatementComparisonOutcome Comparison(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyDictionary<Guid, string> labels, bool ranked) => CrossCompanyStatementComparisonPolicy.Resolve(rows, labels.Keys.ToArray(),
        new StatementComparisonSpecification(PlanSortDirection.Descending, null, false, null, labels, ranked));

    private static StructuredQueryExecutionResult Execution(StructuredQueryExecutionStatus status,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<string> warnings, string? diagnostic = null, string? unit = null) =>
        new(status, rows, [], "v", "h", "f", TimeSpan.Zero, 0, "unit", warnings, diagnostic, unit);

    private static PersianStructuredQueryResponse Generate(StructuredQueryPlan plan, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<string>? warnings = null) => new PersianStructuredQueryResponseGenerator().Generate(new StructuredQueryAnswerRequest(plan,
        Execution(StructuredQueryExecutionStatus.Succeeded, rows, warnings ?? [],
            unit: plan.Sources[0].ReferenceType == FinancialValueReferenceType.DirectSourceItem ? "MillionIRR" : null)));
}
