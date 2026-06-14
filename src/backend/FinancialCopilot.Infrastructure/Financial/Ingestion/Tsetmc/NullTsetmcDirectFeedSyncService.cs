using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;

/// <summary>
/// Stand-in used when TsetmcWebService is disabled or credentials are absent.
/// All sync operations throw; callers must check <see cref="IsOperational"/> first.
/// </summary>
public sealed class NullTsetmcDirectFeedSyncService : ITsetmcDirectFeedSyncService
{
    public bool IsOperational => false;

    public Task<TsetmcSyncResult> SynchronizeInstrumentsAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TsetmcWebService is not operational.");

    public Task<TsetmcSyncResult> SynchronizeIntradayTradesAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TsetmcWebService is not operational.");

    public Task<TsetmcSyncResult> SynchronizeDailyTradesAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TsetmcWebService is not operational.");

    public Task<TsetmcSyncResult> SynchronizeDailyIndicesAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TsetmcWebService is not operational.");

    public Task<TsetmcSyncResult> SynchronizeIntradayIndicesAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TsetmcWebService is not operational.");
}
