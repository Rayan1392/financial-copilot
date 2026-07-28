namespace FinancialCopilot.Application.FinancialData.Providers;

/// <summary>
/// Explicit per-dataset source-priority configuration (spec 051 AC #8). For each dataset the ordered
/// candidate source names express which physical source owns the data first; the Shamsi boundary year
/// governs the archive/current split for Noavaran Amin (AC #6): periods strictly before the boundary
/// are owned by the archive, periods from the boundary year onward are owned by the current API.
/// </summary>
public sealed class SourcePriorityOptions
{
    public const string SectionName = "FinancialSourcePriority";

    /// <summary>
    /// Shamsi (Jalali) year from which the Noavaran current API owns coverage. Periods whose Shamsi
    /// year is &lt; this value are archive-owned; periods &gt;= this value are current-API-owned.
    /// </summary>
    public int CurrentApiBoundaryShamsiYear { get; set; } = 1403;

    /// <summary>
    /// Ordered source names per dataset (highest priority first). Keys are <see cref="ProviderDataset"/>
    /// names; values are <see cref="ProviderSources"/> source names. Missing datasets fall back to
    /// <see cref="DefaultOrder"/>.
    /// </summary>
    public Dictionary<string, List<string>> DatasetPriority { get; set; } = new();

    /// <summary>Fallback ordering when a dataset has no explicit configuration.</summary>
    public List<string> DefaultOrder { get; set; } =
    [
        ProviderSources.NoavaranArchiveSqlName,
        ProviderSources.NoavaranCurrentApiName,
        ProviderSources.CyclicalWavesName
    ];
}

/// <summary>The Shamsi year/month an observation period belongs to, used for archive/current ownership.</summary>
public readonly record struct ShamsiPeriod(int Year, int Month);

/// <summary>
/// Resolves which physical source owns a given dataset/period, and the full priority order. Pure
/// policy: it depends only on configuration, never on a physical connection. Scanner reads remain
/// source-agnostic; this resolver governs ingestion ownership and conflict precedence only.
/// </summary>
public interface ISourcePriorityResolver
{
    /// <summary>Ordered candidate source names for a dataset (highest priority first).</summary>
    IReadOnlyList<string> ResolvePriority(ProviderDataset dataset);

    /// <summary>
    /// The Noavaran Amin source mode that owns a Shamsi period: <see cref="SourceMode.ArchiveOneTime"/>
    /// before the boundary year, <see cref="SourceMode.CurrentIncremental"/> from the boundary onward.
    /// </summary>
    SourceMode ResolveNoavaranOwnership(ShamsiPeriod period);

    /// <summary>The configured Shamsi boundary year where current-API coverage begins.</summary>
    int CurrentApiBoundaryShamsiYear { get; }
}
