using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>Feature 115 standalone P/S gauge backed only by Feature 148 latest-state rows.</summary>
public sealed class CyclicalWavesV2PsGaugeVisualizationUseCase(
    FinancialIngestionDbContext db,
    IOptions<CyclicalWavesPsVisualizationOptions> options,
    TimeProvider clock) : IPsVisualizationExperienceUseCase
{
    public async Task<PsVisualizationResult?> ExecuteAsync(PsVisualizationQuery query, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.EnableStandaloneGauge || query.IncludeInMonthlySalesTrendChart || string.IsNullOrWhiteSpace(query.SymbolOrCompanyName)) return null;
        var normalized = PersianSymbolNormalizer.Normalize(query.SymbolOrCompanyName);
        var matches = await db.CyclicalWavesV2Tickers.AsNoTracking().Where(x => x.IsActive && x.NormalizedTicker == normalized).ToArrayAsync(cancellationToken);
        if (matches.Length != 1) return null;
        var ticker = matches[0];
        var fetchedAt = await db.CyclicalWavesV2GaugeDetails.AsNoTracking()
            .Where(x => x.TickerId == ticker.Id && x.GaugeType == "PS")
            .Select(x => (DateTimeOffset?)x.FetchedAtUtc).SingleOrDefaultAsync(cancellationToken);
        var available = await db.CyclicalWavesV2GaugeAvailability.AsNoTracking().Where(x => x.TickerId == ticker.Id)
            .Select(x => x.HasPs).SingleOrDefaultAsync(cancellationToken);
        var row = available ? await db.CyclicalWavesV2GaugeDetails.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TickerId == ticker.Id && x.GaugeType == "PS", cancellationToken) : null;
        var historyRequested = query.IncludeHistory && settings.IncludeHistoryInStandaloneGauge;
        if (row is null)
            return Result(ticker.Id, ticker.Ticker, PsVisualizationStatus.Unavailable, GaugeRenderabilityStatus.ProviderUnavailable,
                null, null, null, null, null, null, null, null, [], null, false, historyRequested ? PsVisualizationStatus.Unavailable : PsVisualizationStatus.NotRequested,
                fetchedAt, ["GaugeUnavailable"]);

        var age = clock.GetUtcNow() - row.FetchedAtUtc;
        var fresh = age <= TimeSpan.FromHours(settings.MaxSyncAgeHours);
        var close = ToDecimal(row.Close);
        var start = ToDecimal(row.Start); var end = ToDecimal(row.End); var min = ToDecimal(row.Min); var avg = ToDecimal(row.Avg); var max = ToDecimal(row.Max);
        if (new[] { close, start, end, min, avg, max }.Any(x => x is null))
            return Result(ticker.Id, ticker.Ticker, fresh ? PsVisualizationStatus.Partial : PsVisualizationStatus.Stale,
                GaugeRenderabilityStatus.InvalidBoundaries, null, null, close, start, end, min, max, avg, [], null,
                false, historyRequested ? PsVisualizationStatus.Unavailable : PsVisualizationStatus.NotRequested,
                row.FetchedAtUtc, historyRequested ? new[] { "GaugePrecisionOrCloseUnavailable", "HistoryUnsupported" } : new[] { "GaugePrecisionOrCloseUnavailable" });

        if (!fresh && !settings.AllowStaleStandaloneGauge)
            return Result(ticker.Id, ticker.Ticker, PsVisualizationStatus.Stale, GaugeRenderabilityStatus.Renderable,
                null, null, close, start, end, min, max, avg, [], null, false,
                historyRequested ? PsVisualizationStatus.Unavailable : PsVisualizationStatus.NotRequested,
                row.FetchedAtUtc, historyRequested ? new[] { "StaleSnapshotHidden", "HistoryUnsupported" } : new[] { "StaleSnapshotHidden" });

        var counts = new long[] { row.A, row.B, row.C, row.D, row.E, row.F };
        if (counts.Sum() <= 0)
            return Result(ticker.Id, ticker.Ticker, PsVisualizationStatus.Invalid, GaugeRenderabilityStatus.InvalidBucketTotal,
                null, null, close, start, end, min, max, avg, [], null, false,
                historyRequested ? PsVisualizationStatus.Unavailable : PsVisualizationStatus.NotRequested,
                row.FetchedAtUtc, historyRequested ? new[] { "InvalidBucketTotal", "HistoryUnsupported" } : new[] { "InvalidBucketTotal" });
        if (!(start!.Value < min!.Value && min.Value < max!.Value && max.Value < end!.Value))
            return Result(ticker.Id, ticker.Ticker, PsVisualizationStatus.Invalid, GaugeRenderabilityStatus.InvalidBoundaries,
                null, null, close, start, end, min, max, avg, [], null, false,
                historyRequested ? PsVisualizationStatus.Unavailable : PsVisualizationStatus.NotRequested,
                row.FetchedAtUtc, historyRequested ? new[] { "InvalidBoundaries", "HistoryUnsupported" } : new[] { "InvalidBoundaries" });
        var gauge = PsGaugeCalculator.Calculate(counts, start.Value, min.Value, max.Value, end.Value, close!.Value, settings.DisplayPercentageDecimals);
        return Result(ticker.Id, ticker.Ticker, fresh ? PsVisualizationStatus.Fresh : PsVisualizationStatus.Stale,
            GaugeRenderabilityStatus.Renderable, null, null, close, start, end, min, max, avg, gauge.Bands, gauge.Needle,
            false, historyRequested ? PsVisualizationStatus.Unavailable : PsVisualizationStatus.NotRequested,
            row.FetchedAtUtc, historyRequested ? new[] { "HistoryUnsupported" } : Array.Empty<string>());
    }

    private static PsVisualizationResult Result(Guid id, string ticker, PsVisualizationStatus status,
        GaugeRenderabilityStatus renderability, decimal? ttm, decimal? forward, decimal? close,
        decimal? start, decimal? end, decimal? min, decimal? max, decimal? average, IReadOnlyList<PsGaugeBand> bands, PsGaugeNeedle? needle,
        bool historyIncluded, PsVisualizationStatus historyStatus, DateTimeOffset? fetchedAt, IReadOnlyList<string> warnings) =>
        new(1, id, ticker, ticker, "CyclicalWaves v2", null, status, renderability,
            new PsVisualizationCurrentValue(ttm, PsValueState.Missing), new PsVisualizationCurrentValue(forward, PsValueState.Missing),
            new PsVisualizationCurrentValue(close, close is null ? PsValueState.Missing : PsValueState.Present), bands, needle,
            start, end, min, max, average, historyIncluded, historyStatus, Array.Empty<PsVisualizationHistoryPoint>(), 0, false, fetchedAt, warnings);

    private static decimal? ToDecimal(FinancialCopilot.Domain.Financial.CyclicalWavesV2.ExactDecimal? value)
    {
        if (value is null || !decimal.TryParse(value.Value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result)) return null;
        return FinancialCopilot.Domain.Financial.CyclicalWavesV2.ExactDecimal.Parse(result.ToString(CultureInfo.InvariantCulture)) == value.Value ? result : null;
    }
}
