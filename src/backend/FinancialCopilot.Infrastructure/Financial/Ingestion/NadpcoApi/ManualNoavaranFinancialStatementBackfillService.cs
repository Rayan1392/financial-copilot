using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class ManualNoavaranFinancialStatementBackfillService(
    NadpcoApiDataProviderClient provider,
    IFinancialDataSyncProcessor processor,
    FinancialIngestionDbContext dbContext,
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
        var persistedItems = request.StatementType == ManualNoavaranFinancialStatementType.FinancialRatios
            ? run.ProcessedRecords
            : await CountPersistedItemsAsync(request, payload.Checksum, cancellationToken);
        if (result.AlreadyProcessed && run.ProcessedRecords > 0)
            return new ManualNoavaranFinancialStatementBackfillResult(
                run.Id, "AlreadyExists", run.ProcessedRecords, "This provider response was already persisted.", persistedItems);

        if (run.Status == DataSyncRunStatus.Failed)
            return new ManualNoavaranFinancialStatementBackfillResult(
                run.Id, "PersistenceFailed", run.ProcessedRecords, run.ErrorMessage ?? "Data persistence failed.", persistedItems);

        if (run.ProcessedRecords == 0)
            return new ManualNoavaranFinancialStatementBackfillResult(
                run.Id, "Empty", 0, "The provider returned no persistable data for the selected report.", persistedItems);

        return new ManualNoavaranFinancialStatementBackfillResult(
            run.Id, "Persisted", run.ProcessedRecords, "The report was retrieved and persisted.", persistedItems);
    }

    private async Task<int> CountPersistedItemsAsync(
        ManualNoavaranFinancialStatementBackfillRequest request,
        string payloadChecksum,
        CancellationToken cancellationToken)
    {
        var statementType = request.StatementType switch
        {
            ManualNoavaranFinancialStatementType.BalanceSheet => "BalanceSheet",
            ManualNoavaranFinancialStatementType.IncomeStatement => "IncomeStatement",
            ManualNoavaranFinancialStatementType.CashFlow => "CashFlow",
            _ => throw new ArgumentOutOfRangeException(nameof(request.StatementType))
        };
        var statementIds = dbContext.FinancialStatements.AsNoTracking()
            .Where(statement => statement.ProviderName == ProviderSources.NoavaranCurrentApiName &&
                statement.ExternalCompanyId == request.ExternalCompanyId &&
                statement.StatementType == statementType &&
                statement.SourcePayloadChecksum == payloadChecksum)
            .Select(statement => statement.Id);

        return await dbContext.FinancialStatementLineItems.AsNoTracking()
            .CountAsync(item => statementIds.Contains(item.FinancialStatementId), cancellationToken);
    }
}
