namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public sealed record CashFlowCheck(string CheckId, string State, decimal? Expected, decimal? Actual, decimal? Difference);
public sealed record CashFlowVerificationResult(string Status, IReadOnlyList<CashFlowCheck> Checks,
    IReadOnlyList<string> Warnings, CashFlowCell ResolvedFx);

/// <summary>Exact checks on published values; net change is never adjusted for FX.</summary>
public static class CashFlowReconciliation
{
    public static CashFlowVerificationResult Verify(IReadOnlyDictionary<int, CashFlowCell> values,
        CashFlowCell? previousYearClosing = null, CashFlowCell? currentYtdOpening = null,
        CashFlowCell? predecessorYtdOpening = null, IReadOnlyCollection<string>? existingWarnings = null,
        CashFlowCell? fiscalYearOpening = null)
    {
        var warnings = new List<string>(existingWarnings ?? []);
        if (values.Values.Any(cell => cell.State == "ambiguous") && !warnings.Contains("duplicate_line_item"))
            warnings.Add("duplicate_line_item");
        CashFlowCell Get(int item) => values.TryGetValue(item, out var cell) ? cell :
            new(null, "not_reported", null, new([], [], [item]));
        var operating = Get(128);
        var investing = Get(121);
        var beforeFinancing = Get(122);
        var financing = Get(123);
        var change = Get(124);
        var opening = Get(33);
        var fx = Get(35);
        var closing = Get(125);
        var checks = new List<CashFlowCheck>();
        checks.Add(Check("R1", Sum(operating.Value, investing.Value, financing.Value), change.Value));

        var beforeFx = Sum(opening.Value, change.Value);
        var resolvedFx = fx;
        CashFlowCheck r2;
        if (beforeFx is null || closing.Value is null || fx.State is "ambiguous" or "unavailable")
        {
            r2 = Check("R2", null, closing.Value);
        }
        else if (fx.Value is { } reportedFx)
        {
            if (reportedFx != 0m && beforeFx == closing.Value) warnings.Add("fx_possibly_embedded_in_net_change");
            r2 = Check("R2", beforeFx.Value + reportedFx, closing.Value);
        }
        else if (beforeFx == closing.Value)
        {
            resolvedFx = fx with { Value = 0m, State = "inferred_zero", Reason = "inferred_zero_by_reconciliation",
                Provenance = fx.Provenance with { Formula = "opening + net_change = closing" } };
            r2 = Check("R2", beforeFx, closing.Value);
        }
        else
        {
            warnings.Add("fx_missing_reconciliation_gap");
            r2 = Check("R2", beforeFx, closing.Value);
        }
        checks.Add(r2);
        var v3 = Check("V3", Sum(operating.Value, investing.Value), beforeFinancing.Value);
        checks.Add(v3);
        if (v3.State == "failed") warnings.Add("ocf_codal_subtotal_mismatch");

        var continuity = Check("C1", previousYearClosing?.Value, (fiscalYearOpening ?? opening).Value);
        checks.Add(continuity);
        if (continuity.State == "failed") warnings.Add("opening_balance_discontinuity");
        var quarterlyContinuity = Check("C2", predecessorYtdOpening?.Value, currentYtdOpening?.Value);
        checks.Add(quarterlyContinuity);
        if (quarterlyContinuity.State == "failed") warnings.Add("quarter_opening_discontinuity");

        var mandatory = checks.Where(check => check.CheckId is "R1" or "R2").ToArray();
        var quarterlyContinuityRequired = currentYtdOpening is not null || predecessorYtdOpening is not null;
        var status = mandatory.Any(check => check.State == "failed") ? "failed"
            : mandatory.Any(check => check.State == "not_evaluated") || quarterlyContinuityRequired && quarterlyContinuity.State != "passed" ? "unverified"
            : warnings.Count != 0 ? "verified_with_warnings" : "verified";
        return new(status, checks, warnings.Distinct().ToArray(), resolvedFx);
    }

    private static decimal? Sum(params decimal?[] values) => values.All(value => value.HasValue) ? values.Sum(value => value!.Value) : null;
    private static CashFlowCheck Check(string id, decimal? expected, decimal? actual) =>
        expected is null || actual is null ? new(id, "not_evaluated", expected, actual, null) :
        new(id, expected == actual ? "passed" : "failed", expected, actual, actual - expected);
}
