using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature 156 against an isolated PostgreSQL database (FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING).</summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class SymbolSnapshot156PostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    private static readonly Guid EligibleMarket = Guid.Parse("037c69ad-f519-419f-ae62-59003b6b2428");
    private static readonly Guid OtherMarket = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Group = Guid.Parse("97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c");

    [SkippableFact]
    public async Task EligibleTickers_ResolveThroughTheView_AndTheCardRunsOnRealSql()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = database.CreateContext())
        {
            db.Markets.AddRange(
                new NormalizedMarketRow { Id = EligibleMarket, ProviderName = "NoavaranCurrentApi", ExternalId = "1", Name = "بورس", LastSynchronizedAt = now },
                new NormalizedMarketRow { Id = OtherMarket, ProviderName = "NoavaranCurrentApi", ExternalId = "2", Name = "سایر", LastSynchronizedAt = now });
            db.IndustryGroups.Add(new NormalizedIndustryGroupRow { Id = Group, ProviderName = "NoavaranCurrentApi", ExternalId = "g", Name = "مخابرات", LastSynchronizedAt = now });
            db.Companies.AddRange(
                Company("464", "اخابر", "مخابرات ایران", EligibleMarket, 0, now),
                Company("465", "حق‌تقدم", "حق تقدم", EligibleMarket, 1, now),
                Company("466", "خارج", "بازار دیگر", OtherMarket, 0, now));
            await db.SaveChangesAsync();
        }

        var apiConfigPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/FinancialCopilot.API"));
        var configuration = new ConfigurationBuilder().SetBasePath(apiConfigPath)
            .AddJsonFile("appsettings.json")
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:FinancialCopilot"] = database.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new BankingFinancialReportsConfiguration(Group));
        services.AddFinancialCopilotInfrastructure(configuration);
        services.RemoveAll<DbContextOptions<FinancialIngestionDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<FinancialIngestionDbContext>>();
        services.AddDbContext<FinancialIngestionDbContext>(options => options.UseNpgsql(database.ConnectionString));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<SymbolSnapshotOptions>().Enabled);
        Assert.Contains(
            scope.ServiceProvider.GetServices<IConversationalCapabilityExecutor>(),
            executor => executor.CapabilityCode == SymbolSnapshotCapability.Code);
        Assert.IsAssignableFrom<IConversationDialogueGate>(scope.ServiceProvider.GetRequiredService<IConversationDialogueGate>());

        var resolver = scope.ServiceProvider.GetRequiredService<IEligibleSymbolResolver>();
        Assert.Equal(
            new EligibleSymbol("اخابر", "مخابرات ایران", "بورس", "مخابرات"),
            await resolver.ResolveAsync("اخابر", CancellationToken.None));
        Assert.Null(await resolver.ResolveAsync(SymbolSnapshotIntentRules.CompactKey("حق‌تقدم"), CancellationToken.None));
        Assert.Null(await resolver.ResolveAsync("خارج", CancellationToken.None));

        // No price, monthly or quarterly data is seeded: every read must run and report NoData. A SQL or wiring
        // failure would show as Unavailable instead.
        var options = scope.ServiceProvider.GetRequiredService<SymbolSnapshotOptions>();
        options.SectionTimeoutMilliseconds = 30000;
        options.TurnBudgetMilliseconds = 60000;
        var result = await scope.ServiceProvider.GetRequiredService<ISymbolSnapshotUseCase>()
            .ExecuteAsync("اخابر", "correlation", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(SymbolSnapshotSectionStatus.NoData, result.Price.Status);
        Assert.Equal(SymbolSnapshotSectionStatus.NoData, result.MonthlySales.Status);
        Assert.Equal(SymbolSnapshotSectionStatus.NoData, result.QuarterlyReport.Status);
    }

    private static NormalizedCompanyRow Company(string externalId, string symbol, string name, Guid market, int precedencyRight, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProviderName = "NoavaranCurrentApi",
        ExternalCompanyId = externalId,
        Name = name,
        CompanySymbol = symbol,
        TseSymbol = symbol,
        Ticker = symbol,
        GroupId = Group,
        MarketId = market,
        PrecedencyRight = precedencyRight,
        LastSynchronizedAt = now
    };
}
