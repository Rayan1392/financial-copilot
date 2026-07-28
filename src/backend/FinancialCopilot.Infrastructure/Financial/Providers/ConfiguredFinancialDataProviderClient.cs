using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers;

public sealed class ConfiguredFinancialDataProviderClient(
    HttpClient httpClient,
    IProviderRawPayloadStore rawPayloads,
    IOptions<FinancialProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<ConfiguredFinancialDataProviderClient> logger) :
    ISymbolDataProvider,
    IFinancialStatementProvider,
    IMonthlyProductionSalesProvider,
    IMarketDataProvider,
    IFinancialDataProviderHealthService
{
    private readonly FinancialProviderOptions _settings = options.Value;

    public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
        FetchRawAsync(ProviderDataset.Symbols, "symbols", "all", cancellationToken);

    public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        FetchRawAsync(
            ProviderDataset.FinancialStatements,
            $"financial-statements/{Uri.EscapeDataString(RequireReference(externalCompanyId))}",
            externalCompanyId,
            cancellationToken);

    public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        FetchRawAsync(
            ProviderDataset.MonthlyProductionSales,
            $"monthly-reports/{Uri.EscapeDataString(RequireReference(externalCompanyId))}",
            externalCompanyId,
            cancellationToken);

    public async Task<BatchMarketQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return new BatchMarketQuoteResult([], []);
        }

        var symbolText = string.Join(",", symbols.Select(symbol => symbol.Value));
        using var response = await SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"market/quotes?symbols={Uri.EscapeDataString(symbolText)}"),
            cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var observations = JsonSerializer.Deserialize<ConfiguredQuoteObservation[]>(payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "Provider quote response was empty.");
        var requested = symbols.ToDictionary(symbol => symbol.Value, StringComparer.OrdinalIgnoreCase);
        var mapped = observations
            .Where(observation => requested.ContainsKey(observation.Symbol))
            .Select(observation =>
            {
                var source = observation.IsLive
                    ? MarketQuoteSource.LiveQuote
                    : MarketQuoteSource.PreviousTradingDay;
                return new MarketQuoteObservation(
                    requested[observation.Symbol],
                    observation.LatestPrice,
                    observation.PriceChangePercentage,
                    observation.AsOf,
                    source,
                    new FinancialSourceEvidence(
                        _settings.ProviderName,
                        observation.AsOf,
                        timeProvider.GetUtcNow()));
            })
            .ToArray();
        var returnedCodes = mapped.Select(item => item.SymbolCode.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new BatchMarketQuoteResult(
            mapped,
            symbols.Where(symbol => !returnedCodes.Contains(symbol.Value)).ToArray());
    }

    public async Task<ProviderHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "health"), cancellationToken);
            return new ProviderHealthResult(
                _settings.ProviderName,
                ProviderHealthStatus.Healthy,
                timeProvider.GetUtcNow());
        }
        catch (FinancialProviderException exception)
        {
            logger.LogWarning(exception, "Financial data provider health check failed.");
            return new ProviderHealthResult(
                _settings.ProviderName,
                ProviderHealthStatus.Unavailable,
                timeProvider.GetUtcNow(),
                exception.Code.ToString());
        }
    }

    private async Task<ProviderRawPayload> FetchRawAsync(
        ProviderDataset dataset,
        string endpoint,
        string externalReference,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, endpoint), cancellationToken);
        var payloadText = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = new ProviderRawPayload(
            Guid.NewGuid(),
            _settings.ProviderName,
            dataset,
            endpoint,
            externalReference,
            payloadText,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadText))),
            timeProvider.GetUtcNow());
        await rawPayloads.StoreAsync(payload, cancellationToken);
        return payload;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var code = response.StatusCode == HttpStatusCode.Unauthorized
                ? FinancialProviderErrorCode.Unauthorized
                : FinancialProviderErrorCode.RemoteUnavailable;
            logger.LogWarning(
                "Financial data provider returned {StatusCode} for {Endpoint}.",
                response.StatusCode,
                request.RequestUri);
            response.Dispose();
            throw new FinancialProviderException(code, $"Provider request failed for '{request.RequestUri}'.");
        }
        catch (FinancialProviderException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Financial data provider request failed for {Endpoint}.", request.RequestUri);
            throw new FinancialProviderException(
                FinancialProviderErrorCode.RemoteUnavailable,
                "Financial data provider request failed.",
                exception);
        }
    }

    private static string RequireReference(string externalCompanyId) =>
        string.IsNullOrWhiteSpace(externalCompanyId)
            ? throw new ArgumentException("External company id is required.", nameof(externalCompanyId))
            : externalCompanyId.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ConfiguredQuoteObservation(
        string Symbol,
        decimal LatestPrice,
        decimal PriceChangePercentage,
        DateTimeOffset AsOf,
        bool IsLive);
}
