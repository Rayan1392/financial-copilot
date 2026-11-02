using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class CompanyDisclosureFeedPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    [Fact]
    public async Task QueryAsync_PaginatesFilteredRevisionGroupsInPostgreSql()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = "DisclosurePagingTest",
            ExternalCompanyId = "company-1",
            Name = "Disclosure Paging Company",
            Ticker = "DPC",
            CompanySymbol = "DPC",
            LastSynchronizedAt = now
        });

        for (var index = 1; index <= 25; index++)
        {
            db.MonthlyReports.Add(new NormalizedMonthlyReportRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "DisclosurePagingTest",
                ExternalCompanyId = "company-1",
                ExternalReportId = $"report-{index:D2}",
                CompanyId = companyId,
                PeriodStart = new DateOnly(2026, 1, 1),
                PeriodEnd = new DateOnly(2026, 1, 31),
                ReportType = $"Monthly-{index:D2}",
                OutputType = 0,
                SourcePayloadChecksum = $"payload-{index:D2}",
                LogicalReportKey = $"logical-{index:D2}",
                RevisionFingerprint = $"revision-{index:D2}",
                IsAccepted = true,
                RevisionStatus = "Accepted",
                LastSynchronizedAt = now.AddMinutes(index),
                PublishedAt = new DateOnly(2026, 2, 1)
            });
        }

        await db.SaveChangesAsync();

        var repository = new CompanyDisclosureFeedRepository(
            db,
            new CompanyDisclosureFeedCache(new MemoryCache(new MemoryCacheOptions())));
        var first = await repository.QueryAsync(new CompanyDisclosureFeedQuery(PageSize: 20));
        var second = await repository.QueryAsync(new CompanyDisclosureFeedQuery(Page: 2, PageSize: 20));

        Assert.Equal(25, first.TotalCount);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Empty(first.Items.Select(item => item.DisclosureId)
            .Intersect(second.Items.Select(item => item.DisclosureId)));
    }
}
