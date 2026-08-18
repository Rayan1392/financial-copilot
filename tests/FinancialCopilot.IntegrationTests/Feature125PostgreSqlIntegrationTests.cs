using System.Data;
using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class Feature125PostgreSqlIntegrationTests(PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateOnly Day = new(2026, 8, 11);
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Concurrent_same_day_evaluations_use_independent_connections_and_converge_once()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var industry = Guid.NewGuid();
        Guid calculation;
        await using (var seed = database.CreateContext())
        {
            await SeedIndustryAsync(seed, industry);
            calculation = await AddCalculationAsync(seed, industry, Day, 1, "Published", true, WatchOutcome.Entry);
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        Assert.NotEqual(await BackendPidAsync(firstContext), await BackendPidAsync(secondContext));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = EvaluateAfterGateAsync(firstContext, gate.Task, industry, calculation);
        var second = EvaluateAfterGateAsync(secondContext, gate.Task, industry, calculation);
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => !result!.NoOp);
        Assert.Single(results, result => result!.NoOp);
        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.IndustryWatchEvaluations.CountAsync());
        Assert.Equal(1, await verify.IndustryWatchEvaluations.CountAsync(row => row.IsEffective));
        Assert.Equal(1, await verify.IndustryWatchTransitions.CountAsync());
        var state = await verify.IndustryWatchStates.SingleAsync();
        Assert.Equal(IndustryWatchState.Watching.ToString(), state.State);
        Assert.Equal(1, state.EntryStreak);
        Assert.Equal(0, state.ExitStreak);
    }

    [SkippableFact]
    public async Task Concurrent_retries_and_reload_remain_idempotent()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var industry = Guid.NewGuid();
        Guid calculation;
        await using (var seed = database.CreateContext())
        {
            await SeedIndustryAsync(seed, industry);
            calculation = await AddCalculationAsync(seed, industry, Day, 1, "Published", true, WatchOutcome.Entry);
        }

        await using (var initial = database.CreateContext())
            Assert.False((await EvaluateAsync(initial, industry, calculation))!.NoOp);

        var retries = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = database.CreateContext();
            return await EvaluateAsync(context, industry, calculation);
        });
        var replayResults = await Task.WhenAll(retries);
        Assert.All(replayResults, result => Assert.True(result!.NoOp));

        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.IndustryWatchEvaluations.CountAsync());
        Assert.Equal(1, await verify.IndustryWatchTransitions.CountAsync());
        Assert.Equal(1, (await verify.IndustryWatchStates.SingleAsync()).EntryStreak);
    }

    [SkippableFact]
    public async Task Snapshot_writer_is_the_publication_boundary_and_only_published_selected_advances()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var industry = Guid.NewGuid();
        var companies = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var seed = database.CreateContext())
        {
            await SeedIndustryAsync(seed, industry);
            await SeedCompaniesAsync(seed, industry, companies);
            var statuses = new[] { "Published", "Pending", "Ready", "Failed", "Inconclusive" };
            for (var index = 0; index < statuses.Length; index++)
                await AddCalculationAsync(seed, industry, Day, index + 1, statuses[index], false, WatchOutcome.Entry);
        }

        IndustryRelativeValuationSnapshotWriteResult published;
        await using (var context = database.CreateContext())
        {
            var writer = Writer(context);
            published = await writer.WriteAsync(Day,
                BuildInput(industry, companies, WatchOutcome.Entry, "published-selected"), Now,
                CancellationToken.None);
            Assert.Equal("Published", published.Status);
            Assert.False(published.NoOp);

            var replay = await writer.WriteAsync(Day,
                BuildInput(industry, companies, WatchOutcome.Entry, "published-selected"), Now.AddMinutes(1),
                CancellationToken.None);
            Assert.True(replay.NoOp);
            Assert.Equal(published.CalculationId, replay.CalculationId);

            var inconclusive = await writer.WriteAsync(Day,
                BuildInput(industry, companies, WatchOutcome.Inconclusive, "inconclusive"), Now.AddMinutes(2),
                CancellationToken.None);
            Assert.Equal("Inconclusive", inconclusive.Status);
        }

        await using var verify = database.CreateContext();
        var evaluation = await verify.IndustryWatchEvaluations.SingleAsync();
        Assert.Equal(published.CalculationId, evaluation.CalculationId);
        Assert.Equal(1, (await verify.IndustryWatchStates.SingleAsync()).EntryStreak);
        Assert.False(await verify.IndustryWatchEvaluations.AnyAsync(row =>
            verify.IndustryRelativeValuationCalculations
                .Where(calculation => calculation.Status != "Published" || !calculation.IsSelectedCurrent)
                .Select(calculation => calculation.Id)
                .Contains(row.CalculationId)));
    }

    public static TheoryData<WatchOutcome, WatchOutcome> CorrectionCases => new()
    {
        { WatchOutcome.Entry, WatchOutcome.Neutral },
        { WatchOutcome.Neutral, WatchOutcome.Entry },
        { WatchOutcome.Entry, WatchOutcome.Exit },
        { WatchOutcome.Entry, WatchOutcome.Inconclusive },
        { WatchOutcome.Inconclusive, WatchOutcome.Entry }
    };

    [SkippableTheory]
    [MemberData(nameof(CorrectionCases))]
    public async Task Same_date_correction_retains_evidence_and_uses_only_latest_selected_version(
        WatchOutcome firstOutcome, WatchOutcome correctedOutcome)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var industry = Guid.NewGuid();
        Guid first;
        await using (var seed = database.CreateContext())
        {
            await SeedIndustryAsync(seed, industry);
            first = await AddCalculationAsync(seed, industry, Day, 1, "Published", true, firstOutcome);
        }
        await using (var context = database.CreateContext())
            _ = await EvaluateAsync(context, industry, first, new(3, 3));

        Guid corrected;
        await using (var correction = database.CreateContext())
        {
            var prior = await correction.IndustryRelativeValuationCalculations.SingleAsync(row => row.Id == first);
            prior.IsSelectedCurrent = false;
            prior.IsLatestEvaluation = false;
            corrected = await AddCalculationAsync(correction, industry, Day, 2, "Published", true, correctedOutcome);
        }
        await using (var context = database.CreateContext())
        {
            var result = await EvaluateAsync(context, industry, corrected, new(3, 3));
            Assert.False(result!.NoOp);
        }
        await using (var replayContext = database.CreateContext())
            Assert.True((await EvaluateAsync(replayContext, industry, corrected, new(3, 3)))!.NoOp);

        await using var verify = database.CreateContext();
        var evaluations = await verify.IndustryWatchEvaluations.OrderBy(row => row.EvaluatedAtUtc).ToArrayAsync();
        Assert.Equal(2, evaluations.Length);
        Assert.Equal(ToDomainOutcome(firstOutcome).ToString(), evaluations.Single(row => row.CalculationId == first).Outcome);
        Assert.False(evaluations.Single(row => row.CalculationId == first).IsEffective);
        var effective = Assert.Single(evaluations, row => row.IsEffective);
        Assert.Equal(corrected, effective.CalculationId);
        Assert.Equal(ToDomainOutcome(correctedOutcome).ToString(), effective.Outcome);

        var expected = IndustryWatchStateMachine.EvaluateOutcome(
            IndustryWatchState.NotWatching, 0, 0, ToDomainOutcome(correctedOutcome), new(3, 3));
        var state = await verify.IndustryWatchStates.SingleAsync();
        Assert.Equal(expected.NewState.ToString(), state.State);
        Assert.Equal(expected.NewEntryStreak, state.EntryStreak);
        Assert.Equal(expected.NewExitStreak, state.ExitStreak);
    }

    [SkippableFact]
    public async Task Restart_reload_resumes_persisted_streak_without_process_local_state()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var industry = Guid.NewGuid();
        Guid first;
        await using (var seed = database.CreateContext())
        {
            await SeedIndustryAsync(seed, industry);
            first = await AddCalculationAsync(seed, industry, Day, 1, "Published", true, WatchOutcome.Entry);
        }
        await using (var firstScope = database.CreateContext())
            _ = await EvaluateAsync(firstScope, industry, first, new(3, 3));

        Guid second;
        await using (var restartedSeedScope = database.CreateContext())
            second = await AddCalculationAsync(restartedSeedScope, industry, Day.AddDays(1), 1, "Published", true, WatchOutcome.Entry);
        await using (var restartedEvaluationScope = database.CreateContext())
        {
            var result = await EvaluateAsync(restartedEvaluationScope, industry, second, new(3, 3));
            Assert.Equal(IndustryWatchState.EntryPending, result!.State);
            Assert.Equal(2, result.EntryStreak);
        }
        await using (var replayAfterRestart = database.CreateContext())
            Assert.True((await EvaluateAsync(replayAfterRestart, industry, second, new(3, 3)))!.NoOp);

        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.IndustryWatchEvaluations.CountAsync(row => row.IsEffective));
        Assert.Equal(2, (await verify.IndustryWatchStates.SingleAsync()).EntryStreak);
    }

    [SkippableFact]
    public async Task Clean_database_migrations_create_feature125_schema_indexes_and_constraints()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var context = database.CreateContext();
        Assert.Contains("20260812063122_Feature125Slice3Persistence",
            await context.Database.GetAppliedMigrationsAsync());
        Assert.Contains(
            (await context.Database.GetAppliedMigrationsAsync()),
            migration => migration.EndsWith("_AddIndustryRelativeValuationGroupCohort", StringComparison.Ordinal));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var tables = await StringsAsync(connection,
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND (tablename LIKE '%IndustryRelativeValuation%' OR tablename LIKE 'IndustryWatch%') ORDER BY tablename");
        Assert.Contains("CompanyIndustryRelativeValuations", tables);
        Assert.Contains("IndustryRelativeValuationCalculations", tables);
        Assert.Contains("IndustryRelativeValuationMetrics", tables);
        Assert.Contains("IndustryRelativeValuationOutbox", tables);
        Assert.Contains("IndustryRelativeValuationSourceFacts", tables);
        Assert.Contains("IndustryRelativeValuationSourceLeases", tables);
        Assert.Contains("IndustryWatchEvaluations", tables);
        Assert.Contains("IndustryWatchStates", tables);
        Assert.Contains("IndustryWatchTransitions", tables);

        Assert.True(await ScalarBoolAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND tablename='IndustryWatchStates' AND indexdef LIKE 'CREATE UNIQUE INDEX%')"));
        Assert.True(await ScalarBoolAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND tablename='IndustryWatchEvaluations' AND indexdef LIKE 'CREATE UNIQUE INDEX%')"));
        Assert.True(await ScalarBoolAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND tablename='IndustryRelativeValuationCalculations' AND indexdef LIKE '%IsSelectedCurrent%' AND indexdef LIKE '%WHERE%')"));
        Assert.True(await ScalarBoolAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='IndustryRelativeValuationCalculations' AND column_name='GroupId' AND is_nullable='YES')"));
        Assert.True(await ScalarBoolAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND tablename='IndustryRelativeValuationCalculations' AND indexdef LIKE '%GroupId%' AND indexdef LIKE '%WHERE%')"));
        Assert.True(await ScalarBoolAsync(connection,
            "SELECT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE table_schema='public' AND table_name='IndustryWatchEvaluations' AND constraint_type='FOREIGN KEY')"));
    }

    [SkippableFact]
    public async Task Valid_feature126_handoff_triggers_feature125_publication_and_idempotent_watch_evaluation()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var industry = Guid.NewGuid();
        var companies = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var seed = database.CreateContext())
        {
            await SeedIndustryAsync(seed, industry);
            await SeedCompaniesAsync(seed, industry, companies);
            foreach (var company in companies)
            foreach (var (kind, current, reference) in new[]
                     {
                         ("PEGauge", 90m, 100m),
                         ("PSGauge", 80m, 100m),
                         ("EquilibriumGauge", 70m, 100m)
                     })
            {
                seed.IndustryRelativeValuationSourceFacts.Add(new IndustryRelativeValuationSourceFactRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company,
                    ProviderName = "CyclicalWaves",
                    SourceKind = kind,
                    SourceObservationId = $"{company:N}-{kind}",
                    CurrentValue = current,
                    ReferenceValue = reference,
                    FetchedAtUtc = Now.AddMinutes(-1),
                    PersistedAtUtc = Now.AddMinutes(-1),
                    SourceEndpoint = $"test/{kind}",
                    SourceWatermark = $"watermark-{company:N}-{kind}",
                    PayloadHash = $"hash-{company:N}-{kind}",
                    Readiness = "Ready",
                    QualityCode = "Valid",
                    IdentityEvidence = "test-identity",
                    RawPayload = "{}"
                });
            }
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var source = new StubFeature125SourceIngestion();
        var sourceFacts = new IndustryRelativeValuationSourceFactStore(context, new FixedTimeProvider(Now));
        var pipeline = new IndustryRelativeValuationOrchestrationService(
            source,
            new IndustryRelativeValuationCalculationInputBuilder(context),
            new IndustryRelativeValuationCalculationSnapshotWriter(
                context,
                new IndustryWatchEvaluationService(context, new(1, 1))),
            Options.Create(new IndustryRelativeValuationOptions
            {
                Enabled = true,
                SourceFreshnessHours = 26,
                EntryConsecutiveSnapshots = 1,
                ExitConsecutiveSnapshots = 1
            }),
            Options.Create(new IndustryRelativeValuationSourceOptions
            {
                CanonicalProviderName = "NADPCO"
            }),
            new FixedTimeProvider(Now),
            NullLogger<IndustryRelativeValuationOrchestrationService>.Instance,
            sourceFacts,
            new Feature125HandoffConsumer());
        var admitted = companies.Select((company, index) =>
            new RelativeValuationEligibleSymbol($"IRTEST{index + 1:000}", company)).ToArray();
        var snapshot = await sourceFacts.ReadCurrentSnapshotAsync(Day, admitted, CancellationToken.None);
        var package = Feature126HandoffPackage.Create(
            new Feature126RunIdentity("feature126-integration", Day),
            Guid.NewGuid(),
            snapshot.Facts);
        var lease = new Feature126HandoffLeaseState(
            "feature126", Day, LeaseState.Handoff, package.FencingToken, Now.AddMinutes(5));
        await using (var leaseSeed = database.CreateContext())
        {
            leaseSeed.IndustryRelativeValuationSourceLeases.Add(new IndustryRelativeValuationSourceLeaseRow
            {
                LeaseName = lease.LeaseName,
                Owner = new LeaseOwnerId(lease.LeaseName, lease.CalculationDate, lease.FencingToken, LeaseState.Handoff).Envelope,
                ExpiresAtUtc = lease.ExpiresAtUtc,
                UpdatedAtUtc = Now
            });
            await leaseSeed.SaveChangesAsync();
        }

        var first = await pipeline.SubmitAsync(package, lease, Now, CancellationToken.None);
        var second = await pipeline.SubmitAsync(package, lease, Now, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Single(await context.IndustryRelativeValuationCalculations.ToArrayAsync());
        Assert.Equal("Published", (await context.IndustryRelativeValuationCalculations.SingleAsync()).Status);
        Assert.Single(await context.IndustryWatchEvaluations.ToArrayAsync());
        Assert.Single(await context.IndustryWatchTransitions.ToArrayAsync());
        Assert.Equal(0, source.InvocationCount);
    }

    private static IndustryRelativeValuationCalculationSnapshotWriter Writer(FinancialIngestionDbContext context) =>
        new(context, new IndustryWatchEvaluationService(context, new(1, 1)));

    private static async Task<IndustryWatchEvaluationResult?> EvaluateAfterGateAsync(
        FinancialIngestionDbContext context, Task gate, Guid industry, Guid calculation)
    {
        await gate;
        return await EvaluateAsync(context, industry, calculation);
    }

    private static Task<IndustryWatchEvaluationResult?> EvaluateAsync(
        FinancialIngestionDbContext context, Guid industry, Guid calculation,
        IndustryWatchOptions? options = null) =>
        new IndustryWatchEvaluationService(context, options ?? new(1, 1))
            .EvaluateAsync(industry, calculation, "Daily", Now, CancellationToken.None);

    private static async Task<int> BackendPidAsync(FinancialIngestionDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task SeedIndustryAsync(FinancialIngestionDbContext context, Guid industry)
    {
        context.Industries.Add(new NormalizedIndustryRow
        {
            Id = industry,
            ProviderName = "NADPCO",
            ExternalId = $"industry-{industry:N}",
            Name = "Feature 125 Test Industry",
            LastSynchronizedAt = Now
        });
        context.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = industry,
            ProviderName = "NADPCO",
            ExternalId = $"group-{industry:N}",
            Name = "Feature 125 Test Group",
            LastSynchronizedAt = Now
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedCompaniesAsync(
        FinancialIngestionDbContext context, Guid industry, IReadOnlyList<Guid> companies)
    {
        for (var index = 0; index < companies.Count; index++)
            context.Companies.Add(new NormalizedCompanyRow
            {
                Id = companies[index],
                ProviderName = "NADPCO",
                ExternalCompanyId = $"company-{companies[index]:N}",
                Name = $"Company {index + 1}",
                IndustryId = industry,
                GroupId = industry,
                SymbolIsin = $"IRTEST{index + 1:000}",
                LastSynchronizedAt = Now
            });
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> AddCalculationAsync(
        FinancialIngestionDbContext context,
        Guid industry,
        DateOnly date,
        int version,
        string status,
        bool selected,
        WatchOutcome outcome)
    {
        var id = Guid.NewGuid();
        context.IndustryRelativeValuationCalculations.Add(new IndustryRelativeValuationCalculationRow
        {
            Id = id,
            GroupId = industry,
            GroupExternalId = $"group-{industry:N}",
            GroupTitleSnapshot = "Feature 125 Test Group",
            IndustryId = industry,
            CalculationDate = date,
            CalculationVersion = version,
            Status = status,
            IsSelectedCurrent = selected,
            IsLatestEvaluation = selected,
            IndustryExternalId = $"industry-{industry:N}",
            IndustryTitleSnapshot = "Feature 125 Test Industry",
            AlgorithmVersion = IndustryRelativeValuationEngine.AlgorithmVersion,
            MembershipHash = $"membership-{date:yyyyMMdd}-{version}",
            SourceBarrierHash = $"source-{date:yyyyMMdd}-{version}-{Guid.NewGuid():N}",
            SourceBarrierEvidenceJson = "[]",
            CalculatedAtUtc = Now,
            PublishedAtUtc = status == "Published" ? Now : null
        });
        foreach (var metric in Enum.GetValues<RelativeValuationMetric>())
        {
            var (cleanCount, average) = MetricValue(outcome, metric);
            context.IndustryRelativeValuationMetrics.Add(new IndustryRelativeValuationMetricRow
            {
                Id = Guid.NewGuid(),
                CalculationId = id,
                MetricKind = metric.ToString(),
                ValidCount = cleanCount,
                CleanCount = cleanCount,
                CleanAverage = average,
                Readiness = cleanCount >= 2 ? "Ready" : "Inconclusive",
                Reason = cleanCount >= 2 ? "Ready" : "InsufficientCleanObservations"
            });
        }
        await context.SaveChangesAsync();
        return id;
    }

    private static IndustryRelativeValuationCalculationInput BuildInput(
        Guid industry, IReadOnlyList<Guid> companies, WatchOutcome outcome, string identity)
    {
        var members = companies.Select(company => new CanonicalIndustryMember(
            company, industry, $"industry-{industry:N}", "Feature 125 Test Industry")).ToArray();
        var facts = new List<RelativeValuationSourceFact>();
        foreach (var company in companies)
        foreach (var metric in Enum.GetValues<RelativeValuationMetric>())
        {
            if (outcome == WatchOutcome.Inconclusive && metric == RelativeValuationMetric.Equilibrium)
                continue;
            var current = outcome == WatchOutcome.Inconclusive
                ? 99m
                : MetricValue(outcome, metric).Average!.Value;
            var observation = $"{identity}-{company:N}-{metric}";
            facts.Add(new RelativeValuationSourceFact(
                company, metric, current, 100m, true, true, true,
                Now.AddMinutes(-1), Now.AddMinutes(-1), observation, DeterministicGuid(observation), "v1", observation));
        }
        var barrier = IndustryRelativeValuationSourceBarrierBuilder.Build(members, facts, Now, TimeSpan.FromHours(26));
        var result = IndustryRelativeValuationEngine.Calculate(members, barrier.SelectedFacts,
            new RelativeValuationCalculationContext("NADPCO", Now, TimeSpan.FromHours(26)));
        return new(
            industry, $"group-{industry:N}", "Feature 125 Test Group", members, barrier, result,
            industry, $"industry-{industry:N}", "Feature 125 Test Industry");
    }

    private static (int CleanCount, decimal? Average) MetricValue(WatchOutcome outcome, RelativeValuationMetric metric) =>
        outcome switch
        {
            WatchOutcome.Entry => (2, 99m),
            WatchOutcome.Exit => (2, 101m),
            WatchOutcome.Neutral when metric == RelativeValuationMetric.Pe => (2, 100m),
            WatchOutcome.Neutral => (2, 99m),
            _ => (1, null)
        };

    private static IndustryWatchEvaluationOutcome ToDomainOutcome(WatchOutcome outcome) => outcome switch
    {
        WatchOutcome.Entry => IndustryWatchEvaluationOutcome.EntryQualifying,
        WatchOutcome.Exit => IndustryWatchEvaluationOutcome.ExitQualifying,
        WatchOutcome.Neutral => IndustryWatchEvaluationOutcome.Neutral,
        _ => IndustryWatchEvaluationOutcome.Inconclusive
    };

    private static Guid DeterministicGuid(string value) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));

    private static async Task<string[]> StringsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private static async Task<bool> ScalarBoolAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    public enum WatchOutcome { Entry, Neutral, Exit, Inconclusive }

    private sealed class StubFeature125SourceIngestion : IIndustryRelativeValuationSourceIngestionService
    {
        public int InvocationCount { get; private set; }

        public Task<IndustryRelativeValuationSourceRunResult> RunAsync(
            IndustryRelativeValuationSourceRunRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(new IndustryRelativeValuationSourceRunResult(
                request.CorrelationId ?? "test", 2, 0, 6, 0, false));
        }
    }

    private sealed class StubNadpcoApiScheduledSyncService : INadpcoApiScheduledSyncService
    {
        public Task<NadpcoApiSyncResult> ExecuteAsync(
            bool fullReload,
            CancellationToken cancellationToken,
            int? fromShamsiYearOverride = null) =>
            Task.FromResult(new NadpcoApiSyncResult(
                fullReload, 2, 2, 0, [], 2, Now, Now, TimeSpan.Zero,
                NadpcoApiSyncRunMode.IncrementalSync));

        public Task<NadpcoApiSyncResult> ExecuteCompanyCatalogAsync(
            bool cleanSlate,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NadpcoApiSyncResult(
                false, 0, 0, 0, [], 0, Now, Now, TimeSpan.Zero,
                NadpcoApiSyncRunMode.CompanyCatalogRefresh));
    }

    private sealed class NoopNadpcoAlertSink : INadpcoScheduledSyncAlertSink
    {
        public Task<bool> EmitAsync(NadpcoScheduledSyncAlert alert, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
