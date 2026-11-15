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
        if (verification.Checks.Any(check => check.CheckId == "R1" && check.State != "passed")) tainted.UnionWith([122, 121, 123, 124]);
        if (verification.Checks.Any(check => check.CheckId == "V3" && check.State == "failed")) tainted.UnionWith([122, 121]);
        if (verification.Checks.Any(check => check.CheckId == "R2" && check.State != "passed")) tainted.UnionWith([33, 124, 35, 125]);
        var quarterUnverified = verification.Status == "unverified" &&
            verification.Checks.Where(check => check.CheckId is "R1" or "R2").All(check => check.State == "passed");

        CashFlowMetric Make(string id, string unit, decimal? amount, string? reason, string formula,
            IReadOnlyList<int> ids, IReadOnlyList<CashFlowMetricComponent>? extra = null,
            IReadOnlySet<int>? mappingUnverifiedIds = null)
        {
            var components = ids.Select(item => new CashFlowMetricComponent("cash_flow", item, Cell(item).Value,
                    mappingUnverifiedIds?.Contains(item) == true ? "mapping_unverified" : Cell(item).State))
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
            var selected = verifiedOnly ? ids.Where(source => CashFlowLineRegistry.Find(source) is { } line &&
                line.AppliesTo(template) && line.VerifiedFor(template)).ToArray() : ids;
            if (verifiedOnly && selected.Length == 0) return;
            var cells = selected.Select(Cell).ToArray();
            var available = cells.Length > 0 && cells.All(cell => cell.Value.HasValue && cell.State is not ("ambiguous" or "unavailable"));
            metrics.Add(Make(id, "million_irr", available ? cells.Sum(cell => cell.Value ?? 0m) : null,
                available ? null : "component_unavailable", "sum of reported components", selected));
        }

        var operatingComponents = new[] { Cell(122), Cell(121) };
        var operatingValue = operatingComponents.All(cell => cell.Value.HasValue)
            ? operatingComponents[0].Value!.Value - operatingComponents[1].Value!.Value : (decimal?)null;
        metrics.Add(Make("operating_cash_flow", "million_irr", operatingValue,
            operatingValue is null ? "operating_total_missing" : null, "122 - 121", [122, 121]));
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
        SumMetric("interest_paid", [5, 37, 134, 135], verifiedOnly: true);
        SumMetric("capital_transactions", [21, 131], verifiedOnly: true);
        if (template == CashFlowLineRegistry.BankingTemplate && (Cell(117).Value.HasValue || Cell(119).Value.HasValue))
            SumMetric("treasury_net", [117, 119], verifiedOnly: true);

        if (CashFlowIndustryPolicy.Supports(template, "capex"))
        {
            var ppe = Cell(10);
            var ppeCapex = ppe.Value is < 0m ? -ppe.Value.Value : ppe.Value == 0m ? 0m : (decimal?)null;
            var ppeReason = ppe.State is "ambiguous" or "unavailable" ? "component_unavailable" :
                ppe.Value is null ? "capex_component_missing" : ppe.Value > 0m ? "unexpected_capex_sign" : null;
            if (ppeReason is not null) ppeCapex = null;
            metrics.Add(Make("ppe_capex", "million_irr", ppeCapex, ppeReason,
                "-10; PPE cash payments only", [10]));
            var ocf = operatingValue;
            metrics.Add(Make("simplified_free_cash_flow", "million_irr", ocf is not null && ppeCapex is not null ? ocf - ppeCapex : null,
                ocf is null ? "operating_total_missing" : ppeReason, "(122 - 121) - PPE capex", [122, 121, 10]));
            var ppeCoverageReason = ppeCapex is null ? ppeReason : ppeCapex <= 0m ? "zero_capex" :
                ocf is null ? "operating_total_missing" : ocf <= 0m ? "non_positive_operating_cash_flow" : null;
            metrics.Add(Make("ppe_capex_coverage", "multiple", ppeCoverageReason is null ? ocf / ppeCapex : null,
                ppeCoverageReason, "(122 - 121) / PPE capex", [122, 121, 10]));

            var capexDefinitions = CashFlowLineRegistry.Lines.Where(line => line.Role == "detail" && line.MetricComponent == "capex" &&
                line.AppliesTo(template)).ToArray();
            var capexIds = capexDefinitions.Select(line => line.SourceItemId).ToArray();
            var unverifiedMappings = capexDefinitions.Where(line => !line.VerifiedFor(template)).Select(line => line.SourceItemId).ToHashSet();
            var capexCells = capexIds.Select(Cell).ToArray();
            var comprehensiveReason = unverifiedMappings.Count > 0 ? "capex_mapping_unverified" :
                capexCells.Any(cell => cell.Value is null || cell.State is "ambiguous" or "unavailable") ? "capex_component_missing" : null;
            decimal? comprehensiveCapex = comprehensiveReason is null ? -capexCells.Sum(cell => cell.Value!.Value) : null;
            metrics.Add(Make("capex", "million_irr", comprehensiveCapex, comprehensiveReason,
                $"-({string.Join(" + ", capexIds)})", capexIds, mappingUnverifiedIds: unverifiedMappings));
            metrics.Add(Make("free_cash_flow", "million_irr", ocf is not null && comprehensiveCapex is not null ? ocf - comprehensiveCapex : null,
                ocf is null ? "operating_total_missing" : comprehensiveReason,
                "(122 - 121) - comprehensive capex", [122, 121, .. capexIds], mappingUnverifiedIds: unverifiedMappings));
            var coverageReason = comprehensiveCapex is null ? comprehensiveReason : comprehensiveCapex <= 0m ? "zero_capex" :
                ocf is null ? "operating_total_missing" : ocf <= 0m ? "non_positive_operating_cash_flow" : null;
            metrics.Add(Make("capex_coverage", "multiple", coverageReason is null ? ocf / comprehensiveCapex : null,
                coverageReason, "(122 - 121) / comprehensive capex", [122, 121, .. capexIds], mappingUnverifiedIds: unverifiedMappings));
            SumMetric("net_debt_cash_flow", [23, 40, 132, 24, 41, 133, 114], verifiedOnly: true);

            var netProfit = isYtd && income?.NetProfitSourceItemId is 143 or 313 ? income?.NetProfit : null;
            var profitReason = netProfit is null ? "income_statement_unavailable" : netProfit <= 0m ? "non_positive_net_profit" :
                ocf is null ? "operating_total_missing" : null;
            var profitComponent = new CashFlowMetricComponent("income_statement", income?.NetProfitSourceItemId ?? 143,
                netProfit, netProfit is null ? "not_reported" : "available");
            metrics.Add(Make("cash_conversion", "multiple", profitReason is null ? ocf / netProfit : null,
                profitReason, "(122 - 121) / net_profit", [122, 121], [profitComponent]));
            var revenue = isYtd ? income?.Revenue : null;
            var revenueReason = revenue is null ? "income_statement_unavailable" : revenue <= 0m ? "non_positive_revenue" :
                ocf is null ? "operating_total_missing" : null;
            metrics.Add(Make("ocf_to_revenue", "percent", revenueReason is null ? ocf / revenue * 100m : null,
                revenueReason, "(122 - 121) / revenue * 100", [122, 121], [new("income_statement", 15, revenue, revenue is null ? "not_reported" : "available")]));
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
