using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    IFinancialDataProviderHealthService
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
