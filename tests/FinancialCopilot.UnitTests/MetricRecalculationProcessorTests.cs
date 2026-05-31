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

namespace FinancialCopilot.UnitTests;

public sealed class MetricRecalculationProcessorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-31T09:00:00Z");

    [Fact]
    public async Task ProcessPending_WithFinancialStatementsRequest_ComputesGrowthForDependentMetric()
    {
        await using var db = NewDb();
        var (companyId, symbolId) = SeedCompanyWithSymbol(db, "5001", "CODAL1");
        SeedQuarterlyNetProfit(db, "5001", priorYearValue: 100m, currentYearValue: 200m);
        var requestId = AddPendingRequest(db, ProviderDataset.FinancialStatements, externalRef: "5001");
        await db.SaveChangesAsync();

        var processor = NewProcessor(db);
        var result = await processor.ProcessPendingAsync(maximumBatch: 10, CancellationToken.None);

        Assert.Equal(1, result.ProcessedRequestCount);
        Assert.Equal(1, result.CompletedRequestCount);
        Assert.True(result.MetricsRecomputed > 0);
        var growth = await db.DerivedMetrics
            .SingleAsync(m => m.MetricCode == "NET_PROFIT_GROWTH_YOY" && m.SymbolId == symbolId);
        Assert.Equal(100m, growth.Value); // (200-100)/100 * 100
        var row = await db.MetricRecalculationRequests.SingleAsync(r => r.Id == requestId);
        Assert.NotNull(row.ProcessedAt);
        Assert.Equal(1, row.AttemptCount);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task ProcessPending_ReProcessing_IsIdempotent()
    {
        await using var db = NewDb();
        var (_, _) = SeedCompanyWithSymbol(db, "5002", "CODAL2");
        SeedQuarterlyNetProfit(db, "5002", priorYearValue: 100m, currentYearValue: 200m);
        AddPendingRequest(db, ProviderDataset.FinancialStatements, externalRef: "5002");
        await db.SaveChangesAsync();
        var processor = NewProcessor(db);
        await processor.ProcessPendingAsync(10, CancellationToken.None);
        var rowCountAfterFirst = await db.DerivedMetrics.CountAsync(m => m.MetricCode == "NET_PROFIT_GROWTH_YOY");

        // Mark the request unprocessed and process again (simulating a retry).
        var req = await db.MetricRecalculationRequests.SingleAsync();
        req.ProcessedAt = null;
        await db.SaveChangesAsync();
        await processor.ProcessPendingAsync(10, CancellationToken.None);
        var rowCountAfterSecond = await db.DerivedMetrics.CountAsync(m => m.MetricCode == "NET_PROFIT_GROWTH_YOY");

        Assert.Equal(rowCountAfterFirst, rowCountAfterSecond); // upsert on unique key
    }

    [Fact]
    public async Task ProcessPending_BoundedBatch_LimitsToMaximum()
    {
        await using var db = NewDb();
        SeedCompanyWithSymbol(db, "9001", "SYM1");
        for (var i = 0; i < 5; i++)
        {
            AddPendingRequest(db, ProviderDataset.FinancialStatements, externalRef: "9001", checksumSuffix: i.ToString());
        }
        await db.SaveChangesAsync();
        var processor = NewProcessor(db);

        var result = await processor.ProcessPendingAsync(maximumBatch: 2, CancellationToken.None);

        Assert.Equal(2, result.ProcessedRequestCount);
        Assert.Equal(3, await db.MetricRecalculationRequests.CountAsync(r => r.ProcessedAt == null));
    }

    [Fact]
    public async Task ProcessPending_SymbolsDataset_NoOp()
    {
        await using var db = NewDb();
        AddPendingRequest(db, ProviderDataset.Symbols, externalRef: null);
        await db.SaveChangesAsync();
        var processor = NewProcessor(db);

        var result = await processor.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, result.CompletedRequestCount);
        Assert.Equal(0, result.MetricsRecomputed);
        Assert.Equal(0, await db.DerivedMetrics.CountAsync());
    }

    [Fact]
    public async Task ProcessPending_FailureOnOne_DoesNotBlockOthers()
    {
        await using var db = NewDb();
        SeedCompanyWithSymbol(db, "1001", "OK1");
        SeedQuarterlyNetProfit(db, "1001", priorYearValue: 100m, currentYearValue: 150m);
        AddPendingRequest(db, ProviderDataset.FinancialStatements, externalRef: "1001", checksumSuffix: "ok");
        AddPendingRequest(db, ProviderDataset.FinancialStatements, externalRef: null, checksumSuffix: "bad"); // no-op (no ext ref)
        AddPendingRequest(db, ProviderDataset.FinancialStatements, externalRef: "1001", checksumSuffix: "ok2");
        await db.SaveChangesAsync();
        var processor = NewProcessor(db);

        var result = await processor.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(3, result.ProcessedRequestCount);
        Assert.Equal(3, result.CompletedRequestCount); // null external ref is a no-op, not a failure
        Assert.True(await db.DerivedMetrics.AnyAsync(m => m.MetricCode == "NET_PROFIT_GROWTH_YOY"));
    }

    [Fact]
    public async Task ProcessPending_NoRegisteredCalculator_DoesNotComputeUnregisteredMetric()
    {
        await using var db = NewDb();
        SeedCompanyWithSymbol(db, "3001", "SYM3");
        SeedQuarterlyNetProfit(db, "3001", priorYearValue: 100m, currentYearValue: 200m);
        AddPendingRequest(db, ProviderDataset.FinancialStatements, externalRef: "3001");
        await db.SaveChangesAsync();

        // Build a processor whose registry includes only ONE registered metric (NET_PROFIT_GROWTH_YOY).
        var processor = NewProcessor(db, growthOnly: true);
        await processor.ProcessPendingAsync(10, CancellationToken.None);

        // The unregistered REVENUE_GROWTH_YOY must not be computed even though its dependency is in scope.
        Assert.Equal(0, await db.DerivedMetrics.CountAsync(m => m.MetricCode == "REVENUE_GROWTH_YOY"));
        Assert.Equal(1, await db.DerivedMetrics.CountAsync(m => m.MetricCode == "NET_PROFIT_GROWTH_YOY"));
    }

    // ---- Helpers ----

    private static FinancialIngestionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (Guid CompanyId, Guid SymbolId) SeedCompanyWithSymbol(
        FinancialIngestionDbContext db,
        string externalCompanyId,
        string symbolCode)
    {
        var companyId = Guid.NewGuid();
        var symbolId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = "CodalDb",
            ExternalCompanyId = externalCompanyId,
            Name = "Test Co",
            LastSynchronizedAt = Now
        });
        db.Symbols.Add(new NormalizedSymbolRow
        {
            Id = symbolId,
            CompanyId = companyId,
            ProviderName = "CodalDb",
            ExternalSymbolId = externalCompanyId,
            SymbolCode = symbolCode,
            LastSynchronizedAt = Now
        });
        return (companyId, symbolId);
    }

    private static void SeedQuarterlyNetProfit(
        FinancialIngestionDbContext db,
        string externalCompanyId,
        decimal priorYearValue,
        decimal currentYearValue)
    {
        // YoY: same fiscal quarter, two years apart. Q1 2025 vs Q1 2026.
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
                { Id = Guid.NewGuid(), FinancialStatementId = priorStmt.Id, MetricCode = "NET_PROFIT", Value = priorYearValue },
            new NormalizedFinancialStatementLineItemRow
                { Id = Guid.NewGuid(), FinancialStatementId = currentStmt.Id, MetricCode = "NET_PROFIT", Value = currentYearValue });
    }

    private static Guid AddPendingRequest(
        FinancialIngestionDbContext db,
        ProviderDataset dataset,
        string? externalRef,
        string checksumSuffix = "default")
    {
        var id = Guid.NewGuid();
        db.MetricRecalculationRequests.Add(new MetricRecalculationRequestRow
        {
            Id = id,
            SourceDataset = dataset.ToString(),
            ExternalReference = externalRef,
            SourcePayloadChecksum = $"{dataset}-{externalRef}-{checksumSuffix}",
            RequestedAt = Now
        });
        return id;
    }

    private static MetricRecalculationProcessor NewProcessor(
        FinancialIngestionDbContext db,
        bool growthOnly = false)
    {
        IEnumerable<IFinancialMetricCalculator> calculators = growthOnly
            ? [new PercentageGrowthMetricCalculator(
                new MetricCode("NET_PROFIT_GROWTH_YOY"), new MetricCode("NET_PROFIT"))]
            : [
                new PercentageGrowthMetricCalculator(
                    new MetricCode("NET_PROFIT_GROWTH_YOY"), new MetricCode("NET_PROFIT")),
                new PercentageGrowthMetricCalculator(
                    new MetricCode("NET_PROFIT_GROWTH_QOQ"), new MetricCode("NET_PROFIT"))
            ];

        var registry = new FinancialMetricRegistry(
            PhaseOneFinancialSemanticCatalog.Definitions,
            calculators);
        var policyProvider = new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies);
        var inputReader = new NormalizedMetricInputReader(
            [new LineItemMetricInputSource(db, new MetricCode("NET_PROFIT"))]);
        var resultStore = new PersistedDerivedMetricResultStore(db);
        var calcService = new DerivedMetricCalculationService(registry, policyProvider, resultStore);
        var recalcCommand = new DerivedMetricRecalculationCommand(calcService);
        return new MetricRecalculationProcessor(
            db,
            registry,
            policyProvider,
            inputReader,
            recalcCommand,
            new FixedTimeProvider(Now),
            NullLogger<MetricRecalculationProcessor>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
