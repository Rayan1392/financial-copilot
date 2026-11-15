using System.Text.Json;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161V1FreezeTests
{
    [Fact]
    public void V1RoutingSignatures_MatchThePreFeature161GoldenBaseline()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "specs"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root!.FullName, "tests", "FinancialCopilot.UnitTests", "TestData", "Feature161", "routing-baseline.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var phrase = item.GetProperty("Phrase").GetString()!;
            var expected = item.GetProperty("V1").Deserialize<Feature154RoutingBaselineTests.RoutingSignature>();
            Assert.NotNull(expected);
            Assert.Equal(expected, Feature154RoutingBaselineTests.Capture(phrase, expected.Preserved, v2: false));
        }
    }
}
