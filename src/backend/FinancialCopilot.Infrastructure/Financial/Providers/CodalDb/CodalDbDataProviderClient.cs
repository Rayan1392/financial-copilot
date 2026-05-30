using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// CodalDB provider client. Implements the financial-data provider interfaces by querying CodalDB
/// (via <see cref="ICodalDbQueryExecutor"/>), serializing the result rows into a canonical JSON
/// <see cref="ProviderRawPayload"/> with a deterministic SHA-256 checksum, so the rest of the
/// ingestion pipeline (raw-payload audit, checksum dedup, normalizer selection) is reused unchanged.
/// Deliberately does NOT implement <c>IMarketDataProvider</c> — CodalDB is not a real-time quote source.
/// </summary>
public sealed class CodalDbDataProviderClient(
    ICodalDbQueryExecutor queryExecutor,
    IOptions<CodalDbProviderOptions> options,
    TimeProvider timeProvider)
    : ISymbolDataProvider, IFinancialStatementProvider, IMonthlyProductionSalesProvider, IFinancialDataProviderHealthService
{
    private readonly CodalDbProviderOptions _options = options.Value;

    public string ProviderName => _options.ProviderName;

    public async Task<ProviderRawPayload> FetchSymbolsAsync(CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryCompaniesAsync(cancellationToken);
        var serialized = CodalDbPayloadSerializer.Serialize(rows, row => row.CoID);
        return BuildPayload(ProviderDataset.Symbols, "codaldb://companies", "all", serialized);
    }

    public async Task<ProviderRawPayload> FetchFinancialStatementsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var companyId = ParseCompanyId(externalCompanyId);
        var rows = await queryExecutor.QueryStatementsAsync(companyId, cancellationToken);
        var serialized = CodalDbPayloadSerializer.Serialize(rows, row => row.Id);
        return BuildPayload(
            ProviderDataset.FinancialStatements,
            $"codaldb://statements/{companyId}",
            externalCompanyId,
            serialized);
    }

    public async Task<ProviderRawPayload> FetchMonthlyReportsAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var companyId = ParseCompanyId(externalCompanyId);
        var rows = await queryExecutor.QueryMonthlyActivityAsync(companyId, cancellationToken);
        var serialized = CodalDbPayloadSerializer.Serialize(rows, row => row.Id);
        return BuildPayload(
            ProviderDataset.MonthlyProductionSales,
            $"codaldb://monthly-activity/{companyId}",
            externalCompanyId,
            serialized);
    }

    public async Task<ProviderHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var probe = await queryExecutor.ProbeAsync(cancellationToken);
            var status = probe switch
            {
                { Reachable: true, CompanyCount: > 0 } => ProviderHealthStatus.Healthy,
                { Reachable: true } => ProviderHealthStatus.Degraded,
                _ => ProviderHealthStatus.Unavailable
            };
            var detail = probe.Detail ?? (probe.CompanyCount is { } count ? $"Companies={count}" : null);
            return new ProviderHealthResult(ProviderName, status, timeProvider.GetUtcNow(), detail);
        }
        catch (FinancialProviderException exception)
        {
            return new ProviderHealthResult(
                ProviderName,
                ProviderHealthStatus.Unavailable,
                timeProvider.GetUtcNow(),
                exception.Message);
        }
    }

    private ProviderRawPayload BuildPayload(
        ProviderDataset dataset,
        string endpoint,
        string externalReference,
        CodalDbSerializedPayload serialized) =>
        new(
            Guid.NewGuid(),
            ProviderName,
            dataset,
            endpoint,
            externalReference,
            serialized.Json,
            serialized.Checksum,
            timeProvider.GetUtcNow());

    private static int ParseCompanyId(string externalCompanyId) =>
        int.TryParse(externalCompanyId, out var companyId)
            ? companyId
            : throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"CodalDb external company id '{externalCompanyId}' is not a valid CoID.");
}
