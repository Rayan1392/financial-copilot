using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Executes industry-relative valuation from persisted CyclicalWaves snapshots after acquisition.
/// Publication and watch persistence remain at their established boundaries.
/// </summary>
public sealed class IndustryRelativeValuationOrchestrationService
    : IIndustryRelativeValuationOrchestrationService, IFeature125HandoffSubmissionBoundary
{
    private readonly IndustryRelativeValuationCalculationInputBuilder inputBuilder;
    private readonly IndustryRelativeValuationCalculationSnapshotWriter snapshotWriter;
    private readonly IOptions<IndustryRelativeValuationOptions> featureOptions;
    private readonly IOptions<IndustryRelativeValuationSourceOptions> sourceOptions;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<IndustryRelativeValuationOrchestrationService> logger;
    private readonly IFeature126SourceFactStore? sourceFacts;
    private readonly IFeature125HandoffConsumer? handoffConsumer;
    private readonly IFeature126LeaseStore? leaseStore;

    public IndustryRelativeValuationOrchestrationService(
        IndustryRelativeValuationCalculationInputBuilder inputBuilder,
        IndustryRelativeValuationCalculationSnapshotWriter snapshotWriter,
        IOptions<IndustryRelativeValuationOptions> featureOptions,
        IOptions<IndustryRelativeValuationSourceOptions> sourceOptions,
        TimeProvider timeProvider,
        ILogger<IndustryRelativeValuationOrchestrationService> logger)
    {
        this.inputBuilder = inputBuilder;
        this.snapshotWriter = snapshotWriter;
        this.featureOptions = featureOptions;
        this.sourceOptions = sourceOptions;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    // Retained for historical handoff replay compatibility. Production registration resolves
    // the persisted-snapshot constructor above and does not register this legacy boundary.
    public IndustryRelativeValuationOrchestrationService(
        IIndustryRelativeValuationSourceIngestionService sourceIngestion,
        IndustryRelativeValuationCalculationInputBuilder inputBuilder,
        IndustryRelativeValuationCalculationSnapshotWriter snapshotWriter,
        IOptions<IndustryRelativeValuationOptions> featureOptions,
        IOptions<IndustryRelativeValuationSourceOptions> sourceOptions,
        TimeProvider timeProvider,
        ILogger<IndustryRelativeValuationOrchestrationService> logger,
        IFeature126SourceFactStore? sourceFacts = null,
        IFeature125HandoffConsumer? handoffConsumer = null,
        IFeature126LeaseStore? leaseStore = null)
        : this(inputBuilder, snapshotWriter, featureOptions, sourceOptions, timeProvider, logger)
    {
        _ = sourceIngestion;
        this.sourceFacts = sourceFacts;
        this.handoffConsumer = handoffConsumer;
        this.leaseStore = leaseStore;
    }

    public async Task<IndustryRelativeValuationOrchestrationResult> RunAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();
        var settings = featureOptions.Value;
        if (!settings.Enabled)
            return new(normalizedCorrelationId, 0, 0, 0, 0, 0, 0, 0);

        var calculatedAtUtc = timeProvider.GetUtcNow();
        var calculationDate = TehranCalculationDate(calculatedAtUtc);
        var inputs = await inputBuilder.BuildAsync(
            sourceOptions.Value.CanonicalProviderName,
            calculatedAtUtc,
            TimeSpan.FromHours(settings.SourceFreshnessHours),
            cancellationToken);

        var published = 0;
        var inconclusive = 0;
        foreach (var input in inputs)
        {
            var write = await snapshotWriter.WriteAsync(
                calculationDate,
                input,
                calculatedAtUtc,
                cancellationToken);
            if (write.Status == "Published") published++;
            if (write.Status == "Inconclusive") inconclusive++;
        }

        var companiesConsidered = inputs.Sum(input => input.Members.Count);
        logger.LogInformation(
            "Industry relative valuation calculation completed from persisted snapshots. correlationId={CorrelationId} companies={Companies} groups={Groups} published={Published} inconclusive={Inconclusive}.",
            normalizedCorrelationId,
            companiesConsidered,
            inputs.Count,
            published,
            inconclusive);
        return new(
            normalizedCorrelationId,
            companiesConsidered,
            0,
            0,
            0,
            inputs.Count,
            published,
            inconclusive);
    }

    public async Task<Feature125HandoffValidationResult> SubmitAsync(
        Feature126HandoffPackage package,
        Feature126HandoffLeaseState lease,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (sourceFacts is null || handoffConsumer is null)
            throw new InvalidOperationException("Feature 125 handoff dependencies are not registered.");

        var currentSnapshot = await sourceFacts.ReadCurrentSnapshotAsync(
            package.RunIdentity.TehranCalculationDate, package.AdmittedUniverse, cancellationToken);
        var validation = handoffConsumer.Validate(package, lease, currentSnapshot, nowUtc);
        if (!validation.Accepted)
        {
            logger.LogWarning(
                "Feature 125 handoff rejected before downstream side effects. reason={Reason} correlationId={CorrelationId}.",
                validation.RejectionReason,
                package.RunIdentity.CorrelationId);
            return validation;
        }

        try
        {
            await EnsureFenceAsync(package, lease, nowUtc, cancellationToken);
            await CalculateAsync(package.RunIdentity.CorrelationId, package, lease, cancellationToken);
        }
        catch (Feature125FencingLostException)
        {
            return Feature125HandoffValidationResult.Reject(Feature125HandoffRejectionReason.StaleFencingToken);
        }
        return validation;
    }

    private async Task<IndustryRelativeValuationOrchestrationResult> CalculateAsync(
        string correlationId,
        Feature126HandoffPackage package,
        Feature126HandoffLeaseState lease,
        CancellationToken cancellationToken)
    {
        var settings = featureOptions.Value;
        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();

        if (!settings.Enabled)
        {
            logger.LogInformation(
                "Feature 125 downstream calculation skipped because it is disabled. correlationId={CorrelationId}.",
                normalizedCorrelationId);
            return new(normalizedCorrelationId, 0, 0, 0, 0, 0, 0, 0);
        }

        try
        {
            var sourceRun = new IndustryRelativeValuationSourceRunResult(
                normalizedCorrelationId, 0, 0, 0, 0, false);
            var calculatedAtUtc = timeProvider.GetUtcNow();
            var calculationDate = TehranCalculationDate(calculatedAtUtc);
            var inputs = await inputBuilder.BuildAsync(
                sourceOptions.Value.CanonicalProviderName,
                calculatedAtUtc,
                TimeSpan.FromHours(settings.SourceFreshnessHours),
                package.SourceSnapshotEvidence,
                cancellationToken);

            var published = 0;
            var inconclusive = 0;
            foreach (var input in inputs)
            {
                await EnsureFenceAsync(package, lease, calculatedAtUtc, cancellationToken);
                var write = await snapshotWriter.WriteAsync(
                    calculationDate,
                    input,
                    calculatedAtUtc,
                    lease,
                    cancellationToken);
                if (write.Status == "Rejected") throw new Feature125FencingLostException();
                if (write.Status == "Published") published++;
                if (write.Status == "Inconclusive") inconclusive++;
            }

            logger.LogInformation(
                "Feature 125 downstream calculation completed. correlationId={CorrelationId} companies={Companies} factsPersisted={FactsPersisted} factsUnchanged={FactsUnchanged} sourceFailures={SourceFailures} groups={Groups} published={Published} inconclusive={Inconclusive}.",
                normalizedCorrelationId,
                sourceRun.CompaniesConsidered,
                sourceRun.FactsPersisted,
                sourceRun.FactsUnchanged,
                sourceRun.Failures,
                inputs.Count,
                published,
                inconclusive);

            return new(
                normalizedCorrelationId,
                sourceRun.CompaniesConsidered,
                sourceRun.FactsPersisted,
                sourceRun.FactsUnchanged,
                sourceRun.Failures,
                inputs.Count,
                published,
                inconclusive);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Feature 125 downstream calculation failed. correlationId={CorrelationId}; current published snapshots and watch state remain authoritative.",
                normalizedCorrelationId);
            throw;
        }
    }

    private async Task EnsureFenceAsync(Feature126HandoffPackage package, Feature126HandoffLeaseState lease,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (sourceFacts is null || handoffConsumer is null) throw new InvalidOperationException("Feature 125 handoff dependencies are not registered.");
        var current = await sourceFacts.ReadCurrentSnapshotAsync(
            package.RunIdentity.TehranCalculationDate, package.AdmittedUniverse, cancellationToken);
        var validation = handoffConsumer.Validate(package, lease, current, nowUtc);
        if (!validation.Accepted) throw new Feature125FencingLostException();
        if (leaseStore is not null)
        {
            var owner = new LeaseHandle(lease.LeaseName, lease.CalculationDate, lease.FencingToken, lease.ExpiresAtUtc);
            if (!await leaseStore.IsOwnerAsync(owner, cancellationToken)) throw new Feature125FencingLostException();
        }
    }

    private sealed class Feature125FencingLostException : Exception;

    private static DateOnly TehranCalculationDate(DateTimeOffset utc)
    {
        var zone = ResolveTehranTimeZone();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);
    }

    private static TimeZoneInfo ResolveTehranTimeZone()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        }

        return TimeZoneInfo.Utc;
    }
}
