using System.Net;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesDataAcquisitionClient(
    HttpClient httpClient,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesDataAcquisitionClient> logger) : ICyclicalWavesDataAcquisitionClient
{
    private const int ResponsePreviewMaximumLength = 512;
    private static readonly string[] CircleChartNumericFields =
        ["a", "b", "c", "d", "e", "f", "close", "start", "end", "min", "max", "avg"];

    private static readonly string[] EquilibriumNumericFields =
        ["a", "b", "c", "d", "e", "f", "close", "balance", "maxbalance", "minbalance", "volume", "growth"];

    public async Task<CyclicalWavesProviderAcquisitionResult> AcquireAsync(
        CyclicalWavesMetricType metricType,
        string normalizedIsin,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedIsin);

        var checkedAtUtc = timeProvider.GetUtcNow();
        var endpoint = GetEndpoint(metricType, normalizedIsin);
        var attemptContext = new CyclicalWavesAcquisitionRequestContext();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            request.Headers.Pragma.ParseAdd("no-cache");
            request.Options.Set(CyclicalWavesAcquisitionRequestOptions.Context, attemptContext);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            {
                return Failure(
                    metricType,
                    endpoint,
                    checkedAtUtc,
                    attemptContext,
                    response.StatusCode,
                    CyclicalWavesAcquisitionFailureCodes.NotFoundOrNoData,
                    "CyclicalWaves returned no data for this metric.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var preview = await ReadPreviewAsync(response, cancellationToken);
                logger.LogWarning(
                    "CyclicalWaves authentication response rejected after recovery. Metric={Metric} Endpoint={Endpoint} " +
                    "StatusCode={StatusCode} ContentType={ContentType} RedirectLocation={RedirectLocation} " +
                    "ResponsePreview={ResponsePreview}",
                    metricType,
                    endpoint,
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    response.Headers.Location?.ToString(),
                    preview);
                return Failure(
                    metricType,
                    endpoint,
                    checkedAtUtc,
                    attemptContext,
                    response.StatusCode,
                    CyclicalWavesAcquisitionFailureCodes.AuthenticationFailed,
                    $"CyclicalWaves authentication failed after controlled token recovery. {preview}");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden ||
                (int)response.StatusCode is >= 300 and <= 399)
            {
                var preview = await ReadPreviewAsync(response, cancellationToken);
                logger.LogWarning(
                    "CyclicalWaves authentication response rejected. Metric={Metric} Endpoint={Endpoint} " +
                    "StatusCode={StatusCode} ContentType={ContentType} RedirectLocation={RedirectLocation} " +
                    "ResponsePreview={ResponsePreview}",
                    metricType,
                    endpoint,
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    response.Headers.Location?.ToString(),
                    preview);
                return Failure(
                    metricType,
                    endpoint,
                    checkedAtUtc,
                    attemptContext,
                    response.StatusCode,
                    CyclicalWavesAcquisitionFailureCodes.AuthenticationFailed,
                    $"CyclicalWaves authentication response was rejected: HTTP {(int)response.StatusCode}. {preview}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var preview = await ReadPreviewAsync(response, cancellationToken);
                logger.LogWarning(
                    "CyclicalWaves request returned an unsuccessful response. Metric={Metric} Endpoint={Endpoint} " +
                    "StatusCode={StatusCode} ContentType={ContentType} RedirectLocation={RedirectLocation} " +
                    "ResponsePreview={ResponsePreview}",
                    metricType,
                    endpoint,
                    (int)response.StatusCode,
                    response.Content.Headers.ContentType?.ToString(),
                    response.Headers.Location?.ToString(),
                    preview);
                return Failure(
                    metricType,
                    endpoint,
                    checkedAtUtc,
                    attemptContext,
                    response.StatusCode,
                    MapStatusFailure(response.StatusCode),
                    $"CyclicalWaves returned HTTP status {(int)response.StatusCode}.");
            }

            var rawResponseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var validationFailure = ValidateResponse(metricType, normalizedIsin, rawResponseJson);
            if (validationFailure is not null)
            {
                return Failure(
                    metricType,
                    endpoint,
                    checkedAtUtc,
                    attemptContext,
                    response.StatusCode,
                    validationFailure.Value.Code,
                    validationFailure.Value.Message);
            }

            return new CyclicalWavesProviderAcquisitionResult(
                metricType,
                endpoint,
                checkedAtUtc,
                attemptContext.FirstRequestedAtUtc,
                attemptContext.LastRequestedAtUtc,
                timeProvider.GetUtcNow(),
                rawResponseJson,
                (int)response.StatusCode,
                attemptContext.AttemptCount,
                null,
                null,
                ExtractProviderObservationDate(metricType, rawResponseJson));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FinancialProviderException exception)
        {
            var code = exception.Code switch
            {
                FinancialProviderErrorCode.Timeout => CyclicalWavesAcquisitionFailureCodes.Timeout,
                FinancialProviderErrorCode.Unauthorized => CyclicalWavesAcquisitionFailureCodes.AuthenticationFailed,
                FinancialProviderErrorCode.RemoteUnavailable => CyclicalWavesAcquisitionFailureCodes.NetworkError,
                _ => CyclicalWavesAcquisitionFailureCodes.UnexpectedFailure
            };

            return Failure(
                metricType,
                endpoint,
                checkedAtUtc,
                attemptContext,
                null,
                code,
                exception.Message);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Failure(
                metricType,
                endpoint,
                checkedAtUtc,
                attemptContext,
                null,
                CyclicalWavesAcquisitionFailureCodes.UnexpectedFailure,
                "CyclicalWaves response processing failed.");
        }
    }

    public static string GetEndpoint(CyclicalWavesMetricType metricType, string normalizedIsin)
    {
        var escapedIsin = Uri.EscapeDataString(normalizedIsin);
        return metricType switch
        {
            CyclicalWavesMetricType.PS => $"ps/circle-chart-data/{escapedIsin}",
            CyclicalWavesMetricType.LastPS => $"ps-data/{escapedIsin}",
            CyclicalWavesMetricType.PE => $"pe/circle-chart-data/{escapedIsin}",
            CyclicalWavesMetricType.LastPE => $"pe-data/{escapedIsin}",
            CyclicalWavesMetricType.Equilibrium => $"equilibrium/gauge/{escapedIsin}",
            _ => throw new ArgumentOutOfRangeException(nameof(metricType), metricType, null)
        };
    }

    private static (string Code, string Message)? ValidateResponse(
        CyclicalWavesMetricType metricType,
        string normalizedIsin,
        string rawResponseJson)
    {
        if (string.IsNullOrWhiteSpace(rawResponseJson))
        {
            return (CyclicalWavesAcquisitionFailureCodes.InvalidJson, "Provider response was empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawResponseJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
        }
        catch (JsonException)
        {
            return (CyclicalWavesAcquisitionFailureCodes.InvalidJson, "Provider response was not valid JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (CyclicalWavesAcquisitionFailureCodes.ContractMismatch, "Provider response root was not an object.");
            }

            if (!AllNumbersAreSupported(document.RootElement))
            {
                return (CyclicalWavesAcquisitionFailureCodes.ContractMismatch, "Provider response contained an unsupported number.");
            }

            if (HasDuplicateProperties(document.RootElement))
            {
                return (CyclicalWavesAcquisitionFailureCodes.InvalidJson, "Provider response contained duplicate object properties.");
            }

            if (metricType is CyclicalWavesMetricType.LastPS or CyclicalWavesMetricType.LastPE)
            {
                var ratioField = metricType == CyclicalWavesMetricType.LastPS ? "ps_ratio" : "pe_ratio";
                if (!document.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Object ||
                    !HasString(data, "symbol") ||
                    !HasString(data, "ticker") ||
                    !HasSupportedNumber(data, ratioField) ||
                    !HasSupportedNumber(data, "close") ||
                    !TryGetProviderDate(data, out _))
                {
                    return (CyclicalWavesAcquisitionFailureCodes.ContractMismatch,
                        "Provider response lacked the required latest valuation data fields.");
                }

                if (!string.Equals(
                        NormalizeIsin(data.GetProperty("ticker").GetString()),
                        normalizedIsin,
                        StringComparison.Ordinal))
                {
                    return (CyclicalWavesAcquisitionFailureCodes.IdentityMismatch,
                        "Provider response identity did not match the requested ISIN.");
                }

                return null;
            }

            var requiredFields = metricType == CyclicalWavesMetricType.Equilibrium
                ? EquilibriumNumericFields
                : CircleChartNumericFields;

            if (requiredFields.Any(field => !HasSupportedNumber(document.RootElement, field)))
            {
                return (CyclicalWavesAcquisitionFailureCodes.ContractMismatch, "Provider response lacked required numeric gauge fields.");
            }

            if (metricType == CyclicalWavesMetricType.Equilibrium &&
                document.RootElement.TryGetProperty("enticker", out var identity))
            {
                if (identity.ValueKind != JsonValueKind.String ||
                    !string.Equals(
                        NormalizeIsin(identity.GetString()),
                        normalizedIsin,
                        StringComparison.Ordinal))
                {
                    return (CyclicalWavesAcquisitionFailureCodes.IdentityMismatch, "Provider response identity did not match the requested ISIN.");
                }
            }
        }

        return null;
    }

    private static bool HasSupportedNumber(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out _);

    private static bool HasString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static bool TryGetProviderDate(JsonElement data, out DateOnly date)
    {
        date = default;
        return data.TryGetProperty("date", out var value) &&
               value.ValueKind == JsonValueKind.String &&
               DateOnly.TryParse(value.GetString(), out date);
    }

    private static DateOnly? ExtractProviderObservationDate(
        CyclicalWavesMetricType metricType,
        string rawResponseJson)
    {
        if (metricType is not (CyclicalWavesMetricType.LastPS or CyclicalWavesMetricType.LastPE))
            return null;

        using var document = JsonDocument.Parse(rawResponseJson);
        return document.RootElement.TryGetProperty("data", out var data) &&
               TryGetProviderDate(data, out var date)
            ? date
            : null;
    }

    private static bool AllNumbersAreSupported(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out _),
            JsonValueKind.Object => element.EnumerateObject().All(property => AllNumbersAreSupported(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().All(AllNumbersAreSupported),
            _ => true
        };
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperties);
        }

        return false;
    }

    private CyclicalWavesProviderAcquisitionResult Failure(
        CyclicalWavesMetricType metricType,
        string endpoint,
        DateTimeOffset checkedAtUtc,
        CyclicalWavesAcquisitionRequestContext attemptContext,
        HttpStatusCode? statusCode,
        string failureCode,
        string failureMessage) =>
        new(
            metricType,
            endpoint,
            checkedAtUtc,
            attemptContext.FirstRequestedAtUtc,
            null,
            timeProvider.GetUtcNow(),
            null,
            statusCode is null ? null : (int)statusCode.Value,
            attemptContext.AttemptCount,
            failureCode,
            Sanitize(failureMessage));

    private static string MapStatusFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.RequestTimeout => CyclicalWavesAcquisitionFailureCodes.Timeout,
        HttpStatusCode.TooManyRequests => CyclicalWavesAcquisitionFailureCodes.RateLimited,
        _ when (int)statusCode >= 500 => CyclicalWavesAcquisitionFailureCodes.ProviderServerError,
        _ => CyclicalWavesAcquisitionFailureCodes.HttpClientError
    };

    private static string? NormalizeIsin(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(value.Length, 1_000)];

    private static async Task<string> ReadPreviewAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Sanitize(body)[..Math.Min(body.Length, ResponsePreviewMaximumLength)];
    }
}
