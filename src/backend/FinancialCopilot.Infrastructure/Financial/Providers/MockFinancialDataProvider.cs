using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Infrastructure.Financial.Providers;

public sealed class MockFinancialDataProvider(
    IProviderRawPayloadStore rawPayloads,
    TimeProvider timeProvider) :
    ISymbolDataProvider,
    IFinancialStatementProvider,
    IMonthlyProductionSalesProvider,
    IMarketDataProvider,
    IFinancialDataProviderHealthService
{
    public const string ProviderName = "DeterministicMockFinancialProvider";

    public Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken) =>
        CreateAndStorePayloadAsync(
            ProviderDataset.Symbols,
            "/mock/symbols",
            "all",
            """[{"symbol":"LIVE","company":"Live Quote Company"},{"symbol":"FALLBACK","company":"Fallback Quote Company"}]""",
            cancellationToken);

    public Task<ProviderRawPayload> FetchFinancialStatementsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        CreateAndStorePayloadAsync(
            ProviderDataset.FinancialStatements,
            $"/mock/financial-statements/{externalCompanyId}",
            RequireReference(externalCompanyId),
            $$"""{"companyId":"{{externalCompanyId}}","netProfit":1500,"period":"ThreeMonths"}""",
            cancellationToken);

    public Task<ProviderRawPayload> FetchMonthlyReportsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        CreateAndStorePayloadAsync(
            ProviderDataset.MonthlyProductionSales,
            $"/mock/monthly-reports/{externalCompanyId}",
            RequireReference(externalCompanyId),
            $$"""{"companyId":"{{externalCompanyId}}","salesAmount":800,"period":"Monthly"}""",
            cancellationToken);

    public Task<BatchMarketQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var observedAt = timeProvider.GetUtcNow();
        var observations = new List<MarketQuoteObservation>();
        var unavailable = new List<SymbolCode>();

        foreach (var symbol in symbols)
        {
            var observation = symbol.Value switch
            {
                "LIVE" => CreateQuote(symbol, 23_450m, 2.4m, observedAt, MarketQuoteSource.LiveQuote),
                "FALLBACK" => CreateQuote(
                    symbol,
                    14_100m,
                    -0.8m,
                    observedAt.AddDays(-1),
                    MarketQuoteSource.PreviousTradingDay),
                _ => null
            };

            if (observation is null)
            {
                unavailable.Add(symbol);
            }
            else
            {
                observations.Add(observation);
            }
        }

        return Task.FromResult(new BatchMarketQuoteResult(observations, unavailable));
    }

    public Task<ProviderHealthResult> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderHealthResult(
            ProviderName,
            ProviderHealthStatus.Healthy,
            timeProvider.GetUtcNow(),
            "Deterministic mock provider is available."));

    private async Task<ProviderRawPayload> CreateAndStorePayloadAsync(
        ProviderDataset dataset,
        string endpoint,
        string externalReference,
        string payload,
        CancellationToken cancellationToken)
    {
        var document = new ProviderRawPayload(
            Guid.NewGuid(),
            ProviderName,
            dataset,
            endpoint,
            externalReference,
            payload,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            timeProvider.GetUtcNow());
        await rawPayloads.StoreAsync(document, cancellationToken);
        return document;
    }

    private static MarketQuoteObservation CreateQuote(
        SymbolCode symbol,
        decimal price,
        decimal changePercentage,
        DateTimeOffset asOf,
        MarketQuoteSource source) =>
        new(
            symbol,
            price,
            changePercentage,
            asOf,
            source,
            new FinancialSourceEvidence(ProviderName, asOf, asOf));

    private static string RequireReference(string externalCompanyId) =>
        string.IsNullOrWhiteSpace(externalCompanyId)
            ? throw new ArgumentException("External company id is required.", nameof(externalCompanyId))
            : externalCompanyId.Trim();
}
