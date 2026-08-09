namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum PsVisualizationStatus { Fresh, Stale, Partial, Invalid, Unavailable, NotRequested }
public enum PsValueState { Present, Missing }
public enum PsGaugeBandRole { VeryLow, Low, LowerMiddle, UpperMiddle, High, VeryHigh }

public sealed record PsVisualizationOptionsSnapshot(
    bool Enabled, bool IncludeStandaloneGauge, bool IncludeHistory, int MaxHistoryPoints, int DisplayPercentageDecimals);

public sealed record PsVisualizationCurrentValue(decimal? Value, PsValueState State);

public sealed record PsVisualizationHistoryPoint(string Key, DateOnly ObservationDate, decimal PsRatio);

public sealed record PsGaugeBand(
    int Order,
    PsGaugeBandRole Role,
    long ProviderCount,
    decimal ExactPercentage,
    decimal DisplayPercentage,
    decimal LowerBoundary,
    decimal UpperBoundary,
    decimal StartAngleDegrees,
    decimal EndAngleDegrees);

public sealed record PsGaugeNeedle(
    decimal SourceValue,
    decimal NormalizedPosition,
    decimal AngleDegrees,
    int BandOrder,
    bool IsClampedLow,
    bool IsClampedHigh);

/// <summary>Versioned presentation-ready result. Bands/needle are absent unless verified semantics make the gauge renderable.</summary>
public sealed record PsVisualizationResult(
    int ContractVersion,
    Guid CompanyId,
    string CompanySymbol,
    string? CompanyName,
    string ProviderName,
    DateOnly? SourceObservationDate,
    PsVisualizationStatus Status,
    GaugeRenderabilityStatus GaugeRenderabilityStatus,
    PsVisualizationCurrentValue TtmPs,
    PsVisualizationCurrentValue ForwardPs,
    PsVisualizationCurrentValue GaugeClose,
    IReadOnlyList<PsGaugeBand> GaugeBands,
    PsGaugeNeedle? Needle,
    decimal? ProviderBoundaryStart,
    decimal? ProviderBoundaryEnd,
    decimal? GaugeAxisMin,
    decimal? GaugeAxisMax,
    decimal? ProviderAverage,
    bool HistoryIncluded,
    PsVisualizationStatus HistoryStatus,
    IReadOnlyList<PsVisualizationHistoryPoint> HistoryPoints,
    int SourceHistoryPointCount,
    bool IsHistoryTruncated,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    IReadOnlyList<string> WarningCodes);

public sealed record PsVisualizationQuery(
    string SymbolOrCompanyName,
    bool IncludeHistory = true,
    bool IncludeInMonthlySalesTrendChart = false);

public interface IPsVisualizationExperienceUseCase
{
    Task<PsVisualizationResult?> ExecuteAsync(PsVisualizationQuery query, CancellationToken cancellationToken = default);
}
