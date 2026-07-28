using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Enqueues bounded monthly-activity sync requests for one company across an explicit Shamsi range
/// (spec 057 pipeline). Each month becomes one <see cref="DataSyncRequest"/> with explicit Jalali
/// from/to dates, identical to what the full backfill coordinator enqueues per company-month.
/// Uses a distinct idempotency-key prefix so runs do not interfere with the full backfill state.
/// </summary>
public sealed class SingleCompanyMonthlyIngestionService(
    IDataSyncRequestPublisher publisher,
    IOptions<NadpcoApiProviderOptions> providerOptions,
    TimeProvider timeProvider) : ISingleCompanyMonthlyIngestionService
{
    private const string KeyPrefix = "nadpco-single-monthly";

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
        foreach (var month in months)
        {
            var fromDate = month.FirstDayJalali;
            var toDate = ShamsiMonthCalculator.LastDayJalali(month);
            var key = BuildKey(month, request.ExternalCompanyId);

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
                    SourceDateRangeEndJalali: toDate),
                cancellationToken);
            enqueued++;
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

    private static string BuildKey(ShamsiMonth month, int companyId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{KeyPrefix}-{month.Year:D4}{month.Month:D2}-{companyId}");
}
