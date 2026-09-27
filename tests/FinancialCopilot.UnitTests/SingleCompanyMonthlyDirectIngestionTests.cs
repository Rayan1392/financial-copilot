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
    public async Task ExecuteDirect_FetchesOutputTypeZeroInlineAndQueuesRemainingTypes()
    {
        var directProvider = new RecordingDirectProvider();
        var processor = new RecordingProcessor();
        var publisher = new RecordingPublisher();
        var service = new SingleCompanyMonthlyIngestionService(
            publisher,
            directProvider,
            processor,
            Options.Create(new NadpcoApiProviderOptions { ProviderName = "NoavaranCurrentApi" }),
            new FixedTimeProvider(Now));

        var result = await service.ExecuteDirectAsync(
            new SingleCompanyMonthlyDirectIngestionRequest(19, 1405, 5),
            CancellationToken.None);

        Assert.Equal(("19", 1405, 5), directProvider.Invocation);
        Assert.Equal(0, directProvider.OutputType);
        Assert.NotNull(processor.Request);
        Assert.Equal("19", processor.Request.ExternalReference);
        Assert.Equal(0, processor.Request.MonthlyActivityOutputType);
        Assert.Equal("1405/05/01", processor.Request.SourceDateRangeStartJalali);
        Assert.Equal("1405/05/31", processor.Request.SourceDateRangeEndJalali);
        Assert.StartsWith("nadpco-single-monthly-direct-140505-19-", processor.Request.IdempotencyKey);
        Assert.Same(directProvider.Payload, processor.Payload);
        Assert.Equal(DataSyncRunStatus.Completed, result.Run.Status);
        Assert.Equal(new[] { 1, 2, 3, 4 }, publisher.Requests.Select(x => x.MonthlyActivityOutputType!.Value).ToArray());
    }

    [Fact]
    public async Task ExecuteDirect_ProviderFailure_PersistsFailedRunBeforeRethrowing()
    {
        var directProvider = new ThrowingDirectProvider();
        var processor = new RecordingProcessor();
        var publisher = new RecordingPublisher();
        var service = new SingleCompanyMonthlyIngestionService(
            publisher,
            directProvider,
            processor,
            Options.Create(new NadpcoApiProviderOptions { ProviderName = "NoavaranCurrentApi" }),
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() =>
            service.ExecuteDirectAsync(
                new SingleCompanyMonthlyDirectIngestionRequest(4, 1405, 6),
                CancellationToken.None));

        Assert.Equal(FinancialProviderErrorCode.Timeout, exception.Code);
        Assert.Equal("provider timeout", exception.Message);
        Assert.Equal(1, directProvider.Calls);
        Assert.Empty(publisher.Requests);
        Assert.NotNull(processor.ProviderRequest);
        Assert.Equal("4", processor.ProviderRequest!.ExternalReference);
        Assert.Equal(1405, processor.ProviderRequest.SourceDateRangeStartJalali is not null
            ? int.Parse(processor.ProviderRequest.SourceDateRangeStartJalali[..4])
            : 0);
        Assert.Equal(DataSyncRunStatus.Failed, processor.FailedRun?.Status);
        Assert.Equal("4", processor.FailedRun?.ExternalReference);
        Assert.Equal(1, processor.FailedRun?.ErrorCount);
        Assert.Equal("provider timeout", processor.FailedRun?.ErrorMessage);
        Assert.NotEqual(DataSyncRunStatus.Completed, processor.FailedRun?.Status);
    }

    private sealed class RecordingPublisher : IDataSyncRequestPublisher
    {
        public List<DataSyncRequest> Requests { get; } = [];

        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDirectProvider : INadpcoMonthlyProductSalesDirectProvider
    {
        public (string CompanyId, int Year, int Month)? Invocation { get; private set; }
        public int? OutputType { get; private set; }

        public ProviderRawPayload Payload { get; } = new(
            Guid.NewGuid(),
            "NoavaranCurrentApi",
            ProviderDataset.MonthlyProductionSales,
            "api/v2/MonthlyActivity/ProductSales?outputTypeId=0",
            "19",
            "{}",
            "checksum",
            Now);

        public Task<ProviderRawPayload> FetchProductSalesAllOutputTypesAsync(
            string externalCompanyId,
            int shamsiYear,
            int shamsiMonth,
            CancellationToken cancellationToken,
            int? monthlyActivityOutputType = null)
        {
            Invocation = (externalCompanyId, shamsiYear, shamsiMonth);
            OutputType = monthlyActivityOutputType;
            return Task.FromResult(Payload);
        }
    }

    private sealed class ThrowingDirectProvider : INadpcoMonthlyProductSalesDirectProvider
    {
        public int Calls { get; private set; }

        public Task<ProviderRawPayload> FetchProductSalesAllOutputTypesAsync(
            string externalCompanyId,
            int shamsiYear,
            int shamsiMonth,
            CancellationToken cancellationToken,
            int? monthlyActivityOutputType = null)
        {
            Calls++;
            throw new FinancialProviderException(FinancialProviderErrorCode.Timeout, "provider timeout");
        }
    }

    private sealed class RecordingProcessor : IFinancialDataSyncProcessor
    {
        public DataSyncRequest? Request { get; private set; }
        public ProviderRawPayload? Payload { get; private set; }
        public DataSyncRequest? ProviderRequest { get; private set; }
        public DataSyncRun? FailedRun { get; private set; }

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

        public async Task<DataSyncProcessingResult> ProcessProviderAsync(
            DataSyncRequest request,
            Func<Task<ProviderRawPayload>> payloadFactory,
            CancellationToken cancellationToken,
            bool rethrowProviderExceptions = false)
        {
            ProviderRequest = request;
            try
            {
                return await ProcessPayloadAsync(request, await payloadFactory(), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                FailedRun = new DataSyncRun(
                    request.RequestId,
                    request.IdempotencyKey,
                    request.Dataset,
                    request.ExternalReference,
                    DataSyncRunStatus.Failed,
                    request.RequestedAt,
                    request.RequestedAt,
                    request.RequestedAt.AddSeconds(1),
                    ProcessedRecords: 0,
                    ErrorCount: 1,
                    ErrorMessage: exception.Message,
                    SourcePayloadChecksum: null,
                    ProviderName: request.ProviderName,
                    Mode: request.Mode,
                    SourceDateRangeStartJalali: request.SourceDateRangeStartJalali,
                    SourceDateRangeEndJalali: request.SourceDateRangeEndJalali);

                if (rethrowProviderExceptions)
                {
                    throw;
                }

                return new DataSyncProcessingResult(FailedRun, AlreadyProcessed: false);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
