using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Infrastructure.Financial.Providers;

/// <summary>
/// Name-keyed registry of coexisting financial-data providers, built at the composition root where
/// concrete provider types and their names are known. This keeps provider selection out of the
/// provider interfaces themselves and avoids a service-locator dependency in the processor.
/// Lookups are case-insensitive.
/// </summary>
public sealed class FinancialDataProviderRouter : IFinancialDataProviderRouter
{
    private readonly IReadOnlyDictionary<string, ISymbolDataProvider> _symbolProviders;
    private readonly IReadOnlyDictionary<string, IFinancialStatementProvider> _statementProviders;
    private readonly IReadOnlyDictionary<string, IMonthlyProductionSalesProvider> _monthlyProviders;

    public FinancialDataProviderRouter(
        IReadOnlyDictionary<string, ISymbolDataProvider> symbolProviders,
        IReadOnlyDictionary<string, IFinancialStatementProvider> statementProviders,
        IReadOnlyDictionary<string, IMonthlyProductionSalesProvider> monthlyProviders)
    {
        _symbolProviders = Normalize(symbolProviders);
        _statementProviders = Normalize(statementProviders);
        _monthlyProviders = Normalize(monthlyProviders);
    }

    public ISymbolDataProvider? ResolveSymbolProvider(string providerName) =>
        _symbolProviders.GetValueOrDefault(providerName);

    public IFinancialStatementProvider? ResolveStatementProvider(string providerName) =>
        _statementProviders.GetValueOrDefault(providerName);

    public IMonthlyProductionSalesProvider? ResolveMonthlyProvider(string providerName) =>
        _monthlyProviders.GetValueOrDefault(providerName);

    private static IReadOnlyDictionary<string, T> Normalize<T>(IReadOnlyDictionary<string, T> source) =>
        new Dictionary<string, T>(source, StringComparer.OrdinalIgnoreCase);
}
