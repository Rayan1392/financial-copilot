using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesPsOperation(ICyclicalWavesPsProviderClient client)
    : ICyclicalWavesPsAcceptedOperation
{
    public Task<PsProviderResult<PsGaugeDistribution>> AcquireAcceptedPsGaugeAsync(
        string approvedSymbolIsin,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(approvedSymbolIsin))
            throw new ArgumentException("An admitted SymbolIsin is required.", nameof(approvedSymbolIsin));
        return client.GetGaugeAsync(approvedSymbolIsin.Trim(), cancellationToken);
    }
}
