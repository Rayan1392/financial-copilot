using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class ScannerResultColumnPolicyTests
{
    [Fact]
    public void BuildColumns_NoConditionsOrRequested_ReturnsOnlyIdentityColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([]);

        var columns = policy.BuildColumns(plan);

        // Only identity columns — no automatic quote columns for scanner results
        Assert.Equal(2, columns.Count);
        Assert.Contains(columns, c => c.ColumnType == ScannerColumnType.Symbol);
        Assert.Contains(columns, c => c.ColumnType == ScannerColumnType.CompanyName);
        Assert.DoesNotContain(columns, c => c.ColumnType == ScannerColumnType.LatestPrice);
        Assert.DoesNotContain(columns, c => c.ColumnType == ScannerColumnType.DailyChangePercent);
        Assert.DoesNotContain(columns, c => c.ColumnType == ScannerColumnType.MarketCap);
    }

    [Fact]
    public void BuildColumns_SingleCondition_AddsConditionMetricColumn()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM")]);

        var columns = policy.BuildColumns(plan);

        // 2 identity + 1 condition metric = 3
        Assert.Equal(3, columns.Count);
        var metricCol = Assert.Single(columns, c => c.ColumnType == ScannerColumnType.Metric);
        Assert.Equal("PE_TTM", metricCol.MetricCode);
    }

    [Fact]
    public void BuildColumns_NineConditions_KeepsEveryConditionMetric()
    {
        var policy = new ScannerResultColumnPolicy();
        // Condition metrics are deterministic output columns and must not be dropped.
        var conditions = Enumerable.Range(0, 9)
            .Select(i => MakeCondition($"METRIC_{i}"))
            .ToList();
        var plan = MakePlan(conditions);

        var columns = policy.BuildColumns(plan);

        // 2 identity + 9 condition metrics = 11
        Assert.Equal(11, columns.Count);
        foreach (var condition in conditions)
        {
            Assert.Contains(columns, c => c.Identifier == condition.MetricReference.MetricCode.Value);
        }
    }

    [Fact]
    public void BuildColumns_MarketCapCondition_AddsQuoteColumnWithCorrectType()
    {
        var policy = new ScannerResultColumnPolicy();
        // MARKET_CAP used as a filter condition — must appear with MarketCap ColumnType
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("MARKET_CAP")]);

        var columns = policy.BuildColumns(plan);

        // 2 identity + PE_TTM + MARKET_CAP = 4; MARKET_CAP once with correct type
        Assert.Equal(4, columns.Count);
        Assert.Single(columns, c => c.Identifier.Equals("MARKET_CAP", StringComparison.OrdinalIgnoreCase)
            && c.ColumnType == ScannerColumnType.MarketCap);
    }

    [Fact]
    public void BuildColumns_RequestedColumns_AddedAfterConditionColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlanWithRequested([MakeCondition("PE_TTM")], ["NET_PROFIT"]);

        var columns = policy.BuildColumns(plan);

        // 2 identity + PE_TTM + NET_PROFIT = 4
        Assert.Equal(4, columns.Count);
        Assert.Contains(columns, c => c.Identifier == "PE_TTM");
        Assert.Contains(columns, c => c.Identifier == "NET_PROFIT");
    }

    [Fact]
    public void BuildColumns_QuoteColumnSynonymsInRequestedColumns_AreResolvedWithCorrectType()
    {
        var policy = new ScannerResultColumnPolicy();
        // "latest price", "DAILY_CHANGE_PCT", "MARKET_CAP" in RequestedColumns → resolved
        // to their canonical quote column definitions with the correct ColumnType
        var plan = MakePlanWithRequested(
            [MakeCondition("PE_TTM")],
            ["latest price", "DAILY_CHANGE_PCT", "MARKET_CAP"]);

        var columns = policy.BuildColumns(plan);

        // identity + PE_TTM + 3 explicitly requested quote columns = 6
        Assert.Equal(6, columns.Count);
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.LatestPrice);
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.DailyChangePercent);
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.MarketCap);
    }

    [Fact]
    public void BuildColumns_DuplicateConditionAndRequestedMetric_AppearsOnce()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlanWithRequested([MakeCondition("PE_TTM")], ["pe_ttm"]);

        var columns = policy.BuildColumns(plan);

        Assert.Single(columns, c => c.Identifier.Equals("PE_TTM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildColumns_PersianIdentitySynonymsRequested_AreBlocked()
    {
        var policy = new ScannerResultColumnPolicy();
        // Persian identity synonyms must be blocked; Persian quote synonyms resolve to quote columns
        var plan = MakePlanWithRequested(
            [MakeCondition("PE_TTM")],
            ["نماد", "نام شرکت", "قیمت", "آخرین قیمت", "درصد تغییر", "ارزش بازار"]);

        var columns = policy.BuildColumns(plan);

        // "نماد" and "نام شرکت" → blocked (identity synonyms)
        Assert.DoesNotContain(columns, c => c.Identifier == "نماد");
        Assert.DoesNotContain(columns, c => c.Identifier == "نام شرکت");
        // Identity columns appear exactly once each (the canonical ones)
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.Symbol);
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.CompanyName);
        // PE_TTM from condition
        Assert.Contains(columns, c => c.Identifier == "PE_TTM");
        // Persian quote synonyms resolve to quote columns
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.LatestPrice);
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.DailyChangePercent);
        Assert.Single(columns, c => c.ColumnType == ScannerColumnType.MarketCap);
    }

    [Fact]
    public void BuildColumns_PersianPlan_LocalizesIdentityAndMetricLabels()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM")], language: "fa");

        var columns = policy.BuildColumns(plan);

        // No quote columns without an explicit condition/request
        Assert.Equal(3, columns.Count);
        Assert.Contains(columns, c => c.Identifier == "SYMBOL" && c.DisplayName == "نماد");
        Assert.Contains(columns, c => c.Identifier == "COMPANY" && c.DisplayName == "شرکت");
        Assert.Contains(columns, c => c.Identifier == "PE_TTM" && c.DisplayName == "P/E دوازده‌ماهه");
        Assert.DoesNotContain(columns, c => c.Identifier == "LATEST_PRICE");
        Assert.DoesNotContain(columns, c => c.Identifier == "DAILY_CHANGE_PCT");
        Assert.DoesNotContain(columns, c => c.Identifier == "MARKET_CAP");
    }

    [Fact]
    public void BuildColumns_PersianPlan_WithExplicitPriceRequest_LocalizesQuoteColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        // User explicitly asked for LATEST_PRICE as a condition
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("LATEST_PRICE")], language: "fa");

        var columns = policy.BuildColumns(plan);

        Assert.Contains(columns, c => c.Identifier == "SYMBOL" && c.DisplayName == "نماد");
        Assert.Contains(columns, c => c.Identifier == "COMPANY" && c.DisplayName == "شرکت");
        Assert.Contains(columns, c => c.Identifier == "LATEST_PRICE" && c.DisplayName == "آخرین قیمت");
        Assert.Contains(columns, c => c.Identifier == "PE_TTM" && c.DisplayName == "P/E دوازده‌ماهه");
    }

    // --- Bug 3 regression: scanner table column policy ---

    [Fact]
    public void BuildColumns_PeOnlyScanner_ExactlyThreeColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM")]);

        var columns = policy.BuildColumns(plan);

        Assert.Equal(3, columns.Count);
        Assert.Equal("SYMBOL",  columns.ElementAt(0).Identifier);
        Assert.Equal("COMPANY", columns.ElementAt(1).Identifier);
        Assert.Equal("PE_TTM",  columns.ElementAt(2).Identifier);
    }

    [Fact]
    public void BuildColumns_PeAndPsScanner_ExactlyFourColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("PS_TTM")]);

        var columns = policy.BuildColumns(plan);

        Assert.Equal(4, columns.Count);
        Assert.Equal("SYMBOL",  columns.ElementAt(0).Identifier);
        Assert.Equal("COMPANY", columns.ElementAt(1).Identifier);
        Assert.Contains(columns, c => c.Identifier == "PE_TTM");
        Assert.Contains(columns, c => c.Identifier == "PS_TTM");
    }

    [Fact]
    public void BuildColumns_PeAndPsScanner_NoLatestPrice()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("PS_TTM")]);

        var columns = policy.BuildColumns(plan);

        Assert.DoesNotContain(columns, c => c.Identifier == "LATEST_PRICE");
    }

    [Fact]
    public void BuildColumns_PeAndPsScanner_NoDailyChangePct()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("PS_TTM")]);

        var columns = policy.BuildColumns(plan);

        Assert.DoesNotContain(columns, c => c.Identifier == "DAILY_CHANGE_PCT");
    }

    [Fact]
    public void BuildColumns_PeAndPsScanner_NoMarketCap()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("PS_TTM")]);

        var columns = policy.BuildColumns(plan);

        Assert.DoesNotContain(columns, c => c.Identifier == "MARKET_CAP");
    }

    [Fact]
    public void BuildColumns_NoColumnHasIdentifierSymbolsLowercase()
    {
        // "symbols" is an internal LLM schema field and must never appear as a column
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlanWithRequested([MakeCondition("PE_TTM")], ["symbols", "universe", "conditions"]);

        var columns = policy.BuildColumns(plan);

        Assert.DoesNotContain(columns, c => c.Identifier.Equals("symbols",    StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c.Identifier.Equals("universe",   StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c.Identifier.Equals("conditions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildColumns_ExplicitLatestPriceRequest_IncludesLatestPriceWithCorrectType()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlanWithRequested([MakeCondition("PE_TTM")], ["LATEST_PRICE"]);

        var columns = policy.BuildColumns(plan);

        Assert.Contains(columns, c =>
            c.Identifier == "LATEST_PRICE" && c.ColumnType == ScannerColumnType.LatestPrice);
    }

    [Fact]
    public void BuildColumns_MarketCapFilterCondition_IncludesMarketCapWithCorrectType()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("MARKET_CAP")]);

        var columns = policy.BuildColumns(plan);

        Assert.Contains(columns, c =>
            c.Identifier == "MARKET_CAP" && c.ColumnType == ScannerColumnType.MarketCap);
    }

    private static ScannerQueryPlan MakePlan(
        IReadOnlyCollection<ScannerCondition> conditions,
        string language = "en") =>
        new(Guid.NewGuid(), "test", language, conditions, [], false, null, [], [], DateTimeOffset.UtcNow, "v1");

    private static ScannerQueryPlan MakePlanWithRequested(
        IReadOnlyCollection<ScannerCondition> conditions,
        IEnumerable<string> requested) =>
        new(Guid.NewGuid(), "test", "en", conditions,
            requested.Select(r => new ScannerColumnRequest(r, IsUserRequested: true)).ToList(),
            false, null, [], [], DateTimeOffset.UtcNow, "v1");

    private static ScannerCondition MakeCondition(string code) =>
        new(
            new ScannerMetricReference(
                code,
                new MetricCode(code),
                new MetricVersion("v1"),
                new CalculationPolicyVersion($"{code}_v1"),
                FiscalPeriodType.TrailingTwelveMonths,
                null),
            ConditionOperator.LessThan,
            6m,
            FilterOrigin.Explicit);
}

