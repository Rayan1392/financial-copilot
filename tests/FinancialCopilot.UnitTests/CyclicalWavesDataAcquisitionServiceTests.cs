using System.Net;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesDataAcquisitionServiceTests
{
    private const string CirclePayload =
        "{\"a\":0,\"b\":1,\"c\":2,\"d\":3,\"e\":4,\"f\":5," +
        "\"close\":6,\"start\":7,\"end\":8,\"min\":9,\"max\":10,\"avg\":11}";

    private const string EquilibriumPayload =
        "{\"a\":0,\"b\":1,\"c\":2,\"d\":3,\"e\":4,\"f\":5," +
        "\"close\":6,\"balance\":7,\"maxbalance\":8,\"minbalance\":9," +
        "\"volume\":10,\"growth\":11,\"enticker\":\"IRO1TEST0001\"}";

    [Fact]
    public async Task ExecuteAsync_UsesStableCompanyAndPsPeEquilibriumOrder()
    {
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var source = new FakeCompanySource(
            new CyclicalWavesAcquisitionCompany(secondId, "272", "نمادب", "IRO1BBBB0002"),
            new CyclicalWavesAcquisitionCompany(firstId, "271", "نمادالف", "IRO1AAAA0001"));
        var client = new RecordingClient();
        var repository = new RecordingRepository();
        var service = CreateService(source, client, repository);

        var summary = await service.ExecuteAsync(new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.Equal(
            [
                "IRO1AAAA0001:PS", "IRO1AAAA0001:LastPS", "IRO1AAAA0001:PE", "IRO1AAAA0001:LastPE", "IRO1AAAA0001:Equilibrium",
                "IRO1BBBB0002:PS", "IRO1BBBB0002:LastPS", "IRO1BBBB0002:PE", "IRO1BBBB0002:LastPE", "IRO1BBBB0002:Equilibrium"
            ],
            client.Calls);
        Assert.Equal(10, summary.Changed);
        Assert.Equal(1, client.MaximumConcurrency);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsSuccessfulCheckpointAndContinuesAfterFailure()
    {
        var companyId = Guid.NewGuid();
        var source = new FakeCompanySource(
            new CyclicalWavesAcquisitionCompany(companyId, "271", "فاسمین", "IRO1TEST0001"));
        var client = new RecordingClient(CyclicalWavesMetricType.PS);
        var repository = new RecordingRepository
        {
            CompletedMetrics = [CyclicalWavesMetricType.PE]
        };
        var service = CreateService(source, client, repository);

        var summary = await service.ExecuteAsync(new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.Equal(["IRO1TEST0001:PS", "IRO1TEST0001:LastPS", "IRO1TEST0001:LastPE", "IRO1TEST0001:Equilibrium"], client.Calls);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(3, summary.Changed);
        Assert.Equal(1, summary.Skipped);
        Assert.Single(repository.Failures);
    }

    [Fact]
    public async Task ExecuteAsync_ResponseBodyTimeoutAfterHeaders_RetriesAndPersistsFailedCheck()
    {
        var companyId = Guid.NewGuid();
        var source = new FakeCompanySource(
            new CyclicalWavesAcquisitionCompany(companyId, "271", "فاسمین", "IRO1TEST0001"));
        var attempts = 0;
        using var httpClient = CreateHttpClient(
            _ =>
            {
                attempts++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new DelayedBodyContent()
                };
            },
            retryCount: 1,
            timeoutSeconds: 1);
        var client = new CyclicalWavesDataAcquisitionClient(httpClient, TimeProvider.System);
        var repository = new RecordingRepository
        {
            CompletedMetrics =
            [
                CyclicalWavesMetricType.PE,
                CyclicalWavesMetricType.LastPS,
                CyclicalWavesMetricType.LastPE,
                CyclicalWavesMetricType.Equilibrium
            ]
        };
        var service = CreateService(source, client, repository);

        var summary = await service.ExecuteAsync(new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(4, summary.Skipped);
        var failure = Assert.Single(repository.Failures);
        Assert.Equal(CyclicalWavesAcquisitionResult.Failed, failure.Result);
        Assert.Equal(CyclicalWavesAcquisitionFailureCodes.Timeout, failure.Acquisition.FailureCode);
        Assert.Equal(2, failure.Acquisition.AttemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_ConnectionResetDuringBodyRead_RetriesThenContinuesSequentially()
    {
        var companyId = Guid.NewGuid();
        var source = new FakeCompanySource(
            new CyclicalWavesAcquisitionCompany(companyId, "271", "فاسمین", "IRO1TEST0001"));
        var requestedPaths = new List<string>();
        using var httpClient = CreateHttpClient(
            request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                requestedPaths.Add(path);
                if (path.Contains("/ps/", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new TruncatedBodyContent()
                    };
                }

                var payload = path.Contains("/equilibrium/", StringComparison.Ordinal)
                    ? EquilibriumPayload
                    : path.Contains("/ps-data/", StringComparison.Ordinal) ||
                      path.Contains("/pe-data/", StringComparison.Ordinal)
                        ? "{\"data\":{\"symbol\":\"IRO1TEST0001\",\"ticker\":\"IRO1TEST0001\",\"ps_ratio\":1,\"pe_ratio\":2,\"close\":3,\"date\":\"2026-08-14\"}}"
                        : CirclePayload;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            },
            retryCount: 1,
            timeoutSeconds: 5);
        var client = new CyclicalWavesDataAcquisitionClient(httpClient, TimeProvider.System);
        var repository = new RecordingRepository();
        var service = CreateService(source, client, repository);

        var summary = await service.ExecuteAsync(new DateOnly(2026, 8, 14), CancellationToken.None);

        Assert.Equal(
            [
                "/api/ps/circle-chart-data/IRO1TEST0001",
                "/api/ps/circle-chart-data/IRO1TEST0001",
                "/api/ps-data/IRO1TEST0001",
                "/api/pe/circle-chart-data/IRO1TEST0001",
                "/api/pe-data/IRO1TEST0001",
                "/api/equilibrium/gauge/IRO1TEST0001"
            ],
            requestedPaths);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(4, summary.Changed);
        var failure = Assert.Single(repository.Failures);
        Assert.Equal(CyclicalWavesAcquisitionFailureCodes.NetworkError, failure.Acquisition.FailureCode);
        Assert.Equal(2, failure.Acquisition.AttemptCount);
        Assert.Equal(
            [CyclicalWavesMetricType.LastPS, CyclicalWavesMetricType.PE, CyclicalWavesMetricType.LastPE, CyclicalWavesMetricType.Equilibrium],
            repository.Accepted.Select(item => item.MetricType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("فاسمین")]
    public void ResolveIsin_RejectsMissingOrInvalidSymbolIsin(string? symbolIsin)
    {
        var result = CyclicalWavesDataAcquisitionService.ResolveIsin(
            new CyclicalWavesAcquisitionCompany(Guid.NewGuid(), "271", "فاسمین", symbolIsin));

        Assert.Null(result.NormalizedIsin);
        Assert.Equal(CyclicalWavesAcquisitionFailureCodes.MissingSymbolIsin, result.FailureCode);
    }

    [Fact]
    public void UtcCronSchedule_ParsesAndFindsNextOccurrence()
    {
        var schedule = CyclicalWavesUtcCronSchedule.Parse("0 2 * * *");

        var next = schedule.GetNextOccurrence(DateTimeOffset.Parse("2026-08-14T02:00:01Z"));

        Assert.Equal(DateTimeOffset.Parse("2026-08-15T02:00:00Z"), next);
        Assert.False(CyclicalWavesUtcCronSchedule.IsValid("0 2 * *"));
    }

    private static CyclicalWavesDataAcquisitionService CreateService(
        ICyclicalWavesAcquisitionCompanySource source,
        ICyclicalWavesDataAcquisitionClient client,
        ICyclicalWavesDataAcquisitionRepository repository) =>
        new(
            source,
            client,
            repository,
            new CanonicalJsonHasher(),
            Options.Create(new CyclicalWavesDataAcquisitionOptions
            {
                RequestDelayMilliseconds = 0
            }),
            TimeProvider.System,
            NullLogger<CyclicalWavesDataAcquisitionService>.Instance);

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        int retryCount,
        int timeoutSeconds)
    {
        var resilience = new CyclicalWavesDataAcquisitionResilienceHandler(
            Options.Create(new CyclicalWavesDataAcquisitionOptions
            {
                RetryCount = retryCount,
                TimeoutSeconds = timeoutSeconds
            }),
            TimeProvider.System)
        {
            InnerHandler = new StubHandler(responseFactory)
        };

        return new HttpClient(resilience)
        {
            BaseAddress = new Uri("https://api.example.test/api/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private sealed class FakeCompanySource(params CyclicalWavesAcquisitionCompany[] companies) :
        ICyclicalWavesAcquisitionCompanySource
    {
        public Task<IReadOnlyList<CyclicalWavesAcquisitionCompany>> GetCompaniesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CyclicalWavesAcquisitionCompany>>(companies);
    }

    private sealed class RecordingClient(CyclicalWavesMetricType? failedMetric = null) :
        ICyclicalWavesDataAcquisitionClient
    {
        private int _concurrency;
        public List<string> Calls { get; } = [];
        public int MaximumConcurrency { get; private set; }

        public Task<CyclicalWavesProviderAcquisitionResult> AcquireAsync(
            CyclicalWavesMetricType metricType,
            string normalizedIsin,
            CancellationToken cancellationToken)
        {
            var concurrency = Interlocked.Increment(ref _concurrency);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
            Calls.Add($"{normalizedIsin}:{metricType}");
            var now = DateTimeOffset.UtcNow;
            Interlocked.Decrement(ref _concurrency);

            return Task.FromResult(metricType == failedMetric
                ? new CyclicalWavesProviderAcquisitionResult(
                    metricType,
                    "endpoint",
                    now,
                    now,
                    null,
                    now,
                    null,
                    500,
                    1,
                    CyclicalWavesAcquisitionFailureCodes.ProviderServerError,
                    "Provider failed.")
                : new CyclicalWavesProviderAcquisitionResult(
                    metricType,
                    "endpoint",
                    now,
                    now,
                    now,
                    now,
                    metricType is CyclicalWavesMetricType.LastPS or CyclicalWavesMetricType.LastPE
                        ? "{\"data\":{\"symbol\":\"IRO1TEST0001\",\"ticker\":\"IRO1TEST0001\",\"ps_ratio\":1,\"pe_ratio\":2,\"close\":3,\"date\":\"2026-08-14\"}}"
                        : "{\"value\":1}",
                    200,
                    1,
                    null,
                    null));
        }
    }

    private sealed class RecordingRepository : ICyclicalWavesDataAcquisitionRepository
    {
        public HashSet<CyclicalWavesMetricType> CompletedMetrics { get; init; } = [];
        public List<CyclicalWavesAcceptedAcquisition> Accepted { get; } = [];
        public List<RecordedFailure> Failures { get; } = [];

        public Task<bool> HasSuccessfulCheckAsync(
            DateOnly cycleDateUtc,
            Guid companyId,
            CyclicalWavesMetricType metricType,
            CancellationToken cancellationToken) =>
            Task.FromResult(CompletedMetrics.Contains(metricType));

        public Task<CyclicalWavesPersistenceResult> PersistAcceptedAsync(
            CyclicalWavesAcceptedAcquisition acquisition,
            CancellationToken cancellationToken)
        {
            Accepted.Add(acquisition);
            return Task.FromResult(new CyclicalWavesPersistenceResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CyclicalWavesAcquisitionResult.Changed));
        }

        public Task<Guid> PersistFailedAsync(
            CyclicalWavesFailedAcquisition acquisition,
            CancellationToken cancellationToken)
        {
            Failures.Add(new RecordedFailure(CyclicalWavesAcquisitionResult.Failed, acquisition));
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed record RecordedFailure(
        CyclicalWavesAcquisitionResult Result,
        CyclicalWavesFailedAcquisition Acquisition);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class DelayedBodyContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.Delay(TimeSpan.FromSeconds(10));

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 100;
            return true;
        }
    }

    private sealed class TruncatedBodyContent : HttpContent
    {
        private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("{\"a\":");

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            WritePartialBodyAndFailAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) =>
            WritePartialBodyAndFailAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 100;
            return true;
        }

        private static async Task WritePartialBodyAndFailAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            await stream.WriteAsync(Prefix.AsMemory(), cancellationToken);
            throw new IOException("The provider closed the connection before the response body completed.");
        }
    }
}
