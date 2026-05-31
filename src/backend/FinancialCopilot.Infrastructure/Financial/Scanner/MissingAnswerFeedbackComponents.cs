using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

/// <summary>
/// Configuration for spec 028 feedback collection. Default <c>Enabled = false</c> means the no-op
/// collector is wired by DI and there is no overhead on the scanner hot path.
/// </summary>
public sealed class MissingAnswerFeedbackOptions
{
    public const string SectionName = "MissingAnswerFeedback";

    public bool Enabled { get; init; }
}

/// <summary>
/// Default Phase 1 collector — discards every call. Wired by DI when the
/// <c>MissingAnswerFeedback:Enabled</c> setting is false (the default), so production has no
/// feedback-collection overhead until the feature is turned on.
/// </summary>
public sealed class NoOpMissingAnswerFeedbackCollector : IMissingAnswerFeedbackCollector
{
    public Task CollectAsync(MissingAnswerFeedbackRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// Wraps a real <see cref="IMissingAnswerFeedbackRepository"/> with fire-and-forget semantics.
/// The caller's <c>await</c> resolves immediately; the upsert is performed on a background task
/// that swallows exceptions and logs them — collector failure must never alter the query response.
///
/// Each background task creates its own DI scope so the repository's scoped <c>DbContext</c> is not
/// shared with the request-scoped one that may already have been disposed by the time the write runs.
/// </summary>
public sealed class AsyncFireAndForgetMissingAnswerFeedbackCollector(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AsyncFireAndForgetMissingAnswerFeedbackCollector> logger) : IMissingAnswerFeedbackCollector
{
    public Task CollectAsync(MissingAnswerFeedbackRequest request, CancellationToken cancellationToken)
    {
        var feedback = MissingAnswerFeedbackFactory.FromRequest(request, timeProvider);
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IMissingAnswerFeedbackRepository>();
                await repository.UpsertAsync(feedback, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Missing-answer feedback collection failed for actor {ActorId} ({Classification}); swallowed to protect query.",
                    request.ActorId,
                    request.Classification);
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
}

internal static class MissingAnswerFeedbackFactory
{
    public static MissingAnswerFeedback FromRequest(
        MissingAnswerFeedbackRequest request,
        TimeProvider timeProvider) =>
        new(
            Guid.NewGuid(),
            ActorId: request.ActorId,
            QueryText: Truncate(request.QueryText ?? string.Empty, 500) ?? string.Empty,
            QueryHashSha256: ComputeSha256(request.QueryText ?? string.Empty),
            Classification: request.Classification,
            RequestedMetricCode: request.RequestedMetricCode,
            AffectedDataCodeOrName: request.AffectedDataCodeOrName,
            SymbolCountTotal: request.SymbolCountTotal,
            SymbolCountMatched: request.SymbolCountMatched,
            SubmittedAt: request.SubmittedAt,
            DateBucket: DateOnly.FromDateTime(request.SubmittedAt.UtcDateTime),
            Context: Truncate(request.Context, 2000),
            FrequencyCount: 1,
            ResolvedAt: null);

    public static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>
/// PostgreSQL-backed feedback repository. Upsert coalesces duplicate feedback inside the same
/// <c>(ActorId, QueryHashSha256, Classification, DateBucket)</c> by incrementing
/// <c>FrequencyCount</c> rather than inserting a new row. Queries are paged.
/// </summary>
public sealed class EfCoreMissingAnswerFeedbackRepository(
    FinancialIngestionDbContext dbContext) : IMissingAnswerFeedbackRepository
{
    public async Task UpsertAsync(MissingAnswerFeedback feedback, CancellationToken cancellationToken)
    {
        var classification = feedback.Classification.ToString();
        var existing = await dbContext.MissingAnswerFeedbacks
            .SingleOrDefaultAsync(row =>
                row.ActorId == feedback.ActorId &&
                row.QueryHashSha256 == feedback.QueryHashSha256 &&
                row.Classification == classification &&
                row.DateBucket == feedback.DateBucket,
                cancellationToken);

        if (existing is not null)
        {
            existing.FrequencyCount += 1;
            existing.SymbolCountMatched = feedback.SymbolCountMatched;
            existing.SymbolCountTotal = feedback.SymbolCountTotal;
            existing.SubmittedAt = feedback.SubmittedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        dbContext.MissingAnswerFeedbacks.Add(new MissingAnswerFeedbackRow
        {
            Id = feedback.Id,
            ActorId = feedback.ActorId,
            QueryText = feedback.QueryText,
            QueryHashSha256 = feedback.QueryHashSha256,
            Classification = classification,
            RequestedMetricCode = feedback.RequestedMetricCode,
            AffectedDataCodeOrName = feedback.AffectedDataCodeOrName,
            SymbolCountTotal = feedback.SymbolCountTotal,
            SymbolCountMatched = feedback.SymbolCountMatched,
            SubmittedAt = feedback.SubmittedAt,
            DateBucket = feedback.DateBucket,
            Context = feedback.Context,
            FrequencyCount = feedback.FrequencyCount,
            ResolvedAt = feedback.ResolvedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<MissingAnswerFeedback>> QueryAsync(
        MissingAnswerFeedbackQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.MissingAnswerFeedbacks.AsNoTracking().AsQueryable();

        if (query.DateFrom is not null)
        {
            queryable = queryable.Where(row => row.SubmittedAt >= query.DateFrom.Value);
        }
        if (query.DateTo is not null)
        {
            queryable = queryable.Where(row => row.SubmittedAt <= query.DateTo.Value);
        }
        if (query.Classification is not null)
        {
            var code = query.Classification.Value.ToString();
            queryable = queryable.Where(row => row.Classification == code);
        }
        if (!string.IsNullOrWhiteSpace(query.RequestedMetricCode))
        {
            queryable = queryable.Where(row => row.RequestedMetricCode == query.RequestedMetricCode);
        }
        if (!string.IsNullOrWhiteSpace(query.ActorId))
        {
            queryable = queryable.Where(row => row.ActorId == query.ActorId);
        }

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 1000);

        var rows = await queryable
            .OrderByDescending(row => row.SubmittedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(MapRow).ToArray();
    }

    public async Task<IReadOnlyDictionary<MissingAnswerFeedbackClassification, int>> GetCountByClassificationAsync(
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.MissingAnswerFeedbacks.AsNoTracking().AsQueryable();
        if (dateFrom is not null) queryable = queryable.Where(row => row.SubmittedAt >= dateFrom.Value);
        if (dateTo is not null) queryable = queryable.Where(row => row.SubmittedAt <= dateTo.Value);

        var grouped = await queryable
            .GroupBy(row => row.Classification)
            .Select(g => new { Code = g.Key, Count = g.Sum(row => row.FrequencyCount) })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<MissingAnswerFeedbackClassification, int>();
        foreach (var item in grouped)
        {
            if (Enum.TryParse<MissingAnswerFeedbackClassification>(item.Code, out var parsed))
            {
                result[parsed] = item.Count;
            }
        }
        return result;
    }

    private static MissingAnswerFeedback MapRow(MissingAnswerFeedbackRow row) =>
        new(
            row.Id,
            row.ActorId,
            row.QueryText,
            row.QueryHashSha256,
            Enum.Parse<MissingAnswerFeedbackClassification>(row.Classification),
            row.RequestedMetricCode,
            row.AffectedDataCodeOrName,
            row.SymbolCountTotal,
            row.SymbolCountMatched,
            row.SubmittedAt,
            row.DateBucket,
            row.Context,
            row.FrequencyCount,
            row.ResolvedAt);
}
