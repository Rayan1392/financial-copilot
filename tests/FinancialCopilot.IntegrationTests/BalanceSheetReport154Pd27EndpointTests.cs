using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// PD-27 (Product Owner instruction, 2026-11-05) through the normal V2 endpoint: the balance-sheet phrasings and the
/// new starter card open the Feature 154 report for فاذر; the renamed income card behaves like the old card; explicit
/// «تحلیل» requests and bare «وضعیت» keep their routes.
/// </summary>
public sealed class BalanceSheetReport154Pd27EndpointTests : IClassFixture<BalanceSheetReport154ApiFactory>
{
    private readonly BalanceSheetReport154ApiFactory _factory;

    public BalanceSheetReport154Pd27EndpointTests(BalanceSheetReport154ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    private async Task<JsonElement> Ask(string message)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static JsonElement? Table(JsonElement root) =>
        root.TryGetProperty("financialStatementTableResult", out var table) && table.ValueKind == JsonValueKind.Object ? table : null;

    private static bool Has(JsonElement? table, string property) =>
        table is { } value && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Object;

    [Theory] // AC-R13
    [InlineData("صورت وضعیت مالی فاذر را بررسی کن")]
    [InlineData("صورت وضعیت فاذر")]
    [InlineData("ترازنامه فاذر را بده")]
    [InlineData("ترازنامه فاذر؟")]
    public async Task BalanceSheetPhrasings_OpenTheFeature154ReportForFazar(string message)
    {
        var root = await Ask(message);
        Assert.Equal("financial_statement_table", root.GetProperty("semanticCapabilityCode").GetString());
        var report = Table(root)!.Value.GetProperty("balanceSheetReport");
        Assert.Equal(JsonValueKind.Object, report.ValueKind);
        Assert.Equal("available", report.GetProperty("outcome").GetString());
        Assert.Equal("GeneralBalanceSheet", report.GetProperty("templateId").GetString());
        Assert.Equal("default", report.GetProperty("requestedView").GetString());
        Assert.Equal("فاذر", report.GetProperty("company").GetProperty("symbol").GetString());
        Assert.False(Has(Table(root), "quarterlyResult"));
        Assert.Contains("فاذر", root.GetProperty("textAnswer").GetString());
    }

    [Fact] // AC-R14
    public async Task RenamedIncomeCard_ReturnsWhatTheOldCardReturns()
    {
        var oldCard = await Ask("گزارش سود و زیان فصلی فولاد را بررسی کن");
        var newCard = await Ask("عملکرد مالی فصلی فولاد را بررسی کن");
        foreach (var property in new[] { "intent", "semanticCapabilityCode" })
            Assert.Equal(oldCard.GetProperty(property).ToString(), newCard.GetProperty(property).ToString());
        Assert.True(Has(Table(oldCard), "quarterlyResult"));
        Assert.True(Has(Table(newCard), "quarterlyResult"));
        Assert.False(Has(Table(newCard), "balanceSheetReport"));
        Assert.Equal(Table(oldCard)!.Value.GetProperty("quarterlyResult").GetProperty("companySymbol").ToString(),
            Table(newCard)!.Value.GetProperty("quarterlyResult").GetProperty("companySymbol").ToString());
    }

    [Fact] // AC-R14 (owner 2026-11-05): «عملکرد فصلی X را بررسی کن» returns what «عملکرد مالی فصلی X را بررسی کن» returns.
    public async Task QuarterlyPerformanceShorthand_ReturnsTheQuarterlyReport()
    {
        var full = await Ask("عملکرد مالی فصلی فولاد را بررسی کن");
        var shorthand = await Ask("عملکرد فصلی فولاد را بررسی کن");
        foreach (var property in new[] { "intent", "semanticCapabilityCode" })
            Assert.Equal(full.GetProperty(property).ToString(), shorthand.GetProperty(property).ToString());
        Assert.True(Has(Table(shorthand), "quarterlyResult"));
        Assert.False(Has(Table(shorthand), "balanceSheetReport"));
        Assert.Equal(Table(full)!.Value.GetProperty("quarterlyResult").GetProperty("companySymbol").ToString(),
            Table(shorthand)!.Value.GetProperty("quarterlyResult").GetProperty("companySymbol").ToString());
    }

    [Theory] // AC-R13/AC-R14 boundaries: explicit «تحلیل» keeps analysis; bare «وضعیت» keeps its route.
    [InlineData("تحلیل ترازنامه فاذر", "financial_statement_period_analysis")]
    [InlineData("تحلیل صورت سود و زیان فولاد", "financial_statement_period_analysis")]
    [InlineData("وضعیت فاذر", "comprehensive_analysis")]
    public async Task AnalysisAndBareStatus_KeepTheirRoutes(string message, string capability)
    {
        var root = await Ask(message);
        Assert.Equal(capability, root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(Has(Table(root), "balanceSheetReport"));
        Assert.False(Has(Table(root), "quarterlyResult"));
    }
}
