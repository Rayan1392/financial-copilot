namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Bound from the "TrendSnapshotBackfill" appsettings section.
/// Controls the Jalali date range and rebuild behaviour for the admin backfill operation.
/// </summary>
public sealed class TrendSnapshotBackfillOptions
{
    public const string SectionName = "TrendSnapshotBackfill";

    public int FromYear { get; set; } = 1404;
    public int FromMonth { get; set; } = 1;
    public int ToYear { get; set; } = 1405;
    public int ToMonth { get; set; } = 3;
    public bool ForceRebuild { get; set; }
}
