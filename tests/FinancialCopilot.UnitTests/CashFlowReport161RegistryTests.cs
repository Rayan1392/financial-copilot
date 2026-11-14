using System.Text.Json;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.FinancialData.CashFlowReport;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161RegistryTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "specs"))) root = root.Parent;
        Assert.NotNull(root);
        return root!.FullName;
    }

    [Fact]
    public void Registry_CoversTheEntireSourceCatalogWithTheExactLabels()
    {
        var path = Path.Combine(Root(), "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog", "source-item-snapshot.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var source = document.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("providerName").GetString() == "NoavaranCurrentApi" && item.GetProperty("statementType").GetString() == "CashFlow")
            .ToDictionary(item => item.GetProperty("sourceItemId").GetInt32(), item => item.GetProperty("titleFa").GetString());
        Assert.Equal(70, CashFlowLineRegistry.Lines.Count);
        Assert.Equal(source.Keys.Order(), CashFlowLineRegistry.Lines.Select(item => item.SourceItemId).Order());
        foreach (var line in CashFlowLineRegistry.Lines) Assert.Equal(source[line.SourceItemId], line.Label);
    }

    [Fact]
    public void TotalsCrossCheckAndProposedItems_HaveApprovedRolesAndVisibility()
    {
        Assert.Equal("primary-total", CashFlowLineRegistry.Find(128)!.Role);
        Assert.Equal(CashFlowSection.Operating, CashFlowLineRegistry.Find(128)!.Section);
        Assert.Equal("cross-check", CashFlowLineRegistry.Find(122)!.Role);
        Assert.Equal("presentation-marker", CashFlowLineRegistry.Find(120)!.Role);
        Assert.Equal("capex", CashFlowLineRegistry.Find(10)!.MetricComponent);
        Assert.Null(CashFlowLineRegistry.Find(130)!.MetricComponent);
        Assert.Equal(2, CashFlowLineRegistry.ResidualLines.Count);
        foreach (var template in new[] { CashFlowLineRegistry.GeneralTemplate, CashFlowLineRegistry.BankingTemplate })
        {
            Assert.DoesNotContain(CashFlowLineRegistry.Displayable(template), line => line.Verification is CashFlowVerification.Proposed or CashFlowVerification.Excluded);
            Assert.DoesNotContain(CashFlowLineRegistry.Displayable(template), line => CashFlowLineRegistry.ExcludedValueItems.Contains(line.SourceItemId));
        }
        Assert.False(CashFlowLineRegistry.Find(4)!.CanDisplay(CashFlowLineRegistry.GeneralTemplate));
        Assert.True(CashFlowLineRegistry.Find(4)!.CanDisplay(CashFlowLineRegistry.BankingTemplate));
    }

    [Fact]
    public void MappingMatrix_SectionsAndVerificationApplyToEveryDetailItem()
    {
        void Check(int[] ids, CashFlowSection section, CashFlowVerification verification, string industry = "all")
        {
            foreach (var id in ids)
            {
                var item = Assert.IsType<CashFlowLineDefinition>(CashFlowLineRegistry.Find(id));
                Assert.Equal(section, item.Section);
                Assert.Equal(verification, item.Verification);
                Assert.Equal(industry, item.Industry);
            }
        }
        Check([9], CashFlowSection.Operating, CashFlowVerification.Proposed);
        Check([10, 14], CashFlowSection.Investing, CashFlowVerification.Both);
        Check([48, 104, 4], CashFlowSection.Investing, CashFlowVerification.Bank);
        Check([46, 47, 3, 42, 95, 96], CashFlowSection.Investing, CashFlowVerification.Proposed, "general");
        Check([83, 11, 15, 12, 17, 70, 71, 103, 130, 129, 13], CashFlowSection.Investing, CashFlowVerification.Proposed);
        Check([23, 24, 5, 6], CashFlowSection.Financing, CashFlowVerification.Both);
        Check([117, 119], CashFlowSection.Financing, CashFlowVerification.Bank);
        Check([21, 131, 40, 41, 37, 132, 133, 134, 114, 135], CashFlowSection.Financing, CashFlowVerification.Proposed);
        Check([22, 136, 137, 138, 139], CashFlowSection.Financing, CashFlowVerification.Excluded, "consolidated-only");
        foreach (var id in new[] { 48, 104, 4, 117, 119 })
            Assert.False(CashFlowLineRegistry.Find(id)!.CanDisplay(CashFlowLineRegistry.GeneralTemplate));
    }

    [Fact]
    public void ExcludedItems_AreNeverIncludedInTheValueReadSet()
    {
        int[] excluded = [1, 58, 59, 60, 61, 62, 63, 106, 107, 108, 109, 110, 111, 112, 113, 126, 127];
        Assert.Equal(excluded.Order(), CashFlowLineRegistry.ExcludedValueItems.Order());
        Assert.Empty(CashFlowLineRegistry.ReadableSourceItemIds.Intersect(excluded));
        Assert.Contains(120, CashFlowLineRegistry.ReadableSourceItemIds);
        Assert.Contains(22, CashFlowLineRegistry.ReadableSourceItemIds);
    }

    [Fact]
    public void FeatureCode_DoesNotEncodeSpecificCompaniesOrTickers()
    {
        var directory = Path.Combine(Root(), "src", "backend", "FinancialCopilot.Application", "FinancialData", "CashFlowReport");
        string[] forbiddenTickers = ["فلوله", "وبملت", "فولاد", "فملی", "کگل", "شگل"];
        foreach (var path in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            foreach (var ticker in forbiddenTickers) Assert.DoesNotContain(ticker, source);
            Assert.DoesNotMatch(new Regex("\\\"[0-9]{3,}\\\"", RegexOptions.CultureInvariant), source);
        }
    }
}
