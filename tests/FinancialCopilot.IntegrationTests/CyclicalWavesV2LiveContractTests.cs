using Microsoft.Extensions.Configuration;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Xunit.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 148 read-only live contract check against the real CyclicalWaves v2 provider, through the production
/// client pipeline (token service, auth handler, resilience handler, DTO parser). Opt-in: set
/// CYCLICALWAVES_V2_RUN_LIVE=1. Credentials come from CYCLICALWAVES_V2_IDENTIFIER / CYCLICALWAVES_V2_PASSWORD or the
/// test project's user-secrets (CyclicalWavesV2:Identifier / Password); they are never printed or committed.
/// </summary>
public sealed class CyclicalWavesV2LiveContractTests
{
    [SkippableFact]
    public async Task LiveProvider_AnswersAllFiveDataEndpointsWithTheDocumentedShapes()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("CYCLICALWAVES_V2_RUN_LIVE") == "1", "Set CYCLICALWAVES_V2_RUN_LIVE=1 to call the live provider.");
        var (identifier, password) = ReadCredentials();
        Skip.If(string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password), "CyclicalWaves v2 credentials are not configured.");

        var options = Options.Create(new CyclicalWavesV2Options { Identifier = identifier!, Password = password! });
        using var authClient = new HttpClient(new CyclicalWavesV2ResilienceHandler(options, TimeProvider.System) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = new Uri(options.Value.AuthBaseAddress)
        };
        var tokens = new CyclicalWavesV2TokenService(new FixedFactory(authClient), options, TimeProvider.System,
            NullLogger<CyclicalWavesV2TokenService>.Instance);
        var pipeline = new CyclicalWavesV2AuthHandler(tokens)
        {
            InnerHandler = new CyclicalWavesV2ResilienceHandler(options, TimeProvider.System) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } }
        };
        var client = new CyclicalWavesV2DataClient(new HttpClient(pipeline) { BaseAddress = new Uri(options.Value.DataBaseAddress) });

        var tickers = await client.GetTickersAsync(CancellationToken.None);
        Assert.True(tickers.Count > 100);
        Assert.Contains("فولاد", tickers);

        var screener = await client.GetScreenerAsync("فولاد", CancellationToken.None);
        Assert.NotNull(screener);
        Assert.Equal("IRO1FOLD0001", screener!.Enticker);
        Assert.Equal(29, screener.Values.Count);
        Assert.NotNull(screener.LastQuarterDate);
        Assert.Null(await client.GetScreenerAsync("نمادناموجود۱۴۸", CancellationToken.None));

        var statements = await client.GetIncomeStatementsAsync("فولاد", 2, CancellationToken.None);
        Assert.NotNull(statements);
        Assert.Equal(2, statements!.Statements.Count);
        Assert.Contains(statements.Statements[0].Lines, line => line.Key == 44);
        Assert.Contains(statements.Statements[0].Lines, line => line.Key == 85);

        var lines = await client.GetIncomeStatementLinesAsync(CancellationToken.None);
        Assert.Equal(82, lines.Count);

        var page = await client.GetAnalysesPageAsync(1, 5, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)), CancellationToken.None);
        Assert.True(page.Items.Count > 0);
        Assert.True(page.LastPage >= 1);
        Assert.All(page.Items, item => Assert.True(item.Id > 0));
    }

    [SkippableFact]
    public async Task LiveProvider_AnswersAllFourGaugeAndEquilibriumEndpointsWithTheDocumentedShapes()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("CYCLICALWAVES_V2_RUN_LIVE") == "1", "Set CYCLICALWAVES_V2_RUN_LIVE=1 to call the live provider.");
        var (identifier, password) = ReadCredentials();
        Skip.If(string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password), "CyclicalWaves v2 credentials are not configured.");

        var options = Options.Create(new CyclicalWavesV2Options { Identifier = identifier!, Password = password! });
        using var authClient = new HttpClient(new CyclicalWavesV2ResilienceHandler(options, TimeProvider.System) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = new Uri(options.Value.AuthBaseAddress)
        };
        var tokens = new CyclicalWavesV2TokenService(new FixedFactory(authClient), options, TimeProvider.System,
            NullLogger<CyclicalWavesV2TokenService>.Instance);
        var pipeline = new CyclicalWavesV2AuthHandler(tokens)
        {
            InnerHandler = new CyclicalWavesV2ResilienceHandler(options, TimeProvider.System) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } }
        };
        var client = new CyclicalWavesV2GaugeDataClient(new HttpClient(pipeline) { BaseAddress = new Uri(options.Value.GaugeBaseUrl) });

        var gaugeCatalog = await client.GetGaugeCatalogAsync(CancellationToken.None);
        Assert.NotEmpty(gaugeCatalog);
        FinancialCopilot.Application.FinancialData.CyclicalWavesV2.CyclicalWavesV2GaugeDetails? gaugeDetails = null;
        foreach (var item in gaugeCatalog.Where(item => item.HasPe || item.HasPs).Take(5))
        {
            gaugeDetails = await client.GetGaugeDetailsAsync(item.Enticker ?? item.Ticker, CancellationToken.None);
            if (gaugeDetails is not null) break;
        }
        Assert.NotNull(gaugeDetails);
        Assert.NotNull(gaugeDetails!.Pe ?? gaugeDetails.Ps);
        foreach (var gauge in new[] { gaugeDetails.Pe, gaugeDetails.Ps }.Where(gauge => gauge is not null))
        {
            Assert.All(new[] { gauge!.A, gauge.B, gauge.C, gauge.D, gauge.E, gauge.F }, count => Assert.True(count >= 0));
            Assert.True(gauge.Status is null or >= 1 and <= 4);
        }

        var equilibriumCatalog = await client.GetEquilibriumCatalogAsync(CancellationToken.None);
        Assert.NotEmpty(equilibriumCatalog);
        FinancialCopilot.Application.FinancialData.CyclicalWavesV2.CyclicalWavesV2EquilibriumRange? range = null;
        foreach (var item in equilibriumCatalog.Take(5))
        {
            range = await client.GetEquilibriumRangeAsync(item.Enticker ?? item.Ticker, CancellationToken.None);
            if (range is not null) break;
        }
        Assert.NotNull(range);
        _ = range!.Min.ToString();
        _ = range.Avg.ToString();
        _ = range.Max.ToString();

        Assert.Null(await client.GetGaugeDetailsAsync("NO-SUCH-FEATURE148-GAUGE-20261108", CancellationToken.None));
        Assert.Null(await client.GetEquilibriumRangeAsync("NO-SUCH-FEATURE148-EQUILIBRIUM-20261108", CancellationToken.None));
    }

    // Credentials come only from untracked stores: environment variables or this project's user-secrets.
    internal static (string? Identifier, string? Password) ReadCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<CyclicalWavesV2LiveContractTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        return (Environment.GetEnvironmentVariable("CYCLICALWAVES_V2_IDENTIFIER") ?? configuration["CyclicalWavesV2:Identifier"],
                Environment.GetEnvironmentVariable("CYCLICALWAVES_V2_PASSWORD") ?? configuration["CyclicalWavesV2:Password"]);
    }

    private sealed class FixedFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

