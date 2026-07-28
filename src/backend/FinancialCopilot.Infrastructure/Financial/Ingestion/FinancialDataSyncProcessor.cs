using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

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
    INoavaranCurrentApiBoundaryOverride? boundaryOverride = null,
    IFundamentalIndexCoverageProvider? coverageProvider = null) : IFinancialDataSyncProcessor, IDataSyncRunReader
{
    private readonly IReadOnlyDictionary<(string ProviderName, ProviderDataset Dataset), IFinancialPayloadNormalizer> _normalizers =
        normalizers.ToDictionary(normalizer => (normalizer.ProviderName, normalizer.Dataset));

    public async Task<DataSyncProcessingResult> ProcessAsync(
        DataSyncRequest request,
        CancellationToken cancellationToken) =>
        await ProcessCoreAsync(
            request,
            () => FetchPayloadAsync(request, cancellationToken),
            cancellationToken);

    public async Task<DataSyncProcessingResult> ProcessPayloadAsync(
        DataSyncRequest request,
        ProviderRawPayload payload,
        CancellationToken cancellationToken) =>
        await ProcessCoreAsync(
            request,
            () => Task.FromResult(payload),
            cancellationToken);

    private async Task<DataSyncProcessingResult> ProcessCoreAsync(
        DataSyncRequest request,
        Func<Task<ProviderRawPayload>> payloadFactory,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SyncRuns.SingleOrDefaultAsync(
            row => row.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);

        if (existing is not null && await IsEffectivelyCompletedAsync(existing, cancellationToken))
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
            if (ShouldSkipCyclicalWavesSymbolSync(request))
            {
                logger.LogInformation(
                    "Skipping CyclicalWaves Symbols synchronization because company catalog updates from CyclicalWaves are disabled by spec 068.");
                run.ProcessedRecords = 0;
                run.Status = DataSyncRunStatus.Completed.ToString();
                run.CompletedAt = timeProvider.GetUtcNow();
                await dbContext.SaveChangesAsync(cancellationToken);
                return new DataSyncProcessingResult(Map(run), AlreadyProcessed: false);
            }

            // Apply a per-run current-API Shamsi boundary override (spec 053) so it reaches this
            // worker scope's provider client; no-op for providers without a Shamsi boundary.
            boundaryOverride?.Set(request.FromShamsiYearOverride);
            // Bound monthly-activity fetches to the request's Jalali window when one is set
            // (spec 057: one Shamsi month per backfill/steady-state request).
            if (request.Dataset == ProviderDataset.MonthlyProductionSales &&
                request.SourceDateRangeStartJalali is not null)
            {
                boundaryOverride?.SetMonthlyActivityWindow(
                    request.SourceDateRangeStartJalali,
                    request.SourceDateRangeEndJalali);
            }

            var payload = await payloadFactory();
            await rawPayloads.StoreAsync(payload, cancellationToken);
            var normalizationPayload = payload with { Dataset = request.Dataset };
            var outcome = await _normalizers[(normalizationPayload.ProviderName, normalizationPayload.Dataset)]
                .NormalizeAsync(normalizationPayload, cancellationToken);

            run.SourcePayloadChecksum = payload.Checksum;
            run.ProcessedRecords = outcome.ProcessedRecords;
            run.ErrorCount = 0;
            run.ErrorMessage = null;

            if (request.Dataset == ProviderDataset.MonthlyProductionSales)
            {
                var hasRequestedCompanyMonthRows = await MonthlyReportExistsForRunAsync(run, cancellationToken);
                if (!hasRequestedCompanyMonthRows)
                {
                    run.Status = DataSyncRunStatus.Failed.ToString();
                    run.ErrorCount = 1;
                    run.ErrorMessage = Limit("NoDataYet - vendor returned no monthly report rows for this company/month.");
                    run.CompletedAt = timeProvider.GetUtcNow();
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return new DataSyncProcessingResult(Map(run), AlreadyProcessed: false);
                }
            }

            run.Status = DataSyncRunStatus.Completed.ToString();
            run.CompletedAt = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);

            // Prefer the canonical ExternalCompanyId the normalizer resolved and stored in
            // FinancialStatements/MonthlyReports. This guarantees MetricRecalculationProcessor
            // can always resolve the company with a simple direct lookup. Fall back to the
            // request's ExternalReference only for normalizers that don't write company-scoped
            // financial rows (e.g. Symbols-only providers).
            var recalculationReference = outcome.CanonicalExternalCompanyId ?? request.ExternalReference;

            await recalculationPublisher.PublishAsync(
                new DerivedMetricRecalculationRequested(
                    Guid.NewGuid(),
                    request.Dataset,
                    recalculationReference,
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
        catch (DbUpdateException exception) when (TryGetUniqueConstraintViolation(exception, out var postgresException))
        {
            var externalCompanyId = DetachPendingDerivedMetricRows() ?? request.ExternalReference;
            await MarkRunFailedAsync(
                run,
                $"DERIVED_METRIC_UNIQUE_CONSTRAINT_VIOLATION: {postgresException.MessageText}",
                cancellationToken);

            logger.LogError(
                exception,
                "Financial data sync failed permanently due to a unique constraint violation. " +
                "SyncRunId: {SyncRunId}, Provider: {Provider}, ExternalCompanyId: {ExternalCompanyId}, " +
                "SqlState: {SqlState}, Constraint: {Constraint}, OriginalMessage: {OriginalMessage}",
                run.Id,
                run.ProviderName,
                externalCompanyId,
                postgresException.SqlState,
                postgresException.ConstraintName,
                postgresException.Message);

            // A normal return deliberately ACKs this permanent failure at the RabbitMQ boundary.
            return new DataSyncProcessingResult(Map(run), AlreadyProcessed: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Financial data synchronization failed for {Dataset}.", request.Dataset);
            await MarkRunFailedAsync(run, exception.Message, cancellationToken);
            return new DataSyncProcessingResult(Map(run), AlreadyProcessed: false);
        }
    }

    private async Task MarkRunFailedAsync(
        DataSyncRunRow run,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        run.Status = DataSyncRunStatus.Failed.ToString();
        run.ErrorCount = 1;
        run.ErrorMessage = Limit(failureMessage);
        run.CompletedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string? DetachPendingDerivedMetricRows()
    {
        var entries = dbContext.ChangeTracker.Entries<DerivedMetricRow>()
            .Where(entry => entry.State == EntityState.Added)
            .ToList();
        var externalCompanyId = entries.Select(entry => entry.Entity.ExternalCompanyId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }

        return externalCompanyId;
    }

    private static bool TryGetUniqueConstraintViolation(
        DbUpdateException exception,
        out PostgresException postgresException)
    {
        postgresException = exception.GetBaseException() as PostgresException
            ?? exception.InnerException as PostgresException
            ?? null!;
        return postgresException is not null &&
            postgresException.SqlState == PostgresErrorCodes.UniqueViolation;
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
            ProviderDataset.FundamentalIndexCoverage => ResolveCoverageProvider()
                .FetchAllFundamentalIndexesAsync(
                    RequireExternalReference(request),
                    ParseShamsiYear(request.SourceDateRangeStartJalali, 1403),
                    ParseShamsiYear(request.SourceDateRangeEndJalali, 1405),
                    cancellationToken),
            _ => throw new InvalidOperationException(
                $"Dataset '{request.Dataset}' is not supported for normalized ingestion.")
        };

    private IFundamentalIndexCoverageProvider ResolveCoverageProvider() =>
        coverageProvider ?? throw new InvalidOperationException(
            "No IFundamentalIndexCoverageProvider is registered for the FundamentalIndexCoverage dataset.");

    private async Task<bool> IsEffectivelyCompletedAsync(
        DataSyncRunRow run,
        CancellationToken cancellationToken)
    {
        if (run.Status != DataSyncRunStatus.Completed.ToString())
        {
            return false;
        }

        if (run.Dataset != ProviderDataset.MonthlyProductionSales.ToString())
        {
            return true;
        }

        return await MonthlyReportExistsForRunAsync(run, cancellationToken);
    }

    private async Task<bool> MonthlyReportExistsForRunAsync(
        DataSyncRunRow run,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(run.ExternalReference) ||
            !TryResolveRunPeriod(run, out var periodStart, out var periodEnd))
        {
            return run.ProcessedRecords > 0;
        }

        var query = dbContext.MonthlyReports.AsNoTracking()
            .Where(row => row.ExternalCompanyId == run.ExternalReference &&
                row.PeriodStart == periodStart &&
                row.PeriodEnd == periodEnd);

        if (!string.IsNullOrWhiteSpace(run.ProviderName))
        {
            query = query.Where(row => row.ProviderName == run.ProviderName);
        }

        return await query.AnyAsync(cancellationToken);
    }

    private static bool TryResolveRunPeriod(
        DataSyncRunRow run,
        out DateOnly periodStart,
        out DateOnly periodEnd)
    {
        periodStart = default;
        periodEnd = default;

        if (TryParseShamsiYearMonth(run.SourceDateRangeStartJalali, out var year, out var month) ||
            TryParseMonthToken(run.IdempotencyKey, out year, out month))
        {
            var resolved = JalaliDateResolver.ResolveMonth(year, (byte)month);
            periodStart = resolved.PeriodStart;
            periodEnd = resolved.PeriodEnd;
            return true;
        }

        return false;
    }

    private static bool TryParseShamsiYearMonth(string? value, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('/');
        return parts.Length >= 2 &&
            int.TryParse(parts[0], out year) &&
            int.TryParse(parts[1], out month);
    }

    private static bool TryParseMonthToken(string idempotencyKey, out int year, out int month)
    {
        year = 0;
        month = 0;
        var parts = idempotencyKey.Split('-');
        if (parts.Length < 3 || parts[2].Length != 6)
        {
            return false;
        }

        return int.TryParse(parts[2][..4], out year) &&
            int.TryParse(parts[2][4..6], out month);
    }

    private static int ParseShamsiYear(string? value, int fallback) =>
        int.TryParse(value, out var year) ? year : fallback;

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

    private static bool ShouldSkipCyclicalWavesSymbolSync(DataSyncRequest request) =>
        request.Dataset == ProviderDataset.Symbols &&
        string.Equals(request.ProviderName, ProviderSources.CyclicalWavesName, StringComparison.OrdinalIgnoreCase);

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
