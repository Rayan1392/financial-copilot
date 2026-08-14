using System.Net;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesDataAcquisitionResilienceHandler(
    IOptions<CyclicalWavesDataAcquisitionOptions> options,
    TimeProvider timeProvider) : DelegatingHandler
{
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(60);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        for (var attempt = 0; attempt <= settings.RetryCount; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            HttpResponseMessage? response = null;

            try
            {
                using var attemptRequest = await CloneAsync(request, timeout.Token);
                RecordAttempt(attemptRequest);
                response = await base.SendAsync(attemptRequest, timeout.Token);
                await response.Content.LoadIntoBufferAsync(timeout.Token);

                if (!IsTransient(response.StatusCode) || attempt == settings.RetryCount)
                {
                    var completedResponse = response;
                    response = null;
                    return completedResponse;
                }

                var delay = GetRetryDelay(response, attempt);
                response.Dispose();
                response = null;
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == settings.RetryCount)
                {
                    throw new FinancialProviderException(
                        FinancialProviderErrorCode.Timeout,
                        "CyclicalWaves data request timed out.",
                        exception);
                }

                await Task.Delay(GetBackoff(attempt), timeProvider, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                if (attempt == settings.RetryCount)
                {
                    throw new FinancialProviderException(
                        FinancialProviderErrorCode.RemoteUnavailable,
                        "CyclicalWaves data endpoint is unavailable.",
                        exception);
                }

                await Task.Delay(GetBackoff(attempt), timeProvider, cancellationToken);
            }
            finally
            {
                response?.Dispose();
            }
        }

        throw new InvalidOperationException("CyclicalWaves retry policy exhausted without a terminal outcome.");
    }

    private void RecordAttempt(HttpRequestMessage request)
    {
        if (request.Options.TryGetValue(CyclicalWavesAcquisitionRequestOptions.Context, out var context))
        {
            context.RecordAttempt(timeProvider.GetUtcNow());
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter;
            var value = retryAfter?.Delta;
            if (value is null && retryAfter?.Date is { } retryDate)
            {
                value = retryDate - timeProvider.GetUtcNow();
            }

            if (value.HasValue && value.Value > TimeSpan.Zero && value.Value <= MaximumRetryAfter)
            {
                return value.Value;
            }
        }

        return GetBackoff(attempt);
    }

    private static TimeSpan GetBackoff(int attempt)
    {
        var exponentialMilliseconds = Math.Min(5_000, 250 * Math.Pow(2, attempt));
        var jitterMilliseconds = Random.Shared.Next(25, 126);
        return TimeSpan.FromMilliseconds(exponentialMilliseconds + jitterMilliseconds);
    }

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

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
