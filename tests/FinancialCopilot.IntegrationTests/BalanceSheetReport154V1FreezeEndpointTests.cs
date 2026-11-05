using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

public sealed class BalanceSheetReport154V1ApiFactory : AiFacadeApiFactory;

/// <summary>
/// F154-R-05 (POLICY-V1-FREEZE): the V1 endpoint (ForceV1Orchestration) returns exactly the intents HEAD 573cfd8
/// returned for these phrases (captured on the clean HEAD worktree with the same factory), and never the
/// Feature 154 report.
/// </summary>
public sealed class BalanceSheetReport154V1FreezeEndpointTests(BalanceSheetReport154V1ApiFactory factory)
    : IClassFixture<BalanceSheetReport154V1ApiFactory>
{
    [Theory]
    [InlineData("وضعیت مالی وبملت", "ComprehensiveAnalysis")]
    [InlineData("بیلان فاذر", "Scanner")]
    [InlineData("balance sheet وبملت", "Scanner")]
    [InlineData("عملکرد مالی وبملت", "Scanner")]
    [InlineData("P&L وبملت", "Scanner")]
    [InlineData("تحلیل عملکرد مالی وبملت", "ComprehensiveAnalysis")]
    [InlineData("ترازنامه وبملت", "FinancialStatementTableLookup")]
    [InlineData("صورت سود و زیان وبملت", "FinancialStatementTableLookup")]
    [InlineData("آخرین ترازنامه کگل", "FinancialStatementTableLookup")]
    [InlineData("تحلیل ترازنامه وبملت", "FinancialStatementPeriodAnalysis")]
    public async Task V1Intents_AreUnchangedFromHead(string message, string headIntent)
    {
        factory.EnsureBillingSeeded();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message }, CancellationToken.None);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(headIntent, document.RootElement.GetProperty("intent").GetString());
        if (document.RootElement.TryGetProperty("financialStatementTableResult", out var table) && table.ValueKind == JsonValueKind.Object)
            Assert.Equal(JsonValueKind.Null, table.GetProperty("balanceSheetReport").ValueKind);
    }
}
