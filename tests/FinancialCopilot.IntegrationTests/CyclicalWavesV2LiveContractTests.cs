using Microsoft.Extensions.Configuration;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    // Credentials come only from untracked stores: environment variables or this project's user-secrets.
    private static (string? Identifier, string? Password) ReadCredentials()
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
