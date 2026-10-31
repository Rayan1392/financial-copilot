using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Scanner.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class CyclicalWavesV2DomainAndMappingTests
{
    [Fact]
    public void Catalog_HasTwentyNineUniqueFieldsMatchingTheDocumentedScreenerSample()
    {
        Assert.Equal(29, ScreenerMetricCatalog.All.Count);
        Assert.Equal(Enumerable.Range(1, 29).Select(id => (short)id), ScreenerMetricCatalog.All.Select(definition => definition.Id));
        Assert.Equal(29, ScreenerMetricCatalog.All.Select(definition => definition.FieldKey).Distinct().Count());
        Assert.All(ScreenerMetricCatalog.All, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(definition.NameFa));
        });

        using var sample = CyclicalWavesV2Fixtures.Parse("screener-shlrd.json");
        foreach (var definition in ScreenerMetricCatalog.All)
        {
            Assert.True(sample.RootElement.TryGetProperty(definition.FieldKey, out _), definition.FieldKey);
        }
    }

    [Fact]
    public void Catalog_UnitsFollowTheContract()
    {
        Assert.Equal(18, ScreenerMetricCatalog.All.Count(definition => definition.Unit == ScreenerMetricUnit.Irr));
        Assert.Equal(9, ScreenerMetricCatalog.All.Count(definition => definition.Unit == ScreenerMetricUnit.Percent));
        Assert.Equal(2, ScreenerMetricCatalog.All.Count(definition => definition.Unit == ScreenerMetricUnit.Ratio));
        Assert.True(ScreenerMetricCatalog.Get("pe").ZeroMeansNotComputable);
        Assert.False(ScreenerMetricCatalog.Get("ps").ZeroMeansNotComputable);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1.5, 1.50, true)]
    [InlineData(null, 0.0, false)]
    [InlineData(0.0, null, false)]
    [InlineData(-2.5, -2.51, false)]
    public void Comparer_TreatsNullAsEqualToNullAndComparesDecimalsByValue(double? stored, double? incoming, bool equal) =>
        Assert.Equal(equal, ScreenerValueComparer.AreEqual((decimal?)stored, (decimal?)incoming));

    [Theory]
    [InlineData("۱۰۰ میلیارد تومان", 100, 1_000_000_000_000)]
    [InlineData("۵۰۰ میلیون ریال", 500, 500_000_000)]
    [InlineData("۲ همت", 2, 20_000_000_000_000)]
    [InlineData("۱٬۵ میلیارد تومان", 1.5, 15_000_000_000)]
    [InlineData("فروش ماه اخیر بیشتر از 100 میلیارد تومان", 100_000_000_000, 1_000_000_000_000)]
    [InlineData("فروش بیش از ۱۰۰ هزار میلیارد ریال", 100, 100_000_000_000_000)]
    [InlineData("فروش بیش از ۱۲۰٬۰۰۰ میلیون ریال", 120_000, 120_000_000_000)]
    public void MonetaryResolver_ConvertsUserAmountsToIrr(string query, double threshold, double expectedIrr)
    {
        var resolution = MonetaryThresholdResolver.Resolve(query, (decimal)threshold);

        Assert.Equal(MonetaryThresholdStatus.Resolved, resolution.Status);
        Assert.Equal((decimal)expectedIrr, resolution.IrrValue);
    }

    [Fact]
    public void MonetaryResolver_MissingUnitAsksTheUser()
    {
        Assert.Equal(MonetaryThresholdStatus.MissingUnit, MonetaryThresholdResolver.Resolve("فروش بیشتر از ۱۰۰ میلیارد", 100m).Status);
        Assert.Equal(MonetaryThresholdStatus.MissingUnit, MonetaryThresholdResolver.Resolve("فروش بیشتر از ۱۰۰", 100m).Status);
    }

    [Fact]
    public void MonetaryResolver_TwoEqualNumbersWithDifferentUnitsAreAmbiguous() =>
        Assert.Equal(
            MonetaryThresholdStatus.Ambiguous,
            MonetaryThresholdResolver.Resolve("فروش بیشتر از ۱۰۰ میلیارد تومان و سود بیشتر از ۱۰۰ میلیارد ریال", 100m).Status);

    [Fact]
    public void MonetaryResolver_ZeroNeedsNoUnit()
    {
        var resolution = MonetaryThresholdResolver.Resolve("سود خالص فصل اخیر بیشتر از صفر", 0m);

        Assert.Equal(MonetaryThresholdStatus.ZeroNoUnitNeeded, resolution.Status);
        Assert.Equal(0m, resolution.IrrValue);
    }

    [Theory]
    [InlineData(120, 100, 20)]
    [InlineData(50, -100, 150)]
    [InlineData(-50, 100, -150)]
    [InlineData(-150, -100, -50)]
    public void Growth_UsesTheExistingFormulaWithAbsoluteBase(double current, double baseValue, double expectedPercent) =>
        Assert.Equal((decimal)expectedPercent, ScreenerGrowth.Percent((decimal)current, (decimal)baseValue));

    [Fact]
    public void Growth_ZeroOrMissingBaseIsUnavailable()
    {
        Assert.Null(ScreenerGrowth.Percent(10m, 0m));
        Assert.Null(ScreenerGrowth.Percent(null, 10m));
        Assert.Null(ScreenerGrowth.Percent(10m, null));
    }

    [Fact]
    public void Growth_MatchesTheDomainPercentageGrowthCalculatorFormula()
    {
        // PercentageGrowthMetricCalculator: (current - prior) / Math.Abs(prior) * 100, null when prior is 0.
        decimal current = 834_166_799m, prior = -120_356_493m;
        Assert.Equal((current - prior) / Math.Abs(prior) * 100m, ScreenerGrowth.Percent(current, prior));
    }

    [Theory]
    [InlineData("PE_TTM", "pe")]
    [InlineData("PS_TTM", "ps")]
    [InlineData("REVENUE", "lastQuarterSale")]
    [InlineData("NET_PROFIT", "lastQuarterNetProfit")]
    [InlineData("GROSS_PROFIT", "lastQuarterGrossProfit")]
    [InlineData("OPERATING_PROFIT", "lastQuarterOperatingProfit")]
    [InlineData("NET_PROFIT_MARGIN", "lastQuarterNetProfitMargin")]
    [InlineData("GROSS_PROFIT_MARGIN", "lastQuarterGrossProfitMargin")]
    [InlineData("OPERATING_PROFIT_MARGIN", "lastQuarterOperatingProfitMargin")]
    [InlineData("MONTHLY_SALES", "lastMonthSale")]
    [InlineData("AVG_12M_MONTHLY_SALES", "average12MonthSale")]
    [InlineData("AVG_4Q_REVENUE", "average4QuarterSale")]
    public void ScannerMapping_DirectCodes(string code, string field)
    {
        var binding = ScreenerMetricMapping.ForScanner(new MetricCode(code));

        Assert.Equal(ScreenerOperandKind.Direct, binding.Kind);
        Assert.Equal(field, binding.FieldKey);
    }

    [Theory]
    [InlineData("REVENUE_GROWTH_QOQ", "lastQuarterSale", "penultimateQuarterSale")]
    [InlineData("NET_PROFIT_GROWTH_YOY", "lastQuarterNetProfit", "lastYearSameQuarterNetProfit")]
    [InlineData("GROSS_PROFIT_GROWTH_QOQ", "lastQuarterGrossProfit", "penultimateQuarterGrossProfit")]
    [InlineData("OPERATING_PROFIT_GROWTH_YOY", "lastQuarterOperatingProfit", "lastYearSameQuarterOperatingProfit")]
    [InlineData("MONTHLY_SALES_GROWTH_MOM", "lastMonthSale", "penultimateMonthSale")]
    [InlineData("MONTHLY_SALES_GROWTH_YOY", "lastMonthSale", "lastYearSameMonthSale")]
    public void ScannerMapping_GrowthCodes(string code, string current, string baseField)
    {
        var binding = ScreenerMetricMapping.ForScanner(new MetricCode(code));

        Assert.Equal(ScreenerOperandKind.Growth, binding.Kind);
        Assert.Equal((current, baseField), (binding.FieldKey, binding.BaseFieldKey));
        Assert.Equal(ScreenerMetricUnit.Percent, binding.ComparisonUnit);
    }

    [Fact]
    public void ScannerMapping_HasTwelveDirectAndTenGrowthCodes()
    {
        Assert.Equal(12, ScreenerMetricMapping.DirectScannerCodes.Count);
        Assert.Equal(10, ScreenerMetricMapping.GrowthScannerCodes.Count);
    }

    [Fact]
    public void ScannerMapping_BaseCodeWithComparisonMeansItsGrowth()
    {
        var binding = ScreenerMetricMapping.ForScanner(new MetricCode("REVENUE"), GrowthComparison.QuarterOverQuarter);

        Assert.Equal("REVENUE_GROWTH_QOQ", binding.MetricCode);
        Assert.Equal(ScreenerOperandKind.Growth, binding.Kind);
    }

    [Theory]
    [InlineData("EPS_GROWTH_QOQ")]
    [InlineData("EBIT")]
    [InlineData("EBIT_GROWTH_YOY")]
    [InlineData("MONTHLY_SALES_QUANTITY")]
    [InlineData("TOTAL_ASSETS")]
    [InlineData("ROE")]
    public void ScannerMapping_UnpublishedCriteriaAreUnavailable(string code) =>
        Assert.False(ScreenerMetricMapping.ForScanner(new MetricCode(code)).IsAvailable);

    [Fact]
    public void ScannerMapping_GrowthComparisonWithoutAV2GrowthIsUnavailableNotTheLevel() =>
        Assert.False(ScreenerMetricMapping.ForScanner(new MetricCode("PE_TTM"), GrowthComparison.YearOverYear).IsAvailable);

    [Theory]
    [InlineData("REVENUE", null, "lastQuarterSale")]
    [InlineData("REVENUE", SymbolLookupPeriodSelector.PreviousQuarter, "penultimateQuarterSale")]
    [InlineData("REVENUE", SymbolLookupPeriodSelector.SameQuarterLastYear, "lastYearSameQuarterSale")]
    [InlineData("NET_PROFIT_MARGIN", SymbolLookupPeriodSelector.SameQuarterLastYear, "lastYearSameQuarterNetProfitMargin")]
    [InlineData("MONTHLY_SALES", SymbolLookupPeriodSelector.PreviousMonth, "penultimateMonthSale")]
    [InlineData("MONTHLY_SALES", SymbolLookupPeriodSelector.SameMonthLastYear, "lastYearSameMonthSale")]
    [InlineData("AVG_12M_MONTHLY_SALES", SymbolLookupPeriodSelector.LastYearAverage12Month, "lastYearAverage12MonthSale")]
    [InlineData("PE_TTM", null, "pe")]
    public void LookupMapping_FollowsDesignTable(string code, SymbolLookupPeriodSelector? selector, string field) =>
        Assert.Equal(field, ScreenerMetricMapping.ForLookup(new MetricCode(code), selector).FieldKey);

    [Theory]
    [InlineData("REVENUE", SymbolLookupPeriodSelector.LatestMonth)]
    [InlineData("PE_TTM", SymbolLookupPeriodSelector.PreviousQuarter)]
    [InlineData("ROE", null)]
    public void LookupMapping_NeverSubstitutesAnotherPeriod(string code, SymbolLookupPeriodSelector? selector) =>
        Assert.False(ScreenerMetricMapping.ForLookup(new MetricCode(code), selector).IsAvailable);

    [Fact]
    public void Availability_CoversEveryRouteInBothModes()
    {
        var noavaran = new ChatCapabilityAvailability(FinancialDataChatProvider.NoavaranAmin);
        var cyclicalWaves = new ChatCapabilityAvailability(FinancialDataChatProvider.CyclicalWaves);

        foreach (var route in CapabilityExecutionRoutes.All)
        {
            var inNoavaran = noavaran.Evaluate(route);
            var inCyclicalWaves = cyclicalWaves.Evaluate(route);
            if (ChatCapabilityAvailability.RetiredRoutes.Contains(route))
            {
                Assert.Equal(ChatCapabilityAvailabilityStatus.Retired, inNoavaran.Status);
                Assert.Equal(ChatCapabilityAvailabilityStatus.Retired, inCyclicalWaves.Status);
                Assert.Equal(ChatCapabilityAvailability.RetiredMessageFa, inNoavaran.PersianMessage);
                continue;
            }

            if (route == CapabilityExecutionRoutes.ComprehensiveAnalysis)
            {
                Assert.Equal(ChatCapabilityAvailability.ComprehensiveAnalysisUnavailableReasonCode, inNoavaran.ReasonCode);
                Assert.True(inCyclicalWaves.IsAvailable);
                continue;
            }

            Assert.True(inNoavaran.IsAvailable, route);
            Assert.Equal(ChatCapabilityAvailability.CyclicalWavesSupportedRoutes.Contains(route), inCyclicalWaves.IsAvailable);
            if (!inCyclicalWaves.IsAvailable)
                Assert.Equal(ChatCapabilityAvailability.UnavailableForProviderMessageFa, inCyclicalWaves.PersianMessage);
        }
    }

    [Fact]
    public void Availability_MessagesReachTheUserThroughTheOutcomePolicy()
    {
        var message = AiDialogueOutcomePolicy.ComposeSystemMessage(new DialogueOutcomeResult(
            DialogueOutcome.Unsupported, ChatCapabilityAvailability.ComprehensiveAnalysisUnavailableReasonCode, "fa", null, false));

        Assert.Equal(ChatCapabilityAvailability.ComprehensiveAnalysisUnavailableMessageFa, message);
    }
}
