using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationCalculationWorkerTests
{
    [Fact]
    public async Task Tick_ExecutesIndependentlyAfterAcquisitionFailure()
    {
        var orchestration = new RecordingOrchestration();
        var acquisition = new FailingAcquisition();
        await using var provider = new ServiceCollection()
            .AddSingleton<IIndustryRelativeValuationOrchestrationService>(orchestration)
            .AddSingleton<ICyclicalWavesDataAcquisitionService>(acquisition)
            .BuildServiceProvider();
        var worker = new IndustryRelativeValuationCalculationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IndustryRelativeValuationOptions { Enabled = true }),
            new FixedTimeProvider(new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<IndustryRelativeValuationCalculationWorker>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => acquisition.ExecuteAsync(
            new DateOnly(2026, 8, 15), CancellationToken.None));
        await worker.ExecuteTickAsync(CancellationToken.None);

        Assert.Equal(1, orchestration.CallCount);
        Assert.StartsWith("industry-relative-valuation-", orchestration.CorrelationId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TickFailure_DoesNotEscapeIntoAcquisitionExecution()
    {
        await using var provider = new ServiceCollection()
            .AddSingleton<IIndustryRelativeValuationOrchestrationService>(new ThrowingOrchestration())
            .BuildServiceProvider();
        var worker = new IndustryRelativeValuationCalculationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IndustryRelativeValuationOptions { Enabled = true }),
            TimeProvider.System,
            NullLogger<IndustryRelativeValuationCalculationWorker>.Instance);

        await worker.ExecuteTickAsync(CancellationToken.None);
    }

    private sealed class RecordingOrchestration : IIndustryRelativeValuationOrchestrationService
    {
        public int CallCount { get; private set; }
        public string CorrelationId { get; private set; } = string.Empty;

        public Task<IndustryRelativeValuationOrchestrationResult> RunAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CorrelationId = correlationId;
            return Task.FromResult(new IndustryRelativeValuationOrchestrationResult(
                correlationId, 0, 0, 0, 0, 0, 0, 0));
        }
    }

    private sealed class ThrowingOrchestration : IIndustryRelativeValuationOrchestrationService
    {
        public Task<IndustryRelativeValuationOrchestrationResult> RunAsync(
            string correlationId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("calculation failed");
    }

    private sealed class FailingAcquisition : ICyclicalWavesDataAcquisitionService
    {
        public Task<CyclicalWavesAcquisitionCycleSummary> ExecuteAsync(
            DateOnly cycleDateUtc,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("provider acquisition failed");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
