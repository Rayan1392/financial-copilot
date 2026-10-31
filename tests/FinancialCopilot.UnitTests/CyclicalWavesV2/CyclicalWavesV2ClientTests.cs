using System.Net;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class CyclicalWavesV2ClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parser_ReadsAllTwentyNineScreenerFieldsAndKeepsPeZeroAndNulls()
    {
        using var shlrd = CyclicalWavesV2Fixtures.Parse("screener-shlrd.json");
        var snapshot = CyclicalWavesV2PayloadParser.ParseScreener(shlrd.RootElement);

        Assert.Equal("شلرد", snapshot.Ticker);
        Assert.Equal("IRO7SHLP0001", snapshot.Enticker);
        Assert.Equal(29, snapshot.Values.Count);
        Assert.Equal(0m, snapshot.Values["pe"]);
        Assert.Equal(-36_117_908_000_000m, snapshot.Values["lastQuarterNetProfit"]);
        Assert.Equal(13_832_075_333_333.334m, snapshot.Values["lastYearAverage12MonthSale"]);
        Assert.Equal(new DateOnly(2026, 9, 22), snapshot.LastMonthSaleDate);
        Assert.Equal(new DateOnly(2026, 9, 22), snapshot.LastQuarterDate);

        using var withNull = CyclicalWavesV2Fixtures.Parse("screener-null-metric.json");
        var nullSnapshot = CyclicalWavesV2PayloadParser.ParseScreener(withNull.RootElement);
        Assert.Null(nullSnapshot.Values["lastQuarterNetProfit"]);
        Assert.Null(nullSnapshot.Values["ps"]);
    }

    [Fact]
    public void Parser_ReadsStatementsWithNullNegativeAndEpsValues()
    {
        using var folad = CyclicalWavesV2Fixtures.Parse("statements-folad.json");
        var set = CyclicalWavesV2PayloadParser.ParseIncomeStatements(folad.RootElement);

        Assert.Equal("فولاد", set.Ticker);
        Assert.Equal(52, set.Total);
        var latest = set.Statements[0];
        Assert.Equal(new DateOnly(2026, 9, 22), latest.PeriodEndingDate);
        Assert.Equal(6, latest.FiscalMonth);
        Assert.False(latest.Audited);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 11, 7, 13, TimeSpan.Zero), latest.PublishDate);
        Assert.Equal(-1_158_078_707_000_000m, latest.Lines.Single(line => line.Key == 48).Quarter);
        Assert.Equal(243.55m, latest.Lines.Single(line => line.Key == 85).Quarter);
        Assert.Null(latest.Lines.Single(line => line.Key == 86).TrailingYear);
        Assert.Equal("سود (زیان) خالص", latest.Lines.Single(line => line.Key == 66).NameFa);

        using var bank = CyclicalWavesV2Fixtures.Parse("statements-vbmellat.json");
        var bankSet = CyclicalWavesV2PayloadParser.ParseIncomeStatements(bank.RootElement);
        Assert.Null(bankSet.Statements[0].Lines.Single(line => line.Key == 111).Quarter);
        Assert.Contains(bankSet.Statements[0].Lines, line => line.Key == 113);
    }

    [Fact]
    public void Parser_ReadsLineDefinitionsAsBareOrWrappedArray()
    {
        using var lines = CyclicalWavesV2Fixtures.Parse("lines.json");
        Assert.Equal(82, CyclicalWavesV2PayloadParser.ParseLineDefinitions(lines.RootElement).Count);

        using var wrapped = JsonDocument.Parse("{\"data\":[{\"key\":44,\"name\":\"Sales\",\"nameFa\":\"فروش\"}]}");
        Assert.Equal(44, Assert.Single(CyclicalWavesV2PayloadParser.ParseLineDefinitions(wrapped.RootElement)).Key);
    }

    [Fact]
    public void Parser_ReadsAnalysisPageWithMetaAndEmptySummary()
    {
        using var blogs = CyclicalWavesV2Fixtures.Parse("blogs-page.json");
        var page = CyclicalWavesV2PayloadParser.ParseAnalysesPage(blogs.RootElement);

        Assert.Equal(3, page.LastPage);
        Assert.Equal(4, page.Items.Count);
        Assert.Contains(page.Items, item => string.IsNullOrEmpty(item.Summary));
        Assert.Contains(page.Items, item => item.Tags.Any(tag => tag.Name == "خودرو"));
        Assert.All(page.Items, item => Assert.Equal(TimeSpan.Zero, item.PublishedAt.Offset));
    }

    [Fact]
    public void Parser_RejectsAnUnexpectedShape() =>
        Assert.Throws<FinancialProviderException>(() =>
            CyclicalWavesV2PayloadParser.ParseTickers(JsonDocument.Parse("{\"tickers\":[]}").RootElement));

    [Fact]
    public void TokenService_ReadsJwtExpiryAndFallsBackWhenAbsent()
    {
        Assert.Equal(Now.AddMinutes(15), CyclicalWavesV2TokenService.ReadExpiry(CyclicalWavesV2Fixtures.Jwt(Now.AddMinutes(15))));
        Assert.Null(CyclicalWavesV2TokenService.ReadExpiry("opaque-token"));
    }

    [Fact]
    public async Task TokenService_LogsInOnceForTwentyConcurrentCallersWithClientIdHeader()
    {
        var time = new ManualTimeProvider(Now);
        var auth = AuthServer(time);
        var service = CreateTokenService(auth, time);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.GetAccessTokenAsync(CancellationToken.None)));

        Assert.Single(tokens.Distinct());
        var login = Assert.Single(auth.Requests);
        Assert.EndsWith("/auth/login", login.Uri);
        Assert.Equal("default", login.Headers["x-client-id"]);
        Assert.Contains("\"identifier\":\"user\"", login.Body);
    }

    [Fact]
    public async Task TokenService_RefreshesBeforeExpiryAndStoresTheRotatedRefreshToken()
    {
        var time = new ManualTimeProvider(Now);
        var auth = AuthServer(time);
        var service = CreateTokenService(auth, time);

        await service.GetAccessTokenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(14.5));
        await service.GetAccessTokenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(14.5));
        await service.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal(["/auth/login", "/auth/refresh", "/auth/refresh"], auth.Requests.Select(request => request.Uri[request.Uri.LastIndexOf("/auth", StringComparison.Ordinal)..]));
        Assert.Contains("\"refreshToken\":\"refresh-0\"", auth.Requests[1].Body);
        Assert.Contains("\"refreshToken\":\"refresh-1\"", auth.Requests[2].Body);
        Assert.All(auth.Requests, request => Assert.Equal("default", request.Headers["x-client-id"]));
    }

    [Fact]
    public async Task TokenService_RefreshRejectedLogsInExactlyOnce()
    {
        var time = new ManualTimeProvider(Now);
        var auth = AuthServer(time, refreshStatus: HttpStatusCode.Unauthorized);
        var service = CreateTokenService(auth, time);

        await service.GetAccessTokenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(15));
        await service.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal(["/auth/login", "/auth/refresh", "/auth/login"], auth.Requests.Select(request => request.Uri[request.Uri.LastIndexOf("/auth", StringComparison.Ordinal)..]));
    }

    [Fact]
    public async Task TokenService_FailedLoginRaisesUnauthorizedWithoutRetrying()
    {
        var time = new ManualTimeProvider(Now);
        var auth = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Unauthorized, "{\"statusCode\":401}"));
        var service = CreateTokenService(auth, time);

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() => service.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.Unauthorized, exception.Code);
        Assert.Equal(1, auth.Count);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthHandler_On401RenewsOnceAndReplaysOnce()
    {
        var time = new ManualTimeProvider(Now);
        var auth = AuthServer(time);
        var data = new StubHttpHandler((_, index) => index == 0
            ? StubHttpHandler.Json(HttpStatusCode.Unauthorized, CyclicalWavesV2Fixtures.Read("error-401.json"))
            : StubHttpHandler.Json(HttpStatusCode.OK, "[\"فولاد\"]"));
        var client = CreateDataClient(auth, data, time);

        var tickers = await client.GetTickersAsync(CancellationToken.None);

        Assert.Equal(["فولاد"], tickers);
        Assert.Equal(2, data.Count);
        Assert.Equal(2, auth.Count);
        Assert.NotEqual(data.Requests[0].Headers["Authorization"], data.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task AuthHandler_SecondUnauthorizedSurfaces()
    {
        var time = new ManualTimeProvider(Now);
        var data = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        var client = CreateDataClient(AuthServer(time), data, time);

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() => client.GetTickersAsync(CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.Unauthorized, exception.Code);
        Assert.Equal(2, data.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Resilience_DoesNotRetryClientErrors(HttpStatusCode status)
    {
        var time = new ManualTimeProvider(Now);
        var data = new StubHttpHandler((_, _) => StubHttpHandler.Json(status, CyclicalWavesV2Fixtures.Read("error-400-limit.json")));
        var client = CreateDataClient(AuthServer(time), data, time);

        await Assert.ThrowsAsync<FinancialProviderException>(() => client.GetTickersAsync(CancellationToken.None));

        Assert.Equal(1, data.Count);
    }

    [Fact]
    public async Task Resilience_RetriesServerErrorsUpToRetryCount()
    {
        var time = new ManualTimeProvider(Now);
        var data = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var client = CreateDataClient(AuthServer(time), data, time, retryCount: 2);

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() => client.GetTickersAsync(CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.RemoteUnavailable, exception.Code);
        Assert.Equal(3, data.Count);
    }

    [Fact]
    public async Task DataClient_EncodesPersianTickerAndMaps404()
    {
        var time = new ManualTimeProvider(Now);
        var data = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.NotFound, CyclicalWavesV2Fixtures.Read("error-404-screener.json")));
        var client = CreateDataClient(AuthServer(time), data, time);

        Assert.Null(await client.GetScreenerAsync("آ س پ", CancellationToken.None));
        Assert.Null(await client.GetIncomeStatementsAsync("فولاد", 12, CancellationToken.None));
        Assert.Contains("custom-filtering/ticker/%D8%A2%20%D8%B3%20%D9%BE", data.Requests[0].Uri);
        Assert.Contains("income-statements/ticker/%D9%81%D9%88%D9%84%D8%A7%D8%AF?limit=12", data.Requests[1].Uri);
    }

    [Fact]
    public async Task DataClient_InvalidLimitIsAnInvalidResponse()
    {
        var time = new ManualTimeProvider(Now);
        var data = new StubHttpHandler((_, _) => StubHttpHandler.Json(HttpStatusCode.BadRequest, CyclicalWavesV2Fixtures.Read("error-400-limit.json")));
        var client = CreateDataClient(AuthServer(time), data, time);

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() => client.GetIncomeStatementsAsync("فولاد", 0, CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, exception.Code);
    }

    [Fact]
    public void DataClient_AnalysisPathAlwaysFiltersAnalysisAndEncodesBrackets()
    {
        Assert.Equal("blogs?filter%5Btype%5D=analysis&page=2&paginate=100", CyclicalWavesV2DataClient.BuildAnalysesPath(2, 100, null));
        Assert.Equal(
            "blogs?filter%5Btype%5D=analysis&page=1&paginate=5&filter%5Bfrom_date%5D=2026-10-20",
            CyclicalWavesV2DataClient.BuildAnalysesPath(1, 5, new DateOnly(2026, 10, 20)));
    }

    [Theory]
    [InlineData(true, "", "", false)]
    [InlineData(false, "", "", true)]
    [InlineData(true, "user", "secret", true)]
    public void Options_RequireCredentialsOnlyWhileIngesting(bool ingestion, string identifier, string password, bool valid) =>
        Assert.Equal(valid, new CyclicalWavesV2Options { IngestionEnabled = ingestion, Identifier = identifier, Password = password }.IsValid(out _));

    [Theory]
    [InlineData("http://api.tahlilapp.com/api/", 12, 100, "30 23 * * *")]
    [InlineData("https://api.tahlilapp.com/api/", 0, 100, "30 23 * * *")]
    [InlineData("https://api.tahlilapp.com/api/", 41, 100, "30 23 * * *")]
    [InlineData("https://api.tahlilapp.com/api/", 12, 101, "30 23 * * *")]
    [InlineData("https://api.tahlilapp.com/api/", 12, 100, "not a cron")]
    public void Options_RejectOutOfRangeValues(string dataBase, int limit, int pageSize, string cron) =>
        Assert.False(new CyclicalWavesV2Options
        {
            DataBaseAddress = dataBase,
            IncomeStatementLimit = limit,
            AnalysisPageSize = pageSize,
            DailySyncCronUtc = cron
        }.IsValid(out _));

    [Fact]
    public void Options_DefaultsAreValidWithEmptyCredentials()
    {
        var options = new CyclicalWavesV2Options();

        Assert.True(options.IsValid(out _));
        Assert.Equal("30 23 * * *", options.DailySyncCronUtc);
        Assert.False(options.IngestionEnabled);
    }

    private static StubHttpHandler AuthServer(ManualTimeProvider time, HttpStatusCode refreshStatus = HttpStatusCode.Created)
    {
        var issued = 0;
        return new StubHttpHandler((request, _) =>
        {
            var isRefresh = request.RequestUri!.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal);
            if (isRefresh && refreshStatus != HttpStatusCode.Created)
                return StubHttpHandler.Json(refreshStatus, "{\"statusCode\":401}");
            var n = Interlocked.Increment(ref issued) - 1;
            var access = CyclicalWavesV2Fixtures.Jwt(time.GetUtcNow().AddMinutes(15)) + n;
            return StubHttpHandler.Json(HttpStatusCode.Created, $"{{\"access_token\":\"{access}\",\"refresh_token\":\"refresh-{n}\"}}");
        });
    }

    private static CyclicalWavesV2TokenService CreateTokenService(StubHttpHandler auth, TimeProvider time) =>
        new(new SingleClientFactory(auth, "https://auth.cycfx.com/api/"),
            Options.Create(new CyclicalWavesV2Options { Identifier = "user", Password = "secret" }),
            time,
            NullLogger<CyclicalWavesV2TokenService>.Instance);

    private static CyclicalWavesV2DataClient CreateDataClient(StubHttpHandler auth, StubHttpHandler data, ManualTimeProvider time, int retryCount = 0)
    {
        var options = Options.Create(new CyclicalWavesV2Options { Identifier = "user", Password = "secret", RetryCount = retryCount });
        var tokens = new CyclicalWavesV2TokenService(new SingleClientFactory(auth, "https://auth.cycfx.com/api/"), options, time,
            NullLogger<CyclicalWavesV2TokenService>.Instance);
        var resilience = new CyclicalWavesV2ResilienceHandler(options, TimeProvider.System) { InnerHandler = data };
        var authHandler = new CyclicalWavesV2AuthHandler(tokens) { InnerHandler = resilience };
        return new CyclicalWavesV2DataClient(new HttpClient(authHandler) { BaseAddress = new Uri("https://api.tahlilapp.com/api/") });
    }
}
