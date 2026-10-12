using System.Net;
using System.Text.Json;
using FinancialCopilot.API.Controllers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class FinancialStatementInventoryTests
{
    [Fact]
    public async Task Inventory_MapsReportRowsAndAggregatesIndicatorMetrics_ReadOnly()
    {
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
            Name = "Company", TseSymbol = "کگل"
        });
        var reportId = Guid.NewGuid();
        db.FinancialStatements.AddRange(
            new NormalizedFinancialStatementRow
            {
                Id = reportId, ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
                ExternalStatementId = "5001", StatementType = "IncomeStatement", PeriodType = "NineMonths",
                PeriodStart = new DateOnly(2025, 3, 21), PeriodEnd = new DateOnly(2025, 12, 21),
                IsAudited = true, IsRepresented = false, IsComposing = true,
                WarningsJson = "[{\"source\":\"NoavaranCurrentApi\",\"jalaliFiscalYearEnd\":\"1404/12/29\",\"isAudited\":true,\"isRepresented\":false,\"isComposing\":true}]"
            },
            new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
                ExternalStatementId = "5002", StatementType = "BalanceSheet", PeriodType = "TwelveMonths",
                PeriodStart = new DateOnly(2024, 3, 20), PeriodEnd = new DateOnly(2025, 3, 20),
                IsAudited = false, IsRepresented = true, IsComposing = false, WarningsJson = "[]"
            },
            new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
                ExternalStatementId = "5003", StatementType = "IncomeStatement", PeriodType = "NineMonths",
                PeriodStart = new DateOnly(2025, 3, 21), PeriodEnd = new DateOnly(2025, 12, 21),
                IsAudited = false, IsRepresented = true, IsComposing = true,
                WarningsJson = "[{\"source\":\"NoavaranCurrentApi\",\"jalaliFiscalYearEnd\":\"1404/12/29\",\"isAudited\":false,\"isRepresented\":true,\"isComposing\":true}]"
            },
            new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
                ExternalStatementId = "5004", StatementType = "CashFlow", PeriodType = "ThreeMonths",
                PeriodStart = new DateOnly(2025, 3, 21), PeriodEnd = new DateOnly(2025, 6, 21),
                IsAudited = false, IsRepresented = false, IsComposing = false,
                WarningsJson = "[{\"source\":\"NoavaranCurrentApi\",\"jalaliFiscalYearEnd\":\"1404/12/29\",\"isAudited\":false,\"isRepresented\":false,\"isComposing\":false}]"
            },
            new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
                ExternalStatementId = "5005", StatementType = "CashFlow", PeriodType = "SixMonths",
                PeriodStart = new DateOnly(2025, 3, 21), PeriodEnd = new DateOnly(2025, 9, 22),
                IsAudited = false, IsRepresented = false, IsComposing = false,
                WarningsJson = "[{\"source\":\"NoavaranCurrentApi\",\"jalaliFiscalYearEnd\":\"1404/12/29\",\"isAudited\":false,\"isRepresented\":false,\"isComposing\":false}]"
            },
            new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "43",
                ExternalStatementId = "9999", StatementType = "IncomeStatement", PeriodType = "TwelveMonths",
                PeriodStart = new DateOnly(2024, 3, 20), PeriodEnd = new DateOnly(2025, 3, 20),
                IsAudited = true, IsRepresented = false, IsComposing = true,
                WarningsJson = "[{\"source\":\"NoavaranCurrentApi\",\"jalaliFiscalYearEnd\":\"1404/12/29\",\"isAudited\":true,\"isRepresented\":false,\"isComposing\":true}]"
            });
        db.FinancialStatementLineItems.AddRange(
            new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = reportId, MetricCode = "Revenue", Value = 5 },
            new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = reportId, MetricCode = "Profit", Value = 2 });
        var evidence = JsonSerializer.Serialize(new[]
        {
            new { source = "NoavaranCurrentApi", comBSID = 777, jalaliFiscalYearEnd = "1404/12/29", isAudited = true, isRepresented = false, isComposing = true }
        });
        foreach (var metricCode in new[] { "EPS", "PE_TTM" })
        {
            db.DerivedMetrics.Add(new DerivedMetricRow
            {
                Id = Guid.NewGuid(), ExternalCompanyId = "42", MetricCode = metricCode,
                MetricVersion = "v1", CalculationPolicyVersion = "v1", PeriodType = "NineMonths",
                PeriodStart = new DateOnly(2025, 3, 21), PeriodEnd = new DateOnly(2025, 12, 21),
                Unit = "x", ObservedAt = DateTimeOffset.UtcNow, LastSynchronizedAt = DateTimeOffset.UtcNow,
                SourceEvidenceJson = evidence
            });
        }
        await db.SaveChangesAsync();

        var controller = new NoavaranFinancialStatementBackfillController(db, null!);
        var action = await controller.GetInventory("کگل", CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var inventory = Assert.IsType<FinancialCopilot.API.Contracts.FinancialStatementInventoryResponse>(response.Value);

        Assert.Equal(6, inventory.TotalCount);
        Assert.Equal(6, inventory.Items.Count);
        Assert.Equal(1, inventory.Page);
        Assert.Equal(15, inventory.PageSize);
        Assert.Equal(1, inventory.TotalPages);
        var income = Assert.Single(inventory.Items, item => item.VersionId == "5001");
        Assert.Equal(reportId, income.ReportId);
        Assert.Equal(1404, income.FiscalYearEndJalali);
        Assert.Equal("1404/09/30", income.PeriodEndJalali);
        Assert.Equal(9, income.PeriodMonths);
        Assert.True(income.IsAudited);
        Assert.False(income.IsRestated);
        Assert.True(income.IsConsolidated);
        Assert.Single(inventory.Items, item => item.StatementType == "FinancialIndicators");
        Assert.Equal(2, inventory.Items.Count(item => item.StatementType == "IncomeStatement"));
        Assert.Single(inventory.Items, item => item.StatementType == "CashFlow" && item.PeriodMonths == 3);
        Assert.Single(inventory.Items, item => item.StatementType == "CashFlow" && item.PeriodMonths == 6);
        Assert.Single(inventory.Items, item => item.StatementType == "BalanceSheet" && item.PeriodMonths == 12 && item.FiscalYearEndJalali is null && item.IsAudited is null);
        Assert.Equal(2, await db.DerivedMetrics.CountAsync());
        Assert.Equal(new[] { "5001", "5003" }, inventory.Items
            .Where(item => item.StatementType == "IncomeStatement")
            .Select(item => item.VersionId));
    }

    [Fact]
    public async Task Inventory_UnknownTicker_ReturnsNotFound()
    {
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var controller = new NoavaranFinancialStatementBackfillController(db, null!);

        var action = await controller.GetInventory("unknown", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(action.Result);
    }

    [Fact]
    public async Task Inventory_EligibleTickerWithoutReports_ReturnsEmptyInventory()
    {
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
            Name = "Company", TseSymbol = "کگل"
        });
        await db.SaveChangesAsync();

        var controller = new NoavaranFinancialStatementBackfillController(db, null!);
        var action = await controller.GetInventory("کگل", CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var inventory = Assert.IsType<FinancialCopilot.API.Contracts.FinancialStatementInventoryResponse>(response.Value);

        Assert.Empty(inventory.Items);
        Assert.Equal(0, inventory.TotalCount);
        Assert.Equal(1, inventory.Page);
        Assert.Equal(15, inventory.PageSize);
        Assert.Equal(0, inventory.TotalPages);
    }

    [Fact]
    public async Task Inventory_PaginatesCombinedReportRowsWithoutRepeatingRecords()
    {
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
            Name = "Company", TseSymbol = "کگل"
        });
        var types = new[] { "BalanceSheet", "IncomeStatement", "CashFlow", "BalanceSheet" };
        for (var i = 0; i < 16; i++)
        {
            db.FinancialStatements.Add(new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "42",
                ExternalStatementId = $"version-{i:00}", StatementType = types[i % types.Length], PeriodType = "ThreeMonths",
                PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 2, 1).AddDays(i),
                WarningsJson = "[]"
            });
        }
        await db.SaveChangesAsync();
        var controller = new NoavaranFinancialStatementBackfillController(db, null!);

        var firstAction = await controller.GetInventory("کگل", CancellationToken.None, 1);
        var first = Assert.IsType<FinancialCopilot.API.Contracts.FinancialStatementInventoryResponse>(
            Assert.IsType<OkObjectResult>(firstAction.Result).Value);
        var secondAction = await controller.GetInventory("کگل", CancellationToken.None, 2);
        var second = Assert.IsType<FinancialCopilot.API.Contracts.FinancialStatementInventoryResponse>(
            Assert.IsType<OkObjectResult>(secondAction.Result).Value);
        var outOfRangeAction = await controller.GetInventory("کگل", CancellationToken.None, 3);
        var outOfRange = Assert.IsType<FinancialCopilot.API.Contracts.FinancialStatementInventoryResponse>(
            Assert.IsType<OkObjectResult>(outOfRangeAction.Result).Value);

        Assert.Equal(16, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(15, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(2, second.Page);
        Assert.Equal(16, first.Items.Concat(second.Items).Select(item => item.VersionId).Distinct().Count());
        Assert.Equal("version-15", first.Items[0].VersionId);
        Assert.Equal("version-00", second.Items[0].VersionId);
        Assert.Empty(outOfRange.Items);
        Assert.Equal(16, outOfRange.TotalCount);
        Assert.Equal(2, outOfRange.TotalPages);
        Assert.Contains(first.Items, item => item.StatementType == "CashFlow");
        Assert.Contains(first.Items, item => item.StatementType == "BalanceSheet");
        Assert.Contains(first.Items, item => item.StatementType == "IncomeStatement");
    }

    [Fact]
    public async Task Inventory_RejectsNonPositivePage()
    {
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var controller = new NoavaranFinancialStatementBackfillController(db, null!);

        var action = await controller.GetInventory("کگل", CancellationToken.None, 0);

        Assert.IsType<BadRequestObjectResult>(action.Result);
    }
}
