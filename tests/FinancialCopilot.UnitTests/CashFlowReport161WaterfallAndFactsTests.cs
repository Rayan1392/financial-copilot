using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161WaterfallAndFactsTests
{
    private static CashFlowCell Cell(decimal value) => new(value, "available", null, new([], [], []));
    private static Dictionary<int, CashFlowCell> General() => new()
    {
        [128] = Cell(6_918_890), [121] = Cell(-359_104), [122] = Cell(6_559_786), [123] = Cell(9_025_064),
        [124] = Cell(15_584_850), [33] = Cell(6_692_454), [35] = Cell(0), [125] = Cell(22_277_304), [10] = Cell(-359_535)
    };
    private static Dictionary<int, CashFlowCell> Bank() => new()
    {
        [128] = Cell(386_206_543), [121] = Cell(-43_767_594), [122] = Cell(342_438_949), [123] = Cell(-141_868_091),
        [124] = Cell(200_570_858), [33] = Cell(2_718_638_069), [35] = Cell(617_787_300), [125] = Cell(3_536_996_227)
    };

    [Fact]
    public void FixtureWaterfalls_UseBackendRunningValuesAndReportedClosing()
    {
        var a = General();
        var result = CashFlowWaterfallBuilder.Build(CashFlowLineRegistry.GeneralTemplate, a, CashFlowReconciliation.Verify(a))!;
        Assert.Equal([6_692_454m, 13_611_344m, 13_252_240m, 22_277_304m, 22_277_304m, 22_277_304m], result.Steps.Select(x => x.To));
        Assert.Equal(15_584_850m, result.FlowSteps[^1].Value);
        Assert.DoesNotContain(result.Steps, x => x.StepId == "fx");
        var b = Bank();
        var bank = CashFlowWaterfallBuilder.Build(CashFlowLineRegistry.BankingTemplate, b, CashFlowReconciliation.Verify(b))!;
        Assert.Equal([2_718_638_069m, 3_104_844_612m, 3_061_077_018m, 2_919_208_927m, 2_919_208_927m, 3_536_996_227m, 3_536_996_227m], bank.Steps.Select(x => x.To));
        Assert.Equal(818_358_158m, bank.FlowSteps[^1].Value);
        b[125] = Cell(3_536_996_228m);
        var mismatch = CashFlowWaterfallBuilder.Build(CashFlowLineRegistry.BankingTemplate, b, CashFlowReconciliation.Verify(b))!;
        Assert.Equal(3_536_996_228m, mismatch.Steps[^1].To);
        Assert.Equal("failed", CashFlowReconciliation.Verify(b).Status);
    }

    [Fact]
    public void FactsAreDeterministicAndNeverContainVerdictWords()
    {
        var values = General();
        var metrics = CashFlowMetricsCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate, values,
            CashFlowReconciliation.Verify(values), new(10_000_000, -1000, 143));
        var facts = CashFlowAnalysisFacts.Build("earnings_quality", "anchor", CashFlowLineRegistry.GeneralTemplate, metrics);
        Assert.StartsWith("جریان خالص نقد عملیاتی: ", facts.Facts[0].Text);
        Assert.DoesNotContain("میلیون ریال", facts.Facts[0].Text);
        Assert.Equal("سود خالص: (۱٬۰۰۰).", facts.Facts[1].Text);
        Assert.Contains("سود خالص غیرمثبت", facts.Facts[2].Text);
        foreach (var topic in new[] { "overview", "earnings_quality", "operating_generation", "cash_change_drivers", "financing_dependence", "financing_structure" })
        {
            var rendered = string.Join(" ", CashFlowAnalysisFacts.Build(topic, "anchor", CashFlowLineRegistry.GeneralTemplate, metrics).Facts.Select(f => f.Text));
            foreach (var word in new[] { "قوی", "ضعیف", "مناسب", "نامناسب" }) Assert.DoesNotContain(word, rendered);
        }
    }

    [Fact]
    public void ComparisonFactsRequireVerifiedAlignedMetricsAndCarryTheComparedPeriod()
    {
        var currentValues = General();
        var priorValues = General();
        priorValues[128] = Cell(5_000_000);
        priorValues[122] = Cell(4_640_896);
        priorValues[124] = Cell(13_665_960);
        priorValues[125] = Cell(20_358_414);
        var currentMetrics = CashFlowMetricsCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate, currentValues,
            CashFlowReconciliation.Verify(currentValues));
        var priorMetrics = CashFlowMetricsCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate, priorValues,
            CashFlowReconciliation.Verify(priorValues));

        var facts = CashFlowAnalysisFacts.Build("overview", "current_q", CashFlowLineRegistry.GeneralTemplate,
            currentMetrics, previousQuarter: new(priorMetrics, "prior_q", "فصل قبل"));

        var comparison = Assert.Single(facts.Facts, fact => fact.FactId == "operating_cash_flow_previous_quarter");
        Assert.Contains("فصل قبل", comparison.Text);
        Assert.Contains("افزایش داشته است", comparison.Text);
        Assert.Equal(["prior_q"], comparison.RelatedColumnIds);

        currentValues[124] = Cell(currentValues[124].Value!.Value + 1m);
        var unverified = CashFlowMetricsCalculator.Calculate(CashFlowLineRegistry.GeneralTemplate, currentValues,
            CashFlowReconciliation.Verify(currentValues));
        var unverifiedFacts = CashFlowAnalysisFacts.Build("overview", "current_q", CashFlowLineRegistry.GeneralTemplate,
            unverified, previousQuarter: new(priorMetrics, "prior_q", "فصل قبل"));
        Assert.DoesNotContain(unverifiedFacts.Facts, fact => fact.FactId == "operating_cash_flow_previous_quarter");
    }

    [Fact]
    public void MissingFxGap_DoesNotInventWaterfallValues()
    {
        var values = General(); values.Remove(35); values[125] = Cell(22_277_404);
        Assert.Null(CashFlowWaterfallBuilder.Build(CashFlowLineRegistry.GeneralTemplate, values, CashFlowReconciliation.Verify(values)));
    }
}