public sealed class ScannerResultRankerTests
{
    [Fact]
    public void Rank_LessThanCondition_OrdersLowestValueFirst()
    {
        var ranker = new ScannerResultRanker();
        var plan = MakePlan([MakeCondition("PE_TTM", ConditionOperator.LessThan, 10m)]);

        var rows = new[]
        {
            MakeRow("BBB", "PE_TTM", 8m),
            MakeRow("AAA", "PE_TTM", 3m),
            MakeRow("CCC", "PE_TTM", 6m)
        };

        var ranked = ranker.Rank(rows, plan);

        // Lowest P/E → highest score (furthest below threshold)
        Assert.Equal("AAA", ranked.First().SymbolCode);
        Assert.Equal("CCC", ranked.ElementAt(1).SymbolCode);
        Assert.Equal("BBB", ranked.Last().SymbolCode);
    }

    [Fact]
    public void Rank_GreaterThanCondition_OrdersHighestValueFirst()
    {
        var ranker = new ScannerResultRanker();
        var plan = MakePlan([MakeCondition("NET_PROFIT_GROWTH_YOY", ConditionOperator.GreaterThan, 20m)]);

        var rows = new[]
        {
            MakeRow("LOW", "NET_PROFIT_GROWTH_YOY", 25m),
            MakeRow("HIGH", "NET_PROFIT_GROWTH_YOY", 80m),
            MakeRow("MED", "NET_PROFIT_GROWTH_YOY", 50m)
        };

        var ranked = ranker.Rank(rows, plan);

        Assert.Equal("HIGH", ranked.First().SymbolCode);
        Assert.Equal("MED", ranked.ElementAt(1).SymbolCode);
        Assert.Equal("LOW", ranked.Last().SymbolCode);
    }

