using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using FinancialCopilot.Domain.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioOperationsTests
{
    [Fact]
    public async Task ConfiguredLocalSource_UsesBoundedDiscoveryAndRejectsPathEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), "fund-portfolio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "a.xlsx"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(root, "b.xlsx"), [2]);
            var source = new ConfiguredLocalFundPortfolioReportSource(Options.Create(new FundPortfolioLocalSourceOptions { ProviderName = "Local", RootPath = root, MaximumItemsPerPage = 1 }));
            var page = await source.DiscoverAsync(new("Local", MaximumItems: 50), CancellationToken.None);
            Assert.Single(page.Items);
            var secondPage = await source.DiscoverAsync(new("Local", MaximumItems: 50, ContinuationToken: page.ContinuationToken), CancellationToken.None);
            Assert.Single(secondPage.Items);
            Assert.Null(secondPage.ContinuationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => source.DownloadAsync(page.Items[0] with { DownloadToken = "../outside.xlsx" }, CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void SourceEligibilityAndRetryPolicy_AreDeterministic()
    {
        var watermark = DateTimeOffset.UtcNow;
        var newer = new FundPortfolioReportSourceDescriptor("p", "b", "b.xlsx", null, "f", watermark.AddMinutes(1), null, "b");
        var same = newer with { LastModifiedUtc = watermark, StableSourceObjectId = "z" };
        Assert.True(FundPortfolioSourceEligibilityPolicy.IsNewer(newer, watermark, "a"));
        Assert.True(FundPortfolioSourceEligibilityPolicy.IsNewer(same, watermark, "a"));
        Assert.False(FundPortfolioSourceEligibilityPolicy.IsNewer(newer with { StableSourceObjectId = "" }, watermark, "a"));
        Assert.Equal(TimeSpan.FromSeconds(30), FundPortfolioRetryPolicy.DelayForAttempt(1));
        Assert.Equal(TimeSpan.FromSeconds(60), FundPortfolioRetryPolicy.DelayForAttempt(2));
        Assert.True(FundPortfolioRetryPolicy.IsPoisoned(3, 3));
    }

    [Fact]
    public async Task MappingReview_RejectsStaleVersionAndCreatesGovernedDecision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(connection).Options;
        await using var db = new FinancialProviderDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.FundPortfolioMappingReviews.Add(new FundPortfolioMappingReviewRow { Id = Guid.NewGuid(), ReportId = Guid.NewGuid(), MappingType = FundPortfolioMappingReviewType.InvalidDate, RawValue = "1403/01/01", NormalizedValue = "2024-03-20", CandidateJson = "[]", Status = FundPortfolioMappingReviewStatus.Pending, Version = 0 });
        await db.SaveChangesAsync();
        var id = await db.FundPortfolioMappingReviews.Select(x => x.Id).SingleAsync();
        var repository = new EfCoreFundPortfolioMappingReviewRepository(db);
        var first = await repository.ResolveAsync(new(id, 0, true, "{\"value\":\"2024-03-20\"}", "admin"), CancellationToken.None);
        var stale = await repository.ResolveAsync(new(id, 0, false, "{}", "other-admin"), CancellationToken.None);
        Assert.True(first.Changed);
        Assert.Equal(1, first.AffectedReportCount);
        Assert.False(stale.Changed);
        Assert.True(await db.FundPortfolioGovernedMappings.AnyAsync(x => x.IsApproved));
    }

    [Fact]
    public async Task SourceWatermark_ProvidesDistributedLeaseAndDefaultScheduleIsDisabled()
    {
        Assert.False(new FundPortfolioScheduledDiscoveryOptions().Enabled);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(connection).Options;
        await using var db = new FinancialProviderDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfCoreFundPortfolioSourceWatermarkStore(db);
        Assert.True(await store.TryAcquireAsync("Local", TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.False(await store.TryAcquireAsync("Local", TimeSpan.FromMinutes(5), CancellationToken.None));
        await store.AdvanceAsync("Local", DateTimeOffset.UtcNow, "source-1", CancellationToken.None);
        var watermark = await store.GetAsync("Local", CancellationToken.None);
        Assert.Equal("source-1", watermark?.LastSourceObjectId);
    }

    [Fact]
    public async Task ImportRepository_DeduplicatesSourceIdentityWithinAndAcrossRuns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(connection).Options;
        await using var db = new FinancialProviderDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreFundPortfolioImportRunRepository(db);
        var descriptor = new FundPortfolioReportSourceDescriptor("Local", "source-1", "one.xlsx", null, "Fund", null, null, "one.xlsx");

        var firstRun = await repository.CreateRunAsync(new(FundPortfolioImportTriggerType.BulkBackfill, "Local", "admin", [descriptor, descriptor]), "corr-1", CancellationToken.None);
        await repository.AddItemsAsync(firstRun, [descriptor, descriptor], CancellationToken.None);
        var firstItems = await repository.ListItemsAsync(new(firstRun), CancellationToken.None);

        var secondRun = await repository.CreateRunAsync(new(FundPortfolioImportTriggerType.BulkBackfill, "Local", "admin", [descriptor]), "corr-2", CancellationToken.None);
        await repository.AddItemsAsync(secondRun, [descriptor], CancellationToken.None);
        var secondItems = await repository.ListItemsAsync(new(secondRun), CancellationToken.None);

        Assert.Single(firstItems.Items);
        Assert.Equal(1, (await repository.GetRunAsync(firstRun, CancellationToken.None))?.DiscoveredCount);
        Assert.Empty(secondItems.Items);
    }

    [Fact]
    public async Task ImportRepository_LeaseBlocksConcurrentClaimAndRetryUntilEligible()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(connection).Options;
        await using var db = new FinancialProviderDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreFundPortfolioImportRunRepository(db);
        var descriptor = new FundPortfolioReportSourceDescriptor("Local", "source-lease", "lease.xlsx", null, "Fund", null, null, "lease.xlsx");
        var runId = await repository.CreateRunAsync(new(FundPortfolioImportTriggerType.BulkBackfill, "Local", "admin", [descriptor]), "corr", CancellationToken.None);
        await repository.AddItemsAsync(runId, [descriptor], CancellationToken.None);
        var itemId = (await repository.ListItemsAsync(new(runId), CancellationToken.None)).Items.Single().Id;

        Assert.NotNull(await repository.ClaimItemAsync(runId, itemId, 300, CancellationToken.None));
        Assert.Null(await repository.ClaimItemAsync(runId, itemId, 300, CancellationToken.None));
        await repository.CompleteItemAsync(itemId, FundPortfolioImportItemStatus.RetryableFailure, null, "IO", "retry", CancellationToken.None);
        Assert.Empty(await repository.ListRunnableItemsAsync(10, CancellationToken.None));

        var row = await db.FundPortfolioImportItems.SingleAsync(x => x.Id == itemId);
        row.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.NotNull(await repository.ClaimItemAsync(runId, itemId, 300, CancellationToken.None));
    }

    [Fact]
    public async Task ReportRepository_PersistsCorrectedRevisionAndPreservesDuplicateHashIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(connection).Options;
        await using var db = new FinancialProviderDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreFundPortfolioReportRepository(db);
        var fund = new InvestmentFund(Guid.NewGuid(), "Fund", "fund", "Test", null, null);
        var period = new FundPortfolioReportPeriod("1403/03/29", new DateOnly(2024, 6, 18));
        var firstId = Guid.NewGuid();
        var first = new FundPortfolioWorkbookEnvelope(firstId, fund.Id, "Test", "first.xlsx", "hash-1", "v1", period, [], []);
        var request = new IngestFundPortfolioWorkbookRequest("Test", "Fund", "first.xlsx", "application/octet-stream", Stream.Null, CorrelationId: "corr");
        Assert.True(await repository.SaveParsedReportAsync(fund, request, new("fund-portfolio/first", 1, request.ContentType, "hash-1"), first, 1, null, CancellationToken.None));

        var secondId = Guid.NewGuid();
        var second = new FundPortfolioWorkbookEnvelope(secondId, fund.Id, "Test", "second.xlsx", "hash-2", "v1", period, [], []);
        Assert.True(await repository.SaveParsedReportAsync(fund, request with { OriginalFileName = "second.xlsx" }, new("fund-portfolio/second", 1, request.ContentType, "hash-2"), second, 2, firstId, CancellationToken.None));

        Assert.Equal((firstId, 1), await repository.FindByHashAsync("Test", "hash-1", CancellationToken.None));
        Assert.Equal(FundPortfolioParseStatus.Superseded, await db.FundPortfolioReports.Where(x => x.Id == firstId).Select(x => x.ParseStatus).SingleAsync());
        Assert.Equal(2, await db.FundPortfolioReports.Where(x => x.FundId == fund.Id).MaxAsync(x => x.SourceRevision));
    }

    [Fact]
    public async Task ReprocessEvidence_TwiceDoesNotCreateDuplicateReports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(connection).Options;
        await using var db = new FinancialProviderDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var reportId = Guid.NewGuid();
        db.FundPortfolioReports.Add(new FundPortfolioReportRow { Id = reportId, FundId = Guid.NewGuid(), ProviderName = "Test", ReportType = FundPortfolioReportType.MonthlyPortfolio, OriginalFileName = "test.xlsx", FileSha256 = "abc", RawStorageKey = "fund-portfolio/test.xlsx", RawFileSizeBytes = 3, RawMimeType = "application/octet-stream", ParserProfileVersion = "v1", ParseStatus = FundPortfolioParseStatus.Parsed, SourceRevision = 1, ImportedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var repository = new EfCoreFundPortfolioReportReprocessRepository(db);
        var sheet = new FundWorkbookSheetEnvelope(Guid.NewGuid(), "holdings", "holdings", FundWorkbookLogicalSheetType.EquityPortfolioCurrent, 0, "A1:B1", 1m, "fingerprint", "classifier-v1", [], []);
        var envelope = new FundPortfolioWorkbookEnvelope(reportId, Guid.NewGuid(), "Test", "test.xlsx", "abc", "v2", new(null, null), [sheet], []);
        await repository.ReplaceParsedEvidenceAsync(envelope, "v2", CancellationToken.None);
        var firstEvidence = await db.FundPortfolioReportSheets.AsNoTracking().Where(x => x.ReportId == reportId).Select(x => new { x.Id, x.OriginalSheetName, x.ParserProfileVersion }).SingleAsync();
        await repository.ReplaceParsedEvidenceAsync(envelope, "v2", CancellationToken.None);
        var secondEvidence = await db.FundPortfolioReportSheets.AsNoTracking().Where(x => x.ReportId == reportId).Select(x => new { x.Id, x.OriginalSheetName, x.ParserProfileVersion }).SingleAsync();
        Assert.Equal(1, await db.FundPortfolioReports.CountAsync(x => x.Id == reportId));
        Assert.Equal(firstEvidence, secondEvidence);
        Assert.Equal(1, await db.FundPortfolioReportSheets.CountAsync(x => x.ReportId == reportId));
    }

    [Fact]
    public async Task ScheduledWorker_WhenDisabled_DoesNotResolveOrRunDiscovery()
    {
        using var worker = new FundPortfolioScheduledDiscoveryWorker(
            new ThrowingScopeFactory(),
            Options.Create(new FundPortfolioScheduledDiscoveryOptions { Enabled = false }),
            NullLogger<FundPortfolioScheduledDiscoveryWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ImportUseCase_IsolatesMalformedWorkbookFromValidWorkbookInSameRun()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FinancialProviderDbContext>().UseSqlite(connection).Options;
        await using var db = new FinancialProviderDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreFundPortfolioImportRunRepository(db);
        var source = new StubSource();
        var malformed = new FundPortfolioReportSourceDescriptor("Stub", "malformed", "malformed.xlsx", null, "Fund", null, null, "malformed");
        var valid = new FundPortfolioReportSourceDescriptor("Stub", "valid", "valid.xlsx", null, "Fund", null, null, "valid");
        var runId = await repository.CreateRunAsync(new(FundPortfolioImportTriggerType.BulkBackfill, "Stub", "admin", [malformed, valid]), "corr", CancellationToken.None);
        await repository.AddItemsAsync(runId, [malformed, valid], CancellationToken.None);

        var importer = new ImportFundPortfolioItemUseCase(
            repository,
            new FundPortfolioReportSourceRegistry([source]),
            new StubIngestion(),
            new NoopReviews(),
            NullTelemetry.Instance);
        var items = (await repository.ListItemsAsync(new(runId), CancellationToken.None)).Items;
        var malformedStatus = await importer.ExecuteAsync(new(runId, items.Single(x => x.OriginalFileName == "malformed.xlsx").Id, MaximumAttempts: 1), CancellationToken.None);
        var validStatus = await importer.ExecuteAsync(new(runId, items.Single(x => x.OriginalFileName == "valid.xlsx").Id), CancellationToken.None);
        var final = await repository.FinalizeAsync(runId, CancellationToken.None);

        Assert.Equal(FundPortfolioImportItemStatus.Poisoned, malformedStatus);
        Assert.Equal(FundPortfolioImportItemStatus.Imported, validStatus);
        Assert.Equal(FundPortfolioImportRunStatus.CompletedWithErrors, final.Status);
        Assert.Equal(1, final.ImportedCount);
        Assert.Equal(1, final.FailedCount);
    }

    private sealed class StubSource : IFundPortfolioReportSource
    {
        public string ProviderName => "Stub";
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public Task<FundPortfolioSourcePage> DiscoverAsync(FundPortfolioSourceQuery query, CancellationToken cancellationToken) => Task.FromResult(new FundPortfolioSourcePage([], null));
        public Task<FundPortfolioSourceDownload> DownloadAsync(FundPortfolioReportSourceDescriptor descriptor, CancellationToken cancellationToken) => Task.FromResult(new FundPortfolioSourceDownload(new MemoryStream([1]), "application/octet-stream", 1, null));
    }

    private sealed class StubIngestion : IIngestFundPortfolioWorkbookUseCase
    {
        public async Task<IngestFundPortfolioWorkbookResult> ExecuteAsync(IngestFundPortfolioWorkbookRequest request, CancellationToken cancellationToken)
        {
            if (request.OriginalFileName.StartsWith("malformed", StringComparison.Ordinal)) throw new InvalidDataException("malformed workbook");
            await request.Workbook.CopyToAsync(Stream.Null, cancellationToken);
            return new(Guid.NewGuid(), FundResolutionStatus.Resolved, FundPortfolioParseStatus.Parsed, false, 1, "valid-hash");
        }
    }

    private sealed class NoopReviews : IFundPortfolioMappingReviewRepository
    {
        public Task<IReadOnlyList<FundPortfolioMappingReviewView>> ListAsync(FundPortfolioMappingReviewStatus? status, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FundPortfolioMappingReviewView>>([]);
        public Task<FundPortfolioMappingReviewPage> ListPageAsync(FundPortfolioMappingReviewStatus? status, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new FundPortfolioMappingReviewPage([], page, pageSize, 0));
        public Task<int> CreateFromReportIssuesAsync(Guid reportId, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<FundPortfolioMappingResolutionResult> ResolveAsync(ResolveFundPortfolioMappingReviewRequest request, CancellationToken cancellationToken) => Task.FromResult(new FundPortfolioMappingResolutionResult(false, 0, null));
    }

    private sealed class NullTelemetry : IFundPortfolioOperationalTelemetry
    {
        public static NullTelemetry Instance { get; } = new();
        public void RecordDiscovery(int count) { }
        public void RecordUpload(long bytes) { }
        public void RecordDownload(long bytes, double latencyMilliseconds) { }
        public void RecordRetry() { }
        public void RecordReview(int count) { }
        public void RecordFinalStatus(FundPortfolioImportRunStatus status) { }
        public void RecordQueueLag(TimeSpan lag) { }
        public void RecordOutcome(FundPortfolioImportItemStatus status) { }
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new Xunit.Sdk.XunitException("A disabled scheduled worker must not create a scope.");
    }
}
