using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Handles targeted monthly activity for one company. Range requests use the existing queue; the
/// direct recovery method fetches all ProductSales output types and invokes normalization/persistence inline.
/// </summary>
public sealed class SingleCompanyMonthlyIngestionService(
    IDataSyncRequestPublisher publisher,
    INadpcoMonthlyProductSalesDirectProvider directProvider,
    IFinancialDataSyncProcessor syncProcessor,
    IOptions<NadpcoApiProviderOptions> providerOptions,
    TimeProvider timeProvider) : ISingleCompanyMonthlyIngestionService
{
    private const string KeyPrefix = "nadpco-single-monthly";

    public async Task<DataSyncProcessingResult> ExecuteDirectAsync(
        SingleCompanyMonthlyDirectIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var month = new ShamsiMonth(request.ShamsiYear, request.ShamsiMonth);
        var companyId = request.ExternalCompanyId.ToString(CultureInfo.InvariantCulture);
        var syncRequest = new DataSyncRequest(
            requestId,
            ProviderDataset.MonthlyProductionSales,
            companyId,
            timeProvider.GetUtcNow(),
            IdempotencyKey: string.Create(
                CultureInfo.InvariantCulture,
                $"{KeyPrefix}-direct-{month.Year:D4}{month.Month:D2}-{request.ExternalCompanyId}-{requestId:N}"),
            ProviderName: providerOptions.Value.ProviderName,
            Mode: SourceMode.CurrentIncremental,
            SourceDateRangeStartJalali: month.FirstDayJalali,
            SourceDateRangeEndJalali: ShamsiMonthCalculator.LastDayJalali(month),
            MonthlyActivityOutputType: 0);
        var payload = await directProvider.FetchProductSalesAllOutputTypesAsync(
            companyId,
            request.ShamsiYear,
            request.ShamsiMonth,
            cancellationToken,
            monthlyActivityOutputType: 0);

        var result = await syncProcessor.ProcessPayloadAsync(syncRequest, payload, cancellationToken);

        if (result.Run.Status == DataSyncRunStatus.Completed && result.Run.ErrorCount == 0)
        {
            await EnqueueRemainingOutputTypesAsync(request, month, cancellationToken);
        }

        return result;
    }

    private async Task EnqueueRemainingOutputTypesAsync(
        SingleCompanyMonthlyDirectIngestionRequest request,
        ShamsiMonth month,
        CancellationToken cancellationToken)
    {
        var providerName = providerOptions.Value.ProviderName;
        var externalCompanyId = request.ExternalCompanyId.ToString(CultureInfo.InvariantCulture);
        var fromDate = month.FirstDayJalali;
        var toDate = ShamsiMonthCalculator.LastDayJalali(month);

        foreach (var outputType in Enumerable.Range(1, 4))
        {
            await publisher.PublishAsync(
                new DataSyncRequest(
                    Guid.NewGuid(),
                    ProviderDataset.MonthlyProductionSales,
                    externalCompanyId,
                    timeProvider.GetUtcNow(),
                    IdempotencyKey: string.Create(
                        CultureInfo.InvariantCulture,
                        $"{KeyPrefix}-async-{month.Year:D4}{month.Month:D2}-{request.ExternalCompanyId}-ot{outputType}-{Guid.NewGuid():N}"),
                    ProviderName: providerName,
                    Mode: SourceMode.CurrentIncremental,
                    SourceDateRangeStartJalali: fromDate,
                    SourceDateRangeEndJalali: toDate,
                    MonthlyActivityOutputType: outputType),
                cancellationToken);
        }
    }

    public async Task<SingleCompanyMonthlyIngestionResult> EnqueueAsync(
        SingleCompanyMonthlyIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var providerName = providerOptions.Value.ProviderName;
        var from = new ShamsiMonth(request.FromShamsiYear, (byte)request.FromShamsiMonth);
        var to = new ShamsiMonth(request.ToShamsiYear, (byte)request.ToShamsiMonth);

        // Build ascending month list so logging is readable; order doesn't matter for enqueueing.
        var months = new List<ShamsiMonth>();
        for (var m = from; m <= to; m = m.Month == 12
                 ? new ShamsiMonth(m.Year + 1, 1)
                 : new ShamsiMonth(m.Year, m.Month + 1))
        {
            months.Add(m);
        }

        if (months.Count == 0)
        {
            return new SingleCompanyMonthlyIngestionResult(
                "EmptyRange",
                request.ExternalCompanyId,
                MonthsInRange: 0,
                RequestsEnqueued: 0,
                from.ToString(),
                to.ToString(),
                request.RequestedBy);
        }

        var enqueued = 0;
        for (var outputType = 0; outputType <= 4; outputType++)
        {
            foreach (var month in months)
            {
                var fromDate = month.FirstDayJalali;
                var toDate = ShamsiMonthCalculator.LastDayJalali(month);
                var key = BuildKey(month, request.ExternalCompanyId, outputType);

                await publisher.PublishAsync(
                    new DataSyncRequest(
                        Guid.NewGuid(),
                        ProviderDataset.MonthlyProductionSales,
                        request.ExternalCompanyId.ToString(CultureInfo.InvariantCulture),
                        timeProvider.GetUtcNow(),
                        IdempotencyKey: key,
                        ProviderName: providerName,
                        Mode: SourceMode.CurrentIncremental,
                        SourceDateRangeStartJalali: fromDate,
                        SourceDateRangeEndJalali: toDate,
                        MonthlyActivityOutputType: outputType),
                    cancellationToken);
                enqueued++;
            }
        }

        return new SingleCompanyMonthlyIngestionResult(
            "Enqueued",
            request.ExternalCompanyId,
            months.Count,
            enqueued,
            months[0].ToString(),
            months[^1].ToString(),
            request.RequestedBy);
    }

    private static string BuildKey(ShamsiMonth month, int companyId, int outputType) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{KeyPrefix}-{month.Year:D4}{month.Month:D2}-{companyId}-ot{outputType}");
}
