using System.Net;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesAuthHandlerTests
{
    private static readonly CyclicalWavesProviderOptions Options = new()
    {
        UserName = "testuser",
        Password = "testpass",
        BaseAddress = "https://api.example.com/api/"
    };

    private static HttpResponseMessage OkLoginResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { access_token = "jwt-token-123", expires_in = 864000 }),
                Encoding.UTF8,
                "application/json")
        };

    private static HttpResponseMessage OkDataResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task FirstRequest_CallsLoginEndpointAndAddsAuthorizationHeader()
    {
        var loginCalled = false;
        var capturedAuthHeader = string.Empty;

        var inner = new FakeHandler(request =>
        {
            if (request.RequestUri!.OriginalString.Contains("auth/login"))
            {
                loginCalled = true;
                return Task.FromResult(OkLoginResponse());
            }

            capturedAuthHeader = request.Headers.Authorization?.Parameter ?? string.Empty;
            return Task.FromResult(OkDataResponse());
        });

        var (client, _) = BuildClient(inner);
        await client.GetAsync("custom-filtering/tickers");

        Assert.True(loginCalled);
        Assert.Equal("jwt-token-123", capturedAuthHeader);
    }

    [Fact]
    public async Task SecondRequest_ReusesCachedTokenWithoutRelogin()
    {
        var loginCount = 0;

        var inner = new FakeHandler(request =>
        {
            if (request.RequestUri!.OriginalString.Contains("auth/login"))
            {
                loginCount++;
                return Task.FromResult(OkLoginResponse());
            }

            return Task.FromResult(OkDataResponse());
        });

        var (client, _) = BuildClient(inner);
        await client.GetAsync("custom-filtering/tickers");
        await client.GetAsync("custom-filtering/tickers");

        Assert.Equal(1, loginCount);
    }

    [Fact]
    public async Task ValidRedisCachedToken_DoesNotCallLogin()
    {
        var loginCount = 0;
        var tokenCache = CreateTokenCache();
        var now = DateTimeOffset.UtcNow;
        await tokenCache.SetTokenAsync(
            new CyclicalWavesCachedToken(
                "already-cached-token",
                "Bearer",
                now,
                now.AddMinutes(10),
                null),
            CancellationToken.None);

        var inner = new FakeHandler(request =>
        {
            if (request.RequestUri!.OriginalString.Contains("auth/login"))
            {
                Interlocked.Increment(ref loginCount);
                return Task.FromResult(OkLoginResponse());
            }

            Assert.Equal("already-cached-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(OkDataResponse());
        });

        var (client, _) = BuildClient(inner, tokenCache);
        await client.GetAsync("custom-filtering/tickers");

        Assert.Equal(0, loginCount);
    }

    [Fact]
    public async Task ConcurrentRequests_UseSingleFlightLogin()
    {
        var loginCount = 0;
        var inner = new FakeHandler(async request =>
        {
            if (request.RequestUri!.OriginalString.Contains("auth/login"))
            {
                Interlocked.Increment(ref loginCount);
                await Task.Delay(25);
                return OkLoginResponse();
            }

            return OkDataResponse();
        });

        var (client, _) = BuildClient(inner);
        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => client.GetAsync("custom-filtering/tickers")));

        Assert.Equal(1, loginCount);
    }

    [Fact]
    public async Task Response401_TriggersReloginAndRetry()
    {
        var loginCount = 0;
        var requestCount = 0;

        var inner = new FakeHandler(request =>
        {
            if (request.RequestUri!.OriginalString.Contains("auth/login"))
            {
                loginCount++;
                return Task.FromResult(OkLoginResponse());
            }

            requestCount++;
            var status = requestCount == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        });

        var (client, _) = BuildClient(inner);
        var response = await client.GetAsync("custom-filtering/tickers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, loginCount);
    }

    [Fact]
    public async Task ExpiredToken_TriggersReloginOnNextRequest()
    {
        var loginCount = 0;
        var tokenCache = CreateTokenCache();
        var now = DateTimeOffset.UtcNow;
        await tokenCache.SetTokenAsync(
            new CyclicalWavesCachedToken(
                "expired-token",
                "Bearer",
                now.AddMinutes(-2),
                now.AddSeconds(-1),
                null),
            CancellationToken.None);

        var inner = new FakeHandler(request =>
        {
            if (request.RequestUri!.OriginalString.Contains("auth/login"))
            {
                loginCount++;
                return Task.FromResult(OkLoginResponse());
            }

            return Task.FromResult(OkDataResponse());
        });

        var (client, _) = BuildClient(inner, tokenCache);
        await client.GetAsync("custom-filtering/tickers");

        Assert.Equal(1, loginCount);
    }

    [Fact]
    public async Task LoginFailure_ThrowsFinancialProviderException()
    {
        var inner = new FakeHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var (client, _) = BuildClient(inner);

        await Assert.ThrowsAsync<FinancialProviderException>(
            () => client.GetAsync("custom-filtering/tickers"));
    }

    [Fact]
    public async Task HtmlLoginResponse_ThrowsDiagnosticInvalidResponse()
    {
        var inner = new FakeHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<html><body>Bad gateway</body></html>",
                    Encoding.UTF8,
                    "text/html")
            }));

        var (client, _) = BuildClient(inner);

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(
            () => client.GetAsync("custom-filtering/tickers"));

        Assert.Equal(FinancialProviderErrorCode.InvalidResponse, exception.Code);
        Assert.Contains("not valid JSON", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bad gateway", exception.Message, StringComparison.Ordinal);
    }

    private static (HttpClient Client, CyclicalWavesTokenCache Cache) BuildClient(
        HttpMessageHandler innerHandler,
        CyclicalWavesTokenCache? cache = null)
    {
        var tokenCache = cache ?? CreateTokenCache();
        var authHandler = new CyclicalWavesAuthHandler(
            tokenCache,
            Microsoft.Extensions.Options.Options.Create(Options),
            TimeProvider.System)
        {
            InnerHandler = innerHandler
        };
        var client = new HttpClient(authHandler)
        {
            BaseAddress = new Uri(Options.BaseAddress)
        };
        return (client, tokenCache);
    }

    private static CyclicalWavesTokenCache CreateTokenCache() =>
        new(
            new MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<CyclicalWavesTokenCache>.Instance);

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request);
    }
}