/// <summary>Opt-in live Full sync against a disposable PostgreSQL database only.</summary>
public sealed class CyclicalWavesV2LiveFullSyncTests(
    PostgreSqlIntegrationFixture fixture,
    ITestOutputHelper output) : IClassFixture<PostgreSqlIntegrationFixture>
{
    [SkippableFact]
    public async Task LiveFullSync_PersistsProviderDataAndIsIdempotent()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("CYCLICALWAVES_V2_RUN_LIVE") == "1", "Set CYCLICALWAVES_V2_RUN_LIVE=1 to call the live provider.");
        var (identifier, password) = CyclicalWavesV2LiveContractTests.ReadCredentials();
        Skip.If(string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password), "CyclicalWaves v2 credentials are not configured.");

        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var options = Options.Create(new CyclicalWavesV2Options { Identifier = identifier!, Password = password! });
        using var authClient = new HttpClient(new CyclicalWavesV2ResilienceHandler(options, TimeProvider.System) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = new Uri(options.Value.AuthBaseAddress)
        };
        var tokens = new CyclicalWavesV2TokenService(new FixedFactory(authClient), options, TimeProvider.System,
            NullLogger<CyclicalWavesV2TokenService>.Instance);
        using var pipeline = new CyclicalWavesV2AuthHandler(tokens)
        {
            InnerHandler = new CyclicalWavesV2ResilienceHandler(options, TimeProvider.System) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } }
        };
        using var dataHttp = new HttpClient(pipeline, disposeHandler: false) { BaseAddress = new Uri(options.Value.DataBaseAddress) };
        using var gaugeHttp = new HttpClient(pipeline, disposeHandler: false) { BaseAddress = new Uri(options.Value.GaugeBaseUrl) };
        var coordinator = new CyclicalWavesV2SyncCoordinator(
            new CyclicalWavesV2DataClient(dataHttp),
            new EfCoreCyclicalWavesV2Store(db, NullLogger<EfCoreCyclicalWavesV2Store>.Instance),
            new EfCoreCyclicalWavesV2AnalysisStore(db, new HtmlAgilityPackTextStripper()),
            new EfCoreCyclicalWavesV2SyncRunStore(db),
            new CyclicalWavesV2SyncSettings(options.Value.IncomeStatementLimit, options.Value.AnalysisPageSize,
                options.Value.AnalysisIncrementalOverlapDays, TimeSpan.FromMilliseconds(options.Value.RequestDelayMilliseconds),
                TimeSpan.FromMinutes(options.Value.LeaseMinutes)),
            TimeProvider.System,
            new CyclicalWavesV2GaugeDataClient(gaugeHttp),
            new EfCoreCyclicalWavesV2GaugeStore(db));

        var request = new CyclicalWavesV2SyncRequest(CyclicalWavesV2SyncScope.Full, null, null, CyclicalWavesV2SyncTrigger.Admin, "feature148-live-verification");
        var first = await coordinator.RunAsync(request, "feature148-live-verification-1", CancellationToken.None);
        Assert.NotEqual(CyclicalWavesV2SyncRunStatus.Failed, first.Status);
        Assert.True(first.Counts.ProcessedItems > 0);
        var firstCounts = await PersistedCountsAsync(db);
        Assert.True(firstCounts.Tickers > 0);
        Assert.True(firstCounts.Gauges > 0);
        Assert.True(firstCounts.Ranges > 0);

        var second = await coordinator.RunAsync(request, "feature148-live-verification-2", CancellationToken.None);
        Assert.NotEqual(CyclicalWavesV2SyncRunStatus.Failed, second.Status);
        Assert.Equal(firstCounts, await PersistedCountsAsync(db));

        var runs = await db.CyclicalWavesV2SyncRuns.AsNoTracking()
            .Where(run => run.RequestedBy == "feature148-live-verification")
            .OrderBy(run => run.StartedAt)
            .Select(run => new { run.Scope, run.Status, run.TotalItems, run.ProcessedItems, run.UpdatedItems, run.UnchangedItems, run.FailedItems, run.ErrorSummary })
            .ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal(CyclicalWavesV2SyncScope.Full.ToString(), run.Scope));
        output.WriteLine($"Full runs: {string.Join("; ", runs.Select(run => $"{run.Status} total={run.TotalItems} processed={run.ProcessedItems} updated={run.UpdatedItems} unchanged={run.UnchangedItems} failed={run.FailedItems}"))}");
        output.WriteLine($"Persisted records after both runs: tickers={firstCounts.Tickers}, gaugeAvailability={firstCounts.Availability}, gaugeDetails={firstCounts.Gauges}, equilibriumRanges={firstCounts.Ranges}");
        output.WriteLine($"Run summaries contain unmatched ticker reports: {runs.Any(run => run.ErrorSummary?.Contains("UnmatchedOrAmbiguous", StringComparison.Ordinal) == true)}");
    }

    private static async Task<(int Tickers, int Availability, int Gauges, int Ranges)> PersistedCountsAsync(FinancialIngestionDbContext db) =>
        (await db.CyclicalWavesV2Tickers.CountAsync(), await db.CyclicalWavesV2GaugeAvailability.CountAsync(),
            await db.CyclicalWavesV2GaugeDetails.CountAsync(), await db.CyclicalWavesV2EquilibriumRanges.CountAsync());

    private sealed class FixedFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
