using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class MonthlyProductComparison129TelegramTests
{
    [Fact]
    public void Renderer_UsesTypedValues_BoundsRows_AndPreservesNulls()
    {
        var period = new JalaliPeriod(1403, 2);
        var products = Enumerable.Range(0, 25)
            .Select(i => new ProductComparisonItem(
                $"Product {i}", "ton", $"C:{i}", ProductIdentityState.Code, ProductLifecycle.Continuing,
                new ProductPeriodValues(i == 0 ? -10m : 1m, null, null, null, "ton"),
                new ProductPeriodValues(0m, null, null, null, "ton"), i == 0 ? -10m : null, null, null, null, null,
                ProductDriver.Unclassified, ProductionSalesSignal.Unavailable, null, [], []))
            .ToArray();
        var comparison = new MonthlyProductComparisonResponse(
            MonthlyProductComparisonState.Partial, "شغدیر", "external", period, new JalaliPeriod(1403, 1),
            new CompanySalesTotals(0m, 0m, 0m, null), ProductDriver.Unclassified, products[0], null,
            products, [MonthlyProductComparisonWarning.UnitChanged], []);
        var response = new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.MonthlyProductComparison,
            null, null, null, null, null, null, false, null, null, MonthlyProductComparisonResult: comparison);

        var messages = new TelegramAssistantResponseRenderer(
            new ThrowingChartRenderer(), NullLogger<TelegramAssistantResponseRenderer>.Instance).Render(response, "fa-IR");
        var text = string.Join("\n", messages.Select(x => x.Text));

        Assert.Contains("شغدیر", text);
        Assert.Contains("1403/02", text);
        Assert.Contains("-10", text);
        Assert.DoesNotContain("25", text); // renderer bound is 20 rows
        Assert.Contains(nameof(MonthlyProductComparisonWarning.UnitChanged), text);
        Assert.DoesNotContain("Exception", text);
    }

    private sealed class ThrowingChartRenderer : ITelegramMonthlyTrendChartRenderer
    {
        public TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse response) =>
            throw new InvalidOperationException("must not be called");
    }
}
