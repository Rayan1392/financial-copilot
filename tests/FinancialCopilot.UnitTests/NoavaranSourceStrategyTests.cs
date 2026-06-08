using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 051 — Noavaran Amin archive/current source model: catalog mapping, source-priority
/// resolution, archive/current ownership boundary, provenance derivation, and identity-conflict
/// logging.
/// </summary>
public sealed class NoavaranSourceStrategyTests
{
    [Fact]
    public void Catalog_MapsBothArchiveAndCurrentToTheSameLogicalVendor()
    {
        var archive = ProviderSources.TryResolve(ProviderSources.NoavaranArchiveSqlName);
        var current = ProviderSources.TryResolve(ProviderSources.NoavaranCurrentApiName);

        Assert.NotNull(archive);
        Assert.NotNull(current);
        Assert.Equal(LogicalVendor.NoavaranAmin, archive!.Vendor);
        Assert.Equal(LogicalVendor.NoavaranAmin, current!.Vendor);
        Assert.NotEqual(archive.Source, current.Source);
        Assert.Equal(SourceMode.ArchiveOneTime, archive.DefaultMode);
        Assert.Equal(SourceMode.CurrentIncremental, current.DefaultMode);
    }

    [Fact]
    public void Catalog_TreatsCyclicalWavesAsAnIndependentVendor()
    {
        var cyclical = ProviderSources.TryResolve(ProviderSources.CyclicalWavesName);

        Assert.NotNull(cyclical);
        Assert.Equal(LogicalVendor.CyclicalWaves, cyclical!.Vendor);
        Assert.NotEqual(LogicalVendor.NoavaranAmin, cyclical.Vendor);
    }

    [Fact]
    public void Catalog_IdentifiesArchiveSourceAsOneTimeOnly()
    {
        Assert.True(ProviderSources.IsArchiveOnly(ProviderSources.NoavaranArchiveSqlName));
        Assert.False(ProviderSources.IsArchiveOnly(ProviderSources.NoavaranCurrentApiName));
        Assert.False(ProviderSources.IsArchiveOnly("UnknownSource"));
        Assert.False(ProviderSources.IsArchiveOnly(null));
    }

    [Fact]
    public void PriorityResolver_UsesDatasetSpecificOrderThenFallsBackToDefault()
    {
        var resolver = CreateResolver(new SourcePriorityOptions
        {
            DefaultOrder = [ProviderSources.NoavaranCurrentApiName, ProviderSources.NoavaranArchiveSqlName],
            DatasetPriority = new Dictionary<string, List<string>>
            {
                [ProviderDataset.FinancialRatios.ToString()] =
                    [ProviderSources.NoavaranArchiveSqlName, ProviderSources.NoavaranCurrentApiName]
            }
        });

        Assert.Equal(
            [ProviderSources.NoavaranArchiveSqlName, ProviderSources.NoavaranCurrentApiName],
            resolver.ResolvePriority(ProviderDataset.FinancialRatios));

        // Unconfigured dataset falls back to default order.
        Assert.Equal(
            [ProviderSources.NoavaranCurrentApiName, ProviderSources.NoavaranArchiveSqlName],
            resolver.ResolvePriority(ProviderDataset.Symbols));
    }

    [Theory]
    [InlineData(1402, 6, "ArchiveOneTime")]
    [InlineData(1403, 1, "CurrentIncremental")]
    [InlineData(1404, 12, "CurrentIncremental")]
    public void PriorityResolver_SplitsNoavaranOwnershipAtTheBoundaryYear(int year, int month, string expectedMode)
    {
        var resolver = CreateResolver(new SourcePriorityOptions { CurrentApiBoundaryShamsiYear = 1403 });

        var mode = resolver.ResolveNoavaranOwnership(new ShamsiPeriod(year, month));

        Assert.Equal(Enum.Parse<SourceMode>(expectedMode), mode);
    }

    [Fact]
    public void Provenance_FromArchiveSourceName_RecoversVendorSourceAndMode()
    {
        var runId = Guid.NewGuid();
        var provenance = ProviderSourceProvenance.FromSourceName(
            ProviderSources.NoavaranArchiveSqlName,
            runId,
            sourceDateRangeStartJalali: "1399/01/01",
            sourceDateRangeEndJalali: "1402/12/29");

        Assert.Equal(LogicalVendor.NoavaranAmin, provenance.Vendor);
        Assert.Equal(PhysicalSource.NoavaranArchiveSql, provenance.Source);
        Assert.Equal(SourceMode.ArchiveOneTime, provenance.Mode);
        Assert.Equal(runId, provenance.IngestionRunId);
        Assert.Equal("1399/01/01", provenance.SourceDateRangeStartJalali);
        Assert.Equal("1402/12/29", provenance.SourceDateRangeEndJalali);
    }

    [Fact]
    public async Task IdentityConflictLog_CoalescesRepeatedConflictsAndNeverThrows()
    {
        var log = new LoggingIdentityConflictLog(NullLogger<LoggingIdentityConflictLog>.Instance);
        var conflict = new IdentityConflict(
            CanonicalIdentifierKind.Isin,
            "IRO1234",
            ProviderSources.NoavaranArchiveSqlName,
            ProviderSources.NoavaranCurrentApiName,
            "ALPHA",
            "BETA",
            "Symbol disagreement across archive and current sources.");

        // Recording the same conflict repeatedly must be safe (coalesced) and never throw.
        await log.RecordAsync(conflict, CancellationToken.None);
        await log.RecordAsync(conflict, CancellationToken.None);
        await log.RecordAsync(conflict with { IdentifierValue = "IRO9999" }, CancellationToken.None);
    }

    private static SourcePriorityResolver CreateResolver(SourcePriorityOptions options) =>
        new(Options.Create(options));
}
