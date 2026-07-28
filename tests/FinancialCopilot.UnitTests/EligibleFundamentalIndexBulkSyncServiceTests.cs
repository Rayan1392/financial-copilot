using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class EligibleFundamentalIndexBulkSyncServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-28T08:34:50Z");

    [Fact]
    public async Task ViewReader_ReadsExternalReferencesFromNoavaranEligibleCompanies()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateSqliteDb(connection);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "Companies" (
                "ExternalCompanyId" TEXT NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Companies" ("ExternalCompanyId") VALUES
                ('4'),
                ('12'),
                ('4'),
                (' 7 '),
                (NULL);
            """);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE VIEW "NoavaranEligibleCompanies" AS
            SELECT "ExternalCompanyId"
            FROM "Companies";
            """);

        var reader = new NoavaranEligibleCompanyViewReader(db);

        var references = await reader.ReadExternalReferencesAsync(CancellationToken.None);

        Assert.Equal(["4", "7", "12"], references);
    }

    [Fact]
    public async Task Run_DryRun_DoesNotPublishAnyRequests()
    {
        var reader = new StubReader(["4", "12"]);
        var publisher = new RecordingPublisher();
        var service = NewService(reader, publisher);

        var result = await service.RunAsync(
            new EligibleFundamentalIndexBulkSyncRequest(
                "User:test",
                ProviderSources.NoavaranCurrentApiName,
                "batch-1",
                MaxItems: null,
                DryRun: true),
            CancellationToken.None);

        Assert.Equal("DryRun", result.Status);
        Assert.Equal(2, result.EligibleCount);
        Assert.Equal(0, result.QueuedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(publisher.Requests);
        Assert.All(result.Items, item => Assert.Equal("DryRun", item.Status));
    }

    [Fact]
    public async Task Run_MaxItems_LimitsEligibleCompaniesAfterDeterministicOrdering()
    {
        var reader = new StubReader(["12", "4", "7"]);
        var publisher = new RecordingPublisher();
        var service = NewService(reader, publisher);

        var result = await service.RunAsync(
            new EligibleFundamentalIndexBulkSyncRequest(
                "User:test",
                ProviderSources.NoavaranCurrentApiName,
                "batch-2",
                MaxItems: 2,
                DryRun: false),
            CancellationToken.None);

        Assert.Equal(2, result.EligibleCount);
        Assert.Collection(
            publisher.Requests,
            request => Assert.Equal("4", request.ExternalReference),
            request => Assert.Equal("7", request.ExternalReference));
    }

    [Fact]
    public async Task Run_ChildIdempotencyKeys_AreDeterministicPerExternalReference()
    {
        var reader = new StubReader(["4", "12"]);
        var publisher = new RecordingPublisher();
        var service = NewService(reader, publisher);

        var result = await service.RunAsync(
            new EligibleFundamentalIndexBulkSyncRequest(
                "User:test",
                ProviderSources.NoavaranCurrentApiName,
                "fixed-batch-key",
                MaxItems: null,
                DryRun: false),
            CancellationToken.None);

        Assert.Equal(
            "fixed-batch-key:externalReference:4",
            result.Items.Single(item => item.ExternalReference == "4").IdempotencyKey);
        Assert.Equal(
            "fixed-batch-key:externalReference:12",
            result.Items.Single(item => item.ExternalReference == "12").IdempotencyKey);
    }

    [Fact]
    public async Task Run_PartialFailure_ReportsFailedAndQueuedItems()
    {
        var reader = new StubReader(["4", "12"]);
        var publisher = new RecordingPublisher { FailOnExternalReference = "12" };
        var service = NewService(reader, publisher);

        var result = await service.RunAsync(
            new EligibleFundamentalIndexBulkSyncRequest(
                "User:test",
                ProviderSources.NoavaranCurrentApiName,
                "batch-3",
                MaxItems: null,
                DryRun: false),
            CancellationToken.None);

        Assert.Equal("QueuedWithFailures", result.Status);
        Assert.Equal(2, result.EligibleCount);
        Assert.Equal(1, result.QueuedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal("Queued", result.Items.Single(item => item.ExternalReference == "4").Status);
        var failed = result.Items.Single(item => item.ExternalReference == "12");
        Assert.Equal("Failed", failed.Status);
        Assert.NotNull(failed.Error);
    }

    [Fact]
    public async Task Run_EmptyEligibleList_ReturnsZeroQueuedItems()
    {
        var reader = new StubReader([]);
        var publisher = new RecordingPublisher();
        var service = NewService(reader, publisher);

        var result = await service.RunAsync(
            new EligibleFundamentalIndexBulkSyncRequest(
                "User:test",
                ProviderSources.NoavaranCurrentApiName,
                "batch-4",
                MaxItems: null,
                DryRun: false),
            CancellationToken.None);

        Assert.Equal("Queued", result.Status);
        Assert.Equal(0, result.EligibleCount);
        Assert.Equal(0, result.QueuedCount);
        Assert.Empty(result.Items);
        Assert.Empty(publisher.Requests);
    }

    private static EligibleFundamentalIndexBulkSyncService NewService(
        INoavaranEligibleCompanyReferenceReader reader,
        RecordingPublisher publisher) =>
        new(
            reader,
            publisher,
            new FixedTimeProvider(Now),
            NullLogger<EligibleFundamentalIndexBulkSyncService>.Instance);

    private static FinancialIngestionDbContext CreateSqliteDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseSqlite(connection)
            .Options);

    private sealed class StubReader(IReadOnlyCollection<string> references)
        : INoavaranEligibleCompanyReferenceReader
    {
        public IReadOnlyCollection<string> SourceReferences { get; } = references;

        public Task<IReadOnlyCollection<string>> ReadExternalReferencesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(SourceReferences);
    }

    private sealed class RecordingPublisher : IDataSyncRequestPublisher
    {
        public List<DataSyncRequest> Requests { get; } = [];
        public string? FailOnExternalReference { get; set; }

        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
        {
            if (request.ExternalReference == FailOnExternalReference)
            {
                throw new InvalidOperationException("simulated enqueue failure");
            }

            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
