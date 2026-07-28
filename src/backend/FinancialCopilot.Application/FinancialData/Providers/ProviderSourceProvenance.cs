namespace FinancialCopilot.Application.FinancialData.Providers;

/// <summary>
/// Row- and batch-level source provenance (spec 051 AC #7). Persisted alongside normalized data so a
/// stored observation can always be traced back to the logical vendor, the physical source it arrived
/// through, the import mode, the Shamsi source date range it covers, and the ingestion run that wrote
/// it. Provenance is descriptive metadata; canonical identity and scanner reads never key off it.
/// </summary>
public sealed record ProviderSourceProvenance(
    LogicalVendor Vendor,
    PhysicalSource Source,
    SourceMode Mode,
    Guid IngestionRunId,
    string? SourceDateRangeStartJalali = null,
    string? SourceDateRangeEndJalali = null)
{
    /// <summary>
    /// Builds provenance from a stored source name and run id. An uncatalogued source name yields a
    /// best-effort provenance so ingestion never fails on provenance alone; the unknown name is left
    /// for conflict logging to surface.
    /// </summary>
    public static ProviderSourceProvenance FromSourceName(
        string sourceName,
        Guid ingestionRunId,
        string? sourceDateRangeStartJalali = null,
        string? sourceDateRangeEndJalali = null)
    {
        var descriptor = ProviderSources.TryResolve(sourceName);
        return new ProviderSourceProvenance(
            descriptor?.Vendor ?? LogicalVendor.NoavaranAmin,
            descriptor?.Source ?? PhysicalSource.NoavaranCurrentApi,
            descriptor?.DefaultMode ?? SourceMode.CurrentIncremental,
            ingestionRunId,
            sourceDateRangeStartJalali,
            sourceDateRangeEndJalali);
    }
}
