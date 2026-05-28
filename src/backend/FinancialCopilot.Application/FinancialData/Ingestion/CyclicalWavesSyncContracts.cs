namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed record CyclicalWavesFullSyncResult(
    int SymbolsSynced,
    int TickersSynced,
    int TickersFailed,
    IReadOnlyCollection<string> FailedTickers,
    TimeSpan Duration);

public interface ICyclicalWavesFullSyncService
{
    Task<CyclicalWavesFullSyncResult> ExecuteAsync(CancellationToken cancellationToken);
}
