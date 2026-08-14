using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Executes Feature 125 after the existing ingestion workflow. This service owns no schedule or
/// parallel lease; those remain with the existing worker/coordinator and the established
/// publication/watch persistence boundaries.
/// </summary>
public sealed class IndustryRelativeValuationOrchestrationService(
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
    : IIndustryRelativeValuationOrchestrationService, IFeature125HandoffSubmissionBoundary
{
    public async Task<IndustryRelativeValuationOrchestrationResult> RunAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        _ = sourceIngestion; // retained for constructor compatibility; Feature 125 no longer invokes acquisition.
        logger.LogWarning(
            "Feature 125 direct orchestration request was rejected because a validated Feature 126 handoff is required. correlationId={CorrelationId}.",
            correlationId);
        return new(
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim(),
            0, 0, 0, 0, 0, 0, 0);
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
                "Feature 125 downstream calculation completed. correlationId={CorrelationId} companies={Companies} factsPersisted={FactsPersisted} factsUnchanged={FactsUnchanged} sourceFailures={SourceFailures} industries={Industries} published={Published} inconclusive={Inconclusive}.",
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
