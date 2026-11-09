namespace FinancialCopilot.Application.FinancialData.CyclicalWavesV2;

public sealed record CyclicalWavesV2GaugeTarget(Guid TickerId, string Ticker, string? Enticker, bool HasPe, bool HasPs, bool HasRange);
public sealed record CyclicalWavesV2GaugeCatalogApplyResult(int Matched, int Unmatched, int Updated, int Unchanged);

public interface ICyclicalWavesV2GaugeStore
{
    Task<CyclicalWavesV2GaugeCatalogApplyResult> ApplyGaugeCatalogAsync(IReadOnlyCollection<CyclicalWavesV2GaugeCatalogItem> items, DateTimeOffset now, CancellationToken cancellationToken);
    Task<CyclicalWavesV2GaugeCatalogApplyResult> ApplyEquilibriumCatalogAsync(IReadOnlyCollection<CyclicalWavesV2EquilibriumCatalogItem> items, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<CyclicalWavesV2GaugeTarget>> GetGaugeTargetsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CyclicalWavesV2GaugeTarget>> GetEquilibriumTargetsAsync(CancellationToken cancellationToken);
    Task<CyclicalWavesV2ApplyOutcome> ApplyGaugeDetailsAsync(CyclicalWavesV2GaugeDetails details, DateTimeOffset now, CancellationToken cancellationToken);
    Task<CyclicalWavesV2ApplyOutcome> ApplyEquilibriumRangeAsync(string identifier, CyclicalWavesV2EquilibriumRange range, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkGaugeUnavailableAsync(string identifier, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkEquilibriumUnavailableAsync(string identifier, DateTimeOffset now, CancellationToken cancellationToken);
}
