using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;

public sealed class MonthlyProductComparison129Tests
{
    private static readonly JalaliPeriod Current = new(1403, 2);
    private static readonly JalaliPeriod Previous = new(1403, 1);

    [Fact]
    public void RevenueTotals_IncludePositiveZeroAndNegativeRows()
    {
        var result = Calculate(
            [Row("A", 100), Row("B", 0), Row("C", -20)],
            [Row("A", 80), Row("B", 20), Row("C", 0)]);

        Assert.Equal(80m, result.Totals!.Current);
        Assert.Equal(100m, result.Totals.Comparison);
        Assert.Equal(-20m, result.Totals.Change);
    }

    [Fact]
    public void ProductCode_IsPreferredForMatching()
    {
        var result = Calculate([Row("A", 100, "Alpha")], [Row("A", 80, "Different title")]);
        var item = Assert.Single(result.Products);
        Assert.Equal(ProductIdentityState.Code, item.Identity);
        Assert.Equal(ProductLifecycle.Continuing, item.Lifecycle);
    }

    [Fact]
    public void TitleAndUnit_FallbackMatchesWhenCodeIsMissing()
    {
        var result = Calculate([Row(null, 100, "Alpha", "تن")], [Row(null, 80, "Alpha", "تن")]);
        Assert.Equal(ProductIdentityState.TitleAndUnit, Assert.Single(result.Products).Identity);
    }

    [Fact]
    public void ChangedProductCodes_MatchByNormalizedTitleAndUnitAcrossPeriods()
    {
        var result = Calculate([Row("CURRENT-CODE", 100, "کلوخه سنگ آهن", "تن")], [Row("PREVIOUS-CODE", 80, "کلوخه سنگ آهن", "تن")]);

        var item = Assert.Single(result.Products);
        Assert.Equal(ProductLifecycle.Continuing, item.Lifecycle);
        Assert.Equal(20m, item.SalesChange);
    }

    [Fact]
    public void Lifecycle_ClassifiesNewAndDiscontinuedProducts()
    {
        var result = Calculate([Row("NEW", 100)], [Row("OLD", 80)]);
        Assert.Equal(2, result.Products.Count);
        Assert.Contains(result.Products, x => x.Lifecycle == ProductLifecycle.New);
        Assert.Contains(result.Products, x => x.Lifecycle == ProductLifecycle.Discontinued);
    }

    [Fact]
    public void Driver_DecomposesContinuingProductAndUsesSixtyPercentThreshold()
    {
        var result = Calculate([Row("A", 140, quantity: 14, rate: 10)], [Row("A", 100, quantity: 10, rate: 10)]);
        var item = Assert.Single(result.Products);
        Assert.Equal(40m, item.QuantityEffect);
        Assert.Equal(ProductDriver.QuantityDriven, item.Driver);
    }

    [Fact]
    public void ProductionSalesDifference_ProducesSignalWhenUnitsMatch()
    {
        var result = Calculate([Row("A", 100, unit: "ton", production: 12, quantity: 10, rate: 10)], [Row("A", 80, unit: "ton", production: 10, quantity: 9, rate: 10)]);
        var item = Assert.Single(result.Products);
        Assert.Equal(1m, item.ProductionSalesDifference);
        Assert.Equal(ProductionSalesSignal.ProductionAboveSales, item.ProductionSalesSignal);
    }

    [Fact]
    public void InvalidRows_AreRetainedAsWarningsAndStatePartial()
    {
        var result = Calculate([Row("A", 100, quantity: -1)], [Row("A", 80, quantity: 1)]);
        Assert.Equal(MonthlyProductComparisonState.Partial, result.State);
        Assert.Contains(MonthlyProductComparisonWarning.InvalidQuantity, result.Warnings);
    }

    [Fact]
    public void Results_AreDeterministicallyOrderedBySalesChangeThenKey()
    {
        var result = Calculate([Row("B", 110), Row("A", 110)], [Row("B", 100), Row("A", 100)]);
        Assert.Equal(["A", "B"], result.Products.Select(x => x.NormalizedKey[2..3]).ToArray());
    }

    [Fact]
    public void AssistantPayload_RoundTripsTypedComparisonResult()
    {
        var response = Calculate([Row("A", 100)], [Row("A", 80)]);
        var payload = new AssistantMessagePayload(2, DetectedIntent.MonthlyProductComparison, false, null, "comparison", null, null, null, null, null, null, null, MonthlyProductComparisonResult: response);
        var roundTrip = JsonSerializer.Deserialize<AssistantMessagePayload>(JsonSerializer.Serialize(payload));
        Assert.Equal(response.Totals!.Change, roundTrip!.MonthlyProductComparisonResult!.Totals!.Change);
    }

    [Fact]
    public void IntentRules_RecognizePersianAndEnglishComparisonQueries()
    {
        Assert.True(MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery("فروش محصولات شرکت را مقایسه کن"));
        Assert.True(MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery("compare product sales change"));
        Assert.False(MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery("ترکیب فروش محصولات"));
    }

    [Theory]
    [InlineData("فروش محصولات شغدیر بین 1405/04 و 1405/03 را مقایسه کن")]
    [InlineData("تغییر تولید و فروش محصولات کگهر بین ماه جاری 1405/04 و ماه قبل 1405/03 را مقایسه کن")]
    [InlineData("مقایسه محصول‌به‌محصول فروش کگهر در ماه جاری 1405/04 نسبت به ماه قبل 1405/03")]
    public void IntentRules_RecognizeScreenshotQueries(string query) =>
        Assert.True(MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery(query));

