using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class Feature126WorkerTests
{
    [Fact]
    public async Task MasterDisabledDoesNotCreateScopeOrStartScheduledExecution()
    {
        var health = new Feature126WorkerHealth();
        var logger = new CapturingLogger<CyclicalWavesRelativeValuationWorker>();
        var worker = new CyclicalWavesRelativeValuationWorker(
            new ThrowingScopeFactory(),
            Options.Create(new Feature126Options { Enabled = false }),
            new ThrowingOptions<RelativeValuationIngestionOptions>(),
            health,
            logger);

        await worker.StartAsync(CancellationToken.None);
        await WaitForAsync(() => health.State == Feature126WorkerReadiness.disabled);

        Assert.Equal(Feature126WorkerReadiness.disabled, health.State);
        Assert.Contains(logger.Messages, message =>
            message.Contains("Feature126:Enabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MasterDisabledTickDoesNotCreateScope()
    {
        var health = new Feature126WorkerHealth();
        var worker = new CyclicalWavesRelativeValuationWorker(
            new ThrowingScopeFactory(),
            Options.Create(new Feature126Options { Enabled = false }),
            new ThrowingOptions<RelativeValuationIngestionOptions>(),
            health,
            new CapturingLogger<CyclicalWavesRelativeValuationWorker>());

        await worker.ExecuteTickAsync(CancellationToken.None);

        Assert.Equal(Feature126WorkerReadiness.disabled, health.State);
    }

    [Fact]
    public void MasterSwitchDefaultsToDisabledAndDisabledHealthIsReady()
    {
        var health = new Feature126WorkerHealth();
        health.MarkDisabled();

        Assert.False(new Feature126Options().Enabled);
        Assert.True(health.Live);
        Assert.True(Feature126ManagementServer.IsReady(health.Snapshot()));
        Assert.Contains("feature126_readiness{state=\"disabled\"} 1", Feature126PrometheusMetrics.Render(health.Snapshot()));
    }

    [Fact]
    public async Task MasterDisabledManagementDoesNotResolveOperationalScope()
    {
        var health = new Feature126WorkerHealth();
        var server = new Feature126ManagementServer(
            Options.Create(new Feature126ManagementOptions { Enabled = false }),
            Options.Create(new Feature126Options { Enabled = false }),
            new ThrowingOptions<RelativeValuationIngestionOptions>(),
            new ThrowingScopeFactory(),
            health,
            new CapturingLogger<Feature126ManagementServer>());

        await server.StartAsync(CancellationToken.None);
        await WaitForAsync(() => health.State == Feature126WorkerReadiness.disabled);

        Assert.Equal(Feature126WorkerReadiness.disabled, health.State);
        Assert.True(Feature126ManagementServer.IsReady(health.Snapshot()));

        await server.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new Xunit.Sdk.XunitException("A disabled Feature126 worker must not create a scope.");
    }

    private sealed class ThrowingOptions<T> : IOptions<T> where T : class
    {
        public T Value => throw new Xunit.Sdk.XunitException("The master-disabled path must not read nested execution options.");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
