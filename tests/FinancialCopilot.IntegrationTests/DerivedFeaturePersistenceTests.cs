using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Features;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class DerivedFeaturePersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    [Fact]
    public async Task DefinitionsAndHistoricalSnapshots_RetainReproducibilityAndDependencyEvidence()
    {
        await using var db = CreateDbContext();
        var registry = new PersistedFeatureDefinitionRegistry(db);
        var repository = new PersistedFeatureSnapshotRepository(db);
        var definition = Definition();
        await registry.RegisterAsync(definition, CancellationToken.None);
        var first = Snapshot(definition, 72m, "input-sha-1", Period(2026, 3));
        var revised = Snapshot(definition, 74m, "input-sha-2", Period(2026, 3));

        await repository.StoreAsync(first, CancellationToken.None);
        await repository.StoreAsync(first, CancellationToken.None);
        await repository.StoreAsync(revised, CancellationToken.None);
        var readDefinition = await registry.ResolveAsync(definition.Code, definition.Version, CancellationToken.None);
        var results = await repository.QueryAsync(
            new FeatureSnapshotQuery([definition.Code], [first.ExternalCompanyId], new DateOnly(2026, 3, 31)),
            CancellationToken.None);

        Assert.Equal(12, readDefinition.RequiredObservationWindow);
        Assert.Equal("metrics-window-v1", readDefinition.Reproducibility.InputSchemaVersion);
        Assert.Equal(2, await db.FeatureSnapshots.CountAsync());
        Assert.Equal(2, results.Count);
        Assert.Contains(results, snapshot => snapshot.InputFingerprint == "input-sha-1");
        Assert.Contains(results, snapshot => snapshot.DependencyEvidence.Single().Code == "MONTHLY_SALES_GROWTH_MOM");
    }

    [Fact]
    public async Task ScheduledComputation_IsIdempotentAndPersistsCompletedJobAndSnapshot()
    {
        await using var db = CreateDbContext();
        var definition = Definition();
        var registry = new PersistedFeatureDefinitionRegistry(db);
        await registry.RegisterAsync(definition, CancellationToken.None);
        var repository = new PersistedFeatureSnapshotRepository(db);
        var publisher = new CapturingPublisher();
        var jobs = new PersistedFeatureComputationJobRepository(db);
        var scheduler = new FeatureRecalculationScheduler(jobs, publisher);
        var calculator = new FixedCalculator();
        var processor = new FeatureComputationProcessor(
            jobs,
            registry,
            new FixedInputReader(),
            new DerivedFeatureCalculationService(registry, [calculator], repository),
            publisher,
            new FixedTimeProvider(Now));
        var request = new FeatureRecalculationRequested(
            Guid.NewGuid(),
            definition.Code,
            definition.Version,
            "ext-2fc64d56-1320-4612-b894-511bd163914f",
            FeatureComputationPeriod.From(Period(2026, 4)),
            "feature-job-1",
            Now);

        var requested = await scheduler.ScheduleAsync(request, CancellationToken.None);
        await scheduler.ScheduleAsync(request, CancellationToken.None);
        await processor.ProcessAsync(request, CancellationToken.None);
        await processor.ProcessAsync(request, CancellationToken.None);
        var completed = await jobs.GetByIdempotencyKeyAsync(request.IdempotencyKey, CancellationToken.None);

        Assert.Equal(FeatureComputationStatus.Requested, requested.Status);
        Assert.Single(publisher.Requested);
        Assert.Single(publisher.Completed);
        Assert.Empty(publisher.Failed);
        Assert.Equal(FeatureComputationStatus.Completed, completed!.Status);
        Assert.Single(await db.FeatureSnapshots.ToListAsync());
    }

    [Fact]
    public async Task MissingRequiredInput_RecordsFailedJobAndFailureNotification()
    {
        await using var db = CreateDbContext();
        var definition = Definition();
        var registry = new PersistedFeatureDefinitionRegistry(db);
        await registry.RegisterAsync(definition, CancellationToken.None);
        var publisher = new CapturingPublisher();
        var jobs = new PersistedFeatureComputationJobRepository(db);
        var processor = new FeatureComputationProcessor(
            jobs,
            registry,
            new EmptyInputReader(),
            new DerivedFeatureCalculationService(registry, [new FixedCalculator()], new PersistedFeatureSnapshotRepository(db)),
            publisher,
            new FixedTimeProvider(Now));
        var request = new FeatureRecalculationRequested(
            Guid.NewGuid(),
            definition.Code,
            definition.Version,
            "ext-missing-company-1",
            FeatureComputationPeriod.From(Period(2026, 4)),
            "missing-input-job",
            Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessAsync(request, CancellationToken.None));
        var failed = await jobs.GetByIdempotencyKeyAsync(request.IdempotencyKey, CancellationToken.None);

        Assert.Equal(FeatureComputationStatus.Failed, failed!.Status);
        Assert.Contains("MONTHLY_SALES_GROWTH_MOM", failed.ErrorMessage);
        Assert.Single(publisher.Failed);
    }

    private static FeatureDefinition Definition() =>
        new(
            new FeatureCode("GROWTH_CONSISTENCY"),
            new FeatureVersion("v1"),
            "Growth consistency",
            "Integration fixture for feature foundation persistence.",
            new CalculationPolicyVersion("growth-consistency-v1"),
            12,
            new FeatureOutputSpecification(MetricValueUnit.Ratio, 0m, 100m),
            [FeatureDependency.Metric(new MetricCode("MONTHLY_SALES_GROWTH_MOM"), new MetricVersion("v1"))],
            new FeatureReproducibilityMetadata("fixture-only", "v1", "metrics-window-v1"));

    private static FeatureSnapshot Snapshot(
        FeatureDefinition definition,
        decimal value,
        string fingerprint,
        FiscalPeriod period)
    {
        var source = new FinancialSourceEvidence("DerivedMetricStore", Now, Now);
        return new FeatureSnapshot(
            Guid.NewGuid(),
            "ext-2fc64d56-1320-4612-b894-511bd163914f",
            new DerivedFeature(definition.Code, definition.Version, definition.PolicyVersion),
            period,
            value,
            definition.Output.Unit,
            FinancialObservationQuality.Current(Now, Now),
            [source],
            [new FeatureDependencyEvidence(
                FeatureDependencyKind.Metric,
                "MONTHLY_SALES_GROWTH_MOM",
                "v1",
                new CalculationPolicyVersion("mom-monthly-sales-v1"))],
            fingerprint);
    }

    private static FiscalPeriod Period(int year, int month) =>
        FiscalPeriod.Closed(
            FiscalPeriodType.Monthly,
            new DateOnly(year, month, 1),
            new DateOnly(year, month, 1).AddMonths(1).AddDays(-1));

    private static FinancialIngestionDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedInputReader : IFeatureInputReader
    {
        public Task<IReadOnlyCollection<FeatureInputObservation>> LoadAsync(
            FeatureDefinition definition,
            string externalCompanyId,
            FiscalPeriod period,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FeatureInputObservation>>(
            [
                new(definition.Dependencies.Single(), 32m, period, "metric-result-sha-1")
            ]);
    }

    private sealed class EmptyInputReader : IFeatureInputReader
    {
        public Task<IReadOnlyCollection<FeatureInputObservation>> LoadAsync(
            FeatureDefinition definition,
            string externalCompanyId,
            FiscalPeriod period,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FeatureInputObservation>>([]);
    }

    private sealed class FixedCalculator : IDerivedFeatureCalculator
    {
        public FeatureCode FeatureCode => new("GROWTH_CONSISTENCY");

        public Task<FeatureSnapshot> CalculateAsync(FeatureCalculationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot(context.Definition, 80m, "metric-result-sha-1", context.Period));
    }

    private sealed class CapturingPublisher : IFeatureRecalculationPublisher
    {
        public List<FeatureRecalculationRequested> Requested { get; } = [];

        public List<FeatureRecalculationCompleted> Completed { get; } = [];

        public List<FeatureRecalculationFailed> Failed { get; } = [];

        public Task PublishRequestedAsync(FeatureRecalculationRequested request, CancellationToken cancellationToken)
        {
            Requested.Add(request);
            return Task.CompletedTask;
        }

        public Task PublishCompletedAsync(FeatureRecalculationCompleted notification, CancellationToken cancellationToken)
        {
            Completed.Add(notification);
            return Task.CompletedTask;
        }

        public Task PublishFailedAsync(FeatureRecalculationFailed notification, CancellationToken cancellationToken)
        {
            Failed.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