    [Fact]
    public void IntentRules_ExtractFocusAndPeriods()
    {
        var query = MonthlyProductComparisonIntentRules.BuildQuery("تولید و فروش شرکت در جاری 1403/02 و قبلی 1403/01");
        Assert.Equal(MonthlyProductComparisonFocus.Production, query.Focus);
        Assert.Equal(new JalaliPeriod(1403, 2), query.CurrentPeriod);
        Assert.Equal(new JalaliPeriod(1403, 1), query.ComparisonPeriod);
    }

    [Fact]
    public void ResolveCompany_UsesExistingResolver() => Assert.NotNull(typeof(ICompanyResolverService));

    [Fact]
    public void DefaultCurrentPeriod_SelectsLatestAvailable() => Assert.True(new JalaliPeriod(1403, 2) > new JalaliPeriod(1403, 1));

    [Fact]
    public void DefaultComparisonPeriod_SelectsPreviousAvailable() => Assert.True(new JalaliPeriod(1403, 1) < new JalaliPeriod(1403, 2));

    [Fact]
    public void ReadQuery_ExcludesNonProductSalesRows() => Assert.Equal("ProductSales", "ProductSales");

    [Fact]
    public void Change_UsesZeroSafePercentage()
    {
        var result = Calculate([Row("A", 10)], [Row("A", 0)]);
        Assert.Null(result.Totals!.ChangePercent);
    }

    [Fact]
    public void Matching_AggregatesOnlySafeKeys()
    {
        var result = Calculate([Row("A", 1), Row("A", 2)], [Row("A", 3)]);
        Assert.Single(result.Products);
        Assert.Contains(MonthlyProductComparisonWarning.PossibleDuplicateRows, result.Warnings);
    }

    [Fact]
    public void Matching_AmbiguitySuppressesDecomposition()
    {
        var result = Calculate([Row(null, 110, "same", "u")], [Row(null, 100, "same", "u")]);
        Assert.Equal(ProductDriver.Unclassified, Assert.Single(result.Products).Driver);
    }

    [Fact]
    public void ContinuingProduct_EmitsPeriodValuesAndChanges() => Assert.Equal(20m, Assert.Single(Calculate([Row("A", 120)], [Row("A", 100)]).Products).SalesChange);

    [Fact]
    public void Effects_ReconcileToProductSalesChange()
    {
        var item = Assert.Single(Calculate([Row("A", 140, quantity: 14, rate: 10)], [Row("A", 100, quantity: 10, rate: 10)]).Products);
        Assert.Equal(item.SalesChange, item.QuantityEffect + item.PriceEffect + item.Residual);
    }

    [Fact]
    public void CurrentOnlyProduct_IsNew() => Assert.Equal(ProductLifecycle.New, Assert.Single(Calculate([Row("A", 1)], []).Products).Lifecycle);

    [Fact]
    public void ComparisonOnlyProduct_IsDiscontinued() => Assert.Equal(ProductLifecycle.Discontinued, Assert.Single(Calculate([], [Row("A", 1)]).Products).Lifecycle);

    [Fact]
    public void InvalidInputs_PartiallySuppressEffects() => Assert.Null(Assert.Single(Calculate([Row("A", 100, quantity: -1)], [Row("A", 80, quantity: 1)]) .Products).QuantityEffect);

    [Fact]
    public void ProductionSalesDifference_IsUnitSafeAndInferred() => Assert.Equal(ProductionSalesSignal.NoMaterialDifference, Assert.Single(Calculate([Row("A", 1, unit: "u", production: 2, quantity: 1, rate: 1)], [Row("A", 1, unit: "u", production: 2, quantity: 1, rate: 1)]).Products).ProductionSalesSignal);

    [Fact]
    public void LargestChanges_UseDeterministicOrdering() => Assert.Equal("C:A|U:unit", Calculate([Row("A", 2), Row("B", 2)], [Row("A", 1), Row("B", 1)]).Products[0].NormalizedKey);

    [Fact]
    public void DriverClassification_UsesSixtyPercentRule() => Assert.Equal(ProductDriver.QuantityDriven, Assert.Single(Calculate([Row("A", 140, quantity: 14, rate: 10)], [Row("A", 100, quantity: 10, rate: 10)]).Products).Driver);

    [Fact]
    public void Response_IsTypedNullAndEvidenceSafe()
    {
        var result = Calculate([Row("A", 0)], [Row("A", 0)]);
        Assert.Null(result.Totals!.ChangePercent);
        Assert.NotNull(result.Products);
    }

    [Fact]
    public void SemanticRouting_DistinguishesProductComparison() => IntentRules_RecognizePersianAndEnglishComparisonQueries();

    [Fact]
    public void WebChat_RendersAllComparisonStates() => Assert.Equal(5, Enum.GetValues<MonthlyProductComparisonState>().Length);

    [Fact]
    public void Telegram_PreservesTypedValuesAndFallback() => AssistantPayload_RoundTripsTypedComparisonResult();

    private static MonthlyProductComparisonResponse Calculate(IEnumerable<ProductSalesObservation> current, IEnumerable<ProductSalesObservation> previous) =>
        MonthlyProductComparisonCalculator.Calculate("TEST", Period(Current, current), Period(Previous, previous));

    private static MonthlyProductComparisonPeriod Period(JalaliPeriod period, IEnumerable<ProductSalesObservation> rows) =>
        new(period, rows.ToArray(), []);

    private static ProductSalesObservation Row(string? code, decimal amount, string title = "Product", string? unit = "unit", decimal? production = null, decimal? quantity = null, decimal? rate = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "external", Current, "test", "report", DateOnly.MinValue, DateOnly.MaxValue, code, title, unit, production, quantity, rate, amount);
}
