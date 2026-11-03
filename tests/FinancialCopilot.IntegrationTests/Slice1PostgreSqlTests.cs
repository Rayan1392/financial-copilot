using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class Slice1PostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly CalculationDate = new(2026, 8, 12);

    [Fact]
    public async Task UniverseReader_UsesOnlyNoavaranEligibleCompaniesSymbolIsin()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var eligibleMarket = new NormalizedMarketRow
        {
            Id = NoavaranEligibleMarketId,
            ProviderName = "NoavaranCurrentApi",
            ExternalId = "bourse",
            Name = "Bourse"
        };
        db.Markets.Add(eligibleMarket);
        db.Companies.AddRange(
            Company("eligible", "ELIGIBLE", "IROELIGIBLE1", NoavaranEligibleMarketId, 0),
            Company("wrong-provider", "WRONG", "IROWRONG0001", NoavaranEligibleMarketId, 0, "OtherProvider"),
            Company("wrong-right", "RIGHT", "IRORIGHT0001", NoavaranEligibleMarketId, 1));
        await db.SaveChangesAsync();

        var reader = new NoavaranEligibleCompanyUniverseReader(db);
        var admitted = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(["IROELIGIBLE1"], admitted.Select(item => item.SymbolIsin).OfType<string>().ToArray());
    }

    [Fact]
    public async Task LeaseAndSourceFactPersistence_UsesExistingRowsAndRejectsStaleOwner()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var company = Company("fact", "FACT", "IROFACT00001", NoavaranEligibleMarketId, 0);
        db.Markets.Add(new NormalizedMarketRow
        {
            Id = NoavaranEligibleMarketId,
            ProviderName = "NoavaranCurrentApi",
            ExternalId = "bourse",
            Name = "Bourse"
        });
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var leases = new IndustryRelativeValuationLeaseStore(db, new FixedTimeProvider(Now));
        var first = await leases.TryAcquireAsync("feature126", CalculationDate, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal("feature126", await db.IndustryRelativeValuationSourceLeases.Select(row => row.LeaseName).SingleAsync());

        var stale = new LeaseHandle(
            "feature126", CalculationDate, Guid.NewGuid(), Now.AddMinutes(5));
        Assert.False(await leases.RenewAsync(stale, TimeSpan.FromMinutes(5), CancellationToken.None));

        var store = new IndustryRelativeValuationSourceFactStore(db, new FixedTimeProvider(Now));
        var result = ReadyResult("observation-1");
        Assert.Equal(Feature126SourceFactWriteResult.Persisted,
            await store.PersistAcceptedAsync(company.Id, result, first!, CancellationToken.None));
        Assert.Equal(Feature126SourceFactWriteResult.Rejected,
            await store.PersistAcceptedAsync(company.Id, result with { SourceObservationId = "observation-2" }, stale, CancellationToken.None));
        Assert.Single(await db.IndustryRelativeValuationSourceFacts.ToListAsync());
    }

    [Fact]
    public async Task LeaseTakeover_RejectsStaleRenewalAndWrite_AllowsCurrentToken()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var company = Company("takeover", "TAKEOVER", "IROTAKEOVER1", NoavaranEligibleMarketId, 0);
        db.Markets.Add(new NormalizedMarketRow
        {
            Id = NoavaranEligibleMarketId,
            ProviderName = "NoavaranCurrentApi",
            ExternalId = "bourse",
            Name = "Bourse"
        });
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var clock = new MutableTimeProvider(Now);
        var ownerStore = new IndustryRelativeValuationLeaseStore(db, clock);
        var owner = await ownerStore.TryAcquireAsync("feature126", CalculationDate, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(owner);
        clock.UtcNow = Now.AddMinutes(2);
        var takeover = await ownerStore.TryAcquireAsync("feature126", CalculationDate, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.NotNull(takeover);
        Assert.NotEqual(owner!.FencingToken, takeover!.FencingToken);

        Assert.False(await ownerStore.RenewAsync(owner, TimeSpan.FromMinutes(5), CancellationToken.None));
        Assert.False(await ownerStore.IsOwnerAsync(owner, CancellationToken.None));
        Assert.False(await ownerStore.TransitionAsync(owner, LeaseState.Succeeded, CancellationToken.None));
        Assert.True(takeover.RecoveredLease);
        var store = new IndustryRelativeValuationSourceFactStore(db, clock);
        Assert.Equal(Feature126SourceFactWriteResult.Rejected,
            await store.PersistAcceptedAsync(company.Id, ReadyResult("stale"), owner, CancellationToken.None));
        Assert.Equal(Feature126SourceFactWriteResult.Persisted,
            await store.PersistAcceptedAsync(company.Id, ReadyResult("current"), takeover, CancellationToken.None));
        Assert.True(await ownerStore.TransitionAsync(takeover, LeaseState.Succeeded, CancellationToken.None));
    }

    [Fact]
    public async Task SameDaySucceededMarkerProducesObservableNoOpSummary()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        db.IndustryRelativeValuationSourceLeases.Add(new IndustryRelativeValuationSourceLeaseRow
        {
            LeaseName = "feature126",
            Owner = new LeaseOwnerId("feature126", CalculationDate, Guid.NewGuid(), LeaseState.Succeeded).Envelope,
            UpdatedAtUtc = Now,
            ExpiresAtUtc = Now
        });
        await db.SaveChangesAsync();

        var registry = new Feature126OperationalSummaryRegistry();
        var pipeline = new RelativeValuationPipeline(
            new EmptyUniverseReader(), new EmptyPsOperation(), new EmptyProvider(),
            new EmptyFactStore(), new IndustryRelativeValuationLeaseStore(db, new FixedTimeProvider(Now)),
            Options.Create(new RelativeValuationIngestionOptions { Enabled = true }),
            new FixedTimeProvider(Now), NullLogger<RelativeValuationPipeline>.Instance,
            new EmptyHandoffBoundary(), summarySink: registry,
            featureOptions: Options.Create(new Feature126Options { Enabled = true }));

        var result = await pipeline.RunAsync("same-day-no-op", CancellationToken.None);

        Assert.NotNull(result.OperationalSummary);
        Assert.Equal(Feature126RunState.CurrentDaySucceededNoOp, result.OperationalSummary!.RunState);
        Assert.Single(registry.ReadRecent());
        Assert.Equal(Feature126RunState.CurrentDaySucceededNoOp, registry.ReadRecent()[0].Summary.RunState);
        Assert.DoesNotContain((byte)0, registry.ReadRecent()[0].CanonicalJson);
    }

    [Fact]
    public async Task LeaseLossAndRecoveredExecutionArePublishedAsDistinctSummaries()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var store = new IndustryRelativeValuationLeaseStore(db, new FixedTimeProvider(Now));
        var registry = new Feature126OperationalSummaryRegistry();
        var lost = Feature126OperationalSummaryFactory.Create(
            "lease-lost", Now, Now.AddSeconds(1), CalculationDate, true,
            Feature126RunState.LeaseLost, Feature126LeaseStatus.Lost, failures: new Dictionary<string, long?>
            {
                ["LeaseLost"] = 1
            });
        var recovered = Feature126OperationalSummaryFactory.Create(
            "recovered", Now, Now.AddSeconds(2), CalculationDate, true,
            Feature126RunState.Success, Feature126LeaseStatus.Recovered, true);
        registry.Publish(lost);
        registry.Publish(recovered);

        Assert.Equal(
            [Feature126RunState.LeaseLost, Feature126RunState.Success],
            registry.ReadRecent().Select(x => x.Summary.RunState).ToArray());
        Assert.Equal(1, registry.ReadRecent()[0].Summary.FailureCodeCounts["LeaseLost"]);
        Assert.True(registry.ReadRecent()[1].Summary.RecoveredLease);
        Assert.NotNull(await store.TryAcquireAsync("feature126", CalculationDate, TimeSpan.FromMinutes(5), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentTakeoverAndStaleWrite_CommitTakeoverAndRejectWrite()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var company = Company("race", "RACE", "IRORACE00001", NoavaranEligibleMarketId, 0);
        db.Markets.Add(new NormalizedMarketRow
        {
            Id = NoavaranEligibleMarketId,
            ProviderName = "NoavaranCurrentApi",
            ExternalId = "bourse",
            Name = "Bourse"
        });
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var first = await new IndustryRelativeValuationLeaseStore(db, new FixedTimeProvider(Now))
            .TryAcquireAsync("feature126", CalculationDate, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(first);

        var secondToken = Guid.NewGuid();
        await using var takeoverConnection = new NpgsqlConnection(database.ConnectionString);
        await takeoverConnection.OpenAsync();
        await using var takeoverTransaction = await takeoverConnection.BeginTransactionAsync();
        await using (var takeover = new NpgsqlCommand("""
            UPDATE "IndustryRelativeValuationSourceLeases"
            SET "Owner" = @owner, "UpdatedAtUtc" = @updated, "ExpiresAtUtc" = @expires
            WHERE "LeaseName" = @leaseName
            """, takeoverConnection, takeoverTransaction))
        {
            takeover.Parameters.AddWithValue("owner",
                new LeaseOwnerId("feature126", CalculationDate, secondToken, LeaseState.Running).Envelope);
            takeover.Parameters.AddWithValue("updated", Now.AddMinutes(2));
            takeover.Parameters.AddWithValue("expires", Now.AddMinutes(7));
            takeover.Parameters.AddWithValue("leaseName", "feature126");
            Assert.Equal(1, await takeover.ExecuteNonQueryAsync());
        }

        var staleWrite = Task.Run(async () =>
        {
            await using var writeDb = database.CreateContext();
            var store = new IndustryRelativeValuationSourceFactStore(
                writeDb, new FixedTimeProvider(Now.AddMinutes(2)));
            return await store.PersistAcceptedAsync(
                company.Id, ReadyResult("stale-race"), first!, CancellationToken.None);
        });

        await WaitForLeaseLockWaiterAsync(database.ConnectionString);
        await takeoverTransaction.CommitAsync();

        Assert.Equal(Feature126SourceFactWriteResult.Rejected, await staleWrite);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.IndustryRelativeValuationSourceFacts.ToListAsync());
        Assert.Equal(
            new LeaseOwnerId("feature126", CalculationDate, secondToken, LeaseState.Running).Envelope,
            await verify.IndustryRelativeValuationSourceLeases
                .Where(row => row.LeaseName == "feature126")
                .Select(row => row.Owner)
                .SingleAsync());
    }

    private static NormalizedCompanyRow Company(
        string externalId,
        string symbol,
        string isin,
        Guid marketId,
        int precedency,
        string provider = "NoavaranCurrentApi") => new()
        {
            Id = Guid.NewGuid(),
            ProviderName = provider,
            ExternalCompanyId = externalId,
            Name = symbol,
            SymbolIsin = isin,
            MarketId = marketId,
            PrecedencyRight = precedency,
            LastSynchronizedAt = Now
        };

    private static RelativeValuationProviderResult ReadyResult(string observationId) => new(
        RelativeValuationSourceKind.PEGauge,
        5m,
        7m,
        observationId,
        "pe/circle-chart-data/isin",
        "response-identity:isin",
        RelativeValuationFactReadiness.Ready,
        "Valid",
        $"hash-{observationId}",
        "{}",
        Now);

    private static async Task WaitForLeaseLockWaiterAsync(string connectionString)
    {
        await using var monitor = new NpgsqlConnection(connectionString);
        await monitor.OpenAsync();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS
                (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND wait_event_type = 'Lock'
                      AND query LIKE '%IndustryRelativeValuationSourceLeases%'
                )
                """, monitor);
            if ((bool)(await command.ExecuteScalarAsync())!)
                return;
            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException("The stale writer did not reach the PostgreSQL lease lock.");
    }

    private static readonly Guid NoavaranEligibleMarketId =
        Guid.Parse("037c69ad-f519-419f-ae62-59003b6b2428");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class EmptyUniverseReader : IEligibleUniverseReader
    {
        public Task<IReadOnlyList<RelativeValuationEligibleSymbol>> ReadAsync(CancellationToken _) =>
            Task.FromResult<IReadOnlyList<RelativeValuationEligibleSymbol>>(Array.Empty<RelativeValuationEligibleSymbol>());
    }

    private sealed class EmptyPsOperation : ICyclicalWavesPsAcceptedOperation
    {
        public Task<PsProviderResult<PsGaugeDistribution>> AcquireAcceptedPsGaugeAsync(string _, CancellationToken __) =>
            throw new InvalidOperationException("No provider call is expected for same-day no-op.");
    }

    private sealed class EmptyProvider : ICyclicalWavesRelativeValuationProviderClient
    {
        public Task<RelativeValuationProviderResult> GetPeGaugeAsync(string _, CancellationToken __) =>
            throw new InvalidOperationException("No provider call is expected for same-day no-op.");

        public Task<RelativeValuationProviderResult> GetEquilibriumGaugeAsync(string _, CancellationToken __) =>
            throw new InvalidOperationException("No provider call is expected for same-day no-op.");
    }

    private sealed class EmptyFactStore : IFeature126SourceFactStore
    {
        public Task<Feature126SourceFactWriteResult> PersistAcceptedAsync(Guid _, RelativeValuationProviderResult __, LeaseHandle ___, CancellationToken ____) =>
            throw new InvalidOperationException("No persistence call is expected for same-day no-op.");

        public Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(DateOnly date, CancellationToken _) =>
            Task.FromResult(Feature126SourceSnapshotEvidence.Create(date, Array.Empty<Feature126SourceFactEvidence>()));
    }

    private sealed class EmptyHandoffBoundary : IFeature125HandoffSubmissionBoundary
    {
        public Task<Feature125HandoffValidationResult> SubmitAsync(Feature126HandoffPackage _, Feature126HandoffLeaseState __, DateTimeOffset ___, CancellationToken ____) =>
            throw new InvalidOperationException("No handoff call is expected for same-day no-op.");
    }
}
