using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class MissingAnswerFeedbackTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-31T09:00:00Z");

    [Fact]
    public async Task Upsert_NewEntry_PersistsRow()
    {
        await using var db = NewDb();
        var repo = new EfCoreMissingAnswerFeedbackRepository(db);
        var feedback = MakeFeedback(
            actorId: "user-1",
            queryText: "list companies with 50% revenue growth",
            classification: MissingAnswerFeedbackClassification.MetricGap);

        await repo.UpsertAsync(feedback, CancellationToken.None);

        var row = await db.MissingAnswerFeedbacks.SingleAsync();
        Assert.Equal("user-1", row.ActorId);
        Assert.Equal(1, row.FrequencyCount);
        Assert.Equal("MetricGap", row.Classification);
    }

    [Fact]
    public async Task Upsert_SameKey_CoalescesAndIncrementsFrequency()
    {
        await using var db = NewDb();
        var repo = new EfCoreMissingAnswerFeedbackRepository(db);
        var first = MakeFeedback(
            actorId: "user-1",
            queryText: "list companies with 50% revenue growth",
            classification: MissingAnswerFeedbackClassification.MetricGap);
        await repo.UpsertAsync(first, CancellationToken.None);

        var duplicate = MakeFeedback(
            actorId: "user-1",
            queryText: "list companies with 50% revenue growth",
            classification: MissingAnswerFeedbackClassification.MetricGap,
            submittedAt: Now.AddMinutes(10));
        await repo.UpsertAsync(duplicate, CancellationToken.None);

        var rows = await db.MissingAnswerFeedbacks.ToListAsync();
        var only = Assert.Single(rows); // not duplicated
        Assert.Equal(2, only.FrequencyCount);
    }

    [Fact]
    public async Task Upsert_DifferentClassificationSameQuery_KeepsSeparateRows()
    {
        await using var db = NewDb();
        var repo = new EfCoreMissingAnswerFeedbackRepository(db);
        var queryText = "find companies with X";

        await repo.UpsertAsync(MakeFeedback("user-1", queryText, MissingAnswerFeedbackClassification.MetricGap), CancellationToken.None);
        await repo.UpsertAsync(MakeFeedback("user-1", queryText, MissingAnswerFeedbackClassification.CalculationGap), CancellationToken.None);

        Assert.Equal(2, await db.MissingAnswerFeedbacks.CountAsync());
    }

    [Fact]
    public async Task Query_FiltersByActorAndClassification()
    {
        await using var db = NewDb();
        var repo = new EfCoreMissingAnswerFeedbackRepository(db);
        await repo.UpsertAsync(MakeFeedback("user-a", "q1", MissingAnswerFeedbackClassification.MetricGap), CancellationToken.None);
        await repo.UpsertAsync(MakeFeedback("user-a", "q2", MissingAnswerFeedbackClassification.CalculationGap), CancellationToken.None);
        await repo.UpsertAsync(MakeFeedback("user-b", "q3", MissingAnswerFeedbackClassification.MetricGap), CancellationToken.None);

        var userAOnly = await repo.QueryAsync(
            new MissingAnswerFeedbackQuery(ActorId: "user-a"), CancellationToken.None);
        var metricGapOnly = await repo.QueryAsync(
            new MissingAnswerFeedbackQuery(Classification: MissingAnswerFeedbackClassification.MetricGap),
            CancellationToken.None);

        Assert.Equal(2, userAOnly.Count);
        Assert.Equal(2, metricGapOnly.Count);
        Assert.All(userAOnly, item => Assert.Equal("user-a", item.ActorId));
        Assert.All(metricGapOnly, item => Assert.Equal(MissingAnswerFeedbackClassification.MetricGap, item.Classification));
    }

    [Fact]
    public async Task GetCountByClassification_SumsFrequencyCount()
    {
        await using var db = NewDb();
        var repo = new EfCoreMissingAnswerFeedbackRepository(db);
        // Coalesced into one row with FrequencyCount=3.
        for (var i = 0; i < 3; i++)
        {
            await repo.UpsertAsync(
                MakeFeedback("user-1", "same query", MissingAnswerFeedbackClassification.MetricGap),
                CancellationToken.None);
        }
        await repo.UpsertAsync(
            MakeFeedback("user-1", "other query", MissingAnswerFeedbackClassification.CalculationGap),
            CancellationToken.None);

        var counts = await repo.GetCountByClassificationAsync(null, null, CancellationToken.None);

        Assert.Equal(3, counts[MissingAnswerFeedbackClassification.MetricGap]);
        Assert.Equal(1, counts[MissingAnswerFeedbackClassification.CalculationGap]);
    }

    [Fact]
    public async Task NoOpCollector_AcceptsCallSilentlyWithoutPersisting()
    {
        await using var db = NewDb();
        var collector = new NoOpMissingAnswerFeedbackCollector();

        await collector.CollectAsync(
            new MissingAnswerFeedbackRequest(
                "user-1", "query", MissingAnswerFeedbackClassification.MetricGap,
                "REVENUE_GROWTH_YOY", "revenue growth", 100, 0, Now, null),
            CancellationToken.None);

        Assert.Equal(0, await db.MissingAnswerFeedbacks.CountAsync());
    }

    // ---- Helpers ----

    private static FinancialIngestionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MissingAnswerFeedback MakeFeedback(
        string actorId,
        string queryText,
        MissingAnswerFeedbackClassification classification,
        DateTimeOffset? submittedAt = null)
    {
        var when = submittedAt ?? Now;
        var hash = MissingAnswerFeedbackFactoryHashHelper.Hash(queryText);
        return new MissingAnswerFeedback(
            Id: Guid.NewGuid(),
            ActorId: actorId,
            QueryText: queryText,
            QueryHashSha256: hash,
            Classification: classification,
            RequestedMetricCode: null,
            AffectedDataCodeOrName: null,
            SymbolCountTotal: 100,
            SymbolCountMatched: 0,
            SubmittedAt: when,
            DateBucket: DateOnly.FromDateTime(when.UtcDateTime),
            Context: null,
            FrequencyCount: 1,
            ResolvedAt: null);
    }

    private static class MissingAnswerFeedbackFactoryHashHelper
    {
        public static string Hash(string text) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
