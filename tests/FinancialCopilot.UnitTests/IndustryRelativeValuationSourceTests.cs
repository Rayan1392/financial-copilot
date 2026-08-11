using System.Net;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationSourceTests
{
    [Fact]
    public void PsProjection_UsesGaugeCloseAndGaugeAverage_NotBoundaryAverage()
    {
        var projection = PsRelativeValuationFactProjection.FromGauge(
            Guid.NewGuid(), "CyclicalWaves", "observation-1", "IRO123456789",
            new PsGaugeDistribution(1, 2, 3, 4, 5, 6, 4.25m, 2.5m, 1m, 1.5m, 99m, 8m, 9m),
            DateTimeOffset.UtcNow, "hash", "payload");

        Assert.Equal(4.25m, projection.CurrentPS);
        Assert.Equal(2.5m, projection.HistoricalAveragePS);
        Assert.NotEqual(99m, projection.HistoricalAveragePS);
    }

    [Fact]
    public async Task PeGauge_MapsCloseAndAvg_AndIgnoresAdditiveFields()
    {
        var client = CreateProviderClient((request, _) =>
            JsonResponse("{\"close\":5.4,\"avg\":7.2,\"new_field\":\"ignored\"}"));

        var result = await client.GetPeGaugeAsync("IRO123456789", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RelativeValuationSourceKind.PEGauge, result.SourceKind);
        Assert.Equal(5.4m, result.CurrentValue);
        Assert.Equal(7.2m, result.ReferenceValue);
    }

    [Fact]
    public async Task EquilibriumGauge_MapsCloseAndBalance_AndChecksIdentity()
    {
        var client = CreateProviderClient((request, _) =>
            JsonResponse("{\"enticker\":\"IRO123456789\",\"ticker\":\"TEST\",\"close\":1000,\"balance\":1250,\"future_field\":1}"));

        var result = await client.GetEquilibriumGaugeAsync("IRO123456789", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1000m, result.CurrentValue);
        Assert.Equal(1250m, result.ReferenceValue);
        Assert.Contains("response-identity:IRO123456789", result.IdentityEvidence);
    }

    [Fact]
    public async Task ProviderContracts_DistinguishMalformedIdentityAndNumericFailures()
    {
        var malformed = CreateProviderClient((_, _) => JsonResponse("{bad"));
        var malformedResult = await malformed.GetPeGaugeAsync("IRO123456789", CancellationToken.None);
        Assert.Equal(RelativeValuationFactReadiness.InvalidPayload, malformedResult.Readiness);

        var mismatch = CreateProviderClient((_, _) => JsonResponse("{\"ticker\":\"OTHER\",\"enticker\":\"OTHER\",\"close\":100,\"balance\":120}"));
        var mismatchResult = await mismatch.GetEquilibriumGaugeAsync("IRO123456789", CancellationToken.None);
        Assert.Equal(RelativeValuationFactReadiness.IdentityMismatch, mismatchResult.Readiness);

        var invalid = CreateProviderClient((_, _) => JsonResponse("{\"close\":0,\"avg\":2}"));
        var invalidResult = await invalid.GetPeGaugeAsync("IRO123456789", CancellationToken.None);
        Assert.Equal(RelativeValuationFactReadiness.InvalidNumericValue, invalidResult.Readiness);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, RelativeValuationFactReadiness.NotFoundOrNoData)]
    [InlineData(HttpStatusCode.TooManyRequests, RelativeValuationFactReadiness.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, RelativeValuationFactReadiness.RemoteServerFailure)]
    public async Task ProviderContracts_PreserveHttpFailureClassification(HttpStatusCode status, RelativeValuationFactReadiness expected)
    {
        var client = CreateProviderClient((_, _) => new HttpResponseMessage(status));

        var result = await client.GetPeGaugeAsync("IRO123456789", CancellationToken.None);
        var equilibrium = await client.GetEquilibriumGaugeAsync("IRO123456789", CancellationToken.None);

        Assert.Equal(expected, result.Readiness);
        Assert.Equal(expected, equilibrium.Readiness);
    }

    [Fact]
    public async Task ProviderContracts_ClassifyTimeout()
    {
        var handler = new StubHandler((_, _) => throw new TaskCanceledException("timeout"));
        var client = CreateProviderClient(handler);

        var result = await client.GetPeGaugeAsync("IRO123456789", CancellationToken.None);

        Assert.Equal(RelativeValuationFactReadiness.Timeout, result.Readiness);
    }

    [Fact]
    public async Task SourceIngestion_IsIdempotent_AndChangedHashCreatesNewVersions()
    {
        await using var db = CreateDb();
        var company = new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = "1",
            Name = "Company",
            IndustryId = Guid.NewGuid(),
            SymbolIsin = "IRO123456789",
            LastSynchronizedAt = DateTimeOffset.UtcNow
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var provider = new StubRelativeProvider();
        var service = new IndustryRelativeValuationSourceIngestionService(
            db,
            provider,
            Options.Create(new IndustryRelativeValuationSourceOptions { Enabled = true }),
            TimeProvider.System,
            NullLogger<IndustryRelativeValuationSourceIngestionService>.Instance);

        var first = await service.RunAsync(new(), CancellationToken.None);
        var second = await service.RunAsync(new(), CancellationToken.None);
        provider.Version = 2;
        var third = await service.RunAsync(new(), CancellationToken.None);

        Assert.Equal(2, first.FactsPersisted);
        Assert.Equal(2, second.FactsUnchanged);
        Assert.Equal(2, third.FactsPersisted);
        Assert.Equal(4, await db.IndustryRelativeValuationSourceFacts.CountAsync());
    }

    [Fact]
    public void SourceFactModel_HasImmutableObservationUniquenessMetadata()
    {
        using var db = CreateDb();
        var index = db.Model.FindEntityType(typeof(IndustryRelativeValuationSourceFactRow))!
            .GetIndexes()
            .Single(candidate => candidate.IsUnique && candidate.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(IndustryRelativeValuationSourceFactRow.ProviderName), nameof(IndustryRelativeValuationSourceFactRow.SourceKind), nameof(IndustryRelativeValuationSourceFactRow.SourceObservationId) }));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void SourceIngestion_DoesNotDependOnFeature114ProviderOrCreateASecondPsWorker()
    {
        var constructorTypes = typeof(IndustryRelativeValuationSourceIngestionService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(ICyclicalWavesPsProviderClient), constructorTypes);
    }

    private static CyclicalWavesDataProviderClient CreateProviderClient(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        return CreateProviderClient(new StubHandler(responder));
    }

    private static CyclicalWavesDataProviderClient CreateProviderClient(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        return new CyclicalWavesDataProviderClient(
            httpClient,
            new FakeRawPayloadStore(),
            Options.Create(new CyclicalWavesProviderOptions { PsMaxResponseBytes = 1024 * 1024 }),
            TimeProvider.System,
            NullLogger<CyclicalWavesDataProviderClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request, cancellationToken));
    }

    private sealed class FakeRawPayloadStore : IProviderRawPayloadStore
    {
        public Task StoreAsync(ProviderRawPayload payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ProviderRawPayload?> FindByChecksumAsync(string providerName, string checksum, CancellationToken cancellationToken) => Task.FromResult<ProviderRawPayload?>(null);
    }

    private sealed class StubRelativeProvider : ICyclicalWavesRelativeValuationProviderClient
    {
        public int Version { get; set; } = 1;
        public Task<RelativeValuationProviderResult> GetPeGaugeAsync(string isin, CancellationToken cancellationToken) =>
            Task.FromResult(Result(RelativeValuationSourceKind.PEGauge, 5m + Version, 7m, "pe"));
        public Task<RelativeValuationProviderResult> GetEquilibriumGaugeAsync(string isin, CancellationToken cancellationToken) =>
            Task.FromResult(Result(RelativeValuationSourceKind.EquilibriumGauge, 100m + Version, 120m, "eq"));
        private static RelativeValuationProviderResult Result(RelativeValuationSourceKind kind, decimal current, decimal reference, string key) =>
            new(kind, current, reference, $"{key}-observation-{current}", $"{key}/endpoint", "identity", RelativeValuationFactReadiness.Ready, "Valid", $"hash-{current}", "{}");
    }
}
