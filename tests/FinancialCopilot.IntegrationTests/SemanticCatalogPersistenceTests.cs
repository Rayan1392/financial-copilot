using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class SemanticCatalogPersistenceTests
{
    [Fact]
    public async Task SemanticCatalogRows_PersistHistoricalDefinitionAliasPolicyAndDependencyMetadata()
    {
        var options = new DbContextOptionsBuilder<SemanticCatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new SemanticCatalogDbContext(options);
        dbContext.MetricDefinitions.Add(new FinancialMetricDefinitionRow
        {
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            DisplayName = "P/E (TTM)",
            Description = "TTM valuation definition.",
            Category = "Valuation",
            UnitCode = "ratio",
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        dbContext.MetricAliases.Add(new MetricAliasRow
        {
            Expression = "p/e",
            Language = "en-US",
            MetricCode = "PE_TTM",
            MetricVersion = "v1"
        });
        dbContext.MetricCalculationPolicies.Add(new MetricCalculationPolicyRow
        {
            MetricCode = "PE_TTM",
            PolicyVersion = "ttm-valuation-v1",
            DefinitionVersion = "v1",
            Unit = "Ratio",
            MissingDataPolicy = "ReturnMissingValue",
            FormulaIdentifier = "price-divided-by-ttm-eps",
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        dbContext.MetricDependencies.Add(new MetricDependencyRow
        {
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            DependencyMetricCode = "TTM_EPS",
            RequiredDefinitionVersion = "v1",
            Required = true
        });

        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("v1", (await dbContext.MetricDefinitions.SingleAsync()).MetricVersion);
        Assert.Equal("p/e", (await dbContext.MetricAliases.SingleAsync()).Expression);
        Assert.Equal("ttm-valuation-v1", (await dbContext.MetricCalculationPolicies.SingleAsync()).PolicyVersion);
        Assert.Equal("TTM_EPS", (await dbContext.MetricDependencies.SingleAsync()).DependencyMetricCode);
    }
}
