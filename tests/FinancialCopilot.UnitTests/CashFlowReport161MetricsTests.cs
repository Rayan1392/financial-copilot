using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161MetricsTests
{
    private static CashFlowCell Cell(decimal? value, string? state = null) =>
        new(value, state ?? (value is null ? "not_reported" : "available"), null, new([], [], []));
    private static Dictionary<int, CashFlowCell> General() => new()
    {
        [128] = Cell(6_918_890), [121] = Cell(-359_104), [122] = Cell(6_559_786), [123] = Cell(9_025_064),
        [124] = Cell(15_584_850), [33] = Cell(6_692_454), [35] = Cell(0), [125] = Cell(22_277_304),
        [10] = Cell(-359_535), [23] = Cell(24_635_600), [24] = Cell(-13_908_702), [5] = Cell(-1_457_520), [6] = Cell(-244_314)
    };
    private static IReadOnlyList<CashFlowMetric> Calculate(Dictionary<int, CashFlowCell> values, string template = CashFlowLineRegistry.GeneralTemplate,
        CashFlowIncomeStatementValues? income = null, bool ytd = true) =>
        CashFlowMetricsCalculator.Calculate(template, values, CashFlowReconciliation.Verify(values), income, ytd);
    private static CashFlowMetric Metric(IReadOnlyList<CashFlowMetric> metrics, string id) => Assert.Single(metrics, item => item.MetricId == id);

    [Fact]
    public void FixtureA_ComputesFcfFromCapexNotFromNetInvesting()
    {
        var values = General();
        var metrics = Calculate(values);
        Assert.Equal(359_535m, Metric(metrics, "capex").Value);
        Assert.Equal(6_559_355m, Metric(metrics, "free_cash_flow").Value);
        Assert.Equal(6_918_890m / 359_535m, Metric(metrics, "capex_coverage").Value);
        Assert.Equal(10_726_898m, Metric(metrics, "net_debt_cash_flow").Value);
        Assert.DoesNotContain(Metric(metrics, "free_cash_flow").Components, x => x.SourceItemId == 121);
        Assert.DoesNotContain(Metric(metrics, "capex").Components, x => x.SourceItemId is 13 or 130);
        Assert.Equal(-244_314m, Metric(metrics, "dividends_paid").Value);
    }

    [Fact]
    public void CapexUsesApprovedGrossComponentsAndRequiresItemTen()
    {
        var values = General(); values[48] = Cell(-20); values[46] = Cell(-30); values[130] = Cell(-1000);
        var metrics = Calculate(values);
        Assert.Equal(359_585m, Metric(metrics, "capex").Value);
        values[10] = Cell(0);
        values[48] = Cell(null); values[46] = Cell(null);
        Assert.Equal("zero_capex", Metric(Calculate(values), "capex_coverage").Reason);
        values.Remove(10);
        Assert.Equal("capex_component_missing", Metric(Calculate(values), "capex").Reason);
    }

    [Fact]
    public void NetDebtUsesOnlyVerifiedComponents()
    {
        var values = General(); values[40] = Cell(999m); values[133] = Cell(-200m);
        var netDebt = Metric(Calculate(values), "net_debt_cash_flow");
        Assert.Equal(10_726_898m, netDebt.Value);
        Assert.Equal([23, 24], netDebt.Components.Select(x => x.SourceItemId));
    }

    [Fact]
    public void ProfitAndRevenueRatiosUseOnlyAlignedIncomeAndValidDenominators()
    {
        var values = General();
        var valid = Calculate(values, income: new(10_000_000m, 2_000_000m, 143));
        Assert.Equal(6_918_890m / 2_000_000m, Metric(valid, "cash_conversion").Value);
        Assert.Equal(6_918_890m / 10_000_000m * 100m, Metric(valid, "ocf_to_revenue").Value);
        Assert.Equal("non_positive_net_profit", Metric(Calculate(values, income: new(1m, -1000m, 143)), "cash_conversion").Reason);
        Assert.Equal("non_positive_revenue", Metric(Calculate(values, income: new(0m, 1m, 313)), "ocf_to_revenue").Reason);
        Assert.Equal("income_statement_unavailable", Metric(Calculate(values, income: new(1m, 1m, 144)), "cash_conversion").Reason);
        Assert.Equal("income_statement_unavailable", Metric(Calculate(values, income: new(1m, 1m, 143), ytd: false), "cash_conversion").Reason);
    }

    [Fact]
    public void BankExcludesIndustrialRatiosAndCalculatesFxShare()
    {
        var values = new Dictionary<int, CashFlowCell>
        {
            [128] = Cell(386_206_543m), [121] = Cell(-43_767_594m), [122] = Cell(342_438_949m), [123] = Cell(-141_868_091m),
            [124] = Cell(200_570_858m), [33] = Cell(2_718_638_069m), [35] = Cell(617_787_300m), [125] = Cell(3_536_996_227m)
        };
        var metrics = Calculate(values, CashFlowLineRegistry.BankingTemplate);
        foreach (var id in new[] { "capex", "free_cash_flow", "capex_coverage", "net_debt_cash_flow", "cash_conversion", "ocf_to_revenue" })
            Assert.DoesNotContain(metrics, item => item.MetricId == id);
        Assert.Equal(617_787_300m / 818_358_158m * 100m, Metric(metrics, "fx_share_of_cash_change").Value);
        values[35] = Cell(-1m);
        Assert.Equal("mixed_sign_change", Metric(Calculate(values, CashFlowLineRegistry.BankingTemplate), "fx_share_of_cash_change").Reason);
    }

    [Fact]
    public void FailedTotalsMarkDependentMetricsUnverified()
    {
        var values = General(); values[124] = Cell(15_584_973m);
        var metrics = Calculate(values);
        Assert.Equal("unverified", Metric(metrics, "free_cash_flow").State);
        Assert.Equal("available", Metric(metrics, "dividends_paid").State);
    }

    [Fact]
    public void RemainingComponents_UseSignedValuesAndPreserveMissingStates()
    {
        var values = General();
        values[37] = Cell(-10); values[134] = Cell(-20); values[135] = Cell(-30);
        values[21] = Cell(15); values[131] = Cell(5);
        values[117] = Cell(-30); values[119] = Cell(10);
        var metrics = Calculate(values);
        Assert.Equal(-1_457_580m, Metric(metrics, "interest_paid").Value);
        Assert.Equal(20m, Metric(metrics, "capital_transactions").Value);
        Assert.Equal(-20m, Metric(metrics, "treasury_net").Value);
        Assert.Equal(22_277_304m, Metric(metrics, "cash_before_fx").Value);
        Assert.Equal(15_584_850m, Metric(metrics, "total_change_in_cash").Value);
        Assert.Equal(24_635_600m, Metric(metrics, "facilities_received").Value);
        Assert.Equal(-13_908_702m, Metric(metrics, "facilities_repaid").Value);
        values[128] = Cell(0m);
        Assert.Equal("non_positive_operating_cash_flow", Metric(Calculate(values), "capex_coverage").Reason);
        values[48] = Cell(null, "ambiguous");
        Assert.Equal("component_unavailable", Metric(Calculate(values), "capex").Reason);
    }
}
