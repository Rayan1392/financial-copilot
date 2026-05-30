namespace FinancialCopilot.Application.FinancialData.Providers;

/// <summary>
/// Resolves a financial-data provider by its provider name for a given dataset, so ingestion can
/// target a specific coexisting provider (e.g. CyclicalWaves vs. CodalDb) via
/// <c>DataSyncRequest.ProviderName</c>. Returns <c>null</c> when no provider is registered under the
/// name, letting callers fall back to the configured primary provider.
/// </summary>
public interface IFinancialDataProviderRouter
{
    ISymbolDataProvider? ResolveSymbolProvider(string providerName);

    IFinancialStatementProvider? ResolveStatementProvider(string providerName);

    IMonthlyProductionSalesProvider? ResolveMonthlyProvider(string providerName);
}
