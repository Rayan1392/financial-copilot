using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.FinancialData.Metrics;

public interface IMetricAliasExpressionNormalizer
{
    string Normalize(string expression, string language);
}

public interface IDynamicMetricAliasRepository
{
    Task<IReadOnlyList<DynamicMetricAlias>> GetActiveByLanguageAsync(
        string language, CancellationToken cancellationToken);

    Task<DynamicMetricAlias?> FindActiveAsync(
        string normalizedExpression, string language, CancellationToken cancellationToken);

    Task AddAsync(DynamicMetricAlias alias, CancellationToken cancellationToken);

    Task DisableAsync(
        Guid id, string disabledBy, string reason, CancellationToken cancellationToken);
}

public interface IMetricAliasCandidateRepository
{
    Task<IReadOnlyList<MetricAliasCandidate>> GetPendingAsync(
        int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<MetricAliasCandidate>> QueryAsync(
        MetricAliasCandidateQuery query, CancellationToken cancellationToken);

    Task UpsertAsync(MetricAliasCandidate candidate, CancellationToken cancellationToken);

    Task<MetricAliasCandidate?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task ApproveAsync(
        Guid id, string approvedBy, Guid promotedAliasId, CancellationToken cancellationToken);

    Task RejectAsync(Guid id, string reason, CancellationToken cancellationToken);
}

public sealed record MetricAliasCandidateQuery(
    MetricAliasCandidateStatus? Status = null,
    string? Language = null,
    string? MetricCode = null,
    int Take = 50,
    int Skip = 0);

public interface IMetricAliasCacheInvalidator
{
    void InvalidateLanguage(string language);
}
