using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class FinancialDataSyncProcessor(
    FinancialIngestionDbContext dbContext,
    IProviderRawPayloadStore rawPayloads,
    ISymbolDataProvider symbolProvider,
    IFinancialStatementProvider statementProvider,
    IMonthlyProductionSalesProvider monthlyProvider,
    IEnumerable<IFinancialPayloadNormalizer> normalizers,
    IDerivedMetricRecalculationPublisher recalculationPublisher,
    TimeProvider timeProvider,
    ILogger<FinancialDataSyncProcessor> logger,
    IScannerCache? scannerCache = null,
    IFinancialDataProviderRouter? providerRouter = null,
    IFinancialRatioProvider? ratioProvider = null,
    INoavaranCurrentApiBoundaryOverride? boundaryOverride = null) : IFinancialDataSyncProcessor, IDataSyncRunReader
{
    private readonly IReadOnlyDictionary<(string ProviderName, ProviderDataset Dataset), IFinancialPayloadNormalizer> _normalizers =
        normalizers.ToDictionary(normalizer => (normalizer.ProviderName, normalizer.Dataset));

    public async Task<DataSyncProcessingResult> ProcessAsync(
        DataSyncRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SyncRuns.SingleOrDefaultAsync(
            row => row.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);

        if (existing?.Status == DataSyncRunStatus.Completed.ToString())
        {
            return new DataSyncProcessingResult(Map(existing), AlreadyProcessed: true);
        }

        var provenance = ResolveProvenance(request);
        var run = existing ?? new DataSyncRunRow
        {
            Id = request.RequestId,
            IdempotencyKey = request.IdempotencyKey,
            Dataset = request.Dataset.ToString(),
            ProviderName = request.ProviderName,
            ExternalReference = request.ExternalReference,
            RequestedAt = request.RequestedAt,
            LogicalVendor = provenance?.Vendor.ToString(),
            PhysicalSource = provenance?.Source.ToString(),
            SourceMode = (request.Mode ?? provenance?.DefaultMode)?.ToString(),
            SourceDateRangeStartJalali = request.SourceDateRangeStartJalali,
            SourceDateRangeEndJalali = request.SourceDateRangeEndJalali
        };

        if (existing is null)
        {
            dbContext.SyncRuns.Add(run);
        }

        run.Status = DataSyncRunStatus.Running.ToString();
        run.StartedAt ??= timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            // Apply a per-run current-API Shamsi boundary override (spec 053) so it reaches this
            // worker scope's provider client; no-op for providers without a Shamsi boundary.
            boundaryOverride?.Set(request.FromShamsiYearOverride);

            var payload = await FetchPayloadAsync(request, cancellationToken);
            await rawPayloads.StoreAsync(payload, cancellationToken);
            var processedRecords = await _normalizers[(payload.ProviderName, payload.Dataset)]
                .NormalizeAsync(payload, cancellationToken);

            run.SourcePayloadChecksum = payload.Checksum;
            run.ProcessedRecords = processedRecords;
            run.Status = DataSyncRunStatus.Completed.ToString();
            run.CompletedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);

            await recalculationPublisher.PublishAsync(
                new DerivedMetricRecalculationRequested(
                    Guid.NewGuid(),
                    request.Dataset,
                    request.ExternalReference,
                    payload.Checksum,
                    timeProvider.GetUtcNow()),
                cancellationToken);
            if (scannerCache is not null)
            {
                await scannerCache.InvalidateAsync(
                    new ScannerCacheInvalidation(
                        $"DataSync.{request.Dataset}",
                        timeProvider.GetUtcNow()),
                    cancellationToken);
            }

            return new DataSyncProcessingResult(Map(run), AlreadyProcessed: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Financial data synchronization failed for {Dataset}.", request.Dataset);
            run.Status = DataSyncRunStatus.Failed.ToString();
            run.ErrorCount = 1;
            run.ErrorMessage = Limit(exception.Message);
            run.CompletedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            return new DataSyncProcessingResult(Map(run), AlreadyProcessed: false);
        }
    }

    public async Task<IReadOnlyCollection<DataSyncRun>> QueryRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return (await dbContext.SyncRuns.AsNoTracking()
                .OrderByDescending(row => row.RequestedAt)
                .Take(maximumCount)
                .ToListAsync(cancellationToken))
            .Select(Map)
            .ToArray();
    }

    private Task<ProviderRawPayload> FetchPayloadAsync(
        DataSyncRequest request,
        CancellationToken cancellationToken) =>
        request.Dataset switch
        {
            ProviderDataset.Symbols => ResolveSymbolProvider(request.ProviderName)
                .FetchSymbolsAsync(cancellationToken),
            ProviderDataset.FinancialStatements => ResolveStatementProvider(request.ProviderName)
                .FetchFinancialStatementsAsync(RequireExternalReference(request), cancellationToken),
            ProviderDataset.MonthlyProductionSales => ResolveMonthlyProvider(request.ProviderName)
                .FetchMonthlyReportsAsync(RequireExternalReference(request), cancellationToken),
            ProviderDataset.FinancialRatios => ResolveRatioProvider(request.ProviderName)
                .FetchFinancialRatiosAsync(RequireExternalReference(request), cancellationToken),
            ProviderDataset.FundamentalIndexes => ResolveRatioProvider(request.ProviderName)
                .FetchFinancialRatiosAsync(RequireExternalReference(request), cancellationToken),
            _ => throw new InvalidOperationException(
                $"Dataset '{request.Dataset}' is not supported for normalized ingestion.")
        };

    // Route to a named coexisting provider when requested; otherwise use the configured primary
    // (the directly-injected default provider), preserving existing single-provider behavior.
    private ISymbolDataProvider ResolveSymbolProvider(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName)
            ? symbolProvider
            : providerRouter?.ResolveSymbolProvider(providerName) ??
              throw UnknownProvider(providerName, ProviderDataset.Symbols);

    private IFinancialStatementProvider ResolveStatementProvider(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName)
            ? statementProvider
            : providerRouter?.ResolveStatementProvider(providerName) ??
              throw UnknownProvider(providerName, ProviderDataset.FinancialStatements);

    private IMonthlyProductionSalesProvider ResolveMonthlyProvider(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName)
            ? monthlyProvider
            : providerRouter?.ResolveMonthlyProvider(providerName) ??
              throw UnknownProvider(providerName, ProviderDataset.MonthlyProductionSales);

    private IFinancialRatioProvider ResolveRatioProvider(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName)
            ? ratioProvider ?? throw new InvalidOperationException(
                "No IFinancialRatioProvider is registered for the FinancialRatios/FundamentalIndexes dataset.")
            : providerRouter?.ResolveRatioProvider(providerName) ??
              throw UnknownProvider(providerName, ProviderDataset.FinancialRatios);

    // Recover the catalogued source descriptor for this run's provider so provenance (logical vendor,
    // physical source, default mode) can be persisted at batch level. A null/unknown provider (the
    // configured primary, or a foreign name) yields null and leaves provenance columns null.
    private static ProviderSourceDescriptor? ResolveProvenance(DataSyncRequest request) =>
        ProviderSources.TryResolve(request.ProviderName);

    private static InvalidOperationException UnknownProvider(
        string providerName,
        ProviderDataset dataset) =>
        new($"No provider named '{providerName}' is registered for dataset '{dataset}'.");

    private static string RequireExternalReference(DataSyncRequest request) =>
        string.IsNullOrWhiteSpace(request.ExternalReference)
            ? throw new ArgumentException("This synchronization request requires an external reference.")
            : request.ExternalReference;

    private static DataSyncRun Map(DataSyncRunRow row) =>
        new(
            row.Id,
            row.IdempotencyKey,
            Enum.Parse<ProviderDataset>(row.Dataset),
            row.ExternalReference,
            Enum.Parse<DataSyncRunStatus>(row.Status),
            row.RequestedAt,
            row.StartedAt,
            row.CompletedAt,
            row.ProcessedRecords,
            row.ErrorCount,
            row.ErrorMessage,
            row.SourcePayloadChecksum,
            row.ProviderName,
            ParseEnum<LogicalVendor>(row.LogicalVendor),
            ParseEnum<PhysicalSource>(row.PhysicalSource),
            ParseEnum<SourceMode>(row.SourceMode),
            row.SourceDateRangeStartJalali,
            row.SourceDateRangeEndJalali);

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, out var parsed) ? parsed : null;

    private static string Limit(string message) => message.Length <= 1000 ? message : message[..1000];
}

public sealed class StoredDerivedMetricRecalculationPublisher(
    FinancialIngestionDbContext dbContext) : IDerivedMetricRecalculationPublisher
{
    public async Task PublishAsync(
        DerivedMetricRecalculationRequested request,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.MetricRecalculationRequests.AnyAsync(
            row => row.SourceDataset == request.SourceDataset.ToString() &&
                row.SourcePayloadChecksum == request.SourcePayloadChecksum,
            cancellationToken);

        if (exists)
        {
            return;
        }

        dbContext.MetricRecalculationRequests.Add(new MetricRecalculationRequestRow
        {
            Id = request.Id,
            SourceDataset = request.SourceDataset.ToString(),
            ExternalReference = request.ExternalReference,
            SourcePayloadChecksum = request.SourcePayloadChecksum,
            RequestedAt = request.RequestedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
