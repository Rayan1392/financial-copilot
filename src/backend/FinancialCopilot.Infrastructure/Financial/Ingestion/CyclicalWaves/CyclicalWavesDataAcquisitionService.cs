using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesDataAcquisitionService(
    ICyclicalWavesAcquisitionCompanySource companySource,
    ICyclicalWavesDataAcquisitionClient client,
    ICyclicalWavesDataAcquisitionRepository repository,
    ICanonicalJsonHasher canonicalJsonHasher,
    IOptions<CyclicalWavesDataAcquisitionOptions> options,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesDataAcquisitionService> logger) : ICyclicalWavesDataAcquisitionService
{
    private static readonly CyclicalWavesMetricType[] MetricOrder =
        [
            CyclicalWavesMetricType.PS,
            CyclicalWavesMetricType.LastPS,
            CyclicalWavesMetricType.PE,
            CyclicalWavesMetricType.LastPE,
            CyclicalWavesMetricType.Equilibrium
        ];

    public async Task<CyclicalWavesAcquisitionCycleSummary> ExecuteAsync(
        DateOnly cycleDateUtc,
        CancellationToken cancellationToken)
    {
        var changed = 0;
        var unchanged = 0;
        var failed = 0;
        var skipped = 0;

        var companies = (await companySource.GetCompaniesAsync(cancellationToken))
            .Select(company => new ResolvedCompany(company, ResolveIsin(company)))
            .OrderBy(item => item.Identity.NormalizedIsin is null ? 1 : 0)
            .ThenBy(item => item.Identity.NormalizedIsin, StringComparer.Ordinal)
            .ThenBy(item => item.Company.CompanyId)
            .ToArray();

        foreach (var item in companies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Identity.FailureCode is not null)
            {
                foreach (var metricType in MetricOrder)
                {
                    failed++;
                    try
                    {
                        await PersistIdentityFailureAsync(
                            cycleDateUtc,
                            item.Company,
                            metricType,
                            item.Identity.FailureCode,
                            item.Identity.FailureMessage!,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(
                            exception,
                            "CyclicalWaves identity failure could not be persisted. " +
                            "CompanyId={CompanyId} Metric={Metric} FailureCode={FailureCode}",
                            item.Company.CompanyId,
                            metricType,
                            CyclicalWavesAcquisitionFailureCodes.PersistenceFailure);
                    }
                }

                continue;
            }

            foreach (var metricType in MetricOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var providerCalled = false;

                try
                {
                    if (await repository.HasSuccessfulCheckAsync(
                            cycleDateUtc,
                            item.Company.CompanyId,
                            metricType,
                            cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    providerCalled = true;
                    var acquisition = await client.AcquireAsync(
                        metricType,
                        item.Identity.NormalizedIsin!,
                        cancellationToken);

                    if (acquisition.IsAccepted)
                    {
                        if (acquisition.ProviderObservationDate is { } providerDate &&
                            metricType is CyclicalWavesMetricType.LastPS or CyclicalWavesMetricType.LastPE)
                        {
                            var latestProviderDate = await repository.GetLatestProviderObservationDateAsync(
                                item.Company.CompanyId,
                                metricType,
                                cancellationToken);
                            if (latestProviderDate is { } latest && providerDate < latest)
                            {
                                await repository.PersistFailedAsync(
                                    new CyclicalWavesFailedAcquisition(
                                        cycleDateUtc,
                                        item.Company.CompanyId,
                                        item.Identity.NormalizedIsin!,
                                        metricType,
                                        acquisition.CheckedAtUtc,
                                        acquisition.RequestedAtUtc,
                                        acquisition.CompletedAtUtc,
                                        acquisition.SourceEndpoint,
                                        acquisition.HttpStatusCode,
                                        acquisition.AttemptCount,
                                        CyclicalWavesAcquisitionFailureCodes.StaleProviderObservationDate,
                                        $"Provider observation date {providerDate:yyyy-MM-dd} is older than the latest stored date {latest:yyyy-MM-dd}."),
                                    cancellationToken);
                                failed++;
                                logger.LogWarning(
                                    "Rejected stale CyclicalWaves observation. CompanyId={CompanyId} Isin={Isin} " +
                                    "Metric={Metric} ProviderDate={ProviderDate} LatestStoredDate={LatestStoredDate}",
                                    item.Company.CompanyId,
                                    item.Identity.NormalizedIsin,
                                    metricType,
                                    providerDate,
                                    latest);
                                continue;
                            }
                        }

                        var hash = canonicalJsonHasher.ComputeHash(acquisition.RawResponseJson!);
                        var persisted = await repository.PersistAcceptedAsync(
                            new CyclicalWavesAcceptedAcquisition(
                                cycleDateUtc,
                                item.Company.CompanyId,
                                item.Identity.NormalizedIsin!,
                                metricType,
                                acquisition.RawResponseJson!,
                                hash,
                                acquisition.CheckedAtUtc,
                                acquisition.RequestedAtUtc!.Value,
                                acquisition.AcquisitionDateUtc!.Value,
                                acquisition.CompletedAtUtc,
                                acquisition.SourceEndpoint,
                                acquisition.HttpStatusCode!.Value,
                                acquisition.AttemptCount,
                                acquisition.ProviderObservationDate),
                            cancellationToken);

                        if (persisted.Result == CyclicalWavesAcquisitionResult.Changed)
                        {
                            changed++;
                        }
                        else
                        {
                            unchanged++;
                        }

                        logger.LogInformation(
                            "CyclicalWaves acquisition completed. CompanyId={CompanyId} Isin={Isin} " +
                            "Metric={Metric} Result={Result} CheckId={CheckId} Attempts={Attempts} HashPrefix={HashPrefix}",
                            item.Company.CompanyId,
                            item.Identity.NormalizedIsin,
                            metricType,
                            persisted.Result,
                            persisted.CheckId,
                            acquisition.AttemptCount,
                            hash[..12]);
                    }
                    else
                    {
                        await repository.PersistFailedAsync(
                            ToFailedAcquisition(cycleDateUtc, item.Company.CompanyId, item.Identity.NormalizedIsin!, acquisition),
                            cancellationToken);
                        failed++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failed++;
                    logger.LogError(
                        exception,
                        "CyclicalWaves metric acquisition failed unexpectedly. CompanyId={CompanyId} " +
                        "Isin={Isin} Metric={Metric} FailureCode={FailureCode}",
                        item.Company.CompanyId,
                        item.Identity.NormalizedIsin,
                        metricType,
                        CyclicalWavesAcquisitionFailureCodes.PersistenceFailure);
                }
                finally
                {
                    if (providerCalled && options.Value.RequestDelayMilliseconds > 0)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(options.Value.RequestDelayMilliseconds),
                            timeProvider,
                            cancellationToken);
                    }
                }
            }
        }

        return new CyclicalWavesAcquisitionCycleSummary(
            cycleDateUtc,
            changed,
            unchanged,
            failed,
            skipped);
    }

    public static CyclicalWavesIsinResolution ResolveIsin(CyclicalWavesAcquisitionCompany company)
    {
        var symbolIsin = NormalizeValidIsin(company.SymbolIsin);
        return symbolIsin is null
            ? new CyclicalWavesIsinResolution(
                null,
                CyclicalWavesAcquisitionFailureCodes.MissingSymbolIsin,
                "Eligible company has no valid symbol ISIN.")
            : new CyclicalWavesIsinResolution(symbolIsin, null, null);
    }

    private async Task PersistIdentityFailureAsync(
        DateOnly cycleDateUtc,
        CyclicalWavesAcquisitionCompany company,
        CyclicalWavesMetricType metricType,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await repository.PersistFailedAsync(
            new CyclicalWavesFailedAcquisition(
                cycleDateUtc,
                company.CompanyId,
                null,
                metricType,
                now,
                null,
                now,
                GetIntendedEndpoint(metricType),
                null,
                0,
                failureCode,
                failureMessage),
            cancellationToken);
    }

    private static CyclicalWavesFailedAcquisition ToFailedAcquisition(
        DateOnly cycleDateUtc,
        Guid companyId,
        string normalizedIsin,
        CyclicalWavesProviderAcquisitionResult result) =>
        new(
            cycleDateUtc,
            companyId,
            normalizedIsin,
            result.MetricType,
            result.CheckedAtUtc,
            result.RequestedAtUtc,
            result.CompletedAtUtc,
            result.SourceEndpoint,
            result.HttpStatusCode,
            result.AttemptCount,
            result.FailureCode ?? CyclicalWavesAcquisitionFailureCodes.UnexpectedFailure,
            result.FailureMessage ?? "CyclicalWaves acquisition failed.");

    private static string GetIntendedEndpoint(CyclicalWavesMetricType metricType) => metricType switch
    {
        CyclicalWavesMetricType.PS => "ps/circle-chart-data/{ISIN}",
        CyclicalWavesMetricType.LastPS => "ps-data/{ISIN}",
        CyclicalWavesMetricType.PE => "pe/circle-chart-data/{ISIN}",
        CyclicalWavesMetricType.LastPE => "pe-data/{ISIN}",
        CyclicalWavesMetricType.Equilibrium => "equilibrium/gauge/{ISIN}",
        _ => throw new ArgumentOutOfRangeException(nameof(metricType), metricType, null)
    };

    private static string? NormalizeValidIsin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length is >= 12 and <= 32 &&
               normalized.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            ? normalized
            : null;
    }

    private sealed record ResolvedCompany(
        CyclicalWavesAcquisitionCompany Company,
        CyclicalWavesIsinResolution Identity);
}

public sealed record CyclicalWavesIsinResolution(
    string? NormalizedIsin,
    string? FailureCode,
    string? FailureMessage);
