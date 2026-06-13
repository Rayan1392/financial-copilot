namespace FinancialCopilot.Application.FinancialData.Providers;

/// <summary>
/// The logical data vendor — the business entity that owns the data — independent of how the data
/// physically reaches the system. Spec 051 corrects an earlier model that treated each transport
/// (CodalDB SQL archive, NADPCO HTTP API) as its own vendor; both are Noavaran Amin.
/// </summary>
public enum LogicalVendor
{
    /// <summary>Noavaran Amin fundamentals vendor (archive SQL + current HTTP API source modes).</summary>
    NoavaranAmin,

    /// <summary>CyclicalWaves — an independent fundamentals vendor, unrelated to Noavaran Amin.</summary>
    CyclicalWaves,

    /// <summary>Tehran Securities Exchange market-trading data vendor (e.g. StockMarketDB bridge, future direct feed).</summary>
    Tsetmc
}

/// <summary>
/// The physical source/transport a vendor's data arrives through. One <see cref="LogicalVendor"/>
/// can own several physical sources (Noavaran Amin owns both the frozen archive SQL source and the
/// current HTTP API source).
/// </summary>
public enum PhysicalSource
{
    /// <summary>Noavaran Amin frozen archive, read from the legacy CodalDB SQL Server snapshot.</summary>
    NoavaranArchiveSql,

    /// <summary>Noavaran Amin current HTTP API (formerly modeled as the standalone "NadpcoApi" vendor).</summary>
    NoavaranCurrentApi,

    /// <summary>CyclicalWaves HTTP API.</summary>
    CyclicalWavesApi,

    /// <summary>StockMarketDB SQL Server (bridge market-trading source).</summary>
    StockMarketDb,

    /// <summary>Future direct TSETMC web-service feed (see order 56 / spec 054).</summary>
    TsetmcWebService
}

/// <summary>
/// How a physical source is expected to be ingested over time. This governs scheduling and freshness
/// semantics: an <see cref="ArchiveOneTime"/> source is synchronized once and then frozen, whereas a
/// <see cref="CurrentIncremental"/> source is refreshed on a recurring cadence.
/// </summary>
public enum SourceMode
{
    /// <summary>Imported once, audited, then frozen unless an explicit maintenance re-import is requested.</summary>
    ArchiveOneTime,

    /// <summary>Refreshed incrementally on a recurring schedule (covers data from the boundary year onward).</summary>
    CurrentIncremental,

    /// <summary>Periodic external snapshot ingestion (e.g. CyclicalWaves full sync).</summary>
    ExternalSnapshot,

    /// <summary>Bridge source kept temporarily pending migration to a direct feed.</summary>
    MigrationBridge
}

/// <summary>
/// Canonical description of one physical data source: which logical vendor owns it, the physical
/// transport, its default ingestion mode, and the stable persisted source name. This is the single
/// authoritative owner (Pragmatic Programmer DRY) of source identity — options classes, the provider
/// router, normalizers, scheduling guards, and provenance all derive their source name from here
/// rather than repeating string literals.
/// </summary>
public sealed record ProviderSourceDescriptor(
    LogicalVendor Vendor,
    PhysicalSource Source,
    SourceMode DefaultMode,
    string SourceName);

/// <summary>
/// Authoritative catalog of known financial data sources. The persisted <see cref="ProviderRawPayload.ProviderName"/>
/// and normalized-row <c>ProviderName</c> values use the <see cref="ProviderSourceDescriptor.SourceName"/>
/// strings defined here, so the logical vendor and source mode can always be recovered from a stored row.
/// </summary>
public static class ProviderSources
{
    /// <summary>Noavaran Amin frozen archive (legacy CodalDB SQL snapshot). Imported once, then frozen.</summary>
    public const string NoavaranArchiveSqlName = "NoavaranArchiveSql";

    /// <summary>Noavaran Amin current HTTP API (formerly "NadpcoApi"). Covers data from the boundary year onward.</summary>
    public const string NoavaranCurrentApiName = "NoavaranCurrentApi";

