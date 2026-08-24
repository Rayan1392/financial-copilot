using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class SingleCompanyMonthlyDirectIngestionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-24T10:00:00Z");

    [Fact]
    public async Task ExecuteDirect_FetchesAndProcessesInlineWithoutRabbitMqPublisher()
    {
        var directProvider = new RecordingDirectProvider();
        var processor = new RecordingProcessor();
        var service = new SingleCompanyMonthlyIngestionService(
            new RejectingPublisher(),
            directProvider,
            processor,
            Options.Create(new NadpcoApiProviderOptions { ProviderName = "NoavaranCurrentApi" }),
            new FixedTimeProvider(Now));

        var result = await service.ExecuteDirectAsync(
            new SingleCompanyMonthlyDirectIngestionRequest(19, 1405, 5),
            CancellationToken.None);

        Assert.Equal(("19", 1405, 5), directProvider.Invocation);
        Assert.NotNull(processor.Request);
        Assert.Equal("19", processor.Request.ExternalReference);
        Assert.Equal("1405/05/01", processor.Request.SourceDateRangeStartJalali);
        Assert.Equal("1405/05/31", processor.Request.SourceDateRangeEndJalali);
        Assert.StartsWith("nadpco-single-monthly-direct-140505-19-", processor.Request.IdempotencyKey);
        Assert.Same(directProvider.Payload, processor.Payload);
        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);
    }

    private sealed class RejectingPublisher : IDataSyncRequestPublisher
    {
        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("The direct endpoint must not publish to RabbitMQ.");
    }

    private sealed class RecordingDirectProvider : INadpcoMonthlyProductSalesDirectProvider
    {
        public (string CompanyId, int Year, int Month)? Invocation { get; private set; }

        public ProviderRawPayload Payload { get; } = new(
            Guid.NewGuid(),
            "NoavaranCurrentApi",
            ProviderDataset.MonthlyProductionSales,
            "api/v2/MonthlyActivity/ProductSales?outputTypeId=0",
            "19",
            "{}",
            "checksum",
            Now);

        public Task<ProviderRawPayload> FetchProductSalesOutputTypeZeroAsync(
            string externalCompanyId,
            int shamsiYear,
            int shamsiMonth,
            CancellationToken cancellationToken)
        {
            Invocation = (externalCompanyId, shamsiYear, shamsiMonth);
            return Task.FromResult(Payload);
        }
    }

    private sealed class RecordingProcessor : IFinancialDataSyncProcessor
    {
        public DataSyncRequest? Request { get; private set; }
        public ProviderRawPayload? Payload { get; private set; }

        public Task<DataSyncProcessingResult> ProcessAsync(
            DataSyncRequest request,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("The direct endpoint must process the supplied payload.");

        public Task<DataSyncProcessingResult> ProcessPayloadAsync(
            DataSyncRequest request,
            ProviderRawPayload payload,
            CancellationToken cancellationToken)
        {
            Request = request;
            Payload = payload;
            return Task.FromResult(new DataSyncProcessingResult(
                new DataSyncRun(
                    request.RequestId,
                    request.IdempotencyKey,
                    request.Dataset,
                    request.ExternalReference,
                    DataSyncRunStatus.Completed,
                    request.RequestedAt,
                    request.RequestedAt,
                    request.RequestedAt.AddSeconds(1),
                    ProcessedRecords: 1,
                    ErrorCount: 0,
                    ErrorMessage: null,
                    SourcePayloadChecksum: payload.Checksum,
                    ProviderName: request.ProviderName,
                    Mode: request.Mode,
                    SourceDateRangeStartJalali: request.SourceDateRangeStartJalali,
                    SourceDateRangeEndJalali: request.SourceDateRangeEndJalali),
                AlreadyProcessed: false));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
