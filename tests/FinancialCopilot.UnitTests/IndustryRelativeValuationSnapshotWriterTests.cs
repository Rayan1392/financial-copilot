using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationSnapshotWriterTests
{
    private static readonly Guid Industry = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid Company = Guid.Parse("00000000-0000-0000-0000-000000000202");
    private static readonly Guid CompanyTwo = Guid.Parse("00000000-0000-0000-0000-000000000203");
    private static readonly DateTimeOffset CalculatedAt = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentWritersForSameIdentityAllocateDistinctVersions()
    {
        var database = Guid.NewGuid().ToString("N");
        var first = WriteAsync(database, "pe-1", complete: true);
        var second = WriteAsync(database, "pe-2", complete: true);

        var results = await Task.WhenAll(first, second);

        Assert.Equal(new[] { 1, 2 }, results.Select(x => x.CalculationVersion).OrderBy(x => x));
        await using var db = CreateContext(database);
        Assert.Equal(2, await db.IndustryRelativeValuationCalculations.CountAsync());
    }

    [Fact]
    public async Task DuplicateRequestIsIdempotentAndRetryReturnsWinningVersion()
    {
        var database = Guid.NewGuid().ToString("N");
        var first = await WriteAsync(database, "same", complete: true);
        var duplicate = await WriteAsync(database, "same", complete: true);
        var retry = await WriteAsync(database, "same", complete: true);

        Assert.False(first.NoOp);
        Assert.True(duplicate.NoOp);
        Assert.True(retry.NoOp);
        Assert.Equal(first.CalculationId, duplicate.CalculationId);
        Assert.Equal(first.CalculationId, retry.CalculationId);
        Assert.Equal(1, first.CalculationVersion);
    }

    [Fact]
    public async Task InconclusiveLatestEvaluationDoesNotErasePublishedSnapshot()
    {
        var database = Guid.NewGuid().ToString("N");
        var published = await WriteAsync(database, "published", complete: true);
        var inconclusive = await WriteAsync(database, "inconclusive", complete: false);

        await using var db = CreateContext(database);
        var rows = await db.IndustryRelativeValuationCalculations
            .OrderBy(x => x.CalculationVersion).ToArrayAsync();
        Assert.Equal(published.CalculationId, rows.Single(x => x.IsSelectedCurrent).Id);
        Assert.Equal(inconclusive.CalculationId, rows.Single(x => x.IsLatestEvaluation).Id);
        Assert.Equal("Published", rows.Single(x => x.IsSelectedCurrent).Status);
        Assert.Equal("Inconclusive", rows.Single(x => x.IsLatestEvaluation).Status);
    }

    [Fact]
    public async Task FailedAndPendingRowsDoNotReplacePublishedPointer_AndSuccessfulPublishDoes()
    {
        var database = Guid.NewGuid().ToString("N");
        var published = await WriteAsync(database, "published", complete: true);
        await using (var db = CreateContext(database))
        {
            var prior = await db.IndustryRelativeValuationCalculations.SingleAsync(x => x.Id == published.CalculationId);
            prior.IsLatestEvaluation = false;
            db.IndustryRelativeValuationCalculations.AddRange(
                ManualRow(2, "Failed", latest: true),
                ManualRow(3, "Pending", latest: false));
            await db.SaveChangesAsync();
        }

        var successful = await WriteAsync(database, "successful", complete: true);

        await using var verify = CreateContext(database);
        Assert.Equal(successful.CalculationId,
            (await verify.IndustryRelativeValuationCalculations.SingleAsync(x => x.IsSelectedCurrent)).Id);
        Assert.False(await verify.IndustryRelativeValuationCalculations.AnyAsync(x => x.Status == "Pending" && x.IsSelectedCurrent));
        Assert.False(await verify.IndustryRelativeValuationCalculations.AnyAsync(x => x.Status == "Failed" && x.IsSelectedCurrent));
        Assert.Equal(4, await verify.IndustryRelativeValuationCalculations.MaxAsync(x => x.CalculationVersion));
        _ = published;
    }

    private static async Task<IndustryRelativeValuationSnapshotWriteResult> WriteAsync(
        string database, string observation, bool complete)
    {
        await using var db = CreateContext(database);
        var members = new[]
        {
            new CanonicalIndustryMember(Company, Industry, "industry-201", "Industry"),
            new CanonicalIndustryMember(CompanyTwo, Industry, "industry-201", "Industry")
        };
        var facts = new List<RelativeValuationSourceFact>
        {
            Fact(Company, RelativeValuationMetric.Pe, observation + "-pe", 5m),
            Fact(Company, RelativeValuationMetric.Ps, observation + "-ps", 2m),
            Fact(CompanyTwo, RelativeValuationMetric.Pe, observation + "-pe-2", 6m),
            Fact(CompanyTwo, RelativeValuationMetric.Ps, observation + "-ps-2", 3m)
        };
        if (complete)
        {
            facts.Add(Fact(Company, RelativeValuationMetric.Equilibrium, observation + "-eq", 100m));
            facts.Add(Fact(CompanyTwo, RelativeValuationMetric.Equilibrium, observation + "-eq-2", 110m));
        }
        var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(members, facts, CalculatedAt, TimeSpan.FromHours(26));
        var result = IndustryRelativeValuationEngine.Calculate(members, barrier.SelectedFacts,
            new("NADPCO", CalculatedAt, TimeSpan.FromHours(26)));
        return await new IndustryRelativeValuationCalculationSnapshotWriter(db).WriteAsync(
            new(2026, 8, 11), new(Industry, "group-201", "Group", members, barrier, result,
                Industry, "industry-201", "Industry"),
            CalculatedAt, CancellationToken.None);
    }

    private static IndustryRelativeValuationCalculationRow ManualRow(int version, string status, bool latest) => new()
    {
        Id = Guid.NewGuid(), CalculationDate = new(2026, 8, 11), GroupId = Industry,
        GroupExternalId = "industry-201", GroupTitleSnapshot = "Industry", IndustryId = Industry,
        IndustryExternalId = "industry-201", IndustryTitleSnapshot = "Industry", CalculationVersion = version,
        Status = status, AlgorithmVersion = IndustryRelativeValuationEngine.AlgorithmVersion,
        MembershipHash = "membership", SourceBarrierHash = $"manual-{version}", SourceBarrierEvidenceJson = "{}",
        CalculatedAtUtc = CalculatedAt, IsLatestEvaluation = latest
    };

    private static FinancialIngestionDbContext CreateContext(string database) =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(database).Options);

    private static RelativeValuationSourceFact Fact(Guid company, RelativeValuationMetric metric, string id, decimal current) =>
        new(company, metric, current, current + 1m, true, true, true, CalculatedAt.AddHours(-1), CalculatedAt.AddHours(-2), id,
            id.StartsWith("same", StringComparison.Ordinal) ? Guid.Parse("00000000-0000-0000-0000-000000000299") : Guid.NewGuid(), "v1", "watermark");
}
