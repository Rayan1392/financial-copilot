using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public sealed class StartFundPortfolioImportRunUseCase(IFundPortfolioImportRunRepository repository) : IStartFundPortfolioImportRunUseCase
{
    public async Task<FundPortfolioImportRunResult> ExecuteAsync(StartFundPortfolioImportRunRequest request, CancellationToken cancellationToken)
    {
        if (request.Sources.Count == 0) throw new ArgumentException("At least one source item is required.", nameof(request));
        if (request.Sources.Count > 500) throw new ArgumentException("A run cannot contain more than 500 items.", nameof(request));
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");
        var runId = await repository.CreateRunAsync(request, correlationId, cancellationToken);
        await repository.AddItemsAsync(runId, request.Sources, cancellationToken);
        return new(runId, request.Sources.Count, FundPortfolioImportRunStatus.Queued, correlationId);
    }
}

public sealed class ImportFundPortfolioItemUseCase(
    IFundPortfolioImportRunRepository runs,
    IFundPortfolioReportSourceRegistry sources,
    IIngestFundPortfolioWorkbookUseCase ingestion,
    IFundPortfolioMappingReviewRepository reviews,
    IFundPortfolioOperationalTelemetry telemetry) : IImportFundPortfolioItemUseCase
{
    public async Task<FundPortfolioImportItemStatus> ExecuteAsync(ImportFundPortfolioItemRequest request, CancellationToken cancellationToken)
    {
        var work = await runs.ClaimItemAsync(request.RunId, request.ItemId, request.LeaseDurationSeconds, cancellationToken);
        if (work is null) return FundPortfolioImportItemStatus.Cancelled;
        telemetry.RecordQueueLag(DateTimeOffset.UtcNow - work.QueuedAtUtc);
        try
        {
            if (string.IsNullOrWhiteSpace(work.ObservedFundName))
            {
                await runs.CompleteItemAsync(work.Id, FundPortfolioImportItemStatus.NeedsReview, null, "FUND_HINT_MISSING", "Source did not provide a governed fund identity hint.", cancellationToken);
                return FundPortfolioImportItemStatus.NeedsReview;
            }
            var source = sources.Get(work.ProviderName);
            var downloadStarted = DateTimeOffset.UtcNow;
            var download = await source.DownloadAsync(new(work.ProviderName, work.SourceObjectId, work.OriginalFileName, work.ObservedPeriodEnd, work.ObservedFundName, null, null, work.DownloadToken), cancellationToken);
            telemetry.RecordDownload(download.Length, (DateTimeOffset.UtcNow - downloadStarted).TotalMilliseconds);
            await using var content = download.Content;
            var result = await ingestion.ExecuteAsync(new(work.ProviderName, work.ObservedFundName, work.OriginalFileName, download.ContentType, download.Content, KnownPeriod: work.ObservedPeriodEnd is null ? null : new(null, work.ObservedPeriodEnd), CorrelationId: work.CorrelationId, SourceObjectId: work.SourceObjectId), cancellationToken);
            telemetry.RecordReview(await reviews.CreateFromReportIssuesAsync(result.ReportId, cancellationToken));
            var status = result.IsDuplicate ? FundPortfolioImportItemStatus.Duplicate : result.ParseStatus == FundPortfolioParseStatus.PartiallyParsed ? FundPortfolioImportItemStatus.Partial : result.SourceRevision > 1 ? FundPortfolioImportItemStatus.CorrectedRevision : FundPortfolioImportItemStatus.Imported;
            await runs.CompleteItemAsync(work.Id, status, result.ReportId, null, null, cancellationToken);
            telemetry.RecordOutcome(status);
            return status;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException)
        {
            var status = FundPortfolioRetryPolicy.IsPoisoned(work.AttemptCount, request.MaximumAttempts) ? FundPortfolioImportItemStatus.Poisoned : FundPortfolioImportItemStatus.RetryableFailure;
            if (status == FundPortfolioImportItemStatus.RetryableFailure) telemetry.RecordRetry();
            // Persist only a bounded operational summary. Detailed exception data stays in worker logs.
            await runs.CompleteItemAsync(work.Id, status, null, exception.GetType().Name, "Workbook import failed; inspect correlated worker logs.", cancellationToken);
            telemetry.RecordOutcome(status);
            return status;
        }
    }
}

public sealed class FinalizeFundPortfolioImportRunUseCase(IFundPortfolioImportRunRepository repository, IFundPortfolioOperationalTelemetry telemetry) : IFinalizeFundPortfolioImportRunUseCase
{
    public async Task<FinalizeFundPortfolioImportRunResult> ExecuteAsync(Guid runId, CancellationToken cancellationToken) { var result = await repository.FinalizeAsync(runId, cancellationToken); telemetry.RecordFinalStatus(result.Status); return result; }
}
