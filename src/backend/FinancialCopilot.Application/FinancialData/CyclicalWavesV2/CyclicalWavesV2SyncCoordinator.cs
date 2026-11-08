using System.Text;
using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Application.FinancialData.CyclicalWavesV2;

/// <summary>
/// The single CyclicalWaves v2 synchronization use case, shared by the daily Worker and the Admin API.
/// Item failures are counted and the run continues; authentication or catalog failures end the run.
/// The error summary carries ticker and error category only, never payloads, tokens or credentials.
/// </summary>
public sealed class CyclicalWavesV2SyncCoordinator(
    ICyclicalWavesV2Client client,
    ICyclicalWavesV2Store store,
    ICyclicalWavesV2AnalysisStore analysisStore,
    ICyclicalWavesV2SyncRunStore runStore,
    CyclicalWavesV2SyncSettings settings,
    TimeProvider timeProvider,
    ICyclicalWavesV2GaugeClient? gaugeClient = null,
    ICyclicalWavesV2GaugeStore? gaugeStore = null) : ICyclicalWavesV2SyncCoordinator
{
    public const int MaximumErrorSummaryLength = 4_000;
    private const int MaximumAnalysisPages = 10_000;
    private static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);

    public async Task<CyclicalWavesV2SyncOutcome> RunAsync(
        CyclicalWavesV2SyncRequest request,
        string lockOwner,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var runId = await runStore.TryStartAsync(request, lockOwner, now, now + settings.Lease, cancellationToken);
        if (runId is null)
        {
            return new CyclicalWavesV2SyncOutcome(
                null,
                CyclicalWavesV2SyncRunStatus.SkippedAlreadyRunning,
                new CyclicalWavesV2RunCounts(0, 0, 0, 0, 0),
                null);
        }

        return await ExecuteAsync(runId.Value, request, cancellationToken);
    }

    public Task<CyclicalWavesV2SyncOutcome> RunClaimedAsync(
        CyclicalWavesV2ClaimedRun run,
        CancellationToken cancellationToken) =>
        ExecuteAsync(run.RunId, run.Request, cancellationToken);

    private async Task<CyclicalWavesV2SyncOutcome> ExecuteAsync(
        Guid runId,
        CyclicalWavesV2SyncRequest request,
        CancellationToken cancellationToken)
    {
        var run = new RunContext(runId);
        string? runFailure = null;

        try
        {
            switch (request.Scope)
            {
                case CyclicalWavesV2SyncScope.Catalog:
                    await SyncCatalogAsync(run, countItems: true, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.Ticker:
                    await SyncSingleTickerAsync(run, request.Ticker, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.Screener:
                    await SyncTickersAsync(run, includeScreener: true, includeStatements: false, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.IncomeStatements:
                    await SyncLineDefinitionsAsync(run, cancellationToken);
                    await SyncTickersAsync(run, includeScreener: false, includeStatements: true, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.Analysis:
                    await SyncAnalysesAsync(run, request.AnalysisMode ?? CyclicalWavesV2AnalysisMode.Incremental, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.GaugeCatalog:
                    await SyncGaugeCatalogAsync(run, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.GaugeDetails:
                    await SyncGaugeDetailsAsync(run, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.EquilibriumCatalog:
                    await SyncEquilibriumCatalogAsync(run, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.EquilibriumDetails:
                    await SyncEquilibriumDetailsAsync(run, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.TickerGaugeEquilibrium:
                    await SyncSingleGaugeTickerAsync(run, request.Ticker, cancellationToken);
                    break;
                case CyclicalWavesV2SyncScope.Full:
                    await SyncCatalogAsync(run, countItems: false, cancellationToken);
                    await SyncLineDefinitionsAsync(run, cancellationToken);
                    await SyncTickersAsync(run, includeScreener: true, includeStatements: true, cancellationToken);
                    await SyncGaugeCatalogAsync(run, cancellationToken);
                    await SyncGaugeDetailsAsync(run, cancellationToken);
                    await SyncEquilibriumCatalogAsync(run, cancellationToken);
                    await SyncEquilibriumDetailsAsync(run, cancellationToken);
                    await SyncAnalysesAsync(run, CyclicalWavesV2AnalysisMode.Incremental, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unknown sync scope.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            runFailure = "Cancelled";
            await CompleteAsync(run, runFailure, CancellationToken.None);
            throw;
        }
        catch (RunFailedException exception)
        {
            runFailure = exception.Category;
        }
        catch (Exception exception)
        {
            runFailure = Categorize(exception);
        }

        return await CompleteAsync(run, runFailure, cancellationToken);
    }

    private async Task SyncGaugeCatalogAsync(RunContext run, CancellationToken cancellationToken)
    {
        var client = gaugeClient ?? throw new InvalidOperationException("Gauge client is not registered.");
        var store = gaugeStore ?? throw new InvalidOperationException("Gauge store is not registered.");
        await PaceAsync(run, cancellationToken);
        var items = await client.GetGaugeCatalogAsync(cancellationToken);
        if (items.Count == 0) throw new RunFailedException("GaugeCatalogEmpty");
        var result = await store.ApplyGaugeCatalogAsync(items, timeProvider.GetUtcNow(), cancellationToken);
        run.Total += items.Count; run.Processed += items.Count; run.Updated += result.Updated; run.Unchanged += result.Unchanged; run.Failed += result.Unmatched;
        if (result.Unmatched > 0) run.AddError("gauge-catalog", "UnmatchedOrAmbiguous:" + result.Unmatched);
        run.AddPhase("GaugeCatalog", $"listed={items.Count} matched={result.Matched} unmatched={result.Unmatched} updated={result.Updated} unchanged={result.Unchanged}");
    }

    private async Task SyncEquilibriumCatalogAsync(RunContext run, CancellationToken cancellationToken)
    {
        var client = gaugeClient ?? throw new InvalidOperationException("Gauge client is not registered.");
        var store = gaugeStore ?? throw new InvalidOperationException("Gauge store is not registered.");
        await PaceAsync(run, cancellationToken);
        var items = await client.GetEquilibriumCatalogAsync(cancellationToken);
        if (items.Count == 0) throw new RunFailedException("EquilibriumCatalogEmpty");
        var result = await store.ApplyEquilibriumCatalogAsync(items, timeProvider.GetUtcNow(), cancellationToken);
        run.Total += items.Count; run.Processed += items.Count; run.Updated += result.Updated; run.Unchanged += result.Unchanged; run.Failed += result.Unmatched;
        if (result.Unmatched > 0) run.AddError("equilibrium-catalog", "UnmatchedOrAmbiguous:" + result.Unmatched);
        run.AddPhase("EquilibriumCatalog", $"listed={items.Count} matched={result.Matched} unmatched={result.Unmatched} updated={result.Updated} unchanged={result.Unchanged}");
    }

    private async Task SyncGaugeDetailsAsync(RunContext run, CancellationToken cancellationToken)
    {
        var client = gaugeClient ?? throw new InvalidOperationException("Gauge client is not registered.");
        var store = gaugeStore ?? throw new InvalidOperationException("Gauge store is not registered.");
        var targets = await store.GetGaugeTargetsAsync(cancellationToken); run.Total += targets.Count;
        var updatedBefore = run.Updated; var unchangedBefore = run.Unchanged; var failedBefore = run.Failed;
        foreach (var target in targets)
        {
            try
            {
                await PaceAsync(run, cancellationToken);
                var details = await client.GetGaugeDetailsAsync(target.Enticker ?? target.Ticker, cancellationToken);
                if (details is null) { await store.MarkGaugeUnavailableAsync(target.Enticker ?? target.Ticker, timeProvider.GetUtcNow(), cancellationToken); run.Failed++; run.AddError(target.Ticker, "GaugeNotFound"); }
                else if (await store.ApplyGaugeDetailsAsync(details, timeProvider.GetUtcNow(), cancellationToken) == CyclicalWavesV2ApplyOutcome.Updated) run.Updated++;
                else run.Unchanged++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            { ThrowIfRunFatal(exception); run.Failed++; run.AddError(target.Ticker, "Gauge:" + Categorize(exception)); }
            run.Processed++; await HeartbeatIfDueAsync(run, cancellationToken);
        }
        run.AddPhase("GaugeDetails", $"targets={targets.Count} processed={targets.Count} updated={run.Updated - updatedBefore} unchanged={run.Unchanged - unchangedBefore} failed={run.Failed - failedBefore}");
    }

    private async Task SyncEquilibriumDetailsAsync(RunContext run, CancellationToken cancellationToken)
    {
        var client = gaugeClient ?? throw new InvalidOperationException("Gauge client is not registered.");
        var store = gaugeStore ?? throw new InvalidOperationException("Gauge store is not registered.");
        var targets = await store.GetEquilibriumTargetsAsync(cancellationToken); run.Total += targets.Count;
        var updatedBefore = run.Updated; var unchangedBefore = run.Unchanged; var failedBefore = run.Failed;
        foreach (var target in targets)
        {
            try
            {
                await PaceAsync(run, cancellationToken);
                var identifier = target.Enticker ?? target.Ticker;
                var range = await client.GetEquilibriumRangeAsync(identifier, cancellationToken);
                if (range is null) { await store.MarkEquilibriumUnavailableAsync(identifier, timeProvider.GetUtcNow(), cancellationToken); run.Failed++; run.AddError(target.Ticker, "EquilibriumNotFound"); }
                else if (await store.ApplyEquilibriumRangeAsync(identifier, range, timeProvider.GetUtcNow(), cancellationToken) == CyclicalWavesV2ApplyOutcome.Updated) run.Updated++;
                else run.Unchanged++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            { ThrowIfRunFatal(exception); run.Failed++; run.AddError(target.Ticker, "Equilibrium:" + Categorize(exception)); }
            run.Processed++; await HeartbeatIfDueAsync(run, cancellationToken);
        }
        run.AddPhase("EquilibriumDetails", $"targets={targets.Count} processed={targets.Count} updated={run.Updated - updatedBefore} unchanged={run.Unchanged - unchangedBefore} failed={run.Failed - failedBefore}");
    }

    private async Task SyncSingleGaugeTickerAsync(RunContext run, string? ticker, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticker) || await store.FindCatalogTickerAsync(ticker, cancellationToken) is not { } canonical)
            throw new RunFailedException("TickerNotInCatalog");
        var client = gaugeClient ?? throw new InvalidOperationException("Gauge client is not registered.");
        var gaugeDataStore = gaugeStore ?? throw new InvalidOperationException("Gauge store is not registered.");
        run.Total = 2;
        await PaceAsync(run, cancellationToken);
        var details = await client.GetGaugeDetailsAsync(canonical, cancellationToken);
        if (details is null) { await gaugeDataStore.MarkGaugeUnavailableAsync(canonical, timeProvider.GetUtcNow(), cancellationToken); run.Failed++; run.AddError(canonical, "GaugeNotFound"); }
        else if (await gaugeDataStore.ApplyGaugeDetailsAsync(details, timeProvider.GetUtcNow(), cancellationToken) == CyclicalWavesV2ApplyOutcome.Updated) run.Updated++;
        else run.Unchanged++;
        await PaceAsync(run, cancellationToken);
        var range = await client.GetEquilibriumRangeAsync(canonical, cancellationToken);
        if (range is null) { await gaugeDataStore.MarkEquilibriumUnavailableAsync(canonical, timeProvider.GetUtcNow(), cancellationToken); run.Failed++; run.AddError(canonical, "EquilibriumNotFound"); }
        else if (await gaugeDataStore.ApplyEquilibriumRangeAsync(canonical, range, timeProvider.GetUtcNow(), cancellationToken) == CyclicalWavesV2ApplyOutcome.Updated) run.Updated++;
        else run.Unchanged++;
        run.Processed = 2;
    }

    private async Task<CyclicalWavesV2SyncOutcome> CompleteAsync(
        RunContext run,
        string? runFailure,
        CancellationToken cancellationToken)
    {
        var status = runFailure is not null
            ? CyclicalWavesV2SyncRunStatus.Failed
            : run.Failed > 0
                ? CyclicalWavesV2SyncRunStatus.CompletedWithErrors
                : CyclicalWavesV2SyncRunStatus.Succeeded;
        var summary = run.BuildSummary(runFailure);
        var counts = run.Counts;
        await runStore.CompleteAsync(run.RunId, status, counts, summary, timeProvider.GetUtcNow(), cancellationToken);
        return new CyclicalWavesV2SyncOutcome(run.RunId, status, counts, summary);
    }

    private async Task SyncCatalogAsync(RunContext run, bool countItems, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> tickers;
        try
        {
            await PaceAsync(run, cancellationToken);
            tickers = await client.GetTickersAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new RunFailedException("CatalogUnavailable:" + Categorize(exception));
        }

        // An empty catalog response would deactivate every ticker; treat it as an invalid provider answer.
        if (tickers.Count == 0)
        {
            throw new RunFailedException("CatalogEmpty");
        }

        var result = await store.UpsertCatalogAsync(tickers, timeProvider.GetUtcNow(), cancellationToken);
        run.AddPhase(
            "Catalog",
            $"tickers={tickers.Count} added={result.Added} deactivated={result.Deactivated} reactivated={result.Reactivated}");
        if (countItems)
        {
            run.Total += tickers.Count;
            run.Processed += tickers.Count;
            run.Updated += result.Added + result.Deactivated + result.Reactivated;
            run.Unchanged += result.Unchanged;
        }
    }

    private async Task SyncLineDefinitionsAsync(RunContext run, CancellationToken cancellationToken)
    {
        try
        {
            await PaceAsync(run, cancellationToken);
            var definitions = await client.GetIncomeStatementLinesAsync(cancellationToken);
            var changed = await store.UpsertLineDefinitionsAsync(definitions, timeProvider.GetUtcNow(), cancellationToken);
            run.AddPhase("Lines", $"definitions={definitions.Count} changed={changed}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            ThrowIfRunFatal(exception);
            // Unknown statement keys still create their own definitions, so statements can continue.
            run.AddError("lines", Categorize(exception));
        }
    }

    private async Task SyncSingleTickerAsync(RunContext run, string? ticker, CancellationToken cancellationToken)
    {
        var catalogTicker = string.IsNullOrWhiteSpace(ticker)
            ? null
            : await store.FindCatalogTickerAsync(ticker, cancellationToken);
        if (catalogTicker is null)
        {
            throw new RunFailedException("TickerNotInCatalog");
        }

        run.Total = 1;
        await SyncTickerAsync(run, catalogTicker, includeScreener: true, includeStatements: true, cancellationToken);
    }

    private async Task SyncTickersAsync(
        RunContext run,
        bool includeScreener,
        bool includeStatements,
        CancellationToken cancellationToken)
    {
        var tickers = await store.GetActiveTickersAsync(cancellationToken);
        run.Total += tickers.Count;
        var updatedBefore = run.Updated;
        var failedBefore = run.Failed;

        foreach (var ticker in tickers)
        {
            await SyncTickerAsync(run, ticker, includeScreener, includeStatements, cancellationToken);
        }

        run.AddPhase(
            includeScreener && includeStatements ? "Tickers" : includeScreener ? "Screener" : "IncomeStatements",
            $"tickers={tickers.Count} updated={run.Updated - updatedBefore} failed={run.Failed - failedBefore}");
    }

    private async Task SyncTickerAsync(
        RunContext run,
        string ticker,
        bool includeScreener,
        bool includeStatements,
        CancellationToken cancellationToken)
    {
        var changed = false;
        var failed = false;

        if (includeScreener)
        {
            try
            {
                await PaceAsync(run, cancellationToken);
                var snapshot = await client.GetScreenerAsync(ticker, cancellationToken);
                if (snapshot is null)
                {
                    failed = true;
                    run.AddError(ticker, "ScreenerNotFound");
                }
                else
                {
                    var result = await store.ApplyScreenerAsync(snapshot, timeProvider.GetUtcNow(), cancellationToken);
                    changed |= result.Outcome == CyclicalWavesV2ApplyOutcome.Updated;
                    if (result.Outcome == CyclicalWavesV2ApplyOutcome.NotInCatalog)
                    {
                        failed = true;
                        run.AddError(ticker, "ScreenerTickerNotInCatalog");
                    }

                    if (result.EntickerConflict)
                    {
                        failed = true;
                        run.AddError(ticker, "EntickerConflict");
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                ThrowIfRunFatal(exception);
                failed = true;
                run.AddError(ticker, "Screener:" + Categorize(exception));
            }
        }

        if (includeStatements)
        {
            try
            {
                await PaceAsync(run, cancellationToken);
                var statements = await client.GetIncomeStatementsAsync(ticker, settings.IncomeStatementLimit, cancellationToken);
                if (statements is not null)
                {
                    var result = await store.ApplyIncomeStatementsAsync(statements, timeProvider.GetUtcNow(), cancellationToken);
                    changed |= result.Outcome == CyclicalWavesV2ApplyOutcome.Updated;
                    if (result.Outcome == CyclicalWavesV2ApplyOutcome.NotInCatalog)
                    {
                        failed = true;
                        run.AddError(ticker, "StatementsTickerNotInCatalog");
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                ThrowIfRunFatal(exception);
                failed = true;
                run.AddError(ticker, "Statements:" + Categorize(exception));
            }
        }

        run.Processed++;
        if (failed)
        {
            run.Failed++;
        }
        else if (changed)
        {
            run.Updated++;
        }
        else
        {
            run.Unchanged++;
        }

        await HeartbeatIfDueAsync(run, cancellationToken);
    }

    private async Task SyncAnalysesAsync(
        RunContext run,
        CyclicalWavesV2AnalysisMode requestedMode,
        CancellationToken cancellationToken)
    {
        DateOnly? fromDate = null;
        var mode = requestedMode;
        if (mode == CyclicalWavesV2AnalysisMode.Incremental)
        {
            var latest = await analysisStore.GetMaxPublishedAtAsync(cancellationToken);
            if (latest is null)
            {
                mode = CyclicalWavesV2AnalysisMode.Full;
            }
            else
            {
                fromDate = DateOnly.FromDateTime(latest.Value.ToOffset(TehranOffset).DateTime)
                    .AddDays(-settings.AnalysisIncrementalOverlapDays);
            }
        }

        var inserted = 0;
        var updated = 0;
        var unchanged = 0;
        var pages = 0;
        for (var page = 1; page <= MaximumAnalysisPages; page++)
        {
            CyclicalWavesV2AnalysisPage result;
            try
            {
                await PaceAsync(run, cancellationToken);
                result = await client.GetAnalysesPageAsync(page, settings.AnalysisPageSize, fromDate, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                ThrowIfRunFatal(exception);
                // Continuing past a missing page would leave a silent gap; stop this phase and report it.
                run.Failed++;
                run.AddError($"analysis-page-{page}", Categorize(exception));
                break;
            }

            if (result.Items.Count == 0)
            {
                break;
            }

            pages++;
            var applied = await analysisStore.ApplyAnalysesPageAsync(result.Items, timeProvider.GetUtcNow(), cancellationToken);
            inserted += applied.Inserted;
            updated += applied.Updated;
            unchanged += applied.Unchanged;
            run.Total += result.Items.Count;
            run.Processed += result.Items.Count;
            run.Updated += applied.Inserted + applied.Updated;
            run.Unchanged += applied.Unchanged;
            await HeartbeatIfDueAsync(run, cancellationToken);

            if (page >= result.LastPage)
            {
                break;
            }
        }

        run.AddPhase(
            "Analysis",
            $"mode={mode} from={fromDate?.ToString("yyyy-MM-dd") ?? "-"} pages={pages} inserted={inserted} updated={updated} unchanged={unchanged}");
    }

    private async Task PaceAsync(RunContext run, CancellationToken cancellationToken)
    {
        if (run.ProviderCalls++ > 0 && settings.RequestDelay > TimeSpan.Zero)
        {
            await Task.Delay(settings.RequestDelay, timeProvider, cancellationToken);
        }
    }

    private async Task HeartbeatIfDueAsync(RunContext run, CancellationToken cancellationToken)
    {
        if (settings.HeartbeatEveryItems > 0 && run.Processed % settings.HeartbeatEveryItems == 0)
        {
            await runStore.HeartbeatAsync(run.RunId, timeProvider.GetUtcNow() + settings.Lease, cancellationToken);
        }
    }

    private static void ThrowIfRunFatal(Exception exception)
    {
        if (exception is FinancialProviderException { Code: FinancialProviderErrorCode.Unauthorized or FinancialProviderErrorCode.ConfigurationMissing } providerException)
        {
            throw new RunFailedException("Authentication:" + providerException.Code);
        }
    }

    private static string Categorize(Exception exception) => exception switch
    {
        RunFailedException runFailed => runFailed.Category,
        FinancialProviderException provider => "Provider" + provider.Code,
        OperationCanceledException => "Timeout",
        _ => exception.GetType().Name
    };

    private sealed class RunFailedException(string category) : Exception(category)
    {
        public string Category { get; } = category;
    }

    private sealed class RunContext(Guid runId)
    {
        private readonly List<string> _errors = [];
        private readonly List<string> _phases = [];

        public Guid RunId { get; } = runId;
        public int ProviderCalls { get; set; }
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Failed { get; set; }

        public CyclicalWavesV2RunCounts Counts => new(Total, Processed, Updated, Unchanged, Failed);

        public void AddError(string item, string category) => _errors.Add($"{item}: {category}");

        public void AddPhase(string phase, string detail) => _phases.Add($"{phase} {detail}");

        public string? BuildSummary(string? runFailure)
        {
            if (runFailure is null && _errors.Count == 0 && _phases.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            if (runFailure is not null)
            {
                builder.Append("RunFailed: ").Append(runFailure).Append('\n');
            }

            foreach (var phase in _phases)
            {
                builder.Append(phase).Append('\n');
            }

            foreach (var error in _errors)
            {
                if (builder.Length + error.Length + 32 > MaximumErrorSummaryLength)
                {
                    builder.Append("... ").Append(_errors.Count).Append(" errors in total");
                    break;
                }

                builder.Append(error).Append('\n');
            }

            var text = builder.ToString().TrimEnd();
            return text.Length <= MaximumErrorSummaryLength ? text : text[..MaximumErrorSummaryLength];
        }
    }
}
