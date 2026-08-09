using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesPsVisualizationOptions
{
    public const string SectionName = "CyclicalWavesPsVisualization";
    public bool Enabled { get; init; }
    public bool EnableStandaloneGauge { get; init; } = true;
    public bool IncludeGaugeInMonthlySalesTrendChart { get; init; }
    public bool IncludeHistoryInStandaloneGauge { get; init; } = true;
    public bool AllowStaleStandaloneGauge { get; init; } = true;
    public int MaxSyncAgeHours { get; init; } = 48;
    public int MaxObservationLagTradingDays { get; init; } = 2;
    public int MaxHistoryPoints { get; init; } = 5000;
    public int DisplayPercentageDecimals { get; init; } = 2;
}

/// <summary>Composes solely from the local Spec 114 read model; it never owns a provider client.</summary>
public sealed class PsVisualizationExperienceUseCase(
    ICompanyResolverService companyResolver,
    ICompanyPsVisualizationReader reader,
    IOptions<CyclicalWavesPsVisualizationOptions> options,
    TimeProvider clock) : IPsVisualizationExperienceUseCase
{
    public async Task<PsVisualizationResult?> ExecuteAsync(PsVisualizationQuery query, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.EnableStandaloneGauge || string.IsNullOrWhiteSpace(query.SymbolOrCompanyName)) return null;
        if (query.IncludeInMonthlySalesTrendChart && !settings.IncludeGaugeInMonthlySalesTrendChart) return null;
        var company = await companyResolver.ResolveBySymbolAsync(query.SymbolOrCompanyName, cancellationToken);
        if (company is null) return null;
        var local = await reader.GetAsync(company.Id, cancellationToken);
        var includeHistory = query.IncludeHistory && settings.IncludeHistoryInStandaloneGauge;
        if (local?.Snapshot is null)
            return new PsVisualizationResult(1, company.Id, company.Ticker ?? query.SymbolOrCompanyName, company.CompanySymbol, "CyclicalWaves", local?.SnapshotObservationDate, PsVisualizationStatus.Unavailable, local?.GaugeRenderabilityStatus ?? GaugeRenderabilityStatus.UnverifiedSemantics, Missing(), Missing(), Missing(), Array.Empty<PsGaugeBand>(), null, null, null, null, null, null, includeHistory, includeHistory ? PsVisualizationStatus.Unavailable : PsVisualizationStatus.NotRequested, Array.Empty<PsVisualizationHistoryPoint>(), local?.HistoryPoints.Count ?? 0, false, local?.LastSnapshotSyncAtUtc, local?.WarningCodes ?? new[] { "PsGaugeUnavailable" });

        var age = clock.GetUtcNow() - local.Snapshot.LastSyncedAtUtc;
        var fresh = age <= TimeSpan.FromHours(settings.MaxSyncAgeHours);
        if (!fresh && !settings.AllowStaleStandaloneGauge)
            return new PsVisualizationResult(1, company.Id, company.Ticker ?? query.SymbolOrCompanyName, company.CompanySymbol, local.Snapshot.ProviderName, local.SnapshotObservationDate, PsVisualizationStatus.Stale, local.GaugeRenderabilityStatus, Present(local.Snapshot.TtmPsRatio), Present(local.Snapshot.ForwardPsRatio), Present(local.Snapshot.GaugeClose), Array.Empty<PsGaugeBand>(), null, local.Snapshot.BoundaryStart, local.Snapshot.BoundaryEnd, local.Snapshot.BoundaryMin, local.Snapshot.BoundaryMax, local.Snapshot.BoundaryAverage, false, PsVisualizationStatus.NotRequested, Array.Empty<PsVisualizationHistoryPoint>(), local.HistoryPoints.Count, false, local.Snapshot.LastSyncedAtUtc, new[] { "StaleSnapshotHidden" });
        var ordered = local.HistoryPoints.OrderBy(x => x.ObservationDate).ThenBy(x => x.ProviderPointId, StringComparer.Ordinal).ToArray();
        var projected = includeHistory ? ordered.TakeLast(settings.MaxHistoryPoints).Select(x => new PsVisualizationHistoryPoint(x.ProviderPointId, x.ObservationDate, x.PsRatio)).ToArray() : Array.Empty<PsVisualizationHistoryPoint>();
        var gauge = PsGaugeCalculator.Calculate(
            new[] { local.Snapshot.BucketA, local.Snapshot.BucketB, local.Snapshot.BucketC, local.Snapshot.BucketD, local.Snapshot.BucketE, local.Snapshot.BucketF },
            local.Snapshot.BoundaryStart,
            local.Snapshot.BoundaryMin,
            local.Snapshot.BoundaryMax,
            local.Snapshot.BoundaryEnd,
            local.Snapshot.TtmPsRatio,
            settings.DisplayPercentageDecimals);
        return new PsVisualizationResult(1, company.Id, company.Ticker ?? query.SymbolOrCompanyName, company.CompanySymbol, local.Snapshot.ProviderName, local.SnapshotObservationDate, fresh ? PsVisualizationStatus.Fresh : PsVisualizationStatus.Stale, local.GaugeRenderabilityStatus, Present(local.Snapshot.TtmPsRatio), Present(local.Snapshot.ForwardPsRatio), Present(local.Snapshot.GaugeClose), gauge.Bands, gauge.Needle, local.Snapshot.BoundaryStart, local.Snapshot.BoundaryEnd, local.Snapshot.BoundaryMin, local.Snapshot.BoundaryMax, local.Snapshot.BoundaryAverage, includeHistory, includeHistory ? PsVisualizationStatus.Fresh : PsVisualizationStatus.NotRequested, projected, ordered.Length, includeHistory && ordered.Length > projected.Length, local.Snapshot.LastSyncedAtUtc, local.WarningCodes);
    }
    private static PsVisualizationCurrentValue Present(decimal value) => new(value, PsValueState.Present);
    private static PsVisualizationCurrentValue Missing() => new(null, PsValueState.Missing);
}
