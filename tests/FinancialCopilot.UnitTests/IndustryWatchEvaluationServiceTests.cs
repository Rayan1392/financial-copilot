using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryWatchEvaluationServiceTests
{
    private static readonly Guid Industry = Guid.Parse("00000000-0000-0000-0000-000000000901");
    private static readonly DateOnly Date = new(2026, 8, 11);
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_selected_published_calculation_advances_and_replay_is_idempotent()
    {
        var database = Guid.NewGuid().ToString("N");
        var pending = await SeedCalculation(database, "pending", "Pending", false, 1, 99m);
        await using (var db = Context(database))
        {
            var ignored = await new IndustryWatchEvaluationService(db, new(1, 1))
                .EvaluateAsync(Industry, pending, "Daily", Now);
            Assert.Null(ignored);
        }

        var published = await SeedCalculation(database, "published", "Published", true, 2, 99m);
        await using (var db = Context(database))
        {
            var service = new IndustryWatchEvaluationService(db, new(1, 1));
            var first = await service.EvaluateAsync(Industry, published, "Daily", Now);
            var replay = await service.EvaluateAsync(Industry, published, "Daily", Now.AddMinutes(1));
            Assert.False(first!.NoOp);
            Assert.Equal(IndustryWatchState.Watching, first.State);
            Assert.True(replay!.NoOp);
        }

        await using var verify = Context(database);
        Assert.Equal(1, await verify.IndustryWatchEvaluations.CountAsync());
        Assert.Equal(1, (await verify.IndustryWatchStates.SingleAsync()).EntryStreak);
    }

    [Fact]
    public async Task Corrected_same_date_selected_version_does_not_create_second_watch_day()
    {
        var database = Guid.NewGuid().ToString("N");
        var first = await SeedCalculation(database, "first", "Published", true, 1, 99m);
        await using (var db = Context(database))
            _ = await new IndustryWatchEvaluationService(db, new(3, 3)).EvaluateAsync(Industry, first, "Daily", Now);

        var corrected = await SeedCalculation(database, "corrected", "Published", true, 2, 98m);
        await using (var db = Context(database))
        {
            var result = await new IndustryWatchEvaluationService(db, new(3, 3))
                .EvaluateAsync(Industry, corrected, "Daily", Now.AddHours(1));
            Assert.False(result!.NoOp);
        }

        await using var verify = Context(database);
        Assert.Equal(2, await verify.IndustryWatchEvaluations.CountAsync());
        Assert.Equal(1, (await verify.IndustryWatchStates.SingleAsync()).EntryStreak);
    }

    private static async Task<Guid> SeedCalculation(string database, string name, string status, bool selected, int version, decimal benchmark)
    {
        await using var db = Context(database);
        var id = Guid.NewGuid();
        db.IndustryRelativeValuationCalculations.Add(new()
        {
            Id = id, IndustryId = Industry, CalculationDate = Date, CalculationVersion = version,
            Status = status, IsSelectedCurrent = selected, IsLatestEvaluation = true,
            IndustryExternalId = "industry-901", IndustryTitleSnapshot = "Industry",
            AlgorithmVersion = "calc-v1", MembershipHash = "membership", SourceBarrierHash = $"source-{name}", SourceBarrierEvidenceJson = "{}", CalculatedAtUtc = Now
        });
        foreach (var metric in new[] { "Pe", "Ps", "Equilibrium" })
            db.IndustryRelativeValuationMetrics.Add(new()
            {
                Id = Guid.NewGuid(), CalculationId = id, MetricKind = metric,
                CleanCount = 2, CleanAverage = benchmark, Readiness = "Ready", Reason = ""
            });
        await db.SaveChangesAsync();
        return id;
    }

    private static FinancialIngestionDbContext Context(string database) =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(database).Options);
}
