using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class NormalizedMetricInputReader(
    IEnumerable<INormalizedMetricInputSource> sources) : INormalizedMetricInputReader
{
    private readonly IReadOnlyDictionary<MetricCode, INormalizedMetricInputSource> _sources =
        sources.ToDictionary(source => source.MetricCode);

    public Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        MetricCode metricCode,
        CancellationToken cancellationToken) =>
        _sources.TryGetValue(metricCode, out var source)
            ? source.LoadAsync(externalCompanyId, cancellationToken)
            : throw new KeyNotFoundException($"No normalized input source is registered for metric '{metricCode}'.");
}

/// <summary>
/// Generic input source for any metric code backed by <c>NormalizedFinancialStatementLineItems</c>.
/// Reads line items keyed by <paramref name="metricCode"/> from all providers, provider-agnostically.
/// Replaces/subsumes per-metric source classes such as the legacy <c>NetProfitMetricInputSource</c>.
/// </summary>
public sealed class LineItemMetricInputSource(
    FinancialIngestionDbContext dbContext,
    MetricCode metricCode) : INormalizedMetricInputSource
{
    public MetricCode MetricCode { get; } = metricCode;

    public async Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var observations = await (
                from statement in dbContext.FinancialStatements.AsNoTracking()
                join item in dbContext.FinancialStatementLineItems.AsNoTracking()
                    on statement.Id equals item.FinancialStatementId
                where statement.ExternalCompanyId == externalCompanyId && item.MetricCode == MetricCode.Value
                select new { statement, item.Value })
            .ToListAsync(cancellationToken);

        return observations.Select(observation => NormalizedMetricInputFactory.Create(
            MetricCode,
            FiscalPeriod.Closed(
                Enum.Parse<FiscalPeriodType>(observation.statement.PeriodType),
                observation.statement.PeriodStart,
                observation.statement.PeriodEnd),
            observation.Value,
            observation.statement.ProviderName,
            observation.statement.ExternalStatementId,
            observation.statement.LastSynchronizedAt)).ToArray();
    }
}

// Kept for backward compatibility in tests that construct it directly.
public sealed class NetProfitMetricInputSource(
    FinancialIngestionDbContext dbContext) : INormalizedMetricInputSource
{
    public MetricCode MetricCode { get; } = new("NET_PROFIT");

    public async Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken) =>
        await new LineItemMetricInputSource(dbContext, MetricCode).LoadAsync(externalCompanyId, cancellationToken);
}

/// <summary>
/// Shared per-company-month aggregation over normalized monthly report line items (spec 057).
/// Each <c>MonthlyReport</c> row corresponds to exactly one vendor report type
/// (<c>ProductSales</c> or <c>ServiceSales</c>) — a company publishes one type, not both.
/// One observation is produced per report. The aggregate policy is supplied by the concrete source.
/// </summary>
public abstract class MonthlyReportAggregateInputSource(
    FinancialIngestionDbContext dbContext,
    string metricCode) : INormalizedMetricInputSource
{
    public MetricCode MetricCode { get; } = new(metricCode);

    protected abstract decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems);

    public async Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var reports = await dbContext.MonthlyReports.AsNoTracking()
            .Where(report => report.ExternalCompanyId == externalCompanyId)
            .ToListAsync(cancellationToken);
        if (reports.Count == 0)
        {
            return [];
        }

        var reportIds = reports.Select(report => report.Id).ToList();
        var lineItemsByReport = (await dbContext.MonthlyReportLineItems.AsNoTracking()
                .Where(item => reportIds.Contains(item.MonthlyReportId))
                .ToListAsync(cancellationToken))
            .ToLookup(item => item.MonthlyReportId);

        return reports
            .Select(report =>
            {
                var lineItems = lineItemsByReport[report.Id].ToArray();
                return NormalizedMetricInputFactory.Create(
                    MetricCode,
                    FiscalPeriod.Closed(FiscalPeriodType.Monthly, report.PeriodStart, report.PeriodEnd),
                    Aggregate(lineItems),
                    report.ProviderName,
                    report.ExternalReportId,
                    report.LastSynchronizedAt);
            })
            .ToArray();
    }
}

public sealed class MonthlySalesMetricInputSource(
    FinancialIngestionDbContext dbContext) : MonthlyReportAggregateInputSource(dbContext, "MONTHLY_SALES")
{
    // Pre-057 behavior preserved: the month's sales amount is reliable only when every line item
    // carries a value; a partially-valued report yields null (MissingData) instead of an
    // understated total.
    protected override decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems) =>
        lineItems.Count > 0 && lineItems.All(item => item.SalesAmount is not null)
            ? lineItems.Sum(item => item.SalesAmount!.Value)
            : null;
}

public sealed class MonthlySalesQuantityMetricInputSource(
    FinancialIngestionDbContext dbContext) :
    MonthlyReportAggregateInputSource(dbContext, "MONTHLY_SALES_QUANTITY")
{
    // Sum over lines that report a sales quantity; null when no line does. Lines without a
    // quantity (rare aggregate rows) are excluded rather than nulling the whole month, because
    // quantities are additive only across the lines that actually carry them.
    protected override decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems)
    {
        var values = lineItems.Where(item => item.SalesQuantity is not null).ToArray();
        return values.Length > 0 ? values.Sum(item => item.SalesQuantity!.Value) : null;
    }
}

public sealed class MonthlyProductionQuantityMetricInputSource(
    FinancialIngestionDbContext dbContext) :
    MonthlyReportAggregateInputSource(dbContext, "MONTHLY_PRODUCTION_QUANTITY")
{
    // Service-sales lines never carry production; sum the product lines that do, null when none.
    protected override decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems)
    {
        var values = lineItems.Where(item => item.ProductionQuantity is not null).ToArray();
        return values.Length > 0 ? values.Sum(item => item.ProductionQuantity!.Value) : null;
    }
}

public sealed class MonthlySalesRateMetricInputSource(
    FinancialIngestionDbContext dbContext) :
    MonthlyReportAggregateInputSource(dbContext, "MONTHLY_SALES_RATE")
{
    // Quantity-weighted average rate: Σ sales amount ÷ Σ sales quantity over lines where both are
    // present and quantity is positive (policy "monthly-sales-rate-source-v1"). Null when no
    // eligible line exists — mixed-unit months degrade to a blended rate by design, documented in
    // the calculation policy rather than silently picking one product line.
    protected override decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems)
    {
        var eligible = lineItems
            .Where(item => item.SalesAmount is not null && item.SalesQuantity is > 0)
            .ToArray();
        if (eligible.Length == 0)
        {
            return null;
        }

        var totalQuantity = eligible.Sum(item => item.SalesQuantity!.Value);
        return totalQuantity > 0 ? eligible.Sum(item => item.SalesAmount!.Value) / totalQuantity : null;
    }
}

internal static class NormalizedMetricInputFactory
{
    public static MetricInputObservation Create(
        MetricCode code,
        FiscalPeriod period,
        decimal? value,
        string providerName,
        string documentId,
        DateTimeOffset synchronizedAt)
    {
        var observedAt = new DateTimeOffset(
            period.EndDate!.Value.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        return new MetricInputObservation(
            code,
            new MetricVersion("v1"),
            new CalculationPolicyVersion("normalized-source-v1"),
            period,
            value,
            [new FinancialSourceEvidence(providerName, observedAt, synchronizedAt, documentId)]);
    }
}
