using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

public sealed class ScannerResultColumnPolicyTests
{
    [Fact]
    public void BuildColumns_NoConditionsOrRequested_ReturnsOnlyDefaultColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([]);

        var columns = policy.BuildColumns(plan);

        Assert.Equal(5, columns.Count);
        Assert.Contains(columns, c => c.ColumnType == ScannerColumnType.Symbol);
        Assert.Contains(columns, c => c.ColumnType == ScannerColumnType.CompanyName);
        Assert.Contains(columns, c => c.ColumnType == ScannerColumnType.LatestPrice);
        Assert.Contains(columns, c => c.ColumnType == ScannerColumnType.DailyChangePercent);
        Assert.Contains(columns, c => c.ColumnType == ScannerColumnType.MarketCap);
    }

    [Fact]
    public void BuildColumns_SingleCondition_AddsConditionMetricColumn()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM")]);

        var columns = policy.BuildColumns(plan);

        Assert.Equal(6, columns.Count);
        var metricCol = Assert.Single(columns, c => c.ColumnType == ScannerColumnType.Metric);
        Assert.Equal("PE_TTM", metricCol.MetricCode);
    }

    [Fact]
    public void BuildColumns_NineConditions_CappsAtTenColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        // 5 default + 9 conditions = 14, but cap at 10
        var conditions = Enumerable.Range(0, 9)
            .Select(i => MakeCondition($"METRIC_{i}"))
            .ToList();
        var plan = MakePlan(conditions);

        var columns = policy.BuildColumns(plan);

        Assert.Equal(ScannerQueryPlan.MaxDisplayColumns, columns.Count);
    }

    [Fact]
    public void BuildColumns_DuplicateConditionAndDefault_DoesNotAddDuplicate()
    {
        var policy = new ScannerResultColumnPolicy();
        // PE_TTM is a condition; MARKET_CAP overlaps with the default column identifier
        var plan = MakePlan([MakeCondition("PE_TTM"), MakeCondition("MARKET_CAP")]);

        var columns = policy.BuildColumns(plan);

        // MARKET_CAP already in defaults so not added again; PE_TTM is new
        Assert.Equal(6, columns.Count);
        Assert.Single(columns, c => c.Identifier.Equals("MARKET_CAP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildColumns_RequestedColumns_AddedAfterConditionColumns()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlanWithRequested([MakeCondition("PE_TTM")], ["NET_PROFIT"]);

        var columns = policy.BuildColumns(plan);

        Assert.Equal(7, columns.Count);
        Assert.Contains(columns, c => c.Identifier == "PE_TTM");
        Assert.Contains(columns, c => c.Identifier == "NET_PROFIT");
    }

    [Fact]
    public void BuildColumns_PersianPlan_LocalizesDefaultAndMetricLabels()
    {
        var policy = new ScannerResultColumnPolicy();
        var plan = MakePlan([MakeCondition("PE_TTM")], language: "fa");

        var columns = policy.BuildColumns(plan);

        Assert.Contains(columns, c => c.Identifier == "SYMBOL" && c.DisplayName == "نماد");
        Assert.Contains(columns, c => c.Identifier == "COMPANY" && c.DisplayName == "شرکت");
        Assert.Contains(columns, c => c.Identifier == "LATEST_PRICE" && c.DisplayName == "آخرین قیمت");
        Assert.Contains(columns, c => c.Identifier == "DAILY_CHANGE_PCT" && c.DisplayName == "تغییر روزانه %");
        Assert.Contains(columns, c => c.Identifier == "MARKET_CAP" && c.DisplayName == "ارزش بازار");
        Assert.Contains(columns, c => c.Identifier == "PE_TTM" && c.DisplayName == "P/E دوازده‌ماهه");
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
