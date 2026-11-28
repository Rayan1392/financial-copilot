namespace FinancialCopilot.Application.FinancialData.CyclicalWavesV2;

public enum CodalAnnouncementKind
{
    MonthlyReport,
    FinancialStatement
}

/// <summary>A CodalMirror post about one symbol; carries identifiers only, never the post text.</summary>
public sealed record CodalAnnouncementGaugeRefreshRequest(
    string Symbol,
    CodalAnnouncementKind Kind,
    long TelegramChatId,
    long TelegramMessageId,
    string? CorrelationId);

/// <summary>
/// Asks for the CyclicalWaves v2 gauges and equilibrium range of an announced symbol to be fetched again.
/// Implementations queue the work and return; they never call the provider and never throw, except for
/// cancellation, so the caller's own handling of the announcement is not affected.
/// </summary>
public interface ICodalAnnouncementGaugeRefreshTrigger
{
    Task RequestAsync(CodalAnnouncementGaugeRefreshRequest request, CancellationToken cancellationToken);
}

public enum CodalAnnouncementGaugeRefreshStatus
{
    Disabled,
    SymbolMissing,
    NotInCatalog,
    Queued,
    Duplicate,
    QueueFull
}

/// <param name="Immediate">Outcome of the request that is due at once.</param>
/// <param name="Deferred">Outcome of the later re-fetch; null when no re-fetch is configured or nothing was queued.</param>
public sealed record CodalAnnouncementGaugeRefreshOutcome(
    CodalAnnouncementGaugeRefreshStatus Status,
    string? Ticker,
    CyclicalWavesV2AnnouncementEnqueueResult? Immediate,
    CyclicalWavesV2AnnouncementEnqueueResult? Deferred);

/// <param name="Enabled">Ingestion and the announcement switch are both on.</param>
/// <param name="RecheckDelay">Delay of the second fetch; zero means no second fetch.</param>
public sealed record CodalAnnouncementGaugeRefreshSettings(
    bool Enabled,
    TimeSpan RecheckDelay,
    int MaximumPending = CodalAnnouncementGaugeRefreshSettings.DefaultMaximumPending)
{
    public const int DefaultMaximumPending = 500;
}

/// <summary>
/// Queues the gauge and equilibrium refresh of an announced symbol: once at once and, when configured, once
/// more later, because it is not known how soon the provider recalculates after a filing.
/// </summary>
public sealed class CodalAnnouncementGaugeRefresh(
    ICyclicalWavesV2Store store,
    ICyclicalWavesV2SyncRunStore runs,
    CodalAnnouncementGaugeRefreshSettings settings,
    TimeProvider timeProvider)
{
    public async Task<CodalAnnouncementGaugeRefreshOutcome> RequestAsync(
        CodalAnnouncementGaugeRefreshRequest request,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return new(CodalAnnouncementGaugeRefreshStatus.Disabled, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(request.Symbol))
        {
            return new(CodalAnnouncementGaugeRefreshStatus.SymbolMissing, null, null, null);
        }

        var ticker = await store.FindCatalogTickerAsync(request.Symbol.Trim(), cancellationToken);
        if (ticker is null)
        {
            return new(CodalAnnouncementGaugeRefreshStatus.NotInCatalog, null, null, null);
        }

        var now = timeProvider.GetUtcNow();
        var requestedBy = $"codal:{request.Kind}:{request.TelegramChatId}:{request.TelegramMessageId}";
        var immediate = await runs.EnqueueAnnouncementRefreshAsync(
            ticker, requestedBy, now, notBeforeUtc: null, settings.MaximumPending, cancellationToken);
        CyclicalWavesV2AnnouncementEnqueueResult? deferred = settings.RecheckDelay > TimeSpan.Zero
            ? await runs.EnqueueAnnouncementRefreshAsync(
                ticker, requestedBy, now, now + settings.RecheckDelay, settings.MaximumPending, cancellationToken)
            : null;

        var status = immediate == CyclicalWavesV2AnnouncementEnqueueResult.Queued ||
                     deferred == CyclicalWavesV2AnnouncementEnqueueResult.Queued
            ? CodalAnnouncementGaugeRefreshStatus.Queued
            : immediate == CyclicalWavesV2AnnouncementEnqueueResult.QueueFull ||
              deferred == CyclicalWavesV2AnnouncementEnqueueResult.QueueFull
                ? CodalAnnouncementGaugeRefreshStatus.QueueFull
                : CodalAnnouncementGaugeRefreshStatus.Duplicate;
        return new(status, ticker, immediate, deferred);
    }
}
