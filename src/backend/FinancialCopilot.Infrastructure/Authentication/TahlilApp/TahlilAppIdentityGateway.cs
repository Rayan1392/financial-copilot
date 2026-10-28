using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Server-to-server TahlilApp exchange and status client using the confirmed partner API contract.
/// The named HttpClient is registered without any retry or resilience handler: the exchange is not idempotent.
/// Transport and protocol failures are returned as outcomes; no response body or code is ever logged.
/// </summary>
public sealed class TahlilAppIdentityGateway(
    HttpClient httpClient,
    IOptions<TahlilAppOptions> options,
    TimeProvider timeProvider,
    ILogger<TahlilAppIdentityGateway> logger) : ITahlilAppIdentityGateway
{
    /// <summary>The status response is bounded to 8 KB.</summary>
    private const int StatusMaximumResponseBytes = 8192;

    public async Task<TahlilAppExchangeResult> ExchangeAsync(
        string code,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var clientSecret = Environment.GetEnvironmentVariable(settings.ClientSecretEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError("TahlilApp service credential is not configured. CorrelationId: {CorrelationId}", correlationId);
            return Unavailable();
        }

        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildPartnerUri(settings.BaseUrl, settings.ExchangePath));
        SetBasicAuthentication(request, settings.ClientId, clientSecret);
        request.Content = new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.ExchangeTimeoutSeconds));

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                logger.LogInformation(
                    "TahlilApp exchange rejected the code. Status: {Status}; LatencyMs: {LatencyMs}; CorrelationId: {CorrelationId}",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, correlationId);
                return new TahlilAppExchangeResult(TahlilAppExchangeOutcome.InvalidCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "TahlilApp exchange failed. Status: {Status}; LatencyMs: {LatencyMs}; CorrelationId: {CorrelationId}",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, correlationId);
                return Unavailable();
            }

            if (response.Content.Headers.ContentType?.MediaType != "application/json")
            {
                logger.LogWarning("TahlilApp exchange returned an unexpected content type. CorrelationId: {CorrelationId}", correlationId);
                return Unavailable();
            }

            var body = await ReadBoundedAsync(response.Content, settings.MaximumResponseBytes, timeout.Token);
            if (body is null)
            {
                logger.LogWarning("TahlilApp exchange response exceeded the size limit. CorrelationId: {CorrelationId}", correlationId);
                return Unavailable();
            }

            var snapshot = TahlilAppExchangeParser.TryParse(body, timeProvider.GetUtcNow());
            if (snapshot is null)
            {
                logger.LogWarning("TahlilApp exchange response failed validation. CorrelationId: {CorrelationId}", correlationId);
                return Unavailable();
            }

            logger.LogInformation(
                "TahlilApp exchange succeeded. LatencyMs: {LatencyMs}; ExternalPermissionCount: {PermissionCount}; CorrelationId: {CorrelationId}",
                stopwatch.ElapsedMilliseconds, snapshot.ExternalPermissionCount, correlationId);
            return new TahlilAppExchangeResult(TahlilAppExchangeOutcome.Succeeded, snapshot);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("TahlilApp exchange timed out. LatencyMs: {LatencyMs}; CorrelationId: {CorrelationId}", stopwatch.ElapsedMilliseconds, correlationId);
            return Unavailable();
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("TahlilApp exchange transport failed. CorrelationId: {CorrelationId}", correlationId);
            return Unavailable();
        }
    }

    /// <summary>
    /// Reads the confirmed per-user status endpoint. One bounded GET, no retry, and no redirects.
    /// Only the outcome category is returned; no body, PII or credential is logged.
    /// </summary>
    public async Task<TahlilAppStatusResult> GetStatusAsync(
        string externalUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var clientSecret = Environment.GetEnvironmentVariable(settings.ClientSecretEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            logger.LogError("TahlilApp service credential is not configured. CorrelationId: {CorrelationId}", correlationId);
            return StatusUnavailable();
        }

        var path = settings.StatusPathTemplate.Replace(
            "{externalUserId}",
            Uri.EscapeDataString(externalUserId),
            StringComparison.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildPartnerUri(settings.BaseUrl, path));
        SetBasicAuthentication(request, settings.ClientId, clientSecret);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.StatusTimeoutSeconds));

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation(
                    "TahlilApp status reported no such user. Status: {Status}; LatencyMs: {LatencyMs}; CorrelationId: {CorrelationId}",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, correlationId);
                return new TahlilAppStatusResult(TahlilAppStatusOutcome.NotFound);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "TahlilApp status call failed. Status: {Status}; LatencyMs: {LatencyMs}; CorrelationId: {CorrelationId}",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, correlationId);
                return StatusUnavailable();
            }

            if (response.Content.Headers.ContentType?.MediaType != "application/json")
            {
                logger.LogWarning("TahlilApp status returned an unexpected content type. CorrelationId: {CorrelationId}", correlationId);
                return StatusUnavailable();
            }

            var body = await ReadBoundedAsync(response.Content, StatusMaximumResponseBytes, timeout.Token);
            if (body is null)
            {
                logger.LogWarning("TahlilApp status response exceeded the size limit. CorrelationId: {CorrelationId}", correlationId);
                return StatusUnavailable();
            }

            var snapshot = TahlilAppStatusParser.TryParse(body, externalUserId, timeProvider.GetUtcNow(), out var rejectedField);
            if (snapshot is null)
            {
                // Field name only: a rejected response may contain PII, so its value is never logged.
                logger.LogWarning(
                    "TahlilApp status response failed validation. Field: {Field}; CorrelationId: {CorrelationId}",
                    rejectedField, correlationId);
                return StatusUnavailable();
            }

            logger.LogInformation(
                "TahlilApp status succeeded. LatencyMs: {LatencyMs}; CorrelationId: {CorrelationId}",
                stopwatch.ElapsedMilliseconds, correlationId);
            return new TahlilAppStatusResult(TahlilAppStatusOutcome.Succeeded, snapshot);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("TahlilApp status timed out. LatencyMs: {LatencyMs}; CorrelationId: {CorrelationId}", stopwatch.ElapsedMilliseconds, correlationId);
            return StatusUnavailable();
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("TahlilApp status transport failed. CorrelationId: {CorrelationId}", correlationId);
            return StatusUnavailable();
        }
    }

    private static TahlilAppExchangeResult Unavailable() => new(TahlilAppExchangeOutcome.Unavailable);

    private static void SetBasicAuthentication(HttpRequestMessage request, string clientId, string clientSecret)
    {
        var bytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    private static Uri BuildPartnerUri(string baseUrl, string relativePath) =>
        new(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), relativePath);

    private static TahlilAppStatusResult StatusUnavailable() => new(TahlilAppStatusOutcome.Unavailable);

    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maximumBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
