using FinancialCopilot.Domain.Financial.MissingAnswer;

namespace FinancialCopilot.Application.Scanner;

/// <summary>
/// Input to <see cref="IMissingAnswerFeedbackCollector"/> describing a single missed query.
/// Built by the scanner execution service after classification.
/// </summary>
public sealed record MissingAnswerFeedbackRequest(
    string ActorId,
    string QueryText,
    MissingAnswerFeedbackClassification Classification,
    string? RequestedMetricCode,
    string? AffectedDataCodeOrName,
    int SymbolCountTotal,
    int SymbolCountMatched,
    DateTimeOffset SubmittedAt,
    string? Context);

/// <summary>
/// Collection seam called by the scanner after every executed query that produced no answer
/// (or a sparse one). The default Phase 1 implementation is a no-op; the real implementation
/// persists asynchronously (fire-and-forget) so query latency is never affected.
///
/// Implementations MUST NOT throw — collection failures are swallowed/logged.
/// </summary>
public interface IMissingAnswerFeedbackCollector
{
    Task CollectAsync(MissingAnswerFeedbackRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Filters for <see cref="IMissingAnswerFeedbackRepository.QueryAsync"/>.
/// </summary>
public sealed record MissingAnswerFeedbackQuery(
    DateTimeOffset? DateFrom = null,
    DateTimeOffset? DateTo = null,
    MissingAnswerFeedbackClassification? Classification = null,
    string? RequestedMetricCode = null,
    string? ActorId = null,
    int Skip = 0,
    int Take = 100);

/// <summary>
/// Persistence boundary for <see cref="MissingAnswerFeedback"/>. <see cref="UpsertAsync"/> is
/// idempotent on <c>(ActorId, QueryHashSha256, Classification, DateBucket)</c>: duplicate feedback
/// in the same day bucket increments <c>FrequencyCount</c> rather than inserting a new row.
/// </summary>
public interface IMissingAnswerFeedbackRepository
{
    Task UpsertAsync(MissingAnswerFeedback feedback, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<MissingAnswerFeedback>> QueryAsync(
        MissingAnswerFeedbackQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<MissingAnswerFeedbackClassification, int>> GetCountByClassificationAsync(
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        CancellationToken cancellationToken);
}