    /// <summary>CyclicalWaves HTTP API — independent vendor.</summary>
    public const string CyclicalWavesName = "CyclicalWaves";

    /// <summary>StockMarketDB SQL bridge market-trading source.</summary>
    public const string StockMarketDbName = "StockMarketDb";

    /// <summary>Future direct TSETMC web-service ingestion feed (spec 054, order 56).</summary>
    public const string TsetmcWebServiceName = "TsetmcWebService";

    public static readonly ProviderSourceDescriptor NoavaranArchiveSql = new(
        LogicalVendor.NoavaranAmin, PhysicalSource.NoavaranArchiveSql, SourceMode.ArchiveOneTime, NoavaranArchiveSqlName);

    public static readonly ProviderSourceDescriptor NoavaranCurrentApi = new(
        LogicalVendor.NoavaranAmin, PhysicalSource.NoavaranCurrentApi, SourceMode.CurrentIncremental, NoavaranCurrentApiName);

    public static readonly ProviderSourceDescriptor CyclicalWaves = new(
        LogicalVendor.CyclicalWaves, PhysicalSource.CyclicalWavesApi, SourceMode.ExternalSnapshot, CyclicalWavesName);

    public static readonly ProviderSourceDescriptor StockMarketDb = new(
        LogicalVendor.Tsetmc, PhysicalSource.StockMarketDb, SourceMode.MigrationBridge, StockMarketDbName);

    /// <summary>
    /// Future direct TSETMC web-service feed — registered in the catalog so provenance can be stored
    /// once Phase 2 of spec 054 is implemented; not yet backed by a live provider.
    /// </summary>
    public static readonly ProviderSourceDescriptor TsetmcWebService = new(
        LogicalVendor.Tsetmc, PhysicalSource.TsetmcWebService, SourceMode.CurrentIncremental, TsetmcWebServiceName);

    private static readonly IReadOnlyDictionary<string, ProviderSourceDescriptor> ByName =
        new[] { NoavaranArchiveSql, NoavaranCurrentApi, CyclicalWaves, StockMarketDb, TsetmcWebService }
            .ToDictionary(descriptor => descriptor.SourceName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<ProviderSourceDescriptor> All { get; } =
        new[] { NoavaranArchiveSql, NoavaranCurrentApi, CyclicalWaves, StockMarketDb, TsetmcWebService };

    /// <summary>
    /// Pre-spec-051 physical source names mapped to their current names. Used to keep in-flight
    /// <c>DataSyncRequest</c> messages and any externally-persisted requests that still carry the old
    /// names resolvable after the rename (Release It!: a deploy must tolerate in-flight work).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyNameAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CodalDb"] = NoavaranArchiveSqlName,
            ["NadpcoApi"] = NoavaranCurrentApiName
        };

    /// <summary>
    /// Maps a possibly-legacy source name to the current catalogued name (identity for current names,
    /// translated for known legacy names, unchanged for unknown names).
    /// </summary>
    public static string? NormalizeName(string? sourceName) =>
        string.IsNullOrWhiteSpace(sourceName)
            ? sourceName
            : LegacyNameAliases.TryGetValue(sourceName, out var current) ? current : sourceName;

    /// <summary>
    /// Resolves a stored source name (current or legacy) to its descriptor, or <c>null</c> when the
    /// name is not catalogued. Callers persisting provenance treat an unknown name as a quality signal
    /// rather than failing the ingestion run.
    /// </summary>
    public static ProviderSourceDescriptor? TryResolve(string? sourceName) =>
        string.IsNullOrWhiteSpace(sourceName) ? null : ByName.GetValueOrDefault(NormalizeName(sourceName)!);

    /// <summary>True when the named source is a one-time archive that must not be refreshed by a recurring worker.</summary>
    public static bool IsArchiveOnly(string? sourceName) =>
        TryResolve(sourceName)?.DefaultMode == SourceMode.ArchiveOneTime;
}
