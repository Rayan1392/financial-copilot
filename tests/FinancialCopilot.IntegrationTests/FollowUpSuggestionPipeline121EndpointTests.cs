using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 121 change «follow-up-suggestion-pipeline» through the real V2 endpoint: the Feature 154 balance-sheet
/// report («ترازنامه X», «صورت وضعیت X») now ends with related follow-ups, and answers that had follow-ups before keep
/// exactly the same ones (ids and texts recorded on the pre-pipeline code, commit ce1ff2e, 2026-11-11).
/// </summary>
public sealed class FollowUpSuggestionPipeline121EndpointTests : IClassFixture<BalanceSheetReport154ApiFactory>
{
    private readonly BalanceSheetReport154ApiFactory _factory;

    public FollowUpSuggestionPipeline121EndpointTests(BalanceSheetReport154ApiFactory factory)
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

    private static string[] Messages(JsonElement root) =>
        root.GetProperty("suggestedActions").EnumerateArray().Select(action => action.GetProperty("message").GetString()!).ToArray();

    [Theory]
    [InlineData("ترازنامه وبملت", "وبملت")]
    [InlineData("صورت وضعیت فاذر", "فاذر")]
    [InlineData("ترازنامه فاذر را بده", "فاذر")]
    public async Task BalanceSheetReport_EndsWithRelatedFollowUps(string question, string symbol)
    {
        var root = await Ask(question);

        Assert.Equal("available", root.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport").GetProperty("outcome").GetString());
        Assert.Equal([$"گزارش فصلی {symbol}", $"روند فروش ماهانه {symbol} را نشان بده"], Messages(root));
        Assert.All(root.GetProperty("suggestedActions").EnumerateArray(),
            action => Assert.Equal("balance_sheet_report_follow_up", action.GetProperty("relevanceReason").GetString()));
    }

    // Unchanged: the quarterly report keeps its Feature 144 follow-ups, ids included.
    [Theory]
    [InlineData("گزارش سود و زیان فصلی فولاد را بررسی کن")]
    [InlineData("عملکرد فصلی فولاد را بررسی کن")]
    public async Task QuarterlyReport_KeepsItsFollowUps(string question)
    {
        var root = await Ask(question);

        Assert.Equal(
        [
            "quarterly-report:c711626326e9841507def8668760f519a0ba7eae3dc479f9fdce3bae4d7be93d",
            "quarterly-report:44e42387a8918a09dea4f84048feaed2e5d708b59dbcdb598042521da55c0d25"
        ], root.GetProperty("suggestedActions").EnumerateArray().Select(action => action.GetProperty("id").GetString()));
        Assert.Equal(["روند فروش ماهانه فولاد را نشان بده", "ترکیب فروش محصولات فولاد"], Messages(root));
    }

    // Unchanged: a no-data balance sheet keeps the guidance follow-up.
    [Fact]
    public async Task BalanceSheetWithNoSnapshotAtTheDate_KeepsTheGuidanceFollowUp()
    {
        var root = await Ask("ترازنامه وبملت در ۱۴۰۵/۰۴/۳۱");

        Assert.Equal(["روند فروش وبملت"], Messages(root));
    }

    // Unchanged: the greeting keeps its four sample questions.
    [Fact]
    public async Task Greeting_KeepsItsSampleQuestions()
    {
        var root = await Ask("سلام");

        Assert.Equal(
            ["روند فروش ماهانه کگل را نشان بده", "گزارش فصلی کگل را نشان بده", "ترازنامه کگل را نشان بده", "ترکیب فروش محصولات کگل"],
            Messages(root));
    }
}
