using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class PersianStructuredQueryResponseGeneratorTests
{
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly Guid CompanyId = Guid.Parse("9e0ae687-4fc7-45ec-b9d0-5367c4b2f891");
    private readonly PersianStructuredQueryResponseGenerator _generator = new();

    [Fact]
    public void ProductionQuantity_UsesExecutedValueUnitPeriodAndSource()
    {
        var response = _generator.Generate(Request(Row(("Value", 47m), ("CompanyId", CompanyId),
            ("ProductTitle", "گندله"), ("PeriodStart", new DateOnly(2026, 8, 23)),
            ("PeriodEnd", new DateOnly(2026, 9, 22)), ("ProviderName", "NoavaranCurrentApi"),
            ("ReportId", "report-17"), ("SourceRowFingerprint", "row-sha-17"), ("SourceUnit", "ton"),
            ("OutputType", 0)), metric: "metric_monthly_production_quantity"));

        Assert.Equal(StructuredAnswerStatus.Answered, response.Status);
        Assert.Contains("۴۷ تن", response.TextAnswer);
        // Noavaran is the default source: it is kept in the evidence and not repeated in the text.
        Assert.DoesNotContain("NoavaranCurrentApi", response.TextAnswer);
        Assert.DoesNotContain("منبع", response.TextAnswer);
        Assert.Equal("NoavaranCurrentApi", Assert.Single(response.Evidence).Provider);
        Assert.Contains("گندله", response.TextAnswer);
        Assert.Contains("برابر با ۴۷ تن بوده است.", response.TextAnswer);
        Assert.Equal("row-sha-17", Assert.Single(response.Evidence).SourceReferences.Single());
    }

    [Fact]
    public void SalesAmount_UsesCatalogAmountUnitInsteadOfQuantityUnit()
    {
        var response = _generator.Generate(Request(Row(("Value", 850m), ("CompanyId", CompanyId),
            ("ProductTitle", null), ("PeriodEnd", new DateOnly(2026, 9, 22)),
            ("ProviderName", "NoavaranCurrentApi"), ("ReportIdentity", new[] { "r-1", "r-2" }),
            ("SourceUnit", "ton")), metric: "metric_monthly_sales", unit: "MillionIRR"));

        Assert.Contains("۸۵ میلیون تومان", response.TextAnswer);
        Assert.DoesNotContain("تن", response.TextAnswer);
        Assert.DoesNotContain("r-1", response.TextAnswer);
        Assert.Contains("r-1", Assert.Single(response.Evidence).ReportIdentities);
    }

    [Fact]
    public void NumericFormatting_UsesPersianSeparatorsAndBillionTomanWithTwoDecimals()
    {
        var response = _generator.Generate(Request(Row(("Value", 1234567.125m), ("CompanyId", CompanyId)),
            metric: "metric_monthly_sales", unit: "MillionIRR"));

        Assert.Contains("۱۲۳٫۴۶ میلیارد تومان", response.TextAnswer);
    }

    [Fact]
    public void Growth_OnlyFormatsStructuredComparisonValuesWithoutRecomputing()
    {
        var response = _generator.Generate(Request(Row(("Value", 12m), ("PreviousValue", 6m),
            ("GrowthPercentage", 100m), ("ComparisonStatus", "comparable"), ("CompanyId", CompanyId),
            ("PreviousSourceUnit", "ton"),
            ("ProductTitle", "کنسانتره"), ("PeriodEnd", new DateOnly(2026, 9, 22)),
            ("ProviderName", "NoavaranCurrentApi"), ("ReportId", "report-growth"), ("SourceUnit", "ton")),
            kind: PlanFinancialOperationKind.GrowthPercentage, metric: "metric_monthly_production_quantity"));

        Assert.Contains("۱۲ تن", response.TextAnswer);
        Assert.Contains("۶ تن", response.TextAnswer);
        Assert.Contains("۱۰۰ درصد", response.TextAnswer);
    }

    [Fact]
    public void SalesQuantity_UsesTheVerifiedUnitFromTheExecutedRowInsteadOfCatalogPlaceholder()
    {
        var response = _generator.Generate(new(Plan("metric_monthly_sales_quantity"),
            Execution(StructuredQueryExecutionStatus.Succeeded,
                [Row(("Value", 40m), ("SourceUnit", "ton"), ("CompanyId", CompanyId))],
                unit: "source-quantity-unit")));

        Assert.Equal(StructuredAnswerStatus.Answered, response.Status);
        Assert.Contains("۴۰ تن", response.TextAnswer);
        Assert.DoesNotContain("source-quantity-unit", response.TextAnswer);
    }

    [Theory]
    [InlineData("kilogram", "کیلوگرم")]
    [InlineData("kg", "کیلوگرم")]
    [InlineData("pieces", "عدد")]
    public void QuantityRenderer_UsesReviewedDisplayForSourceUnits(string sourceUnit, string expectedLabel)
    {
        var response = _generator.Generate(new(Plan("metric_monthly_production_quantity"),
            Execution(StructuredQueryExecutionStatus.Succeeded,
                [Row(("Value", 3m), ("SourceUnit", sourceUnit), ("CompanyId", CompanyId))],
                unit: "source-quantity-unit")));

        Assert.Equal(StructuredAnswerStatus.Answered, response.Status);
        Assert.Contains($"۳ {expectedLabel}", response.TextAnswer);
    }

    [Fact]
    public void QuantityRenderer_UsesUnknownLabelInsteadOfInternalPlaceholderWhenSourceUnitIsMissing()
    {
        var response = _generator.Generate(new(Plan("metric_monthly_production_quantity"),
            Execution(StructuredQueryExecutionStatus.Succeeded,
                [Row(("Value", 3m), ("CompanyId", CompanyId))], unit: "source-quantity-unit")));

        Assert.Contains("۳ واحد اندازه‌گیری نامشخص", response.TextAnswer);
        Assert.DoesNotContain("source-quantity-unit", response.TextAnswer);
    }

    [Fact]
    public void QuantityTimeSeries_WithIncompatibleUnitsDoesNotRenderNumericAnswer()
    {
        var response = _generator.Generate(Request(
            Row(("Value", 1000m), ("SourceUnit", "kg"), ("PeriodEnd", new DateOnly(2026, 8, 31))),
            Row(("Value", 1m), ("SourceUnit", "ton"), ("PeriodEnd", new DateOnly(2026, 9, 30))),
            kind: PlanFinancialOperationKind.TimeSeries, metric: "metric_monthly_production_quantity",
            unit: "source-quantity-unit"));

        Assert.Equal(StructuredAnswerStatus.Ambiguous, response.Status);
        Assert.Equal("unit_validation", response.FailureStage);
        Assert.DoesNotContain("۱٬۰۰۰", response.TextAnswer);
        Assert.DoesNotContain("1000", response.TextAnswer);
        Assert.Contains("monthly_quantity_units_incompatible", response.Diagnostics);
    }

    [Fact]
    public void QuantityComparison_WithDifferentCurrentAndBaselineUnitsDoesNotRenderGrowth()
    {
        var response = _generator.Generate(new(Plan("metric_monthly_production_quantity",
                PlanFinancialOperationKind.GrowthPercentage),
            Execution(StructuredQueryExecutionStatus.Succeeded,
                [Row(("Value", 1m), ("PreviousValue", 1000m), ("SourceUnit", "ton"),
                    ("PreviousSourceUnit", "kg"), ("GrowthPercentage", -99.9m),
                    ("ComparisonStatus", "comparable"), ("CompanyId", CompanyId))],
                unit: "source-quantity-unit")));

        Assert.Equal(StructuredAnswerStatus.Ambiguous, response.Status);
        Assert.DoesNotContain("۹۹٫۹", response.TextAnswer);
        Assert.Contains("monthly_quantity_units_incompatible", response.Diagnostics);
        Assert.Contains("واحدهای اندازه‌گیری", response.TextAnswer);
    }

    [Fact]
    public void TimeSeries_PreservesRowOrderAndEveryExecutedPoint()
    {
        var response = _generator.Generate(Request(
            Row(("Value", 120m), ("PeriodStart", new DateOnly(2026, 3, 1)), ("PeriodEnd", new DateOnly(2026, 3, 31))),
            Row(("Value", 150m), ("PeriodStart", new DateOnly(2026, 4, 1)), ("PeriodEnd", new DateOnly(2026, 4, 30))),
            kind: PlanFinancialOperationKind.TimeSeries, metric: "metric_monthly_sales", unit: "MillionIRR"));

        Assert.Contains("۱۲ میلیون تومان", response.TextAnswer);
        Assert.Contains("۱۵ میلیون تومان", response.TextAnswer);
        Assert.Equal(2, response.Evidence.Count);
    }

    [Fact]
    public void PopulationDiscovery_PartialAnswerIncludesObservedPeriodAndSourceOnce()
    {
        var start = new DateOnly(2026, 8, 23);
        var end = new DateOnly(2026, 9, 22);
        var plan = Plan("metric_monthly_production_quantity") with
        {
            Entities = [new PlanEntityReference("Product", "\u06af\u0646\u062f\u0644\u0647",
                ResolvedEntityKey: "product_iron_ore_pellets", IdentityProvenance: "owner-review", IdentityVerified: true)],
            Period = new PlanPeriodSelection(["explicit-period"], [], false, false, start, end, 1),
            PopulationCoveragePolicyId = "explicit-reporting-period-v1"
        };
        var row = Row(("CompanyId", CompanyId), ("CompanyName", "Fixture producer"), ("CompanySymbol", "FIX"),
            ("ProductTitle", "\u06af\u0646\u062f\u0644\u0647"), ("Value", 62m), ("SourceUnit", "ton"),
            ("PeriodStart", start), ("PeriodEnd", end), ("ProviderName", "NoavaranCurrentApi"),
            ("ExternalReportId", "report-pellet-1"), ("SourceRowFingerprint", "row-pellet-1"));
        var response = _generator.Generate(new(plan, Execution(StructuredQueryExecutionStatus.Incomplete,
            [row], diagnostic: "population_discovery_product_alias_coverage_incomplete")));

        Assert.Equal(StructuredAnswerStatus.Incomplete, response.Status);
        Assert.Contains("Fixture producer (FIX)", response.TextAnswer);
        // Report identities are kept for audit in the evidence, never shown in the text.
        Assert.DoesNotContain("report-pellet-1", response.TextAnswer);
        Assert.Contains("report-pellet-1", Assert.Single(response.Evidence).ReportIdentities);
        // The observation's period is stated once, as its Jalali month (2026-08-23..2026-09-22 = Shahrivar 1405).
        Assert.Equal(1, CountOccurrences(response.TextAnswer, "در شهریور ۱۴۰۵"));
        Assert.DoesNotContain("۲۰۲۶", response.TextAnswer);
        Assert.Contains("فهرست جزئی است", response.TextAnswer);
    }

    [Fact]
    public void ProducerSalesRanking_TotalCompanySalesStatesThePopulationAndSalesScopeSeparately()
    {
        var plan = ProducerSalesPlan(PopulationSalesScope.TotalCompanySales, "metric_monthly_sales");
        var row = Row(("CompanyId", CompanyId), ("CompanyName", "Fixture producer"), ("CompanySymbol", "FIX"),
            ("Value", 850m), ("PeriodStart", new DateOnly(2026, 8, 23)), ("PeriodEnd", new DateOnly(2026, 9, 22)),
            ("ProviderName", "NoavaranCurrentApi"), ("ExternalReportId", "sales-report-1"), ("SourceUnit", "ton"));

        var response = _generator.Generate(new(plan,
            Execution(StructuredQueryExecutionStatus.Succeeded, [row], unit: "MillionIRR")));

        Assert.Equal(StructuredAnswerStatus.Answered, response.Status);
        Assert.Contains("تولیدکنندگان گندله سنگ‌آهن", response.TextAnswer);
        Assert.Contains("فروش ماهانه کل شرکت", response.TextAnswer);
        Assert.Contains("۸۵ میلیون تومان", response.TextAnswer);
        Assert.DoesNotContain("sales-report-1", response.TextAnswer);
        Assert.Contains("sales-report-1", Assert.Single(response.Evidence).ReportIdentities);
    }

    [Fact]
    public void ProducerSalesRanking_IncompleteCompanyCoverageDoesNotRenderPartialNumericRanks()
    {
        var plan = ProducerSalesPlan(PopulationSalesScope.TotalCompanySales, "metric_monthly_sales");
        var row = Row(("CompanyId", CompanyId), ("CompanyName", "Fixture producer"), ("Value", 850m),
            ("PeriodStart", new DateOnly(2026, 8, 23)), ("PeriodEnd", new DateOnly(2026, 9, 22)));

        var response = _generator.Generate(new(plan,
            Execution(StructuredQueryExecutionStatus.Incomplete, [row], diagnostic: "population_discovery_authorized_company_coverage_incomplete")));

        Assert.Equal(StructuredAnswerStatus.Incomplete, response.Status);
        Assert.Contains("پوشش جمعیت تولیدکنندگان", response.TextAnswer);
        Assert.DoesNotContain("۸۵۰", response.TextAnswer);
        Assert.Empty(response.Evidence);
    }

    [Fact]
    public void ProducerSalesRanking_PartialProductCoverageRanksObservedCompaniesAndSaysSo()
    {
        var plan = ProducerSalesPlan(PopulationSalesScope.RequestedProductSales, "metric_monthly_sales") with
        {
            PopulationCoveragePolicyId = "explicit-reporting-period-v1",
            Period = new(["explicit-period"], [], false, false, new DateOnly(2026, 7, 23), new DateOnly(2026, 8, 22))
        };
        var otherCompany = Guid.Parse("0d6b1f5e-2c3a-4b7d-9e8f-1a2b3c4d5e6f");
        var missingCompany = Guid.Parse("7a8b9c0d-1e2f-4a3b-8c5d-6e7f8a9b0c1d");
        IReadOnlyDictionary<string, object?> Sales(Guid id, string symbol, object? value, string report) =>
            Row(("CompanyId", id), ("CompanyName", "شرکت " + symbol), ("CompanySymbol", symbol), ("Value", value),
                ("SourceUnit", "تن"), ("PeriodStart", new DateOnly(2026, 7, 23)), ("PeriodEnd", new DateOnly(2026, 8, 22)),
                ("ProviderName", "NoavaranCurrentApi"), ("ExternalReportId", new[] { report }));

        var response = _generator.Generate(new(plan, Execution(StructuredQueryExecutionStatus.Incomplete,
            // The executor's order (value descending); a NULL total sorts first in PostgreSQL and must not be ranked.
            [Sales(missingCompany, "کنول", null, "r-null"), Sales(CompanyId, "کگل", 136_725_376m, "r-1"),
             Sales(otherCompany, "کگهر", 39_580_764m, "r-2")],
            unit: "MillionIRR", diagnostic: "population_discovery_product_alias_coverage_incomplete")));

        Assert.Equal(StructuredAnswerStatus.Incomplete, response.Status);
        Assert.StartsWith("در مرداد ۱۴۰۵، در میان شرکت‌های تولیدکنندهٔ گندله سنگ‌آهن که اطلاعات معتبر دارند", response.TextAnswer);
        Assert.Contains("رتبهٔ ۱: شرکت کگل (کگل)، ۱۳٬۶۷۲٫۵۴ میلیارد تومان", response.TextAnswer);
        Assert.Contains("رتبهٔ ۲: شرکت کگهر (کگهر)، ۳٬۹۵۸٫۰۸ میلیارد تومان", response.TextAnswer);
        Assert.DoesNotContain("کنول", response.TextAnswer);
        Assert.DoesNotContain("رتبهٔ ۳", response.TextAnswer);
        Assert.Contains("پوشش کامل بازار را نشان نمی‌دهد", response.TextAnswer);
        Assert.Equal(2, response.Evidence.Count);
        Assert.Contains("population_ranking_partial_product_coverage", response.Diagnostics);
    }

    [Fact]
    public void ProducerSalesRanking_PartialQuantityRankingKeepsThePhysicalUnitAndRefusesMixedUnits()
    {
        var plan = ProducerSalesPlan(PopulationSalesScope.RequestedProductSales, "metric_monthly_sales_quantity");
        IReadOnlyDictionary<string, object?> Quantity(Guid id, decimal value, string unit) =>
            Row(("CompanyId", id), ("CompanyName", "Fixture"), ("CompanySymbol", "FIX"), ("Value", value), ("SourceUnit", unit),
                ("PeriodStart", new DateOnly(2026, 7, 23)), ("PeriodEnd", new DateOnly(2026, 8, 22)));

        var tons = _generator.Generate(new(plan, Execution(StructuredQueryExecutionStatus.Incomplete,
            [Quantity(CompanyId, 971_970m, "تن"), Quantity(Guid.NewGuid(), 277_281m, "تن")],
            unit: "row-source-unit", diagnostic: "population_discovery_product_alias_coverage_incomplete")));
        var mixed = _generator.Generate(new(plan, Execution(StructuredQueryExecutionStatus.Incomplete,
            [Quantity(CompanyId, 971_970m, "تن"), Quantity(Guid.NewGuid(), 277_281m, "کیلوگرم")],
            unit: "row-source-unit", diagnostic: "population_discovery_product_alias_coverage_incomplete")));

        Assert.Contains("رتبهٔ ۱: Fixture (FIX)، ۹۷۱٬۹۷۰ تن", tons.TextAnswer);
        Assert.Contains("رتبهٔ ۲: Fixture (FIX)، ۲۷۷٬۲۸۱ تن", tons.TextAnswer);
        Assert.Equal(StructuredAnswerStatus.Ambiguous, mixed.Status);
        Assert.DoesNotContain("۹۷۱", mixed.TextAnswer);
    }

    [Fact]
    public void UnqualifiedProducerSalesRequestAsksWhichSalesScopeTheUserMeans()
    {
        var response = _generator.Generate(new(null, null, "planning_validation", ["producer_sales_scope_ambiguous"]));

        Assert.Equal(StructuredAnswerStatus.Ambiguous, response.Status);
        Assert.Equal("planning_validation", response.FailureStage);
        Assert.Contains("فروش کل شرکت", response.TextAnswer);
        Assert.Contains("فروش خود گندله", response.TextAnswer);
    }

    [Theory]
    [InlineData(StructuredQueryExecutionStatus.NoData, StructuredAnswerStatus.NoData)]
    [InlineData(StructuredQueryExecutionStatus.Ambiguous, StructuredAnswerStatus.Ambiguous)]
    [InlineData(StructuredQueryExecutionStatus.Incomplete, StructuredAnswerStatus.Incomplete)]
    [InlineData(StructuredQueryExecutionStatus.Unauthorized, StructuredAnswerStatus.Failed)]
    [InlineData(StructuredQueryExecutionStatus.TimedOut, StructuredAnswerStatus.Failed)]
    public void NonSuccessExecutionStatusNeverRendersNumericValues(StructuredQueryExecutionStatus executionStatus,
        StructuredAnswerStatus expected)
    {
        var result = Execution(executionStatus, [Row(("Value", 233.33333333333333m),
            ("GrowthPercentage", 233.33333333333333m), ("ComparisonStatus", "period-window-mismatch"))],
            diagnostic: "monthly_comparison_reporting_windows_not_comparable");
        var response = _generator.Generate(new(Plan(), result));

        Assert.Equal(expected, response.Status);
        Assert.DoesNotContain("۲۳۳", response.TextAnswer);
        if (executionStatus == StructuredQueryExecutionStatus.Ambiguous)
            Assert.Equal("ambiguous-not-answerable", Assert.Single(response.Evidence).EvidenceStatus);
        else
            Assert.Empty(response.Evidence);
    }

    [Fact]
    public void UnsupportedWithoutPlan_ReturnsActionableSafeMessage()
    {
        var response = _generator.Generate(new(null, null, "planning_validation", ["population_approval_missing"]));

        Assert.Equal(StructuredAnswerStatus.Unsupported, response.Status);
        Assert.Equal("planning_validation", response.FailureStage);
        Assert.Contains("قابل اجرا نیست", response.TextAnswer);
        Assert.Empty(response.Evidence);
    }

    [Fact]
    public void WarningsAndSourceReferencesArePreserved()
    {
        var response = _generator.Generate(new(Plan(), Execution(StructuredQueryExecutionStatus.Succeeded,
            [Row(("Value", 1.25m), ("ProviderName", "fixture-provider"), ("ExternalReportId", "external-9"),
                ("SourceRowFingerprint", "source-fingerprint-9"))], warnings: ["partial_source_coverage"])));

        Assert.Contains("هشدار: partial_source_coverage", response.TextAnswer);
        Assert.Contains("external-9", response.Evidence[0].ReportIdentities);
        Assert.Contains("source-fingerprint-9", response.Evidence[0].SourceReferences);
    }

    [Theory]
    [InlineData("500", "۵۰ میلیون تومان")]
    [InlineData("8500", "۸۵۰ میلیون تومان")]
    [InlineData("10000", "۱ میلیارد تومان")]
    [InlineData("15000", "۱٫۵ میلیارد تومان")]
    [InlineData("154095165", "۱۵٬۴۰۹٫۵۲ میلیارد تومان")]
    [InlineData("-25000", "منفی ۲٫۵ میلیارد تومان")]
    [InlineData("1025278132", "۱۰۲٬۵۲۷٫۸۱ میلیارد تومان")]
    public void MillionIrrAmountsAreDisplayedInToman(string millionIrr, string expected)
    {
        Assert.Equal(expected, PersianStructuredQueryResponseGenerator.Amount(
            decimal.Parse(millionIrr, System.Globalization.CultureInfo.InvariantCulture), "میلیون ریال"));
    }

    [Fact]
    public void OnlyMillionIrrIsConverted_PerShareAndQuantitiesKeepTheirUnits()
    {
        // Net profit of وبملت (statement item, Million IRR) and sales amount of کگل (monthly SalesAmount).
        var netProfit = _generator.Generate(Request(Row(("Value", 1025278132m), ("CompanyId", CompanyId)), metric: "source_item_net_profit", unit: "MillionIRR"));
        var sales = _generator.Generate(Request(Row(("Value", 154095165m), ("CompanyId", CompanyId)), metric: "metric_monthly_sales", unit: "MillionIRR"));
        var eps = _generator.Generate(Request(Row(("Value", 303m), ("CompanyId", CompanyId)), metric: "source_item_eps", unit: "IRRPerShare"));
        var production = _generator.Generate(Request(Row(("Value", 1085157m), ("CompanyId", CompanyId), ("SourceUnit", "تن")),
            metric: "metric_monthly_production_quantity", unit: "row-source-unit"));

        Assert.Contains("۱۰۲٬۵۲۷٫۸۱ میلیارد تومان", netProfit.TextAnswer);
        Assert.Contains("۱۵٬۴۰۹٫۵۲ میلیارد تومان", sales.TextAnswer);
        Assert.Contains("۳۰۳ ریال به‌ازای هر سهم", eps.TextAnswer);
        Assert.Contains("۱٬۰۸۵٬۱۵۷ تن", production.TextAnswer);
        Assert.DoesNotContain("تومان", eps.TextAnswer + production.TextAnswer);
        // Typed evidence keeps the database value and unit.
        Assert.Equal("154095165", sales.Evidence[0].Facts["Value"]);
        Assert.Equal("میلیون ریال", sales.Unit);
    }

    [Theory]
    [InlineData(false, "بر اساس صورت مالی جداگانه")]
    [InlineData(true, "بر اساس صورت مالی تلفیقی")]
    public void StatementValue_StatesItsReportingBasis(bool isComposing, string basisPhrase)
    {
        var response = _generator.Generate(Request(Row(("Value", 1234567.125m), ("CompanyId", CompanyId),
            ("IsComposing", isComposing), ("PeriodEnd", new DateOnly(2026, 9, 22))), metric: "netProfit", unit: "MillionIRR"));

        Assert.Equal(StructuredAnswerStatus.Answered, response.Status);
        Assert.Contains(basisPhrase, response.TextAnswer);
        Assert.Contains("۱۲۳٫۴۶ میلیارد تومان", response.TextAnswer);
    }

    private static StructuredQueryAnswerRequest Request(IReadOnlyDictionary<string, object?> row,
        string metric = "metric_monthly_sales", string? unit = "ton", PlanFinancialOperationKind kind = PlanFinancialOperationKind.DirectValue) =>
        new(Plan(metric, kind), Execution(StructuredQueryExecutionStatus.Succeeded, [row], unit));

    private static StructuredQueryAnswerRequest Request(IReadOnlyDictionary<string, object?> first,
        IReadOnlyDictionary<string, object?> second, PlanFinancialOperationKind kind, string metric, string? unit) =>
        new(Plan(metric, kind), Execution(StructuredQueryExecutionStatus.Succeeded, [first, second], unit));

    private static StructuredQueryPlan Plan(string metric = "metric_monthly_sales",
        PlanFinancialOperationKind kind = PlanFinancialOperationKind.DirectValue) => new(1, "fixture-v1", "hash", TenantId,
            [new("Company", "کگل", CompanyId, IdentityVerified: true)],
            [new(FinancialValueReferenceType.MonthlyMeasure, metric, null, "NoavaranCurrentApi", null,
                null, metric, "MillionIRR", "owner-approved", "verified", "approved", true)],
            [], [new(metric, PlanProjectionRole.Value)], [], new(["latest"], [], true, false),
            new([], ["NoavaranCurrentApi"], [], [], false, false, false, false, false, true),
            [], [], new(0, 10), kind == PlanFinancialOperationKind.TimeSeries ? PlanPresentation.TimeSeries : PlanPresentation.Scalar,
            new(FinancialValueOperation.DirectValueRetrieval, kind, PlanComparisonMode.None, [], false),
            true, true, true, true, []);

    private static StructuredQueryPlan ProducerSalesPlan(PopulationSalesScope salesScope, string metric) =>
        Plan(metric) with
        {
            Entities = [new PlanEntityReference("Product", "گندله", ResolvedEntityKey: "product_iron_ore_pellets",
                IdentityProvenance: "owner-approved-product-catalog-v1", IdentityVerified: true)],
            ProductConcept = new(ProductConceptResolutionStatus.Resolved, "product_iron_ore_pellets", "product-catalog-v1",
                "گندله سنگ‌آهن", ["گندله", "گندله سنگ‌آهن"], "owner-approved-product-equivalence-v1", "complete", null),
            CompanyDiscoveryScope = new(TenantId, "fixture-scope", "fixture-scope-approval", true),
            PopulationCoveragePolicyId = "observed-production-trailing-12-months-v1",
            PopulationSalesScope = salesScope,
            Period = new(["approved-observation-window"], [], false, false,
                new DateOnly(2025, 10, 1), new DateOnly(2026, 9, 30), 12),
            Presentation = PlanPresentation.RankedTable
        };

    private static StructuredQueryExecutionResult Execution(StructuredQueryExecutionStatus status,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string? unit = "ton", string? diagnostic = null,
        IReadOnlyList<string>? warnings = null) => new(status, rows, [], "fixture-v1", "hash", "query-fingerprint",
        TimeSpan.FromMilliseconds(1), 32, "fixture-read-only", warnings ?? [], diagnostic, unit, "verified", "catalog");

    private static IReadOnlyDictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static int CountOccurrences(string value, string part) =>
        value.Split(part, StringSplitOptions.None).Length - 1;
}
