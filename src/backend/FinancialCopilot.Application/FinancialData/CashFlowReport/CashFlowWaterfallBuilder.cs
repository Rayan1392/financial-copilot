namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public sealed record CashFlowWaterfallStep(string StepId, string Label, string Kind, decimal Value, decimal From, decimal To);
public sealed record CashFlowWaterfall(string ColumnId, IReadOnlyList<CashFlowWaterfallStep> Steps, IReadOnlyList<CashFlowWaterfallStep> FlowSteps);

public static class CashFlowWaterfallBuilder
{
    public static CashFlowWaterfall? Build(string template, IReadOnlyDictionary<int, CashFlowCell> values, CashFlowVerificationResult verification,
        string columnId = "anchor")
    {
        decimal? Value(int id) => id == 35 ? verification.ResolvedFx.Value : values.GetValueOrDefault(id)?.Value;
        var opening = Value(33); var operating = Value(128); var investing = Value(121); var financing = Value(123);
        var change = Value(124); var fx = Value(35); var closing = Value(125);
        if (opening is null || operating is null || investing is null || financing is null || change is null || fx is null || closing is null) return null;

        var useFx = template == CashFlowLineRegistry.BankingTemplate || fx != 0m;
        static CashFlowWaterfallStep Bar(string id, string label, string kind, decimal amount) => new(id, label, kind, amount, 0m, amount);
        static CashFlowWaterfallStep Delta(string id, string label, decimal amount, ref decimal cursor)
        {
            var start = cursor; cursor += amount;
            return new(id, label, "delta", amount, start, cursor);
        }
        var steps = new List<CashFlowWaterfallStep> { Bar("opening", "مانده ابتدای سال مالی", "start", opening.Value) };
        var position = opening.Value;
        steps.Add(Delta("operating", "فعالیت‌های عملیاتی", operating.Value, ref position));
        steps.Add(Delta("investing", "فعالیت‌های سرمایه‌گذاری", investing.Value, ref position));
        steps.Add(Delta("financing", "فعالیت‌های تأمین مالی", financing.Value, ref position));
        position = opening.Value + change.Value;
        steps.Add(Bar("before_fx", "مانده قبل از اثر نرخ ارز", "subtotal", position));
        if (useFx) steps.Add(Delta("fx", "اثر تغییرات نرخ ارز", fx.Value, ref position));
        steps.Add(Bar("closing", "مانده پایان دوره", "end", closing.Value));

        var flows = new List<CashFlowWaterfallStep>();
        var flowPosition = 0m;
        flows.Add(Delta("operating", "فعالیت‌های عملیاتی", operating.Value, ref flowPosition));
        flows.Add(Delta("investing", "فعالیت‌های سرمایه‌گذاری", investing.Value, ref flowPosition));
        flows.Add(Delta("financing", "فعالیت‌های تأمین مالی", financing.Value, ref flowPosition));
        flowPosition = change.Value;
        flows.Add(Bar("net_change", "خالص افزایش (کاهش) در وجه نقد", "subtotal", flowPosition));
        if (useFx) flows.Add(Delta("fx", "اثر تغییرات نرخ ارز", fx.Value, ref flowPosition));
        flows.Add(Bar("total_change", "تغییر کل موجودی نقد", "end", change.Value + fx.Value));
        return new(columnId, steps, flows);
    }
}
