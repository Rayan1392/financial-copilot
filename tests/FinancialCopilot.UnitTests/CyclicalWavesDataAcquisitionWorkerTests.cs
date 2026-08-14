using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using FinancialCopilot.Worker;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesDataAcquisitionWorkerTests
{
    [Fact]
    public async Task DisabledWorker_PerformsNoFeatureActivity()
    {
        var service = new RecordingService();
        await using var provider = CreateServiceProvider(service);
        var worker = CreateWorker(provider, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task EnabledWorker_ImmediatelyEvaluatesStartupCycle()
    {
        var service = new RecordingService();
        await using var provider = CreateServiceProvider(service);
        var worker = CreateWorker(provider, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, service.CallCount);
    }

    private static CyclicalWavesDataAcquisitionWorker CreateWorker(
        ServiceProvider provider,
        bool enabled)
    {
        var providerOptions = Options.Create(new CyclicalWavesProviderOptions());
        var tokenCache = new CyclicalWavesTokenCache(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            providerOptions);

        return new CyclicalWavesDataAcquisitionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CyclicalWavesDataAcquisitionOptions
            {
                Enabled = enabled,
                Schedule = "0 2 * * *",
                RequestDelayMilliseconds = 0
            }),
            tokenCache,
            TimeProvider.System,
            NullLogger<CyclicalWavesDataAcquisitionWorker>.Instance);
    }

    private static ServiceProvider CreateServiceProvider(RecordingService service) =>
        new ServiceCollection()
            .AddSingleton<ICyclicalWavesDataAcquisitionService>(service)
            .BuildServiceProvider();

    private sealed class RecordingService : ICyclicalWavesDataAcquisitionService
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CyclicalWavesAcquisitionCycleSummary> ExecuteAsync(
            DateOnly cycleDateUtc,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Called.TrySetResult();
            return Task.FromResult(new CyclicalWavesAcquisitionCycleSummary(
                cycleDateUtc,
                0,
                0,
                0,
                0));
        }
    }
}