    [Fact]
    public void Rank_MissingMetricValue_RowStillIncluded_WithZeroScore()
    {
        var ranker = new ScannerResultRanker();
        var plan = MakePlan([MakeCondition("PE_TTM", ConditionOperator.LessThan, 10m)]);

        var rows = new[]
        {
            MakeRow("WITH_DATA", "PE_TTM", 3m),
            MakeRowEmpty("NO_DATA")
        };

        var ranked = ranker.Rank(rows, plan);

        Assert.Equal(2, ranked.Count);
        Assert.Equal("WITH_DATA", ranked.First().SymbolCode);
        Assert.Equal("NO_DATA", ranked.Last().SymbolCode);
        Assert.Equal(0.0, ranked.Last().Score);
    }

    private static ScannerQueryPlan MakePlan(IReadOnlyCollection<ScannerCondition> conditions) =>
        new(Guid.NewGuid(), "test", "en", conditions, [], false, null, [], [], DateTimeOffset.UtcNow, "v1");

    private static ScannerCondition MakeCondition(string code, ConditionOperator op, decimal threshold) =>
        new(
            new ScannerMetricReference(
                code, new MetricCode(code), new MetricVersion("v1"),
                new CalculationPolicyVersion($"{code}_v1"),
                FiscalPeriodType.TrailingTwelveMonths, null),
            op, threshold, FilterOrigin.Explicit);

