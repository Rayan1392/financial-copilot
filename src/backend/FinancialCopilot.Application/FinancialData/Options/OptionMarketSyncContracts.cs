namespace FinancialCopilot.Application.FinancialData.Options;

/// <summary>
/// Ingests listed option contracts (terms, open interest, order book) from the market data
/// provider. Callers must check <see cref="IsOperational"/> first.
/// </summary>
public interface IOptionMarketSyncService
{
    bool IsOperational { get; }

    /// <summary>Synchronizes contract terms and open interest of every listed option contract.</summary>
    Task<OptionMarketSyncResult> SynchronizeContractsAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the stored order book of every known option contract with the provider's current one.</summary>
    Task<OptionMarketSyncResult> SynchronizeOrderBookAsync(CancellationToken cancellationToken);
}

/// <param name="Dataset">Dataset name as it appears in the sync-state monitor.</param>
/// <param name="RowsFetched">Rows returned by the provider.</param>
/// <param name="RowsPersisted">Rows inserted or updated.</param>
/// <param name="RowsUnchanged">Rows already stored with the same values.</param>
/// <param name="RowsRejected">Rows that broke a validation rule.</param>
/// <param name="RowsUnresolved">Rows whose contract or underlying instrument is not in the instrument catalog yet.</param>
/// <param name="RowsRemoved">Stored rows deleted because the provider no longer reports them.</param>
/// <param name="RowsIgnored">Rows outside the scope of the dataset: contracts that are not listed calls or puts, order-book rows of other instruments.</param>
public sealed record OptionMarketSyncResult(
    string Dataset,
    int RowsFetched,
    int RowsPersisted,
    int RowsUnchanged,
    int RowsRejected,
    int RowsUnresolved,
    int RowsRemoved,
    int RowsIgnored,
    TimeSpan Duration);

/// <summary>
/// Gives instruments that were first seen through a symbol-less trade feed their real symbol,
/// using daily trade payloads already stored.
/// </summary>
public interface IInstrumentStubSymbolBackfill
{
    Task<InstrumentStubBackfillResult> BackfillAsync(CancellationToken cancellationToken);
}

/// <param name="StubsFound">Instruments whose symbol was still the numeric instrument code.</param>
/// <param name="StubsRenamed">Stubs that received a symbol.</param>
/// <param name="StubsSkipped">Stubs for which no stored payload carried a symbol.</param>
/// <param name="PayloadsRead">Stored daily trade payloads read.</param>
public sealed record InstrumentStubBackfillResult(
    int StubsFound,
    int StubsRenamed,
    int StubsSkipped,
    int PayloadsRead);
