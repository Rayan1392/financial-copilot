using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class ManualNoavaranFinancialStatementBackfillService(
    NadpcoApiDataProviderClient provider,
    IFinancialDataSyncProcessor processor,
    TimeProvider timeProvider) : IManualNoavaranFinancialStatementBackfillService
{
    public async Task<ManualNoavaranFinancialStatementBackfillResult> ExecuteAsync(
        ManualNoavaranFinancialStatementBackfillRequest request,
        CancellationToken cancellationToken)
    {
        var payload = await provider.FetchManualFinancialStatementAsync(
            request.ExternalCompanyId,
            request.FiscalYearEnding,
            request.PeriodMonths,
            request.StatementType,
            cancellationToken);

        var requestId = Guid.NewGuid();
        var result = await processor.ProcessPayloadAsync(
            new DataSyncRequest(
                requestId,
                payload.Dataset,
                payload.ExternalReference,
                timeProvider.GetUtcNow(),
                // A manual backfill is also the repair path for incomplete or stale persisted
                // reports. Do not let a matching payload checksum short-circuit normalization:
                // the normalizer safely upserts the report and every source line item.
                $"manual-noavaran-statement-{request.ExternalCompanyId}-{request.StatementType}-{request.FiscalYearEnding}-{request.PeriodMonths}-{requestId:N}",
                payload.ProviderName,
                SourceMode.CurrentIncremental,
                $"{request.FiscalYearEnding:D4}",
                $"{request.FiscalYearEnding:D4}"),
            payload,
            cancellationToken);

        var run = result.Run;
        if (result.AlreadyProcessed && run.ProcessedRecords > 0)
            return new ManualNoavaranFinancialStatementBackfillResult(
                run.Id, "AlreadyExists", run.ProcessedRecords, "This provider response was already persisted.");

        if (run.Status == DataSyncRunStatus.Failed)
            return new ManualNoavaranFinancialStatementBackfillResult(
                run.Id, "PersistenceFailed", run.ProcessedRecords, run.ErrorMessage ?? "Data persistence failed.");

        if (run.ProcessedRecords == 0)
            return new ManualNoavaranFinancialStatementBackfillResult(
                run.Id, "Empty", 0, "The provider returned no persistable data for the selected report.");

        return new ManualNoavaranFinancialStatementBackfillResult(
            run.Id, "Persisted", run.ProcessedRecords, "The report was retrieved and persisted.");
    }
}
