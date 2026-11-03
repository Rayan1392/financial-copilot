using System.Net;
using System.Net.Http.Headers;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;

/// <summary>
/// Adds the bearer token to data calls. A 401 triggers exactly one token renewal and one replay;
/// a second 401 is returned to the caller.
/// </summary>
public sealed class CyclicalWavesV2AuthHandler(CyclicalWavesV2TokenService tokenService) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var renewed = await tokenService.RenewAfterRejectionAsync(token, cancellationToken);
        using var replay = await CyclicalWavesV2ResilienceHandler.CloneAsync(request, cancellationToken);
        replay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        return await base.SendAsync(replay, cancellationToken);
    }
}

/// <summary>
/// Per-attempt timeout plus bounded, jittered retries for 5xx, 408, timeouts and connection failures.
/// Other 4xx answers are definitive and are never retried.
/// </summary>
public sealed class CyclicalWavesV2ResilienceHandler(
    IOptions<CyclicalWavesV2Options> options,
    TimeProvider timeProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        for (var attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var isLastAttempt = attempt >= settings.RetryCount;

            try
            {
                using var attemptRequest = await CloneAsync(request, cancellationToken);
                var response = await base.SendAsync(attemptRequest, timeout.Token);
                if (!IsTransient(response.StatusCode) || isLastAttempt)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (isLastAttempt)
                {
                    throw new FinancialProviderException(
                        FinancialProviderErrorCode.Timeout,
                        "CyclicalWaves v2 request timed out.",
                        exception);
                }
            }
            catch (HttpRequestException exception)
            {
                if (isLastAttempt)
                {
                    throw new FinancialProviderException(
                        FinancialProviderErrorCode.RemoteUnavailable,
                        "CyclicalWaves v2 is unavailable.",
                        exception);
                }
            }

            await Task.Delay(Backoff(attempt), timeProvider, cancellationToken);
        }
    }

    internal static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt) + Random.Shared.Next(0, 250));

    /// <summary>Clones a request; all v2 calls are GETs or small JSON bodies, so buffering the content is cheap.</summary>
    internal static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            clone.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
