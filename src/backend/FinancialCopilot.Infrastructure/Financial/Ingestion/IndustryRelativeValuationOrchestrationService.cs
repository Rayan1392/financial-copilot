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
    ILogger<IndustryRelativeValuationOrchestrationService> logger)
    : IIndustryRelativeValuationOrchestrationService
{
    public async Task<IndustryRelativeValuationOrchestrationResult> RunAsync(
        string correlationId,
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
            var sourceRun = await sourceIngestion.RunAsync(
                new IndustryRelativeValuationSourceRunRequest(normalizedCorrelationId),
                cancellationToken);
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
