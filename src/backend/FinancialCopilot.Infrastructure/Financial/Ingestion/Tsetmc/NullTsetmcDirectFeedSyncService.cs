using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;

/// <summary>
/// Stand-in for the future direct TSETMC web-service ingestion adapter (spec 054, Phase 2).
/// Always reports non-operational so callers fall back to the StockMarketDB bridge.
/// Replace with a real implementation once the TSETMC ASMX client is built.
/// </summary>
public sealed class NullTsetmcDirectFeedSyncService : ITsetmcDirectFeedSyncService
{
    public bool IsOperational => false;
}
