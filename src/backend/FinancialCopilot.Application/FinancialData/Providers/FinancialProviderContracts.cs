using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Application.FinancialData.Providers;

public enum ProviderDataset
{
    Symbols,
    MarketQuotes,
    FinancialStatements,
    MonthlyProductionSales
}

public sealed record ProviderFetchRequest(
    ProviderDataset Dataset,
    string? ExternalReference = null,
    DateOnly? AsOf = null);

public sealed record ProviderRawPayload(
    Guid Id,
    string ProviderName,
    ProviderDataset Dataset,
    string Endpoint,
    string ExternalReference,
    string Payload,
    string Checksum,
    DateTimeOffset ReceivedAt);

public interface IProviderRawPayloadStore
{
    Task StoreAsync(ProviderRawPayload payload, CancellationToken cancellationToken);

    Task<ProviderRawPayload?> FindByChecksumAsync(
        string providerName,
        string checksum,
        CancellationToken cancellationToken);
}

public interface ISymbolDataProvider
{
    Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken);
}

public interface IFinancialStatementProvider
{
    Task<ProviderRawPayload> FetchFinancialStatementsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken);
}

public interface IMonthlyProductionSalesProvider
{
    Task<ProviderRawPayload> FetchMonthlyReportsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken);
}

public enum MarketQuoteSource
{
    LiveQuote,
    PreviousTradingDay
}

public sealed record MarketQuoteObservation(
    SymbolCode SymbolCode,
    decimal LatestPrice,
    decimal PriceChangePercentage,
    DateTimeOffset AsOf,
    MarketQuoteSource Source,
    FinancialSourceEvidence SourceEvidence);

public sealed record BatchMarketQuoteResult(
    IReadOnlyCollection<MarketQuoteObservation> Observations,
    IReadOnlyCollection<SymbolCode> UnavailableSymbols);

public interface IMarketDataProvider
{
    Task<BatchMarketQuoteResult> GetLatestQuotesAsync(
        IReadOnlyCollection<SymbolCode> symbols,
        CancellationToken cancellationToken);
}

public enum ProviderHealthStatus
{
    Healthy,
    Degraded,
    Unavailable
}

public sealed record ProviderHealthResult(
    string ProviderName,
    ProviderHealthStatus Status,
    DateTimeOffset CheckedAt,
    string? Detail = null);

public interface IFinancialDataProviderHealthService
{
    Task<ProviderHealthResult> CheckAsync(CancellationToken cancellationToken);
}

public enum FinancialProviderErrorCode
{
    ConfigurationMissing,
    Timeout,
    Unauthorized,
    RemoteUnavailable,
    InvalidResponse
}

public sealed class FinancialProviderException(
    FinancialProviderErrorCode code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public FinancialProviderErrorCode Code { get; } = code;
}
