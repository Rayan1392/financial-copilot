namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public enum FundPortfolioImportTriggerType { ManualUpload, BulkBackfill, ScheduledDiscovery, Reprocess }
public enum FundPortfolioImportRunStatus { Queued, Running, Completed, CompletedWithErrors, Cancelled, Failed }
public enum FundPortfolioImportItemStatus { Queued, Running, Imported, Duplicate, CorrectedRevision, Partial, NeedsReview, RetryableFailure, Poisoned, Cancelled, Failed }
public enum FundPortfolioMappingReviewType { AmbiguousFundIdentity, UnresolvedSecurity, UnknownSheet, HeaderLayoutMismatch, InvalidDate, UnitAmbiguity, ReportPeriodConflict, ReconciliationFailure }
public enum FundPortfolioMappingReviewStatus { Pending, Approved, Rejected }

public sealed record StartFundPortfolioImportRunRequest(
    FundPortfolioImportTriggerType TriggerType,
    string ProviderName,
    string? RequestedByActorId,
    IReadOnlyList<FundPortfolioReportSourceDescriptor> Sources,
    string? CorrelationId = null);

public sealed record FundPortfolioImportRunResult(Guid RunId, int ItemCount, FundPortfolioImportRunStatus Status, string CorrelationId);

public sealed record ImportFundPortfolioItemRequest(Guid RunId, Guid ItemId, int MaximumAttempts = 3, int LeaseDurationSeconds = 300);

public sealed record FinalizeFundPortfolioImportRunResult(Guid RunId, FundPortfolioImportRunStatus Status, int ImportedCount, int DuplicateCount, int FailedCount, int PartialCount);

public sealed record FundPortfolioImportRunView(Guid Id, FundPortfolioImportTriggerType TriggerType, string ProviderName, FundPortfolioImportRunStatus Status, int DiscoveredCount, int ImportedCount, int DuplicateCount, int PartialCount, int FailedCount, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string CorrelationId);
public sealed record FundPortfolioImportRunQuery(int Page = 1, int PageSize = 50, FundPortfolioImportRunStatus? Status = null, string? ProviderName = null);
public sealed record FundPortfolioImportRunPage(IReadOnlyList<FundPortfolioImportRunView> Items, int Page, int PageSize, int TotalCount);
public sealed record FundPortfolioImportItemView(Guid Id, Guid RunId, string ProviderName, string OriginalFileName, string? ObservedFundName, DateOnly? ObservedPeriodEnd, string SourceObjectId, FundPortfolioImportItemStatus Status, int AttemptCount, Guid? ReportId, string? LastErrorCode, string? LastErrorSummary, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc);
public sealed record FundPortfolioImportItemQuery(Guid? RunId = null, int Page = 1, int PageSize = 50, FundPortfolioImportItemStatus? Status = null);
public sealed record FundPortfolioImportItemPage(IReadOnlyList<FundPortfolioImportItemView> Items, int Page, int PageSize, int TotalCount);
public sealed record FundPortfolioBulkReprocessRequest(IReadOnlyList<Guid> ReportIds, bool Confirm);

public interface IStartFundPortfolioImportRunUseCase
{
    Task<FundPortfolioImportRunResult> ExecuteAsync(StartFundPortfolioImportRunRequest request, CancellationToken cancellationToken);
}

public interface IImportFundPortfolioItemUseCase
{
    Task<FundPortfolioImportItemStatus> ExecuteAsync(ImportFundPortfolioItemRequest request, CancellationToken cancellationToken);
}

public interface IFinalizeFundPortfolioImportRunUseCase
{
    Task<FinalizeFundPortfolioImportRunResult> ExecuteAsync(Guid runId, CancellationToken cancellationToken);
}

public interface IFundPortfolioImportRunRepository
{
    Task<Guid> CreateRunAsync(StartFundPortfolioImportRunRequest request, string correlationId, CancellationToken cancellationToken);
    Task AddItemsAsync(Guid runId, IReadOnlyList<FundPortfolioReportSourceDescriptor> sources, CancellationToken cancellationToken);
    Task<FundPortfolioImportRunView?> GetRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<FundPortfolioImportRunPage> ListRunsAsync(FundPortfolioImportRunQuery query, CancellationToken cancellationToken);
    Task<FundPortfolioImportItemPage> ListItemsAsync(FundPortfolioImportItemQuery query, CancellationToken cancellationToken);
    Task<int> CancelRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<FundPortfolioImportItemWork?> ClaimItemAsync(Guid runId, Guid itemId, int leaseDurationSeconds, CancellationToken cancellationToken);
    Task<IReadOnlyList<(Guid RunId, Guid ItemId)>> ListRunnableItemsAsync(int maximumItems, CancellationToken cancellationToken);
    Task CompleteItemAsync(Guid itemId, FundPortfolioImportItemStatus status, Guid? reportId, string? errorCode, string? errorSummary, CancellationToken cancellationToken);
    Task<FinalizeFundPortfolioImportRunResult> FinalizeAsync(Guid runId, CancellationToken cancellationToken);
}

public interface IFundPortfolioReportSourceRegistry
{
    IFundPortfolioReportSource Get(string providerName);
}

public sealed record FundPortfolioImportItemWork(Guid Id, Guid RunId, string ProviderName, string OriginalFileName, string? ObservedFundName, DateOnly? ObservedPeriodEnd, string SourceObjectId, string DownloadToken, int AttemptCount, string CorrelationId, DateTimeOffset QueuedAtUtc);

public sealed record FundPortfolioMappingReviewView(Guid Id, Guid ReportId, FundPortfolioMappingReviewType MappingType, string RawValue, string NormalizedValue, string CandidateJson, FundPortfolioMappingReviewStatus Status, string? ResolutionJson, string? ResolvedByActorId, DateTimeOffset? ResolvedAtUtc, int Version);
public sealed record FundPortfolioMappingReviewPage(IReadOnlyList<FundPortfolioMappingReviewView> Items, int Page, int PageSize, int TotalCount);
public sealed record ResolveFundPortfolioMappingReviewRequest(Guid ReviewId, int ExpectedVersion, bool Approve, string ResolutionJson, string ResolvedByActorId);
public sealed record FundPortfolioMappingResolutionResult(bool Changed, int AffectedReportCount, string? PreviousResolutionJson);

public interface IFundPortfolioMappingReviewRepository
{
    Task<IReadOnlyList<FundPortfolioMappingReviewView>> ListAsync(FundPortfolioMappingReviewStatus? status, CancellationToken cancellationToken);
    Task<FundPortfolioMappingReviewPage> ListPageAsync(FundPortfolioMappingReviewStatus? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<int> CreateFromReportIssuesAsync(Guid reportId, CancellationToken cancellationToken);
    Task<FundPortfolioMappingResolutionResult> ResolveAsync(ResolveFundPortfolioMappingReviewRequest request, CancellationToken cancellationToken);
}

public interface IResolveFundPortfolioMappingReviewUseCase
{
    Task<FundPortfolioMappingResolutionResult> ExecuteAsync(ResolveFundPortfolioMappingReviewRequest request, CancellationToken cancellationToken);
}
