using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Application.FinancialData.Providers;

public enum ProviderDataset
{
    Symbols,
    MarketQuotes,
    FinancialStatements,
    MonthlyProductionSales,
    FinancialRatios,
    FundamentalIndexes,
    /// <summary>
    /// All-index coverage staging (spec 050): every vendor fundamental index for a company/period,
    /// persisted to a non-scannable coverage table — distinct from the curated <see cref="FundamentalIndexes"/>
    /// promotion path that writes governed DerivedMetrics.
    /// </summary>
    FundamentalIndexCoverage,
    TradingInstruments,
    IntradayTrades,
    DailyTrades,
    IntradayIndices,
    DailyIndices
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

/// <summary>
/// Fetches NADPCO ProductSales outputs for an explicit company-month. The targeted operator path
/// deliberately excludes ServiceSales and persists product output types independently.
/// </summary>
public interface INadpcoMonthlyProductSalesDirectProvider
{
    Task<ProviderRawPayload> FetchProductSalesAllOutputTypesAsync(
        string externalCompanyId,
        int shamsiYear,
        int shamsiMonth,
        CancellationToken cancellationToken);
}

public interface IFinancialRatioProvider
{
    Task<ProviderRawPayload> FetchFinancialRatiosAsync(
        string externalCompanyId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fetches the complete set of vendor fundamental indexes for a company over a Shamsi year range,
/// with no curated index allowlist (spec 050 all-index catch-up). Distinct from
/// <see cref="IFinancialRatioProvider"/>, whose fundamental-index fetch is the curated subset.
/// </summary>
public interface IFundamentalIndexCoverageProvider
{
    Task<ProviderRawPayload> FetchAllFundamentalIndexesAsync(
        string externalCompanyId,
        int fromShamsiYear,
        int toShamsiYear,
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
    FinancialSourceEvidence SourceEvidence,
    DateOnly TradingDate = default,
    string SourceLabel = "");

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
