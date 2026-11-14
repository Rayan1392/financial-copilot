using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161ReconciliationTests
{
    private static CashFlowCell Cell(decimal? value, string? state = null) =>
        new(value, state ?? (value.HasValue ? "available" : "not_reported"), null, new([], [], []));

    private static Dictionary<int, CashFlowCell> FixtureA() => new()
    {
        [128] = Cell(6_918_890m), [121] = Cell(-359_104m), [122] = Cell(6_559_786m), [123] = Cell(9_025_064m),
        [124] = Cell(15_584_850m), [33] = Cell(6_692_454m), [35] = Cell(0m), [125] = Cell(22_277_304m)
    };
    private static CashFlowCheck Check(CashFlowVerificationResult result, string id) => Assert.Single(result.Checks, x => x.CheckId == id);

    [Fact]
    public void BothFixtures_ReconcileExactlyAndBankFxRemainsSeparate()
    {
        var a = CashFlowReconciliation.Verify(FixtureA());
        Assert.Equal("verified", a.Status);
        Assert.All(a.Checks.Where(x => x.CheckId is "R1" or "R2" or "V3"), check => Assert.Equal("passed", check.State));
        var b = new Dictionary<int, CashFlowCell>
        {
            [128] = Cell(386_206_543m), [121] = Cell(-43_767_594m), [122] = Cell(342_438_949m), [123] = Cell(-141_868_091m),
            [124] = Cell(200_570_858m), [33] = Cell(2_718_638_069m), [35] = Cell(617_787_300m), [125] = Cell(3_536_996_227m)
        };
        var bank = CashFlowReconciliation.Verify(b);
        Assert.Equal("verified", bank.Status);
        Assert.Equal(617_787_300m, bank.ResolvedFx.Value);
        Assert.Equal(200_570_858m, b[124].Value);
    }

    [Fact]
    public void S1_NetChangeMismatchFailsBothMandatoryChecksExactly()
    {
        var values = FixtureA(); values[124] = Cell(15_584_973m);
        var result = CashFlowReconciliation.Verify(values);
        Assert.Equal("failed", result.Status);
        Assert.Equal(123m, Check(result, "R1").Difference);
        Assert.Equal(-123m, Check(result, "R2").Difference);
    }

    [Fact]
    public void S2_ReportedFxPossiblyEmbedded_IsNotSubtractedFromNetChange()
    {
        var values = FixtureA(); values[35] = Cell(10m);
        var result = CashFlowReconciliation.Verify(values);
        Assert.Equal("failed", result.Status);
        Assert.Contains("fx_possibly_embedded_in_net_change", result.Warnings);
        Assert.Equal(-10m, Check(result, "R2").Difference);
        Assert.Equal(15_584_850m, values[124].Value);
    }

    [Fact]
    public void S3_S4_MissingFxIsInferredOnlyWhenBalancesClose()
    {
        var values = FixtureA(); values.Remove(35);
        var closes = CashFlowReconciliation.Verify(values);
        Assert.Equal("verified", closes.Status);
        Assert.Equal("inferred_zero", closes.ResolvedFx.State);
        Assert.Equal(0m, closes.ResolvedFx.Value);
        values[125] = Cell(22_277_404m);
        var gap = CashFlowReconciliation.Verify(values);
        Assert.Equal("failed", gap.Status);
        Assert.Null(gap.ResolvedFx.Value);
        Assert.Equal(100m, Check(gap, "R2").Difference);
        Assert.Contains("fx_missing_reconciliation_gap", gap.Warnings);
    }

    [Fact]
    public void S6_S7_S15_CrossCheckIsOptionalAndMismatchNeverReplacesOperatingValue()
    {
        var values = FixtureA(); values.Remove(122);
        var absent = CashFlowReconciliation.Verify(values);
        Assert.Equal("verified", absent.Status);
        Assert.Equal("not_evaluated", Check(absent, "V3").State);
        values[122] = Cell(6_559_786m); values[128] = Cell(6_918_891m);
        var wrongOperating = CashFlowReconciliation.Verify(values);
        Assert.Equal("failed", wrongOperating.Status);
        Assert.Equal(-1m, Check(wrongOperating, "V3").Difference);
        values[128] = Cell(6_918_890m); values[122] = Cell(6_559_787m);
        var warning = CashFlowReconciliation.Verify(values);
        Assert.Equal("verified_with_warnings", warning.Status);
        Assert.Contains("ocf_codal_subtotal_mismatch", warning.Warnings);
    }

    [Fact]
    public void AmbiguityAndContinuityDoNotClaimVerified()
    {
        var values = FixtureA(); values[123] = Cell(null, "ambiguous");
        var ambiguous = CashFlowReconciliation.Verify(values);
        Assert.Equal("unverified", ambiguous.Status);
        Assert.Equal("not_evaluated", Check(ambiguous, "R1").State);
        Assert.Contains("duplicate_line_item", ambiguous.Warnings);
        values = FixtureA();
        var c1 = CashFlowReconciliation.Verify(values, previousYearClosing: Cell(6_692_455m));
        Assert.Equal("verified_with_warnings", c1.Status);
        Assert.Contains("opening_balance_discontinuity", c1.Warnings);
        var c2 = CashFlowReconciliation.Verify(values, currentYtdOpening: Cell(10m), predecessorYtdOpening: Cell(11m));
        Assert.Equal("unverified", c2.Status);
        Assert.Equal("failed", Check(c2, "C2").State);
        var unavailableC2 = CashFlowReconciliation.Verify(values, currentYtdOpening: Cell(10m), predecessorYtdOpening: Cell(null));
        Assert.Equal("unverified", unavailableC2.Status);
        Assert.Equal("not_evaluated", Check(unavailableC2, "C2").State);
    }

    [Fact]
    public void QuarterlyC1_UsesFiscalYearOpeningRatherThanQuarterOpening()
    {
        var values = FixtureA();
        var result = CashFlowReconciliation.Verify(values, previousYearClosing: Cell(1000m),
            currentYtdOpening: Cell(1000m), predecessorYtdOpening: Cell(1000m), fiscalYearOpening: Cell(1000m));
        Assert.Equal("passed", Check(result, "C1").State);
        Assert.DoesNotContain("opening_balance_discontinuity", result.Warnings);
    }
}
