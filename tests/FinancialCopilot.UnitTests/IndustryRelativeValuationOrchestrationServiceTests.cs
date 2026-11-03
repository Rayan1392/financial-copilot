using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationOrchestrationServiceTests
{
    [Fact]
    public async Task DisabledFeature_DoesNotInvokeDownstreamDependencies()
    {
        var service = new IndustryRelativeValuationOrchestrationService(
            sourceIngestion: null!,
            inputBuilder: null!,
            snapshotWriter: null!,
            Options.Create(new IndustryRelativeValuationOptions { Enabled = false }),
            Options.Create(new IndustryRelativeValuationSourceOptions()),
            TimeProvider.System,
            NullLogger<IndustryRelativeValuationOrchestrationService>.Instance);

        var result = await service.RunAsync("disabled-test", CancellationToken.None);

        Assert.Equal("disabled-test", result.CorrelationId);
        Assert.Equal(0, result.GroupsCalculated);
        Assert.Equal(0, result.PublishedSnapshots);
    }
}
