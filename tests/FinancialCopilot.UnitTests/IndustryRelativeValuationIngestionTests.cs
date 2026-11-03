using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationIngestionTests
{
    [Fact]
    public async Task AcceptedPsOperation_DelegatesOneAdmittedIsinWithoutScopeFiltering()
    {
        var client = new StubPsClient();
        var operation = new CyclicalWavesPsOperation(client);

        var result = await operation.AcquireAcceptedPsGaugeAsync(" IRO123456789 ", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("IRO123456789", client.RequestedIsin);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void LeaseEnvelope_RoundTripsStateDateAndToken()
    {
        var token = Guid.NewGuid();
        var owner = new LeaseOwnerId(
            "feature126",
            new DateOnly(2026, 8, 12),
            token,
            LeaseState.Handoff);

        Assert.True(LeaseFencingEnvelope.TryParse(owner.Envelope, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(owner.CalculationDate, parsed!.CalculationDate);
        Assert.Equal(owner.FencingToken, parsed.FencingToken);
        Assert.Equal(owner.State, parsed.State);
        Assert.Equal(owner.Envelope, LeaseFencingEnvelope.Serialize(parsed));
    }

    [Fact]
    public void LeaseEnvelope_RejectsNonCanonicalValues()
    {
        Assert.False(LeaseFencingEnvelope.TryParse("v1|running|2026-08-12|not-a-token", out _));
        Assert.False(LeaseFencingEnvelope.TryParse("v1|Running|12/08/2026|00000000000000000000000000000001", out _));
    }

    [Fact]
    public async Task SourceFactStore_RejectsInvalidAndNoOpsUnchangedObservation()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new FinancialIngestionDbContext(options);
        var store = new IndustryRelativeValuationSourceFactStore(db, new FixedTimeProvider());
        var owner = new LeaseHandle("feature126", new DateOnly(2026, 8, 12), Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));
        var result = new RelativeValuationProviderResult(
            RelativeValuationSourceKind.PEGauge, 5m, 7m, "obs-1", "pe/circle-chart-data/isin",
            "response-identity:isin", RelativeValuationFactReadiness.Ready, "Valid", "hash-1", "{}",
            SourceWatermark: "watermark-1");

        Assert.Equal(Feature126SourceFactWriteResult.Persisted,
            await store.PersistAcceptedAsync(Guid.NewGuid(), result, owner, CancellationToken.None));
        Assert.Equal(Feature126SourceFactWriteResult.Unchanged,
            await store.PersistAcceptedAsync(Guid.NewGuid(), result, owner, CancellationToken.None));
        Assert.Equal(1, await db.IndustryRelativeValuationSourceFacts.CountAsync());

        var rejected = result with { Readiness = RelativeValuationFactReadiness.InvalidNumericValue, CurrentValue = null };
        Assert.Equal(Feature126SourceFactWriteResult.Rejected,
            await store.PersistAcceptedAsync(Guid.NewGuid(), rejected, owner, CancellationToken.None));
        Assert.Equal(1, await db.IndustryRelativeValuationSourceFacts.CountAsync());
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubPsClient : ICyclicalWavesPsProviderClient
    {
        public string? RequestedIsin { get; private set; }
        public int CallCount { get; private set; }

        public Task<PsProviderResult<PsGaugeDistribution>> GetGaugeAsync(string symbolIsin, CancellationToken cancellationToken)
        {
            RequestedIsin = symbolIsin;
            CallCount++;
            return Task.FromResult(new PsProviderResult<PsGaugeDistribution>(
                new PsGaugeDistribution(1, 2, 3, 4, 5, 6, 4.25m, 2.5m, 1, 1.5m, 99, 8, 9),
                PsVisualizationSyncErrorCode.None));
        }

        public Task<PsProviderResult<PsCurrentValues>> GetCurrentValuesAsync(string symbolIsin, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PsProviderResult<PsForwardValues>> GetForwardValuesAsync(string companySymbol, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PsProviderResult<PsHistorySeries>> GetHistoryAsync(string symbolIsin, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
