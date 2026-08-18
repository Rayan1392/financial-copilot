using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationSourceBarrierTests
{
    private static readonly Guid Industry = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid Company = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly DateTimeOffset CalculatedAt = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Barrier_SelectsLatestValidPersistedObservationIndependentlyPerMetric()
    {
        var members = new[] { Member() };
        var facts = new[]
        {
            Fact(RelativeValuationMetric.Pe, 5m, 7m, "pe-old", CalculatedAt.AddDays(-2), CalculatedAt.AddHours(-3)),
            Fact(RelativeValuationMetric.Pe, 6m, 8m, "pe-new", CalculatedAt.AddDays(-10), CalculatedAt.AddHours(-1)),
            Fact(RelativeValuationMetric.Ps, 2m, 3m, "ps-new", CalculatedAt.AddDays(-20), CalculatedAt.AddHours(-2)),
            Fact(RelativeValuationMetric.Equilibrium, 100m, 120m, "eq-new", CalculatedAt.AddDays(-30), CalculatedAt.AddHours(-4))
        };

        var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(
            members, facts, CalculatedAt, TimeSpan.FromHours(26));

        Assert.True(barrier.IsComplete);
        Assert.Equal(3, barrier.Selections.Count);
        Assert.Equal("pe-new", Selection(barrier, RelativeValuationMetric.Pe).SourceObservationId);
        Assert.Equal("ps-new", Selection(barrier, RelativeValuationMetric.Ps).SourceObservationId);
        Assert.Equal("eq-new", Selection(barrier, RelativeValuationMetric.Equilibrium).SourceObservationId);
        Assert.Equal(facts[1].PersistedAtUtc, Selection(barrier, RelativeValuationMetric.Pe).PersistedAtUtc);
        Assert.NotEqual(
            Selection(barrier, RelativeValuationMetric.Pe).SourceObservationTimestamp,
            Selection(barrier, RelativeValuationMetric.Ps).SourceObservationTimestamp);
    }

    [Fact]
    public void Barrier_UsesOlderValidFactWhenNewestPersistedFactIsInvalid()
    {
        var valid = Fact(RelativeValuationMetric.Pe, 5m, 7m, "valid", CalculatedAt.AddDays(-2), CalculatedAt.AddHours(-2));
        var invalidNewest = Fact(RelativeValuationMetric.Pe, 0m, 7m, "invalid-new", CalculatedAt.AddDays(-1), CalculatedAt.AddHours(-1));

        var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(
            new[] { Member() },
            new[]
            {
                valid,
                invalidNewest,
                Fact(RelativeValuationMetric.Ps, 2m, 3m, "ps", CalculatedAt.AddDays(-3), CalculatedAt.AddHours(-3)),
                Fact(RelativeValuationMetric.Equilibrium, 100m, 120m, "eq", CalculatedAt.AddDays(-4), CalculatedAt.AddHours(-4))
            },
            CalculatedAt,
            TimeSpan.FromHours(26));

        Assert.True(barrier.IsComplete);
        Assert.Equal("valid", Selection(barrier, RelativeValuationMetric.Pe).SourceObservationId);
    }

    [Fact]
    public void Barrier_IsStableForInputOrderingAndPersistsExactEvidence()
    {
        var facts = new[]
        {
            Fact(RelativeValuationMetric.Pe, 5m, 7m, "pe", CalculatedAt.AddDays(-2), CalculatedAt.AddHours(-1), "pe-version", "pe-watermark"),
            Fact(RelativeValuationMetric.Ps, 2m, 3m, "ps", CalculatedAt.AddDays(-3), CalculatedAt.AddHours(-2), "ps-version", "ps-watermark"),
            Fact(RelativeValuationMetric.Equilibrium, 100m, 120m, "eq", CalculatedAt.AddDays(-4), CalculatedAt.AddHours(-3), "eq-version", "eq-watermark")
        };

        var first = IndustryRelativeValuationSourceBarrierBuilder.Build(new[] { Member() }, facts, CalculatedAt, TimeSpan.FromHours(26));
        var second = IndustryRelativeValuationSourceBarrierBuilder.Build(new[] { Member() }, facts.Reverse(), CalculatedAt, TimeSpan.FromHours(26));

        Assert.Equal(first.SourceBarrierHash, second.SourceBarrierHash);
        Assert.Equal(first.Selections, second.Selections);
        Assert.Equal("pe-version", Selection(first, RelativeValuationMetric.Pe).SourceVersion);
        Assert.Equal("ps-watermark", Selection(first, RelativeValuationMetric.Ps).SourceWatermark);
        Assert.Equal(CalculatedAt.AddHours(-3), Selection(first, RelativeValuationMetric.Equilibrium).PersistedAtUtc);
    }

    [Fact]
    public void Barrier_RecordsOnlyUsableProvenanceWithoutTreatingMissingMetricsAsIncomplete()
    {
        var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(
            new[] { Member() },
            new[]
            {
                Fact(RelativeValuationMetric.Pe, 5m, 7m, "pe", CalculatedAt.AddDays(-2), CalculatedAt.AddHours(-1)),
                Fact(RelativeValuationMetric.Ps, 2m, 3m, "ps", CalculatedAt.AddDays(-3), CalculatedAt.AddHours(-2)),
                Fact(RelativeValuationMetric.Equilibrium, 100m, 120m, "eq-stale", CalculatedAt.AddDays(-4), CalculatedAt.AddHours(-27))
            },
            CalculatedAt,
            TimeSpan.FromHours(26));

        Assert.True(barrier.IsComplete);
        Assert.Null(barrier.IncompleteReason);
        Assert.Equal(2, barrier.Selections.Count);
        Assert.Equal(barrier.Selections.Count, barrier.RequiredSelectionCount);
    }

    [Fact]
    public void Barrier_ExcludesFutureAndOverflowingObservationsFromProvenance()
    {
        var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(
            new[] { Member() },
            new[]
            {
                Fact(RelativeValuationMetric.Pe, 5m, 7m, "future", CalculatedAt, CalculatedAt.AddMinutes(1)),
                Fact(RelativeValuationMetric.Ps, decimal.MaxValue, 0.1m, "overflow", CalculatedAt, CalculatedAt),
                Fact(RelativeValuationMetric.Equilibrium, 100m, 120m, "usable", CalculatedAt, CalculatedAt)
            },
            CalculatedAt,
            TimeSpan.FromHours(26));

        var selected = Assert.Single(barrier.Selections);
        Assert.Equal(RelativeValuationMetric.Equilibrium, selected.Metric);
        Assert.True(barrier.IsComplete);
        Assert.Equal(1, barrier.RequiredSelectionCount);
    }

    [Fact]
    public async Task SnapshotWriter_PersistsExactSourceEvidenceForEachMetric()
    {
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var facts = new[]
        {
            Fact(RelativeValuationMetric.Pe, 5m, 7m, "pe-observation", CalculatedAt.AddDays(-2), CalculatedAt.AddHours(-1), "pe-version", "pe-watermark"),
            Fact(RelativeValuationMetric.Ps, 2m, 3m, "ps-observation", CalculatedAt.AddDays(-3), CalculatedAt.AddHours(-2), "ps-version", "ps-watermark"),
            Fact(RelativeValuationMetric.Equilibrium, 100m, 120m, "eq-observation", CalculatedAt.AddDays(-4), CalculatedAt.AddHours(-3), "eq-version", "eq-watermark")
        };
        var members = new[] { Member() };
        var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(members, facts, CalculatedAt, TimeSpan.FromHours(26));
        var result = IndustryRelativeValuationEngine.Calculate(members, barrier.SelectedFacts, new("NADPCO", CalculatedAt, TimeSpan.FromHours(26)));
        var input = new IndustryRelativeValuationCalculationInput(
            Industry, "group-101", "Group", members, barrier, result,
            Industry, "industry-101", "Industry");

        var written = await new IndustryRelativeValuationCalculationSnapshotWriter(db)
            .WriteAsync(new(2026, 8, 11), input, CalculatedAt, CancellationToken.None);
        var calculation = await db.IndustryRelativeValuationCalculations.SingleAsync();
        var company = await db.CompanyIndustryRelativeValuations.SingleAsync();

        Assert.Equal(written.CalculationId, calculation.Id);
        Assert.Contains("pe-observation", calculation.SourceBarrierEvidenceJson);
        Assert.Contains("pe-version", calculation.SourceBarrierEvidenceJson);
        Assert.Contains("ps-watermark", calculation.SourceBarrierEvidenceJson);
        Assert.Equal("eq-observation", company.EquilibriumSourceObservationId);
        Assert.Equal("eq-version", company.EquilibriumSourceVersion);
        Assert.Equal(CalculatedAt.AddHours(-3), company.EquilibriumPersistedAtUtc);
    }

    private static CanonicalIndustryMember Member() =>
        new(Company, Industry, "industry-101", "Industry");

    private static RelativeValuationSourceSelection Selection(
        RelativeValuationSourceBarrier barrier,
        RelativeValuationMetric metric) =>
        barrier.Selections.Single(x => x.Metric == metric);

    private static RelativeValuationSourceFact Fact(
        RelativeValuationMetric metric,
        decimal current,
        decimal reference,
        string observationId,
        DateTimeOffset observationTimestamp,
        DateTimeOffset persistedAt,
        string? version = null,
        string? watermark = null) =>
        new(Company, metric, current, reference, true, true, true,
            observationTimestamp, persistedAt, observationId, Guid.NewGuid(), version, watermark);
}
