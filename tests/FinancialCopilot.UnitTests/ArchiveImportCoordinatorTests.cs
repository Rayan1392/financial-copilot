using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 052 — one-time Noavaran archive import: dry-run, import, validate, freeze, and the
/// reason-gated re-import of a frozen archive.
/// </summary>
public sealed class ArchiveImportCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-09T09:00:00Z");

    [Fact]
    public async Task DryRun_ReportsWouldImportCounts_WithoutEnqueuing()
    {
        await using var db = CreateDb();
        var sync = new RecordingArchiveSync { CompaniesConsidered = 12 };
        var coordinator = CreateCoordinator(db, sync);

        var run = await coordinator.RunAsync(
            new ArchiveImportRequest(ArchiveImportAction.DryRun, "User:admin", []),
            CancellationToken.None);

        Assert.Equal(ArchiveImportRunStatus.Succeeded, run.Status);
        Assert.True(sync.LastDryRun);
        Assert.Equal(12, run.CompaniesConsidered);
        Assert.Equal(0, run.RequestsEnqueued);
        Assert.False(run.Frozen);
    }

    [Fact]
    public async Task Import_EnqueuesThroughArchiveSync_AndRecordsRun()
    {
        await using var db = CreateDb();
        var sync = new RecordingArchiveSync { CompaniesConsidered = 5, CompaniesEnqueued = 5 };
        var coordinator = CreateCoordinator(db, sync);

        var run = await coordinator.RunAsync(
            new ArchiveImportRequest(ArchiveImportAction.Import, "User:admin", []),
            CancellationToken.None);

        Assert.Equal(ArchiveImportRunStatus.Succeeded, run.Status);
        Assert.False(sync.LastDryRun);
        Assert.True(sync.ExecuteCalled);
        Assert.Equal(5, run.RequestsEnqueued);
        Assert.Single(await db.ArchiveImportRuns.ToListAsync());
    }

    [Fact]
    public async Task Freeze_MarksArchiveFrozen_AndBlocksSubsequentImport()
    {
        await using var db = CreateDb();
        var sync = new RecordingArchiveSync { CompaniesConsidered = 5, CompaniesEnqueued = 5 };
        var coordinator = CreateCoordinator(db, sync);

        var freezeRun = await coordinator.RunAsync(
            new ArchiveImportRequest(ArchiveImportAction.Freeze, "User:admin", [], "Archive verified complete."),
            CancellationToken.None);
        Assert.Equal(ArchiveImportRunStatus.Succeeded, freezeRun.Status);
        Assert.True(freezeRun.Frozen);

        var state = await coordinator.GetFreezeStateAsync(CancellationToken.None);
        Assert.True(state.IsFrozen);
        Assert.Equal(freezeRun.RunId, state.FrozenByRunId);

        // A normal import against a frozen archive is rejected.
        var blocked = await coordinator.RunAsync(
            new ArchiveImportRequest(ArchiveImportAction.Import, "User:admin", []),
            CancellationToken.None);
        Assert.Equal(ArchiveImportRunStatus.RejectedFrozen, blocked.Status);
        Assert.False(sync.ExecuteCalled);
    }

    [Fact]
    public async Task ReImport_OfFrozenArchive_RequiresReason()
    {
        await using var db = CreateDb();
        var sync = new RecordingArchiveSync { CompaniesConsidered = 5, CompaniesEnqueued = 5 };
        var coordinator = CreateCoordinator(db, sync);
        await coordinator.RunAsync(
            new ArchiveImportRequest(ArchiveImportAction.Freeze, "User:admin", [], "freeze"),
            CancellationToken.None);

        // Re-import with no reason -> rejected.
        var noReason = await coordinator.RunAsync(
            new ArchiveImportRequest(ArchiveImportAction.ReImport, "User:admin", []),
            CancellationToken.None);
        Assert.Equal(ArchiveImportRunStatus.RejectedFrozen, noReason.Status);
        Assert.False(sync.ExecuteCalled);

        // Re-import with an explicit reason -> proceeds and records the reason.
        var withReason = await coordinator.RunAsync(
            new ArchiveImportRequest(ArchiveImportAction.ReImport, "User:admin", [], "Vendor reissued 1402 statements."),
            CancellationToken.None);
        Assert.Equal(ArchiveImportRunStatus.Succeeded, withReason.Status);
        Assert.True(sync.ExecuteCalled);
        Assert.Equal("Vendor reissued 1402 statements.", withReason.Reason);
    }

    [Fact]
    public async Task Validate_ReportsCompanyMapping_AndCoverage()
    {
        await using var db = CreateDb();
        // One archive company with a canonical symbol (mapped), one without (unmapped).
        SeedCompany(db, externalCompanyId: "1001", canonicalSymbol: "IRO1FOLD0001");
        SeedCompany(db, externalCompanyId: "1002", canonicalSymbol: null);
        SeedStatement(db, externalCompanyId: "1001", periodEnd: new DateOnly(2023, 3, 20));
        await db.SaveChangesAsync();

        var coordinator = CreateCoordinator(db, new RecordingArchiveSync());

        var validation = await coordinator.ValidateAsync(CancellationToken.None);

        Assert.False(validation.CompanyMappingValid);
        Assert.Equal(1, validation.CompaniesWithoutCanonicalSymbol);
        Assert.Contains("1002", validation.UnmappedExternalCompanyIds);
        Assert.Equal(2, validation.Coverage.CompanyCount);
        Assert.Equal(1, validation.Coverage.RowCountByDataset[ArchiveImportDataset.FinancialStatements.ToString()]);
        Assert.True(validation.Coverage.RowCountByFiscalYear.ContainsKey(2023));
    }

    private static void SeedCompany(FinancialIngestionDbContext db, string externalCompanyId, string? canonicalSymbol)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = ProviderSources.NoavaranArchiveSqlName,
            ExternalCompanyId = externalCompanyId,
            Name = $"Company {externalCompanyId}",
            LogicalVendor = LogicalVendor.NoavaranAmin.ToString(),
            SourceMode = SourceMode.ArchiveOneTime.ToString(),
            LastSynchronizedAt = Now
        });
        if (canonicalSymbol is not null)
        {
            db.Symbols.Add(new NormalizedSymbolRow
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                ProviderName = ProviderSources.NoavaranArchiveSqlName,
                ExternalSymbolId = externalCompanyId,
                SymbolCode = canonicalSymbol,
                LastSynchronizedAt = Now
            });
        }
    }

    private static void SeedStatement(FinancialIngestionDbContext db, string externalCompanyId, DateOnly periodEnd)
    {
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderSources.NoavaranArchiveSqlName,
            ExternalCompanyId = externalCompanyId,
            ExternalStatementId = $"stmt-{externalCompanyId}",
            StatementType = "IncomeStatement",
            PeriodType = "TwelveMonths",
            PeriodStart = periodEnd.AddYears(-1).AddDays(1),
            PeriodEnd = periodEnd,
            SourcePayloadChecksum = "chk",
            LastSynchronizedAt = Now
        });
    }

    private static ArchiveImportCoordinator CreateCoordinator(
        FinancialIngestionDbContext db,
        RecordingArchiveSync sync)
    {
        var timeProvider = new FixedTimeProvider(Now);
        var repository = new EfCoreArchiveImportRunRepository(db, timeProvider);
        var freezeStore = new EfCoreArchiveFreezeStateStore(db, timeProvider);
        var coverageReader = new EfCoreArchiveCoverageReader(db);
        return new ArchiveImportCoordinator(
            sync, repository, freezeStore, coverageReader, db,
            NullLogger<ArchiveImportCoordinator>.Instance);
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecordingArchiveSync : ICodalDbScheduledSyncService
    {
        public int CompaniesConsidered { get; init; }
        public int CompaniesEnqueued { get; init; }
        public int FailedCompanies { get; init; }
        public bool ExecuteCalled { get; private set; }
        public bool LastDryRun { get; private set; }

        public Task<CodalDbScheduledSyncResult> ExecuteAsync(
            bool fullReload, CancellationToken cancellationToken, bool dryRun = false)
        {
            ExecuteCalled = true;
            LastDryRun = dryRun;
            return Task.FromResult(new CodalDbScheduledSyncResult(
                fullReload,
                CompaniesConsidered,
                dryRun ? CompaniesConsidered : CompaniesEnqueued,
                FailedCompanies,
                FailedCompanyIds: [],
                AdvancedWatermark: null,
                Duration: TimeSpan.Zero));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
