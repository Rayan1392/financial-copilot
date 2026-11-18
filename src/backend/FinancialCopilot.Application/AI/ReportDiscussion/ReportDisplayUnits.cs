using FinancialCopilot.Domain.Financial.CyclicalWavesV2;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// Change «analytical-answer-and-report-score» (D1, OQ-103): the one owner of the amount units a grounded answer may
/// write. Before a report is rendered into a prompt, every monetary amount is converted per value to همت (from 1,000
/// billion toman upward) or میلیارد تومان, so the model only copies numbers and never converts them. The numeric
/// grounding check runs against the same projected values. The stored report is never changed.
/// </summary>
public static class ReportDisplayUnits
{
    public const string Hemat = "hemat";
    public const string BillionToman = "billion_toman";

    /// <summary>The monthly sales payload already carries its amounts in billion toman under this Persian label.</summary>
    public const string BillionTomanLabelFa = "میلیارد تومان";

    private const decimal HematThresholdInBillionToman = 1_000m;

    private static readonly decimal RialsPerBillionToman =
        MonetaryAmountUnits.ScaleFactor(MonetaryScale.Billion) * MonetaryAmountUnits.RialsPerToman;

    public static ReportDataSet Apply(ReportDataSet data) => data with
    {
        Facts = data.Facts.Select(fact => ToDisplay(fact.Value, fact.Unit) is { } display
            ? fact with { Value = display.Value, Unit = display.Unit }
            : fact).ToArray(),
        Calculations = data.Calculations.Select(calculation => ToDisplay(calculation.Value, calculation.Unit) is { } display
            ? calculation with { Value = display.Value, Unit = display.Unit }
            : calculation).ToArray()
    };

    /// <summary>The display value and unit of a monetary amount, or null when the unit is not a monetary amount.</summary>
    public static (decimal Value, string Unit)? ToDisplay(decimal value, string unit)
    {
        if (BillionTomanFactor(unit) is not { } factor) return null;
        var billionToman = value * factor;

        // The unit is chosen on the rounded value, so 999.7 billion toman is shown as 1 همت, not «1000 میلیارد تومان».
        if (Math.Abs(Math.Round(billionToman, 0, MidpointRounding.AwayFromZero)) >= HematThresholdInBillionToman)
            return (Math.Round(billionToman / HematThresholdInBillionToman, 1, MidpointRounding.AwayFromZero), Hemat);

        var magnitude = Math.Abs(billionToman);
        var decimals = magnitude >= 10m ? 0 : magnitude >= 1m ? 1 : 2;
        var rounded = Math.Round(billionToman, decimals, MidpointRounding.AwayFromZero);
        // A small non-zero amount keeps up to four decimals instead of becoming zero.
        if (rounded == 0m && billionToman != 0m)
            rounded = Math.Round(billionToman, 4, MidpointRounding.AwayFromZero);
        return (rounded, BillionToman);
    }

    private static decimal? BillionTomanFactor(string unit) => unit switch
    {
        "million_rial" or "million_irr" => MonetaryAmountUnits.ScaleFactor(MonetaryScale.Million) / RialsPerBillionToman,
        "rial" or "irr" => 1m / RialsPerBillionToman,
        BillionToman or BillionTomanLabelFa => 1m,
        _ => null
    };
}
