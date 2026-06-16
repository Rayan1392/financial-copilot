using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.MissingAnswer;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Verifies the scanner emits missing-answer feedback for empty/sparse results and that collector
/// failures cannot break the scanner response (spec 028).
/// </summary>
public sealed class ScannerExecutionWithFeedbackTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-31T09:00:00Z");

    [Fact]
    public async Task EmptyResult_WithUnregisteredMetric_EmitsMetricGapFeedback()
    {
        await using var db = NewDb();
        SeedSymbols(db, count: 4);
        await db.SaveChangesAsync();
        var collector = new RecordingCollector();
        var service = NewService(db, collector, registeredMetrics: []);

        var plan = MakePlan("UNKNOWN_METRIC", threshold: 50m);
        var result = await service.ExecuteAsync(
            new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime),
                ActorId: "user-1", QueryText: "list companies by unknown metric"),
            CancellationToken.None);

        Assert.Empty(result.Rows);
        var feedback = Assert.Single(collector.Collected);
        Assert.Equal(MissingAnswerFeedbackClassification.MetricGap, feedback.Classification);
        Assert.Equal("UNKNOWN_METRIC", feedback.RequestedMetricCode);
        Assert.Equal("user-1", feedback.ActorId);
        Assert.Equal(4, feedback.SymbolCountTotal);
        Assert.Equal(0, feedback.SymbolCountMatched);
    }

    [Fact]
    public async Task EmptyResult_WithRegisteredMetricButNoData_EmitsCalculationGapFeedback()
    {
        await using var db = NewDb();
        SeedSymbols(db, count: 4);
        await db.SaveChangesAsync();
        var collector = new RecordingCollector();
        var service = NewService(db, collector, registeredMetrics: ["PE_TTM"]);

        var plan = MakePlan("PE_TTM", threshold: 5m);
        var result = await service.ExecuteAsync(
            new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime),
                ActorId: "user-1", QueryText: "P/E below 5"),
            CancellationToken.None);

        Assert.Empty(result.Rows);
        var feedback = Assert.Single(collector.Collected);
        Assert.Equal(MissingAnswerFeedbackClassification.CalculationGap, feedback.Classification);
        Assert.Equal("PE_TTM", feedback.RequestedMetricCode);
    }

    [Fact]
    public async Task SparseMatch_BelowHalfUniverse_EmitsDataCoverageGapFeedback()
    {
        await using var db = NewDb();
        SeedSymbols(db, count: 10);
        await db.SaveChangesAsync();
        // 10 symbols total; only 2 match a PE<5 filter; PE rows exist for some.
        SeedPeMetricFor(db, symbolIndex: 0, value: 3m);
        SeedPeMetricFor(db, symbolIndex: 1, value: 4m);
        SeedPeMetricFor(db, symbolIndex: 2, value: 20m); // doesn't match filter
        await db.SaveChangesAsync();

        var collector = new RecordingCollector();
        var service = NewService(db, collector, registeredMetrics: ["PE_TTM"]);

        var plan = MakePlan("PE_TTM", threshold: 5m);
        var result = await service.ExecuteAsync(
            new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime),
                ActorId: "user-1", QueryText: "P/E below 5"),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        var feedback = Assert.Single(collector.Collected);
        Assert.Equal(MissingAnswerFeedbackClassification.DataCoverageGap, feedback.Classification);
        Assert.Equal(10, feedback.SymbolCountTotal);
        Assert.Equal(2, feedback.SymbolCountMatched);
    }

    [Fact]
    public async Task HealthyMatch_AboveHalfUniverse_EmitsNoFeedback()
    {
        await using var db = NewDb();
        SeedSymbols(db, count: 4);
        await db.SaveChangesAsync();
        for (var i = 0; i < 4; i++) SeedPeMetricFor(db, symbolIndex: i, value: 3m);
        await db.SaveChangesAsync();

        var collector = new RecordingCollector();
        var service = NewService(db, collector, registeredMetrics: ["PE_TTM"]);

        var plan = MakePlan("PE_TTM", threshold: 5m);
        await service.ExecuteAsync(
            new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime),
                ActorId: "user-1", QueryText: "P/E below 5"),
            CancellationToken.None);

        Assert.Empty(collector.Collected);
    }

    [Fact]
    public async Task CollectorThrows_DoesNotBreakScannerResponse()
    {
        await using var db = NewDb();
        SeedSymbols(db, count: 4);
        await db.SaveChangesAsync();
        var failing = new ThrowingCollector();
        var service = NewService(db, failing, registeredMetrics: []);

        var plan = MakePlan("UNKNOWN", threshold: 50m);
        var result = await service.ExecuteAsync(
            new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime),
                ActorId: "user-1", QueryText: "broken collector"),
            CancellationToken.None);

        // Scanner returned a well-formed empty result instead of propagating the collector exception.
        Assert.NotNull(result);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task NoActorId_DoesNotInvokeCollector()
    {
        await using var db = NewDb();
        SeedSymbols(db, count: 4);
        await db.SaveChangesAsync();
        var collector = new RecordingCollector();
        var service = NewService(db, collector, registeredMetrics: []);

        var plan = MakePlan("UNKNOWN", threshold: 50m);
        await service.ExecuteAsync(
            new ScannerExecutionRequest(plan, DateOnly.FromDateTime(Now.UtcDateTime)), // no ActorId/QueryText
            CancellationToken.None);

        Assert.Empty(collector.Collected);
    }

    // ---- Helpers ----

    private static FinancialIngestionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EfCoreScannerExecutionService NewService(
        FinancialIngestionDbContext db,
        IMissingAnswerFeedbackCollector collector,
        IReadOnlyCollection<string> registeredMetrics) =>
        new(
            db,
            new ScannerResultColumnPolicy(),
            new EmptyQuoteResolver(),
            new ScannerResultRanker(),
            new FixedTimeProvider(Now),
            new StubMetricRegistry(registeredMetrics),
            collector);

    private static void SeedSymbols(FinancialIngestionDbContext db, int count)
    {
        for (var i = 0; i < count; i++)
        {
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "Test",
                ExternalCompanyId = $"co-{i}", Name = $"Company {i}",
                CompanySymbol = $"SYM{i}",
                LastSynchronizedAt = Now
            });
        }
    }

    private static void SeedPeMetricFor(FinancialIngestionDbContext db, int symbolIndex, decimal value)
    {
        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(), ExternalCompanyId = $"co-{symbolIndex}",
            MetricCode = "PE_TTM", MetricVersion = "v1",
            CalculationPolicyVersion = "pe_v1",
            PeriodType = nameof(FiscalPeriodType.TrailingTwelveMonths),
            PeriodStart = new DateOnly(2025, 4, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            Value = value, Unit = "Ratio",
            ObservedAt = Now, LastSynchronizedAt = Now,
            WarningsJson = "[]", SourceEvidenceJson = "[]", DependencyEvidenceJson = "[]"
        });
    }

    private static ScannerQueryPlan MakePlan(string metricCode, decimal threshold) =>
        new(Guid.NewGuid(), "test query", "en", [MakeCondition(metricCode, threshold)],
            [], false, null, [], [], Now, "v1");

    private static ScannerCondition MakeCondition(string metricCode, decimal threshold) =>
        new(
            new ScannerMetricReference(
                metricCode, new MetricCode(metricCode), new MetricVersion("v1"),
                new CalculationPolicyVersion($"{metricCode}_v1"),
                FiscalPeriodType.TrailingTwelveMonths, null),
            ConditionOperator.LessThan, threshold, FilterOrigin.Explicit);

    private sealed class RecordingCollector : IMissingAnswerFeedbackCollector
    {
        public List<MissingAnswerFeedbackRequest> Collected { get; } = new();

        public Task CollectAsync(MissingAnswerFeedbackRequest request, CancellationToken cancellationToken)
        {
            Collected.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCollector : IMissingAnswerFeedbackCollector
    {
        public Task CollectAsync(MissingAnswerFeedbackRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("collector down");
    }

    private sealed class StubMetricRegistry(IReadOnlyCollection<string> registered) : IFinancialMetricRegistry
    {
        private readonly HashSet<string> _codes = registered.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public FinancialMetricDefinition ResolveDefinition(MetricCode metricCode, DateOnly asOf)
        {
            if (!_codes.Contains(metricCode.Value))
                throw new KeyNotFoundException(metricCode.Value);
            return new FinancialMetricDefinition(
                metricCode, new MetricVersion("v1"), metricCode.Value, metricCode.Value,
                MetricCategory.Valuation, new MetricUnit("Ratio", "Ratio"),
                new DateOnly(2020, 1, 1), null,
                [FiscalPeriodType.TrailingTwelveMonths], [], [], []);
        }

        public IFinancialMetricCalculator ResolveCalculator(MetricCode metricCode) =>
            throw new NotSupportedException();

        public IReadOnlyCollection<FinancialMetricDefinition> GetSupportedMetrics(DateOnly asOf) => [];
    }

    private sealed class EmptyQuoteResolver : IMarketQuoteResolver
    {
        public Task<BatchMarketQuoteResult> ResolveAsync(
            IReadOnlyCollection<SymbolCode> symbols,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BatchMarketQuoteResult([], []));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
