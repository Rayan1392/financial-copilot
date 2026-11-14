namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public sealed record CashFlowIncomeStatementValues(decimal? Revenue, decimal? NetProfit, int? NetProfitSourceItemId);
public sealed record CashFlowMetricComponent(string SourceType, int SourceItemId, decimal? Value, string State);
public sealed record CashFlowMetric(string MetricId, string Unit, decimal? Value, string State, string? Reason,
    string Formula, IReadOnlyList<CashFlowMetricComponent> Components);

public static class CashFlowIndustryPolicy
{
    public static bool Supports(string template, string metricId) =>
        metricId is "capex" or "free_cash_flow" or "capex_coverage" or "net_debt_cash_flow" or "cash_conversion" or "ocf_to_revenue"
            ? template == CashFlowLineRegistry.GeneralTemplate
            : template is CashFlowLineRegistry.GeneralTemplate or CashFlowLineRegistry.BankingTemplate;
}

/// <summary>Computes full-precision financial values from the typed cells; never uses a net investing subtotal as CapEx.</summary>
public static class CashFlowMetricsCalculator
{
    public static IReadOnlyList<CashFlowMetric> Calculate(string template, IReadOnlyDictionary<int, CashFlowCell> values,
        CashFlowVerificationResult verification, CashFlowIncomeStatementValues? income = null, bool isYtd = true)
    {
        CashFlowCell Cell(int id) => id == 35 ? verification.ResolvedFx : values.GetValueOrDefault(id) ??
            new(null, "not_reported", null, new([], [], [id]));
        var metrics = new List<CashFlowMetric>();
        var tainted = new HashSet<int>();
        if (verification.Checks.Any(check => check.CheckId == "R1" && check.State != "passed")) tainted.UnionWith([128, 121, 123, 124]);
        if (verification.Checks.Any(check => check.CheckId == "R2" && check.State != "passed")) tainted.UnionWith([33, 124, 35, 125]);
        var quarterUnverified = verification.Status == "unverified" &&
            verification.Checks.Where(check => check.CheckId is "R1" or "R2").All(check => check.State == "passed");

        CashFlowMetric Make(string id, string unit, decimal? amount, string? reason, string formula,
            IReadOnlyList<int> ids, IReadOnlyList<CashFlowMetricComponent>? extra = null)
        {
            var components = ids.Select(item => new CashFlowMetricComponent("cash_flow", item, Cell(item).Value, Cell(item).State))
                .Concat(extra ?? []).ToArray();
            var state = amount is null ? "unavailable" : quarterUnverified || ids.Any(tainted.Contains) ? "unverified" : "available";
            return new(id, unit, amount, state, reason, formula, components);
        }

        void Single(string id, int source)
        {
            var cell = Cell(source);
            metrics.Add(Make(id, "million_irr", cell.Value, cell.Value is null ? cell.Reason ?? "not_reported" : null,
                $"item {source}", [source]));
        }

        void SumMetric(string id, int[] ids, bool verifiedOnly = false)
        {
            var selected = verifiedOnly ? ids.Where(source => CashFlowLineRegistry.Find(source)?.VerifiedFor(template) == true).ToArray() : ids;
            var cells = selected.Select(Cell).ToArray();
            var available = cells.Any(cell => cell.Value.HasValue) && cells.All(cell => cell.State is not ("ambiguous" or "unavailable"));
            metrics.Add(Make(id, "million_irr", available ? cells.Sum(cell => cell.Value ?? 0m) : null,
                available ? null : "component_unavailable", "sum of reported components", selected));
        }

        Single("operating_cash_flow", 128);
        Single("investing_cash_flow", 121);
        Single("financing_cash_flow", 123);
        Single("net_change", 124);
        Single("opening_cash", 33);
        Single("closing_cash", 125);
        Single("fx_effect", 35);
        Single("facilities_received", 23);
        Single("facilities_repaid", 24);
        Single("dividends_paid", 6);

        var opening = Cell(33).Value;
        var netChange = Cell(124).Value;
        var fx = Cell(35).Value;
        metrics.Add(Make("cash_before_fx", "million_irr", opening is not null && netChange is not null ? opening + netChange : null,
            opening is null || netChange is null ? "component_unavailable" : null, "33 + 124", [33, 124]));
        metrics.Add(Make("total_change_in_cash", "million_irr", netChange is not null && fx is not null ? netChange + fx : null,
            netChange is null || fx is null ? "component_unavailable" : null, "124 + 35", [124, 35]));
        SumMetric("interest_paid", [5, 37, 134, 135]);
        SumMetric("capital_transactions", [21, 131]);
        if (Cell(117).Value.HasValue || Cell(119).Value.HasValue) SumMetric("treasury_net", [117, 119]);

        if (CashFlowIndustryPolicy.Supports(template, "capex"))
        {
            var capexIds = new[] { 10, 48, 46 };
            var capexCells = capexIds.Select(Cell).ToArray();
            var capexReason = capexCells[0].Value is null ? "capex_component_missing" :
                capexCells.Any(cell => cell.State is "ambiguous" or "unavailable") ? "component_unavailable" : null;
            decimal? capex = capexReason is null ? -capexCells.Sum(cell => cell.Value ?? 0m) : null;
            metrics.Add(Make("capex", "million_irr", capex, capexReason, "-(10 + 48 + 46)", capexIds));
            var ocf = Cell(128).Value;
            metrics.Add(Make("free_cash_flow", "million_irr", ocf is not null && capex is not null ? ocf - capex : null,
                ocf is null ? "operating_total_missing" : capexReason, "128 - capex", [128, .. capexIds]));
            var coverageReason = capex is null ? capexReason : capex <= 0m ? "zero_capex" :
                ocf is null ? "operating_total_missing" : ocf <= 0m ? "non_positive_operating_cash_flow" : null;
            metrics.Add(Make("capex_coverage", "multiple", coverageReason is null ? ocf / capex : null,
                coverageReason, "128 / capex", [128, .. capexIds]));
            SumMetric("net_debt_cash_flow", [23, 40, 132, 24, 41, 133, 114], verifiedOnly: true);

            var netProfit = isYtd && income?.NetProfitSourceItemId is 143 or 313 ? income?.NetProfit : null;
            var profitReason = netProfit is null ? "income_statement_unavailable" : netProfit <= 0m ? "non_positive_net_profit" :
                ocf is null ? "operating_total_missing" : null;
            var profitComponent = new CashFlowMetricComponent("income_statement", income?.NetProfitSourceItemId ?? 143,
                netProfit, netProfit is null ? "not_reported" : "available");
            metrics.Add(Make("cash_conversion", "multiple", profitReason is null ? ocf / netProfit : null,
                profitReason, "128 / net_profit", [128], [profitComponent]));
            var revenue = isYtd ? income?.Revenue : null;
            var revenueReason = revenue is null ? "income_statement_unavailable" : revenue <= 0m ? "non_positive_revenue" :
                ocf is null ? "operating_total_missing" : null;
            metrics.Add(Make("ocf_to_revenue", "percent", revenueReason is null ? ocf / revenue * 100m : null,
                revenueReason, "128 / revenue * 100", [128], [new("income_statement", 15, revenue, revenue is null ? "not_reported" : "available")]));
        }

        if (template == CashFlowLineRegistry.BankingTemplate || fx is not null and not 0m)
        {
            var shareReason = fx is null || netChange is null ? "component_unavailable" :
                fx < 0m || netChange < 0m || fx + netChange <= 0m ? "mixed_sign_change" : null;
            metrics.Add(Make("fx_share_of_cash_change", "percent", shareReason is null ? fx / (netChange + fx) * 100m : null,
                shareReason, "35 / (124 + 35) * 100", [35, 124]));
        }
        return metrics;
    }
}
