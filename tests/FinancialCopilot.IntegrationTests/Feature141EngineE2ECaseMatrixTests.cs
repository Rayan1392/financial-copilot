using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Validates the independent input matrix before an isolated engine run is attempted.</summary>
public sealed class Feature141EngineE2ECaseMatrixTests
{
    [Fact]
    public void CaseMatrixHasTwentyDistinctPersianQuestionsAndExplicitExpectations()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "specs", "141-schema-aware-semantic-query-engine", "evaluation");
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "engine-e2e-cases.json")));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "engine-e2e-fixture.json")));
        var items = cases.RootElement.EnumerateArray().ToArray();
        Assert.Equal(20, items.Length);
        Assert.Equal(20, items.Select(item => item.GetProperty("id").GetString()).Distinct().Count());
        Assert.All(items, item =>
        {
            Assert.Contains(item.GetProperty("question").GetString()!, c => c is >= '\u0600' and <= '\u06ff');
            Assert.Equal(JsonValueKind.Object, item.GetProperty("expected").ValueKind);
        });
        var policy = fixture.RootElement.GetProperty("unitPolicy");
        Assert.Equal("MillionIRR", policy.GetProperty("financialStatement").GetString());
        Assert.Equal("IRR", policy.GetProperty("perShare").GetString());
        Assert.Equal("MillionIRR", policy.GetProperty("monthlySalesAmount").GetString());
        Assert.Equal("Ton", policy.GetProperty("productionQuantity").GetString());
        var companies = fixture.RootElement.GetProperty("companies").EnumerateArray().ToArray();
        Assert.Equal(2100m, companies.Where(company => company.GetProperty("symbol").GetString() is "کگل" or "کگهر")
            .Sum(company => company.GetProperty("netProfit").GetDecimal()));
    }
}
