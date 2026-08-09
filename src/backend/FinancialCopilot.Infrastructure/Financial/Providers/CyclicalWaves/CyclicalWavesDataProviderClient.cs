using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesDataProviderClient(
    HttpClient httpClient,
    IProviderRawPayloadStore rawPayloads,
    IOptions<CyclicalWavesProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesDataProviderClient> logger) :
    ISymbolDataProvider,
    IFinancialStatementProvider,
    IMonthlyProductionSalesProvider,
    IFinancialDataProviderHealthService,
    ICyclicalWavesPsProviderClient
{
    private readonly CyclicalWavesProviderOptions _settings = options.Value;
    private static readonly SemaphoreSlim _throttle = new(10, 10);

    public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
        FetchRawAsync(ProviderDataset.Symbols, "custom-filtering/tickers", "all", cancellationToken);

    public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        FetchRawAsync(
            ProviderDataset.FinancialStatements,
            $"custom-filtering/ticker/{Uri.EscapeDataString(RequireTicker(externalCompanyId))}",
            externalCompanyId,
            cancellationToken);

    public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        FetchRawAsync(
            ProviderDataset.MonthlyProductionSales,
            $"custom-filtering/ticker/{Uri.EscapeDataString(RequireTicker(externalCompanyId))}",
            externalCompanyId,
            cancellationToken);

    public async Task<ProviderHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(
                    new { user_name = _settings.UserName, password = _settings.Password })
            };
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var status = response.IsSuccessStatusCode
                ? ProviderHealthStatus.Healthy
                : ProviderHealthStatus.Unavailable;
            return new ProviderHealthResult(_settings.ProviderName, status, timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "CyclicalWaves health check failed.");
            return new ProviderHealthResult(
                _settings.ProviderName,
                ProviderHealthStatus.Unavailable,
                timeProvider.GetUtcNow(),
                exception.Message);
        }
    }

    public async Task<PsProviderResult<PsGaugeDistribution>> GetGaugeAsync(string symbolIsin, CancellationToken cancellationToken)
    {
        var result = await GetPsAsync<CyclicalWavesPsGaugePayload>($"ps/circle-chart-data/{Uri.EscapeDataString(RequireTicker(symbolIsin))}", cancellationToken);
        return result.Value is null
            ? new PsProviderResult<PsGaugeDistribution>(null, result.ErrorCode, result.WarningCode)
            : new PsProviderResult<PsGaugeDistribution>(new PsGaugeDistribution(result.Value.A, result.Value.B, result.Value.C, result.Value.D, result.Value.E, result.Value.F, result.Value.Close, result.Value.Start, result.Value.Min, result.Value.Average, result.Value.Max, result.Value.End), result.ErrorCode, result.WarningCode);
    }

    public async Task<PsProviderResult<PsCurrentValues>> GetCurrentValuesAsync(string symbolIsin, CancellationToken cancellationToken)
    {
        var result = await GetPsAsync<CyclicalWavesPsCurrentEnvelope>($"ps-data/{Uri.EscapeDataString(RequireTicker(symbolIsin))}", cancellationToken);
        var value = result.Value?.Data;
        if (value is null) return new PsProviderResult<PsCurrentValues>(null, result.ErrorCode, result.WarningCode);
        if (string.IsNullOrWhiteSpace(value.Ticker) || value.PsRatio is null || value.Date is null)
            return new PsProviderResult<PsCurrentValues>(null, PsVisualizationSyncErrorCode.InvalidJsonOrContract, "RequiredCurrentValueFieldMissing");
        return new PsProviderResult<PsCurrentValues>(new PsCurrentValues(value.Symbol?.Trim() ?? string.Empty, value.Ticker.Trim(), value.PsRatio.Value, 0m, value.Date.Value), PsVisualizationSyncErrorCode.None);
    }

    public async Task<PsProviderResult<PsForwardValues>> GetForwardValuesAsync(string companySymbol, CancellationToken cancellationToken)
    {
        var result = await GetPsAsync<CyclicalWavesPsForwardEnvelope>($"futureprediction/{Uri.EscapeDataString(RequireTicker(companySymbol))}", cancellationToken);
        var value = result.Value?.Data;
        if (value is null || !result.Value!.Success || string.IsNullOrWhiteSpace(value.Symbol) || value.Ps is null)
            return new PsProviderResult<PsForwardValues>(null, result.ErrorCode == PsVisualizationSyncErrorCode.None ? PsVisualizationSyncErrorCode.InvalidJsonOrContract : result.ErrorCode, result.WarningCode);
        return new PsProviderResult<PsForwardValues>(new PsForwardValues(value.Symbol.Trim(), value.Ps.Value), PsVisualizationSyncErrorCode.None);
    }

    public async Task<PsProviderResult<PsHistorySeries>> GetHistoryAsync(string symbolIsin, CancellationToken cancellationToken)
    {
        var result = await GetPsAsync<CyclicalWavesPsHistoryPayload>($"ps/{Uri.EscapeDataString(RequireTicker(symbolIsin))}", cancellationToken);
        var value = result.Value;
        if (value is null) return new PsProviderResult<PsHistorySeries>(null, result.ErrorCode, result.WarningCode);
        if (value.Data is null || value.Data.Count > _settings.PsMaxHistoryPointsPerCompany)
            return new PsProviderResult<PsHistorySeries>(null, PsVisualizationSyncErrorCode.InvalidJsonOrContract, "HistoryPointLimitExceeded");
        var points = new List<PsHistoryPoint>(value.Data.Count);
        foreach (var point in value.Data)
        {
            if (string.IsNullOrWhiteSpace(point.Id) || point.Date is null || point.Ps is null)
                return new PsProviderResult<PsHistorySeries>(null, PsVisualizationSyncErrorCode.InvalidJsonOrContract, "InvalidHistoryPoint");
            points.Add(new PsHistoryPoint(point.Id.Trim(), point.Date.Value, point.Ps.Value));
        }
        return new PsProviderResult<PsHistorySeries>(new PsHistorySeries(points, value.FirstDate, value.LastDate, value.DataCount), PsVisualizationSyncErrorCode.None);
    }

    private async Task<PsProviderResult<T>> GetPsAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Origin", "https://tahlilapp.com");
            request.Headers.Referrer = new Uri("https://tahlilapp.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
                return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.NotFoundOrNoData);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.RateLimited, response.Headers.RetryAfter?.Delta is { } retry ? $"RetryAfterSeconds:{(int)retry.TotalSeconds}" : null);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.AuthenticationFailed);
            if ((int)response.StatusCode >= 500)
                return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.RemoteServerFailure);
            if (!response.IsSuccessStatusCode)
                return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.InvalidJsonOrContract, $"Http{(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength is > 0 and var length && length > _settings.PsMaxResponseBytes)
                return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.PayloadTooLarge);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > _settings.PsMaxResponseBytes)
                    return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.PayloadTooLarge);
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            var value = JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)), JsonOptions);
            return value is null
                ? new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.InvalidJsonOrContract, "EmptyJson")
                : new PsProviderResult<T>(value, PsVisualizationSyncErrorCode.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.Cancelled);
        }
        catch (HttpRequestException)
        {
            return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.TimeoutOrNetworkFailure);
        }
        catch (JsonException)
        {
            return new PsProviderResult<T>(default, PsVisualizationSyncErrorCode.InvalidJsonOrContract);
        }
    }

    private async Task<ProviderRawPayload> FetchRawAsync(
        ProviderDataset dataset,
        string endpoint,
        string externalReference,
        CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            using var response = await httpClient.GetAsync(endpoint, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.RemoteUnavailable,
                    $"CyclicalWaves returned {response.StatusCode} for '{endpoint}'.");
            }

            var payloadText = await response.Content.ReadAsStringAsync(cancellationToken);
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadText)));
            var payload = new ProviderRawPayload(
                Guid.NewGuid(),
                _settings.ProviderName,
                dataset,
                endpoint,
                externalReference,
                payloadText,
                checksum,
                timeProvider.GetUtcNow());
            await rawPayloads.StoreAsync(payload, cancellationToken);
            return payload;
        }
        catch (FinancialProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "CyclicalWaves request failed for {Endpoint}.", endpoint);
            throw new FinancialProviderException(
                FinancialProviderErrorCode.RemoteUnavailable,
                $"CyclicalWaves request failed for '{endpoint}'.",
                exception);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private static string RequireTicker(string ticker) =>
        string.IsNullOrWhiteSpace(ticker)
            ? throw new ArgumentException("Ticker is required.", nameof(ticker))
            : ticker.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
