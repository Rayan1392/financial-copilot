using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NoavaranEligibleCompanyViewReader(FinancialIngestionDbContext dbContext)
    : INoavaranEligibleCompanyReferenceReader
{
    public async Task<IReadOnlyCollection<string>> ReadExternalReferencesAsync(
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT DISTINCT "ExternalCompanyId" AS "Value"
                FROM "NoavaranEligibleCompanies"
                WHERE "ExternalCompanyId" IS NOT NULL
                """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(reference => reference?.Trim())
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => TryParseNumeric(reference) is null ? 1 : 0)
            .ThenBy(reference => TryParseNumeric(reference) ?? int.MaxValue)
            .ThenBy(reference => reference, StringComparer.Ordinal)
            .ToArray()!;
    }

    private static int? TryParseNumeric(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

public sealed class EligibleFundamentalIndexBulkSyncService(
    INoavaranEligibleCompanyReferenceReader eligibleCompanyReader,
    IDataSyncRequestPublisher publisher,
    TimeProvider timeProvider,
    ILogger<EligibleFundamentalIndexBulkSyncService> logger)
    : IEligibleFundamentalIndexBulkSyncService
{
    private const string SourceName = "NoavaranEligibleCompanies";

    public async Task<EligibleFundamentalIndexBulkSyncResult> RunAsync(
        EligibleFundamentalIndexBulkSyncRequest request,
        CancellationToken cancellationToken)
    {
        var requestedAt = timeProvider.GetUtcNow();
        var batchRequestId = Guid.NewGuid();
        var batchKey = string.IsNullOrWhiteSpace(request.BatchIdempotencyKey)
            ? $"admin-data-sync:{ProviderDataset.FundamentalIndexes}:eligible-companies:{Guid.NewGuid():N}"
            : request.BatchIdempotencyKey.Trim();

        logger.LogInformation(
            "Eligible fundamental-index batch started requestId={RequestId} provider={ProviderName} dryRun={DryRun} maxItems={MaxItems}.",
            batchRequestId,
            request.ProviderName,
            request.DryRun,
            request.MaxItems);

        var references = await eligibleCompanyReader.ReadExternalReferencesAsync(cancellationToken);
        var orderedReferences = references
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => TryParseNumeric(reference) is null ? 1 : 0)
            .ThenBy(reference => TryParseNumeric(reference) ?? int.MaxValue)
            .ThenBy(reference => reference, StringComparer.Ordinal);
        var ordered = request.MaxItems is > 0
            ? orderedReferences.Take(request.MaxItems.Value).ToArray()
            : orderedReferences.ToArray();

        logger.LogInformation(
            "Eligible fundamental-index batch requestId={RequestId} discovered {EligibleCount} eligible references from {Source}.",
            batchRequestId,
            ordered.Length,
            SourceName);

        var items = new List<EligibleFundamentalIndexBulkSyncItemResult>(ordered.Length);
        var queuedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;

        foreach (var externalReference in ordered)
        {
            var childKey = BuildChildIdempotencyKey(batchKey, externalReference);

            if (request.DryRun)
            {
                items.Add(new EligibleFundamentalIndexBulkSyncItemResult(
                    externalReference,
                    "DryRun",
                    childKey));
                continue;
            }

            try
            {
                await publisher.PublishAsync(
                    new DataSyncRequest(
                        Guid.NewGuid(),
                        ProviderDataset.FundamentalIndexes,
                        externalReference,
                        requestedAt,
                        childKey,
                        ProviderName: request.ProviderName),
                    cancellationToken);

                queuedCount++;
                items.Add(new EligibleFundamentalIndexBulkSyncItemResult(
                    externalReference,
                    DataSyncRunStatus.Queued.ToString(),
                    childKey));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Eligible fundamental-index batch requestId={RequestId} failed to enqueue externalReference={ExternalReference}.",
                    batchRequestId,
                    externalReference);
                items.Add(new EligibleFundamentalIndexBulkSyncItemResult(
                    externalReference,
                    DataSyncRunStatus.Failed.ToString(),
                    childKey,
                    exception.Message));
            }
        }

        var status = request.DryRun
            ? "DryRun"
            : failedCount == 0 ? DataSyncRunStatus.Queued.ToString() : "QueuedWithFailures";

        logger.LogInformation(
            "Eligible fundamental-index batch completed requestId={RequestId} status={Status} eligible={EligibleCount} queued={QueuedCount} skipped={SkippedCount} failed={FailedCount}.",
            batchRequestId,
            status,
            ordered.Length,
            queuedCount,
            skippedCount,
            failedCount);

        return new EligibleFundamentalIndexBulkSyncResult(
            batchRequestId,
            ProviderDataset.FundamentalIndexes,
            SourceName,
            requestedAt,
            batchKey,
            status,
            ordered.Length,
            queuedCount,
            skippedCount,
            failedCount,
            items);
    }

    internal static string BuildChildIdempotencyKey(string batchIdempotencyKey, string externalReference) =>
        $"{batchIdempotencyKey}:externalReference:{externalReference}";

    private static int? TryParseNumeric(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