    private static ScannerTableRow MakeRow(string symbol, string metricCode, decimal value) =>
        new(symbol, null,
            new Dictionary<string, ScannerTableCell>
            {
                [metricCode] = new(value, value.ToString("N2"), CellFreshnessStatus.Persisted, null)
            },
            0.0, []);

    private static ScannerTableRow MakeRowEmpty(string symbol) =>
        new(symbol, null, new Dictionary<string, ScannerTableCell>(), 0.0, []);
}

// --- Bug 2 regression: valuation ratio zero-value exclusion ---

public sealed class ScannerPassesConditionTests
{
    // Valuation ratio: zero must be excluded from LessThan/LessThanOrEqual
    [Fact]
    public void PassesCondition_PeTtmZero_LessThan_IsExcluded()
    {
        Assert.False(EfCoreScannerExecutionService.PassesCondition(0m, ConditionOperator.LessThan, 4m, isValuationRatio: true));
    }

    [Fact]
    public void PassesCondition_PsTtmZero_LessThan_IsExcluded()
    {
        Assert.False(EfCoreScannerExecutionService.PassesCondition(0m, ConditionOperator.LessThan, 2m, isValuationRatio: true));
    }

    [Fact]
    public void PassesCondition_PeTtmZero_LessThanOrEqual_IsExcluded()
    {
        Assert.False(EfCoreScannerExecutionService.PassesCondition(0m, ConditionOperator.LessThanOrEqual, 5m, isValuationRatio: true));
    }

