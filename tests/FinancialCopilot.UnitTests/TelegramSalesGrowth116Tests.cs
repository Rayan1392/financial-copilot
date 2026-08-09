using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramSalesGrowth116Tests
{
    [Fact]
    public void Sales_growth_rows_render_compact_values_and_governance_footer()
    {
        var columns = new[]
        {
            new ScannerTableColumn("SYMBOL", "SYMBOL", ScannerColumnType.Symbol),
            new ScannerTableColumn("MONTHLY_SALES", "MONTHLY_SALES", ScannerColumnType.Metric, "MONTHLY_SALES"),
            new ScannerTableColumn("MONTHLY_SALES_BASELINE_PREVIOUS_MONTH", "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH", ScannerColumnType.Metric),
            new ScannerTableColumn("MONTHLY_SALES_GROWTH_PERCENT", "MONTHLY_SALES_GROWTH_PERCENT", ScannerColumnType.Metric)
        };
        var currentPeriod = new DateOnly(2026, 6, 1);
        var row = new ScannerTableRow(
            "TEST",
            "Test Company",
            new Dictionary<string, ScannerTableCell>
            {
                ["MONTHLY_SALES"] = new(200m, "200", CellFreshnessStatus.Persisted, null),
                ["MONTHLY_SALES_BASELINE_PREVIOUS_MONTH"] = new(100m, "100", CellFreshnessStatus.Persisted, null),
                ["MONTHLY_SALES_GROWTH_PERCENT"] = new(100m, "100", CellFreshnessStatus.Persisted, null)
            },
            1,
            [],
            SalesGrowthMetadata: new SalesGrowthRowMetadata(
                currentPeriod,
                new DateOnly(2026, 5, 1),
                [],
                "Rial",
                "Raw",
                [],
                DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                "Official filing",
                null,
                ConditionOperator.GreaterThan,
                FilterOrigin.InferredDefault,
                SalesGrowthPolicyVersions.V1,
                "current exceeds baseline"));
        var table = new ScannerTableResult(
            Guid.NewGuid(),
            columns,
            [row],
            new ScannerExecutionFacts(DateTimeOffset.UtcNow, TimeSpan.Zero, 2, 1, false, 1, 1, 2),
            ["one symbol has no baseline"],
            new SalesGrowthTableMetadata(
                currentPeriod,
                1,
                2,
                50,
                SalesGrowthCommonPeriodSelectionStatus.Partial,
                new CalculationPolicyVersion("period-v1"),
                new CalculationPolicyVersion("calculation-v1"),
                false,
                "partial coverage"));
        var response = new AiQueryResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.Scanner,
            null,
            table,
            null,
            null,
            null,
            "sales-growth",
            false,
            null,
            null);

        var renderer = new TelegramAssistantResponseRenderer(
            new TelegramMonthlyTrendChartRenderer(),
            NullLogger<TelegramAssistantResponseRenderer>.Instance);
        var messages = renderer.Render(response, "fa-IR");
        var text = string.Join("\n", messages.Select(message => message.Text)).Replace("\\", string.Empty);

        Assert.Contains("TEST", text);
        Assert.Contains("200", text);
        Assert.Contains("100", text);
        Assert.Contains("Official filing", text);
        Assert.Contains("one symbol has no baseline", text);
        Assert.Contains("پوشش", text);
        Assert.Contains("صفحه", text);
    }
}
