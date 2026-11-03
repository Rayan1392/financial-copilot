using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class Feature126Slice3OwnershipTests
{
    [Fact]
    public void ActivationGuard_AllowsSafeOwnerStates()
    {
        var result = Feature126ActivationGuard.EvaluateActivation(
            "rev-126", "deployment-1", new(true, false, false));

        Assert.True(result.Allowed);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void ActivationGuard_RejectsMissingRevisionAndDeployment()
    {
        Assert.Equal(
            Feature126ActivationRejectionReason.MissingConfigurationRevision,
            Feature126ActivationGuard.EvaluateActivation(" ", "deployment-1", new(true, false, false)).RejectionReason);
        Assert.Equal(
            Feature126ActivationRejectionReason.MissingDeploymentIdentifier,
            Feature126ActivationGuard.EvaluateActivation("rev-126", null, new(true, false, false)).RejectionReason);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ActivationGuard_RejectsConflictingOwners(bool feature126, bool legacy, bool nadpco)
    {
        var result = Feature126ActivationGuard.EvaluateActivation("rev-126", "deployment-1", new(feature126, legacy, nadpco));

        Assert.False(result.Allowed);
        Assert.Equal(Feature126ActivationRejectionReason.ConflictingOwnerActivation, result.RejectionReason);
    }

    [Fact]
    public async Task ValidHandoff_IsAcceptedAndExecutesExactlyOneDownstreamAction()
    {
        var package = Package();
        var calls = 0;
        var result = await Feature125HandoffValidationBoundary.ExecuteAsync(
            new Feature125HandoffConsumer(), package, Lease(package), package.SourceSnapshotEvidence,
            DateTimeOffset.UtcNow, () => { calls++; return Task.FromResult("published"); });

        Assert.Equal("published", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AdmittedUniverseManifest_IsCompleteDeterministicAndExcludesOtherFacts()
    {
        var admitted = new[]
        {
            new RelativeValuationEligibleSymbol("B-ISIN", Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new RelativeValuationEligibleSymbol("A-ISIN", Guid.Parse("11111111-1111-1111-1111-111111111111"))
        };
        var available = new[]
        {
            new Feature126SourceFactEvidence(admitted[0].CompanyId!.Value, RelativeValuationSourceKind.PSGauge, Guid.NewGuid(), "v1"),
            new Feature126SourceFactEvidence(Guid.Parse("33333333-3333-3333-3333-333333333333"), RelativeValuationSourceKind.PSGauge, Guid.NewGuid(), "outside")
        };

        var first = Feature126SourceSnapshotEvidence.CreateForAdmittedUniverse(new(2026, 8, 12), admitted, available);
        var second = Feature126SourceSnapshotEvidence.CreateForAdmittedUniverse(new(2026, 8, 12), admitted.Reverse(), available.Reverse());

        Assert.Equal(6, first.Facts.Count);
        Assert.Contains(first.Facts, x => x.SymbolIsin == "A-ISIN" && x.IsMissing);
        Assert.DoesNotContain(first.Facts, x => x.CompanyId == Guid.Parse("33333333-3333-3333-3333-333333333333"));
        Assert.Equal(first.Digest, second.Digest);
        Assert.All(first.Facts, x => Assert.Equal(x.SymbolIsin, admitted.Single(a => a.CompanyId == x.CompanyId).SymbolIsin));
    }

    [Fact]
    public void HandoffManifest_MissingMetric_IsAcceptedByTheHandoffPolicy()
    {
        var company = Guid.NewGuid();
        var snapshot = Feature126SourceSnapshotEvidence.CreateForAdmittedUniverse(
            new(2026, 8, 12),
            [new RelativeValuationEligibleSymbol("MISSING-ISIN", company)],
            [new Feature126SourceFactEvidence(company, RelativeValuationSourceKind.PSGauge, Guid.NewGuid(), "v1")]);
        var package = new Feature126HandoffPackage(
            new("missing-metric", snapshot.TehranCalculationDate), Guid.NewGuid(), snapshot);

        var result = new Feature125HandoffConsumer().Validate(
            package,
            new("feature126", snapshot.TehranCalculationDate, LeaseState.Handoff,
                package.FencingToken, DateTimeOffset.UtcNow.AddMinutes(5)),
            snapshot,
            DateTimeOffset.UtcNow);

        Assert.True(package.IsComplete);
        Assert.True(result.Accepted);
    }

    [Fact]
    public async Task RuntimeValidation_UsesManifestProjection_NotLegacyBroadSnapshot()
    {
        var package = PackageWithSymbol();
        var facts = new ManifestAwareSnapshotFacts(package.SourceSnapshotEvidence);
        var service = new IndustryRelativeValuationOrchestrationService(
            null!, null!, null!, Options.Create(new IndustryRelativeValuationOptions { Enabled = false }),
            Options.Create(new IndustryRelativeValuationSourceOptions()), TimeProvider.System,
            NullLogger<IndustryRelativeValuationOrchestrationService>.Instance, facts,
            new Feature125HandoffConsumer());

        var result = await service.SubmitAsync(package, Lease(package), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(0, facts.LegacyReads);
        Assert.Equal(2, facts.ManifestReads);
    }

    [Fact]
    public async Task SideEffectFenceRejectsTakeoverAfterValidation()
    {
        var package = Package();
        var calls = 0;
        var result = await Feature125HandoffValidationBoundary.ExecuteAsync(
            new Feature125HandoffConsumer(), package, Lease(package), package.SourceSnapshotEvidence,
            DateTimeOffset.UtcNow, () => { calls++; return Task.FromResult("must-not-write"); },
            sideEffectFence: () => Task.FromResult(false));

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(Feature125HandoffRejectionReason.StaleFencingToken)]
    [InlineData(Feature125HandoffRejectionReason.ChangedSnapshot)]
    [InlineData(Feature125HandoffRejectionReason.IncompletePackage)]
    public async Task InvalidHandoff_ProducesZeroDownstreamSideEffects(Feature125HandoffRejectionReason reason)
    {
        var package = Package();
        var consumer = new Feature125HandoffConsumer();
        var lease = Lease(package);
        Feature126SourceSnapshotEvidence snapshot = package.SourceSnapshotEvidence;

        switch (reason)
        {
            case Feature125HandoffRejectionReason.StaleFencingToken:
                lease = lease with { FencingToken = Guid.NewGuid() };
                break;
            case Feature125HandoffRejectionReason.ChangedSnapshot:
                snapshot = Feature126SourceSnapshotEvidence.Create(package.RunIdentity.TehranCalculationDate, []);
                break;
            case Feature125HandoffRejectionReason.IncompletePackage:
                package = package with { FencingToken = Guid.Empty };
                break;
        }

        var calls = 0;
        var result = await Feature125HandoffValidationBoundary.ExecuteAsync(
            consumer, package, lease, snapshot, DateTimeOffset.UtcNow,
            () => { calls++; return Task.FromResult("must-not-run"); });

        Assert.Null(result);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(Feature125HandoffRejectionReason.StaleFencingToken)]
    [InlineData(Feature125HandoffRejectionReason.ChangedSnapshot)]
    [InlineData(Feature125HandoffRejectionReason.IncompletePackage)]
    public async Task RuntimeFeature125Boundary_RejectsBeforeCalculation(Feature125HandoffRejectionReason reason)
    {
        var package = Package();
        var current = package.SourceSnapshotEvidence;
        var lease = Lease(package);
        switch (reason)
        {
            case Feature125HandoffRejectionReason.StaleFencingToken:
                lease = lease with { FencingToken = Guid.NewGuid() };
                break;
            case Feature125HandoffRejectionReason.ChangedSnapshot:
                current = Feature126SourceSnapshotEvidence.Create(package.RunIdentity.TehranCalculationDate, []);
                break;
            case Feature125HandoffRejectionReason.IncompletePackage:
                package = package with { FencingToken = Guid.Empty };
                break;
        }

        var service = new IndustryRelativeValuationOrchestrationService(
            null!, null!, null!,
            Options.Create(new IndustryRelativeValuationOptions { Enabled = true }),
            Options.Create(new IndustryRelativeValuationSourceOptions()),
            TimeProvider.System,
            NullLogger<IndustryRelativeValuationOrchestrationService>.Instance,
            new InMemorySnapshotFacts(current),
            new Feature125HandoffConsumer());

        var result = await service.SubmitAsync(package, lease, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.Equal(reason, result.RejectionReason);
    }

    [Fact]
    public async Task RuntimeFeature125Boundary_AcceptsValidHandoff()
    {
        var package = Package();
        var service = new IndustryRelativeValuationOrchestrationService(
            null!, null!, null!,
            Options.Create(new IndustryRelativeValuationOptions { Enabled = false }),
            Options.Create(new IndustryRelativeValuationSourceOptions()),
            TimeProvider.System,
            NullLogger<IndustryRelativeValuationOrchestrationService>.Instance,
            new InMemorySnapshotFacts(package.SourceSnapshotEvidence),
            new Feature125HandoffConsumer());

        var result = await service.SubmitAsync(
            package, Lease(package), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result.Accepted);
    }

    private static Feature126HandoffPackage Package() => Feature126HandoffPackage.Create(
        new Feature126RunIdentity("correlation-1", new DateOnly(2026, 8, 12)), Guid.NewGuid(),
        [new Feature126SourceFactEvidence(Guid.Parse("11111111-1111-1111-1111-111111111111"), RelativeValuationSourceKind.PSGauge, Guid.NewGuid(), "v1")]);

    private static Feature126HandoffPackage PackageWithSymbol()
    {
        var company = Guid.NewGuid();
        var snapshot = Feature126SourceSnapshotEvidence.CreateForAdmittedUniverse(
            new(2026, 8, 12), [new RelativeValuationEligibleSymbol("MANIFEST-ISIN", company)], []);
        return new(new("manifest", snapshot.TehranCalculationDate), Guid.NewGuid(), snapshot);
    }

    private static Feature126HandoffLeaseState Lease(Feature126HandoffPackage package) =>
        new("feature126", package.RunIdentity.TehranCalculationDate, LeaseState.Handoff,
            package.FencingToken, DateTimeOffset.UtcNow.AddMinutes(5));

    private sealed class InMemorySnapshotFacts(Feature126SourceSnapshotEvidence snapshot) : IFeature126SourceFactStore
    {
        public Task<Feature126SourceFactWriteResult> PersistAcceptedAsync(
            Guid _, RelativeValuationProviderResult __, LeaseHandle ___, CancellationToken ____) =>
            Task.FromResult(Feature126SourceFactWriteResult.Persisted);

        public Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(
            DateOnly _, CancellationToken __) => Task.FromResult(snapshot);
    }

    private sealed class ManifestAwareSnapshotFacts(Feature126SourceSnapshotEvidence snapshot) : IFeature126SourceFactStore
    {
        public int LegacyReads { get; private set; }
        public int ManifestReads { get; private set; }
        public Task<Feature126SourceFactWriteResult> PersistAcceptedAsync(Guid _, RelativeValuationProviderResult __, LeaseHandle ___, CancellationToken ____) =>
            Task.FromResult(Feature126SourceFactWriteResult.Persisted);
        public Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(DateOnly _, CancellationToken __)
        {
            LegacyReads++;
            return Task.FromResult(Feature126SourceSnapshotEvidence.Create(snapshot.TehranCalculationDate, []));
        }
        public Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(DateOnly _, IReadOnlyList<RelativeValuationEligibleSymbol> __, CancellationToken ___)
        {
            ManifestReads++;
            return Task.FromResult(snapshot);
        }
    }
}
