using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Config;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class IndustryRelativeValuationSnapshotConsumptionPostgreSqlTests(
    PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    private const string CanonicalProvider = ProviderSources.NoavaranCurrentApiName;
    private static readonly Guid EligibleMarketId =
        Guid.Parse("037c69ad-f519-419f-ae62-59003b6b2428");

    [SkippableFact]
    public async Task V2Comparison_UsesIntradayThenDaily_PricesOnlyEligiblePeers_AndHidesOlderSnapshots()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var (_, companies) = await SeedCatalogAsync(db, 4);
        var roster = await db.Companies.Where(row => companies.Contains(row.Id)).OrderBy(row => row.ExternalCompanyId).ToListAsync();
        var groupId = roster[0].GroupId!.Value;
        roster[0].Ticker = roster[0].TseSymbol = "شگل";
        var group = await db.IndustryGroups.SingleAsync(row => row.Id == groupId);
        group.Name = "تولید محصولات آرایشی و بهداشتی";
        var otherGroup = new NormalizedIndustryGroupRow
        {
            Id = Guid.NewGuid(), ProviderName = CanonicalProvider, ExternalId = "other", Name = "Other Group",
            LastSynchronizedAt = Now
        };
        db.IndustryGroups.Add(otherGroup);
        roster[2].GroupId = otherGroup.Id;
        roster[3].SymbolIsin = "NO_MATCH";
        var today = DateOnly.FromDateTime(Now.ToOffset(TimeSpan.FromHours(3.5)).DateTime);
        var time = new ValuationTime(Now);
        foreach (var company in roster.Take(3))
        {
            var tickerId = Guid.NewGuid();
            var ticker = company.Ticker ?? $"TEST{company.ExternalCompanyId}";
            var instrument = new TradingInstrumentRow
            {
                Id = Guid.NewGuid(), ProviderName = "TestMarket", ExternalInstrumentId = Guid.NewGuid(),
                InstrumentCode = 100 + int.Parse(company.ExternalCompanyId), InstrumentIsin = company.SymbolIsin!,
                Symbol = ticker, Name = ticker, IsActive = true, NormalizedCompanyId = company.Id
            };
            db.TradingInstruments.Add(instrument);
            db.CyclicalWavesV2Tickers.Add(new()
            {
                Id = tickerId, Ticker = ticker, NormalizedTicker = ticker,
                Enticker = company.SymbolIsin!.ToLowerInvariant(), IsActive = true, CreatedAt = Now, UpdatedAt = Now, LastSeenInCatalogAt = Now
            });
            db.CyclicalWavesV2GaugeAvailability.Add(new() { TickerId = tickerId, HasPe = true, HasPs = true, LastFetchedAtUtc = Now });
            foreach (var gaugeType in new[] { "PE", "PS" })
                db.CyclicalWavesV2GaugeDetails.Add(new()
                {
                    TickerId = tickerId, GaugeType = gaugeType, Close = ExactDecimal.Parse("4.0000000000000001"), Avg = ExactDecimal.Parse("2"),
                    Min = ExactDecimal.Parse("1"), Max = ExactDecimal.Parse("6"), Start = ExactDecimal.Parse("1"),
                    End = ExactDecimal.Parse("6"), FetchedAtUtc = Now
                });
            db.CyclicalWavesV2EquilibriumRanges.Add(new()
            {
                TickerId = tickerId, HasRange = true, Avg = ExactDecimal.Parse("1000"),
                FetchedAtUtc = Now, DetailsFetchedAtUtc = Now
            });
            db.IntradayTradeSnapshots.Add(new()
            {
                Id = Guid.NewGuid(), ProviderName = "TestMarket", ExternalSnapshotId = Guid.NewGuid(), TradingInstrumentId = instrument.Id,
                TradingDate = today, TradingTime = new(13, 29), ReceivedAt = Now.AddMinutes(-1), LastTradedPrice = 1200, ClosingPrice = 9000
            });
            db.IntradayTradeSnapshots.Add(new()
            {
                Id = Guid.NewGuid(), ProviderName = "TestMarket", ExternalSnapshotId = Guid.NewGuid(), TradingInstrumentId = instrument.Id,
                TradingDate = today, TradingTime = new(13, 28), ReceivedAt = Now, LastTradedPrice = 9900, ClosingPrice = 9000
            });
            db.LatestMarketQuotes.Add(new()
            {
                Id = Guid.NewGuid(), ProviderName = "TestMarket", TradingInstrumentId = instrument.Id,
                TradingDate = today, SourceKind = "Intraday", LatestPrice = 5000, AsOf = Now
            });
            db.DailyInstrumentTrades.Add(new()
            {
                Id = Guid.NewGuid(), ProviderName = "TestMarket", ExternalTradeId = Guid.NewGuid(), TradingInstrumentId = instrument.Id,
                TradingDate = today.AddDays(-1), SourceInsertedAt = Now.AddDays(-1), LastTradedPrice = 800, ClosingPrice = 7000
            });
        }
        await db.SaveChangesAsync();

        var market = new PersistedMarketDataProvider(db, time);
        var reader = new CyclicalWavesV2RelativeValuationInputReader(db, market,
            Options.Create(new MarketViewOptions()), NullLogger<CyclicalWavesV2RelativeValuationInputReader>.Instance);
        var builder = new IndustryRelativeValuationCalculationInputBuilder(db, new CyclicalWavesMetricSnapshotReader(db), reader);
        var writer = new IndustryRelativeValuationCalculationSnapshotWriter(db);
        var repository = new IndustryRelativeValuationReadRepository(db,
            options: Options.Create(new IndustryRelativeValuationOptions { CyclicalWavesV2Enabled = true }), timeProvider: time);
        var request = new IndustryRelativeValuationReadRequest(groupId, [companies[0]], ChatCapabilityAvailability.PeerComparisonCapabilityCode);

        var legacy = new IndustryRelativeValuationCalculationRow
        {
            Id = Guid.NewGuid(), GroupId = groupId, IndustryId = roster[0].IndustryId!.Value, CalculationDate = today,
            CalculationVersion = 1, Status = "Published", IsSelectedCurrent = true, IsLatestEvaluation = true
        };
        db.IndustryRelativeValuationCalculations.Add(legacy);
        await db.SaveChangesAsync();
        Assert.Null(await repository.ReadAsync(request));

        var live = await BuildGroupAsync();
        Assert.Equal(2, live.Members.Count);
        Assert.DoesNotContain(live.Members, member => member.CompanyId == companies[2] || member.CompanyId == companies[3]);
        var watchQuote = Assert.Single((await market.GetLatestQuotesAsync([new("شگل")], default)).Observations);
        Assert.Equal(watchQuote.LatestPrice, live.SourceBarrier.Selections.Single(selection =>
            selection.CompanyId == companies[0] && selection.Metric == RelativeValuationMetric.Equilibrium).Fact.CurrentValue);
        Assert.Equal("Published", (await writer.WriteAsync(today, live, time.Now, default)).Status);
        Assert.True((await writer.WriteAsync(today, live, time.Now, default)).NoOp);
        Assert.True((await db.IndustryRelativeValuationCalculations.AsNoTracking().SingleAsync(row => row.Id == legacy.Id)).IsSelectedCurrent);
        var liveRead = Assert.IsType<IndustryRelativeValuationReadModel>(await repository.ReadAsync(request));
        Assert.All(liveRead.Members, row => Assert.Equal(120m, row.Equilibrium.Percent));

        time.Now = Now.AddMinutes(20);
        Assert.Null(await repository.ReadAsync(request));
        var daily = await BuildGroupAsync();
        Assert.Equal("Published", (await writer.WriteAsync(today, daily, time.Now, default)).Status);
        var dailyRead = Assert.IsType<IndustryRelativeValuationReadModel>(await repository.ReadAsync(request));
        Assert.All(dailyRead.Members, row =>
        {
            Assert.Equal(80m, row.Equilibrium.Percent);
            Assert.Equal("LatestDailyFallback", row.MarketPriceSource);
            Assert.Equal(today.AddDays(-1), row.MarketTradingDate);
            Assert.Equal(200m, row.PE.Percent);
            Assert.Equal(200m, row.PS.Percent);
        });
        Assert.Contains("روزانه", IndustryRelativeValuationPresentation.Explain(dailyRead));
        foreach (var useNativeWorkflow in new[] { true, false })
        {
            await using var factory = new RelativeValuationApiFactory(database.ConnectionString, time, useNativeWorkflow);
            factory.EnsureBillingSeeded();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
            var message = useNativeWorkflow ? "شگل نسبت به شرکت‌های هم‌گروه چطوره؟" : "نماد شگل را با صنعت خودش مقایسه کن";
            using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message });
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            using var answer = JsonDocument.Parse(body);
            Assert.Equal(ChatCapabilityAvailability.PeerComparisonCapabilityCode,
                answer.RootElement.GetProperty("semanticCapabilityCode").GetString());
            var text = answer.RootElement.GetProperty("textAnswer").GetString();
            Assert.Contains(group.Name, text);
            Assert.Contains("میانگین گروه", text);
            Assert.Contains("۸۰٪", text);
            Assert.Contains("روزانه", text);
            Assert.True(factory.ProposalProvider.Calls == 0,
                $"V2 {(useNativeWorkflow ? "native" : "fallback")} requested model interpretation for '{message}'.");
        }

        await db.DailyInstrumentTrades.ExecuteDeleteAsync();
        var missing = await BuildGroupAsync();
        Assert.Equal("Inconclusive", (await writer.WriteAsync(today, missing, time.Now, default)).Status);
        Assert.Null(await repository.ReadAsync(request));
        Assert.Empty(await db.IndustryWatchStates.AsNoTracking().ToListAsync());
        Assert.Empty(await db.IndustryRelativeValuationOutbox.AsNoTracking().ToListAsync());

        async Task<IndustryRelativeValuationCalculationInput> BuildGroupAsync() =>
            (await builder.BuildFromCyclicalWavesV2Async(CanonicalProvider, time.Now, TimeSpan.FromHours(26), default))
            .Single(input => input.GroupId == groupId);
    }

    private sealed class ValuationTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RelativeValuationApiFactory(string connectionString, TimeProvider time, bool useNativeWorkflow)
        : AiFacadeApiFactory
    {
        protected override bool ForceV1Orchestration => false;
        public CountingProposalProvider ProposalProvider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("AiOrchestration:Mode", "MicrosoftAgentFrameworkV2");
            builder.UseSetting("FinancialData:ChatProvider", "NoavaranAmin");
            builder.UseSetting("IndustryRelativeValuation:CyclicalWavesV2Enabled", "true");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
                    ["FinancialData:ChatProvider"] = "NoavaranAmin",
                    ["IndustryRelativeValuation:CyclicalWavesV2Enabled"] = "true",
                    ["SemanticRouting:Capabilities:symbol_vs_industry_relative_valuation"] = "SemanticPrimary"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IQueryInterpretationProposalProvider>();
                services.AddSingleton<IQueryInterpretationProposalProvider>(ProposalProvider);
                services.RemoveAll<FinancialIngestionDbContext>();
                services.RemoveAll<DbContextOptions<FinancialIngestionDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<FinancialIngestionDbContext>>();
                services.AddDbContext<FinancialIngestionDbContext>(options => options.UseNpgsql(connectionString));
                // Keep authentication and billing on their real test clock; only the persisted answer is time-fixed.
                services.RemoveAll<IIndustryRelativeValuationReadRepository>();
                services.AddScoped<IIndustryRelativeValuationReadRepository>(provider => new IndustryRelativeValuationReadRepository(
                    provider.GetRequiredService<FinancialIngestionDbContext>(),
                    options: Options.Create(new IndustryRelativeValuationOptions { CyclicalWavesV2Enabled = true }), timeProvider: time));
                if (!useNativeWorkflow)
                    services.AddSingleton<IOptions<AiOrchestrationOptions>>(Options.Create(new AiOrchestrationOptions { Mode = AiOrchestrationMode.V1 }));
            });
        }
    }

    private sealed class CountingProposalProvider : IQueryInterpretationProposalProvider
    {
        public int Calls { get; private set; }

        public Task<QueryInterpretationProposal?> ProposeAsync(string originalText, Guid tenantId, string correlationId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<QueryInterpretationProposal?>(null);
        }
    }

    [SkippableFact]
    public async Task Reader_SelectsLatestSnapshot_ThenOrdersMatchingSuccessfulChecksDeterministically()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var (_, companies) = await SeedCatalogAsync(db, 2);
        var companyId = companies[0];
        var otherCompanyId = companies[1];

        var oldPs = AddSnapshot(db, companyId, CyclicalWavesMetricType.PS,
            "{\"close\":99,\"avg\":1}", Hash('a'), Now.AddHours(-3), Now.AddHours(-3),
            Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var latestPs = AddSnapshot(db, companyId, CyclicalWavesMetricType.PS,
            "{\"close\":4,\"avg\":2}", Hash('b'), Now.AddHours(-2), Now.AddHours(-2),
            Guid.Parse("00000000-0000-0000-0000-000000000002"), oldPs.Id);
        AddCheck(db, oldPs, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddHours(2), Now.AddHours(2), Guid.NewGuid());
        AddCheck(db, latestPs, CyclicalWavesAcquisitionResult.Changed,
            Now.AddHours(-2), Now.AddHours(-2), Guid.NewGuid());
        var psWinner = Guid.Parse("00000000-0000-0000-0000-000000000010");
        AddCheck(db, latestPs, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddHours(-1), Now.AddHours(-3), psWinner);

        var pe = AddSnapshot(db, companyId, CyclicalWavesMetricType.PE,
            "{\"close\":6,\"avg\":3}", Hash('c'), Now.AddHours(-2), Now.AddHours(-2));
        AddCheck(db, pe, CyclicalWavesAcquisitionResult.Changed,
            Now.AddHours(-1), Now.AddMinutes(-30), Guid.NewGuid());
        var peWinner = Guid.Parse("00000000-0000-0000-0000-000000000020");
        AddCheck(db, pe, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddHours(-1), Now.AddMinutes(-20), peWinner);

        var equilibrium = AddSnapshot(db, companyId, CyclicalWavesMetricType.Equilibrium,
            "{\"close\":100,\"balance\":125}", Hash('d'), Now.AddHours(-2), Now.AddHours(-2));
        AddCheck(db, equilibrium, CyclicalWavesAcquisitionResult.Changed,
            Now.AddHours(-1), Now.AddMinutes(-20),
            Guid.Parse("00000000-0000-0000-0000-000000000030"));
        var equilibriumWinner = Guid.Parse("00000000-0000-0000-0000-000000000031");
        AddCheck(db, equilibrium, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddHours(-1), Now.AddMinutes(-20), equilibriumWinner);

        AddCheck(db, latestPs, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddMinutes(-1), Now.AddMinutes(-1), Guid.NewGuid(), responseHash: Hash('e'));
        AddCheck(db, latestPs, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddMinutes(-1), Now.AddMinutes(-1), Guid.NewGuid(), companyId: otherCompanyId);
        AddCheck(db, latestPs, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddMinutes(-1), Now.AddMinutes(-1), Guid.NewGuid(), metricType: CyclicalWavesMetricType.PE);
        await db.SaveChangesAsync();

        var snapshots = await new CyclicalWavesMetricSnapshotReader(db)
            .ReadLatestAsync([companyId], CancellationToken.None);

        Assert.Equal(3, snapshots.Count);
        Assert.Equal(latestPs.Id, snapshots.Single(x => x.MetricType == CyclicalWavesMetricType.PS).SnapshotId);
        Assert.Equal(psWinner, snapshots.Single(x => x.MetricType == CyclicalWavesMetricType.PS).AcquisitionCheckId);
        Assert.Equal(peWinner, snapshots.Single(x => x.MetricType == CyclicalWavesMetricType.PE).AcquisitionCheckId);
        Assert.Equal(equilibriumWinner,
            snapshots.Single(x => x.MetricType == CyclicalWavesMetricType.Equilibrium).AcquisitionCheckId);
    }

    [SkippableFact]
    public async Task NoChangeRefreshesFreshness_AndLaterFailureDoesNotInvalidateUntilExpiry()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var (_, companies) = await SeedCatalogAsync(db, 1);
        var companyId = companies.Single();
        var snapshot = AddSnapshot(db, companyId, CyclicalWavesMetricType.PS,
            "{\"close\":4,\"avg\":2}", Hash('a'), Now.AddHours(-10), Now.AddHours(-10));
        AddCheck(db, snapshot, CyclicalWavesAcquisitionResult.Changed,
            Now.AddHours(-10), Now.AddHours(-10), Guid.NewGuid());
        var noChangeId = Guid.NewGuid();
        AddCheck(db, snapshot, CyclicalWavesAcquisitionResult.NoChange,
            Now.AddHours(-1), Now.AddHours(-1), noChangeId);
        AddFailedCheck(db, companyId, CyclicalWavesMetricType.PS, Now);
        await db.SaveChangesAsync();

        var reader = new CyclicalWavesMetricSnapshotReader(db);
        var selected = Assert.Single(await reader.ReadLatestAsync([companyId], CancellationToken.None));
        Assert.Equal(snapshot.Id, selected.SnapshotId);
        Assert.Equal(noChangeId, selected.AcquisitionCheckId);
        Assert.Equal(Now.AddHours(-1), selected.CompletedAtUtc);
        Assert.Equal(1, await db.CyclicalWavesMetricSnapshots.CountAsync());

        var builder = new IndustryRelativeValuationCalculationInputBuilder(db, reader);
        var fresh = Assert.Single(await builder.BuildAsync(
            CanonicalProvider, Now, TimeSpan.FromHours(2), CancellationToken.None));
        Assert.Contains(fresh.SourceBarrier.Selections,
            selection => selection.CompanyId == companyId && selection.Metric == RelativeValuationMetric.Ps);

        Assert.Empty(await builder.BuildAsync(
            CanonicalProvider, Now.AddHours(2), TimeSpan.FromHours(2), CancellationToken.None));
    }

    [SkippableFact]
    public async Task MalformedAndMissingSnapshots_ProduceMissingInputsWithoutLegacyFallback()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var (_, companies) = await SeedCatalogAsync(db, 1);
        var companyId = companies.Single();
        var malformed = AddSnapshot(db, companyId, CyclicalWavesMetricType.PS,
            "{not-json", Hash('a'), Now.AddMinutes(-1), Now.AddMinutes(-1));
        AddCheck(db, malformed, CyclicalWavesAcquisitionResult.Changed,
            Now.AddMinutes(-1), Now.AddMinutes(-1), Guid.NewGuid());
        AddLegacyInputs(db, companyId);
        await db.SaveChangesAsync();

        var reader = new CyclicalWavesMetricSnapshotReader(db);
        var inputs = await new IndustryRelativeValuationCalculationInputBuilder(db, reader)
            .BuildAsync(CanonicalProvider, Now, TimeSpan.FromHours(26), CancellationToken.None);

        Assert.Empty(inputs);
        Assert.Single(await db.IndustryRelativeValuationSourceFacts.ToArrayAsync());
        Assert.Single(await db.CompanyPsGaugeSnapshots.ToArrayAsync());
        Assert.Equal(
            [typeof(FinancialIngestionDbContext)],
            typeof(CyclicalWavesMetricSnapshotReader).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    [SkippableFact]
    public async Task PersistedSnapshots_AssembleExactOperands_AndPublishWithoutLegacySources()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var (_, companies) = await SeedCatalogAsync(db, 2);
        foreach (var companyId in companies)
        {
            var offset = companyId == companies[0] ? 0m : 1m;
            AddAcceptedSnapshot(db, companyId, CyclicalWavesMetricType.PS,
                $"{{\"close\":{4m + offset},\"avg\":{2m + offset}}}", Hash('a'));
            AddAcceptedSnapshot(db, companyId, CyclicalWavesMetricType.PE,
                $"{{\"close\":{6m + offset},\"avg\":{3m + offset}}}", Hash('b'));
            AddAcceptedSnapshot(db, companyId, CyclicalWavesMetricType.Equilibrium,
                $"{{\"close\":{100m + offset},\"balance\":{125m + offset}}}", Hash('c'));
        }
        AddLegacyInputs(db, companies[0]);
        await db.SaveChangesAsync();

        var input = Assert.Single(await new IndustryRelativeValuationCalculationInputBuilder(
                db, new CyclicalWavesMetricSnapshotReader(db))
            .BuildAsync(CanonicalProvider, Now, TimeSpan.FromHours(26), CancellationToken.None));
        var selected = input.SourceBarrier.Selections
            .Where(x => x.CompanyId == companies[0])
            .ToDictionary(x => x.Metric, x => x.Fact);
        Assert.Equal((4m, 2m), (selected[RelativeValuationMetric.Ps].CurrentValue,
            selected[RelativeValuationMetric.Ps].ReferenceValue));
        Assert.Equal((6m, 3m), (selected[RelativeValuationMetric.Pe].CurrentValue,
            selected[RelativeValuationMetric.Pe].ReferenceValue));
        Assert.Equal((100m, 125m), (selected[RelativeValuationMetric.Equilibrium].CurrentValue,
            selected[RelativeValuationMetric.Equilibrium].ReferenceValue));

        var write = await new IndustryRelativeValuationCalculationSnapshotWriter(db)
            .WriteAsync(DateOnly.FromDateTime(Now.UtcDateTime), input, Now, CancellationToken.None);
        Assert.Equal("Published", write.Status);
        var published = await db.CompanyIndustryRelativeValuations
            .SingleAsync(x => x.CalculationId == write.CalculationId && x.CompanyId == companies[0]);
        Assert.Equal(4m, published.CurrentPS);
        Assert.Equal(2m, published.HistoricalAveragePS);
        Assert.Equal(6m, published.CurrentPE);
        Assert.Equal(3m, published.HistoricalAveragePE);
        Assert.Equal(100m, published.CurrentMarketPrice);
        Assert.Equal(125m, published.EquilibriumPrice);
        Assert.Single(await db.IndustryRelativeValuationSourceFacts.ToArrayAsync());
    }

    private static async Task<(Guid IndustryId, Guid[] Companies)> SeedCatalogAsync(
        FinancialIngestionDbContext db,
        int companyCount)
    {
        var industryId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        db.Industries.Add(new NormalizedIndustryRow
        {
            Id = industryId,
            ProviderName = CanonicalProvider,
            ExternalId = "10",
            Name = "Test Industry",
            LastSynchronizedAt = Now
        });
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = groupId,
            ProviderName = CanonicalProvider,
            ExternalId = "group-10",
            Name = "Test Group",
            LastSynchronizedAt = Now
        });
        db.Markets.Add(new NormalizedMarketRow
        {
            Id = EligibleMarketId,
            ProviderName = CanonicalProvider,
            ExternalId = "1",
            Name = "Eligible Market",
            LastSynchronizedAt = Now
        });
        var companies = Enumerable.Range(1, companyCount).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < companies.Length; index++)
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companies[index],
                ProviderName = CanonicalProvider,
                ExternalCompanyId = (index + 1).ToString(),
                Name = $"Company {index + 1}",
                IndustryId = industryId,
                GroupId = groupId,
                MarketId = EligibleMarketId,
                PrecedencyRight = 0,
                SymbolIsin = $"IRTEST{index + 1:000}",
                LastSynchronizedAt = Now
            });
        await db.SaveChangesAsync();
        return (industryId, companies);
    }

    private static CyclicalWavesMetricSnapshotRow AddSnapshot(
        FinancialIngestionDbContext db,
        Guid companyId,
        CyclicalWavesMetricType metricType,
        string json,
        string hash,
        DateTimeOffset acquisitionDateUtc,
        DateTimeOffset createdAtUtc,
        Guid? id = null,
        Guid? previousSnapshotId = null)
    {
        var snapshot = new CyclicalWavesMetricSnapshotRow
        {
            Id = id ?? Guid.NewGuid(),
            CompanyId = companyId,
            SymbolIsin = "IRTEST001",
            ProviderName = "CyclicalWaves",
            MetricType = metricType.ToString(),
            RawResponseJson = json,
            ResponseHash = hash,
            AcquisitionDateUtc = acquisitionDateUtc,
            SourceEndpoint = metricType.ToString(),
            PreviousSnapshotId = previousSnapshotId,
            CreatedAtUtc = createdAtUtc
        };
        db.CyclicalWavesMetricSnapshots.Add(snapshot);
        return snapshot;
    }

    private static void AddAcceptedSnapshot(
        FinancialIngestionDbContext db,
        Guid companyId,
        CyclicalWavesMetricType metricType,
        string json,
        string hash)
    {
        var snapshot = AddSnapshot(db, companyId, metricType, json, hash,
            Now.AddMinutes(-1), Now.AddMinutes(-1));
        AddCheck(db, snapshot, CyclicalWavesAcquisitionResult.Changed,
            Now.AddMinutes(-1), Now.AddMinutes(-1), Guid.NewGuid());
    }

    private static void AddCheck(
        FinancialIngestionDbContext db,
        CyclicalWavesMetricSnapshotRow snapshot,
        CyclicalWavesAcquisitionResult result,
        DateTimeOffset completedAtUtc,
        DateTimeOffset createdAtUtc,
        Guid id,
        string? responseHash = null,
        Guid? companyId = null,
        CyclicalWavesMetricType? metricType = null)
    {
        db.CyclicalWavesAcquisitionChecks.Add(new CyclicalWavesAcquisitionCheckRow
        {
            Id = id,
            CycleDateUtc = DateOnly.FromDateTime(completedAtUtc.UtcDateTime),
            CompanyId = companyId ?? snapshot.CompanyId,
            SymbolIsin = snapshot.SymbolIsin,
            ProviderName = snapshot.ProviderName,
            MetricType = (metricType?.ToString()) ?? snapshot.MetricType,
            CheckedAtUtc = completedAtUtc,
            RequestedAtUtc = completedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ResponseHash = responseHash ?? snapshot.ResponseHash,
            Result = result.ToString(),
            SnapshotId = snapshot.Id,
            SourceEndpoint = snapshot.SourceEndpoint,
            HttpStatusCode = 200,
            AttemptCount = 1,
            CreatedAtUtc = createdAtUtc
        });
    }

    private static void AddFailedCheck(
        FinancialIngestionDbContext db,
        Guid companyId,
        CyclicalWavesMetricType metricType,
        DateTimeOffset completedAtUtc)
    {
        db.CyclicalWavesAcquisitionChecks.Add(new CyclicalWavesAcquisitionCheckRow
        {
            Id = Guid.NewGuid(),
            CycleDateUtc = DateOnly.FromDateTime(completedAtUtc.UtcDateTime),
            CompanyId = companyId,
            SymbolIsin = "IRTEST001",
            ProviderName = "CyclicalWaves",
            MetricType = metricType.ToString(),
            CheckedAtUtc = completedAtUtc,
            RequestedAtUtc = completedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Result = CyclicalWavesAcquisitionResult.Failed.ToString(),
            SourceEndpoint = metricType.ToString(),
            AttemptCount = 1,
            FailureCode = "Timeout",
            CreatedAtUtc = completedAtUtc
        });
    }

    private static void AddLegacyInputs(FinancialIngestionDbContext db, Guid companyId)
    {
        db.IndustryRelativeValuationSourceFacts.Add(new IndustryRelativeValuationSourceFactRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = "CyclicalWaves",
            SourceKind = "PSGauge",
            SourceObservationId = "legacy-ps",
            CurrentValue = 400m,
            ReferenceValue = 200m,
            FetchedAtUtc = Now,
            PersistedAtUtc = Now,
            SourceEndpoint = "legacy/ps",
            SourceWatermark = "legacy-watermark",
            PayloadHash = Hash('f'),
            Readiness = "Ready",
            QualityCode = "Valid",
            IdentityEvidence = "legacy-identity",
            RawPayload = "{}"
        });
        db.CompanyPsGaugeSnapshots.Add(new CompanyPsGaugeSnapshotRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = "CyclicalWaves",
            SourceCompanyIsin = "IRTEST001",
            ObservationDate = DateOnly.FromDateTime(Now.UtcDateTime),
            GaugeClose = 500m,
            GaugeAverage = 250m,
            BoundaryAverage = 999m,
            GaugeFetchedAtUtc = Now,
            CurrentValuesFetchedAtUtc = Now,
            LastSyncedAtUtc = Now,
            FirstSeenAtUtc = Now
        });
    }

    private static string Hash(char value) => new(value, 64);
}
