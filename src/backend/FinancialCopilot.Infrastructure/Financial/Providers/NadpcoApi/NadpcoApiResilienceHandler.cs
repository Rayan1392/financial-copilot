using System.Net;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

public sealed class NadpcoApiResilienceHandler(
    IOptions<NadpcoApiProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<NadpcoApiResilienceHandler> logger) : DelegatingHandler
{
    private readonly object _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _circuitOpenUntil;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        EnsureCircuitAllowsRequest(settings);

        for (var attempt = 0; attempt <= settings.RetryCount; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            try
            {
                using var attemptRequest = await CloneAsync(request, timeout.Token);
                var response = await base.SendAsync(attemptRequest, timeout.Token);

                if (!IsTransient(response.StatusCode))
                {
                    RecordSuccess();
                    return response;
                }

                if (attempt == settings.RetryCount)
                {
                    RecordFailure(settings);
                    return response;
                }

                response.Dispose();
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == settings.RetryCount)
                {
                    RecordFailure(settings);
                    throw new FinancialProviderException(
                        FinancialProviderErrorCode.Timeout,
                        "NADPCO API request timed out.",
                        exception);
                }
            }
            catch (HttpRequestException exception) when (attempt == settings.RetryCount)
            {
                RecordFailure(settings);
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.RemoteUnavailable,
                    "NADPCO API is unavailable.",
                    exception);
            }
        }

        throw new InvalidOperationException("NADPCO resilience handler exhausted without a response.");
    }

    private void EnsureCircuitAllowsRequest(NadpcoApiProviderOptions settings)
    {
        lock (_gate)
        {
            if (_circuitOpenUntil > timeProvider.GetUtcNow())
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.RemoteUnavailable,
                    $"Provider circuit is open for '{settings.ProviderName}'.");
            }

            if (_circuitOpenUntil <= timeProvider.GetUtcNow())
            {
                _circuitOpenUntil = null;
                _consecutiveFailures = 0;
            }
        }
    }

    private void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _circuitOpenUntil = null;
        }
    }

    private void RecordFailure(NadpcoApiProviderOptions settings)
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= settings.CircuitFailureThreshold)
            {
                _circuitOpenUntil = timeProvider.GetUtcNow().AddSeconds(settings.CircuitBreakSeconds);
                logger.LogWarning("NADPCO API provider circuit opened for {ProviderName}.", settings.ProviderName);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
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
            var content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(content);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
