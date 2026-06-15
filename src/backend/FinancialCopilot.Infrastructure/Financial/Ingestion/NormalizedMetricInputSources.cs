using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    MetricCode metricCode,
    ILogger<LineItemMetricInputSource>? logger = null) : INormalizedMetricInputSource
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

        var results = new List<MetricInputObservation>(observations.Count);
        foreach (var observation in observations)
        {
            if (observation.statement.PeriodEnd < observation.statement.PeriodStart)
            {
                logger?.LogWarning(
                    "Skipping statement {StatementId} for company {CompanyId}: PeriodEnd {PeriodEnd} precedes PeriodStart {PeriodStart}",
                    observation.statement.Id, externalCompanyId,
                    observation.statement.PeriodEnd, observation.statement.PeriodStart);
                continue;
            }

            results.Add(NormalizedMetricInputFactory.Create(
                MetricCode,
                FiscalPeriod.Closed(
                    Enum.Parse<FiscalPeriodType>(observation.statement.PeriodType),
                    observation.statement.PeriodStart,
                    observation.statement.PeriodEnd),
                observation.Value,
                observation.statement.ProviderName,
                observation.statement.ExternalStatementId,
                observation.statement.LastSynchronizedAt));
        }
        return results;
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
/// After spec 059, each company-month may have up to 5 rows in <c>MonthlyReports</c> — one per
/// NADPCO outputTypeId (0–4). The <paramref name="outputTypeFilter"/> selects the correct variant
/// so that exactly one observation is produced per period. Null means "accept any OutputType"
/// (used for <c>ServiceSales</c> rows, which have no outputTypeId, and for legacy rows ingested
/// before spec 059).
/// </summary>
public abstract class MonthlyReportAggregateInputSource(
    FinancialIngestionDbContext dbContext,
    string metricCode,
    int? outputTypeFilter) : INormalizedMetricInputSource
{
    public MetricCode MetricCode { get; } = new(metricCode);

    protected abstract decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems);

    public async Task<IReadOnlyCollection<MetricInputObservation>> LoadAsync(
        string externalCompanyId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.MonthlyReports.AsNoTracking()
            .Where(report => report.ExternalCompanyId == externalCompanyId);

        // When a specific output type is requested, include rows that match that type AND legacy
        // rows with null OutputType (ingested before spec 059) so old data remains queryable.
        if (outputTypeFilter.HasValue)
        {
            query = query.Where(report =>
                report.OutputType == outputTypeFilter.Value || report.OutputType == null);
        }

        var reports = await query.ToListAsync(cancellationToken);
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

// OutputType=0 (single month) is the correct filter for all four spec-057 metrics when the
// user asks for "آخرین فروش" / "latest sales". Legacy rows (OutputType=null, ingested before
// spec 059) are also included by the base class so old data remains queryable.
public sealed class MonthlySalesMetricInputSource(
    FinancialIngestionDbContext dbContext) : MonthlyReportAggregateInputSource(
    dbContext, "MONTHLY_SALES",
    outputTypeFilter: (int)MonthlyActivityQueryIntent.SingleMonth)
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
    FinancialIngestionDbContext dbContext) : MonthlyReportAggregateInputSource(
    dbContext, "MONTHLY_SALES_QUANTITY",
    outputTypeFilter: (int)MonthlyActivityQueryIntent.SingleMonth)
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
    FinancialIngestionDbContext dbContext) : MonthlyReportAggregateInputSource(
    dbContext, "MONTHLY_PRODUCTION_QUANTITY",
    outputTypeFilter: (int)MonthlyActivityQueryIntent.SingleMonth)
{
    // Service-sales lines never carry production; sum the product lines that do, null when none.
    protected override decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems)
    {
        var values = lineItems.Where(item => item.ProductionQuantity is not null).ToArray();
        return values.Length > 0 ? values.Sum(item => item.ProductionQuantity!.Value) : null;
    }
}

public sealed class MonthlySalesRateMetricInputSource(
    FinancialIngestionDbContext dbContext) : MonthlyReportAggregateInputSource(
    dbContext, "MONTHLY_SALES_RATE",
    outputTypeFilter: (int)MonthlyActivityQueryIntent.SingleMonth)
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

// Reads the pre-computed 12-month rolling average monthly revenue from the AVG_12M product code
// stored on M0 monthly report line items by CyclicalWavesMonthlyReportNormalizer.
public sealed class MonthlyAvgSaleMetricInputSource(
    FinancialIngestionDbContext dbContext) : MonthlyReportAggregateInputSource(
    dbContext, "AVG_12M_MONTHLY_SALES",
    outputTypeFilter: null)
{
    protected override decimal? Aggregate(IReadOnlyList<NormalizedMonthlyReportLineItemRow> lineItems)
    {
        var item = lineItems.FirstOrDefault(i => i.ProductCode == "AVG_12M");
        return item?.SalesAmount;
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
