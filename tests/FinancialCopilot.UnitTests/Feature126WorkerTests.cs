using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class Feature126WorkerTests
{
    [Fact]
    public async Task DisabledFeatureDoesNotCreateScopeOrExecutePipeline()
    {
        var health = new Feature126WorkerHealth();
        var worker = new CyclicalWavesRelativeValuationWorker(
            new ThrowingScopeFactory(),
            Options.Create(new RelativeValuationIngestionOptions { Enabled = false }),
            health,
            NullLogger<CyclicalWavesRelativeValuationWorker>.Instance);

        await worker.ExecuteTickAsync(CancellationToken.None);

        Assert.Equal(Feature126WorkerReadiness.disabled, health.State);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new Xunit.Sdk.XunitException("A disabled Feature126 worker must not create a scope.");
    }
}
