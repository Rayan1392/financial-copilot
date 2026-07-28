using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// End-to-end: ingestion writes a recalculation request → <c>MetricRecalculationProcessor</c>
/// drains it → <c>DerivedMetrics</c> row materialized → a scanner-style read
/// (<c>NET_PROFIT_GROWTH_YOY &gt;= 100</c>) returns the company from precomputed values.
/// </summary>
public sealed class MetricRecalculationOutboxTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-31T09:00:00Z");

    [Fact]
    public async Task PendingRequest_ProducesPrecomputedGrowthRowReadableByScanner()
    {
        await using var db = NewDb();
        SeedCompanyWithSymbol(db, "5099", "DOUBLER");
        SeedQuarterlyNetProfit(db, "5099", 100m, 200m); // doubled YoY
        db.MetricRecalculationRequests.Add(new MetricRecalculationRequestRow
        {
            Id = Guid.NewGuid(),
            SourceDataset = nameof(ProviderDataset.FinancialStatements),
            ExternalReference = "5099",
            SourcePayloadChecksum = "e2e-checksum",
            RequestedAt = Now
        });
        await db.SaveChangesAsync();

        var registry = new FinancialMetricRegistry(
            PhaseOneFinancialSemanticCatalog.Definitions,
            [new PercentageGrowthMetricCalculator(
                new MetricCode("NET_PROFIT_GROWTH_YOY"), new MetricCode("NET_PROFIT"))]);
        var policyProvider = new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies);
        var inputReader = new NormalizedMetricInputReader(
            [new LineItemMetricInputSource(db, new MetricCode("NET_PROFIT"))]);
        var resultStore = new PersistedDerivedMetricResultStore(db);
        var calcService = new DerivedMetricCalculationService(registry, policyProvider, resultStore);
        var processor = new MetricRecalculationProcessor(
            db,
            registry,
            policyProvider,
            inputReader,
            new DerivedMetricRecalculationCommand(calcService),
            new FixedTimeProvider(Now),
            NullLogger<MetricRecalculationProcessor>.Instance);

        var result = await processor.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, result.CompletedRequestCount);
        var precomputed = await db.DerivedMetrics
            .Where(m => m.MetricCode == "NET_PROFIT_GROWTH_YOY" && m.ExternalCompanyId == "5099" && m.Value >= 100m)
            .ToListAsync();
        var hit = Assert.Single(precomputed);
        Assert.Equal(100m, hit.Value); // (200-100)/100 * 100
    }

    private static FinancialIngestionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Guid SeedCompanyWithSymbol(
        FinancialIngestionDbContext db,
        string externalCompanyId,
        string symbolCode)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = "CodalDb",
            ExternalCompanyId = externalCompanyId,
            Name = "Test Co",
            CompanySymbol = symbolCode,
            LastSynchronizedAt = Now
        });
        return companyId;
    }

    private static void SeedQuarterlyNetProfit(
        FinancialIngestionDbContext db,
        string externalCompanyId,
        decimal priorValue,
        decimal currentValue)
    {
        var priorStmt = new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "CodalDb",
            ExternalCompanyId = externalCompanyId,
            ExternalStatementId = $"{externalCompanyId}-2025-Q1",
            StatementType = nameof(FinancialStatementType.IncomeStatement),
            PeriodType = nameof(FiscalPeriodType.ThreeMonths),
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 3, 31),
            SourcePayloadChecksum = "x",
            LastSynchronizedAt = Now
        };
        var currentStmt = new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "CodalDb",
            ExternalCompanyId = externalCompanyId,
            ExternalStatementId = $"{externalCompanyId}-2026-Q1",
            StatementType = nameof(FinancialStatementType.IncomeStatement),
            PeriodType = nameof(FiscalPeriodType.ThreeMonths),
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            SourcePayloadChecksum = "y",
            LastSynchronizedAt = Now
        };
        db.FinancialStatements.AddRange(priorStmt, currentStmt);
        db.FinancialStatementLineItems.AddRange(
            new NormalizedFinancialStatementLineItemRow
                { Id = Guid.NewGuid(), FinancialStatementId = priorStmt.Id, MetricCode = "NET_PROFIT", Value = priorValue },
            new NormalizedFinancialStatementLineItemRow
                { Id = Guid.NewGuid(), FinancialStatementId = currentStmt.Id, MetricCode = "NET_PROFIT", Value = currentValue });
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
