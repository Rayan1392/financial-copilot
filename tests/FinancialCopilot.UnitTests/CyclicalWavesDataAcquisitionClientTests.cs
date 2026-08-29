using System.Net;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesDataAcquisitionClientTests
{
    private const string CirclePayload =
        "{ \"a\":0,\"b\":1,\"c\":2,\"d\":3,\"e\":4,\"f\":5," +
        "\"close\":6,\"start\":7,\"end\":8,\"min\":9,\"max\":10,\"avg\":11," +
        "\"unknown\":\"نگهداری شود\" }";

    [Theory]
    [InlineData(CyclicalWavesMetricType.PS, "ps/circle-chart-data/IRO1TEST0001")]
    [InlineData(CyclicalWavesMetricType.PE, "pe/circle-chart-data/IRO1TEST0001")]
    public async Task CircleEndpoints_PreserveCompleteRawResponse(
        CyclicalWavesMetricType metricType,
        string expectedEndpoint)
    {
        string? requestedPath = null;
        var client = CreateClient(
            _ =>
            {
                requestedPath = _.RequestUri!.PathAndQuery.TrimStart('/');
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CirclePayload, Encoding.UTF8, "application/json")
                };
            });

        var result = await client.AcquireAsync(metricType, "IRO1TEST0001", CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.EndsWith(expectedEndpoint, requestedPath, StringComparison.Ordinal);
        Assert.Equal(expectedEndpoint, result.SourceEndpoint);
        Assert.Equal(CirclePayload, result.RawResponseJson);
        Assert.Equal(1, result.AttemptCount);
        Assert.NotNull(result.RequestedAtUtc);
        Assert.NotNull(result.AcquisitionDateUtc);
    }

    [Theory]
    [InlineData(CyclicalWavesMetricType.LastPS, "ps-data/IRO1TEST0001")]
    [InlineData(CyclicalWavesMetricType.LastPE, "pe-data/IRO1TEST0001")]
    public async Task LatestValuationEndpoints_ValidateEnvelopeAndExposeProviderDate(
        CyclicalWavesMetricType metricType,
        string expectedEndpoint)
    {
        const string payload =
            "{\"data\":{\"symbol\":\"IRO1TEST0001\",\"ticker\":\"IRO1TEST0001\"," +
            "\"ps_ratio\":1.2,\"pe_ratio\":8.4,\"close\":100,\"date\":\"2026-08-14\"}}";
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });

        var result = await client.AcquireAsync(metricType, "IRO1TEST0001", CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Equal(expectedEndpoint, result.SourceEndpoint);
        Assert.Equal(new DateOnly(2026, 8, 14), result.ProviderObservationDate);
        Assert.Equal(payload, result.RawResponseJson);
    }

    [Fact]
    public async Task LargeResponseBody_PreservesCompleteRawResponse()
    {
        var largePayload =
            "{\"a\":0,\"b\":1,\"c\":2,\"d\":3,\"e\":4,\"f\":5," +
            "\"close\":6,\"start\":7,\"end\":8,\"min\":9,\"max\":10,\"avg\":11," +
            $"\"unknown\":\"{new string('x', 2 * 1024 * 1024)}\"}}";
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largePayload, Encoding.UTF8, "application/json")
        });

        var result = await client.AcquireAsync(
            CyclicalWavesMetricType.PS,
            "IRO1TEST0001",
            CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Equal(largePayload, result.RawResponseJson);
    }

    [Fact]
    public async Task EquilibriumIdentityMismatch_IsRejectedWithoutRawPersistence()
    {
        const string payload =
            "{\"a\":1,\"b\":2,\"c\":3,\"d\":4,\"e\":5,\"f\":6," +
            "\"close\":7,\"balance\":8,\"maxbalance\":9,\"minbalance\":10," +
            "\"volume\":11,\"growth\":12,\"enticker\":\"IRO1OTHER001\"}";
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });

        var result = await client.AcquireAsync(
            CyclicalWavesMetricType.Equilibrium,
            "IRO1TEST0001",
            CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal(CyclicalWavesAcquisitionFailureCodes.IdentityMismatch, result.FailureCode);
        Assert.Null(result.RawResponseJson);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task NoDataResponses_AreTerminalFailedChecks(HttpStatusCode statusCode)
    {
        var client = CreateClient(_ => new HttpResponseMessage(statusCode));

        var result = await client.AcquireAsync(
            CyclicalWavesMetricType.PS,
            "IRO1TEST0001",
            CancellationToken.None);

        Assert.Equal(CyclicalWavesAcquisitionFailureCodes.NotFoundOrNoData, result.FailureCode);
        Assert.Equal(1, result.AttemptCount);
    }

    [Fact]
    public async Task RedirectToLogin_IsRejectedAsAuthenticationFailure()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("https://api.example.test/api/auth/login") },
            Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html")
        });

        var result = await client.AcquireAsync(
            CyclicalWavesMetricType.LastPS,
            "IRO1TEST0001",
            CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal(CyclicalWavesAcquisitionFailureCodes.AuthenticationFailed, result.FailureCode);
        Assert.Equal(302, result.HttpStatusCode);
        Assert.Contains("login", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerFailure_IsRetriedWithinBoundAndThenAccepted()
    {
        var calls = 0;
        var client = CreateClient(
            _ =>
            {
                calls++;
                return calls == 1
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(CirclePayload, Encoding.UTF8, "application/json")
                    };
            },
            retryCount: 1);

        var result = await client.AcquireAsync(
            CyclicalWavesMetricType.PS,
            "IRO1TEST0001",
            CancellationToken.None);

        Assert.True(result.IsAccepted);
        Assert.Equal(2, calls);
        Assert.Equal(2, result.AttemptCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task NonTransientClientStatus_IsNotGenerallyRetried(HttpStatusCode statusCode)
    {
        var calls = 0;
        var client = CreateClient(
            _ =>
            {
                calls++;
                return new HttpResponseMessage(statusCode);
            },
            retryCount: 2);

        var result = await client.AcquireAsync(
            CyclicalWavesMetricType.PS,
            "IRO1TEST0001",
            CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.AttemptCount);
    }

    [Theory]
    [InlineData("{", CyclicalWavesAcquisitionFailureCodes.InvalidJson)]
    [InlineData("{}", CyclicalWavesAcquisitionFailureCodes.ContractMismatch)]
    public async Task InvalidOrMismatchedJson_IsNotRetried(
        string payload,
        string expectedFailureCode)
    {
        var calls = 0;
        var client = CreateClient(
            _ =>
            {
                calls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            },
            retryCount: 2);

        var result = await client.AcquireAsync(
            CyclicalWavesMetricType.PS,
            "IRO1TEST0001",
            CancellationToken.None);

        Assert.False(result.IsAccepted);
        Assert.Equal(expectedFailureCode, result.FailureCode);
        Assert.Equal(1, calls);
        Assert.Equal(1, result.AttemptCount);
    }

    private static CyclicalWavesDataAcquisitionClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        int retryCount = 0)
    {
        var resilience = new CyclicalWavesDataAcquisitionResilienceHandler(
            Options.Create(new CyclicalWavesDataAcquisitionOptions
            {
                RetryCount = retryCount,
                TimeoutSeconds = 5
            }),
            TimeProvider.System)
        {
            InnerHandler = new StubHandler(responseFactory)
        };
        var httpClient = new HttpClient(resilience)
        {
            BaseAddress = new Uri("https://api.example.test/api/")
        };
        return new CyclicalWavesDataAcquisitionClient(
            httpClient,
            TimeProvider.System,
            NullLogger<CyclicalWavesDataAcquisitionClient>.Instance);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