    // Valuation ratio: valid non-zero values below threshold must pass
    [Fact]
    public void PassesCondition_PeTtm_ValidValue_LessThan_IsIncluded()
    {
        Assert.True(EfCoreScannerExecutionService.PassesCondition(3.8m, ConditionOperator.LessThan, 4m, isValuationRatio: true));
    }

    [Fact]
    public void PassesCondition_PsTtm_ValidValue_LessThan_IsIncluded()
    {
        Assert.True(EfCoreScannerExecutionService.PassesCondition(0.7m, ConditionOperator.LessThan, 2m, isValuationRatio: true));
    }

    // Valuation ratio: zero must NOT be blocked for GreaterThan (zero exclusion only for < screens)
    [Fact]
    public void PassesCondition_ValuationRatioZero_GreaterThan_IsNotBlocked()
    {
        // PE_TTM=0 with operator GreaterThan threshold 0 → 0 > 0 is false (mathematically correct)
        Assert.False(EfCoreScannerExecutionService.PassesCondition(0m, ConditionOperator.GreaterThan, 0m, isValuationRatio: true));
        // PE_TTM=0 with operator GreaterThanOrEqual threshold 0 → 0 >= 0 is true (zero exclusion does not apply)
        Assert.True(EfCoreScannerExecutionService.PassesCondition(0m, ConditionOperator.GreaterThanOrEqual, 0m, isValuationRatio: true));
    }

    // Non-ratio metrics: zero must not be accidentally blocked
    [Fact]
    public void PassesCondition_NonRatioMetric_ZeroValue_LessThan_IsIncluded()
    {
        // Net profit = 0 with LessThan threshold 100 → should pass (zero is a valid net profit)
        Assert.True(EfCoreScannerExecutionService.PassesCondition(0m, ConditionOperator.LessThan, 100m, isValuationRatio: false));
    }

    [Fact]
    public void PassesCondition_NonRatioMetric_ZeroValue_LessThanOrEqual_IsIncluded()
    {
        Assert.True(EfCoreScannerExecutionService.PassesCondition(0m, ConditionOperator.LessThanOrEqual, 0m, isValuationRatio: false));
    }
}

public sealed class ScannerIsValuationRatioMetricTests
{
    private static readonly DateOnly AsOf = new(2026, 5, 27);

    private static EfCoreScannerExecutionService BuildService()
    {
        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []);
        return new EfCoreScannerExecutionService(
            null!,  // dbContext — not used by IsValuationRatioMetric
            null!,  // columnPolicy
            null!,  // quoteResolver
            null!,  // ranker
            TimeProvider.System,
            registry,
            null!); // feedbackCollector
    }

    [Fact]
    public void IsValuationRatioMetric_PeTtm_ReturnsTrue()
    {
        var service = BuildService();
        Assert.True(service.IsValuationRatioMetric(new MetricCode("PE_TTM"), AsOf));
    }

    [Fact]
    public void IsValuationRatioMetric_PsTtm_ReturnsTrue()
    {
        var service = BuildService();
        Assert.True(service.IsValuationRatioMetric(new MetricCode("PS_TTM"), AsOf));
    }

    [Fact]
    public void IsValuationRatioMetric_NetProfitGrowth_ReturnsFalse()
    {
        var service = BuildService();
        Assert.False(service.IsValuationRatioMetric(new MetricCode("NET_PROFIT_GROWTH_YOY"), AsOf));
    }

    [Fact]
    public void IsValuationRatioMetric_UnknownCode_ReturnsFalse()
    {
        var service = BuildService();
        Assert.False(service.IsValuationRatioMetric(new MetricCode("MADE_UP_METRIC_XYZ"), AsOf));
    }
}
