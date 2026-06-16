using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using System.Text.Json;

namespace FinancialCopilot.UnitTests;

public sealed class DerivedFeatureFoundationTests
{
    [Fact]
    public void FutureFeatureCodes_ExposeGovernedCandidateVocabularyWithoutFormulaImplementations()
    {
        var codes = FutureFeatureCodes.SupportedCandidates.Select(feature => feature.Value).ToArray();

        Assert.Contains("MOMENTUM_SCORE", codes);
        Assert.Contains("EARNINGS_QUALITY_SCORE", codes);
        Assert.Contains("RELATIVE_STRENGTH", codes);
        Assert.Contains("VOLATILITY_SCORE", codes);
        Assert.Contains("LIQUIDITY_SCORE", codes);
        Assert.Contains("GROWTH_CONSISTENCY", codes);
        Assert.Contains("SMART_MONEY_SIGNAL", codes);
    }

    [Fact]
    public async Task CalculationService_PersistsDeterministicVersionedSnapshotEvidence()
    {
        var definition = Definition();
        var repository = new CapturingRepository();
        var service = new DerivedFeatureCalculationService(
            new InMemoryRegistry(definition),
            [new DeterministicTestCalculator()],
            repository);
        var command = Command(definition, [Input(definition.Dependencies.Single(), 25m)]);

        var first = await service.CalculateAsync(command, CancellationToken.None);
        var second = await service.CalculateAsync(command, CancellationToken.None);

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        Assert.Equal("v1", first.Feature.Version.Value);
        Assert.Equal("feature-policy-v1", first.Feature.PolicyVersion.Value);
        Assert.Equal("PE_TTM", first.DependencyEvidence.Single().Code);
        Assert.Same(second, repository.LastSnapshot);
    }

    [Fact]
    public async Task CalculationService_RejectsMissingRequiredVersionedInput()
    {
        var definition = Definition();
        var service = new DerivedFeatureCalculationService(
            new InMemoryRegistry(definition),
            [new DeterministicTestCalculator()],
            new CapturingRepository());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CalculateAsync(Command(definition, []), CancellationToken.None));

        Assert.Contains("PE_TTM", exception.Message);
    }

    [Fact]
    public void RecalculationMessagePeriod_RoundTripsAsValidatedClosedPeriod()
    {
        var request = new FeatureRecalculationRequested(
            Guid.NewGuid(),
            new FeatureCode("MOMENTUM_SCORE"),
            new FeatureVersion("v1"),
            "EXT-TEST",
            FeatureComputationPeriod.From(Period()),
            "job-period-round-trip",
            DateTimeOffset.Parse("2026-05-27T10:00:00Z"));

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = JsonSerializer.Deserialize<FeatureRecalculationRequested>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        Assert.Equal(Period(), restored.Period.ToFiscalPeriod());
    }

    private static FeatureDefinition Definition() =>
        new(
            new FeatureCode("MOMENTUM_SCORE"),
            new FeatureVersion("v1"),
            "Momentum score",
            "Test-only deterministic promoted implementation.",
            new CalculationPolicyVersion("feature-policy-v1"),
            12,
            new FeatureOutputSpecification(MetricValueUnit.Ratio, 0m, 100m),
            [FeatureDependency.Metric(new MetricCode("PE_TTM"), new MetricVersion("v1"))],
            new FeatureReproducibilityMetadata("test-calculator", "v1", "inputs-v1"));

    private static CalculateDerivedFeatureCommand Command(
        FeatureDefinition definition,
        IReadOnlyCollection<FeatureInputObservation> inputs) =>
        new(
            "EXT-FE6132A3",
            definition.Code,
            definition.Version,
            Period(),
            inputs);

    private static FeatureInputObservation Input(FeatureDependency dependency, decimal value) =>
        new(dependency, value, Period(), "metric-evidence-v1");

    private static FiscalPeriod Period() =>
        FiscalPeriod.Closed(
            FiscalPeriodType.TrailingTwelveMonths,
            new DateOnly(2025, 5, 1),
            new DateOnly(2026, 4, 30));

    private sealed class InMemoryRegistry(FeatureDefinition definition) : IFeatureDefinitionRegistry
    {
        public Task<FeatureDefinition> ResolveAsync(
            FeatureCode code,
            FeatureVersion version,
            CancellationToken cancellationToken) =>
            Task.FromResult(definition);

        public Task RegisterAsync(FeatureDefinition featureDefinition, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CapturingRepository : IFeatureSnapshotRepository
    {
        public FeatureSnapshot? LastSnapshot { get; private set; }

        public Task StoreAsync(FeatureSnapshot snapshot, CancellationToken cancellationToken)
        {
            LastSnapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class DeterministicTestCalculator : IDerivedFeatureCalculator
    {
        public FeatureCode FeatureCode => new("MOMENTUM_SCORE");

        public Task<FeatureSnapshot> CalculateAsync(
            FeatureCalculationContext context,
            CancellationToken cancellationToken)
        {
            var input = context.Inputs.Single();
            var fingerprint = $"{input.Dependency.Code}:{input.Dependency.RequiredVersion}:{input.Value}";
            var at = DateTimeOffset.Parse("2026-04-30T12:00:00Z");
            return Task.FromResult(new FeatureSnapshot(
                Guid.Parse("03ad4097-e838-4cc7-aa63-a08bfe6656bb"),
                context.ExternalCompanyId,
                new DerivedFeature(
                    context.Definition.Code,
                    context.Definition.Version,
                    context.Definition.PolicyVersion),
                context.Period,
                input.Value,
                context.Definition.Output.Unit,
                FinancialObservationQuality.Current(at, at),
                [new FinancialSourceEvidence("TestMetricStore", at, at)],
                [new FeatureDependencyEvidence(
                    input.Dependency.Kind,
                    input.Dependency.Code,
                    input.Dependency.RequiredVersion,
                    new CalculationPolicyVersion("metric-policy-v1"))],
                fingerprint));
        }
    }
}
